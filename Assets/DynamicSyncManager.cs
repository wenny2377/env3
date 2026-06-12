using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class DynamicSyncManager : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Experiment Objects")]
    public List<GameObject> dynamicObjects = new List<GameObject>();

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";

    [Header("Intervals (seconds)")]
    public float positionInterval   = 0.5f;
    public float objectSyncInterval = 2.0f;

    [Header("Movement Threshold")]
    public float moveTolerance = 0.02f;

    [Header("Debug")]
    public bool verboseLog = false;

    Dictionary<string, Vector3>    lastPos    = new Dictionary<string, Vector3>();
    Dictionary<string, Quaternion> lastRot    = new Dictionary<string, Quaternion>();
    Dictionary<string, Vector3>    lastObjPos = new Dictionary<string, Vector3>();

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    void Start()
    {
        StartCoroutine(PositionLoop());
        StartCoroutine(ObjectLoop());
    }

    // ── Position sync loop ────────────────────────────────────────────────────

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
        var    entries   = new List<string>();
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");

        foreach (var user in new UserEntity[] { userMom, userDad })
        {
            if (user == null) continue;

            Vector3    pos = user.transform.position;
            Quaternion rot = user.transform.rotation;

            bool posMoved = !lastPos.ContainsKey(user.userID) ||
                            Vector3.Distance(pos, lastPos[user.userID]) >= moveTolerance;
            bool rotMoved = !lastRot.ContainsKey(user.userID) ||
                            Quaternion.Angle(rot, lastRot[user.userID]) >= 1f;

            if (!posMoved && !rotMoved) continue;

            lastPos[user.userID] = pos;
            lastRot[user.userID] = rot;

            Vector3 fwd = user.transform.forward;

            string fwdJson =
                "\"forward\":["
                + fwd.x.ToString("F3", Inv) + ","
                + fwd.y.ToString("F3", Inv) + ","
                + fwd.z.ToString("F3", Inv) + "]";

            string extra =
                "\"activity\":\"" + EscStr(user.currentActivity) + "\","
                + fwdJson;

            entries.Add(BuildObjectJson(
                label:     user.userID.ToLower(),
                room:      "",
                x:         pos.x,
                z:         pos.z,
                source:    "unity_user",
                timestamp: timestamp,
                extra:     extra
            ));
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(PostJson(backendUrl + "/dynamic_sync", json, "position"));
    }

    // ── Object sync loop ──────────────────────────────────────────────────────

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
        if (dynamicObjects == null || dynamicObjects.Count == 0) yield break;

        var    entries   = new List<string>();
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");

        foreach (var obj in dynamicObjects)
        {
            if (obj == null) continue;

            string heldBy = FindHolderOf(obj);
            bool   isHeld = !string.IsNullOrEmpty(heldBy);

            Vector3 pos;
            if (isHeld)
            {
                UserEntity holder = GetUserByID(heldBy);
                pos = holder != null ? holder.transform.position : obj.transform.position;
            }
            else
            {
                if (!obj.activeInHierarchy) continue;
                pos = obj.transform.position;
            }

            string key   = obj.name;
            bool   moved = !lastObjPos.ContainsKey(key) ||
                           Vector3.Distance(pos, lastObjPos[key]) > moveTolerance;
            if (moved) lastObjPos[key] = pos;

            string room  = DetectRoom(pos);
            string extra = string.IsNullOrEmpty(heldBy)
                ? ""
                : "\"held_by\":\"" + EscStr(heldBy) + "\"";

            entries.Add(BuildObjectJson(
                label:     obj.name.ToLower(),
                room:      room,
                x:         pos.x,
                z:         pos.z,
                source:    "unity",
                timestamp: timestamp,
                extra:     extra
            ));
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(PostJson(backendUrl + "/dynamic_sync", json, "objects"));
    }

    // ── Held object detection ─────────────────────────────────────────────────

    string FindHolderOf(GameObject obj)
    {
        foreach (var user in new UserEntity[] { userMom, userDad })
        {
            if (user == null) continue;
            var items = user.GetBehaviorItems();
            if (items == null) continue;

            foreach (var bi in items)
            {
                if (bi == null) continue;

                bool isCounterpart =
                    (bi.sceneCounterpart  != null && bi.sceneCounterpart  == obj) ||
                    (bi.sceneCounterpart2 != null && bi.sceneCounterpart2 == obj);

                if (!isCounterpart) continue;

                bool counterpartHidden =
                    (bi.sceneCounterpart  != null && !bi.sceneCounterpart.activeSelf) ||
                    (bi.sceneCounterpart2 != null && !bi.sceneCounterpart2.activeSelf);

                bool itemActive =
                    (bi.item  != null && bi.item.activeSelf) ||
                    (bi.item2 != null && bi.item2.activeSelf);

                if (counterpartHidden && itemActive)
                {
                    Debug.Log($"[FindHolderOf] HIT: {obj.name} held by " +
                              $"{user.userID} activity={bi.activity}");
                    return user.userID;
                }
            }
        }
        return "";
    }

    UserEntity GetUserByID(string userID)
    {
        if (userMom != null && userMom.userID == userID) return userMom;
        if (userDad != null && userDad.userID == userID) return userDad;
        return null;
    }

    // ── HTTP helper ───────────────────────────────────────────────────────────

    IEnumerator PostJson(string url, string json, string label)
    {
        if (verboseLog)
            Debug.Log($"[DynamicSync] POST /{label} -> {json}");

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[DynamicSync] /{label} POST failed: {req.error}");
    }

    // ── JSON builders ─────────────────────────────────────────────────────────

    string BuildObjectJson(string label, string room, float x, float z,
                           string source, string timestamp, string extra)
    {
        string xs   = x.ToString("F3", Inv);
        string zs   = z.ToString("F3", Inv);
        string json = "{\"label\":\""     + EscStr(label)     + "\","
                    + "\"room\":\""       + EscStr(room)      + "\","
                    + "\"position\":["    + xs + "," + zs     + "],"
                    + "\"source\":\""     + EscStr(source)    + "\","
                    + "\"timestamp\":\"" + timestamp          + "\"";

        if (!string.IsNullOrEmpty(extra))
            json += "," + extra;

        json += "}";
        return json;
    }

    string EscStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }

    // ── Room detection ────────────────────────────────────────────────────────

    string DetectRoom(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 0.5f,
            Physics.AllLayers, QueryTriggerInteraction.Collide);

        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;
        }
        return "";
    }

    // ── Public force-sync API ─────────────────────────────────────────────────

    public void ForcePositionSync() => StartCoroutine(SendPositions());
    public void ForceObjectSync()   => StartCoroutine(SendObjects());
}