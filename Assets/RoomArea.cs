using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("Room Settings")]
    [Tooltip("Must match CameraNode.roomName exactly.\nExamples: LivingRoom / Kitchen / DadRoom")]
    public string roomName = "LivingRoom";

    [Header("Camera Node Discovery")]
    [Tooltip("On: auto-find CameraNodes whose roomName matches this area.\nOff: assign cameraPivots manually.")]
    public bool autoFetchByRoomName = true;

    [Tooltip("Used only when autoFetchByRoomName is disabled.")]
    public Transform[] cameraPivots;

    [Header("Debug Visualisation")]
    public Color areaColor = new Color(0.1f, 1f, 0.1f, 0.2f);

    List<CameraNode> _cachedNodes;

    void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        _cachedNodes = BuildNodeList();

        if (_cachedNodes.Count == 0)
            Debug.LogWarning($"[RoomArea] '{roomName}' — no CameraNodes found. Check roomName matches exactly.");
        else
            Debug.Log($"[RoomArea] '{roomName}' — cached {_cachedNodes.Count} node(s).");
    }

    void OnTriggerEnter(Collider other)
    {
        UserEntity user = other.GetComponentInParent<UserEntity>();
        if (user == null) return;

        if (StaticCameraManager.Instance == null)
        {
            Debug.LogError("[RoomArea] StaticCameraManager.Instance is null.");
            return;
        }

        if (_cachedNodes == null || _cachedNodes.Count == 0)
        {
            Debug.LogWarning($"[RoomArea] '{roomName}' — no nodes to register for {user.userID}.");
            return;
        }

        StaticCameraManager.Instance.RegisterRoomCameras(roomName, _cachedNodes);
    }

    List<CameraNode> BuildNodeList()
    {
        var result = new List<CameraNode>();

        if (autoFetchByRoomName)
        {
            foreach (CameraNode node in FindObjectsOfType<CameraNode>())
                if (node.roomName == roomName)
                    result.Add(node);
        }
        else
        {
            if (cameraPivots == null) return result;
            foreach (Transform t in cameraPivots)
            {
                if (t == null) continue;
                CameraNode node = t.GetComponent<CameraNode>();
                if (node != null) result.Add(node);
            }
        }

        return result;
    }

    void OnDrawGizmos()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color  = areaColor;
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.color  = new Color(areaColor.r, areaColor.g, areaColor.b, 1f);
        Gizmos.DrawWireCube(col.center, col.size);
    }
}