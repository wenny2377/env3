using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class RobotPatrol : MonoBehaviour
{
    [Header("Patrol Path")]
    public List<Transform> waypoints;
    public float waitTime = 3.0f;

    private NavMeshAgent _agent;
    private int _currentIndex = 0;
    private bool _isPatrolling = false;
    private System.Action _onReachedCallback; // Used to store actions after reaching (e.g., taking a photo)

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    // --- New: Interrupt and move to target ---
    public void InterruptAndMoveTo(Vector3 destination, System.Action callback)
    {
        _isPatrolling = false; // Stop automatic patrolling
        CancelInvoke("GoToNextWaypoint");

        _onReachedCallback = callback;
        _agent.SetDestination(destination);

        Debug.Log("<color=cyan>Robot:</color> Received emergency command, heading to verification point...");
    }

    public void TogglePatrol()
    {
        _isPatrolling = !_isPatrolling;
        if (_isPatrolling) GoToNextWaypoint();
        else _agent.ResetPath();
        Debug.Log(_isPatrolling ? "Robot: Starting patrol mode" : "Robot: Stopping patrol");
    }

    void Update()
    {
        // Check if arrived (whether patrolling or interrupted task)
        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f)
        {
            // If there's an action to perform after arrival (e.g., take a photo)
            if (_onReachedCallback != null)
            {
                _onReachedCallback.Invoke();
                _onReachedCallback = null; // Clear after execution
            }

            if (!_isPatrolling) return;

            if (!IsInvoking("GoToNextWaypoint"))
            {
                Invoke("GoToNextWaypoint", waitTime);
            }
        }
    }

    void GoToNextWaypoint()
    {
        if (waypoints.Count == 0 || !_isPatrolling) return;
        _agent.SetDestination(waypoints[_currentIndex].position);
        _currentIndex = (_currentIndex + 1) % waypoints.Count;
    }
}