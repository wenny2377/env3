using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

[System.Serializable]
public class BehaviorItem
{
    public string activity = "Drinking";
    public GameObject item;
    public GameObject sceneCounterpart;
}

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
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
    public float fridgeOpenAngle = -90f;
    public float fridgeOpenSpeed = 90f;

    [Header("Movement")]
    public float walkSpeed = 1.4f;
    public float arrivalThreshold = 0.15f;
    public float rotationSpeed = 8f;

    [Header("NavMesh")]
    public float navSampleRadius = 3.0f;

    [Header("Shadow Tracking")]
    public float shadowInterval = 0.5f;
    public float jitterRadius = 0.1f;
    public string backendUrl = "http://localhost:5000";

    [Header("Action Durations (seconds)")]
    public float noddingDuration = 1.5f;
    public float drinkDuration = 2.0f;
    public float eatDuration = 3.0f;
    public float cookDuration = 3.0f;
    public float openDuration = 2.0f;
    public float cleanDuration = 3.0f;
    public float pickUpDuration = 1.0f;
    public float putDownDuration = 1.0f;

    [Header("Held items")]
    public BehaviorItem[] behaviorItems;

    [Header("Animator state names")]
    public string stateStanding = "Standing";
    public string stateWalk = "Walking";
    public string stateDrink = "Drinking";
    public string stateSittingDrink = "SittingDrink";
    public string stateLaying = "Laying";
    public string stateReading = "Reading";
    public string stateTyping = "Typing";
    public string stateWatching = "Watching";
    public string statePhone = "PhoneUse";
    public string stateNodding = "Nodding";
    public string stateEating = "Eating";
    public string stateCooking = "Cooking";
    public string stateCleaning = "Cleaning";
    public string stateOpening = "Opening";
    public string statePickingUp = "PickingUp";
    public string statePuttingDown = "PuttingDown";

    public string currentActivity { get; private set; } = "Standing";
    public bool IsBusy { get; private set; } = false;
    public string lastAssignedActivity = "";

    [HideInInspector]
    public float currentVirtualHour = -1f;

    public Vector3 GetAimPosition() =>
        transform.position + Vector3.up * 1.2f;

    public void ResetBusy() => IsBusy = false;

    Animator anim;
    NavMeshAgent agent;
    bool isSitting = false;
    float _shadowTimer = 0f;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    void Start()
    {
        anim = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();

        // Agent only calculates paths — never moves transform
        agent.speed = 0f;
        agent.angularSpeed = 0f;
        agent.acceleration = 0f;
        agent.autoBraking = false;
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.enabled = true;

        // Sync agent position to transform so CalculatePath works correctly
        agent.Warp(transform.position);

        if (behaviorItems != null)
            foreach (var bi in behaviorItems)
            {
                if (bi.item != null) bi.item.SetActive(false);
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
                yield return StartCoroutine(DoDrink()); break;
            case "sittingdrink":
                yield return StartCoroutine(DoSittingDrink()); break;
            case "eat":
            case "eating":
                yield return StartCoroutine(DoEat()); break;
            case "cook":
            case "cooking":
                yield return StartCoroutine(DoCook()); break;
            case "open":
            case "opening":
                yield return StartCoroutine(DoOpen()); break;
            case "laying":
            case "sleep":
                yield return StartCoroutine(DoLaying()); break;
            case "watch":
            case "watching":
                yield return StartCoroutine(DoWatching()); break;
            case "read":
            case "reading":
                yield return StartCoroutine(DoReading()); break;
            case "clean":
            case "cleaning":
                yield return StartCoroutine(DoCleaning()); break;
            case "phone":
            case "phoneuse":
                yield return StartCoroutine(DoPhoneUse()); break;
            case "type":
            case "typing":
                yield return StartCoroutine(DoTyping()); break;
            case "dadreading":
                yield return StartCoroutine(DoDadReading()); break;
            case "dadphone":
                yield return StartCoroutine(DoDadPhone()); break;
            case "dadclean":
            case "dadcleaning":
                yield return StartCoroutine(DoDadCleaning()); break;
            case "pickup":
            case "pickingup":
                yield return StartCoroutine(DoPickUp()); break;
            case "putdown":
            case "puttingdown":
                yield return StartCoroutine(DoPutDown()); break;
            case "standing":
                yield return StartCoroutine(DoReturnToStanding()); break;
            default:
                Debug.LogWarning($"[{userID}] Unknown: {activity}");
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
            "SittingDrink" => stateSittingDrink,
            "Laying" => stateLaying,
            "Reading" => stateReading,
            "Typing" => stateTyping,
            "Watching" => stateWatching,
            "PhoneUse" => statePhone,
            "Eating" => stateEating,
            "Cooking" => stateCooking,
            "Cleaning" => stateCleaning,
            _ => stateStanding,
        });
    }

    public void SetAnim(string s) => PlayAnim(s);

    // ── Kitchen ──────────────────────────────────────────────────

    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(drinkSpot.position, false));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drinking");
        PlayAnim(stateDrink);
        yield return new WaitForSeconds(drinkDuration);
    }

    IEnumerator DoSittingDrink()
    {
        if (sittingDrinkSpot == null) { Warn("sittingDrinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(sittingDrinkSpot.position, true));
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
        yield return StartCoroutine(NavWalkTo(eatSpot.position, true));
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
        yield return StartCoroutine(NavWalkTo(cookSpot.position, false));
        yield return StartCoroutine(SmoothRotateTo(cookSpot.forward));
        SetActivity("Cooking");
        PlayAnim(stateCooking);
        yield return new WaitForSeconds(cookDuration);
    }

    IEnumerator DoOpen()
    {
        if (openSpot == null) { Warn("openSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(openSpot.position, false));
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
        float startY = fridgeDoor.localEulerAngles.y;
        if (startY > 180f) startY -= 360f;
        float duration = Mathf.Abs(targetY - startY) / fridgeOpenSpeed;
        if (duration < 0.01f) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float y = Mathf.Lerp(startY, targetY,
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
        yield return StartCoroutine(NavWalkTo(layingSpot.position, true));
        yield return StartCoroutine(SmoothRotateTo(layingSpot.forward));
        TeleportToSeat(layingSpot);
        SetActivity("Laying");
        PlayAnim(stateLaying);
    }

    IEnumerator DoWatching()
    {
        if (watchingSpot == null) { Warn("watchingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(watchingSpot.position, true));
        yield return StartCoroutine(SmoothRotateTo(watchingSpot.forward));
        TeleportToSeat(watchingSpot);
        SetActivity("Watching");
        PlayAnim(stateWatching);
    }

    IEnumerator DoReading()
    {
        if (readingSpot == null) { Warn("readingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(readingSpot.position, true));
        yield return StartCoroutine(SmoothRotateTo(readingSpot.forward));
        TeleportToSeat(readingSpot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoCleaning()
    {
        if (cleanSpot == null) { Warn("cleanSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(cleanSpot.position, false));
        yield return StartCoroutine(SmoothRotateTo(cleanSpot.forward));
        SetActivity("Cleaning");
        PlayAnim(stateCleaning);
        yield return new WaitForSeconds(cleanDuration);
    }

    IEnumerator DoPhoneUse()
    {
        if (phoneSpot == null) { Warn("phoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(phoneSpot.position, false));
        yield return StartCoroutine(SmoothRotateTo(phoneSpot.forward));
        SetActivity("PhoneUse");
        PlayAnim(statePhone);
    }

    // ── DadRoom ──────────────────────────────────────────────────

    IEnumerator DoTyping()
    {
        if (typingSpot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(typingSpot.position, true));
        yield return StartCoroutine(SmoothRotateTo(typingSpot.forward));
        TeleportToSeat(typingSpot);
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoDadReading()
    {
        Transform spot = dadReadingSpot ?? readingSpot;
        if (spot == null) { Warn("dadReadingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoDadPhone()
    {
        Transform spot = dadPhoneSpot ?? phoneSpot;
        if (spot == null) { Warn("dadPhoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, false));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        SetActivity("PhoneUse");
        PlayAnim(statePhone);
    }

    IEnumerator DoDadCleaning()
    {
        Transform spot = dadCleanSpot ?? cleanSpot;
        if (spot == null) { Warn("dadCleanSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, false));
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
            isSitting = false;
            PlayAnim(stateStanding);
            // Snap Y to floor before walking
            Vector3 p = transform.position;
            p.y = 0f;
            transform.position = p;
            // Sync agent to new position
            agent.Warp(transform.position);
            yield return null;
        }

        if (standingSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        }

        SetActivity("Standing");
        PlayAnim(stateStanding);
    }

    // ── NavMesh walk ──────────────────────────────────────────────
    // Agent never moves transform (updatePosition = false).
    // We use CalculatePath for obstacle-aware corners,
    // then MoveTowards along each corner manually.
    // useSeatTarget = true  → Spot on furniture, use large sample radius
    // useSeatTarget = false → Spot on floor, use small sample radius
    IEnumerator NavWalkTo(Vector3 spotPos, bool useSeatTarget)
    {
        // Sync agent to current transform position before path calculation
        agent.Warp(transform.position);

        // Find nearest walkable NavMesh point from spot
        float radius = useSeatTarget ? navSampleRadius : 1.5f;
        NavMeshHit nmHit;
        if (!NavMesh.SamplePosition(spotPos, out nmHit, radius, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[{userID}] No NavMesh within {radius}m of {spotPos}. " +
                             $"Increase navSampleRadius or check NavMesh bake.");
            yield break;
        }

        Vector3 walkTarget = new Vector3(nmHit.position.x, 0f, nmHit.position.z);

        // Calculate path
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(walkTarget, path) ||
            path.status == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning($"[{userID}] Path invalid to {walkTarget}. " +
                             $"Check NavMesh connectivity between rooms.");
            yield break;
        }

        PlayAnim(stateWalk);
        _shadowTimer = 0f;

        Vector3[] corners = path.corners;

        for (int ci = 0; ci < corners.Length; ci++)
        {
            Vector3 corner = new Vector3(corners[ci].x, 0f, corners[ci].z);
            bool isLast = ci == corners.Length - 1;
            float stop = isLast ? arrivalThreshold : 0.08f;

            while (true)
            {
                Vector3 cur = new Vector3(
                    transform.position.x, 0f, transform.position.z);
                float dist = Vector3.Distance(cur, corner);
                if (dist <= stop) break;

                Vector3 dir = (corner - cur).normalized;

                // Lateral jitter — perpendicular to movement direction
                // Only applied as a small offset to the target, NOT to dir
                // This prevents crab-walking
                Vector3 side = Vector3.Cross(dir, Vector3.up);
                float jitter = Random.Range(-jitterRadius, jitterRadius);
                Vector3 moveTarget = corner + side * jitter * Mathf.Min(dist, 0.5f);

                transform.position = Vector3.MoveTowards(
                    cur, moveTarget, walkSpeed * Time.deltaTime);

                // Rotate toward the true corner direction, not the jittered target
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
        }

        // Snap to exact walk target
        transform.position = new Vector3(walkTarget.x, 0f, walkTarget.z);
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
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler =
            new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
    }

    // Teleports to seat position (furniture height).
    // Agent updatePosition = false so no conflict with transform.
    void TeleportToSeat(Transform spot)
    {
        transform.position = spot.position;
        transform.rotation = spot.rotation;
        isSitting = true;
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

    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot, Color.cyan, "Drink");
        DrawSpot(sittingDrinkSpot, Color.blue, "SitDrink");
        DrawSpot(eatSpot, Color.yellow, "Eat");
        DrawSpot(cookSpot, Color.red, "Cook");
        DrawSpot(openSpot, Color.white, "Open");
        DrawSpot(layingSpot, Color.green, "Laying");
        DrawSpot(watchingSpot, Color.magenta, "Watch");
        DrawSpot(readingSpot, Color.blue, "Read");
        DrawSpot(cleanSpot, Color.gray, "Clean");
        DrawSpot(phoneSpot, Color.magenta, "Phone");
        DrawSpot(typingSpot, Color.red, "Type");
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