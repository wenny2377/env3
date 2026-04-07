using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// PickupController — game-style pickup.
/// When robot arrives at furniture, finds the matching object,
/// attaches it to the robot, walks to user, then detaches.
/// Attach to the robot GameObject.
/// </summary>
public class PickupController : MonoBehaviour
{
    [Header("Pickup Settings")]
    [Tooltip("Objects the robot can pick up. Name must match dynamic object label.")]
    public List<GameObject> pickableObjects = new List<GameObject>();

    [Tooltip("Where the held object sits on the robot (local position)")]
    public Vector3 holdOffset = new Vector3(0f, 1.2f, 0.4f);

    [Header("Delivery")]
    [Tooltip("Who to deliver the object to after pickup")]
    public Transform deliveryTarget;

    [Tooltip("Stop this far from delivery target")]
    public float deliveryStopDistance = 1.2f;

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";

    UnityEngine.AI.NavMeshAgent _agent;
    GameObject _heldObject;

    void Start()
    {
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
    }

    public void TryPickup(string navLabel)
    {
        GameObject target = FindObjectByLabel(navLabel);
        if (target == null)
        {
            Debug.Log($"[Pickup] No pickable object found near '{navLabel}'");
            return;
        }
        StartCoroutine(PickupSequence(target));
    }

    IEnumerator PickupSequence(GameObject obj)
    {
        Debug.Log($"[Pickup] Picking up: {obj.name}");

        obj.transform.SetParent(transform);
        obj.transform.localPosition = holdOffset;
        obj.transform.localRotation = Quaternion.identity;
        _heldObject = obj;

        if (deliveryTarget != null)
        {
            Vector3 dest = deliveryTarget.position;
            _agent.SetDestination(dest);

            while (_agent.remainingDistance > deliveryStopDistance ||
                   _agent.pathPending)
                yield return new WaitForSeconds(0.2f);

            Debug.Log($"[Pickup] Delivered {obj.name} to {deliveryTarget.name}");

            obj.transform.SetParent(null);
            obj.transform.position = deliveryTarget.position + deliveryTarget.forward * 0.5f;
            _heldObject = null;

            yield return StartCoroutine(NotifyDelivered(obj.name));
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
            obj.transform.SetParent(null);
            _heldObject = null;
        }
    }

    GameObject FindObjectByLabel(string label)
    {
        foreach (var obj in pickableObjects)
        {
            if (obj == null) continue;
            if (obj.name.ToLower().Contains(label.ToLower()))
                return obj;
        }
        return null;
    }

    IEnumerator NotifyDelivered(string label)
    {
        string body = $"{{\"label\":\"{label}\",\"status\":\"delivered\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/delivery_complete", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }
}