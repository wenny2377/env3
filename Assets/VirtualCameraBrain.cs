using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class VirtualCameraBrain : MonoBehaviour
{
    [Header("Flask Endpoint")]
    public string predictUrl = "http://127.0.0.1:5000/predict";

    [Header("Multi-Angle Capture Count")]
    [Range(1, 4)]
    public int topN = 2;

    [Header("Capture Resolution (Square)")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

    // ── Virtual day tracking ─────────────────────────────────────
    static System.DateTime? experimentBaseDate = null;

    static string VirtualDayToDateString(int virtualDay)
    {
        if (experimentBaseDate == null)
            experimentBaseDate = System.DateTime.Today;
        return experimentBaseDate.Value
            .AddDays(virtualDay - 1)
            .ToString("yyyy-MM-dd");
    }

    public static void ResetBaseDate()
    {
        experimentBaseDate = null;
        Debug.Log("[VCB] Base date reset.");
    }

    float virtualHour = -1f;
    public void SetVirtualHour(float hour) => virtualHour = hour;

    void Start()
    {
        Debug.Log(
            $"[VCB] Initialized | topN={topN} | " +
            $"resolution={renderWidth}x{renderHeight} | " +
            $"mode=OffScreen per-node Camera");
    }

    // ── Main capture entry point ─────────────────────────────────
    public IEnumerator ExecuteMultiCapture(
        UserEntity       user,
        List<CameraNode> sortedNodes,
        string           activity)
    {
        Debug.Log(
            $"[VCB] ExecuteMultiCapture | user={user.userID} | " +
            $"activity={activity} | nodes={sortedNodes.Count}");

        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VCB] sortedNodes is empty");
            yield break;
        }

        int captureCount = Mathf.Min(topN, sortedNodes.Count);

        var imageList  = new List<string>();
        var nodeNames  = new List<string>();
        var nodeScores = new List<float>();

        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node = sortedNodes[i];

            // ── Find the Camera child component ──────────────────
            // Camera is a disabled child of CameraNode
            Camera nodeCam =
                node.GetComponentInChildren<Camera>(
                    includeInactive: true);

            if (nodeCam == null)
            {
                Debug.LogWarning(
                    $"[VCB] No Camera found under node " +
                    $"'{node.nodeName}'. " +
                    $"Add a disabled Camera child to this CameraNode.");
                continue;
            }

            // ── Sync FOV from CameraNode setting ─────────────────
            nodeCam.fieldOfView = node.fieldOfView;

            // ── Off-screen render ─────────────────────────────────
            RenderTexture rt = new RenderTexture(
                renderWidth, renderHeight, 24);

            nodeCam.targetTexture = rt;
            nodeCam.enabled       = true;
            nodeCam.Render();
            nodeCam.enabled       = false;
            nodeCam.targetTexture = null;

            // ── Read pixels ───────────────────────────────────────
            RenderTexture.active = rt;
            var tex = new Texture2D(
                renderWidth, renderHeight,
                TextureFormat.RGB24, false);
            tex.ReadPixels(
                new Rect(0, 0, renderWidth, renderHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            imageList.Add(
                System.Convert.ToBase64String(tex.EncodeToPNG()));
            nodeNames.Add(node.nodeName);
            nodeScores.Add(node.lastScore);

            Destroy(rt);
            Destroy(tex);

            Debug.Log(
                $"[VCB] [{i + 1}/{captureCount}] " +
                $"node={node.nodeName} " +
                $"score={node.lastScore:F2} " +
                $"fov={node.fieldOfView} " +
                $"(off-screen)");

            // One frame between captures
            yield return null;
        }

        if (imageList.Count == 0)
        {
            Debug.LogWarning(
                $"[VCB] No images captured for " +
                $"{user.userID} | {activity}. " +
                $"Check that each CameraNode has a " +
                $"disabled Camera child.");
            yield break;
        }

        // POST is fire-and-forget after capture finishes
        StartCoroutine(PostMultiImage(
            user, activity, imageList, nodeNames, nodeScores));
    }

    public IEnumerator ExecuteCaptureSequence(
        UserEntity user, CameraNode camNode, string activity)
    {
        yield return StartCoroutine(
            ExecuteMultiCapture(
                user,
                new List<CameraNode> { camNode },
                activity));
    }

    // ── POST to Flask /predict ───────────────────────────────────
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

        // Extract room from first node name
        // e.g. "Kitchen_Cam1" → "Kitchen"
        string roomName = "";
        if (nodeNames.Count > 0)
        {
            int camIdx = nodeNames[0].LastIndexOf("_Cam");
            roomName = camIdx > 0
                ? nodeNames[0].Substring(0, camIdx)
                : nodeNames[0];
        }

        string virtualDayField = "";
        if (ExperimentRunner.UseVirtualDay)
        {
            string dateStr = VirtualDayToDateString(
                ExperimentRunner.CurrentVirtualDay);
            virtualDayField =
                $"\"virtual_day\":\"{dateStr}\",";
        }

        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"virtual_hour\":"
            + $"{hour.ToString("F1", InvCulture)},"
            + virtualDayField
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)}"
            + "}";

        using var req = new UnityWebRequest(predictUrl, "POST");
        byte[] body =
            System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 30;

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            string dayLog = ExperimentRunner.UseVirtualDay
                ? $" day={VirtualDayToDateString(ExperimentRunner.CurrentVirtualDay)}"
                : "";
            Debug.Log(
                $"[VCB] POST ok | {user.userID} | " +
                $"{activity} | {imageList.Count} img | " +
                $"hour={hour}{dayLog}");
        }
        else
        {
            Debug.LogWarning(
                $"[VCB] POST failed: {req.error} | " +
                $"{user.userID} | {activity}");
        }
    }

    // ── String helpers ───────────────────────────────────────────
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