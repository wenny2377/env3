using System.Collections;
using UnityEngine;

[System.Serializable]
public class BehaviorItem
{
    public string activity = "Drink";
    public GameObject item;
    public GameObject sceneCounterpart;
}

[RequireComponent(typeof(Animator))]
public class UserEntity : MonoBehaviour
{
    [Header("User ID")]
    public string userID = "User_Mom";

    [Header("Spots")]
    public Transform drinkSpot;
    public Transform layingSpot;    // 修改：原本是 sittingSpot
    public Transform readingSpot;   // 新增
    public Transform typingSpot;    // 新增
    public Transform idleSpot;

    [Header("Waypoints")]
    public Transform[] drinkWaypoints;
    public Transform[] layingWaypoints;  // 修改：原本是 sittingWaypoints
    public Transform[] readingWaypoints; // 新增
    public Transform[] typingWaypoints;  // 新增
    public Transform[] idleWaypoints;

    [Header("Movement")]
    public float walkSpeed        = 1.4f;
    public float arrivalThreshold = 0.08f;
    public float rotationSpeed    = 8f;

    [Header("Nodding duration (s)")]
    public float noddingDuration = 1.5f;

    [Header("Held items")]
    public BehaviorItem[] behaviorItems;

    [Header("Animator state names")]
    public string stateIdle    = "Idle";
    public string stateWalk    = "Walk";
    public string stateDrink   = "Drink";
    public string stateLaying  = "Laying";
    public string stateReading = "Reading";
    public string stateTyping  = "Typing";
    public string stateNodding = "Nodding";

    public string currentActivity { get; private set; } = "Idle";
    public bool   IsBusy          { get; private set; } = false;
    public Vector3 GetAimPosition() => transform.position + Vector3.up * 1.2f;

    Animator anim;
    bool isSitting = false;

    void Start()
    {
        anim = GetComponent<Animator>();
        if (behaviorItems != null)
            foreach (var bi in behaviorItems)
            {
                if (bi.item != null)
                    bi.item.SetActive(false);
                if (bi.sceneCounterpart != null)
                    bi.sceneCounterpart.SetActive(true);
            }
        PlayAnim(stateIdle);
    }

    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        switch (activity.ToLower())
        {
            case "drink":
                yield return StartCoroutine(DoDrink());   break;
            case "sit":
            case "laying":
                yield return StartCoroutine(DoLaying());  break;
            case "reading":
                yield return StartCoroutine(DoReading()); break;
            case "typing":
                yield return StartCoroutine(DoTyping());  break;
            case "idle":
                yield return StartCoroutine(DoReturnToIdle()); break;
            default:
                Debug.LogWarning($"[{userID}] Unknown activity: {activity}");
                break;
        }

