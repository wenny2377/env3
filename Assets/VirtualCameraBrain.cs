using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class VirtualCameraBrain : MonoBehaviour
{
    [Header("Main Camera (Required)")]
    [Tooltip("The GameObject with a Camera component in the scene\nCameraNodes are virtual scoring nodes and do not need a Camera component")]
    public Camera mainCamera;

    [Header("Flask Endpoint")]
    public string predictUrl = "http://127.0.0.1:5000/predict";

    [Header("Multi-Angle Capture Count")]
    [Tooltip("How many virtual angles to capture per activity\n1 = single shot (legacy / baseline)\n2 = recommended (best + second-best, ~67ms overhead)\n3-4 = more complete but requires more frame time")]
    [Range(1, 4)]
    public int topN = 2;

    [Header("Frames to Wait Before Each Capture")]
    [Tooltip("Frames needed for the render pipeline to refresh after teleporting the camera\nRecommended: 1-2 frames (~16ms each at 60fps)\nToo few: captures before the new position is rendered\nToo many: slows down the capture sequence")]
    [Range(1, 4)]
    public int captureWaitFrames = 2;

    [Header("Capture Resolution (Square)")]
    [Tooltip("Recommended 512x512 — sufficient for VLM, ~200-400KB Base64 per image")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

    [Header("Restore Camera After Capture")]
    [Tooltip("Enabled: move camera back to its Play-start position after all captures\nDisabled: camera stays at the last node position")]
    public bool restoreCamera = true;

    float      virtualHour   = -1f;
    Vector3    originalPos;
    Quaternion originalRot;
    bool       originalSaved = false;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[VirtualCameraBrain] mainCamera not found — " +
                           "drag the scene's Camera GameObject into the Inspector");
            return;
        }

        originalPos   = mainCamera.transform.position;
        originalRot   = mainCamera.transform.rotation;
        originalSaved = true;

        Debug.Log($"[VirtualCameraBrain] Initialized | topN={topN} | " +
                  $"captureWaitFrames={captureWaitFrames} | resolution={renderWidth}x{renderHeight}");
    }

    public void SetVirtualHour(float hour) => virtualHour = hour;

    public IEnumerator ExecuteMultiCapture(
        UserEntity       user,
        List<CameraNode> sortedNodes,
        string           activity)
        
    {
        Debug.Log($"[VCB] ExecuteMultiCapture called | user={user.userID} | activity={activity} | nodes={sortedNodes.Count}");
        if (mainCamera == null)
        {
            Debug.LogError("[VirtualCameraBrain] mainCamera is null, cannot capture");
            yield break;
        }

        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VirtualCameraBrain] sortedNodes is empty, skipping");
            yield break;
        }

        int captureCount = Mathf.Min(topN, sortedNodes.Count);

        var imageList  = new List<string>();
        var nodeNames  = new List<string>();
        var nodeScores = new List<float>();

        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node = sortedNodes[i];

            mainCamera.transform.position = node.transform.position;
            mainCamera.transform.rotation = node.transform.rotation;

            for (int f = 0; f < captureWaitFrames; f++)
                yield return new WaitForEndOfFrame();

            RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24);
            mainCamera.targetTexture = rt;
            mainCamera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            imageList.Add(System.Convert.ToBase64String(tex.EncodeToPNG()));
            nodeNames.Add(node.nodeName);
            nodeScores.Add(node.lastScore);

            mainCamera.targetTexture = null;
            Destroy(rt);
            Destroy(tex);

            Debug.Log($"[VirtualCameraBrain] [{i + 1}/{captureCount}] " +
                      $"node={node.nodeName} score={node.lastScore:F2}");
        }

        if (restoreCamera && originalSaved)
        {
            mainCamera.transform.position = originalPos;
            mainCamera.transform.rotation = originalRot;
        }

        StartCoroutine(PostMultiImage(user, activity, imageList, nodeNames, nodeScores));
    }

    public IEnumerator ExecuteCaptureSequence(
        UserEntity user, CameraNode camNode, string activity)
    {
        yield return StartCoroutine(
            ExecuteMultiCapture(user, new List<CameraNode> { camNode }, activity));
            
    }

    IEnumerator PostMultiImage(
        UserEntity   user,
        string       activity,
        List<string> imageList,
        List<string> nodeNames,
        List<float>  nodeScores)
    {
        float hour = virtualHour >= 0f
            ? virtualHour
            : (float)System.DateTime.Now.Hour;

        string roomName = "";
        if (nodeNames.Count > 0)
        {
            int camIdx = nodeNames[0].LastIndexOf("_Cam");
            roomName = camIdx > 0 ? nodeNames[0].Substring(0, camIdx) : nodeNames[0];
        }

        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"virtual_hour\":{hour.ToString("F1", InvCulture)},"
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)}"
            + "}";

        using var req = new UnityWebRequest(predictUrl, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 30;

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[VirtualCameraBrain] POST succeeded | {user.userID} | " +
                      $"{activity} | {imageList.Count} image(s) | hour={hour}");
        else
            Debug.LogWarning($"[VirtualCameraBrain] POST failed: {req.error}\n" +
                             $"URL={predictUrl} — is Flask running?");
    }

    static readonly System.Globalization.CultureInfo InvCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string StrArrayJson(List<string> list)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Esc(list[i])).Append('"');
        }
        return sb.Append(']').ToString();
    }

    static string FloatArrayJson(List<float> list)
    {
        var parts = new List<string>();
        foreach (var f in list)
            parts.Add(f.ToString("F4", InvCulture));
        return "[" + string.Join(",", parts) + "]";
    }
}
