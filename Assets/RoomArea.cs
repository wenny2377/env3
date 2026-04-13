using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class RoomArea : MonoBehaviour
{
    [Header("Room Settings")]
    public string roomName = "LivingRoom";

    [Header("Auto Fetch Camera Nodes")]
    [Tooltip("Enabled: auto-find CameraNodes with matching roomName\nDisabled: manually assign cameraPivots")]
    public bool autoFetchByRoomName = true;
    public Transform[] cameraPivots;

    [Header("Debug Options")]
    [Tooltip("Enabled: any collider triggers (recommended)\nDisabled: only objects tagged User trigger")]
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
            Debug.Log($"[RoomArea] {roomName} bound {cameraPivots.Length} camera node(s)");
        else
            Debug.LogWarning($"[RoomArea] {roomName} no CameraNode found — check roomName matches exactly");
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[RoomArea] {other.name} entered {roomName}");
        if (!ignoreTagCheck && !other.CompareTag("User")) return;

        UserEntity user = other.GetComponentInParent<UserEntity>();
        if (user == null) return;

        if (StaticCameraManager.Instance == null)
        {
            Debug.LogError("[RoomArea] StaticCameraManager.Instance is null");
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