        IsBusy = false;
    }

    public IEnumerator ReturnToIdle()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToIdle());
        IsBusy = false;
    }

    public IEnumerator Nod()
    {
        PlayAnim(stateNodding);
        yield return new WaitForSeconds(noddingDuration);
        PlayAnim(currentActivity switch
        {
            "Drink"   => stateDrink,
            "Laying"  => stateLaying,
            "Reading" => stateReading,
            "Typing"  => stateTyping,
            _         => stateIdle
        });
    }

    public void SetAnim(string s) => PlayAnim(s);

    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(drinkSpot.position, drinkWaypoints));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drink");
        PlayAnim(stateDrink);
    }

    IEnumerator DoLaying()
    {
        if (layingSpot == null) { Warn("layingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(layingSpot), layingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(layingSpot.forward));
        TeleportToSeat(layingSpot);
        SetActivity("Laying");
        PlayAnim(stateLaying);
    }

    IEnumerator DoReading()
    {
        if (readingSpot == null) { Warn("readingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(readingSpot), readingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(readingSpot.forward));
        TeleportToSeat(readingSpot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoTyping()
    {
        if (typingSpot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(typingSpot), typingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(typingSpot.forward));
        TeleportToSeat(typingSpot);
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoReturnToIdle()
    {
        if (isSitting)
        {
            PlayAnim(stateIdle);
            Vector3 p = transform.position; p.y = 0f;
            transform.position = p;
            isSitting = false;
        }
        if (idleSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(WalkVia(idleSpot.position, idleWaypoints));
        }
        SetActivity("Idle");
        PlayAnim(stateIdle);
    }

    IEnumerator WalkVia(Vector3 target, Transform[] wps)
    {
        if (wps != null)
            foreach (var wp in wps)
                if (wp != null)
                    yield return StartCoroutine(WalkTo(wp.position));
        yield return StartCoroutine(WalkTo(target));
    }

    IEnumerator WalkTo(Vector3 target)
    {
        target.y = 0f;
        transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
        PlayAnim(stateWalk);
        while (true)
        {
            Vector3 cur = new Vector3(transform.position.x, 0f, transform.position.z);
            if (Vector3.Distance(cur, target) <= arrivalThreshold) break;
            transform.position = Vector3.MoveTowards(cur, target, walkSpeed * Time.deltaTime);
            Vector3 dir = (target - cur).normalized;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.position = new Vector3(target.x, 0f, target.z);
    }

    IEnumerator SmoothRotateTo(Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) yield break;
        Quaternion tgt = Quaternion.LookRotation(fwd.normalized);
        while (Quaternion.Angle(transform.rotation, tgt) > 0.5f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation, tgt, Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.rotation = tgt;
    }

    void TeleportToSeat(Transform targetSpot)
    {
        transform.position = targetSpot.position;
        transform.rotation = targetSpot.rotation;
        isSitting = true;
    }

    Vector3 GetApproachPos(Transform spot)
    {
        Vector3 p = spot.position - spot.forward * 0.3f;
        p.y = 0f;
        return p;
    }

    void SetActivity(string a)
    {
        currentActivity = a;
        if (behaviorItems == null) return;

        foreach (var bi in behaviorItems)
        {
            if (bi.item == null) continue;

            bool active = string.Equals(
                bi.activity, a,
                System.StringComparison.OrdinalIgnoreCase);

            bi.item.SetActive(active);

            if (bi.sceneCounterpart != null)
                bi.sceneCounterpart.SetActive(!active);
        }
    }

    void PlayAnim(string s) => anim.Play(s, 0, 0f);
    void Warn(string s)     => Debug.LogWarning($"[{userID}] {s} not set");

    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot,   Color.yellow, "Drink");
        DrawSpot(layingSpot,  Color.green,  "Laying");
        DrawSpot(readingSpot, Color.blue,   "Reading");
        DrawSpot(typingSpot,  Color.red,    "Typing");
        if (idleSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(idleSpot.position, 0.15f);
        }
        DrawWaypointPath(drinkWaypoints,   drinkSpot,   Color.yellow);
        DrawWaypointPath(layingWaypoints,  layingSpot,  Color.green);
        DrawWaypointPath(readingWaypoints, readingSpot, Color.blue);
        DrawWaypointPath(typingWaypoints,  typingSpot,  Color.red);
    }

    void DrawSpot(Transform spot, Color c, string label)
    {
        if (spot == null) return;
        Gizmos.color = c;
        Gizmos.DrawWireSphere(spot.position, 0.2f);
        Gizmos.DrawRay(spot.position, spot.forward * 0.8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(spot.position + Vector3.up * 0.35f, label);
#endif
    }

    void DrawWaypointPath(Transform[] wps, Transform final, Color c)
    {
        if (wps == null || wps.Length == 0 || idleSpot == null) return;
        Gizmos.color = new Color(c.r, c.g, c.b, 0.4f);
        Vector3 prev = idleSpot.position;
        foreach (var wp in wps)
        {
            if (wp != null) { Gizmos.DrawLine(prev, wp.position); prev = wp.position; }
        }
        if (final != null) Gizmos.DrawLine(prev, final.position);
    }
}