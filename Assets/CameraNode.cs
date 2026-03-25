using UnityEngine;

public class CameraNode : MonoBehaviour
{
    [Header("Node Identifier")]
    public string nodeName = "Cam_Node_01";

    [Tooltip("Must match RoomArea.roomName\nExamples: LivingRoom / Kitchen / DadRoom")]
    public string roomName = "LivingRoom";

    [Header("Camera Field of View (degrees)")]
    [Tooltip("Represents the camera's field of view angle\nRecommended to match the actual camera FOV\nTypical values: 60 for normal, 90 for wide angle")]
    [Range(20f, 170f)]
    public float fieldOfView = 70f;

    [Header("Score Multiplier (0.5~1.0)")]
    [Tooltip("Adjusts the importance of this node\nUsually set between 0.7~0.85\nSet to 1.0 for highest priority")]
    [Range(0.5f, 1.0f)]
    public float scoreMultiplier = 1.0f;

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