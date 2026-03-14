using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StaticCameraManager — 虛擬節點多視角評分管理器（單例）
///
/// 架構說明：
///   場景有多個 CameraNode（虛擬空物件，不需要 Camera 元件）
///   本腳本持續監控 UserEntity.currentActivity
///   偵測到截圖目標狀態後，對房間內所有節點評分
///   回傳「通過門檻、由高到低排序」的清單
///   交給 VirtualCameraBrain 依序拍攝前 topN 張
///
/// 評分邏輯（ScoreCamerasRanked）：
///   Step 1 — FOV 硬截斷：angle > fieldOfView/2 → score=0，直接排除
///            → 確保真實相機移到節點位置後，角色一定在畫面內
///   Step 2 — Visibility：Linecast 遮擋 → vis=0
///   Step 3 — Angle Factor：以 halfFov 歸一化（0°=1, halfFov°=0）
///   Step 4 — Distance Factor：10m 線性遞減
///   最終：(vis×0.5 + angle×0.3 + dist×0.2) × scoreMultiplier
///   通過 minScoreThreshold 的才加入候選清單，由高到低排序
///
/// 相機清單注冊（兩種方式）：
///   A. ExperimentRunner.Start() → RegisterRoomCameras()（推薦）
///   B. RoomArea.OnTriggerEnter() → RegisterRoomCameras()（自動）
/// </summary>
public class StaticCameraManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // 單例
    // ══════════════════════════════════════════════════════

    public static StaticCameraManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ══════════════════════════════════════════════════════
    // Inspector 欄位
    // ══════════════════════════════════════════════════════

    [Header("監控對象（必填）")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("截圖目標狀態（區分大小寫，逗號分隔）")]
    [Tooltip("進入這些 Activity 才觸發截圖\nDrink,SittingIdle,Typing,Reading")]
    public string captureStates = "Drink,SittingIdle,Typing,Reading";

    [Header("截圖時機")]
    [Tooltip("進入目標狀態後等幾秒（讓動畫 loop 穩定）\n各狀態有獨立值，此為 fallback 預設")]
    public float defaultSettleTime = 0.4f;

    [Tooltip("穩定後額外等幾幀（避免 Animator 第一幀抖動）")]
    public int settleFrames = 2;

    [Header("相機評分門檻")]
    [Tooltip("低於此分數的節點排除（不進入拍攝候選清單）\n建議 0.45~0.60\n" +
             "設太高：全部節點都被排除進 Fallback\n" +
             "設太低：品質很差的視角也會拍攝")]
    [Range(0f, 1f)]
    public float minScoreThreshold = 0.50f;

    [Tooltip("全部節點都低於門檻時，等幾秒重評")]
    public float fallbackDelay = 0.5f;

    [Tooltip("Fallback 最多幾次後放棄本次截圖")]
    public int fallbackMaxRetry = 3;

    [Header("依賴元件（留空自動搜尋）")]
    public VirtualCameraBrain virtualCameraBrain;

    // ══════════════════════════════════════════════════════
    // 私有成員
    // ══════════════════════════════════════════════════════

    HashSet<string> captureStateSet;
    Dictionary<string, List<CameraNode>> roomCameras
        = new Dictionary<string, List<CameraNode>>();

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        captureStateSet = new HashSet<string>(captureStates.Split(','));

        if (virtualCameraBrain == null)
            virtualCameraBrain = FindObjectOfType<VirtualCameraBrain>();

        if (virtualCameraBrain == null)
            Debug.LogError("[StaticCameraManager] 找不到 VirtualCameraBrain！\n" +
                           "請建立空物件並掛上 VirtualCameraBrain.cs，" +
                           "並把唯一的 Camera 拖入 mainCamera 欄位");

        if (userMom != null) StartCoroutine(SmartScanRoutine(userMom));
        else Debug.LogWarning("[StaticCameraManager] userMom 未設定");

        if (userDad != null) StartCoroutine(SmartScanRoutine(userDad));
        else Debug.LogWarning("[StaticCameraManager] userDad 未設定");
    }

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 注冊房間的虛擬相機節點清單
    /// ExperimentRunner.Start() 或 RoomArea.OnTriggerEnter() 呼叫
    /// </summary>
    public void RegisterRoomCameras(string roomName, List<CameraNode> cameras)
    {
        if (cameras == null || cameras.Count == 0)
        {
            Debug.LogWarning($"[StaticCameraManager] RegisterRoomCameras '{roomName}' 清單為空");
            return;
        }
        roomCameras[roomName] = cameras;
        Debug.Log($"[StaticCameraManager] 注冊 '{roomName}'：{cameras.Count} 個虛擬節點");
    }

    /// <summary>
    /// RoomArea.cs 相容介面（傳 Transform[]）
    /// 自動從 Transform 上取 CameraNode 元件後轉交
    /// </summary>
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
            Debug.LogWarning("[StaticCameraManager] RequestSnapshot: Transform[] 中找不到 CameraNode");
    }

    // ══════════════════════════════════════════════════════
    // 核心監控迴圈
    // ══════════════════════════════════════════════════════

    IEnumerator SmartScanRoutine(UserEntity user)
    {
        string prev = "";

        while (true)
        {
            string cur = user.currentActivity;

            if (cur != prev && captureStateSet.Contains(cur))
            {
                prev = cur;

                // 等動畫穩定
                yield return new WaitForSeconds(GetSettleTime(cur));

                // 額外等幀
                for (int f = 0; f < settleFrames; f++)
                    yield return null;

                // 確認還在同一狀態（未被提早切走）
                if (user.currentActivity != cur)
                {
                    prev = user.currentActivity;
                    continue;
                }

                // 找該用戶所在房間的節點清單
                List<CameraNode> cams = FindCamerasForUser(user);
                if (cams == null || cams.Count == 0)
                {
                    Debug.LogWarning($"[StaticCameraManager] {user.userID}：" +
                                     "找不到已注冊的相機節點\n" +
                                     "請確認 ExperimentRunner 或 RoomArea 已呼叫 RegisterRoomCameras()");
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

    // ══════════════════════════════════════════════════════
    // 截圖流程（含 Fallback）
    // ══════════════════════════════════════════════════════

    IEnumerator CaptureWithFallback(UserEntity user, string activity, List<CameraNode> cameras)
    {
        for (int retry = 0; retry <= fallbackMaxRetry; retry++)
        {
            // ScoreCamerasRanked 回傳「通過門檻、由高到低排序」的清單
            List<CameraNode> qualified = ScoreCamerasRanked(user, cameras);

            if (qualified.Count > 0)
            {
                string names = string.Join(", ", qualified.ConvertAll(n =>
                    $"{n.nodeName}({n.lastScore:F2})"));
                Debug.Log($"[StaticCameraManager] ✓ {user.userID} | {activity} | " +
                          $"拍攝節點: [{names}]");

                // 傳入排序好的清單，VirtualCameraBrain 取前 topN 張
                yield return StartCoroutine(
                    virtualCameraBrain.ExecuteMultiCapture(user, qualified, activity));
                yield break;
            }

            if (retry < fallbackMaxRetry)
            {
                float best = GetBestScore(cameras);
                Debug.Log($"[StaticCameraManager] {user.userID} | {activity} | " +
                          $"全部節點分數不足 (best={best:F2} < {minScoreThreshold})" +
                          $" → Fallback {retry + 1}/{fallbackMaxRetry}");
                yield return new WaitForSeconds(fallbackDelay);
                if (user.currentActivity != activity) yield break;
            }
            else
            {
                Debug.LogWarning($"[StaticCameraManager] {user.userID} | {activity} | " +
                                 "Fallback 超限，放棄本次截圖\n" +
                                 "建議：降低 minScoreThreshold 或調整節點位置/FOV/朝向");
            }
        }
    }

    // ══════════════════════════════════════════════════════
    // 相機評分 → 回傳「通過門檻、由高到低排序」的清單
    //
    // 多視角架構的關鍵改變：
    //   舊版：只取分數最高的 1 台 → CameraNode（單一結果）
    //   新版：回傳所有通過門檻的節點 → List<CameraNode>（排序清單）
    //   VirtualCameraBrain.topN 決定實際拍幾台
    //   這樣的分工讓「拍幾台」的決定權在 VirtualCameraBrain，
    //   而「哪些節點有資格」由 StaticCameraManager 決定
    // ══════════════════════════════════════════════════════

    List<CameraNode> ScoreCamerasRanked(UserEntity user, List<CameraNode> cameras)
    {
        Vector3 aimPos = user.GetAimPosition();   // 胸口 1.2m
        var qualified = new List<CameraNode>();

        foreach (var cam in cameras)
        {
            if (cam == null) continue;

            Vector3 nodePos = cam.transform.position;
            Vector3 toTarget = (aimPos - nodePos).normalized;
            float angle = Vector3.Angle(cam.transform.forward, toTarget);
            float halfFov = cam.fieldOfView * 0.5f;

            // ── Step 1：FOV 硬截斷 ────────────────────────────
            // 角色超出視角範圍 → 真實相機在此位置必然拍不到角色
            if (angle > halfFov)
            {
                cam.lastScore = 0f;
                continue;
            }

            // ── Step 2：Visibility（Linecast 遮擋）────────────
            float vis = 1f;
            if (Physics.Linecast(nodePos, aimPos, out RaycastHit hit))
            {
                bool hitUser = hit.transform == user.transform ||
                               hit.transform.IsChildOf(user.transform);
                if (!hitUser) vis = 0f;
            }

            // ── Step 3：Angle Factor（FOV 內連續）─────────────
            // 用 halfFov 歸一化：正中心=1.0，FOV 邊緣=0.0
            float angleFactor = Mathf.Clamp01(1f - angle / halfFov);

            // ── Step 4：Distance Factor（10m 線性遞減）────────
            float dist = Vector3.Distance(nodePos, aimPos);
            float distFactor = Mathf.Clamp01(1f - dist / 10f);

            // ── 加權總分 ───────────────────────────────────────
            float score = (vis * 0.5f + angleFactor * 0.3f + distFactor * 0.2f)
                          * cam.scoreMultiplier;
            cam.lastScore = score;

            if (score >= minScoreThreshold)
                qualified.Add(cam);
        }

        // 由高到低排序，VirtualCameraBrain 取前 topN 個
        qualified.Sort((a, b) => b.lastScore.CompareTo(a.lastScore));
        return qualified;
    }

    // ══════════════════════════════════════════════════════
    // 輔助方法
    // ══════════════════════════════════════════════════════

    List<CameraNode> FindCamerasForUser(UserEntity user)
    {
        if (roomCameras.Count == 0) return null;
        if (roomCameras.Count == 1)
        {
            foreach (var v in roomCameras.Values) return v;
        }

        // 多房間：以相機群重心找最近的房間
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
        "Drink" => 0.3f,
        "SittingIdle" => 0.5f,
        "Typing" => 0.4f,
        "Reading" => 0.4f,
        _ => defaultSettleTime
    };

    // ══════════════════════════════════════════════════════
    // Gizmo：Play Mode 顯示節點評分顏色
    //   綠  = 通過門檻（會被拍攝）
    //   黃  = 接近門檻
    //   紅  = 分數不足
    //   灰  = FOV 截斷（score=0）
    // ══════════════════════════════════════════════════════

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var kv in roomCameras)
        {
            foreach (var cam in kv.Value)
            {
                if (cam == null) continue;
                Gizmos.color = cam.lastScore >= minScoreThreshold ? Color.green
                             : cam.lastScore >= minScoreThreshold * 0.7f ? Color.yellow
                             : cam.lastScore > 0f ? Color.red
                             : Color.gray;   // FOV 截斷
                Gizmos.DrawWireSphere(cam.transform.position, 0.15f);
                Gizmos.DrawRay(cam.transform.position, cam.transform.forward * 2f);
            }
        }
    }
}