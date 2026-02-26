using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("Room Settings")]
    public string roomName = "New Room";

    [Header("Auto Setup")]
    [Tooltip("If enabled, automatically collect all child objects tagged 'MockCamera' as pivots")]
    public bool autoFetchPivotsFromChildren = true;

    [Header("Camera Pivot References")]
    public Transform[] cameraPivots; // If auto-fetch is disabled, assign manually

    [Header("Scene Visualization (Gizmos)")]
    public Color areaColor = new Color(0, 1, 0, 0.3f);
    public bool showAreaInScene = true;

    private void Awake()
    {
        // Automatic initialization: collect child pivots if enabled
        if (autoFetchPivotsFromChildren)
        {
            List<Transform> childPivots = new List<Transform>();

            foreach (Transform child in transform)
            {
                // Check if child has the tag "MockCamera"
                if (child.CompareTag("MockCamera"))
                {
                    childPivots.Add(child);
                }
            }

            if (childPivots.Count > 0)
                cameraPivots = childPivots.ToArray();
        }
    }

    private void Reset()
    {
        // Ensure collider is set as trigger
        BoxCollider col = GetComponent<BoxCollider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 1. Check if the entering object is the User
        if (other.CompareTag("User"))
        {
            UserEntity user = other.GetComponent<UserEntity>();

            if (user != null)
            {
                Debug.Log($"<color=green>[RoomArea]</color> {user.userID} entered {roomName}, activating spatial perception...");

                // 2. Core logic: instead of selecting one pivot,
                // send ALL room camera pivots to the Manager
                if (cameraPivots != null && cameraPivots.Length > 0)
                {
                    if (StaticCameraManager.Instance != null)
                    {
                        // Call optimized RequestSnapshot and pass the pivot array
                        StaticCameraManager.Instance.RequestSnapshot(
                            cameraPivots,      // Pass array to avoid manual pivot switching
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