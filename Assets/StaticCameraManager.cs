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

    [Header("Target Users (Required)")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Capture Target States (case-sensitive, comma-separated)")]
    [Tooltip("Snapshot triggers only when entering these activities\nDrink,SittingIdle,Typing,Reading")]
    public string captureStates = "Drink,SittingIdle,Typing,Reading";

    [Header("Capture Timing")]
    [Tooltip("Seconds to wait after entering a target state (lets the animation loop settle)\nThis is the fallback default — each state has its own value")]
    public float defaultSettleTime = 0.4f;

    [Tooltip("Extra frames to wait after settle time (avoids Animator first-frame jitter)")]
    public int settleFrames = 2;

    [Header("Camera Score Threshold")]
    [Tooltip("Nodes below this score are excluded from the capture candidate list\nRecommended: 0.45-0.60\nToo high: all nodes fall to Fallback\nToo low: poor-angle views are included")]
    [Range(0f, 1f)]
    public float minScoreThreshold = 0.50f;

    [Tooltip("Seconds to wait before re-scoring when all nodes fall below threshold")]
    public float fallbackDelay = 0.5f;

    [Tooltip("Maximum fallback retries before abandoning the current capture")]
    public int fallbackMaxRetry = 3;

    [Header("Dependencies (auto-found if left empty)")]
    public VirtualCameraBrain virtualCameraBrain;

    HashSet<string> captureStateSet;
    Dictionary<string, List<CameraNode>> roomCameras = new Dictionary<string, List<CameraNode>>();

    void Start()
    {
        captureStateSet = new HashSet<string>(captureStates.Split(','));

        if (virtualCameraBrain == null)
            virtualCameraBrain = FindObjectOfType<VirtualCameraBrain>();

        if (virtualCameraBrain == null)
            Debug.LogError("[StaticCameraManager] VirtualCameraBrain not found — " +
                           "create an empty GameObject, attach VirtualCameraBrain.cs, " +
                           "and drag the main Camera into its mainCamera field");

        if (userMom != null) StartCoroutine(SmartScanRoutine(userMom));
        else Debug.LogWarning("[StaticCameraManager] userMom is not assigned");

        if (userDad != null) StartCoroutine(SmartScanRoutine(userDad));
        else Debug.LogWarning("[StaticCameraManager] userDad is not assigned");
    }

    public void RegisterRoomCameras(string roomName, List<CameraNode> cameras)
    {
        if (cameras == null || cameras.Count == 0)
        {
            Debug.LogWarning($"[StaticCameraManager] RegisterRoomCameras '{roomName}' list is empty");
            return;
        }
        roomCameras[roomName] = cameras;
        Debug.Log($"[StaticCameraManager] Registered '{roomName}': {cameras.Count} virtual node(s)");
    }

    public void RequestSnapshot(Transform[] pivots, string room, string userID, string activity)
    {
        UserEntity target = GetUserById(userID);
        if (target == null) return;

        var nodes = new List<CameraNode>();
        foreach (var t in pivots)
        {
            if (t == null) continue;
            var n = t.GetComponent<CameraNode>();
            if (n != null) nodes.Add(n);
        }

        if (nodes.Count > 0)
            StartCoroutine(CaptureWithFallback(target, activity, nodes));
        else
            Debug.LogWarning("[StaticCameraManager] RequestSnapshot: no CameraNode found in Transform[]");
    }

    IEnumerator SmartScanRoutine(UserEntity user)
    {
        string prev = "";

        while (true)
        {
            string cur = user.currentActivity;

            if (cur != prev && captureStateSet.Contains(cur))
            {
                prev = cur;

                yield return new WaitForSeconds(GetSettleTime(cur));

                for (int f = 0; f < settleFrames; f++)
                    yield return null;

                if (user.currentActivity != cur)
                {
                    prev = user.currentActivity;
                    continue;
                }

                List<CameraNode> cams = FindCamerasForUser(user);
                if (cams == null || cams.Count == 0)
                {
                    Debug.LogWarning($"[StaticCameraManager] {user.userID}: no registered camera nodes found — " +
                                     "ensure ExperimentRunner or RoomArea has called RegisterRoomCameras()");
                    prev = user.currentActivity;
                    continue;
                }

                yield return StartCoroutine(CaptureWithFallback(user, cur, cams));
            }
            else
            {
                prev = cur;
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    IEnumerator CaptureWithFallback(UserEntity user, string activity, List<CameraNode> cameras)
    {
        for (int retry = 0; retry <= fallbackMaxRetry; retry++)
        {
            List<CameraNode> qualified = ScoreCamerasRanked(user, cameras);

            if (qualified.Count > 0)
            {
                string names = string.Join(", ", qualified.ConvertAll(n =>
                    $"{n.nodeName}({n.lastScore:F2})"));
                Debug.Log($"[StaticCameraManager] {user.userID} | {activity} | nodes: [{names}]");

                yield return StartCoroutine(
                    virtualCameraBrain.ExecuteMultiCapture(user, qualified, activity));
                yield break;
            }

            if (retry < fallbackMaxRetry)
            {
                float best = GetBestScore(cameras);
                Debug.Log($"[StaticCameraManager] {user.userID} | {activity} | " +
                          $"all nodes below threshold (best={best:F2} < {minScoreThreshold})" +
                          $" — Fallback {retry + 1}/{fallbackMaxRetry}");
                yield return new WaitForSeconds(fallbackDelay);
                if (user.currentActivity != activity) yield break;
            }
            else
            {
                Debug.LogWarning($"[StaticCameraManager] {user.userID} | {activity} | " +
                                 "fallback limit reached, capture abandoned — " +
                                 "consider lowering minScoreThreshold or adjusting node positions/FOV/orientation");
            }
        }
    }

    List<CameraNode> ScoreCamerasRanked(UserEntity user, List<CameraNode> cameras)
    {
        Vector3 aimPos = user.GetAimPosition();
        var qualified = new List<CameraNode>();

        foreach (var cam in cameras)
        {
            if (cam == null) continue;

            Vector3 nodePos = cam.transform.position;
            Vector3 toTarget = (aimPos - nodePos).normalized;
            float angle = Vector3.Angle(cam.transform.forward, toTarget);
            float halfFov = cam.fieldOfView * 0.5f;

            if (angle > halfFov)
            {
                cam.lastScore = 0f;
                continue;
            }

            float vis = 1f;
            if (Physics.Linecast(nodePos, aimPos, out RaycastHit hit))
            {
                bool hitUser = hit.transform == user.transform ||
                               hit.transform.IsChildOf(user.transform);
                if (!hitUser) vis = 0f;
            }

            float angleFactor = Mathf.Clamp01(1f - angle / halfFov);
            float dist = Vector3.Distance(nodePos, aimPos);
            float distFactor = Mathf.Clamp01(1f - dist / 10f);

            float score = (vis * 0.5f + angleFactor * 0.3f + distFactor * 0.2f)
                          * cam.scoreMultiplier;
            cam.lastScore = score;

            if (score >= minScoreThreshold)
                qualified.Add(cam);
        }

        qualified.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return qualified;
    }

    List<CameraNode> FindCamerasForUser(UserEntity user)
    {
        if (roomCameras.Count == 0) return null;
        if (roomCameras.Count == 1)
        {
            foreach (var v in roomCameras.Values) return v;
        }

        string nearest = null;
        float minDist = float.MaxValue;
        Vector3 uPos = user.transform.position;

        foreach (var kv in roomCameras)
        {
            Vector3 center = Vector3.zero;
            int cnt = 0;
            foreach (var c in kv.Value)
                if (c != null) { center += c.transform.position; cnt++; }
            if (cnt == 0) continue;
            center /= cnt;

            float d = Vector3.Distance(uPos, center);
            if (d < minDist) { minDist = d; nearest = kv.Key; }
        }

        return nearest != null ? roomCameras[nearest] : null;
    }

    UserEntity GetUserById(string id)
    {
        if (userMom != null && userMom.userID == id) return userMom;
        if (userDad != null && userDad.userID == id) return userDad;
        return null;
    }

    float GetBestScore(List<CameraNode> cameras)
    {
        float best = 0f;
        foreach (var c in cameras)
            if (c != null && c.lastScore > best) best = c.lastScore;
        return best;
    }

    float GetSettleTime(string activity) => activity switch
    {
        "Drink"       => 0.3f,
        "SittingIdle" => 0.5f,
        "Typing"      => 0.4f,
        "Reading"     => 0.4f,
        _             => defaultSettleTime
    };

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in roomCameras)
        {
            foreach (var cam in kv.Value)
            {
                if (cam == null) continue;
                Gizmos.color = cam.lastScore >= minScoreThreshold          ? Color.green
                             : cam.lastScore >= minScoreThreshold * 0.7f   ? Color.yellow
                             : cam.lastScore > 0f                           ? Color.red
                             :                                                Color.gray;
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position, cam.transform.forward * 2f);
            }
        }
    }
}
