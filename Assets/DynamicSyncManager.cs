using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// DynamicSyncManager ¡X °Ê÷A×«¥ף§Y®ֹ¦ך¬y¨ל«ב÷Ý
///
/// ­t³d¨ג±ר¸ך®ֶ¬y¡G
///
///   A. ¨ֿ¥־×ּ¦ל¸m¦ך¬y¡]/user_position¡^
///      ¨C positionInterval ¬ם POST ₪@¦¸ User_Mom ©M User_Dad ×÷
///      ¥@¬ֹ®y¼׀ + ³t«׳¦V¶q + ·ם«e¦ז¬°
///      «ב÷Ý¥־³o­׃­p÷ג¯S¼x¦V¶q¡]position_norm / velocity_norm / anchor_distances¡^
///
///   B. °Ê÷A×«¥ף§ף·s¡]/dynamic_sync¡^
///      ¨C objectSyncInterval ¬ם POST ₪@¦¸©ׂ¦³¡u¥i°Ê×«¥ף¡v×÷¦ל¸m
///      ¥i°Ê×«¥ף¡GCup¡BKeyboard µ¥₪ג«ש¹D¨ד¡A¥i¯א³Q¨₪¦ג®³¨««ב¦ל¸m§ןֵÜ
///      ְR÷A®a¨ד₪£»Ý­n¡A¾a SceneSyncManager ×÷ /scene ₪@¦¸©Ê¦P¨B
///
/// ¨ג±ר¬y×÷ְW²v¿W¥ß³]©w¡G
///   positionInterval  ¹w³] 0.5s¡]Manifold »Ý­n³sִע¦ל¸m¦פ÷ג³t«׳¡^
///   objectSyncInterval ¹w³] 5.0s¡]¹D¨ד¦ל¸m₪£»Ý­n¨÷»עְWֱc¡^
///
/// ±¾¸ü¦ל¸m¡G[_System] / DynamicSyncManager
///
/// Inspector ¥²¶ס¡G
///   userMom, userDad ¡ק UserEntity
///   dynamicObjects   ¡ק ³ץ´÷₪₪·|²¾°Ê×÷¹D¨ד¡]Cup, Keyboard µ¥¡^
///
/// POST /user_position JSON¡G
///   {
///     "users": [
///       {
///         "user_id":   "User_Mom",
///         "x": 1.2, "y": 0.0, "z": 3.4,
///         "vx": 0.5, "vy": 0.0, "vz": 0.2,
///         "activity": "Drink",
///         "timestamp": "2026-03-14T10:00:00.000"
///       },
///       ...
///     ]
///   }
///
/// POST /dynamic_sync JSON¡G
///   {
///     "objects": [
///       { "id": "Cup", "x": 1.1, "y": 0.8, "z": 3.3 },
///       { "id": "Keyboard", "x": 4.5, "y": 0.75, "z": 1.2 }
///     ],
///     "timestamp": "2026-03-14T10:00:00.000"
///   }
/// </summary>
public class DynamicSyncManager : MonoBehaviour
{
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Inspector ִז¦ל
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    [Header("¨ֿ¥־×ּ¡]¥²¶ס¡^")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("°Ê÷A×«¥ף¡]¥i°Ê¹D¨ד¡A¨ׂ¦p Cup / Keyboard¡^")]
    [Tooltip("³o¨ַ×«¥ף×÷¦ל¸m·|©w´ֱ¦P¨B¨ל«ב÷Ý\nְR÷A®a¨ד₪£»Ý­n¶ס¡A¾a SceneSyncManager ³B²z")]
    public List<GameObject> dynamicObjects = new List<GameObject>();

