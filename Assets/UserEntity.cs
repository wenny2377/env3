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

    [Header("Kitchen Spots")]
    public Transform drinkSpot;
    public Transform sittingDrinkSpot;
    public Transform eatSpot;
    public Transform cookSpot;
    public Transform openSpot;

    [Header("LivingRoom Spots")]
    public Transform layingSpot;
    public Transform watchingSpot;
    public Transform readingSpot;
    public Transform cleanSpot;
    public Transform phoneSpot;

    [Header("DadRoom Spots")]
    public Transform typingSpot;
    public Transform dadReadingSpot;
    public Transform dadPhoneSpot;
    public Transform dadCleanSpot;

    [Header("Common")]
    public Transform standingSpot;

    [Header("Fridge Door")]
    public Transform fridgeDoor;
    public float     fridgeOpenAngle = -90f;
    public float     fridgeOpenSpeed = 90f;

    [Header("Kitchen Waypoints")]
    public Transform[] drinkWaypoints;
    public Transform[] sittingDrinkWaypoints;
    public Transform[] eatWaypoints;
    public Transform[] cookWaypoints;
    public Transform[] openWaypoints;

    [Header("LivingRoom Waypoints")]
    public Transform[] layingWaypoints;
    public Transform[] watchingWaypoints;
    public Transform[] readingWaypoints;
    public Transform[] cleanWaypoints;
    public Transform[] phoneWaypoints;

    [Header("DadRoom Waypoints")]
    public Transform[] typingWaypoints;
    public Transform[] dadReadingWaypoints;
    public Transform[] dadPhoneWaypoints;
    public Transform[] dadCleanWaypoints;

    [Header("Common Waypoints")]
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

    [Header("Action Durations (seconds)")]
    public float noddingDuration = 1.5f;
    public float drinkDuration   = 2.0f;
    public float eatDuration     = 3.0f;
    public float cookDuration    = 3.0f;
    public float openDuration    = 2.0f;
    public float cleanDuration   = 3.0f;
    public float pickUpDuration  = 1.0f;
    public float putDownDuration = 1.0f;

    [Header("Held items")]
    public BehaviorItem[] behaviorItems;

    [Header("Animator state names")]
    public string stateStanding     = "Standing";
    public string stateWalk         = "Walking";
    public string stateDrink        = "Drinking";
    public string stateSittingDrink = "SittingDrink";
    public string stateLaying       = "Laying";
    public string stateReading      = "Reading";
    public string stateTyping       = "Typing";
    public string stateWatching     = "Watching";
    public string statePhone        = "PhoneUse";
    public string stateNodding      = "Nodding";
    public string stateEating       = "Eating";
    public string stateCooking      = "Cooking";
    public string stateCleaning     = "Cleaning";
    public string stateOpening      = "Opening";
    public string statePickingUp    = "PickingUp";
    public string statePuttingDown  = "PuttingDown";

    // ── Public state ─────────────────────────────────────────────
    public string currentActivity      { get; private set; } = "Standing";
    public bool   IsBusy               { get; private set; } = false;
    public string lastAssignedActivity = "";

    [HideInInspector]
    public float currentVirtualHour = -1f;

    public Vector3 GetAimPosition() =>
        transform.position + Vector3.up * 1.2f;

    // ── ResetBusy — called by ExperimentRunner between sequence steps
    public void ResetBusy() => IsBusy = false;

    // ── Private ──────────────────────────────────────────────────
    Animator anim;
    bool     isSitting   = false;
    float    _shadowTimer = 0f;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    void Start()
    {
        anim = GetComponent<Animator>();
        if (behaviorItems != null)
            foreach (var bi in behaviorItems)
            {
                if (bi.item != null)             bi.item.SetActive(false);
                if (bi.sceneCounterpart != null) bi.sceneCounterpart.SetActive(true);
            }
        StartCoroutine(InitAnim());
    }

    IEnumerator InitAnim()
    {
        yield return null;
        PlayAnim(stateStanding);
    }

    // ── Public API ───────────────────────────────────────────────
    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        switch (activity.ToLower().Trim())
        {
            case "drink":
            case "drinking":
                yield return StartCoroutine(DoDrink());          break;
            case "sittingdrink":
                yield return StartCoroutine(DoSittingDrink());   break;
            case "eat":
            case "eating":
                yield return StartCoroutine(DoEat());            break;
            case "cook":
            case "cooking":
                yield return StartCoroutine(DoCook());           break;
            case "open":
            case "opening":
                yield return StartCoroutine(DoOpen());           break;
            case "laying":
            case "sleep":
                yield return StartCoroutine(DoLaying());         break;
            case "watch":
            case "watching":
                yield return StartCoroutine(DoWatching());       break;
            case "read":
            case "reading":
                yield return StartCoroutine(DoReading());        break;
            case "clean":
            case "cleaning":
                yield return StartCoroutine(DoCleaning());       break;
            case "phone":
            case "phoneuse":
                yield return StartCoroutine(DoPhoneUse());       break;
            case "type":
            case "typing":
                yield return StartCoroutine(DoTyping());         break;
            case "dadreading":
                yield return StartCoroutine(DoDadReading());     break;
            case "dadphone":
                yield return StartCoroutine(DoDadPhone());       break;
            case "dadclean":
            case "dadcleaning":
                yield return StartCoroutine(DoDadCleaning());    break;
            case "pickup":
            case "pickingup":
                yield return StartCoroutine(DoPickUp());         break;
            case "putdown":
            case "puttingdown":
                yield return StartCoroutine(DoPutDown());        break;
            case "standing":
                yield return StartCoroutine(DoReturnToStanding()); break;
            default:
                Debug.LogWarning($"[{userID}] Unknown activity: {activity}");
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
            "Drinking"     => stateDrink,
            "SittingDrink" => stateSittingDrink,
            "Laying"       => stateLaying,
            "Reading"      => stateReading,
            "Typing"       => stateTyping,
            "Watching"     => stateWatching,
            "PhoneUse"     => statePhone,
            "Eating"       => stateEating,
            "Cooking"      => stateCooking,
            "Cleaning"     => stateCleaning,
            _              => stateStanding,
        });
    }

    public void SetAnim(string s) => PlayAnim(s);

    // ── Kitchen ──────────────────────────────────────────────────
    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(drinkSpot.position, drinkWaypoints));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drinking");
        PlayAnim(stateDrink);
        yield return new WaitForSeconds(drinkDuration);
    }

    IEnumerator DoSittingDrink()
    {
        if (sittingDrinkSpot == null) { Warn("sittingDrinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(sittingDrinkSpot), sittingDrinkWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingDrinkSpot.forward));
        TeleportToSeat(sittingDrinkSpot);
        SetActivity("SittingDrink");
        PlayAnim(stateSittingDrink);
        yield return new WaitForSeconds(drinkDuration);
    }

    IEnumerator DoEat()
    {
        if (eatSpot == null) { Warn("eatSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(eatSpot), eatWaypoints));
        yield return StartCoroutine(SmoothRotateTo(eatSpot.forward));
        TeleportToSeat(eatSpot);
        SetActivity("Eating");
        PlayAnim(stateEating);
        yield return new WaitForSeconds(eatDuration);
    }

    IEnumerator DoCook()
    {
        if (cookSpot == null) { Warn("cookSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(cookSpot.position, cookWaypoints));
        yield return StartCoroutine(SmoothRotateTo(cookSpot.forward));
        SetActivity("Cooking");
        PlayAnim(stateCooking);
        yield return new WaitForSeconds(cookDuration);
    }

    IEnumerator DoOpen()
    {
        if (openSpot == null) { Warn("openSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(openSpot.position, openWaypoints));
        yield return StartCoroutine(SmoothRotateTo(openSpot.forward));
        SetActivity("Opening");
        PlayAnim(stateOpening);
        if (fridgeDoor != null)
            yield return StartCoroutine(RotateFridgeDoor(true));
        yield return new WaitForSeconds(openDuration);
        if (fridgeDoor != null)
            yield return StartCoroutine(RotateFridgeDoor(false));
    }

    IEnumerator RotateFridgeDoor(bool opening)
    {
        float targetY = opening ? fridgeOpenAngle : 0f;
        float startY  = fridgeDoor.localEulerAngles.y;
        if (startY > 180f) startY -= 360f;
        float duration = Mathf.Abs(targetY - startY) / fridgeOpenSpeed;
        if (duration < 0.01f) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float y  = Mathf.Lerp(startY, targetY,
                                   Mathf.Clamp01(elapsed / duration));
            var e = fridgeDoor.localEulerAngles;
            fridgeDoor.localEulerAngles = new Vector3(e.x, y, e.z);
            yield return null;
        }
        var ef = fridgeDoor.localEulerAngles;
        fridgeDoor.localEulerAngles = new Vector3(ef.x, targetY, ef.z);
    }

    // ── LivingRoom ───────────────────────────────────────────────
    IEnumerator DoLaying()
    {
        if (layingSpot == null) { Warn("layingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(layingSpot), layingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(layingSpot.forward));
        TeleportToSeat(layingSpot);
        SetActivity("Laying");
        PlayAnim(stateLaying);
    }

    IEnumerator DoWatching()
    {
        if (watchingSpot == null) { Warn("watchingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(watchingSpot), watchingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(watchingSpot.forward));
        TeleportToSeat(watchingSpot);
        SetActivity("Watching");
        PlayAnim(stateWatching);
    }

    IEnumerator DoReading()
    {
        if (readingSpot == null) { Warn("readingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(readingSpot), readingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(readingSpot.forward));
        TeleportToSeat(readingSpot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoCleaning()
    {
        if (cleanSpot == null) { Warn("cleanSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(cleanSpot.position, cleanWaypoints));
        yield return StartCoroutine(SmoothRotateTo(cleanSpot.forward));
        SetActivity("Cleaning");
        PlayAnim(stateCleaning);
        yield return new WaitForSeconds(cleanDuration);
    }

    IEnumerator DoPhoneUse()
    {
        if (phoneSpot == null) { Warn("phoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(phoneSpot.position, phoneWaypoints));
        yield return StartCoroutine(SmoothRotateTo(phoneSpot.forward));
        SetActivity("PhoneUse");
        PlayAnim(statePhone);
    }

    // ── DadRoom ──────────────────────────────────────────────────
    IEnumerator DoTyping()
    {
        if (typingSpot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(
            WalkVia(GetApproachPos(typingSpot), typingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(typingSpot.forward));
        TeleportToSeat(typingSpot);
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoDadReading()
    {
        Transform   spot = dadReadingSpot ?? readingSpot;
        Transform[] wps  = dadReadingWaypoints ?? readingWaypoints;
        if (spot == null) { Warn("dadReadingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(spot), wps));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoDadPhone()
    {
        Transform   spot = dadPhoneSpot ?? phoneSpot;
        Transform[] wps  = dadPhoneWaypoints ?? phoneWaypoints;
        if (spot == null) { Warn("dadPhoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(spot.position, wps));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        SetActivity("PhoneUse");
        PlayAnim(statePhone);
    }

    IEnumerator DoDadCleaning()
    {
        Transform   spot = dadCleanSpot ?? cleanSpot;
        Transform[] wps  = dadCleanWaypoints ?? cleanWaypoints;
        if (spot == null) { Warn("dadCleanSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(spot.position, wps));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        SetActivity("Cleaning");
        PlayAnim(stateCleaning);
        yield return new WaitForSeconds(cleanDuration);
    }

    // ── Utility ──────────────────────────────────────────────────
    IEnumerator DoPickUp()
    {
        SetActivity("PickingUp");
        PlayAnim(statePickingUp);
        yield return new WaitForSeconds(pickUpDuration);
        SetActivity("Standing");
        PlayAnim(stateStanding);
    }

    IEnumerator DoPutDown()
    {
        SetActivity("PuttingDown");
        PlayAnim(statePuttingDown);
        yield return new WaitForSeconds(putDownDuration);
        SetActivity("Standing");
        PlayAnim(stateStanding);
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
        target.y     = 0f;
        _shadowTimer = 0f;
        transform.position = new Vector3(
            transform.position.x, 0f, transform.position.z);
        PlayAnim(stateWalk);

        while (true)
        {
            Vector3 cur  = new Vector3(
                transform.position.x, 0f, transform.position.z);
            float   dist = Vector3.Distance(cur, target);
            if (dist <= arrivalThreshold) break;

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
        req.downloadHandler =
            new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
    }

    // ── Seat / approach helpers ──────────────────────────────────
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

    // ── Activity / item helpers ──────────────────────────────────
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
            Debug.LogWarning($"[{userID}] Animator state '{s}' NOT FOUND");
    }

    void Warn(string s) =>
        Debug.LogWarning($"[{userID}] {s} not set");

    static string EscJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Gizmos ───────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot,        Color.cyan,    "Drink");
        DrawSpot(sittingDrinkSpot, Color.blue,    "SitDrink");
        DrawSpot(eatSpot,          Color.yellow,  "Eat");
        DrawSpot(cookSpot,         Color.red,     "Cook");
        DrawSpot(openSpot,         Color.white,   "Open");
        DrawSpot(layingSpot,       Color.green,   "Laying");
        DrawSpot(watchingSpot,     Color.magenta, "Watch");
        DrawSpot(readingSpot,      Color.blue,    "Read");
        DrawSpot(cleanSpot,        Color.gray,    "Clean");
        DrawSpot(phoneSpot,        Color.magenta, "Phone");
        DrawSpot(typingSpot,       Color.red,     "Type");
        if (standingSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(standingSpot.position, 0.15f);
        }
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
}