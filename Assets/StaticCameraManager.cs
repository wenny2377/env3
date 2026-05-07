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

    // Core actions that trigger camera capture.
    // Opening, PickingUp, PuttingDown, Walking, Standing
    // are excluded — they are not evaluated by BG1.
    [Header("Capture Target States (case-sensitive, comma-separated)")]
    public string captureStates =
        "Drinking,SittingDrink,Eating,Cooking,Laying," +
        "Watching,Reading,Cleaning,PhoneUse,Typing";

    [Header("Capture Timing")]
    public float defaultSettleTime = 0.4f;
    public int   settleFrames      = 2;
    public float captureCooldown   = 3f;

    [Header("Dependencies (auto-found if left empty)")]
    public VirtualCameraBrain virtualCameraBrain;

    HashSet<string>                      captureStateSet;
    Dictionary<string, List<CameraNode>> roomCameras = new();

    bool _isCapturing = false;

    void Start()
    {
        captureStateSet = new HashSet<string>(
            captureStates.Split(','));

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

        Debug.Log($"[SCM] Started | captureStates=[{captureStates}]");
    }

    // Press T to force-capture userMom (debug)
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T)) return;
        if (userMom == null)
        {
            Debug.LogWarning("[SCM-T] userMom is null");
            return;
        }
        var cams = FindCamerasForUser(userMom);
        if (cams == null || cams.Count == 0)
        {
            Debug.LogWarning("[SCM-T] No cameras found");
            return;
        }
        Debug.Log("[SCM-T] Force capture: Drinking");
        StartCoroutine(CaptureWithBestNodes(userMom, "Drinking", cams));
    }

    public void RegisterRoomCameras(
        string roomName, List<CameraNode> cameras)
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

    // ── Core scan loop ───────────────────────────────────────────
    IEnumerator SmartScanRoutine(UserEntity user)
    {
        string lastCaptured = "";
        float  cooldown     = 0f;

        Debug.Log($"[SCM] SmartScanRoutine started for {user.userID}");

        while (true)
        {
            string cur  = user.currentActivity;
            cooldown   -= 0.1f;

            bool inSet      = captureStateSet.Contains(cur);
            bool isNew      = cur != lastCaptured;
            bool notCooling = cooldown <= 0f;

            if (inSet && isNew && notCooling)
            {
                lastCaptured = cur;
                cooldown     = captureCooldown;

                Debug.Log($"[SCM] {user.userID} | {cur} detected, " +
                          $"settling {GetSettleTime(cur)}s...");

                yield return new WaitForSeconds(GetSettleTime(cur));
                for (int f = 0; f < settleFrames; f++)
                    yield return null;

                if (!captureStateSet.Contains(user.currentActivity))
                {
                    Debug.Log($"[SCM] {user.userID} | changed during settle, skip.");
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                // Wait for any in-progress capture
                float lockWait = 0f;
                while (_isCapturing && lockWait < 15f)
                {
                    yield return new WaitForSeconds(0.1f);
                    lockWait += 0.1f;
                }
                if (_isCapturing)
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

                // Use ground truth label if available
                string label =
                    !string.IsNullOrEmpty(user.lastAssignedActivity)
                    ? user.lastAssignedActivity : cur;

                Debug.Log($"[SCM] Triggering: {user.userID} | {label}");

                yield return StartCoroutine(
                    CaptureWithBestNodes(user, label, cams));
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    // ── Capture with global lock ─────────────────────────────────
    public IEnumerator CaptureWithBestNodes(
        UserEntity user, string activity,
        List<CameraNode> cameras)
    {
        _isCapturing = true;

        List<CameraNode> ranked = ScoreCamerasRanked(user, cameras);
        List<CameraNode> toUse  = ranked.Count > 0 ? ranked : cameras;

        string names = string.Join(", ",
            toUse.ConvertAll(n => $"{n.nodeName}({n.lastScore:F2})"));
        Debug.Log($"[SCM] {user.userID} | {activity} | VCB: [{names}]");

        if (virtualCameraBrain == null)
        {
            Debug.LogError("[SCM] virtualCameraBrain is null!");
            _isCapturing = false;
            yield break;
        }

        yield return StartCoroutine(
            virtualCameraBrain.ExecuteMultiCapture(user, toUse, activity));

        Debug.Log($"[SCM] Capture done: {user.userID} | {activity}");
        _isCapturing = false;
    }

    // ── Camera scoring ───────────────────────────────────────────
    List<CameraNode> ScoreCamerasRanked(
        UserEntity user, List<CameraNode> cameras)
    {
        Vector3 aimPos = user.GetAimPosition();
        var     scored = new List<CameraNode>();

        foreach (var cam in cameras)
        {
            if (cam == null) continue;

            Vector3 nodePos  = cam.transform.position;
            Vector3 toTarget = (aimPos - nodePos).normalized;
            float   angle    = Vector3.Angle(cam.transform.forward, toTarget);
            float   halfFov  = cam.fieldOfView * 0.5f;

            if (angle > halfFov)
            {
                cam.lastScore = 0f;
                scored.Add(cam);
                continue;
            }

            float vis = 1f;
            if (Physics.Linecast(nodePos, aimPos, out RaycastHit hit))
            {
                bool hitUser = hit.transform == user.transform
                            || hit.transform.IsChildOf(user.transform);
                if (!hitUser) vis = 0.3f;
            }

            float angleFactor = Mathf.Clamp01(1f - angle / halfFov);
            float dist        = Vector3.Distance(nodePos, aimPos);
            float distFactor  = Mathf.Clamp01(1f - dist / 10f);

            cam.lastScore =
                (vis * 0.5f + angleFactor * 0.3f + distFactor * 0.2f)
                * cam.scoreMultiplier;

            scored.Add(cam);
        }

        scored.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return scored;
    }

    // ── Room / user helpers ──────────────────────────────────────
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

    UserEntity GetUserById(string id)
    {
        if (userMom != null && userMom.userID == id) return userMom;
        if (userDad != null && userDad.userID == id) return userDad;
        return null;
    }

    float GetSettleTime(string activity) => activity switch
    {
        "Drinking"     => 0.3f,
        "SittingDrink" => 0.4f,
        "Eating"       => 0.5f,
        "Cooking"      => 0.4f,
        "Laying"       => 0.5f,
        "Watching"     => 0.4f,
        "Reading"      => 0.4f,
        "Cleaning"     => 0.4f,
        "PhoneUse"     => 0.3f,
        "Typing"       => 0.4f,
        // Choice B: also capture these transitional actions
        "Opening"      => 0.2f,
        "PickingUp"    => 0.2f,
        "PuttingDown"  => 0.2f,
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
                    cam.lastScore >= 0.6f ? Color.green
                    : cam.lastScore >= 0.3f ? Color.yellow
                    : cam.lastScore > 0f   ? Color.red
                    :                        Color.gray;
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position,
                               cam.transform.forward * 2f);
            }
    }
}