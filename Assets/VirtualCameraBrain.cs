using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class VirtualCameraBrain : MonoBehaviour
{
    const string PREDICT_URL    = "http://127.0.0.1:5000/predict";
    const int    TOP_N          = 2;
    const int    BURST_COUNT    = 2;
    const float  BURST_INTERVAL = 0.3f;
    const int    RENDER_WIDTH   = 512;
    const int    RENDER_HEIGHT  = 512;

    bool _tvOn = false;
    public void SetTVState(bool isOn) => _tvOn = isOn;

    public IEnumerator ExecuteMultiCapture(
        UserEntity user, List<CameraNode> sortedNodes, string activity)
    {
        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VCB] sortedNodes is empty");
            yield break;
        }

        string snapshotExpMode = ExperimentRunner.CurrentExperimentMode;
        string tCapture        = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");
        int    captureCount    = Mathf.Min(TOP_N, sortedNodes.Count);

        var imageList  = new List<string>();
        var nodeNames  = new List<string>();
        var nodeScores = new List<float>();

        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node    = sortedNodes[i];
            Camera     nodeCam = node.GetComponentInChildren<Camera>(includeInactive: true);
            if (nodeCam == null)
            {
                Debug.LogWarning($"[VCB] No Camera under '{node.nodeName}'.");
                continue;
            }

            nodeCam.fieldOfView = node.fieldOfView;

            for (int b = 0; b < BURST_COUNT; b++)
            {
                if (b > 0) yield return new WaitForSeconds(BURST_INTERVAL);

                RenderTexture rt = new RenderTexture(RENDER_WIDTH, RENDER_HEIGHT, 24);
                nodeCam.targetTexture = rt;
                nodeCam.enabled       = true;
                nodeCam.Render();
                nodeCam.enabled       = false;
                nodeCam.targetTexture = null;

                RenderTexture.active = rt;
                var tex = new Texture2D(RENDER_WIDTH, RENDER_HEIGHT, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, RENDER_WIDTH, RENDER_HEIGHT), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                imageList.Add(Convert.ToBase64String(tex.EncodeToPNG()));
                nodeNames.Add($"{node.nodeName}_b{b}");
                nodeScores.Add(node.lastScore);

                rt.Release();
                Destroy(tex);
            }
        }

        if (imageList.Count == 0)
        {
            Debug.LogWarning($"[VCB] No images for {user.userID} | {activity}.");
            yield break;
        }

        yield return StartCoroutine(
            PostPredict(user, activity, imageList, nodeNames, nodeScores,
                        tCapture, snapshotExpMode));
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
        string       tCapture,
        string       snapshotExpMode = "")
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

        var    sk       = user.GetComponent<SkeletonHelper>();
        string skelJson = sk != null ? sk.ToJsonFragment() : "";

        if (sk != null)
            Debug.Log($"[SKEL] {user.userID} | {activity} | "
                    + $"axis={sk.BodyAxisAngle():F1} knee={sk.KneeHipRatio():F2} "
                    + $"pitch={sk.HeadPitch():F1} r_h2h={sk.HandToHead(false):F3}");
        else
            Debug.LogWarning($"[SKEL] {user.userID} | SkeletonHelper NOT FOUND");

        string expModeField = !string.IsNullOrEmpty(ExperimentRunner.CurrentExperimentMode)
            ? $"\"experiment_mode\":\"{JsonUtil.Esc(ExperimentRunner.CurrentExperimentMode)}\"," : "";

        string systemModeField =
            $"\"system_mode\":\"{JsonUtil.Esc(ExperimentRunner.CurrentSystemMode)}\",";

        string virtualTimeFields =
              $"\"virtual_hour\":{ExperimentRunner.CurrentVirtualHour.ToString("F1", JsonUtil.Inv)},"
            + $"\"virtual_day\":{ExperimentRunner.CurrentVirtualDay},"
            + $"\"time_slot\":\"{JsonUtil.Esc(ExperimentRunner.CurrentTimeSlot)}\",";

        string posJson = $"\"user_pos\":{{\"x\":{pos.x.ToString("F3", JsonUtil.Inv)},"
                       + $"\"y\":{pos.y.ToString("F3", JsonUtil.Inv)},"
                       + $"\"z\":{pos.z.ToString("F3", JsonUtil.Inv)}}}";
        string fwdJson = $"\"user_forward\":{{\"x\":{fwd.x.ToString("F3", JsonUtil.Inv)},"
                       + $"\"y\":{fwd.y.ToString("F3", JsonUtil.Inv)},"
                       + $"\"z\":{fwd.z.ToString("F3", JsonUtil.Inv)}}}";

        string json = "{"
            + $"\"userID\":\"{JsonUtil.Esc(user.userID)}\","
            + $"\"activity\":\"{JsonUtil.Esc(activity)}\","
            + $"\"room_name\":\"{JsonUtil.Esc(roomName)}\","
            + $"\"t_capture\":\"{tCapture}\","
            + virtualTimeFields
            + expModeField
            + systemModeField
            + $"\"tv_on\":{(_tvOn ? "true" : "false")},"
            + skelJson
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)},"
            + posJson + ","
            + fwdJson
            + "}";

        using var req = new UnityWebRequest(PREDICT_URL, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 120;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[VCB] ok | {user.userID} | {activity} | "
                    + $"day={ExperimentRunner.CurrentVirtualDay} "
                    + $"slot={ExperimentRunner.CurrentTimeSlot} "
                    + $"tv={_tvOn} system={ExperimentRunner.CurrentSystemMode}");
        else
            Debug.LogWarning($"[VCB] failed: {req.error} | {user.userID} | {activity}");
    }

    static string StrArrayJson(List<string> list)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(JsonUtil.Esc(list[i])).Append('"');
        }
        return sb.Append(']').ToString();
    }

    static string FloatArrayJson(List<float> list)
    {
        var parts = new List<string>();
        foreach (var f in list) parts.Add(f.ToString("F4", JsonUtil.Inv));
        return "[" + string.Join(",", parts) + "]";
    }
}