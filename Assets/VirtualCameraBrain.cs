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
    const float  SOM_RADIUS        = 3.0f;
    const float  SOM_MIN_DEPTH     = 0.3f;
    const int    SOM_MAX_OBJS      = 6;
    const float  SOM_FACING_WEIGHT = 2.0f;

    bool _tvOn = false;
    public void SetTVState(bool isOn) => _tvOn = isOn;

    // ── SoM: project dynamic objects onto camera image plane ─────────────────

    struct SomPoint2D
    {
        public string label;
        public int    u;
        public int    v;
        public bool   isHeld;
    }

    bool _TryProjectToScreen(Camera cam, Vector3 worldPos, int imgW, int imgH,
                              out int u, out int v)
    {
        u = v = 0;
        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z < SOM_MIN_DEPTH) return false;

        u = Mathf.RoundToInt(sp.x / Screen.width  * imgW);
        v = Mathf.RoundToInt((1f - sp.y / Screen.height) * imgH);
        if (u < 0 || u >= imgW || v < 0 || v >= imgH) return false;
        return true;
    }

    List<SomPoint2D> _ProjectSomObjects(
        Camera cam, UserEntity user,
        DynamicSyncManager dsm,
        int imgW, int imgH)
    {
        var result = new List<SomPoint2D>();
        if (dsm == null || dsm.dynamicObjects == null) return result;

        Vector3 uPos = user.transform.position;
        Vector3 uFwd = user.transform.forward;
        var seen     = new HashSet<string>();

        var heldCandidates    = new List<(GameObject obj, string label)>();
        var nonHeldCandidates = new List<(GameObject obj, string label, float score)>();

        foreach (var obj in dsm.dynamicObjects)
        {
            if (obj == null) continue;
            string label = obj.name.ToLower().Trim();
            if (string.IsNullOrEmpty(label) || seen.Contains(label)) continue;

            bool isHeld = !obj.activeInHierarchy;

            if (isHeld)
            {
                heldCandidates.Add((obj, label));
                continue;
            }

            Vector3 wPos = obj.transform.position;
            float   dist = Vector3.Distance(uPos, wPos);
            if (dist > SOM_RADIUS) continue;

            Vector3 toObj      = (wPos - uPos).normalized;
            float   facingDot  = Vector3.Dot(uFwd, toObj);
            float   distScore  = Mathf.Max(0f, (SOM_RADIUS - dist) / SOM_RADIUS);
            float   facingScore = Mathf.Max(0f, facingDot);

            float score = distScore + facingScore * SOM_FACING_WEIGHT;

            nonHeldCandidates.Add((obj, label, score));
        }

        foreach (var (obj, label) in heldCandidates)
        {
            if (seen.Contains(label)) continue;
            seen.Add(label);
            if (!_TryProjectToScreen(cam, uPos + Vector3.up * 1.0f, imgW, imgH, out int u, out int v))
                continue;
            result.Add(new SomPoint2D { label = label, u = u, v = v, isHeld = true });
            if (result.Count >= SOM_MAX_OBJS) return result;
        }

        nonHeldCandidates.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (var (obj, label, score) in nonHeldCandidates)
        {
            if (result.Count >= SOM_MAX_OBJS) break;
            if (seen.Contains(label)) continue;
            seen.Add(label);
            if (!_TryProjectToScreen(cam, obj.transform.position, imgW, imgH, out int u, out int v))
                continue;
            result.Add(new SomPoint2D { label = label, u = u, v = v, isHeld = false });
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────

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
        var somPoints  = new List<SomPoint2D>();

        var dsm = FindObjectOfType<DynamicSyncManager>();

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

                if (b == 0)
                {
                    var projected = _ProjectSomObjects(
                        nodeCam, user, dsm, RENDER_WIDTH, RENDER_HEIGHT);
                    foreach (var pt in projected)
                    {
                        bool alreadyHave = false;
                        foreach (var existing in somPoints)
                            if (existing.label == pt.label) { alreadyHave = true; break; }
                        if (!alreadyHave) somPoints.Add(pt);
                    }
                }

                Destroy(rt);
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
                        somPoints, tCapture, snapshotExpMode));
    }

    public IEnumerator ExecuteCaptureSequence(
        UserEntity user, CameraNode camNode, string activity)
    {
        yield return StartCoroutine(
            ExecuteMultiCapture(user, new List<CameraNode> { camNode }, activity));
    }

    IEnumerator PostPredict(
        UserEntity       user,
        string           activity,
        List<string>     imageList,
        List<string>     nodeNames,
        List<float>      nodeScores,
        List<SomPoint2D> somPoints,
        string           tCapture,
        string           snapshotExpMode = "")
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

        string somJson = _BuildSomJson(somPoints);

        string json = "{"
            + $"\"userID\":\"{JsonUtil.Esc(user.userID)}\","
            + $"\"activity\":\"{JsonUtil.Esc(activity)}\","
            + $"\"room_name\":\"{JsonUtil.Esc(roomName)}\","
            + $"\"t_capture\":\"{tCapture}\","
            + virtualTimeFields
            + expModeField
            + $"\"tv_on\":{(_tvOn ? "true" : "false")},"
            + skelJson
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)},"
            + $"\"objects_2d\":{somJson},"
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
                    + $"tv={_tvOn} som={somPoints.Count}obj");
        else
            Debug.LogWarning($"[VCB] failed: {req.error} | {user.userID} | {activity}");
    }

    static string _BuildSomJson(List<SomPoint2D> pts)
    {
        if (pts == null || pts.Count == 0) return "[]";
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = pts[i];
            sb.Append('{');
            sb.Append($"\"label\":\"{JsonUtil.Esc(p.label)}\",");
            sb.Append($"\"u\":{p.u},");
            sb.Append($"\"v\":{p.v},");
            sb.Append($"\"held\":{(p.isHeld ? "true" : "false")}");
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
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
