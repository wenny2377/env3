using UnityEngine;

/// <summary>
/// 掛在每個定點相機 Empty Object Pivot 上。
/// 純資料容器，不做任何運算。
/// StaticCameraManager 讀取這裡的 metadata 進行評分。
/// </summary>
public class CameraNode : MonoBehaviour
{
    [Header("節點資訊")]
    public string nodeName = "Camera_01";
    public string roomName = "Kitchen";

    [Header("評分偏好")]
    [Range(0.5f, 2.0f)]
    [Tooltip("手動微調分數倍率。俯角廣視野可給 1.2，角落偏斜可給 0.8")]
    public float scoreMultiplier = 1.0f;

    /// <summary>StaticCameraManager 每次評分後回寫，Scene 視圖 Gizmo 顯示用</summary>
    [HideInInspector] public float lastScore = 0f;

    void OnDrawGizmos()
    {
        // 球體顏色：紅(0分) → 黃 → 綠(1分)
        Gizmos.color = Color.Lerp(Color.red, Color.green, lastScore);
        Gizmos.DrawWireSphere(transform.position, 0.15f);


#if UNITY_EDITOR
        // Scene 視圖標籤：節點名稱 + 即時分數
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.35f,
            $"{nodeName}\n{lastScore:F2}"
        );
#endif
    }
}