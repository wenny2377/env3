using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("語義設置")]
    public string roomName = "New Room";

    [Header("自動化設定")]
    [Tooltip("若開啟，將自動抓取子物件中所有標記為 MockCamera Tag 的物件作為 Pivots")]
    public bool autoFetchPivotsFromChildren = true;

    [Header("定點感測網路")]
    public Transform[] cameraPivots; // 若沒開啟自動抓取，則手動拖入

    [Header("編輯器視覺化 (Gizmos)")]
    public Color areaColor = new Color(0, 1, 0, 0.3f);
    public bool showAreaInScene = true;

    private void Awake()
    {
        // 自動化優化：如果開啟自動抓取，就省去手動拖拽
        if (autoFetchPivotsFromChildren)
        {
            List<Transform> childPivots = new List<Transform>();
            foreach (Transform child in transform)
            {
                // 這裡檢查 Tag 是否為 MockCamera
                if (child.CompareTag("MockCamera"))
                {
                    childPivots.Add(child);
                }
            }
            if (childPivots.Count > 0) cameraPivots = childPivots.ToArray();
        }
    }

    private void Reset()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 1. 檢查進入的是不是 User
        if (other.CompareTag("User"))
        {
            UserEntity user = other.GetComponent<UserEntity>();

            if (user != null)
            {
                Debug.Log($"<color=green>[RoomArea]</color> {user.userID} 進入 {roomName}，啟動房間感測網路。");

                // 2. 核心改動：不再只傳一個最近的點，而是把該房間所有的相機點傳給 Manager
                if (cameraPivots != null && cameraPivots.Length > 0)
                {
                    if (StaticCameraManager.Instance != null)
                    {
                        // 呼叫優化後的 RequestSnapshot，傳入整個陣列
                        StaticCameraManager.Instance.RequestSnapshot(
                            cameraPivots,  // 傳入陣列，解決妳不想一個一個設定的問題
                            roomName,
                            user.userID,
                            user.currentActivity
                        );
                    }
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showAreaInScene) return;
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.color = areaColor;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.color = new Color(areaColor.r, areaColor.g, areaColor.b, 1.0f);
        Gizmos.DrawWireCube(col.center, col.size);
    }
}