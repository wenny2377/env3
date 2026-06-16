using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

[System.Serializable]
public class BehaviorItem
{
    [HideInInspector] public string     activity          = "";
    public                   GameObject item;
    public                   GameObject item2;
    public                   GameObject sceneCounterpart;
    public                   GameObject sceneCounterpart2;
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
    public Transform sittingSpot;

    [Header("DadRoom Spots")]
    public Transform typingSpot;
    public Transform dadReadingSpot;
    public Transform dadPhoneSpot;
    public Transform dadCleanSpot;

    [Header("Common")]
    public Transform standingSpot;

    [Header("Pickup & Putdown Spots")]
    public Transform cleaningPickupSpot;
    public Transform cleaningPutdownSpot;
    public Transform cookingPickupSpot;
    public Transform cookingPutdownSpot;
    public Transform eatingPickupSpot;
    public Transform eatingPutdownSpot;
    public Transform drinkingPickupSpot;
    public Transform drinkingPutdownSpot;
    public Transform readingPickupSpot;
    public Transform readingPutdownSpot;
    public Transform phonePickupSpot;
    public Transform phonePutdownSpot;

    [Header("TV")]
    public GameObject[] tvScreenObjects;

    [Header("Fridge Door")]
    public Transform fridgeDoor;
    public Transform fridgeHingePoint;
    public float     fridgeOpenAngle = -90f;
    public float     fridgeOpenSpeed = 90f;

    [Header("Action Durations (seconds)")]
    public float noddingDuration      = 1.5f;
    public float drinkDuration        = 2.0f;
    public float eatDuration          = 3.0f;
    public float cookDuration         = 3.0f;
    public float openDuration         = 2.0f;
    public float cleanDuration        = 3.0f;
    public float pickUpDuration       = 1.0f;
    public float putDownDuration      = 1.0f;
    public float sittingDuration      = 3.0f;
    public float standUpDuration      = 0.8f;
    public float activityHoldDuration = 3.0f;

    [Header("Held Items (drag only)")]
    public BehaviorItem drinkItem;
    public BehaviorItem sittingDrinkItem;
    public BehaviorItem eatItem;
    public BehaviorItem cookItem;
    public BehaviorItem cleanItem;
    public BehaviorItem readItem;
    public BehaviorItem phoneItem;

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";

    [Header("Corruption Model")]
    [Range(0f, 1f)] public float pickupMissRate  = 0.0f;
    [Range(0f, 1f)] public float putdownMissRate = 0.0f;

    [HideInInspector] public bool skipReturnToStanding = false;

    const float WALK_SPEED        = 1.4f;
    const float ARRIVAL_THRESHOLD = 0.15f;
    const float ROTATION_SPEED    = 8f;
    const float NAV_SAMPLE_RADIUS = 3.0f;
    const float SHADOW_INTERVAL   = 0.5f;
    const float JITTER_RADIUS     = 0.1f;

    const string STATE_STANDING      = "Standing";
    const string STATE_WALK          = "Walking";
    const string STATE_DRINK         = "Drinking";
    const string STATE_SITTING_DRINK = "SittingDrink";
    const string STATE_SITTING       = "Sitting";
    const string STATE_STAND_UP      = "StandUp";
    const string STATE_LAYING        = "Laying";
    const string STATE_READING       = "Reading";
    const string STATE_TYPING        = "Typing";
    const string STATE_WATCHING      = "Watching";
    const string STATE_PHONE         = "PhoneUse";
    const string STATE_NODDING       = "Nodding";
    const string STATE_EATING        = "Eating";
    const string STATE_COOKING       = "Cooking";
    const string STATE_CLEANING      = "Cleaning";
    const string STATE_OPENING       = "Opening";
    const string STATE_PICKING_UP    = "PickingUp";
    const string STATE_PUTTING_DOWN  = "PuttingDown";

    public string currentActivity      { get; private set; } = "Standing";
    public bool   IsBusy               { get; private set; } = false;
    public string lastAssignedActivity = "";

    [HideInInspector] public float     currentVirtualHour = -1f;
    [HideInInspector] public Transform overrideSpot       = null;

