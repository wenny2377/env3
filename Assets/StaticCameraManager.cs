using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 定點相機管理器（最終版）
/// 職責：評分 → 決策張數 → 交給 VirtualCameraBrain 執行
/// 不依賴機器人，支援多用戶同時監控互不干擾
/// </summary>
public class StaticCameraManager : MonoBehaviour
{
    public static StaticCameraManager Instance;

    [Header("掃描設定")]
    public LayerMask userLayer;
    public float scanInterval = 0.5f;

    [Header("動態張數閾值（Inspector 可調）")]
    [Tooltip("最高分 >= 此值 → 只傳 1 張")]
    public float singleViewThreshold = 0.85f;

    [Tooltip("最高分在此值以上 → 傳 2 張；低於此值 → 傳 3 張")]
    public float dualViewThreshold = 0.60f;

    [Tooltip("最多傳出張數上限，建議維持 3")]
    public int maxOutputImages = 3;

    // 每個 userID 獨立協程，不互相覆蓋
    private Dictionary<string, Coroutine> _userRoutines = new Dictionary<string, Coroutine>();

    void Awake() { Instance = this; }

    // ─────────────────────────────────────────────
    // 外部入口：RoomArea 呼叫此方法
    // 接收 Transform[]，內部轉換成 CameraNode[]
    // ─────────────────────────────────────────────
    public void RequestSnapshot(Transform[] roomPivots, string roomName, string userID, string activity)
    {
        // 第一關：確認入口
        Debug.Log($"<color=orange>【STEP 1】RequestSnapshot 被呼叫！房間：{roomName}, 用戶：{userID}</color>");

        // 第二關：檢查用戶物件
        GameObject userObj = GameObject.Find(userID);
        if (userObj == null) {
            // 嘗試去掉 "User_" 前綴再找一次
            userObj = GameObject.Find(userID.Replace("User_", ""));
        }

        if (userObj == null) {
            Debug.LogError($"<color=red>【FAIL】找不到用戶物件！請確認 Hierarchy 裡的名稱是否叫：{userID}</color>");
            return;
        }
        Debug.Log($"<color=green>【SUCCESS】找到用戶物件：{userObj.name}</color>");

        // 第三關：檢查 CameraNode
        List<CameraNode> nodes = new List<CameraNode>();
        foreach (var pivot in roomPivots) {
            if (pivot == null) continue;
            CameraNode node = pivot.GetComponent<CameraNode>();
            if (node != null) nodes.Add(node);
        }

        if (nodes.Count == 0) {
            Debug.LogError($"<color=red>【FAIL】房間 {roomName} 裡的 Pivots 都沒有掛 CameraNode.cs 腳本！</color>");
            return;
        }
        Debug.Log($"<color=green>【SUCCESS】找到 {nodes.Count} 個有效節點，準備啟動協程...</color>");

        StartMonitoring(userObj.GetComponent<UserEntity>(), nodes);
    }

    // ─────────────────────────────────────────────
    // 開始監控：停舊的同 userID 協程，啟動新的
    // ─────────────────────────────────────────────
    public void StartMonitoring(UserEntity targetUser, List<CameraNode> nodes)
    {
        if (_userRoutines.TryGetValue(targetUser.userID, out Coroutine old) && old != null)
            StopCoroutine(old);

        _userRoutines[targetUser.userID] = StartCoroutine(SmartScanRoutine(targetUser, nodes));
        Debug.Log($"<color=white>[Surveillance]</color> 監控 {targetUser.userID}，{nodes.Count} 個節點");
    }

