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
    public float drinkDuration        = 2.0f;
    public float eatDuration          = 3.0f;
    public float cookDuration         = 3.0f;
    public float openDuration         = 2.0f;
    public float cleanDuration        = 3.0f;
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

    [HideInInspector] public float     pickupMissRate       = 0.0f;
    [HideInInspector] public float     putdownMissRate      = 0.0f;
    [HideInInspector] public bool      skipReturnToStanding = false;
    [HideInInspector] public Transform overrideSpot         = null;
    [HideInInspector] public float     currentVirtualHour   = -1f;
    [HideInInspector] public string    lastAssignedActivity = "";

    public string GroundTruthLabel =>
        JsonUtil.ToGroundTruthLabel(
            !string.IsNullOrEmpty(lastAssignedActivity)
                ? lastAssignedActivity
                : currentActivity);

    const float WALK_SPEED        = 1.4f;
    const float ARRIVAL_THRESHOLD = 0.15f;
    const float ROTATION_SPEED    = 8f;
    const float NAV_SAMPLE_RADIUS = 3.0f;
    const float SHADOW_INTERVAL   = 0.5f;
    const float JITTER_RADIUS     = 0.1f;

    const string STATE_STANDING        = "Standing";
    const string STATE_WALK            = "Walking";
    const string STATE_DRINK           = "Drinking";
    const string STATE_DRINKING_SEATED = "SeatedDrinking";
    const string STATE_SITTING         = "Sitting";
    const string STATE_STAND_UP        = "StandUp";
    const string STATE_LAYING          = "Laying";
    const string STATE_READING         = "Reading";
    const string STATE_TYPING          = "Typing";
    const string STATE_WATCHING        = "Watching";
    const string STATE_USING_PHONE     = "UsingPhone";
    const string STATE_EATING          = "Eating";
    const string STATE_COOKING         = "Cooking";
    const string STATE_CLEANING        = "Cleaning";
    const string STATE_OPENING         = "Opening";
    const string STATE_PICKING_UP      = "PickingUp";
    const string STATE_PUTTING_DOWN    = "PuttingDown";

    public string currentActivity { get; private set; } = "Standing";
    public bool   IsBusy          { get; private set; } = false;

    public Vector3        GetAimPosition()   => transform.position + Vector3.up * 1.2f;
    public void           ResetBusy()        => IsBusy = false;
    public BehaviorItem[] GetBehaviorItems() => _allItems;

    public void SetSkeletonNoise(bool enabled, float std = 15f)
    {
        if (_skeletonHelper != null)
            _skeletonHelper.SetSkeletonNoise(enabled, std);
    }

    Animator           anim;
    NavMeshAgent       agent;
    bool               isSitting    = false;
    float              _shadowTimer = 0f;
    BehaviorItem[]     _allItems;
    DynamicSyncManager _dsm;
    SkeletonHelper     _skeletonHelper;

    static readonly HashSet<string> SeatActions = new HashSet<string>
    {
        "Sitting", "SeatedDrinking", "Eating", "Laying",
        "Watching", "Typing", "Reading"
    };

    static readonly HashSet<string> HeldActions = new HashSet<string>
    {
        "SeatedDrinking", "Drinking", "Eating", "Cooking",
        "Reading", "UsingPhone"
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
        sittingDrinkItem.activity = "SeatedDrinking";
        eatItem.activity          = "Eating";
        cookItem.activity         = "Cooking";
        cleanItem.activity        = "Cleaning";
        readItem.activity         = "Reading";
        phoneItem.activity        = "UsingPhone";

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

    Transform ConsumeOverride(Transform fallback = null)
    {
        Transform result = overrideSpot != null ? overrideSpot : fallback;
        overrideSpot = null;
        return result;
    }

    string ActionToAnimState(string action) => action switch
    {
        "Cooking"        => STATE_COOKING,
        "Eating"         => STATE_EATING,
        "Sitting"        => STATE_SITTING,
        "SeatedDrinking" => STATE_DRINKING_SEATED,
        "Drinking"       => STATE_DRINK,
        "Watching"       => STATE_WATCHING,
        "Typing"         => STATE_TYPING,
        "Reading"        => STATE_READING,
        "Laying"         => STATE_LAYING,
        "UsingPhone"     => STATE_USING_PHONE,
        "Cleaning"       => STATE_CLEANING,
        "Opening"        => STATE_OPENING,
        _                => STATE_STANDING,
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
            case "drinking":
                lastAssignedActivity = "Drinking";
                yield return StartCoroutine(DoDrink());
                lastAssignedActivity = "";
                break;

            case "seateddrinking":
                lastAssignedActivity = "SeatedDrinking";
                yield return StartCoroutine(DoSittingDrink());
                lastAssignedActivity = "";
                break;

            case "sitting":
                lastAssignedActivity = "Sitting";
                yield return StartCoroutine(DoSitting());
                lastAssignedActivity = "";
                break;

            case "standup":
            case "stand up":
                lastAssignedActivity = "StandUp";
                yield return StartCoroutine(DoStandUp());
                lastAssignedActivity = "";
                break;

            case "eat":
            case "eating":
                lastAssignedActivity = "Eating";
                yield return StartCoroutine(DoEat());
                lastAssignedActivity = "";
                break;

            case "cook":
            case "cooking":
                lastAssignedActivity = "Cooking";
                yield return StartCoroutine(DoCook());
                lastAssignedActivity = "";
                break;

            case "open":
            case "opening":
                lastAssignedActivity = "Opening";
                yield return StartCoroutine(DoOpen());
                lastAssignedActivity = "";
                break;

            case "laying":
            case "sleep":
                lastAssignedActivity = "Laying";
                yield return StartCoroutine(DoLaying());
                lastAssignedActivity = "";
                break;

            case "watch":
            case "watching":
                lastAssignedActivity = "Watching";
                yield return StartCoroutine(DoWatching());
                lastAssignedActivity = "";
                break;

            case "read":
            case "reading":
            case "dadreading":
                lastAssignedActivity = "Reading";
                yield return StartCoroutine(DoReading());
                lastAssignedActivity = "";
                break;

            case "clean":
            case "cleaning":
            case "dadclean":
            case "dadcleaning":
                lastAssignedActivity = "Cleaning";
                yield return StartCoroutine(DoCleaning());
                lastAssignedActivity = "";
                break;

            case "phone":
            case "usingphone":
            case "dadphone":
                lastAssignedActivity = "UsingPhone";
                yield return StartCoroutine(DoPhoneUse());
                lastAssignedActivity = "";
                break;

            case "type":
            case "typing":
                lastAssignedActivity = "Typing";
                yield return StartCoroutine(DoTyping());
                lastAssignedActivity = "";
                break;

            case "pickup":
            case "pickingup":
                lastAssignedActivity = "PickingUp";
                yield return StartCoroutine(DoPickUp());
                lastAssignedActivity = "";
                break;

            case "putdown":
            case "puttingdown":
                lastAssignedActivity = "PuttingDown";
                yield return StartCoroutine(DoPutDown());
                lastAssignedActivity = "";
                break;

            case "standing":
                lastAssignedActivity = "Standing";
                yield return StartCoroutine(DoReturnToStanding());
                lastAssignedActivity = "";
                break;

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
        Transform spot = ConsumeOverride();
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoSittingDrink()
    {
        if (drinkingPickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(drinkingPickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("SeatedDrinking");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride();
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, true));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
            TeleportToSeat(spot);
        }
        SetActivity("SeatedDrinking");
        PlayAnim(STATE_DRINKING_SEATED);
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoSitting()
    {
        Transform spot = ConsumeOverride();
        if (spot == null) { Warn("sittingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Sitting");
        PlayAnim(STATE_SITTING);
        yield return new WaitForSeconds(activityHoldDuration);
        yield return StartCoroutine(DoStandUp());
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
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
        Transform spot = ConsumeOverride();
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
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
        Transform spot = ConsumeOverride();
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoOpen()
    {
        Transform spot = ConsumeOverride();
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
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
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
        Transform spot = ConsumeOverride();
        if (spot == null) { Warn("layingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Laying");
        PlayAnim(STATE_LAYING);
        yield return new WaitForSeconds(activityHoldDuration);
        yield return StartCoroutine(DoStandUp());
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoWatching()
    {
        Transform spot = ConsumeOverride();
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
        SetTVActive(false);
        StartCoroutine(PostDeviceState("tv", "off"));
        yield return StartCoroutine(DoStandUp());
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
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
        Transform spot = ConsumeOverride();
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
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
        Transform spot = ConsumeOverride();
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("Cleaning");
        PlayAnim(STATE_CLEANING);
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoPhoneUse()
    {
        if (phonePickupSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(phonePickupSpot.position, false));
            yield return new WaitForSeconds(0.3f);
            PreActivateHeldObject("UsingPhone");
            yield return new WaitForSeconds(0.5f);
        }
        Transform spot = ConsumeOverride();
        if (spot != null)
        {
            yield return StartCoroutine(NavWalkTo(spot.position, false));
            yield return StartCoroutine(SmoothRotateTo(spot.forward));
        }
        SetActivity("UsingPhone");
        PlayAnim(STATE_USING_PHONE);
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
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoTyping()
    {
        Transform spot = ConsumeOverride();
        if (spot == null) { Warn("typingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(spot.position, true));
        yield return StartCoroutine(SmoothRotateTo(spot.forward));
        TeleportToSeat(spot);
        SetActivity("Typing");
        PlayAnim(STATE_TYPING);
        yield return new WaitForSeconds(activityHoldDuration);
        yield return StartCoroutine(DoStandUp());
        if (!skipReturnToStanding && standingSpot != null)
            yield return StartCoroutine(NavWalkTo(standingSpot.position, false));
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoPickUp()
    {
        SetActivity("PickingUp");
        PlayAnim(STATE_PICKING_UP);
        yield return new WaitForSeconds(0.8f);
        SetActivity("Standing");
        PlayAnim(STATE_STANDING);
    }

    IEnumerator DoPutDown()
    {
        SetActivity("PuttingDown");
        PlayAnim(STATE_PUTTING_DOWN);
        yield return new WaitForSeconds(0.8f);
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
            lastAssignedActivity = savedActivity;
            yield break;
        }

        Vector3 walkTarget = new Vector3(nmHit.position.x, 0f, nmHit.position.z);
        NavMeshPath path   = new NavMeshPath();
        if (!agent.CalculatePath(walkTarget, path) ||
            path.status == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning($"[{userID}] Path invalid to {walkTarget}.");
            PlayAnim(STATE_STANDING);
            lastAssignedActivity = savedActivity;
            yield break;
        }

        PlayAnim(STATE_WALK);
        _shadowTimer = 0f;

        Vector3[] corners = path.corners;
        for (int ci = 0; ci < corners.Length; ci++)
        {
            Vector3 corner = new Vector3(corners[ci].x, 0f, corners[ci].z);
            bool    isLast = ci == corners.Length - 1;
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
            if (!string.Equals(bi.activity, activityName,
                StringComparison.OrdinalIgnoreCase)) continue;
            if (bi.item == null)
            {
                Debug.LogError($"[ERROR] bi.item is NULL for {activityName}");
                return;
            }
            if (bi.item              != null) bi.item.SetActive(true);
            if (bi.item2             != null) bi.item2.SetActive(true);
            if (bi.sceneCounterpart  != null) bi.sceneCounterpart.SetActive(false);
            if (bi.sceneCounterpart2 != null) bi.sceneCounterpart2.SetActive(false);
            StartCoroutine(PostPickupEvent(bi.item.name));
            if (_dsm != null) _dsm.ForceObjectSync();
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
        if (_dsm != null) _dsm.ForceObjectSync();
    }

    IEnumerator PostDeviceState(string label, string state)
    {
        string json = "{"
            + $"\"label\":\"{JsonUtil.Esc(label)}\","
            + $"\"state\":\"{JsonUtil.Esc(state)}\","
            + $"\"timestamp\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fff}\","
            + "\"source\":\"unity\""
            + "}";
        using var req = new UnityWebRequest($"{backendUrl}/set_device_state", "POST");
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
            ? currentVirtualHour.ToString("F1", JsonUtil.Inv)
            : ((float)DateTime.Now.Hour).ToString("F1", JsonUtil.Inv);
        Vector3 fwd = transform.forward;
        string json = "{"
            + $"\"userID\":\"{JsonUtil.Esc(userID)}\","
            + $"\"x\":{transform.position.x.ToString("F3", JsonUtil.Inv)},"
            + $"\"z\":{transform.position.z.ToString("F3", JsonUtil.Inv)},"
            + $"\"forward_x\":{fwd.x.ToString("F3", JsonUtil.Inv)},"
            + $"\"forward_z\":{fwd.z.ToString("F3", JsonUtil.Inv)},"
            + "\"room_name\":\"\","
            + $"\"intent_action\":\"{JsonUtil.Esc(intent)}\","
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
            if (bi.item != null && bi.item.activeSelf != active)
            { bi.item.SetActive(active); stateChanged = true; }
            if (bi.item2 != null && bi.item2.activeSelf != active)
            { bi.item2.SetActive(active); stateChanged = true; }
            if (bi.sceneCounterpart != null && bi.sceneCounterpart.activeSelf == active)
            { bi.sceneCounterpart.SetActive(!active); stateChanged = true; }
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

    void OnDrawGizmos()
    {
        if (standingSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(standingSpot.position, 0.15f);
        }
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