    public Vector3        GetAimPosition()   => transform.position + Vector3.up * 1.2f;
    public void           ResetBusy()        => IsBusy = false;
    public BehaviorItem[] GetBehaviorItems() => _allItems;

    public void SetSkeletonNoise(bool enabled)
    {
        if (_skeletonHelper != null)
            _skeletonHelper.skeletonNoiseEnabled = enabled;
    }

    Animator           anim;
    NavMeshAgent       agent;
    bool               isSitting    = false;
    float              _shadowTimer = 0f;
    BehaviorItem[]     _allItems;
    DynamicSyncManager _dsm;
    SkeletonHelper     _skeletonHelper;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    static readonly HashSet<string> SeatActions = new HashSet<string>
    {
        "Sitting", "SittingDrink", "Eating", "Laying",
        "Watching", "Typing", "Reading"
    };

    static readonly HashSet<string> HeldActions = new HashSet<string>
    {
        "SittingDrink", "Drinking", "Eating", "Cooking",
        "Reading", "PhoneUse"
    };

    void Start()
    {
        anim  = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();

        agent.speed          = 0f;
        agent.angularSpeed   = 0f;
        agent.acceleration   = 0f;
        agent.autoBraking    = false;
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis   = false;
        agent.enabled        = true;
        agent.Warp(transform.position);

        _dsm            = FindObjectOfType<DynamicSyncManager>();
        _skeletonHelper = GetComponent<SkeletonHelper>();

        InitBehaviorItems();
        ResetAllItems();
        StartCoroutine(InitAnim());
    }

    void InitBehaviorItems()
    {
        drinkItem.activity        = "Drinking";
        sittingDrinkItem.activity = "SittingDrink";
        eatItem.activity          = "Eating";
        cookItem.activity         = "Cooking";
        cleanItem.activity        = "Cleaning";
        readItem.activity         = "Reading";
        phoneItem.activity        = "PhoneUse";

        _allItems = new BehaviorItem[]
        {
            drinkItem, sittingDrinkItem, eatItem,
            cookItem,  cleanItem,        readItem, phoneItem,
        };
    }

    void ResetAllItems()
    {
        if (_allItems == null) return;
        foreach (var bi in _allItems)
        {
            if (bi.item              != null) bi.item.SetActive(false);
            if (bi.item2             != null) bi.item2.SetActive(false);
            if (bi.sceneCounterpart  != null) bi.sceneCounterpart.SetActive(true);
            if (bi.sceneCounterpart2 != null) bi.sceneCounterpart2.SetActive(true);
        }
    }

    IEnumerator InitAnim()
    {
        yield return null;
        PlayAnim(STATE_STANDING);
    }

    Transform ConsumeOverride(Transform fallback)
    {
        Transform result = overrideSpot != null ? overrideSpot : fallback;
        overrideSpot = null;
        return result;
    }

    string ActionToAnimState(string action) => action switch
    {
        "Cooking"      => STATE_COOKING,
        "Eating"       => STATE_EATING,
        "Sitting"      => STATE_SITTING,
        "SittingDrink" => STATE_SITTING_DRINK,
        "Drinking"     => STATE_DRINK,
        "Watching"     => STATE_WATCHING,
        "Typing"       => STATE_TYPING,
        "Reading"      => STATE_READING,
        "Laying"       => STATE_LAYING,
        "PhoneUse"     => STATE_PHONE,
        "Cleaning"     => STATE_CLEANING,
        "Opening"      => STATE_OPENING,
        _              => STATE_STANDING,
    };

    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        string actLower = activity.ToLower().Trim();
        if (actLower != "watch" && actLower != "watching")
        {
            SetTVActive(false);
            StartCoroutine(PostDeviceState("tv", "off"));
        }

