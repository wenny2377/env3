using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class BehaviorItem
{
    public string     activity = "Drinking";
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
    public Transform layingSpot;
    public Transform readingSpot;
    public Transform typingSpot;
    public Transform watchingSpot;
    public Transform phoneSpot;
    public Transform standingSpot;

    [Header("Waypoints")]
    public Transform[] drinkWaypoints;
    public Transform[] layingWaypoints;
    public Transform[] readingWaypoints;
    public Transform[] typingWaypoints;
    public Transform[] watchingWaypoints;
    public Transform[] phoneWaypoints;
    public Transform[] standingWaypoints;

    [Header("Movement")]
    public float walkSpeed        = 1.4f;
    public float arrivalThreshold = 0.08f;
    public float rotationSpeed    = 8f;

    [Header("Shadow Tracking")]
    public float  shadowInterval = 0.5f;
    public float  jitterStopDist = 0.5f;
    public float  jitterRadius   = 0.25f;
    public string backendUrl     = "http://localhost:5000";

    [Header("Nodding duration (s)")]
    public float noddingDuration = 1.5f;

    [Header("Held items")]
    public BehaviorItem[] behaviorItems;

    [Header("Animator state names")]
    public string stateStanding = "Standing";
    public string stateWalk     = "Walking";
    public string stateDrink    = "Drinking";
    public string stateLaying   = "Laying";
    public string stateReading  = "Reading";
    public string stateTyping   = "Typing";
    public string stateWatching = "Watching";
    public string statePhone    = "PhoneUse";
    public string stateNodding  = "Nodding";

    // ── Public state ─────────────────────────────────────────────
    public string currentActivity      { get; private set; } = "Standing";
    public bool   IsBusy               { get; private set; } = false;
    public string lastAssignedActivity = "";

    [HideInInspector]
    public float currentVirtualHour = -1f;

    public Vector3 GetAimPosition() =>
        transform.position + Vector3.up * 1.2f;

    // ── Private ──────────────────────────────────────────────────
    Animator anim;
    bool     isSitting    = false;
    float    _shadowTimer = 0f;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    // ── Unity lifecycle ──────────────────────────────────────────
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
        StartCoroutine(InitAnim());
    }

    IEnumerator InitAnim()
    {
        yield return null;
        PlayAnim(stateStanding);
    }

    // ── Public coroutines ────────────────────────────────────────
    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        switch (activity.ToLower())
        {
            case "drink":
            case "drinking":
                yield return StartCoroutine(DoDrink());    break;
            case "laying":
                yield return StartCoroutine(DoLaying());   break;
            case "reading":
                yield return StartCoroutine(DoReading());  break;
            case "typing":
                yield return StartCoroutine(DoTyping());   break;
            case "watching":
                yield return StartCoroutine(DoWatching()); break;
            case "phoneuse":
                yield return StartCoroutine(DoPhoneUse()); break;
            case "standing":
                yield return StartCoroutine(DoReturnToStanding()); break;
            default:
                Debug.LogWarning(
                    $"[{userID}] Unknown activity: {activity}");
                break;
        }

        IsBusy = false;
    }

    public IEnumerator ReturnToStanding()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToStanding());
        IsBusy = false;
    }

    public IEnumerator Nod()
    {
        PlayAnim(stateNodding);
        yield return new WaitForSeconds(noddingDuration);
        PlayAnim(currentActivity switch
        {
            "Drinking" => stateDrink,
            "Laying"   => stateLaying,
            "Reading"  => stateReading,
            "Typing"   => stateTyping,
            "Watching" => stateWatching,
            "PhoneUse" => statePhone,
            _          => stateStanding,
        });
    }

    public void SetAnim(string s) => PlayAnim(s);

    // ── Activity implementations ─────────────────────────────────
    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(drinkSpot.position, drinkWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drinking");
        PlayAnim(stateDrink);
    }

    IEnumerator DoLaying()
    {
        if (layingSpot == null) { Warn("layingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(layingSpot), layingWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(layingSpot.forward));
        TeleportToSeat(layingSpot);
        SetActivity("Laying");
        PlayAnim(stateLaying);
    }

    IEnumerator DoReading()
    {
        if (readingSpot == null) { Warn("readingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(readingSpot), readingWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(readingSpot.forward));
        TeleportToSeat(readingSpot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoTyping()
    {
        if (typingSpot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(typingSpot), typingWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(typingSpot.forward));
        TeleportToSeat(typingSpot);
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoWatching()
    {
        if (watchingSpot == null) { Warn("watchingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(watchingSpot), watchingWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(watchingSpot.forward));
        TeleportToSeat(watchingSpot);
        SetActivity("Watching");
        PlayAnim(stateWatching);
    }

    IEnumerator DoPhoneUse()
    {
        if (phoneSpot == null) { Warn("phoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(phoneSpot.position, phoneWaypoints));
        yield return StartCoroutine(
            SmoothRotateTo(phoneSpot.forward));
        SetActivity("PhoneUse");
        PlayAnim(statePhone);
    }

    IEnumerator DoReturnToStanding()
    {
        if (isSitting)
        {
            PlayAnim(stateStanding);
            Vector3 p = transform.position;
            p.y = 0f;
            transform.position = p;
            isSitting = false;
        }
        if (standingSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(
                WalkVia(standingSpot.position, standingWaypoints));
        }
        SetActivity("Standing");
        PlayAnim(stateStanding);
    }

    // ── Walk helpers ─────────────────────────────────────────────
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
        target.y           = 0f;
        _shadowTimer       = 0f;
        transform.position = new Vector3(
            transform.position.x, 0f, transform.position.z);
        PlayAnim(stateWalk);

        while (true)
        {
            Vector3 cur  = new Vector3(
                transform.position.x, 0f, transform.position.z);
            float   dist = Vector3.Distance(cur, target);

            if (dist <= arrivalThreshold) break;

            // Jitter — disabled within jitterStopDist of target
            Vector3 jitter = Vector3.zero;
            if (dist > jitterStopDist)
            {
                Vector2 r2 = Random.insideUnitCircle * jitterRadius;
                jitter = new Vector3(r2.x, 0f, r2.y);
            }

            Vector3 moveTarget = new Vector3(
                target.x + jitter.x, 0f, target.z + jitter.z);

            transform.position = Vector3.MoveTowards(
                cur, moveTarget, walkSpeed * Time.deltaTime);

            Vector3 dir = (moveTarget - cur).normalized;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * rotationSpeed);

            // Shadow tracking POST every shadowInterval seconds
            _shadowTimer += Time.deltaTime;
            if (_shadowTimer >= shadowInterval)
            {
                _shadowTimer = 0f;
                StartCoroutine(PostShadowPoint());
            }

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
                transform.rotation, tgt,
                Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.rotation = tgt;
    }

    // ── Shadow tracking ──────────────────────────────────────────
    IEnumerator PostShadowPoint()
    {
        string intent = !string.IsNullOrEmpty(lastAssignedActivity)
            ? lastAssignedActivity : "Walking";

        string hourStr = currentVirtualHour >= 0f
            ? currentVirtualHour.ToString("F1", Inv)
            : ((float)System.DateTime.Now.Hour).ToString("F1", Inv);

        string json = "{"
            + $"\"userID\":\"{EscJson(userID)}\","
            + $"\"x\":{transform.position.x.ToString("F3", Inv)},"
            + $"\"z\":{transform.position.z.ToString("F3", Inv)},"
            + $"\"room_name\":\"\","
            + $"\"intent_action\":\"{EscJson(intent)}\","
            + $"\"virtual_hour\":{hourStr}"
            + "}";

        using var req = new UnityWebRequest(
            $"{backendUrl}/track_position", "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
    }

    // ── Seat helpers ─────────────────────────────────────────────
    void TeleportToSeat(Transform spot)
    {
        transform.position = spot.position;
        transform.rotation = spot.rotation;
        isSitting = true;
    }

    Vector3 GetApproachPos(Transform spot)
    {
        Vector3 p = spot.position - spot.forward * 0.3f;
        p.y = 0f;
        return p;
    }

    // ── Activity / anim helpers ──────────────────────────────────
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

    void PlayAnim(string s)
    {
        int hash = Animator.StringToHash(s);
        if (anim.HasState(0, hash))
            anim.Play(hash, 0, 0f);
        else
            Debug.LogWarning(
                $"[{userID}] Animator state '{s}' NOT FOUND");
    }

    void Warn(string s) =>
        Debug.LogWarning($"[{userID}] {s} not set");

    static string EscJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Gizmos ───────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot,    Color.yellow,  "Drink");
        DrawSpot(layingSpot,   Color.green,   "Laying");
        DrawSpot(readingSpot,  Color.blue,    "Reading");
        DrawSpot(typingSpot,   Color.red,     "Typing");
        DrawSpot(watchingSpot, Color.cyan,    "Watching");
        DrawSpot(phoneSpot,    Color.magenta, "PhoneUse");
        if (standingSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(standingSpot.position, 0.15f);
        }
        DrawWaypointPath(drinkWaypoints,    drinkSpot,    Color.yellow);
        DrawWaypointPath(layingWaypoints,   layingSpot,   Color.green);
        DrawWaypointPath(readingWaypoints,  readingSpot,  Color.blue);
        DrawWaypointPath(typingWaypoints,   typingSpot,   Color.red);
        DrawWaypointPath(watchingWaypoints, watchingSpot, Color.cyan);
        DrawWaypointPath(phoneWaypoints,    phoneSpot,    Color.magenta);
    }

    void DrawSpot(Transform spot, Color c, string label)
    {
        if (spot == null) return;
        Gizmos.color = c;
        Gizmos.DrawWireSphere(spot.position, 0.2f);
        Gizmos.DrawRay(spot.position, spot.forward * 0.8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            spot.position + Vector3.up * 0.35f, label);
#endif
    }

    void DrawWaypointPath(
        Transform[] wps, Transform final, Color c)
    {
        if (wps == null || wps.Length == 0 ||
            standingSpot == null) return;
        Gizmos.color = new Color(c.r, c.g, c.b, 0.4f);
        Vector3 prev = standingSpot.position;
        foreach (var wp in wps)
        {
            if (wp != null)
            {
                Gizmos.DrawLine(prev, wp.position);
                prev = wp.position;
            }
        }
        if (final != null)
            Gizmos.DrawLine(prev, final.position);
    }
}