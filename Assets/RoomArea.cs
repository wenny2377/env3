using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 房間觸發區域（偵錯增強版）
/// 解決子模型分散導致觸發失敗的問題
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("房間設定")]
    public string roomName = "LivingRoom";

    [Header("自動收集相機")]
    public bool autoFetchByRoomName = true;
    public Transform[] cameraPivots;

    [Header("調試選項")]
    public bool ignoreTagCheck = true; // 演示時建議勾選，防止 Tag 沒設好失敗
    public Color areaColor = new Color(0.1f, 1f, 0.1f, 0.2f);

    void Start()
    {
        if (autoFetchByRoomName) FetchNodes();
        
        // 自動確保 BoxCollider 設定正確
        BoxCollider col = GetComponent<BoxCollider>();
        col.isTrigger = true;
    }

    private void FetchNodes()
    {
        CameraNode[] allNodes = FindObjectsOfType<CameraNode>();
        List<Transform> matched = new List<Transform>();
        foreach (CameraNode node in allNodes)
        {
            if (node.roomName == roomName) matched.Add(node.transform);
        }
        cameraPivots = matched.ToArray();
        
        if (cameraPivots.Length > 0)
            Debug.Log($"<color=white>[RoomArea]</color> {roomName} 已綁定 {cameraPivots.Length} 個相機");
        else
            Debug.LogWarning($"<color=yellow>[RoomArea]</color> {roomName} 找不到相機節點！");
    }

    // ─────────────────────────────────────────────
    // 核心觸發邏輯
    // ─────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        // 1. 物理診斷：只要有東西撞到就先噴 Log，幫你確認 Collider 是否有效
        Debug.Log($"<color=yellow>【Physics】{other.name} 撞進了 {roomName} 感應區</color>");

        // 2. 檢查標籤 (Tag)
        if (!ignoreTagCheck && !other.CompareTag("User")) return;

        // 3. 關鍵修正：從碰撞體往上找父物件的 UserEntity
        // 因為你的 Collider 可能在 User_Mom/Mom_Typing 子物件上
        UserEntity user = other.GetComponentInParent<UserEntity>();

        if (user != null)
        {
            Debug.Log($"<color=green>【Trigger】確認為用戶 {user.userID}，目前狀態：{user.currentActivity}</color>");

            if (cameraPivots == null || cameraPivots.Length == 0)
            {
                Debug.LogWarning($"[RoomArea] {roomName} 找不到相機節點，無法拍照");
                return;
            }

            if (StaticCameraManager.Instance != null)
            {
                // 執行拍照流程
                StaticCameraManager.Instance.RequestSnapshot(
                    cameraPivots,
                    roomName,
                    user.userID,
                    user.currentActivity
                );
            }
            else
            {
                Debug.LogError("[RoomArea] 找不到 StaticCameraManager 單例！");
            }
        }
    }

    // ─────────────────────────────────────────────
    // 輔助視覺化
    // ─────────────────────────────────────────────
    void OnDrawGizmos()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = areaColor;
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.color = new Color(areaColor.r, areaColor.g, areaColor.b, 1f);
        Gizmos.DrawWireCube(col.center, col.size);
    }
}