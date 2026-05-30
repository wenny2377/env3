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

    [Header("Capture Timing")]
    public float defaultSettleTime = 0.4f;
    public int   settleFrames      = 2;
    public float captureCooldown   = 3f;

    [Header("Dependencies")]
    public VirtualCameraBrain virtualCameraBrain;

    HashSet<string>                      captureStateSet;
    Dictionary<string, List<CameraNode>> roomCameras = new();
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

        Debug.Log($"[SCM] Started | mode={captureMode} | " +
                  $"captureStates=[{captureStates}]");
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T)) return;
        if (userMom == null) return;
        var cams = FindCamerasForUser(userMom);
        if (cams == null || cams.Count == 0) return;
        Debug.Log("[SCM-T] Force capture userMom");
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
        string lastCaptured = "";
        float  cooldown     = 0f;

        while (true)
        {
            if (captureMode == CaptureMode.Manual)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            string cur = user.currentActivity;
            cooldown  -= 0.1f;

            if (cur == "Standing" || cur == "Walking")
                lastCaptured = "";

            bool inSet      = captureStateSet.Contains(cur);
            bool isNew      = cur != lastCaptured;
            bool notCooling = cooldown <= 0f;

            if (inSet && isNew && notCooling)
            {
                lastCaptured = cur;
                cooldown     = captureCooldown;

                float settle = GetSettleTime(cur);
                Debug.Log($"[SCM] {user.userID} | {cur} | settling {settle}s...");

                yield return new WaitForSeconds(settle);
                for (int f = 0; f < settleFrames; f++)
                    yield return null;

                if (!captureStateSet.Contains(user.currentActivity))
                {
                    Debug.Log($"[SCM] {user.userID} | changed during settle, skip.");
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                float lockWait = 0f;
                while (IsCapturing(user.userID) && lockWait < 15f)
                {
                    yield return new WaitForSeconds(0.1f);
                    lockWait += 0.1f;
                }
                if (IsCapturing(user.userID))
                {
                    Debug.LogWarning($"[SCM] {user.userID} | lock timeout, skip.");
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                List<CameraNode> cams = FindCamerasForUser(user);
                if (cams == null || cams.Count == 0)
                {
                    Debug.LogWarning($"[SCM] {user.userID}: no cameras.");
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                string label = !string.IsNullOrEmpty(user.lastAssignedActivity)
                    ? user.lastAssignedActivity : cur;

                Debug.Log($"[SCM] {user.userID} | {label} | capturing");
                yield return StartCoroutine(CaptureWithBestNodes(user, label, cams));
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    public IEnumerator CaptureWithBestNodes(
        UserEntity user, string activity, List<CameraNode> cameras)
    {
        SetCapturing(user.userID, true);

        List<CameraNode> ranked = ScoreCamerasRanked(user, cameras);
        List<CameraNode> toUse  = new List<CameraNode>();

        if (ranked != null && ranked.Count > 0)
            toUse.Add(ranked[0]);
        else
            toUse.Add(cameras[0]);

        string names = string.Join(", ",
            toUse.ConvertAll(n => $"{n.nodeName}({n.lastScore:F2})"));
        Debug.Log($"[SCM] {user.userID} | {activity} | [{names}]");

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
        Vector3 userPos     = user.transform.position;
        Vector3 userForward = user.transform.forward;
        Vector3 chestPos    = userPos + Vector3.up * 1.3f;
        var     scoredList  = new List<CameraNode>();

        string act = user.currentActivity;
        float facingThreshold = (act == "Laying"  || act == "SittingDrink" ||
                                 act == "Sitting"  || act == "Watching"     ||
                                 act == "Reading"  || act == "Typing")
                                ? -0.3f : 0.0f;

        foreach (var node in cameras)
        {
            if (node == null) continue;

            Vector3 toCamera  = (node.transform.position - userPos).normalized;
            float   facingDot = Vector3.Dot(userForward, toCamera);

            if (facingDot < facingThreshold)
            {
                node.lastScore = -1000f;
                continue;
            }

            Vector3 rayDir   = chestPos - node.transform.position;
            float   distance = rayDir.magnitude;

            float visibilityScore = 50f;

            if (Physics.Raycast(node.transform.position,
                rayDir.normalized, out RaycastHit hit, distance))
            {
                if (hit.transform != user.transform &&
                    !hit.transform.IsChildOf(user.transform))
                    visibilityScore = 0f;
            }

            float distanceScore = Mathf.Max(0f, (25f - distance) * 2f);

            node.lastScore = (facingDot * 50f) + visibilityScore + distanceScore;

            if (visibilityScore > 0f)
                scoredList.Add(node);
        }

        scoredList.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return scoredList;
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

    float GetSettleTime(string activity) => activity switch
    {
        "Drinking"     => 1.0f,
        "SittingDrink" => 1.5f,
        "Sitting"      => 1.5f,
        "Eating"       => 2.0f,
        "Cooking"      => 1.5f,
        "Laying"       => 2.0f,
        "Watching"     => 2.0f,
        "Reading"      => 2.0f,
        "Cleaning"     => 1.5f,
        "PhoneUse"     => 1.5f,
        "Typing"       => 1.5f,
        "StandUp"      => 0.5f,
        _              => defaultSettleTime,
    };

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in roomCameras)
            foreach (var cam in kv.Value)
            {
                if (cam == null) continue;
                Gizmos.color =
                    cam.lastScore >= 60f ? Color.green
                    : cam.lastScore >= 30f ? Color.yellow
                    : cam.lastScore > 0f   ? Color.red
                    :                        Color.gray;
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position, cam.transform.forward * 2f);
            }
    }
}