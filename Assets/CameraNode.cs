using UnityEngine;

/// <summary>
/// CameraNode — 虛擬相機節點
///
/// 硬體現實：場景只有一台真實 Camera（VirtualCameraBrain.mainCamera）
/// 模擬真實：房間四角各放一個 CameraNode，代表「如果有相機在這裡的視角」
///
/// 運作方式：
///   StaticCameraManager 對所有節點評分（FOV + 遮擋 + 角度 + 距離）
///   → 取通過門檻的前 topN 個節點
///   → VirtualCameraBrain 把唯一真實相機依序瞬移到每個節點位置
///   → 每個位置渲染一張截圖
///   → 全部收集後一次 POST { image_list: [n 張] }
///   → 模擬 n 台固定相機同時拍攝的效果
///
/// 論文聲稱：
///   「系統配備 4 個固定視角節點，每次行為觸發時自動選取最多 topN 個
///   通過 FOV 與可見度篩選的視角進行多視角識別，模擬真實 IP 相機部署」
///
/// 不需要掛 Camera 元件，只需要 Transform（位置與朝向）
///
/// 放置建議：
///   房間天花板四角，高度 2.2~2.5m
///   Z 軸朝向房間中心（Scene 視窗藍箭頭）
///   Scene 選中時會顯示 FOV 錐形輔助線
/// </summary>
public class CameraNode : MonoBehaviour
{
    [Header("節點識別")]
    public string nodeName = "Cam_Node_01";

    [Tooltip("必須與 RoomArea.roomName 完全一致\n例如：LivingRoom / Study")]
    public string roomName = "LivingRoom";

    [Header("模擬相機 FOV（度）")]
    [Tooltip("超過此半角的目標直接排除（拍出來沒有角色）\n建議與真實相機 FOV 一致\n一般監控相機 60°   廣角 90°")]
    [Range(20f, 170f)]
    public float fieldOfView = 70f;

    [Header("評分加權（0.5~1.0）")]
    [Tooltip("角度較差或視野受限的節點可設 0.7~0.85\n正面最佳角度設 1.0")]
    [Range(0.5f, 1.0f)]
    public float scoreMultiplier = 1.0f;

    /// <summary>StaticCameraManager 每次評分後自動寫入</summary>
    [HideInInspector] public float lastScore = 0f;

    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.12f);
        Gizmos.DrawRay(transform.position, transform.forward * 2f);
    }

    void OnDrawGizmosSelected()
    {
        float halfRad = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float len = 4f;
        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;
        Vector3 up = transform.up;

        Vector3 tl = transform.position + (fwd * Mathf.Cos(halfRad) + (-right + up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 tr = transform.position + (fwd * Mathf.Cos(halfRad) + (right + up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 bl = transform.position + (fwd * Mathf.Cos(halfRad) + (-right - up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 br = transform.position + (fwd * Mathf.Cos(halfRad) + (right - up).normalized * Mathf.Sin(halfRad)) * len;

        Gizmos.color = new Color(0f, 1f, 1f, 0.7f);
        Gizmos.DrawLine(transform.position, tl); Gizmos.DrawLine(transform.position, tr);
        Gizmos.DrawLine(transform.position, bl); Gizmos.DrawLine(transform.position, br);
        Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * len);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.3f,
            $"{nodeName}  FOV {fieldOfView}°\nscore {lastScore:F2}");
#endif
    }
}