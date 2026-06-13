using System;
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
    public int   burstCount    = 2;
    public float burstInterval = 0.3f;

    [Header("Capture Resolution (Square)")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

    bool _tvOn = false;
    public void SetTVState(bool isOn) => _tvOn = isOn;
    public void SetVirtualHour(float hour) { }

    void Start()
    {
        Debug.Log($"[VCB] topN={topN} burst={burstCount}x{burstInterval}s {renderWidth}x{renderHeight}");
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

        string tCapture    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");
        int    captureCount = Mathf.Min(topN, sortedNodes.Count);

        var imageList  = new List<string>();
        var nodeNames  = new List<string>();
        var nodeScores = new List<float>();

        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node   = sortedNodes[i];
            Camera     nodeCam = node.GetComponentInChildren<Camera>(includeInactive: true);

            if (nodeCam == null)
            {
                Debug.LogWarning($"[VCB] No Camera under '{node.nodeName}'.");
                continue;
            }

            nodeCam.fieldOfView = node.fieldOfView;

            for (int b = 0; b < burstCount; b++)
            {
                if (b > 0) yield return new WaitForSeconds(burstInterval);

                RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24);
                nodeCam.targetTexture = rt;
                nodeCam.enabled       = true;
                nodeCam.Render();
                nodeCam.enabled       = false;
                nodeCam.targetTexture = null;

                RenderTexture.active = rt;
                var tex = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                imageList.Add(Convert.ToBase64String(tex.EncodeToPNG()));
                nodeNames.Add($"{node.nodeName}_b{b}");
                nodeScores.Add(node.lastScore);

                Destroy(rt);
                Destroy(tex);
            }
        }

        if (imageList.Count == 0)
        {
            Debug.LogWarning($"[VCB] No images captured for {user.userID} | {activity}.");
            yield break;
        }

        yield return StartCoroutine(PostPredict(user, activity, imageList, nodeNames, nodeScores, tCapture));
    }

    public IEnumerator ExecuteCaptureSequence(
        UserEntity user, CameraNode camNode, string activity)
    {
        yield return StartCoroutine(
            ExecuteMultiCapture(user, new List<CameraNode> { camNode }, activity));
    }

    IEnumerator PostPredict(
        UserEntity   user,
        string       activity,
        List<string> imageList,
        List<string> nodeNames,
        List<float>  nodeScores,
        string       tCapture)
    {
        string roomName = "";
        if (nodeNames.Count > 0)
        {
            string firstName = nodeNames[0].Split('_')[0] + "_" + nodeNames[0].Split('_')[1];
            int    camIdx    = firstName.LastIndexOf("_Cam");
            roomName = camIdx > 0 ? firstName.Substring(0, camIdx) : firstName;
        }

        Vector3 pos = user.transform.position;
        Vector3 fwd = user.transform.forward;

        var    sk      = user.GetComponent<SkeletonHelper>();
        string skelJson = sk != null ? sk.ToJsonFragment() : "";

        if (sk != null)
            Debug.Log($"[SKEL] {user.userID} | {activity} | pitch={sk.HeadPitch():F1} wrist_h={sk.NormalizedWristHeight():F3}");
        else
            Debug.LogWarning($"[SKEL] {user.userID} | {activity} | SkeletonHelper NOT FOUND");

        string posJson = $"\"user_pos\":{{\"x\":{pos.x.ToString("F3", Inv)},\"y\":{pos.y.ToString("F3", Inv)},\"z\":{pos.z.ToString("F3", Inv)}}}";
        string fwdJson = $"\"user_forward\":{{\"x\":{fwd.x.ToString("F3", Inv)},\"y\":{fwd.y.ToString("F3", Inv)},\"z\":{fwd.z.ToString("F3", Inv)}}}";

        string expModeField = !string.IsNullOrEmpty(ExperimentRunner.CurrentExperimentMode)
            ? $"\"experiment_mode\":\"{Esc(ExperimentRunner.CurrentExperimentMode)}\"," : "";

        string virtualTimeFields =
            $"\"virtual_hour\":{ExperimentRunner.CurrentVirtualHour.ToString("F1", Inv)},"
          + $"\"virtual_day\":{ExperimentRunner.CurrentVirtualDay},"
          + $"\"time_slot\":\"{Esc(ExperimentRunner.CurrentTimeSlot)}\",";

        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"t_capture\":\"{tCapture}\","
            + virtualTimeFields
            + expModeField
            + $"\"tv_on\":{(_tvOn ? "true" : "false")},"
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
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[VCB] ok | {user.userID} | {activity} | day={ExperimentRunner.CurrentVirtualDay} slot={ExperimentRunner.CurrentTimeSlot} tv={_tvOn}");
        else
            Debug.LogWarning($"[VCB] failed: {req.error} | {user.userID} | {activity}");
    }

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
        foreach (var f in list) parts.Add(f.ToString("F4", Inv));
        return "[" + string.Join(",", parts) + "]";
    }
}