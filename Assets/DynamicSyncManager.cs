using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class DynamicSyncManager : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Experiment Objects (synced during Exp modes)")]
    public List<GameObject> dynamicObjects = new List<GameObject>();

    [Header("Demo Objects (synced during Demo mode only)")]
    public List<GameObject> demoObjects = new List<GameObject>();

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";

    [Header("Intervals (seconds)")]
    public float positionInterval   = 0.5f;
    public float objectSyncInterval = 5.0f;

    [Header("Movement Threshold")]
    public float moveTolerance = 0.02f;

    [Header("Debug")]
    public bool verboseLog = false;

    Dictionary<string, Vector3> lastPos    = new Dictionary<string, Vector3>();
    Dictionary<string, Vector3> lastObjPos = new Dictionary<string, Vector3>();

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    ExperimentRunner _runner;

    void Start()
    {
        _runner = FindObjectOfType<ExperimentRunner>();
        StartCoroutine(PositionLoop());
        StartCoroutine(ObjectLoop());
    }

    bool IsDemo()
    {
        return _runner != null && _runner.mode == ExperimentRunner.RunMode.Demo;
    }

    List<GameObject> ActiveObjects()
    {
        return IsDemo() ? demoObjects : dynamicObjects;
    }

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

            if (lastPos.ContainsKey(user.userID) &&
                Vector3.Distance(pos, lastPos[user.userID]) < moveTolerance)
                continue;

            lastPos[user.userID] = pos;

            string entry = BuildObjectJson(
                label:  user.userID.ToLower(),
                room:   "",
                x:      pos.x,
                z:      pos.z,
                source: "unity_user",
                extra:  "\"activity\":\"" + EscStr(user.currentActivity) + "\""
            );
            entries.Add(entry);
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(PostJson(backendUrl + "/dynamic_sync", json, "position"));
    }

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
        var objects = ActiveObjects();
        if (objects == null || objects.Count == 0) yield break;

        var  entries  = new List<string>();
        bool anyMoved = false;

        foreach (var obj in objects)
        {
            if (obj == null || !obj.activeInHierarchy) continue;

            Vector3 pos = obj.transform.position;
            string  key = obj.name;

            bool moved = !lastObjPos.ContainsKey(key) ||
                         Vector3.Distance(pos, lastObjPos[key]) > moveTolerance;

            if (moved)
            {
                lastObjPos[key] = pos;
                anyMoved        = true;
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

    string BuildObjectJson(string label, string room, float x, float z,
                           string source, string extra)
    {
        string xs   = x.ToString("F3", Inv);
        string zs   = z.ToString("F3", Inv);
        string json = "{\"label\":\""    + EscStr(label)  + "\","
                    + "\"room\":\""      + EscStr(room)   + "\","
                    + "\"position\":["   + xs + "," + zs  + "],"
                    + "\"source\":\""    + EscStr(source) + "\"";

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

    public void ForcePositionSync() => StartCoroutine(SendPositions());
    public void ForceObjectSync()   => StartCoroutine(SendObjects());
}