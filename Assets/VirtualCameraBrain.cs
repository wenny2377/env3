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

    [Header("Burst Capture Settings")]
    [Range(1, 3)]
    public int  burstCount    = 2;
    public float burstInterval = 0.3f;

    [Header("Capture Resolution (Square)")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

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
            $"[VCB] Initialized | topN={topN} | burst={burstCount}x{burstInterval}s | " +
            $"resolution={renderWidth}x{renderHeight}");
    }

    public IEnumerator ExecuteMultiCapture(
        UserEntity       user,
        List<CameraNode> sortedNodes,
        string           activity)
    {
        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VCB] sortedNodes is empty");
            yield break;
        }

        string tCapture = System.DateTime.UtcNow
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        int captureCount = Mathf.Min(topN, sortedNodes.Count);

        var imageList  = new List<string>();
        var nodeNames  = new List<string>();
        var nodeScores = new List<float>();

        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node   = sortedNodes[i];
            Camera     nodeCam =
                node.GetComponentInChildren<Camera>(includeInactive: true);

            if (nodeCam == null)
            {
                Debug.LogWarning($"[VCB] No Camera under '{node.nodeName}'.");
                continue;
            }

            nodeCam.fieldOfView = node.fieldOfView;

            for (int b = 0; b < burstCount; b++)
            {
                if (b > 0)
                    yield return new WaitForSeconds(burstInterval);

                RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24);
                nodeCam.targetTexture = rt;
                nodeCam.enabled       = true;
                nodeCam.Render();
                nodeCam.enabled       = false;
                nodeCam.targetTexture = null;

                RenderTexture.active = rt;
                var tex = new Texture2D(
                    renderWidth, renderHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                imageList.Add(System.Convert.ToBase64String(tex.EncodeToPNG()));
                nodeNames.Add($"{node.nodeName}_b{b}");
                nodeScores.Add(node.lastScore);

                Destroy(rt);
                Destroy(tex);
            }

            Debug.Log(
                $"[VCB] node={node.nodeName} burst={burstCount} score={node.lastScore:F2}");
        }

        if (imageList.Count == 0)
        {
            Debug.LogWarning(
                $"[VCB] No images captured for {user.userID} | {activity}.");
            yield break;
        }

        yield return StartCoroutine(PostMultiImage(
            user, activity, imageList, nodeNames, nodeScores, tCapture));
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
        List<float>  nodeScores,
        string       tCapture)
    {
        float hour = virtualHour >= 0f
            ? virtualHour
            : (float)System.DateTime.Now.Hour;

        string roomName = "";
        if (nodeNames.Count > 0)
        {
            string firstName = nodeNames[0].Split('_')[0] + "_" +
                               nodeNames[0].Split('_')[1];
            int camIdx = firstName.LastIndexOf("_Cam");
            roomName = camIdx > 0
                ? firstName.Substring(0, camIdx)
                : firstName;
        }

        string virtualDayField = "";
        if (ExperimentRunner.UseVirtualDay)
        {
            string dateStr = VirtualDayToDateString(
                ExperimentRunner.CurrentVirtualDay);
            virtualDayField = $"\"virtual_day\":\"{dateStr}\",";
        }

        Vector3 pos = user.transform.position;
        Vector3 fwd = user.transform.forward;

        var sk = user.GetComponent<SkeletonHelper>();
        string skelJson = sk != null ? sk.ToJsonFragment() : "";

        if (sk != null)
        {
            Debug.Log(
                $"[SKEL] {user.userID} | {activity} | " +
                $"hip={sk.NormalizedHipHeight():F3} | " +
                $"pitch={sk.HeadPitch():F1} | " +
                $"spine={sk.SpineAngle():F1} | " +
                $"h2h={sk.NormalizedHandToHead():F3} | " +
                $"arm={sk.RightArmElevation():F1} | " +
                $"wrist={sk.NormalizedWristHeight():F3}");
        }
        else
        {
            Debug.LogWarning($"[SKEL] {user.userID} | {activity} | SkeletonHelper NOT FOUND");
        }

        string posJson =
            $"\"user_pos\":{{" +
            $"\"x\":{pos.x.ToString("F3", InvCulture)}," +
            $"\"y\":{pos.y.ToString("F3", InvCulture)}," +
            $"\"z\":{pos.z.ToString("F3", InvCulture)}}}";

        string fwdJson =
            $"\"user_forward\":{{" +
            $"\"x\":{fwd.x.ToString("F3", InvCulture)}," +
            $"\"y\":{fwd.y.ToString("F3", InvCulture)}," +
            $"\"z\":{fwd.z.ToString("F3", InvCulture)}}}";

        string experimentMode = ExperimentRunner.CurrentExperimentMode;
        string expModeField   = !string.IsNullOrEmpty(experimentMode)
            ? $"\"experiment_mode\":\"{Esc(experimentMode)}\"," : "";

        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"virtual_hour\":{hour.ToString("F1", InvCulture)},"
            + $"\"t_capture\":\"{tCapture}\","
            + virtualDayField
            + expModeField
            + skelJson
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)},"
            + posJson + ","
            + fwdJson
            + "}";

        using var req = new UnityWebRequest(predictUrl, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 120;
        Debug.Log($"[JSON] {json.Substring(0, Mathf.Min(300, json.Length))}");
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            string dayLog = ExperimentRunner.UseVirtualDay
                ? $" day={VirtualDayToDateString(ExperimentRunner.CurrentVirtualDay)}"
                : "";
            string skelLog = sk != null
                ? $" head={sk.HeadPitch():F1}°"
                : " [NO SKELETON]";
            Debug.Log(
                $"[VCB] POST ok | {user.userID} | {activity} | " +
                $"{imageList.Count} img | hour={hour}{dayLog}{skelLog} | t={tCapture}");
        }
        else
        {
            Debug.LogWarning(
                $"[VCB] POST failed: {req.error} | {user.userID} | {activity}");
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