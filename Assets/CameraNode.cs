using UnityEngine;

public class CameraNode : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Unique name for this node.\nExample: LivingRoom_Cam_01")]
    public string nodeName = "Cam_Node_01";

    [Tooltip("Must match RoomArea.roomName exactly.\nExamples: LivingRoom / Kitchen / DadRoom")]
    public string roomName = "LivingRoom";

    [Header("Capture Settings")]
    [Tooltip("Field of view in degrees.\n60 = normal  |  90 = wide-angle")]
    [Range(20f, 170f)]
    public float fieldOfView = 70f;

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
        float len     = 4f;
        Vector3 fwd   = transform.forward;
        Vector3 right = transform.right;
        Vector3 up    = transform.up;

        Vector3 tl = transform.position + (fwd * Mathf.Cos(halfRad) + (-right + up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 tr = transform.position + (fwd * Mathf.Cos(halfRad) + ( right + up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 bl = transform.position + (fwd * Mathf.Cos(halfRad) + (-right - up).normalized * Mathf.Sin(halfRad)) * len;
        Vector3 br = transform.position + (fwd * Mathf.Cos(halfRad) + ( right - up).normalized * Mathf.Sin(halfRad)) * len;

        Gizmos.color = new Color(0f, 1f, 1f, 0.7f);
        Gizmos.DrawLine(transform.position, tl);
        Gizmos.DrawLine(transform.position, tr);
        Gizmos.DrawLine(transform.position, bl);
        Gizmos.DrawLine(transform.position, br);
        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * len);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.3f,
            $"{nodeName}  FOV {fieldOfView}°\nscore {lastScore:F2}");
#endif
    }
}