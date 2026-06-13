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

    public enum CaptureMode { EventDriven, Manual }

    [Header("Capture Mode")]
    public CaptureMode captureMode = CaptureMode.EventDriven;

    [Header("Target Users (Required)")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Capture Target States")]
    public string captureStates =
        "Drinking,SittingDrink,Sitting,Eating,Cooking,Opening," +
        "Laying,Watching,Reading,Cleaning,PhoneUse,Typing,StandUp";

    [Header("Still Detection")]
    public float stillSpeedThreshold = 0.05f;
    public float stillDuration       = 0.5f;
    public float captureCooldown     = 3.0f;

    [Header("Multi-view Selection")]
    public float minViewAngle = 30f;

    [Header("Dependencies")]
    public VirtualCameraBrain virtualCameraBrain;

    HashSet<string>                      captureStateSet;
    Dictionary<string, List<CameraNode>> roomCameras      = new();
    Dictionary<string, bool>             _isCapturingDict = new();

    bool IsCapturing(string userId) =>
        _isCapturingDict.TryGetValue(userId, out bool v) && v;

    void SetCapturing(string userId, bool val) =>
        _isCapturingDict[userId] = val;

    void Start()
    {
        captureStateSet = new HashSet<string>(captureStates.Split(','));

        if (virtualCameraBrain == null)
            virtualCameraBrain = FindObjectOfType<VirtualCameraBrain>();

        if (virtualCameraBrain == null)
            Debug.LogError("[SCM] VirtualCameraBrain not found.");

        if (userMom != null)
            StartCoroutine(SmartScanRoutine(userMom));
        else
            Debug.LogWarning("[SCM] userMom not assigned");

        if (userDad != null)
            StartCoroutine(SmartScanRoutine(userDad));
        else
            Debug.LogWarning("[SCM] userDad not assigned");

        Debug.Log($"[SCM] Started | mode={captureMode} | "
                + $"stillDuration={stillDuration}s | "
                + $"cooldown={captureCooldown}s | "
                + $"captureStates=[{captureStates}]");
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T)) return;
        if (userMom == null) return;
        var cams = FindCamerasForUser(userMom);
        if (cams == null || cams.Count == 0) return;
        StartCoroutine(CaptureWithBestNodes(userMom, "Drinking", cams));
    }

    public void RegisterRoomCameras(string roomName, List<CameraNode> cameras)
    {
        if (cameras == null || cameras.Count == 0) return;
        roomCameras[roomName] = cameras;
        Debug.Log($"[SCM] Registered '{roomName}': {cameras.Count} node(s)");
    }

    public List<CameraNode> GetCamerasForUser(UserEntity user)
        => FindCamerasForUser(user);

    public List<CameraNode> GetScoredCamerasForUser(UserEntity user)
    {
        var cams = FindCamerasForUser(user);
        return (cams == null || cams.Count == 0)
            ? null : ScoreCamerasRanked(user, cams);
    }

    public IEnumerator TriggerManualCapture(UserEntity user, string activity)
    {
        List<CameraNode> cams = GetScoredCamerasForUser(user);
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

            if (captureMode == CaptureMode.Manual)
                continue;

            Vector3 curPos = user.transform.position;
            float   speed  = Vector3.Distance(curPos, lastPos) / 0.1f;
            lastPos  = curPos;
            cooldown -= 0.1f;

            string cur = user.currentActivity;

            if (cur == "Standing" || cur == "Walking")
            {
                lastCaptured = "";
                stillTimer   = 0f;
                continue;
            }

            if (speed < stillSpeedThreshold)
                stillTimer += 0.1f;
            else
                stillTimer = 0f;

            bool inSet      = captureStateSet.Contains(cur);
            bool isNew      = cur != lastCaptured;
            bool isStill    = stillTimer >= stillDuration;
            bool notCooling = cooldown <= 0f;

            if (!inSet || !isNew || !isStill || !notCooling)
                continue;

            lastCaptured = cur;
            cooldown     = captureCooldown;
            stillTimer   = 0f;

            float lockWait = 0f;
            while (IsCapturing(user.userID) && lockWait < 15f)
            {
                yield return new WaitForSeconds(0.1f);
                lockWait += 0.1f;
            }
            if (IsCapturing(user.userID))
            {
                Debug.LogWarning($"[SCM] {user.userID} | lock timeout, skip.");
                continue;
            }

            if (!captureStateSet.Contains(user.currentActivity))
                continue;

            List<CameraNode> cams = FindCamerasForUser(user);
            if (cams == null || cams.Count == 0)
            {
                Debug.LogWarning($"[SCM] {user.userID}: no cameras.");
                continue;
            }

            // Skip if user is in transition (walking to spot)
            // This prevents capturing mid-action states
            string curAct = user.currentActivity;
            if (curAct == "Walking" || curAct == "StandUp" ||
                curAct == "PickingUp" || curAct == "PuttingDown")
                continue;

            // Skip if lastAssignedActivity is an intermediate action
            string assigned = user.lastAssignedActivity;
            if (assigned == "Opening" || assigned == "Walking" ||
                assigned == "Standing" || assigned == "StandUp")
                continue;

            string label = !string.IsNullOrEmpty(assigned) ? assigned : curAct;

            Debug.Log($"[SCM] {user.userID} | {label} | "
                    + $"still={stillTimer:F1}s | capturing");
            yield return StartCoroutine(CaptureWithBestNodes(user, label, cams));
        }
    }

    public IEnumerator CaptureWithBestNodes(
        UserEntity user, string activity, List<CameraNode> cameras)
    {
        SetCapturing(user.userID, true);

        List<CameraNode> ranked = ScoreCamerasRanked(user, cameras);
        List<CameraNode> toUse  = new List<CameraNode>();

        int     topN = virtualCameraBrain != null ? virtualCameraBrain.topN : 1;
        Vector3 dir1 = Vector3.zero;
        Vector3 uPos = user.transform.position;

        if (ranked != null)
        {
            foreach (var node in ranked)
            {
                if (toUse.Count >= topN) break;

                if (toUse.Count == 0)
                {
                    toUse.Add(node);
                    dir1 = (node.transform.position - uPos).normalized;
                }
                else
                {
                    Vector3 dirI  = (node.transform.position - uPos).normalized;
                    float   angle = Vector3.Angle(dir1, dirI);
                    if (angle >= minViewAngle)
                        toUse.Add(node);
                }
            }
        }

        if (toUse.Count == 0)
        {
            if (ranked != null && ranked.Count > 0)
                toUse.Add(ranked[0]);
            else if (cameras.Count > 0)
                toUse.Add(cameras[0]);
        }

        string names = string.Join(", ",
            toUse.ConvertAll(n => $"{n.nodeName}({n.lastScore:F2})"));
        Debug.Log($"[SCM] {user.userID} | {activity} | "
                + $"views={toUse.Count} | [{names}]");

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

            if (facingDot < -0.3f)
            {
                node.lastScore = -1000f;
                continue;
            }

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
            node.lastScore  = (facingDot * 50f) + visScore + distScore;

            if (visScore > 0f)
                scored.Add(node);
        }

        scored.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return scored;
    }

    List<CameraNode> FindCamerasForUser(UserEntity user)
    {
        if (roomCameras.Count == 0) return null;
        if (roomCameras.Count == 1)
        {
            foreach (var v in roomCameras.Values) return v;
        }

        string  nearest = null;
        float   minDist = float.MaxValue;
        Vector3 uPos    = user.transform.position;

        foreach (var kv in roomCameras)
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

        return nearest != null ? roomCameras[nearest] : null;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in roomCameras)
            foreach (var cam in kv.Value)
            {
                if (cam == null) continue;
                Gizmos.color =
                    cam.lastScore >= 60f ? Color.green  :
                    cam.lastScore >= 30f ? Color.yellow :
                    cam.lastScore >  0f  ? Color.red    :
                                           Color.gray;
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position, cam.transform.forward * 2f);
            }
    }
}