using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StaticCameraManager : MonoBehaviour
{
    public static StaticCameraManager Instance;
    public LayerMask userLayer;
    public float scanInterval = 0.5f;

    private bool _isProcessing = false;
    private Transform[] _activeRoomPivots;

    // --- Fix 1: Manage monitoring schedules for multiple users, preventing Mom from being overridden by Dad ---
    private Dictionary<string, Coroutine> _userRoutines = new Dictionary<string, Coroutine>();

    void Awake() { Instance = this; }

    public void RequestSnapshot(Transform[] roomPivots, string roomName, string userID, string activity)
    {
        _activeRoomPivots = roomPivots;
        GameObject userObj = GameObject.Find(userID);

        if (userObj == null)
        {
            string shortName = userID.Replace("User_", "");
            userObj = GameObject.Find(shortName);
        }

        if (userObj != null)
        {
            UserEntity user = userObj.GetComponent<UserEntity>();
            if (user != null)
            {
                Debug.Log($"<color=white>[Surveillance System]</color> Successfully locked onto target: {user.userID}, starting monitoring.");
                StartMonitoring(user);
            }
        }
    }

    public void StartMonitoring(UserEntity targetUser)
    {
        // Removed StopAllCoroutines() and now only stops the old routine for the specific user
        if (_userRoutines.ContainsKey(targetUser.userID))
        {
            if (_userRoutines[targetUser.userID] != null)
                StopCoroutine(_userRoutines[targetUser.userID]);
        }

        _isProcessing = false; // Force unlock to prevent previous tasks from blocking the system
        _userRoutines[targetUser.userID] = StartCoroutine(ScanRoutine(targetUser));
    }

    private IEnumerator ScanRoutine(UserEntity target)
    {
        while (true)
        {
            if (target.currentActivity == "Idle")
            {
                yield return new WaitForSeconds(scanInterval);
                continue;
            }

            if (!_isProcessing && _activeRoomPivots != null && _activeRoomPivots.Length > 0)
            {
                Vector3 aimPos = target.transform.position;
                bool foundActiveModel = false;

                // --- Fix 2: Use a safer model center grab ---
                Renderer currentRend = target.GetComponentInChildren<Renderer>();
                if (currentRend != null && currentRend.gameObject.activeInHierarchy)
                {
                    aimPos = currentRend.bounds.center;
                    foundActiveModel = true;
                }

                if (!foundActiveModel)
                {
                    // If still not found, grab the parent's center and offset it by 1 meter upwards
                    aimPos = target.transform.position + Vector3.up * 1.0f;
                    foundActiveModel = true;
                }

                foreach (Transform pivot in _activeRoomPivots)
                {
                    if (pivot == null) continue;
                    Vector3 dir = aimPos - pivot.position;
                    RaycastHit hit;

                    Debug.DrawLine(pivot.position, aimPos, Color.yellow, 0.2f);

                    if (Physics.Raycast(pivot.position, dir.normalized, out hit, 100f, userLayer))
                    {
                        if (hit.transform.IsChildOf(target.transform) || hit.transform == target.transform)
                        {
                            CreateStrongLaser(pivot.position, aimPos);
                            Debug.Log($"<color=lime>[Lock Successful]</color> Detected {target.userID}'s behavior: {target.currentActivity}, dispatching robot.");

                            TriggerRobotMission(target, aimPos);

                            _userRoutines.Remove(target.userID); // Remove record after completion
                            yield break;
                        }
                    }
                }
            }
            yield return new WaitForSeconds(scanInterval);
        }
    }

    void TriggerRobotMission(UserEntity target, Vector3 actualTargetPos)
    {
        _isProcessing = true;
        GodModeController gmc = FindObjectOfType<GodModeController>();

        if (gmc == null || gmc.robotControl == null) return;

        Vector3 stopPos = actualTargetPos + (gmc.robotControl.transform.position - actualTargetPos).normalized * 1.2f;

        gmc.robotControl.InterruptAndMoveTo(stopPos, () => {
            Debug.Log("<color=cyan>[Robot]</color> Reached target and preparing for snapshot.");
            if (RobotCameraManager.Instance != null)
                RobotCameraManager.Instance.RequestRobotSnapshot(target.userID, target.currentActivity);
            _isProcessing = false; // Unlock the global flag after the task is completed
        });
    }

    private void CreateStrongLaser(Vector3 start, Vector3 end)
    {
        GameObject laser = new GameObject("VLM_Laser_Effect");
        LineRenderer lr = laser.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = 0.08f;
        lr.endWidth = 0.08f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = Color.red;
        lr.endColor = Color.red;
        Destroy(laser, 5.0f);
    }
}