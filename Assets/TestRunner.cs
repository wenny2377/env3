using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class TestModeRunner : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Follow Camera")]
    public DemoFollowCamera followCamera;

    [Header("Test Settings")]
    public float actionHoldTime = 2.0f;
    public float betweenActionTime = 0.5f;

    static readonly string[] MomSequence = {
        "PhoneUse","Opening","Eating", "Cleaning","Drinking", "SittingDrink",  "Cooking",
         "Laying", "Watching", "Reading","StandUp", "Sitting",
         
    };

    static readonly string[] DadSequence = {
        "Drinking", "SittingDrink", "Laying", "Eating", "Cooking",
        "Opening", "Typing", "DadReading",
        "DadPhone", "DadCleaning","StandUp", "Sitting",
    };

    bool _running = false;
    UserEntity _user = null;
    string _current = "";
    Coroutine _routine = null;

    void Start()
    {
        _user = userMom;
        SetCameraTarget(_user);

        if (_user == null)
            Debug.LogError("[TestMode] userMom is not assigned!");
        else
            Debug.Log($"[TestMode] Ready | user={_user.userID} | " +
                      $"Space=start  Tab=switch  Esc=stop");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            Stop();
            _user = (_user == userMom) ? userDad : userMom;
            SetCameraTarget(_user);
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

    void SetCameraTarget(UserEntity user)
    {
        if (followCamera == null || user == null) return;
        followCamera.target = user.transform;
    }

    UserEntity GetOther(UserEntity user) =>
        user == userMom ? userDad : userMom;

    void Stop()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        // Restore both users
        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);

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

        UserEntity other = GetOther(user);

        yield return null;
        yield return null;

        string[] seq = (user == userMom) ? MomSequence : DadSequence;
        Debug.Log($"[TestMode] Sequence start | {user.userID} | " +
                  $"{seq.Length} actions");

        for (int i = 0; i < seq.Length; i++)
        {
            if (!_running) yield break;

            string action = seq[i];
            _current = action;

            // Hide other user during action
            if (other != null) other.gameObject.SetActive(false);
            if (user != null) user.gameObject.SetActive(true);

            Debug.Log($"[TestMode] [{i + 1}/{seq.Length}] {action}");

            user.lastAssignedActivity = action;
            user.ResetBusy();

            yield return StartCoroutine(user.SwitchActivity(action));
            yield return new WaitForSeconds(actionHoldTime);

            user.ResetBusy();
            yield return new WaitForSeconds(betweenActionTime);
        }

        // Restore other user after sequence
        if (other != null) other.gameObject.SetActive(true);

        _running = false;
        _current = "";
        Debug.Log($"[TestMode] Sequence complete | {user.userID}");
    }

    void OnGUI()
    {
        string userName = _user?.userID ?? "None";
        string status = _running ? $"Running: {_current}" : "idle";
        GUI.Label(new Rect(10, 50, 700, 22),
            $"[TestMode] {userName} | {status} | " +
            $"Space=start  Tab=switch  Esc=stop");
    }
}