    [Header("«ב÷Ý URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("¦ך¬yְW²v¡]¬ם¡^")]
    [Tooltip("¨ֿ¥־×ּ¦ל¸m¦ך¬y¶¡¹j¡]«״ִ³ 0.5s¡^\n«ב÷Ý¥־¨׃¦פ÷ג³t«׳¦V¶q©M anchor ¶Zֲק")]
    public float positionInterval = 0.5f;

    [Tooltip("°Ê÷A×«¥ף¦P¨B¶¡¹j¡]«״ִ³ 3~5s¡^\n¥u¦b×«¥ף½T¹ך²¾°Ê«ב₪~ POST")]
    public float objectSyncInterval = 5.0f;

    [Header("®ִ¯א±±¨מ")]
    [Tooltip("¦ל¸mֵÜ₪ֶ₪p©ף¦¹¶Zֲק´N₪£ POST¡]´מ₪ײ₪£¥²­n×÷½׀¨D¡^\n«״ִ³ 0.01~0.05")]
    public float positionChangeTolerance = 0.02f;

    [Tooltip("₪ִ¿ן«ב¦b Console ֵד¥Ü¨C¦¸ POST ×÷₪÷®e¡]°£¿ש¥־¡^")]
    public bool verboseLog = false;

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¨p¦³¦¨­û
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    // °O¿‎₪W₪@¦¸ POST ×÷¦ל¸m¡A¥־¨׃§Pֲ_¬O§_»Ý­n§ף·s
    Dictionary<string, Vector3> lastPostedPosition = new Dictionary<string, Vector3>();
    Dictionary<string, Vector3> lastObjectPosition = new Dictionary<string, Vector3>();

    // ³t«׳¦פ÷ג¡]₪W₪@´V¦ל¸m¡^
    Dictionary<string, Vector3> prevFramePosition = new Dictionary<string, Vector3>();

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Unity ¥ֽ©R¶g´ֱ
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    void Start()
    {
        // ×ל©l₪ֶ¦ל¸m°O¿‎
        InitUserTracking(userMom);
        InitUserTracking(userDad);

        foreach (var obj in dynamicObjects)
            if (obj != null)
                lastObjectPosition[obj.name] = obj.transform.position;

        // ±ׂ°Ê¨ג±ר¦ך¬y
        StartCoroutine(PositionStreamLoop());
        StartCoroutine(ObjectSyncLoop());
    }

    void InitUserTracking(UserEntity user)
    {
        if (user == null) return;
        lastPostedPosition[user.userID] = user.transform.position;
        prevFramePosition[user.userID] = user.transform.position;
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // A. ¨ֿ¥־×ּ¦ל¸m¦ך¬y
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

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

            // ֵÜ₪ֶ¶q₪׃₪p´N¸ץ¹L¡]¦‎₪´¥[₪J¦C×םֵ‎«ב÷Ý×¾¹D×¬÷A¡^
            bool changed = !lastPostedPosition.ContainsKey(user.userID) ||
                           Vector3.Distance(pos, lastPostedPosition[user.userID]) > positionChangeTolerance;

            // ³t«׳¦פ÷ג¡G(²{¦b¦ל¸m - ₪W₪@¦¸°O¿‎¦ל¸m) / ¶¡¹j
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

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // B. °Ê÷A×«¥ף¦P¨B
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

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

        // ¥‏³¡³£¨S°Ê´N₪£ POST
        if (!anyChanged) yield break;

        string json = SimpleJson(new Dictionary<string, object>
        {
            { "objects",   objectList },
            { "timestamp", System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") }
        });

        yield return StartCoroutine(Post($"{backendUrl}/dynamic_sync", json, "objects"));
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // HTTP POST ¦@¥־₪ט×k
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator Post(string url, string json, string label)
    {
        if (verboseLog)
            Debug.Log($"[DynamicSync] POST /{label} ¡ק {json}");

        using var req = new UnityWebRequest(url, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[DynamicSync] /{label} POST ¥¢±ׁ: {req.error}");
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ֲ²©צ JSON §ַ¦C₪ֶ¡]ֱ׳§K Newtonsoft ¨ּ¿א¡^
    //
    // ¥u₪ה´©¡Gstring / float / int / bool / List<object> / Dictionary
    // ¦p×G₪w¦w¸ֻ Newtonsoft.Json ¥i¥H×½±µ´«¦¨ JsonConvert.SerializeObject
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

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

        // °־¦W«¬§O / ₪ֿ®g§ַ¦C₪ֶ
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

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¹ן¥~ API¡]ExperimentRunner / UserEntity ¥i©I¥s¡^
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    /// <summary>¥ß§Y±j¨מ¦P¨B₪@¦¸¦ל¸m¡]¹ךֵח¶}©l®ֹ©I¥s¡^</summary>
    public void ForcePositionSync() =>
        StartCoroutine(PostUserPositions());

    /// <summary>¥ß§Y±j¨מ¦P¨B₪@¦¸°Ê÷A×«¥ף</summary>
    public void ForceObjectSync() =>
        StartCoroutine(PostDynamicObjects());
}