        switch (actLower)
        {
            case "drink":
            case "drinking":     yield return StartCoroutine(DoDrink());            break;
            case "sittingdrink": yield return StartCoroutine(DoSittingDrink());     break;
            case "sitting":      yield return StartCoroutine(DoSitting());          break;
            case "standup":
            case "stand up":     yield return StartCoroutine(DoStandUp());          break;
            case "eat":
            case "eating":       yield return StartCoroutine(DoEat());              break;
            case "cook":
            case "cooking":      yield return StartCoroutine(DoCook());             break;
            case "open":
            case "opening":      yield return StartCoroutine(DoOpen());             break;
            case "laying":
            case "sleep":        yield return StartCoroutine(DoLaying());           break;
            case "watch":
            case "watching":     yield return StartCoroutine(DoWatching());         break;
            case "read":
            case "reading":      yield return StartCoroutine(DoReading());          break;
            case "clean":
            case "cleaning":     yield return StartCoroutine(DoCleaning());         break;
            case "phone":
            case "phoneuse":     yield return StartCoroutine(DoPhoneUse());         break;
            case "type":
            case "typing":       yield return StartCoroutine(DoTyping());           break;
            case "dadreading":   yield return StartCoroutine(DoDadReading());       break;
            case "dadphone":     yield return StartCoroutine(DoDadPhone());         break;
            case "dadclean":
            case "dadcleaning":  yield return StartCoroutine(DoDadCleaning());      break;
            case "pickup":
            case "pickingup":    yield return StartCoroutine(DoPickUp());           break;
            case "putdown":
            case "puttingdown":  yield return StartCoroutine(DoPutDown());          break;
            case "standing":     yield return StartCoroutine(DoReturnToStanding()); break;
            default: Debug.LogWarning($"[{userID}] Unknown activity: {activity}");  break;
        }

