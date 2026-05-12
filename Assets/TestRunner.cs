using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class TestModeRunner : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Test Settings")]
    public float actionHoldTime    = 2.0f;
    public float betweenActionTime = 0.5f;

    static readonly string[] MomSequence = {
        "Drinking", "SittingDrink", "Eating", "Cooking",
        "Opening", "Laying", "Watching", "Reading",
        "Cleaning", "PhoneUse",
    };

    static readonly string[] DadSequence = {
        "Drinking", "SittingDrink", "Eating", "Cooking",
        "Opening", "Laying", "Typing", "DadReading",
        "DadPhone", "DadCleaning",
    };

    bool       _running = false;
    UserEntity _user    = null;
    string     _current = "";
    Coroutine  _routine = null;

    void Start()
    {
        _user = userMom;
        if (_user == null)
            Debug.LogError("[TestMode] userMom is not assigned!");
        else
            Debug.Log($"[TestMode] Ready | user={_user.userID} | " +
                      $"Space=start Tab=switch Esc=stop");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            Stop();
            _user = (_user == userMom) ? userDad : userMom;
            Debug.Log($"[TestMode] Switched to {_user?.userID}");
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_running)
            {
                Debug.Log("[TestMode] Already running, press Esc to stop");
                return;
            }
            if (_user == null)
            {
                Debug.LogError("[TestMode] No user assigned!");
                return;
            }
            Debug.Log($"[TestMode] Starting sequence for {_user.userID}");
            _routine = StartCoroutine(RunSequence(_user));
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Stop();
            return;
        }
    }

    void Stop()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        if (_user != null)
        {
            _user.ResetBusy();
            StartCoroutine(_user.ReturnToStanding());
        }
        _running = false;
        _current = "";
        Debug.Log("[TestMode] Stopped");
    }

    IEnumerator RunSequence(UserEntity user)
    {
        _running = true;

        // Wait 2 frames for NavMeshAgent to fully initialize
        yield return null;
        yield return null;

        string[] seq = (user == userMom) ? MomSequence : DadSequence;
        Debug.Log($"[TestMode] Sequence start | {user.userID} | " +
                  $"{seq.Length} actions | IsBusy={user.IsBusy}");

        for (int i = 0; i < seq.Length; i++)
        {
            if (!_running) yield break;

            string action = seq[i];
            _current = action;

            Debug.Log($"[TestMode] [{i+1}/{seq.Length}] {action} | " +
                      $"pos={user.transform.position} | " +
                      $"IsBusy={user.IsBusy}");

            user.lastAssignedActivity = action;
            user.ResetBusy();

            yield return StartCoroutine(user.SwitchActivity(action));

            Debug.Log($"[TestMode] [{i+1}/{seq.Length}] {action} done");

            yield return new WaitForSeconds(actionHoldTime);

            user.ResetBusy();
            yield return StartCoroutine(user.ReturnToStanding());
            yield return new WaitForSeconds(betweenActionTime);
        }

        _running = false;
        _current = "";
        Debug.Log($"[TestMode] Sequence complete | {user.userID}");
    }

    void OnGUI()
    {
        string userName = _user?.userID ?? "None";
        string status   = _running ? $"Running: {_current}" : "idle";
        GUI.Label(new Rect(10, 50, 700, 22),
            $"[TestMode] {userName} | {status} | " +
            $"Space=start  Tab=switch  Esc=stop");
    }
}