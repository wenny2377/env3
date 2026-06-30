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

    [Header("Corruption Model")]
    [Range(0f, 1f)]
    public float objectConfusionRate = 0.0f;

    const string BACKEND_URL          = "http://localhost:5000";
    const float  POSITION_INTERVAL    = 0.5f;
    const float  OBJECT_SYNC_INTERVAL = 5.0f;
    const float  MOVE_TOLERANCE       = 0.02f;
    const int    POST_TIMEOUT         = 15;

    static readonly Dictionary<string, string> ConfusionMap = new Dictionary<string, string>
    {
        { "cola",   "bottle" }, { "bottle", "cola"   },
        { "remote", "phone"  }, { "phone",  "remote" },
        { "bowl",   "cup"    }, { "cup",    "bowl"   },
    };

    readonly Dictionary<string, Vector3>    _lastPos    = new();
    readonly Dictionary<string, Quaternion> _lastRot    = new();
    readonly Dictionary<string, Vector3>    _lastObjPos = new();

    UserEntity[] AllUsers => new UserEntity[] { userMom, userDad };

    void Start()
    {
        StartCoroutine(PositionLoop());
        StartCoroutine(ObjectLoop());
    }

    string ApplyConfusion(string label)
    {
        if (objectConfusionRate <= 0f) return label;
        if (ConfusionMap.TryGetValue(label, out string confused) &&
            UnityEngine.Random.value < objectConfusionRate)
            return confused;
        return label;
    }

    string VirtualTimeJson() =>
          $"\"virtual_hour\":{ExperimentRunner.CurrentVirtualHour.ToString("F1", JsonUtil.Inv)},"
        + $"\"virtual_day\":{ExperimentRunner.CurrentVirtualDay},"
        + $"\"time_slot\":\"{ExperimentRunner.CurrentTimeSlot}\"";

    IEnumerator PositionLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(POSITION_INTERVAL);
            yield return StartCoroutine(SendPositions());
        }
    }

    IEnumerator SendPositions()
    {
        var entries = new List<string>();

        foreach (var user in AllUsers)
        {
            if (user == null) continue;

            Vector3    pos = user.transform.position;
            Quaternion rot = user.transform.rotation;

            bool posMoved = !_lastPos.TryGetValue(user.userID, out Vector3 lp) ||
                            Vector3.Distance(pos, lp) >= MOVE_TOLERANCE;
            bool rotMoved = !_lastRot.TryGetValue(user.userID, out Quaternion lr) ||
                            Quaternion.Angle(rot, lr) >= 1f;

            if (!posMoved && !rotMoved) continue;

            _lastPos[user.userID] = pos;
            _lastRot[user.userID] = rot;

            Vector3 fwd = user.transform.forward;
            entries.Add("{"
                + $"\"label\":\"{JsonUtil.Esc(user.userID.ToLower())}\","
                + $"\"room\":\"\","
                + $"\"position\":[{pos.x.ToString("F3", JsonUtil.Inv)},{pos.z.ToString("F3", JsonUtil.Inv)}],"
                + $"\"source\":\"unity_user\","
                + $"\"activity\":\"{JsonUtil.Esc(user.GroundTruthLabel)}\","
                + $"\"forward\":[{fwd.x.ToString("F3", JsonUtil.Inv)},{fwd.y.ToString("F3", JsonUtil.Inv)},{fwd.z.ToString("F3", JsonUtil.Inv)}],"
                + VirtualTimeJson()
                + "}");
        }

        if (entries.Count > 0)
            yield return StartCoroutine(Post(
                BACKEND_URL + "/dynamic_sync",
                "{\"objects\":[" + string.Join(",", entries) + "]}"));
    }

    IEnumerator ObjectLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(OBJECT_SYNC_INTERVAL);
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

            string key = obj.name;
            if (_lastObjPos.TryGetValue(key, out Vector3 lastP) &&
                Vector3.Distance(pos, lastP) <= MOVE_TOLERANCE &&
                !isHeld)
                continue;

            _lastObjPos[key] = pos;

            string room  = DetectRoom(pos);
            string label = ApplyConfusion(obj.name.ToLower());

            string entry = "{"
                + $"\"label\":\"{JsonUtil.Esc(label)}\","
                + $"\"room\":\"{JsonUtil.Esc(room)}\","
                + $"\"position\":[{pos.x.ToString("F3", JsonUtil.Inv)},{pos.z.ToString("F3", JsonUtil.Inv)}],"
                + $"\"source\":\"unity\","
                + VirtualTimeJson();

            if (!string.IsNullOrEmpty(heldBy))
                entry += $",\"held_by\":\"{JsonUtil.Esc(heldBy)}\"";

            entries.Add(entry + "}");
        }

        if (entries.Count > 0)
            yield return StartCoroutine(Post(
                BACKEND_URL + "/dynamic_sync",
                "{\"objects\":[" + string.Join(",", entries) + "]}"));
    }

    string FindHolderOf(GameObject obj)
    {
        foreach (var user in AllUsers)
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
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = POST_TIMEOUT;
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

    public void ForcePositionSync() => StartCoroutine(SendPositions());
    public void ForceObjectSync()   => StartCoroutine(SendObjects());
}