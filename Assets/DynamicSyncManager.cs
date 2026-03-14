using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// DynamicSyncManager — 角色位置 + 道具位置串流
///
/// 兩條資料流，全部送到 Flask /dynamic_sync：
///   A. 角色位置（每 0.5 秒）→ source = "unity_user"
///   B. 道具位置（每 5 秒，有移動才送）→ source = "unity"
///
/// Flask /dynamic_sync 期望格式：
///   { "objects": [
///       { "label": "user_mom", "room": "", "position": [x, z], "source": "unity_user" },
///       { "label": "banana",   "room": "LivingRoom", "position": [x, z], "source": "unity" }
///   ]}
/// </summary>
public class DynamicSyncManager : MonoBehaviour
{
    [Header("使用者（必填）")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("動態道具（可動物件，例如 Cup / Banana）")]
    public List<GameObject> dynamicObjects = new List<GameObject>();

    [Header("後端 URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("串流頻率（秒）")]
    public float positionInterval  = 0.5f;
    public float objectSyncInterval = 5.0f;

    [Header("最小移動距離（低於此值不 POST）")]
    public float moveTolerance = 0.02f;

    [Header("除錯")]
    public bool verboseLog = false;

    // ── 私有 ──────────────────────────────────────────────
    Dictionary<string, Vector3> lastPos    = new Dictionary<string, Vector3>();
    Dictionary<string, Vector3> lastObjPos = new Dictionary<string, Vector3>();

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    // ══════════════════════════════════════════════════════
    void Start()
    {
        StartCoroutine(PositionLoop());
        StartCoroutine(ObjectLoop());
    }

    // ══════════════════════════════════════════════════════
    // A. 角色位置
    // ══════════════════════════════════════════════════════

    IEnumerator PositionLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(positionInterval);
            yield return StartCoroutine(SendPositions());
        }
    }

    IEnumerator SendPositions()
    {
        var entries = new List<string>();

        foreach (var user in new UserEntity[] { userMom, userDad })
        {
            if (user == null) continue;

            Vector3 pos = user.transform.position;

            // 沒有移動就跳過
            if (lastPos.ContainsKey(user.userID) &&
                Vector3.Distance(pos, lastPos[user.userID]) < moveTolerance)
                continue;

            lastPos[user.userID] = pos;

            // 手動建 JSON object string，避免匿名型別的序列化問題
            string entry = BuildObjectJson(
                label:    user.userID.ToLower(),
                room:     "",
                x:        pos.x,
                z:        pos.z,
                source:   "unity_user",
                extra:    "\"activity\":\"" + EscStr(user.currentActivity) + "\""
            );
            entries.Add(entry);
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(PostJson(backendUrl + "/dynamic_sync", json, "position"));
    }

    // ══════════════════════════════════════════════════════
    // B. 道具位置
    // ══════════════════════════════════════════════════════

    IEnumerator ObjectLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(objectSyncInterval);
            yield return StartCoroutine(SendObjects());
        }
    }

    IEnumerator SendObjects()
    {
        if (dynamicObjects.Count == 0) yield break;

        var entries    = new List<string>();
        bool anyMoved  = false;

        foreach (var obj in dynamicObjects)
        {
            if (obj == null) continue;

            Vector3 pos = obj.transform.position;
            string  key = obj.name;

            bool moved = !lastObjPos.ContainsKey(key) ||
                         Vector3.Distance(pos, lastObjPos[key]) > moveTolerance;

            if (moved)
            {
                lastObjPos[key] = pos;
                anyMoved = true;
            }

            string room  = DetectRoom(obj.transform.position);
            string entry = BuildObjectJson(
                label:  obj.name.ToLower(),
                room:   room,
                x:      pos.x,
                z:      pos.z,
                source: "unity",
                extra:  ""
            );
            entries.Add(entry);
        }

        if (!anyMoved) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(PostJson(backendUrl + "/dynamic_sync", json, "objects"));
    }

    // ══════════════════════════════════════════════════════
    // HTTP POST
    // ══════════════════════════════════════════════════════

    IEnumerator PostJson(string url, string json, string label)
    {
        if (verboseLog)
            Debug.Log("[DynamicSync] POST /" + label + " → " + json);

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning("[DynamicSync] /" + label + " POST failed: " + req.error);
    }

    // ══════════════════════════════════════════════════════
    // 輔助：手動建 JSON object string
    // ══════════════════════════════════════════════════════

    string BuildObjectJson(string label, string room, float x, float z,
                           string source, string extra)
    {
        string xs = x.ToString("F3", Inv);
        string zs = z.ToString("F3", Inv);

        string json = "{\"label\":\"" + EscStr(label) + "\","
                    + "\"room\":\""   + EscStr(room)   + "\","
                    + "\"position\":[" + xs + "," + zs + "],"
                    + "\"source\":\"" + EscStr(source) + "\"";

        if (!string.IsNullOrEmpty(extra))
            json += "," + extra;

        json += "}";
        return json;
    }

    // JSON 字串跳脫（只處理最基本的幾個字元）
    string EscStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }

    // ══════════════════════════════════════════════════════
    // 輔助：偵測物件所在房間
    // ══════════════════════════════════════════════════════

    string DetectRoom(Vector3 pos)
    {
        // 用 OverlapSphere 找 RoomArea trigger
        Collider[] hits = Physics.OverlapSphere(pos, 0.5f,
            Physics.AllLayers, QueryTriggerInteraction.Collide);

        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;
        }

        // Fallback：名稱推算（不依賴物理層設定）
        return "";
    }

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    public void ForcePositionSync() => StartCoroutine(SendPositions());
    public void ForceObjectSync()   => StartCoroutine(SendObjects());
}