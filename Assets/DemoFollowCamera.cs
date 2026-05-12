using UnityEngine;

public class DemoFollowCamera : MonoBehaviour
{
    [Header("Target (auto-found if empty)")]
    public Transform target;

    [Header("Camera Position")]
    [Tooltip("Height above target")]
    public float height      = 2.5f;
    [Tooltip("Distance behind target")]
    public float distance    = 3.0f;
    [Tooltip("Horizontal offset: 0=behind, positive=right side")]
    public float sideOffset  = 0.5f;
    [Tooltip("Look at point height (1.5 = face level)")]
    public float lookAtHeight = 1.5f;

    [Header("Smoothing")]
    public float smoothSpeed = 5.0f;

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
        else
        {
            Debug.LogWarning("[DemoFollowCamera] No target found.");
        }
    }

    public void SetTarget(Transform t)
    {
        target = t;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Position: behind the character based on their facing direction
        Vector3 back    = -target.forward * distance;
        Vector3 up      = Vector3.up * height;
        Vector3 side    = target.right * sideOffset;
        Vector3 desired = target.position + back + up + side;

        transform.position = Vector3.Lerp(
            transform.position, desired,
            Time.deltaTime * smoothSpeed);

        // Look at slightly above target center to see face
        Vector3 lookAt = target.position + Vector3.up * lookAtHeight;
        transform.LookAt(lookAt);
    }
}