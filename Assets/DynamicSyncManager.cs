using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class DynamicSyncManager : MonoBehaviour
{

    [Header("Users (required)")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Dynamic Objects (movable objects such as Cup / Keyboard)")]
    [Tooltip("The positions of these objects will be synchronized\nUsually objects that do not need SceneSyncManager handling")]
    public List<GameObject> dynamicObjects = new List<GameObject>();

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Frequency Settings")]
    [Tooltip("User position streaming interval (recommended 0.5s)\nUsed to calculate velocity and anchor prediction")]
    public float positionInterval = 0.5f;

    [Tooltip("Dynamic object sync interval (recommended 3~5s)\nPOST only when objects actually move")]
    public float objectSyncInterval = 5.0f;

    [Header("Performance Control")]
    [Tooltip("If position change is smaller than this threshold, POST will be skipped to reduce unnecessary requests\nRecommended 0.01~0.05")]
    public float positionChangeTolerance = 0.02f;

    [Tooltip("Show every POST content in Console (for debugging)")]
    public bool verboseLog = false;

    Dictionary<string, Vector3> lastPostedPosition = new Dictionary<string, Vector3>();
    Dictionary<string, Vector3> lastObjectPosition = new Dictionary<string, Vector3>();

    // velocity calculation (previous frame position)
    Dictionary<string, Vector3> prevFramePosition = new Dictionary<string, Vector3>();


    void Start()
    {
        // initialize position records
        InitUserTracking(userMom);
        InitUserTracking(userDad);

        foreach (var obj in dynamicObjects)
            if (obj != null)
                lastObjectPosition[obj.name] = obj.transform.position;

        // start loops
        StartCoroutine(PositionStreamLoop());
        StartCoroutine(ObjectSyncLoop());
    }

    void InitUserTracking(UserEntity user)
    {
        if (user == null) return;
        lastPostedPosition[user.userID] = user.transform.position;
        prevFramePosition[user.userID] = user.transform.position;
    }


    IEnumerator PositionStreamLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(positionInterval);
            yield return StartCoroutine(PostUserPositions());
        }
    }

    IEnumerator PostUserPositions()
    {
        var userList = new List<object>();

        foreach (var user in new[] { userMom, userDad })
        {
            if (user == null) continue;

            Vector3 pos = user.transform.position;

            // skip if change is too small
            bool changed = !lastPostedPosition.ContainsKey(user.userID) ||
                           Vector3.Distance(pos, lastPostedPosition[user.userID]) > positionChangeTolerance;

            // velocity calculation = (current position - previous position) / interval
            Vector3 prev = prevFramePosition.ContainsKey(user.userID)
                ? prevFramePosition[user.userID]
                : pos;
            Vector3 velocity = (pos - prev) / positionInterval;
            prevFramePosition[user.userID] = pos;

            userList.Add(new
            {
                user_id = user.userID,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                vx = velocity.x,
                vy = velocity.y,
                vz = velocity.z,
                activity = user.currentActivity,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff")
            });

            if (changed)
                lastPostedPosition[user.userID] = pos;
        }

        if (userList.Count == 0) yield break;

        string json = SimpleJson(new Dictionary<string, object>
        {
            { "users", userList }
        });

        yield return StartCoroutine(Post($"{backendUrl}/user_position", json, "position"));
    }


    IEnumerator ObjectSyncLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(objectSyncInterval);
            yield return StartCoroutine(PostDynamicObjects());
        }
    }

    IEnumerator PostDynamicObjects()
    {
        if (dynamicObjects.Count == 0) yield break;

        var objectList = new List<object>();
        bool anyChanged = false;

        foreach (var obj in dynamicObjects)
        {
            if (obj == null) continue;

            Vector3 pos = obj.transform.position;
            bool changed = !lastObjectPosition.ContainsKey(obj.name) ||
                           Vector3.Distance(pos, lastObjectPosition[obj.name]) > positionChangeTolerance;

            objectList.Add(new
            {
                id = obj.name,
                x = pos.x,
                y = pos.y,
                z = pos.z
            });

            if (changed)
            {
                lastObjectPosition[obj.name] = pos;
                anyChanged = true;
            }
        }

        // skip POST if nothing moved
        if (!anyChanged) yield break;

        string json = SimpleJson(new Dictionary<string, object>
        {
            { "objects", objectList },
            { "timestamp", System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") }
        });

        yield return StartCoroutine(Post($"{backendUrl}/dynamic_sync", json, "objects"));
    }


    IEnumerator Post(string url, string json, string label)
    {
        if (verboseLog)
            Debug.Log($"[DynamicSync] POST /{label} -> {json}");

        using var req = new UnityWebRequest(url, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[DynamicSync] /{label} POST failed: {req.error}");
    }

    string SimpleJson(object obj)
    {
        if (obj == null) return "null";
        if (obj is string s) return $"\"{EscapeJson(s)}\"";
        if (obj is bool b) return b ? "true" : "false";
        if (obj is float f) return f.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        if (obj is double d) return d.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        if (obj is int i) return i.ToString();

        if (obj is Dictionary<string, object> dict)
        {
            var pairs = new List<string>();
            foreach (var kv in dict)
                pairs.Add($"\"{EscapeJson(kv.Key)}\":{SimpleJson(kv.Value)}");
            return "{" + string.Join(",", pairs) + "}";
        }

        if (obj is List<object> list)
        {
            var items = new List<string>();
            foreach (var item in list) items.Add(SimpleJson(item));
            return "[" + string.Join(",", items) + "]";
        }

        var type = obj.GetType();
        var props = type.GetProperties();
        if (props.Length > 0)
        {
            var pairs = new List<string>();
            foreach (var p in props)
                pairs.Add($"\"{EscapeJson(p.Name)}\":{SimpleJson(p.GetValue(obj))}");
            return "{" + string.Join(",", pairs) + "}";
        }

        return $"\"{EscapeJson(obj.ToString())}\"";
    }

    string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")
         .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");


    public void ForcePositionSync() =>
        StartCoroutine(PostUserPositions());

    public void ForceObjectSync() =>
        StartCoroutine(PostDynamicObjects());
}