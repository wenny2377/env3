using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 定點相機管理器（最終版）
/// 職責：評分 → 決策張數 → 交給 VirtualCameraBrain 執行
/// </summary>
public class StaticCameraManager : MonoBehaviour
{
    public static StaticCameraManager Instance;

    [Header("掃描設定")]
    public LayerMask userLayer;
    public float scanInterval = 0.5f;

    [Header("動態張數閾值")]
    [Tooltip("最高分 >= 此值 → 只傳 1 張")]
    public float singleViewThreshold = 0.85f;

    [Tooltip("最高分在此值以上 → 傳 2 張；低於此值 → 傳 3 張")]
    public float dualViewThreshold = 0.60f;

    [Tooltip("最多傳出張數上限")]
    public int maxOutputImages = 3;

    private Dictionary<string, Coroutine> _userRoutines = new Dictionary<string, Coroutine>();

    void Awake() { Instance = this; }

    // ─────────────────────────────────────────────
    // 外部入口：RoomArea 呼叫
    // ─────────────────────────────────────────────
    public void RequestSnapshot(Transform[] roomPivots, string roomName, string userID, string activity)
    {
        Debug.Log($"<color=orange>【STEP 1】RequestSnapshot 被呼叫！房間：{roomName}, 用戶：{userID}</color>");

        GameObject userObj = GameObject.Find(userID);
        if (userObj == null)
            userObj = GameObject.Find(userID.Replace("User_", ""));

        if (userObj == null)
        {
            Debug.LogError($"<color=red>【FAIL】找不到用戶物件：{userID}</color>");
            return;
        }
        Debug.Log($"<color=green>【SUCCESS】找到用戶物件：{userObj.name}</color>");

        List<CameraNode> nodes = new List<CameraNode>();
        foreach (var pivot in roomPivots)
        {
            if (pivot == null) continue;
            CameraNode node = pivot.GetComponent<CameraNode>();
            if (node != null) nodes.Add(node);
        }

        if (nodes.Count == 0)
        {
            Debug.LogError($"<color=red>【FAIL】房間 {roomName} 裡的 Pivots 都沒有掛 CameraNode.cs！</color>");
            return;
        }
        Debug.Log($"<color=green>【SUCCESS】找到 {nodes.Count} 個有效節點</color>");

        // ← FIX: 傳入 roomName
        StartMonitoring(userObj.GetComponent<UserEntity>(), nodes, roomName);
    }

    // ─────────────────────────────────────────────
    // 開始監控
    // ← FIX: 加入 roomName 參數
    // ─────────────────────────────────────────────
    public void StartMonitoring(UserEntity targetUser, List<CameraNode> nodes, string roomName = "")
    {
        if (_userRoutines.TryGetValue(targetUser.userID, out Coroutine old) && old != null)
            StopCoroutine(old);

        // ← FIX: 傳入 roomName
        _userRoutines[targetUser.userID] = StartCoroutine(SmartScanRoutine(targetUser, nodes, roomName));
        Debug.Log($"<color=white>[Surveillance]</color> 監控 {targetUser.userID}，{nodes.Count} 個節點，room={roomName}");
    }

    // ─────────────────────────────────────────────
    // 核心掃描協程
    // ← FIX: 加入 roomName 參數，傳給 ExecuteCaptureSequence
    // ─────────────────────────────────────────────
    private IEnumerator SmartScanRoutine(UserEntity target, List<CameraNode> nodes, string roomName = "")
    {
        while (true)
        {
            if (target.currentActivity == "Idle")
            {
                yield return new WaitForSeconds(scanInterval);
                continue;
            }

            Vector3 aimPos = GetAimPosition(target);
            Debug.Log($"[Debug] 目標 {target.userID} 座標：{aimPos} | room={roomName}");

            List<CameraScore> scored = new List<CameraScore>();
            foreach (CameraNode node in nodes)
            {
                if (node == null) continue;
                CameraScore cs = CalculateNodeScore(node, aimPos, target);
                node.lastScore = cs.score;
                scored.Add(cs);
                DrawDebugRay(node.transform.position, aimPos, cs.score);
            }

            var valid = scored.Where(n => n.score > 0f).OrderByDescending(n => n.score).ToList();

            if (valid.Count == 0)
            {
                Debug.Log($"<color=red>[Camera]</color> {target.userID} 所有節點遮擋，重新等待");
                yield return new WaitForSeconds(scanInterval);
                continue;
            }

            float best     = valid[0].score;
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

            // ← FIX: 傳入 roomName
            if (VirtualCameraBrain.Instance != null)
                yield return VirtualCameraBrain.Instance.ExecuteCaptureSequence(
                    selected, target, aimPos, roomName
                );
            else
                Debug.LogError("[StaticCameraManager] VirtualCameraBrain.Instance 為 null！");

            _userRoutines.Remove(target.userID);
            yield break;
        }
    }

    // ─────────────────────────────────────────────
    // 評分演算法
    // ─────────────────────────────────────────────
    private CameraScore CalculateNodeScore(CameraNode node, Vector3 aimPos, UserEntity target)
    {
        Vector3 nodePos = node.transform.position;

        Vector3[] offsets = { Vector3.zero, Vector3.up * 0.5f, Vector3.down * 0.5f };
        int hits = 0;
        foreach (var offset in offsets)
        {
            Vector3 sample  = aimPos + offset;
            Vector3 dir     = (sample - nodePos).normalized;
            float   maxDist = Vector3.Distance(nodePos, sample) + 0.5f;
            if (Physics.Raycast(nodePos, dir, out RaycastHit hit, maxDist, userLayer))
                if (hit.transform.IsChildOf(target.transform) || hit.transform == target.transform)
                    hits++;
        }

        if (hits == 0) return new CameraScore(node, 0f);

        float visibility = hits / 3f;
        float angle      = Vector3.Dot(node.transform.forward, (aimPos - nodePos).normalized);
        float dist       = Vector3.Distance(nodePos, aimPos);
        float distScore  = Mathf.Clamp01(1f - Mathf.Abs(dist - 3f) / 10f);
        float raw        = visibility * 0.5f + angle * 0.3f + distScore * 0.2f;
        float total      = Mathf.Clamp01(raw * node.scoreMultiplier);

        return new CameraScore(node, total);
    }

    // ─────────────────────────────────────────────
    // 輔助
    // ─────────────────────────────────────────────
    private Vector3 GetAimPosition(UserEntity target) => target.GetAimPosition();

    private void DrawDebugRay(Vector3 from, Vector3 to, float score)
    {
        Debug.DrawLine(from, to, Color.Lerp(Color.red, Color.green, score), 0.5f);
    }

    private class CameraScore
    {
        public CameraNode node;
        public float      score;
        public CameraScore(CameraNode n, float s) { node = n; score = s; }
    }
}