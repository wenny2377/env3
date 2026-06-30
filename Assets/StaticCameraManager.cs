using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StaticCameraManager : MonoBehaviour
{
    public static StaticCameraManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Dependencies")]
    public VirtualCameraBrain virtualCameraBrain;

    const float STILL_SPEED_THRESHOLD = 0.05f;
    const float STILL_DURATION        = 0.5f;
    const float CAPTURE_COOLDOWN      = 3.0f;
    const float MIN_VIEW_ANGLE        = 30f;
    const float ROOM_RECHECK_DIST     = 1.0f;
    const float LOCK_TIMEOUT          = 15f;

    static readonly HashSet<string> CaptureStateSet = new HashSet<string>
    {
        "Drinking", "SeatedDrinking", "Sitting", "Eating",
        "Cooking",  "Opening",        "Laying",  "Watching",
        "Reading",  "Cleaning",       "UsingPhone", "Typing", "StandUp",
    };

    static readonly HashSet<string> SkipStates = new HashSet<string>
    {
        "Walking", "StandUp", "PickingUp", "PuttingDown",
    };

    Dictionary<string, List<CameraNode>> _roomCameras      = new();
    Dictionary<string, bool>             _isCapturingDict  = new();
    Dictionary<string, Vector3>          _lastRoomCheckPos = new();
    Dictionary<string, List<CameraNode>> _cachedUserCams   = new();

    bool IsCapturing(string userId) =>
        _isCapturingDict.TryGetValue(userId, out bool v) && v;

    void SetCapturing(string userId, bool val) =>
        _isCapturingDict[userId] = val;

    void Start()
    {
        if (virtualCameraBrain == null)
            virtualCameraBrain = FindObjectOfType<VirtualCameraBrain>();
        if (virtualCameraBrain == null)
            Debug.LogError("[SCM] VirtualCameraBrain not found.");

        if (userMom != null) StartCoroutine(SmartScanRoutine(userMom));
        else Debug.LogWarning("[SCM] userMom not assigned");

        if (userDad != null) StartCoroutine(SmartScanRoutine(userDad));
        else Debug.LogWarning("[SCM] userDad not assigned");
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T) || userMom == null) return;
        var cams = FindCamerasForUser(userMom);
        if (cams != null && cams.Count > 0)
            StartCoroutine(CaptureWithBestNodes(userMom, "Drinking", cams));
    }

    public void RegisterRoomCameras(string roomName, List<CameraNode> cameras)
    {
        if (cameras == null || cameras.Count == 0) return;
        _roomCameras[roomName] = cameras;
        _cachedUserCams.Clear();
        Debug.Log($"[SCM] Registered '{roomName}': {cameras.Count} node(s)");
    }

    public List<CameraNode> GetCamerasForUser(UserEntity user) =>
        FindCamerasForUser(user);

    public List<CameraNode> GetScoredCamerasForUser(UserEntity user)
    {
        var cams = FindCamerasForUser(user);
        return (cams == null || cams.Count == 0) ? null : ScoreCamerasRanked(user, cams);
    }

    public IEnumerator TriggerManualCapture(UserEntity user, string activity)
    {
        var cams = GetScoredCamerasForUser(user);
        if (cams == null || cams.Count == 0)
        {
            Debug.LogWarning($"[SCM] TriggerManualCapture: no cameras for {user.userID}");
            yield break;
        }
        yield return StartCoroutine(CaptureWithBestNodes(user, activity, cams));
    }

    IEnumerator SmartScanRoutine(UserEntity user)
    {
        string  lastCaptured = "";
        float   cooldown     = 0f;
        Vector3 lastPos      = user.transform.position;
        float   stillTimer   = 0f;

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            Vector3 curPos = user.transform.position;
            float   speed  = Vector3.Distance(curPos, lastPos) / 0.1f;
            lastPos   = curPos;
            cooldown -= 0.1f;

            string cur = user.currentActivity;

            if (cur == "Standing" || cur == "Walking")
            {
                lastCaptured = "";
                stillTimer   = 0f;
                continue;
            }

            stillTimer = speed < STILL_SPEED_THRESHOLD ? stillTimer + 0.1f : 0f;

            if (!CaptureStateSet.Contains(cur)) continue;
            if (cur == lastCaptured)            continue;
            if (stillTimer < STILL_DURATION)    continue;
            if (cooldown > 0f)                  continue;

            lastCaptured = cur;
            cooldown     = CAPTURE_COOLDOWN;
            stillTimer   = 0f;

            float lockWait = 0f;
            while (IsCapturing(user.userID) && lockWait < LOCK_TIMEOUT)
            {
                yield return new WaitForSeconds(0.1f);
                lockWait += 0.1f;
            }
            if (IsCapturing(user.userID))
            {
                Debug.LogWarning($"[SCM] {user.userID} | lock timeout, skip.");
                continue;
            }

            string curAct = user.currentActivity;
            if (!CaptureStateSet.Contains(curAct)) continue;
            if (SkipStates.Contains(curAct))       continue;

            string assigned = user.lastAssignedActivity;
            if (assigned == "Walking" || assigned == "Standing" || assigned == "StandUp") continue;

            var cams = FindCamerasForUser(user);
            if (cams == null || cams.Count == 0)
            {
                Debug.LogWarning($"[SCM] {user.userID}: no cameras.");
                continue;
            }

            string label = user.GroundTruthLabel;
            Debug.Log($"[SCM] {user.userID} | {label} | still={stillTimer:F1}s");
            yield return StartCoroutine(CaptureWithBestNodes(user, label, cams));
        }
    }

    public IEnumerator CaptureWithBestNodes(
        UserEntity user, string activity, List<CameraNode> cameras)
    {
        SetCapturing(user.userID, true);

        List<CameraNode> ranked = ScoreCamerasRanked(user, cameras);
        List<CameraNode> toUse  = SelectDiverseNodes(ranked, cameras, user.transform.position);

        string names = string.Join(", ",
            toUse.ConvertAll(n => $"{n.nodeName}({n.lastScore:F2})"));
        Debug.Log($"[SCM] {user.userID} | {activity} | views={toUse.Count} | [{names}]");

        if (virtualCameraBrain == null)
        {
            Debug.LogError("[SCM] virtualCameraBrain is null!");
            SetCapturing(user.userID, false);
            yield break;
        }

        yield return StartCoroutine(
            virtualCameraBrain.ExecuteMultiCapture(user, toUse, activity));

        Debug.Log($"[SCM] done: {user.userID} | {activity}");
        SetCapturing(user.userID, false);
    }

    List<CameraNode> SelectDiverseNodes(
        List<CameraNode> ranked, List<CameraNode> fallback, Vector3 uPos)
    {
        var     toUse = new List<CameraNode>();
        Vector3 dir1  = Vector3.zero;

        if (ranked != null)
        {
            foreach (var node in ranked)
            {
                if (toUse.Count >= 2) break;
                if (toUse.Count == 0)
                {
                    toUse.Add(node);
                    dir1 = (node.transform.position - uPos).normalized;
                }
                else
                {
                    Vector3 dirI = (node.transform.position - uPos).normalized;
                    if (Vector3.Angle(dir1, dirI) >= MIN_VIEW_ANGLE)
                        toUse.Add(node);
                }
            }
        }

        if (toUse.Count == 0)
        {
            if (ranked != null && ranked.Count > 0)  toUse.Add(ranked[0]);
            else if (fallback != null && fallback.Count > 0) toUse.Add(fallback[0]);
        }

        return toUse;
    }

    List<CameraNode> ScoreCamerasRanked(UserEntity user, List<CameraNode> cameras)
    {
        Vector3 userPos  = user.transform.position;
        Vector3 chestPos = userPos + Vector3.up * 1.3f;
        var     scored   = new List<CameraNode>();

        foreach (var node in cameras)
        {
            if (node == null) continue;

            Vector3 toCamera  = (node.transform.position - userPos).normalized;
            float   facingDot = Vector3.Dot(user.transform.forward, toCamera);

            if (facingDot < -0.3f) { node.lastScore = -1000f; continue; }

            Vector3 rayDir   = chestPos - node.transform.position;
            float   distance = rayDir.magnitude;
            float   visScore = 50f;

            if (Physics.Raycast(node.transform.position,
                rayDir.normalized, out RaycastHit hit, distance))
            {
                if (hit.transform != user.transform &&
                    !hit.transform.IsChildOf(user.transform))
                    visScore = 0f;
            }

            float distScore = Mathf.Max(0f, (25f - distance) * 2f);
            node.lastScore  = facingDot * 50f + visScore + distScore;

            if (visScore > 0f) scored.Add(node);
        }

        scored.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return scored;
    }

    List<CameraNode> FindCamerasForUser(UserEntity user)
    {
        if (_roomCameras.Count == 0) return null;
        if (_roomCameras.Count == 1)
        {
            foreach (var v in _roomCameras.Values) return v;
        }

        Vector3 uPos = user.transform.position;

        if (_cachedUserCams.TryGetValue(user.userID, out var cached) &&
            _lastRoomCheckPos.TryGetValue(user.userID, out Vector3 lastCheck) &&
            Vector3.Distance(uPos, lastCheck) < ROOM_RECHECK_DIST)
            return cached;

        string nearest = null;
        float  minDist = float.MaxValue;

        foreach (var kv in _roomCameras)
        {
            Vector3 center = Vector3.zero;
            int     cnt    = 0;
            foreach (var c in kv.Value)
                if (c != null) { center += c.transform.position; cnt++; }
            if (cnt == 0) continue;
            center /= cnt;
            float d = Vector3.Distance(uPos, center);
            if (d < minDist) { minDist = d; nearest = kv.Key; }
        }

        if (nearest == null) return null;

        _cachedUserCams[user.userID]   = _roomCameras[nearest];
        _lastRoomCheckPos[user.userID] = uPos;
        return _roomCameras[nearest];
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in _roomCameras)
            foreach (var cam in kv.Value)
            {
                if (cam == null) continue;
                Gizmos.color =
                    cam.lastScore >= 60f ? Color.green  :
                    cam.lastScore >= 30f ? Color.yellow :
                    cam.lastScore >  0f  ? Color.red    : Color.gray;
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position, cam.transform.forward * 2f);
            }
    }
}