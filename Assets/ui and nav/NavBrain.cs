using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

/// <summary>
/// NavBrain — polls /nav_target from Flask and drives the robot NavMeshAgent.
/// Attach to the robot GameObject. Requires NavMeshAgent component.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NavBrain : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";
    public float  pollInterval = 1.0f;

    [Header("Arrival")]
    [Tooltip("Distance to target considered 'arrived'")]
    public float arrivalThreshold = 0.6f;

    [Header("Pickup")]
    [Tooltip("Assign PickupController if pickup is needed after arrival")]
    public PickupController pickupController;

    NavMeshAgent  _agent;
    Vector3       _lastTarget   = Vector3.positiveInfinity;
    string        _lastNavLabel = "";
    bool          _isMoving     = false;

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);
            yield return StartCoroutine(FetchNavTarget());
        }
    }

    IEnumerator FetchNavTarget()
    {
        using var req = UnityWebRequest.Get($"{backendUrl}/nav_target");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        var data = JsonUtility.FromJson<NavResponse>(req.downloadHandler.text);
        if (data == null || data.nav_target == null || data.nav_target.Length < 2)
            yield break;

        Vector3 target = new Vector3(data.nav_target[0], 0f, data.nav_target[1]);

        if (Vector3.Distance(target, _lastTarget) < 0.05f)
            yield break;

        _lastTarget   = target;
        _lastNavLabel = data.nav_label ?? "";

        Debug.Log($"[NavBrain] New target: {_lastNavLabel} -> {target}");

        _agent.SetDestination(target);
        _isMoving = true;
        StartCoroutine(WaitForArrival(target));
    }

    IEnumerator WaitForArrival(Vector3 target)
    {
        while (true)
        {
            yield return new WaitForSeconds(0.2f);

            if (!_agent.pathPending &&
                _agent.remainingDistance <= arrivalThreshold)
            {
                Debug.Log($"[NavBrain] Arrived at {_lastNavLabel}");
                _isMoving = false;

                if (pickupController != null)
                    pickupController.TryPickup(_lastNavLabel);

                yield break;
            }
        }
    }

    [System.Serializable]
    class NavResponse
    {
        public float[] nav_target;
        public string  nav_label;
    }
}