    // ─────────────────────────────────────────────
    // 核心掃描協程
    // ─────────────────────────────────────────────
    private IEnumerator SmartScanRoutine(UserEntity target, List<CameraNode> nodes)
    {
        while (true)
        {
            if (target.currentActivity == "Idle")
            {
                yield return new WaitForSeconds(scanInterval);
                continue;
            }

            // 1. 取得瞄準點
            Vector3 aimPos = GetAimPosition(target);
            Debug.Log($"[Debug] 目標 {target.userID} 的座標在: {aimPos}");

            // 2. 對所有節點評分
            List<CameraScore> scored = new List<CameraScore>();
            foreach (CameraNode node in nodes)
            {
                if (node == null) continue;
                CameraScore cs = CalculateNodeScore(node, aimPos, target);
                node.lastScore = cs.score; // 回寫讓 Gizmo 即時顯示
                scored.Add(cs);
                DrawDebugRay(node.transform.position, aimPos, cs.score);
            }

            // 3. 過濾 score=0，排序
            var valid = scored.Where(n => n.score > 0f).OrderByDescending(n => n.score).ToList();

            if (valid.Count == 0)
            {
                Debug.Log($"<color=red>[Camera]</color> {target.userID} 所有節點遮擋，重新等待");
                yield return new WaitForSeconds(scanInterval);
                continue;
            }

            // 4. 動態決定張數
            float best = valid[0].score;
            List<CameraNode> selected = new List<CameraNode>();

            if (best >= singleViewThreshold || valid.Count == 1)
            {
                selected.Add(valid[0].node);
                Debug.Log($"<color=lime>[SINGLE]</color> {target.userID} 最高分 {best:F2} → 1 張");
            }
            else if (best >= dualViewThreshold)
            {
                selected.Add(valid[0].node);
                if (valid.Count >= 2) selected.Add(valid[1].node);
                Debug.Log($"<color=yellow>[DUAL]</color> {target.userID} 最高分 {best:F2} → {selected.Count} 張");
            }
            else
            {
                int take = Mathf.Min(maxOutputImages, valid.Count);
                for (int i = 0; i < take; i++) selected.Add(valid[i].node);
                Debug.Log($"<color=orange>[TRIPLE]</color> {target.userID} 最高分 {best:F2} → {selected.Count} 張");
            }

            // 5. 交給 VirtualCameraBrain 執行
            if (VirtualCameraBrain.Instance != null)
                yield return VirtualCameraBrain.Instance.ExecuteCaptureSequence(selected, target, aimPos);
            else
                Debug.LogError("[StaticCameraManager] VirtualCameraBrain.Instance 為 null！");

            _userRoutines.Remove(target.userID);
            yield break;
        }
    }

    // ─────────────────────────────────────────────
    // 評分演算法
    // totalScore = visibility×0.5 + angle×0.3 + distance×0.2
    // ─────────────────────────────────────────────
    private CameraScore CalculateNodeScore(CameraNode node, Vector3 aimPos, UserEntity target)
    {
        Vector3 nodePos = node.transform.position;

        // A. 三點 Raycast 可視性（命中幾條）
        Vector3[] offsets = { Vector3.zero, Vector3.up * 0.5f, Vector3.down * 0.5f };
        int hits = 0;
        foreach (var offset in offsets)
        {
            Vector3 sample = aimPos + offset;
            Vector3 dir    = (sample - nodePos).normalized;
            float   maxDist = Vector3.Distance(nodePos, sample) + 0.5f;
            if (Physics.Raycast(nodePos, dir, out RaycastHit hit, maxDist, userLayer))
                if (hit.transform.IsChildOf(target.transform) || hit.transform == target.transform)
                    hits++;
        }

        if (hits == 0) return new CameraScore(node, 0f); // 完全遮擋，淘汰

        float visibility = hits / 3f;                                                          // 0.33 / 0.67 / 1.0

        // B. Dot Product 角度（相機朝向 vs 目標方向）
        float angle = Vector3.Dot(node.transform.forward, (aimPos - nodePos).normalized);      // -1 ~ +1

        // C. 距離分數（3m 最佳，10m 衰減半徑）
        float dist     = Vector3.Distance(nodePos, aimPos);
        float distScore = Mathf.Clamp01(1f - Mathf.Abs(dist - 3f) / 10f);

        // D. 加權合併 × 節點自訂倍率
        float raw   = visibility * 0.5f + angle * 0.3f + distScore * 0.2f;
        float total = Mathf.Clamp01(raw * node.scoreMultiplier);

        return new CameraScore(node, total);
    }

    // ─────────────────────────────────────────────
    // 輔助
    // ─────────────────────────────────────────────
    private Vector3 GetAimPosition(UserEntity target)
    {
        // 直接呼叫 UserEntity 的 helper，它知道哪個子模型目前啟用中
        return target.GetAimPosition();
    }

    private void DrawDebugRay(Vector3 from, Vector3 to, float score)
    {
        Debug.DrawLine(from, to, Color.Lerp(Color.red, Color.green, score), 0.5f);
        if (score >= dualViewThreshold) CreateLaser(from, to);
    }

    private void CreateLaser(Vector3 start, Vector3 end)
    {
        GameObject go = new GameObject("VLM_Laser");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = lr.endWidth = 0.04f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = Color.cyan;
        Destroy(go, 2f);
    }

    private class CameraScore
    {
        public CameraNode node;
        public float score;
        public CameraScore(CameraNode n, float s) { node = n; score = s; }
    }
}