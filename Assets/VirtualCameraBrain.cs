using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class VirtualCameraBrain : MonoBehaviour
{
    [Header("Main Camera (Required)")]
    public Camera mainCamera;

    [Header("Flask Endpoint")]
    public string predictUrl = "http://127.0.0.1:5000/predict";

    [Header("Multi-Angle Capture Count")]
    [Range(1, 4)]
    public int topN = 2;

    [Header("Frames to Wait Before Each Capture")]
    [Range(1, 4)]
    public int captureWaitFrames = 2;

    [Header("Capture Resolution (Square)")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

    [Header("Restore Camera After Capture")]
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
            Debug.LogError("[VirtualCameraBrain] mainCamera not found.");
            return;
        }

        originalPos   = mainCamera.transform.position;
        originalRot   = mainCamera.transform.rotation;
        originalSaved = true;

        Debug.Log($"[VirtualCameraBrain] Initialized | topN={topN} | " +
                  $"captureWaitFrames={captureWaitFrames} | " +
                  $"resolution={renderWidth}x{renderHeight}");
    }

    public void SetVirtualHour(float hour) => virtualHour = hour;

    public IEnumerator ExecuteMultiCapture(
        UserEntity       user,
        List<CameraNode> sortedNodes,
        string           activity)
    {
        Debug.Log($"[VCB] ExecuteMultiCapture | user={user.userID} | " +
                  $"activity={activity} | nodes={sortedNodes.Count}");

        if (mainCamera == null)
        {
            Debug.LogError("[VirtualCameraBrain] mainCamera is null");
            yield break;
        }

        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VirtualCameraBrain] sortedNodes is empty");
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
            roomName = camIdx > 0
                ? nodeNames[0].Substring(0, camIdx)
                : nodeNames[0];
        }

        // Read virtual day from ExperimentRunner static vars
        string virtualDayField = "";
        if (ExperimentRunner.UseVirtualDay)
        {
            virtualDayField =
                $"\"virtual_day\":{ExperimentRunner.CurrentVirtualDay},";
        }

        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"virtual_hour\":{hour.ToString("F1", InvCulture)},"
            + virtualDayField
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
        {
            string dayLog = ExperimentRunner.UseVirtualDay
                ? $" day={ExperimentRunner.CurrentVirtualDay}"
                : "";
            Debug.Log($"[VirtualCameraBrain] POST ok | {user.userID} | " +
                      $"{activity} | {imageList.Count} img | " +
                      $"hour={hour}{dayLog}");
        }
        else
        {
            Debug.LogWarning($"[VirtualCameraBrain] POST failed: {req.error}");
        }
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
