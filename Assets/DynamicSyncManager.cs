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

    [Header("Corruption Model")]
    [Range(0f, 1f)]
    public float objectConfusionRate = 0.0f;

    [Header("Debug")]
    public bool verboseLog = false;

    static readonly Dictionary<string, string> ConfusionMap = new Dictionary<string, string>
    {
        { "cola",   "bottle" }, { "bottle", "cola"   },
        { "remote", "phone"  }, { "phone",  "remote" },
        { "bowl",   "cup"    }, { "cup",    "bowl"   },
    };

    Dictionary<string, Vector3>    lastPos    = new Dictionary<string, Vector3>();
    Dictionary<string, Quaternion> lastRot    = new Dictionary<string, Quaternion>();
    Dictionary<string, Vector3>    lastObjPos = new Dictionary<string, Vector3>();

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    void Start()
    {
        StartCoroutine(PositionLoop());
        StartCoroutine(ObjectLoop());
    }

    string ApplyConfusion(string label)
    {
        if (objectConfusionRate <= 0f) return label;
        if (ConfusionMap.TryGetValue(label, out string confused))
            if (UnityEngine.Random.value < objectConfusionRate)
                return confused;
        return label;
    }

    string VirtualTimeJson()
    {
        return $"\"virtual_hour\":{ExperimentRunner.CurrentVirtualHour.ToString("F1", Inv)},"
             + $"\"virtual_day\":{ExperimentRunner.CurrentVirtualDay},"
             + $"\"time_slot\":\"{ExperimentRunner.CurrentTimeSlot}\",";
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

            entries.Add("{"
                + $"\"label\":\"{Esc(user.userID.ToLower())}\","
                + $"\"room\":\"\","
                + $"\"position\":[{pos.x.ToString("F3", Inv)},{pos.z.ToString("F3", Inv)}],"
                + $"\"source\":\"unity_user\","
                + $"\"activity\":\"{Esc(user.currentActivity)}\","
                + $"\"forward\":[{fwd.x.ToString("F3", Inv)},{fwd.y.ToString("F3", Inv)},{fwd.z.ToString("F3", Inv)}],"
                + VirtualTimeJson().TrimEnd(',')
                + "}");
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(Post(backendUrl + "/dynamic_sync", json));
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
        if (dynamicObjects == null || dynamicObjects.Count == 0) yield break;

        var entries = new List<string>();

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
            string label = ApplyConfusion(obj.name.ToLower());

            string entry = "{"
                + $"\"label\":\"{Esc(label)}\","
                + $"\"room\":\"{Esc(room)}\","
                + $"\"position\":[{pos.x.ToString("F3", Inv)},{pos.z.ToString("F3", Inv)}],"
                + $"\"source\":\"unity\","
                + VirtualTimeJson();

            if (!string.IsNullOrEmpty(heldBy))
                entry += $"\"held_by\":\"{Esc(heldBy)}\",";

            entry = entry.TrimEnd(',') + "}";
            entries.Add(entry);
        }

        if (entries.Count == 0) yield break;

        string json = "{\"objects\":[" + string.Join(",", entries) + "]}";
        yield return StartCoroutine(Post(backendUrl + "/dynamic_sync", json));
    }

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
                    return user.userID;
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

    IEnumerator Post(string url, string json)
    {
        if (verboseLog) Debug.Log($"[DynamicSync] POST {url}");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[DynamicSync] POST failed: {req.error}");
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

    static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r");
    }

    public void ForcePositionSync() => StartCoroutine(SendPositions());
    public void ForceObjectSync()   => StartCoroutine(SendObjects());
}