        IsBusy = false;
    }

    public IEnumerator ReturnToStanding()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToStanding());
        if (_dsm != null) _dsm.ForceObjectSync();
        IsBusy = false;
    }

    public IEnumerator MoveToSpotAndHold(string action, Transform spot)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        bool useSeat = SeatActions.Contains(action);

        if (spot != null)
        {
            SetActivity("Walking");
            PlayAnim(STATE_WALK);
            yield return StartCoroutine(NavWalkTo(spot.position, useSeat));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
            if (useSeat) TeleportToSeat(spot);
        }

        if (HeldActions.Contains(action))
        {
            PreActivateHeldObject(action);
            yield return new WaitForSeconds(0.5f);
        }

        if (action == "Watching")
        {
            SetTVActive(true);
            StartCoroutine(PostDeviceState("tv", "on"));
        }

        PlayAnim(ActionToAnimState(action));
        SetActivity(action);
        lastAssignedActivity = action;
        IsBusy = false;
    }

    public IEnumerator Nod()
    {
        PlayAnim(STATE_NODDING);
        yield return new WaitForSeconds(noddingDuration);
        PlayAnim(ActionToAnimState(currentActivity));
    }

    public void SetAnim(string s) => PlayAnim(s);

    IEnumerator DoDrink()
    {
        if (drinkingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(drinkingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("Drinking");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(drinkSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("Drinking");
        PlayAnim(STATE_DRINK);
        yield return new WaitForSeconds(drinkDuration);
        if (drinkingPutdownSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(drinkingPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoSittingDrink()
    {
        if (drinkingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(drinkingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("SittingDrink");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(sittingDrinkSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, true));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
            TeleportToSeat(spot);
        }
        SetActivity("SittingDrink");
        PlayAnim(STATE_SITTING_DRINK);
        yield return new WaitForSeconds(drinkDuration);
        if (drinkingPutdownSpot != null)
        {
            yield return StartCoroutine(DoStandUp());
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(drinkingPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoSitting()
    {
        Transform spot = ConsumeOverride(sittingSpot ?? sittingDrinkSpot);
        if (spot == null) { Warn("sittingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Sitting");
        PlayAnim(STATE_SITTING);
        yield return new WaitForSeconds(sittingDuration);
    }

    IEnumerator DoStandUp()
    {
        SetActivity("StandUp");
        PlayAnim(STATE_STAND_UP);
        yield return new WaitForSeconds(standUpDuration);
        isSitting          = false;
        Vector3 p          = transform.position;
        p.y                = 0f;
        transform.position = p;
        agent.Warp(transform.position);
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoEat()
    {
        if (eatingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(eatingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("Eating");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(eatSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, true));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
            TeleportToSeat(spot);
        }
        SetActivity("Eating");
        PlayAnim(STATE_EATING);
        yield return new WaitForSeconds(eatDuration);
        if (eatingPutdownSpot != null)
        {
            yield return StartCoroutine(DoStandUp());
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(eatingPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoCook()
    {
        if (cookingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(cookingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("Cooking");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(cookSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("Cooking");
        PlayAnim(STATE_COOKING);
        yield return new WaitForSeconds(cookDuration);
        if (cookingPutdownSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(cookingPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoOpen()
    {
        Transform spot = ConsumeOverride(openSpot);
        if (spot == null) { Warn("openSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, false));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        PlayAnim(STATE_OPENING);
        yield return null;
        SetActivity("Opening");
        if (fridgeDoor != null) yield return StartCoroutine(RotateFridgeDoor(true));
        yield return new WaitForSeconds(openDuration);
        if (fridgeDoor != null) yield return StartCoroutine(RotateFridgeDoor(false));
    }

    IEnumerator RotateFridgeDoor(bool opening)
    {
        float totalAngle = opening ? fridgeOpenAngle : -fridgeOpenAngle;
        if (Mathf.Abs(totalAngle) / fridgeOpenSpeed < 0.01f) yield break;
        float rotated = 0f;
        while (Mathf.Abs(rotated) < Mathf.Abs(totalAngle))
        {
            float step = Mathf.Sign(totalAngle) * fridgeOpenSpeed * Time.deltaTime;
            if (Mathf.Abs(rotated + step) > Mathf.Abs(totalAngle))
                step = totalAngle - rotated;
            Transform pivot = fridgeHingePoint != null ? fridgeHingePoint : fridgeDoor;
            fridgeDoor.RotateAround(pivot.position, Vector3.up, step);
            rotated += step;
            yield return null;
        }
    }

    IEnumerator DoLaying()
    {
        Transform spot = ConsumeOverride(layingSpot);
        if (spot == null) { Warn("layingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Laying");
        PlayAnim(STATE_LAYING);
        yield return new WaitForSeconds(activityHoldDuration);
    }

    IEnumerator DoWatching()
    {
        Transform spot = ConsumeOverride(watchingSpot);
        if (spot == null) { Warn("watchingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Watching");
        PlayAnim(STATE_WATCHING);
        SetTVActive(true);
        StartCoroutine(PostDeviceState("tv", "on"));
        yield return new WaitForSeconds(activityHoldDuration);
    }

    IEnumerator DoReading()
    {
        if (readingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(readingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("Reading");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(readingSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, true));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
            TeleportToSeat(spot);
        }
        SetActivity("Reading");
        PlayAnim(STATE_READING);
        yield return new WaitForSeconds(activityHoldDuration);
        if (readingPutdownSpot != null)
        {
            yield return StartCoroutine(DoStandUp());
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(readingPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoCleaning()
    {
        if (cleaningPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(cleaningPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("Cleaning");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(cleanSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("Cleaning");
        PlayAnim(STATE_CLEANING);
        lastAssignedActivity = "Cleaning";
        var scm = StaticCameraManager.Instance;
        if (scm != null)
            yield return StartCoroutine(scm.TriggerManualCapture(this, "Cleaning"));
        yield return new WaitForSeconds(cleanDuration);
        if (cleaningPutdownSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(cleaningPutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoPhoneUse()
    {
        if (phonePickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(phonePickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("PhoneUse");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride(phoneSpot);
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("PhoneUse");
        PlayAnim(STATE_PHONE);
        yield return new WaitForSeconds(activityHoldDuration);
        if (phonePutdownSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(phonePutdownSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            yield return ClearHeldObject();
            yield return new WaitForSeconds(0.5f);
        }
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
    }

    IEnumerator DoTyping()
    {
        Transform spot = ConsumeOverride(typingSpot);
        if (spot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Typing");
        PlayAnim(STATE_TYPING);
        yield return new WaitForSeconds(activityHoldDuration);
    }

    IEnumerator DoDadReading()
    {
        Transform spot = ConsumeOverride(dadReadingSpot ?? readingSpot);
        if (spot == null) { Warn("dadReadingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Reading");
        PlayAnim(STATE_READING);
        yield return new WaitForSeconds(activityHoldDuration);
    }

    IEnumerator DoDadPhone()
    {
        Transform spot = ConsumeOverride(dadPhoneSpot ?? phoneSpot);
        if (spot == null) { Warn("dadPhoneSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, false));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        SetActivity("PhoneUse");
        PlayAnim(STATE_PHONE);
        yield return new WaitForSeconds(activityHoldDuration);
    }

    IEnumerator DoDadCleaning()
    {
        Transform spot = ConsumeOverride(dadCleanSpot ?? cleanSpot);
        if (spot == null) { Warn("dadCleanSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, false));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        SetActivity("Cleaning");
        PlayAnim(STATE_CLEANING);
        lastAssignedActivity = "Cleaning";
        var scm = StaticCameraManager.Instance;
        if (scm != null)
            yield return StartCoroutine(scm.TriggerManualCapture(this, "Cleaning"));
        yield return new WaitForSeconds(cleanDuration);
    }

    IEnumerator DoPickUp()
    {
        SetActivity("PickingUp");
        PlayAnim(STATE_PICKING_UP);
        yield return new WaitForSeconds(pickUpDuration);
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoPutDown()
    {
        SetActivity("PuttingDown");
        PlayAnim(STATE_PUTTING_DOWN);
        yield return new WaitForSeconds(putDownDuration);
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoReturnToStanding()
    {
        if (isSitting)
        {
            isSitting          = false;
            PlayAnim(STATE_STANDING);
            Vector3 p          = transform.position;
            p.y                = 0f;
            transform.position = p;
            agent.Warp(transform.position);
            yield return null;
        }
        SetTVActive(false);
        StartCoroutine(PostDeviceState("tv", "off"));
        if (standingSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        }
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
        yield return ClearHeldObject();
    }

    IEnumerator NavWalkTo(Vector3 spotPos, bool useSeatTarget)
    {
        string savedActivity = lastAssignedActivity;
        lastAssignedActivity = "";
        agent.Warp(transform.position);

        float radius = useSeatTarget ? NAV_SAMPLE_RADIUS : 1.5f;
        NavMeshHit nmHit;
        if (!NavMesh.SamplePosition(spotPos, out nmHit, radius, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[{userID}] No NavMesh within {radius}m of {spotPos}.");
            PlayAnim(STATE_STANDING);
            yield break;
        }

        Vector3 walkTarget = new Vector3(nmHit.position.x, 0f, nmHit.position.z);
        NavMeshPath path   = new NavMeshPath();
        if (!agent.CalculatePath(walkTarget, path) ||
            path.status == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning($"[{userID}] Path invalid to {walkTarget}.");
            PlayAnim(STATE_STANDING);
            yield break;
        }

        PlayAnim(STATE_WALK);
        _shadowTimer = 0f;

        foreach (var rawCorner in path.corners)
        {
            Vector3 corner = new Vector3(rawCorner.x, 0f, rawCorner.z);
            bool    isLast = System.Array.IndexOf(path.corners, rawCorner) == path.corners.Length - 1;
            float   stop   = isLast ? ARRIVAL_THRESHOLD : 0.08f;

            while (true)
            {
                Vector3 cur  = new Vector3(transform.position.x, 0f, transform.position.z);
                float   dist = Vector3.Distance(cur, corner);
                if (dist <= stop) break;

                Vector3 dir    = (corner - cur).normalized;
                Vector3 side   = Vector3.Cross(dir, Vector3.up);
                float   jitter = UnityEngine.Random.Range(-JITTER_RADIUS, JITTER_RADIUS);
                transform.position = Vector3.MoveTowards(
                    cur,
                    corner + side * jitter * Mathf.Min(dist, 0.5f),
                    WALK_SPEED * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * ROTATION_SPEED);

                _shadowTimer += Time.deltaTime;
                if (_shadowTimer >= SHADOW_INTERVAL)
                {
                    _shadowTimer = 0f;
                    StartCoroutine(PostShadowPoint());
                }
                yield return null;
            }
        }

        transform.position   = new Vector3(walkTarget.x, 0f, walkTarget.z);
        PlayAnim(STATE_STANDING);
        lastAssignedActivity = savedActivity;
    }

    IEnumerator SmoothRotateTo(Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) yield break;
        Quaternion tgt = Quaternion.LookRotation(fwd.normalized);
        while (Quaternion.Angle(transform.rotation, tgt) > 0.5f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation, tgt, Time.deltaTime * ROTATION_SPEED);
            yield return null;
        }
        transform.rotation = tgt;
    }

    public void PreActivateHeldObject(string activityName)
    {
        if (_allItems == null) return;
        foreach (var bi in _allItems)
        {
            if (bi == null) continue;
            if (!string.Equals(bi.activity, activityName, StringComparison.OrdinalIgnoreCase)) continue;
            if (bi.item == null) { Debug.LogError($"[ERROR] bi.item is NULL for {activityName}"); return; }
            if (bi.item              != null) bi.item.SetActive(true);
            if (bi.item2             != null) bi.item2.SetActive(true);
            if (bi.sceneCounterpart  != null) bi.sceneCounterpart.SetActive(false);
            if (bi.sceneCounterpart2 != null) bi.sceneCounterpart2.SetActive(false);
            StartCoroutine(PostPickupEvent(bi.item.name));
            if (_dsm != null) _dsm.ForceObjectSync();
            Debug.Log($"[PreActivate] {userID} | {activityName} | held object activated");
            return;
        }
    }

    public IEnumerator ClearHeldObject() => PostPutdownEvent();

    IEnumerator PostPickupEvent(string objectName)
    {
        if (pickupMissRate > 0f && UnityEngine.Random.value < pickupMissRate)
        {
            Debug.Log($"[PickupEvent] MISS ({pickupMissRate:P0}): {objectName}");
            yield break;
        }
        string json = $"{{\"user_id\":\"{userID}\","
                    + $"\"object\":\"{objectName}\","
                    + $"\"pickup_time\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/object_event", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
        Debug.Log(req.result == UnityWebRequest.Result.Success
            ? $"[PickupEvent] OK: {userID} picked up {objectName}"
            : $"[PickupEvent] FAILED: {req.error}");
    }

    IEnumerator PostPutdownEvent()
    {
        if (_allItems != null)
        {
            foreach (var bi in _allItems)
            {
                if (bi == null) continue;
                if (bi.item              != null) bi.item.SetActive(false);
                if (bi.item2             != null) bi.item2.SetActive(false);
                if (bi.sceneCounterpart  != null) bi.sceneCounterpart.SetActive(true);
                if (bi.sceneCounterpart2 != null) bi.sceneCounterpart2.SetActive(true);
            }
        }
        if (putdownMissRate > 0f && UnityEngine.Random.value < putdownMissRate)
        {
            Debug.Log($"[PutdownEvent] MISS ({putdownMissRate:P0})");
            if (_dsm != null) _dsm.ForceObjectSync();
            yield break;
        }
        string json = $"{{\"user_id\":\"{userID}\","
                    + $"\"putdown_time\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/object_event", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
        Debug.Log(req.result == UnityWebRequest.Result.Success
            ? $"[PutdownEvent] OK: {userID} put down"
            : $"[PutdownEvent] FAILED: {req.error}");
        if (_dsm != null) _dsm.ForceObjectSync();
    }

    IEnumerator PostDeviceState(string label, string state)
    {
        string json = "{"
            + $"\"label\":\"{EscJson(label)}\","
            + $"\"state\":\"{EscJson(state)}\","
            + $"\"timestamp\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}\","
            + "\"source\":\"unity\""
            + "}";
        using var req = new UnityWebRequest($"{backendUrl}/device_state", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
    }

    IEnumerator PostShadowPoint()
    {
        string intent  = !string.IsNullOrEmpty(lastAssignedActivity)
            ? lastAssignedActivity : "Walking";
        string hourStr = currentVirtualHour >= 0f
            ? currentVirtualHour.ToString("F1", Inv)
            : ((float)DateTime.Now.Hour).ToString("F1", Inv);
        Vector3 fwd    = transform.forward;
        string json = "{"
            + $"\"userID\":\"{EscJson(userID)}\","
            + $"\"x\":{transform.position.x.ToString("F3", Inv)},"
            + $"\"z\":{transform.position.z.ToString("F3", Inv)},"
            + $"\"forward_x\":{fwd.x.ToString("F3", Inv)},"
            + $"\"forward_z\":{fwd.z.ToString("F3", Inv)},"
            + "\"room_name\":\"\","
            + $"\"intent_action\":\"{EscJson(intent)}\","
            + $"\"virtual_hour\":{hourStr},"
            + $"\"timestamp\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}\""
            + "}";
        using var req = new UnityWebRequest($"{backendUrl}/track_position", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
    }

    void TeleportToSeat(Transform spot)
    {
        transform.position = spot.position;
        transform.rotation = spot.rotation;
        isSitting          = true;
        StartCoroutine(PostShadowPoint());
    }

    void SetTVActive(bool isOn)
    {
        foreach (var obj in tvScreenObjects)
            if (obj != null) obj.SetActive(isOn);
    }

    void SetActivity(string a)
    {
        currentActivity = a;
        if (_allItems == null) return;

        bool isTransition = a == "Walking"   || a == "StandUp"    ||
                            a == "PickingUp" || a == "PuttingDown";
        if (isTransition) return;

        if (_skeletonHelper != null)
            _skeletonHelper.OnActivityChanged(a);

        bool stateChanged = false;
        foreach (var bi in _allItems)
        {
            if (bi == null) continue;
            bool active = string.Equals(bi.activity, a, StringComparison.OrdinalIgnoreCase);
            if (bi.item              != null && bi.item.activeSelf  != active)
            { bi.item.SetActive(active);              stateChanged = true; }
            if (bi.item2             != null && bi.item2.activeSelf != active)
            { bi.item2.SetActive(active);             stateChanged = true; }
            if (bi.sceneCounterpart  != null && bi.sceneCounterpart.activeSelf  == active)
            { bi.sceneCounterpart.SetActive(!active);  stateChanged = true; }
            if (bi.sceneCounterpart2 != null && bi.sceneCounterpart2.activeSelf == active)
            { bi.sceneCounterpart2.SetActive(!active); stateChanged = true; }
        }
        if (stateChanged && _dsm != null)
            _dsm.ForceObjectSync();
    }

    void PlayAnim(string s)
    {
        int hash = Animator.StringToHash(s);
        if (anim.HasState(0, hash))
            anim.Play(hash, 0, 0f);
        else
            Debug.LogWarning($"[{userID}] Animator state '{s}' NOT FOUND");
    }

    void Warn(string s) => Debug.LogWarning($"[{userID}] {s} not set");

    static string EscJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot,           Color.cyan,    "Drink");
        DrawSpot(sittingDrinkSpot,    Color.blue,    "SitDrink");
        DrawSpot(sittingSpot,         Color.blue,    "Sitting");
        DrawSpot(eatSpot,             Color.yellow,  "Eat");
        DrawSpot(cookSpot,            Color.red,     "Cook");
        DrawSpot(openSpot,            Color.white,   "Open");
        DrawSpot(layingSpot,          Color.green,   "Laying");
        DrawSpot(watchingSpot,        Color.magenta, "Watch");
        DrawSpot(readingSpot,         Color.blue,    "Read");
        DrawSpot(cleanSpot,           Color.gray,    "Clean");
        DrawSpot(phoneSpot,           Color.magenta, "Phone");
        DrawSpot(typingSpot,          Color.red,     "Type");
        DrawSpot(cleaningPickupSpot,  Color.cyan,    "CleanPickup");
        DrawSpot(cleaningPutdownSpot, Color.magenta, "CleanPutdown");
        DrawSpot(cookingPickupSpot,   Color.cyan,    "CookPickup");
        DrawSpot(cookingPutdownSpot,  Color.magenta, "CookPutdown");
        DrawSpot(eatingPickupSpot,    Color.cyan,    "EatPickup");
        DrawSpot(eatingPutdownSpot,   Color.magenta, "EatPutdown");
        DrawSpot(drinkingPickupSpot,  Color.cyan,    "DrinkPickup");
        DrawSpot(drinkingPutdownSpot, Color.magenta, "DrinkPutdown");
        DrawSpot(readingPickupSpot,   Color.cyan,    "ReadPickup");
        DrawSpot(readingPutdownSpot,  Color.magenta, "ReadPutdown");
        DrawSpot(phonePickupSpot,     Color.cyan,    "PhonePickup");
        DrawSpot(phonePutdownSpot,    Color.magenta, "PhonePutdown");
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
        UnityEditor.Handles.Label(spot.position + Vector3.up * 0.35f, label);
#endif
    }
}