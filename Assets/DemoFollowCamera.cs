using UnityEngine;

public class DemoFollowCamera : MonoBehaviour
{
    [Header("Target (auto-found if empty)")]
    public Transform target;

    [Header("Camera Position")]
    public float height = 1.8f;
    public float distance = 2.5f;
    public float sideOffset = 1.2f;
    public float lookAtHeight = 1.4f;

    [Header("Wall Avoidance")]
    public float wallMargin = 0.3f;
    public LayerMask wallMask = Physics.DefaultRaycastLayers;

    [Header("Smoothing")]
    public float smoothSpeed = 8.0f;

    void Start()
    {
        if (target != null) return;

        var mom = GameObject.Find("User_Mom");
        if (mom != null)
        {
            target = mom.transform;
            Debug.Log("[DemoFollowCamera] Auto-found User_Mom");
            return;
        }

        var user = FindObjectOfType<UserEntity>();
        if (user != null)
        {
            target = user.transform;
            Debug.Log($"[DemoFollowCamera] Auto-found {user.userID}");
        }
    }

    public void SetTarget(Transform t)
    {
        target = t;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Desired camera position: behind and to the side
        Vector3 back = -target.forward * distance;
        Vector3 up = Vector3.up * height;
        Vector3 side = target.right * sideOffset;
        Vector3 desired = target.position + back + up + side;

        // Wall avoidance: raycast from target to desired position
        // If wall is in the way, pull camera closer
        Vector3 lookAtPoint = target.position + Vector3.up * lookAtHeight;
        Vector3 dir = (desired - lookAtPoint).normalized;
        float maxDist = Vector3.Distance(lookAtPoint, desired);

        if (Physics.Raycast(lookAtPoint, dir, out RaycastHit hit,
                            maxDist, wallMask))
        {
            // Place camera just in front of the wall
            desired = hit.point - dir * wallMargin;

            // Make sure camera doesn't go below target
            if (desired.y < target.position.y + 0.5f)
                desired.y = target.position.y + 0.5f;
        }

        // Smooth follow
        transform.position = Vector3.Lerp(
            transform.position, desired,
            Time.deltaTime * smoothSpeed);

        // Always look at face level
        transform.LookAt(lookAtPoint);
    }
}