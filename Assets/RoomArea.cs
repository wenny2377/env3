using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoomArea — 房間觸發區域
///
/// 職責：
///   1. 角色進入 BoxCollider 時，把該房間的 CameraNode 清單注冊到 StaticCameraManager
///   2. 讓 ProxyExportManager.IdentifyRoom() 能透過 OverlapSphere 查到房間名稱
///
/// autoFetchByRoomName：
///   勾選 → Start() 自動找場景中 CameraNode.roomName == 此 roomName 的節點
///   不勾 → 手動把節點拖入 cameraPivots 陣列
///
/// ignoreTagCheck：
///   勾選（推薦）→ 任何 Collider 進入都觸發，不依賴 Tag
///   不勾 → 只有 Tag="User" 的物件觸發
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("房間設定")]
    public string roomName = "LivingRoom";

    [Header("自動收集相機節點")]
    [Tooltip("勾選：自動找場景中 roomName 相同的 CameraNode\n不勾：手動拖入下方 cameraPivots")]
    public bool autoFetchByRoomName = true;
    public Transform[] cameraPivots;

    [Header("調試選項")]
    [Tooltip("勾選：任何碰撞體都觸發（推薦）\n不勾：只有 Tag=User 的物件觸發")]
    public bool ignoreTagCheck = true;
    public Color areaColor = new Color(0.1f, 1f, 0.1f, 0.2f);

    void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        if (autoFetchByRoomName) FetchNodesByRoomName();
    }

    void FetchNodesByRoomName()
    {
        var allNodes = FindObjectsOfType<CameraNode>();
        var matched = new List<Transform>();
        foreach (var n in allNodes)
            if (n.roomName == roomName) matched.Add(n.transform);
        cameraPivots = matched.ToArray();

        if (cameraPivots.Length > 0)
            Debug.Log($"[RoomArea] {roomName} 綁定 {cameraPivots.Length} 個虛擬節點");
        else
            Debug.LogWarning($"[RoomArea] {roomName} 找不到 CameraNode！確認 roomName 完全一致");
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[RoomArea] {other.name} 進入 {roomName}");
        if (!ignoreTagCheck && !other.CompareTag("User")) return;

        UserEntity user = other.GetComponentInParent<UserEntity>();
        if (user == null) return;

        if (StaticCameraManager.Instance == null)
        {
            Debug.LogError("[RoomArea] StaticCameraManager.Instance 為 null");
            return;
        }

        var nodeList = new List<CameraNode>();
        foreach (var t in cameraPivots)
        {
            if (t == null) continue;
            var n = t.GetComponent<CameraNode>();
            if (n != null) nodeList.Add(n);
        }
        if (nodeList.Count > 0)
            StaticCameraManager.Instance.RegisterRoomCameras(roomName, nodeList);
    }

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