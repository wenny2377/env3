using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;

public class ExperimentRunner : MonoBehaviour
{
    [Header("User Settings")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Camera System")]
    public StaticCameraManager cameraManager;
    public VirtualCameraBrain  virtualCameraBrain;

    [Header("Camera Nodes")]
    public List<CameraNode> livingRoomNodes;
    public List<CameraNode> kitchenNodes;
    public List<CameraNode> dadRoomNodes;

    [Header("Run Mode")]
    public RunMode mode = RunMode.Demo;

    [Header("Experiment Type")]
    public ExperimentType experimentType = ExperimentType.Baseline;

    [Header("Experiment Settings")]
    public int   exp_totalObservations = 300;
    public int   episodesPerVirtualDay = 10;
    public bool  addNoiseEpisodes      = true;
    public int   noiseInterval         = 10;
    [Range(0f, 1f)]
    public float skipProbability       = 0.2f;

    [Header("Timing Settings")]
    public float waitAfterCapture    = 3.0f;
    public float waitBetweenEpisodes = 2.0f;
    public float minIntervalInSlot   = 1.5f;

    [Header("Corruption Settings")]
    [Tooltip("Pickup miss rate for corruption experiment (EPIC-KITCHENS baseline: 0.15)")]
    [Range(0f, 1f)]
    public float corruptionPickupMissRate  = 0.15f;
    [Tooltip("Putdown miss rate for corruption experiment")]
    [Range(0f, 1f)]
    public float corruptionPutdownMissRate = 0.10f;
    [Tooltip("Object label confusion rate for corruption experiment")]
    [Range(0f, 1f)]
    public float corruptionObjectConfusionRate = 0.10f;

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Options")]
    public bool runOnStart = true;

    [Header("Demo Mode Settings")]
    public float demoSettleTime = 2.5f;
    public float demoHoldTime   = 1.0f;

    [Header("Mom Spots - Drinking")]     public Transform[] momDrinkingSpots     = new Transform[3];
    [Header("Mom Spots - SittingDrink")] public Transform[] momSittingDrinkSpots = new Transform[3];
    [Header("Mom Spots - Sitting")]      public Transform[] momSittingSpots      = new Transform[3];
    [Header("Mom Spots - Eating")]       public Transform[] momEatingSpots       = new Transform[3];
    [Header("Mom Spots - Cooking")]      public Transform[] momCookingSpots      = new Transform[3];
    [Header("Mom Spots - Opening")]      public Transform[] momOpeningSpots      = new Transform[3];
    [Header("Mom Spots - Laying")]       public Transform[] momLayingSpots       = new Transform[3];
    [Header("Mom Spots - Watching")]     public Transform[] momWatchingSpots     = new Transform[3];
    [Header("Mom Spots - Reading")]      public Transform[] momReadingSpots      = new Transform[3];
    [Header("Mom Spots - Cleaning")]     public Transform[] momCleaningSpots     = new Transform[3];
    [Header("Mom Spots - PhoneUse")]     public Transform[] momPhoneSpots        = new Transform[3];

    [Header("Dad Spots - Drinking")]     public Transform[] dadDrinkingSpots     = new Transform[3];
    [Header("Dad Spots - SittingDrink")] public Transform[] dadSittingDrinkSpots = new Transform[3];
    [Header("Dad Spots - Sitting")]      public Transform[] dadSittingSpots      = new Transform[3];
    [Header("Dad Spots - Eating")]       public Transform[] dadEatingSpots       = new Transform[3];
    [Header("Dad Spots - Cooking")]      public Transform[] dadCookingSpots      = new Transform[3];
    [Header("Dad Spots - Opening")]      public Transform[] dadOpeningSpots      = new Transform[3];
    [Header("Dad Spots - Laying")]       public Transform[] dadLayingSpots       = new Transform[3];
    [Header("Dad Spots - Typing")]       public Transform[] dadTypingSpots       = new Transform[3];
    [Header("Dad Spots - Reading")]      public Transform[] dadReadingSpots      = new Transform[3];
    [Header("Dad Spots - Cleaning")]     public Transform[] dadCleaningSpots     = new Transform[3];
    [Header("Dad Spots - PhoneUse")]     public Transform[] dadPhoneSpots        = new Transform[3];

    public enum RunMode        { Demo, Experiment }
    public enum ExperimentType { Baseline, Corruption }

    public static int    CurrentVirtualDay     = 1;
    public static float  CurrentVirtualHour    = 7f;
    public static string CurrentTimeSlot       = "Morning";
    public static bool   UseVirtualDay         = false;
    public static string CurrentExperimentMode = "";

    static readonly HashSet<string> TV_ON_BEHAVIORS = new HashSet<string> { "Watching" };

    static readonly string[] MomDemoActions = new[]
    {
        "Drinking", "SittingDrink", "Sitting", "Eating",
        "Cooking",  "Opening",      "Laying",  "Watching",
        "Reading",  "Cleaning",     "PhoneUse"
    };

    static readonly string[] DadDemoActions = new[]
    {
        "Drinking", "SittingDrink", "Sitting", "Eating",
        "Cooking",  "Opening",      "Laying",  "Typing",
        "Reading",  "Cleaning",     "PhoneUse"
    };

    struct BehaviorSequence
    {
        public string[] actions;
        public string   groundTruth;
        public int      weight;
    }

    struct TimeSlot
    {
        public string             name;
        public float              virtualHour;
        public BehaviorSequence[] momSequences;
        public BehaviorSequence[] dadSequences;
    }

    static readonly string[] NoiseActions = { "Standing" };

    // ── Time slot sequences ───────────────────────────────────────────────────
    // Design rationale: ADL literature (CASAS, MIT PlacesADL) + circadian patterns
    // Each slot contains weighted behavioral sequences reflecting realistic transitions.
    // Single-action sequences ensure all activity classes are captured.
    // Multi-action sequences simulate naturalistic activity chains.
    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name = "Morning", virtualHour = 7f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Opening","Drinking" },         groundTruth = "Drinking",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Opening","Cooking","Eating" }, groundTruth = "Eating",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating","Cleaning"},  groundTruth = "Cleaning",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating" },            groundTruth = "Eating",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "Opening","SittingDrink" },      groundTruth = "SittingDrink", weight = 2 },
                new BehaviorSequence { actions = new[]{ "Cleaning" },                    groundTruth = "Cleaning",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                    groundTruth = "PhoneUse",     weight = 1 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Drinking" },                    groundTruth = "Drinking",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Opening","Eating","Typing" },   groundTruth = "Typing",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Opening","Eating" },            groundTruth = "Eating",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","Typing" },             groundTruth = "Typing",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                    groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "SittingDrink" },                groundTruth = "SittingDrink", weight = 2 },
                new BehaviorSequence { actions = new[]{ "Sitting" },                     groundTruth = "Sitting",      weight = 1 },
            },
        },
        new TimeSlot {
            name = "Noon", virtualHour = 12f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Sitting","Reading" },            groundTruth = "Reading",      weight = 4 },
                new BehaviorSequence { actions = new[]{ "Reading","SittingDrink","Laying"},groundTruth = "Laying",      weight = 3 },
                new BehaviorSequence { actions = new[]{ "SittingDrink","Laying" },        groundTruth = "Laying",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "Eating" },                       groundTruth = "Eating",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "Reading" },                      groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 1 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Laying" },                       groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Laying","Watching" },            groundTruth = "Watching",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","Laying" },              groundTruth = "Laying",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching" },                     groundTruth = "Watching",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Sitting" },                      groundTruth = "Sitting",      weight = 1 },
            },
        },
        new TimeSlot {
            name = "Afternoon", virtualHour = 15f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Cleaning","Reading" },           groundTruth = "Reading",      weight = 3 },
                new BehaviorSequence { actions = new[]{ "Cleaning" },                     groundTruth = "Cleaning",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Reading","SittingDrink" },       groundTruth = "SittingDrink", weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","Reading" },            groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "SittingDrink" },                 groundTruth = "SittingDrink", weight = 1 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Typing","PhoneUse","Typing" },   groundTruth = "Typing",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Typing" },                       groundTruth = "Typing",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Typing" },            groundTruth = "Typing",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting" },                      groundTruth = "Sitting",      weight = 1 },
            },
        },
        new TimeSlot {
            name = "Evening", virtualHour = 19f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Cooking","Eating","Watching" },  groundTruth = "Watching",     weight = 4 },
                new BehaviorSequence { actions = new[]{ "Eating","Watching" },            groundTruth = "Watching",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating" },             groundTruth = "Eating",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching","SittingDrink" },      groundTruth = "Watching",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching" },                     groundTruth = "Watching",     weight = 1 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Eating","PhoneUse","SittingDrink"},groundTruth = "PhoneUse",   weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","PhoneUse" },             groundTruth = "PhoneUse",   weight = 3 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","SittingDrink" },       groundTruth = "SittingDrink",weight = 2 },
                new BehaviorSequence { actions = new[]{ "Eating","Watching" },             groundTruth = "Watching",   weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                      groundTruth = "PhoneUse",   weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching" },                      groundTruth = "Watching",   weight = 1 },
            },
        },
        new TimeSlot {
            name = "Night", virtualHour = 23f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Reading","Laying" },             groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Laying" },                       groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","Reading","Laying" },   groundTruth = "Laying",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "SittingDrink","Reading" },       groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "Reading" },                      groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 1 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "PhoneUse","Watching","Laying" }, groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Laying" },            groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Watching","Laying" },            groundTruth = "Laying",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse" },                     groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Watching" },          groundTruth = "Watching",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Sitting" },                      groundTruth = "Sitting",      weight = 1 },
            },
        },
    };

    int   totalRuns   = 0;
    int   successRuns = 0;
    int   skippedRuns = 0;
    int   noiseRuns   = 0;
    bool  isRunning   = false;
    bool  flaskReady  = false;

    static readonly System.Globalization.CultureInfo InvCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    static readonly HashSet<string> HeldObjectActions = new HashSet<string>
    {
        "Eating", "Drinking", "SittingDrink", "Cooking",
        "Cleaning", "Reading", "PhoneUse", "Watching"
    };

    void Start()
    {
        WarpUserToSpot(userMom);
        WarpUserToSpot(userDad);

        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance ?? FindObjectOfType<StaticCameraManager>();
        InitCamera();

        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);

        if (mode == RunMode.Demo)
        {
            StartCoroutine(RunDemoScan());
            return;
        }
        StartCoroutine(PollUntilReady());
    }

    void Update()
    {
        if (mode == RunMode.Demo) return;

        if (flaskReady && !runOnStart && !isRunning && Input.GetKeyDown(KeyCode.Space))
            StartExperiment();

        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            CurrentExperimentMode = "";
            isRunning             = false;
            Debug.Log("[ExperimentRunner] Stopped.");
        }
    }

    IEnumerator RunDemoScan()
    {
        yield return new WaitForSeconds(0.5f);
        Debug.Log("[Demo] Skeleton scan start");
        userDad.gameObject.SetActive(false);
        userMom.gameObject.SetActive(true);
        Debug.Log("[Demo] USER MOM");
        yield return StartCoroutine(DemoScanUser(userMom, MomDemoActions, isMom: true));
        yield return StartCoroutine(userMom.ReturnToStanding());
        yield return new WaitForSeconds(1f);
        userMom.gameObject.SetActive(false);
        userDad.gameObject.SetActive(true);
        Debug.Log("[Demo] USER DAD");
        yield return StartCoroutine(DemoScanUser(userDad, DadDemoActions, isMom: false));
        yield return StartCoroutine(userDad.ReturnToStanding());
        userMom.gameObject.SetActive(true);
        userDad.gameObject.SetActive(true);
        Debug.Log("[Demo] Scan complete.");
    }

    IEnumerator DemoScanUser(UserEntity user, string[] actions, bool isMom)
    {
        foreach (string action in actions)
        {
            Transform spot = isMom ? GetMomSpot(action, 0) : GetDadSpot(action, 0);
            if (spot == null) { Debug.LogWarning($"[Demo] No spot: {user.userID}/{action}"); continue; }
            user.overrideSpot         = spot;
            user.lastAssignedActivity = action;
            user.ResetBusy();
            yield return StartCoroutine(user.SwitchActivity(action));
            yield return new WaitForSeconds(demoSettleTime);
            yield return new WaitForSeconds(demoHoldTime);
        }
    }

    IEnumerator PollUntilReady()
    {
        Debug.Log("[ExperimentRunner] Polling /ready...");
        while (true)
        {
            using var req = UnityWebRequest.Get($"{backendUrl}/ready");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = req.downloadHandler.text;
                if (data.Contains("\"ready\":true") || data.Contains("\"ready\": true"))
                {
                    Debug.Log("[ExperimentRunner] Flask ready.");
                    flaskReady = true;
                    if (runOnStart) { yield return new WaitForSeconds(2f); StartExperiment(); }
                    else Debug.Log("[ExperimentRunner] Press Space to start.");
                    yield break;
                }
            }
            yield return new WaitForSeconds(3f);
        }
    }

    public void StartExperiment()
    {
        if (isRunning || mode == RunMode.Demo) return;
        totalRuns = successRuns = skippedRuns = noiseRuns = 0;

        bool isCorruption = experimentType == ExperimentType.Corruption;

        if (userMom != null)
        {
            userMom.pickupMissRate  = isCorruption ? corruptionPickupMissRate  : 0.0f;
            userMom.putdownMissRate = isCorruption ? corruptionPutdownMissRate : 0.0f;
            userMom.SetSkeletonNoise(isCorruption);
        }
        if (userDad != null)
        {
            userDad.pickupMissRate  = isCorruption ? corruptionPickupMissRate  : 0.0f;
            userDad.putdownMissRate = isCorruption ? corruptionPutdownMissRate : 0.0f;
            userDad.SetSkeletonNoise(isCorruption);
        }

        var dsm = FindObjectOfType<DynamicSyncManager>();
        if (dsm != null)
            dsm.objectConfusionRate = isCorruption ? corruptionObjectConfusionRate : 0.0f;

        Debug.Log($"[ExperimentRunner] Start | type={experimentType} | "
                + $"corruption={isCorruption} | "
                + $"pickupMiss={userMom?.pickupMissRate:P0} | "
                + $"objConfusion={dsm?.objectConfusionRate:P0}");

        StartCoroutine(PostExperimentType(experimentType.ToString().ToLower()));
        StartCoroutine(RunExperiment());
    }

    IEnumerator PostExperimentType(string expType)
    {
        string json = $"{{\"type\":\"{expType}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/set_experiment_type", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;
        yield return StartCoroutine(RunObservationExp());
        CurrentExperimentMode = "";
        UseVirtualDay         = false;
        isRunning             = false;
        Debug.Log($"[ExperimentRunner] Done. Regular={successRuns} Skip={skippedRuns} Noise={noiseRuns}");
        StartCoroutine(PostExperimentDone());
    }

    IEnumerator PostExperimentDone()
    {
        string json = $"{{\"mode\":\"Experiment\",\"type\":\"{experimentType.ToString().ToLower()}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/experiment_done", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
    }

    IEnumerator RunObservationExp()
    {
        UseVirtualDay         = true;
        CurrentExperimentMode = "experiment";
        CurrentVirtualDay     = 1;
        CurrentVirtualHour    = TimeSlots[0].virtualHour;
        CurrentTimeSlot       = TimeSlots[0].name;
        cameraManager.captureMode = StaticCameraManager.CaptureMode.EventDriven;

        int totalDays    = exp_totalObservations / episodesPerVirtualDay;
        int epPerSlot    = Mathf.Max(1, episodesPerVirtualDay / TimeSlots.Length);
        int episodeCount = 0;

        for (int day = 1; day <= totalDays; day++)
        {
            CurrentVirtualDay = day;

            foreach (var slot in TimeSlots)
            {
                CurrentVirtualHour = slot.virtualHour;
                CurrentTimeSlot    = slot.name;
                SetUsersVirtualHour(slot.virtualHour);
                PostVirtualHourFireAndForget(slot.virtualHour);

                var momQ   = BuildSequenceQueue(slot.momSequences, epPerSlot);
                var dadQ   = BuildSequenceQueue(slot.dadSequences, epPerSlot);
                int maxLen = Mathf.Max(momQ.Count, dadQ.Count);

                for (int i = 0; i < maxLen; i++)
                {
                    if (i < momQ.Count)
                    {
                        totalRuns++;
                        if (Random.value < skipProbability) { skippedRuns++; }
                        else
                        {
                            yield return StartCoroutine(
                                RunSequenceEpisode(userMom, momQ[i], episodeCount));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }
                        if (addNoiseEpisodes && episodeCount > 0 &&
                            episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(RunNoiseEpisode(userMom));
                            noiseRuns++;
                        }
                    }

                    if (i < dadQ.Count)
                    {
                        totalRuns++;
                        if (Random.value < skipProbability) { skippedRuns++; }
                        else
                        {
                            yield return StartCoroutine(
                                RunSequenceEpisode(userDad, dadQ[i], episodeCount));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }
                        if (addNoiseEpisodes && episodeCount > 0 &&
                            episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(RunNoiseEpisode(userDad));
                            noiseRuns++;
                        }
                    }
                }
                yield return StartCoroutine(PostCheckpoint(day, slot.name, episodeCount));
            }
            Debug.Log($"[Experiment] Day {day}/{totalDays} "
                    + $"success={successRuns} skip={skippedRuns}");
        }
    }

    IEnumerator RunSequenceEpisode(
        UserEntity targetUser, BehaviorSequence seq, int episodeIndex = 0)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        var _dsm = FindObjectOfType<DynamicSyncManager>();
        if (_dsm != null) _dsm.ForceObjectSync();
        yield return new WaitForSeconds(0.5f);

        if (virtualCameraBrain != null)
            virtualCameraBrain.SetVirtualHour(CurrentVirtualHour);
        SetUsersVirtualHour(CurrentVirtualHour);

        bool tvOn = TV_ON_BEHAVIORS.Contains(seq.groundTruth);
        yield return StartCoroutine(SetDeviceState("tv", tvOn ? "on" : "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(tvOn);
        yield return new WaitForSeconds(1.5f);

        int lastIdx = seq.actions.Length - 1;
        for (int i = 0; i < lastIdx; i++)
        {
            targetUser.lastAssignedActivity = seq.actions[i];
            targetUser.ResetBusy();
            yield return StartCoroutine(targetUser.SwitchActivity(seq.actions[i]));
            yield return new WaitForSeconds(0.5f);
        }

        Transform spot = (targetUser == userMom)
            ? GetMomSpot(seq.groundTruth, episodeIndex)
            : GetDadSpot(seq.groundTruth, episodeIndex);
        if (spot != null) targetUser.overrideSpot = spot;

        if (HeldObjectActions.Contains(seq.groundTruth))
        {
            targetUser.PreActivateHeldObject(seq.groundTruth);
            yield return new WaitForSeconds(1.0f);
        }

        targetUser.lastAssignedActivity = seq.actions[lastIdx];
        targetUser.ResetBusy();
        yield return StartCoroutine(targetUser.SwitchActivity(seq.actions[lastIdx]));

        targetUser.lastAssignedActivity = seq.groundTruth;
        yield return new WaitForSeconds(waitAfterCapture);
        targetUser.lastAssignedActivity = "";

        yield return StartCoroutine(SetDeviceState("tv", "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(false);

        yield return StartCoroutine(targetUser.ReturnToStanding());
        yield return new WaitForSeconds(3.0f);

        if (other != null) { WarpUserToSpot(other); other.gameObject.SetActive(true); }
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunNoiseEpisode(UserEntity user)
    {
        string     noise = NoiseActions[Random.Range(0, NoiseActions.Length)];
        UserEntity other = (user == userMom) ? userDad : userMom;
        if (other != null) other.gameObject.SetActive(false);
        if (user  != null) user.gameObject.SetActive(true);

        if (virtualCameraBrain != null)
            virtualCameraBrain.SetVirtualHour(CurrentVirtualHour);

        user.lastAssignedActivity = noise;
        user.ResetBusy();
        yield return StartCoroutine(user.SwitchActivity(noise));
        yield return new WaitForSeconds(waitAfterCapture);
        user.lastAssignedActivity = "";

        yield return StartCoroutine(user.ReturnToStanding());
        if (other != null) { WarpUserToSpot(other); other.gameObject.SetActive(true); }
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator SetDeviceState(string label, string state)
    {
        string json = $"{{\"label\":\"{label}\",\"state\":\"{state}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/set_device_state", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    IEnumerator PostCheckpoint(int day, string slotName, int episodeCount)
    {
        string[] checkUsers   = { "User_Mom", "User_Dad" };
        string[] checkActions = {
            "Watching","Eating","Sitting","Drinking","Reading","Typing","PhoneUse","Laying"
        };
        foreach (string uid in checkUsers)
        {
            foreach (string act in checkActions)
            {
                string json = "{"
                    + $"\"episode\":{episodeCount},"
                    + $"\"user_id\":\"{uid}\","
                    + $"\"action\":\"{act}\","
                    + $"\"day\":{day},"
                    + $"\"slot\":\"{slotName}\""
                    + "}";
                using var req = new UnityWebRequest($"{backendUrl}/exp_checkpoint", "POST");
                req.uploadHandler   = new UploadHandlerRaw(
                    System.Text.Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 5;
                yield return req.SendWebRequest();
            }
        }
        Debug.Log($"[Checkpoint] day={day} slot={slotName} ep={episodeCount}");
    }

    void WarpUserToSpot(UserEntity user)
    {
        if (user == null || user.standingSpot == null) return;
        user.transform.position = user.standingSpot.position;
        user.transform.rotation = user.standingSpot.rotation;
        var ag = user.GetComponent<NavMeshAgent>();
        if (ag != null) ag.Warp(user.standingSpot.position);
    }

    void InitCamera()
    {
        if (cameraManager == null) return;
        if (kitchenNodes?.Count    > 0) cameraManager.RegisterRoomCameras("Kitchen",    kitchenNodes);
        if (livingRoomNodes?.Count > 0) cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
        if (dadRoomNodes?.Count    > 0) cameraManager.RegisterRoomCameras("DadRoom",    dadRoomNodes);
        if (virtualCameraBrain != null) cameraManager.virtualCameraBrain = virtualCameraBrain;
    }

    void SetUsersVirtualHour(float hour)
    {
        if (userMom != null) userMom.currentVirtualHour = hour;
        if (userDad != null) userDad.currentVirtualHour = hour;
    }

    Transform GetSpot(Transform[] spots, int idx)
    {
        if (spots == null || spots.Length == 0) return null;
        var valid = System.Array.FindAll(spots, s => s != null);
        return valid.Length == 0 ? null : valid[idx % valid.Length];
    }

    Transform GetMomSpot(string behavior, int idx) => behavior switch
    {
        "Drinking"     => GetSpot(momDrinkingSpots,     idx),
        "SittingDrink" => GetSpot(momSittingDrinkSpots, idx),
        "Sitting"      => GetSpot(momSittingSpots,      idx),
        "Eating"       => GetSpot(momEatingSpots,       idx),
        "Cooking"      => GetSpot(momCookingSpots,      idx),
        "Opening"      => GetSpot(momOpeningSpots,      idx),
        "Laying"       => GetSpot(momLayingSpots,       idx),
        "Watching"     => GetSpot(momWatchingSpots,     idx),
        "Reading"      => GetSpot(momReadingSpots,      idx),
        "Cleaning"     => GetSpot(momCleaningSpots,     idx),
        "PhoneUse"     => GetSpot(momPhoneSpots,        idx),
        _              => null,
    };

    Transform GetDadSpot(string behavior, int idx) => behavior switch
    {
        "Drinking"     => GetSpot(dadDrinkingSpots,     idx),
        "SittingDrink" => GetSpot(dadSittingDrinkSpots, idx),
        "Sitting"      => GetSpot(dadSittingSpots,      idx),
        "Eating"       => GetSpot(dadEatingSpots,       idx),
        "Cooking"      => GetSpot(dadCookingSpots,      idx),
        "Opening"      => GetSpot(dadOpeningSpots,      idx),
        "Laying"       => GetSpot(dadLayingSpots,       idx),
        "Typing"       => GetSpot(dadTypingSpots,       idx),
        "Reading"      => GetSpot(dadReadingSpots,      idx),
        "Cleaning"     => GetSpot(dadCleaningSpots,     idx),
        "PhoneUse"     => GetSpot(dadPhoneSpots,        idx),
        _              => null,
    };

    List<BehaviorSequence> BuildSequenceQueue(
        BehaviorSequence[] sequences, int totalCount)
    {
        int totalWeight = 0;
        foreach (var s in sequences) totalWeight += s.weight;

        var result    = new List<BehaviorSequence>();
        int allocated = 0;
        for (int i = 0; i < sequences.Length; i++)
        {
            int count = (i == sequences.Length - 1)
                ? Mathf.Max(0, totalCount - allocated)
                : Mathf.Max(0, Mathf.RoundToInt(
                    (float)sequences[i].weight / totalWeight * totalCount));
            for (int j = 0; j < count; j++) result.Add(sequences[i]);
            allocated += count;
        }
        var rng = new System.Random();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    void PostVirtualHourFireAndForget(float hour) =>
        StartCoroutine(PostVirtualHourRoutine(hour));

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour.ToString("F1", InvCulture)}}}";
        using var req = new UnityWebRequest($"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler   = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    void OnGUI()
    {
        if (mode == RunMode.Demo)
        {
            GUI.Label(new Rect(10, 10, 600, 22),
                "[Demo] Skeleton scan — check Console");
            return;
        }
        int totalDays = exp_totalObservations / episodesPerVirtualDay;
        string status = isRunning ? "" :
            (flaskReady ? "[Ready] Press Space" : "[Waiting Flask...]");
        GUI.Label(new Rect(10, 10, 1400, 22),
            $"[{experimentType}] {CurrentTimeSlot} {CurrentVirtualHour:F0}:00  "
          + $"Day={CurrentVirtualDay}/{totalDays}  "
          + $"Skip={skippedRuns}  Noise={noiseRuns}  Regular={successRuns}  "
          + $"Total={totalRuns}/{exp_totalObservations}  {status}");
    }
}