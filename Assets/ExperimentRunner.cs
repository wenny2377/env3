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

    public enum RunMode { Demo, Experiment }

    public static int    CurrentVirtualDay     = 1;
    public static bool   UseVirtualDay         = false;
    public static string CurrentExperimentMode = "";

    static readonly HashSet<string> TV_ON_BEHAVIORS = new HashSet<string>
    {
        "Watching"
    };

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

    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name = "Morning", virtualHour = 7f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Opening","Drinking" },                  groundTruth = "Drinking",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Opening","Cooking","Eating" },          groundTruth = "Eating",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating","Cleaning" },         groundTruth = "Cleaning",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating" },                    groundTruth = "Eating",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Opening","SittingDrink" },              groundTruth = "SittingDrink", weight = 2 },
                new BehaviorSequence { actions = new[]{ "Cleaning" },                            groundTruth = "Cleaning",     weight = 2 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Drinking" },                            groundTruth = "Drinking",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Opening","Eating","Typing" },           groundTruth = "Typing",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Opening","Eating" },                    groundTruth = "Eating",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","Typing" },                     groundTruth = "Typing",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","PhoneUse" },                  groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "SittingDrink" },                        groundTruth = "SittingDrink", weight = 2 },
            },
        },
        new TimeSlot {
            name = "Noon", virtualHour = 12f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Sitting","Reading" },                   groundTruth = "Reading",      weight = 4 },
                new BehaviorSequence { actions = new[]{ "Reading","SittingDrink","Laying" },     groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "SittingDrink","Laying" },               groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating" },                              groundTruth = "Eating",       weight = 2 },
                new BehaviorSequence { actions = new[]{ "Sitting","Reading" },                   groundTruth = "Reading",      weight = 2 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Laying" },                              groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Laying","Watching" },                   groundTruth = "Watching",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","Laying" },                     groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","PhoneUse" },                  groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching" },                            groundTruth = "Watching",     weight = 2 },
            },
        },
        new TimeSlot {
            name = "Afternoon", virtualHour = 15f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Cleaning","Reading" },                  groundTruth = "Reading",      weight = 4 },
                new BehaviorSequence { actions = new[]{ "Cleaning" },                            groundTruth = "Cleaning",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Reading","SittingDrink" },              groundTruth = "SittingDrink", weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","Reading" },                   groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "Cleaning","Reading","SittingDrink" },   groundTruth = "SittingDrink", weight = 2 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Typing","PhoneUse","Typing" },          groundTruth = "Typing",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Typing" },                              groundTruth = "Typing",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Typing" },                   groundTruth = "Typing",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","PhoneUse" },                  groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Typing","PhoneUse" },                   groundTruth = "PhoneUse",     weight = 2 },
            },
        },
        new TimeSlot {
            name = "Evening", virtualHour = 19f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Cooking","Eating","Watching" },         groundTruth = "Watching",     weight = 4 },
                new BehaviorSequence { actions = new[]{ "Eating","Watching" },                   groundTruth = "Watching",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "Cooking","Eating" },                    groundTruth = "Eating",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Watching","SittingDrink" },             groundTruth = "Watching",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Watching" },                            groundTruth = "Watching",     weight = 2 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Eating","PhoneUse","SittingDrink" },    groundTruth = "PhoneUse",     weight = 4 },
                new BehaviorSequence { actions = new[]{ "Eating","PhoneUse" },                   groundTruth = "PhoneUse",     weight = 3 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","SittingDrink" },             groundTruth = "SittingDrink", weight = 3 },
                new BehaviorSequence { actions = new[]{ "Eating","Watching" },                   groundTruth = "Watching",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "Sitting","PhoneUse" },                  groundTruth = "PhoneUse",     weight = 2 },
            },
        },
        new TimeSlot {
            name = "Night", virtualHour = 23f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "Reading","Laying" },                    groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "Laying" },                              groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","Reading","Laying" },          groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "SittingDrink","Reading" },              groundTruth = "Reading",      weight = 2 },
                new BehaviorSequence { actions = new[]{ "Reading" },                             groundTruth = "Reading",      weight = 2 },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence { actions = new[]{ "PhoneUse","Watching","Laying" },        groundTruth = "Laying",       weight = 4 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Laying" },                   groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Watching","Laying" },                   groundTruth = "Laying",       weight = 3 },
                new BehaviorSequence { actions = new[]{ "Sitting","PhoneUse" },                  groundTruth = "PhoneUse",     weight = 2 },
                new BehaviorSequence { actions = new[]{ "PhoneUse","Watching" },                 groundTruth = "Watching",     weight = 2 },
            },
        },
    };

    int   totalRuns          = 0;
    int   successRuns        = 0;
    int   skippedRuns        = 0;
    int   noiseRuns          = 0;
    float currentVirtualHour = 7f;
    bool  isRunning          = false;
    bool  flaskReady         = false;

    static readonly System.Globalization.CultureInfo InvCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    void Start()
    {
        WarpUserToSpot(userMom);
        WarpUserToSpot(userDad);

        if (mode == RunMode.Demo)
        {
            if (userMom != null) userMom.gameObject.SetActive(true);
            if (userDad != null) userDad.gameObject.SetActive(true);
            InitCamera();
            StartCoroutine(RunDemoScan());
            return;
        }

        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();
        InitCamera();

        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);
        StartCoroutine(PollUntilReady());
    }

    void Update()
    {
        if (mode == RunMode.Demo) return;

        if (flaskReady && !runOnStart && !isRunning
            && Input.GetKeyDown(KeyCode.Space))
            StartExperiment();

        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            CurrentExperimentMode = "";
            isRunning             = false;
            Debug.Log("[ExperimentRunner] Stopped by user.");
        }
    }

    IEnumerator RunDemoScan()
    {
        yield return new WaitForSeconds(0.5f);

        Debug.Log("================================================================");
        Debug.Log("[Demo] Skeleton scan started");
        Debug.Log("[Demo] Action | NormHip | HeadPitch | H2H | ArmElev | BodyGuess");
        Debug.Log("================================================================");

        userDad.gameObject.SetActive(false);
        userMom.gameObject.SetActive(true);
        Debug.Log("\n[Demo] USER MOM ------------------------------------------------");
        PrintTableHeader();
        yield return StartCoroutine(DemoScanUser(userMom, MomDemoActions, isMom: true));
        yield return StartCoroutine(userMom.ReturnToStanding());

        yield return new WaitForSeconds(1f);

        userMom.gameObject.SetActive(false);
        userDad.gameObject.SetActive(true);
        Debug.Log("\n[Demo] USER DAD ------------------------------------------------");
        PrintTableHeader();
        yield return StartCoroutine(DemoScanUser(userDad, DadDemoActions, isMom: false));
        yield return StartCoroutine(userDad.ReturnToStanding());

        userMom.gameObject.SetActive(true);
        userDad.gameObject.SetActive(true);

        Debug.Log("\n================================================================");
        Debug.Log("[Demo] Scan complete.");
        Debug.Log("================================================================");
    }

    IEnumerator DemoScanUser(UserEntity user, string[] actions, bool isMom)
    {
        var sk = user.GetComponent<SkeletonHelper>();
        if (sk == null)
            Debug.LogWarning($"[Demo] No SkeletonHelper on {user.userID}");

        foreach (string action in actions)
        {
            Transform spot = isMom ? GetMomSpot(action, 0) : GetDadSpot(action, 0);

            if (spot == null)
            {
                Debug.LogWarning($"[Demo] No spot for {user.userID} / {action} — skipping");
                continue;
            }

            user.overrideSpot         = spot;
            user.lastAssignedActivity = action;
            user.ResetBusy();
            yield return StartCoroutine(user.SwitchActivity(action));
            yield return new WaitForSeconds(demoSettleTime);

            if (sk != null)
                PrintSkeletonRow(action, sk);
            else
                Debug.Log($"{action,-16} | SkeletonHelper missing");

            yield return new WaitForSeconds(demoHoldTime);
        }
    }

    static void PrintTableHeader()
    {
        Debug.Log(
            $"{"Action",-16} | {"NormHip",-9} | {"HeadPitch",-11} | {"H2H",-8} | {"ArmElev",-9} | BodyGuess");
        Debug.Log(new string('-', 80));
    }

    static void PrintSkeletonRow(string action, SkeletonHelper sk)
    {
        float normHip = sk.NormalizedHipHeight();
        float pitch   = sk.HeadPitch();
        float h2h     = sk.NormalizedHandToHead();
        float arm     = sk.RightArmElevation();

        string bodyGuess =
            pitch > -999f && pitch < -55f                           ? "LYING"          :
            pitch > -999f && pitch < -18f && h2h >= 0f && h2h < 0.35f ? "SITTING(drink)" :
            pitch > -999f && pitch > 65f                            ? "SITTING(read)"  :
            arm   >= 0f   && arm   > 165f                          ? "STANDING(open)" :
            "AMBIGUOUS->LLM";

        string normStr  = normHip >= 0   ? normHip.ToString("F3") : "N/A";
        string pitchStr = pitch   > -999 ? pitch.ToString("F1")   : "N/A";
        string h2hStr   = h2h     >= 0   ? h2h.ToString("F3")     : "N/A";
        string armStr   = arm     >= 0   ? arm.ToString("F1")     : "N/A";

        Debug.Log(
            $"{action,-16} | {normStr,-9} | {pitchStr,-11} | {h2hStr,-8} | {armStr,-9} | {bodyGuess}");
        Debug.Log($"  JSON: {sk.ToJsonFragment()}");
    }

    IEnumerator PollUntilReady()
    {
        Debug.Log("[ExperimentRunner] Polling Flask /ready...");
        while (true)
        {
            using var req = UnityWebRequest.Get($"{backendUrl}/ready");
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = req.downloadHandler.text;
                if (data.Contains("\"ready\": true") || data.Contains("\"ready\":true"))
                {
                    Debug.Log("[ExperimentRunner] Flask ready! " + data);
                    flaskReady = true;
                    if (runOnStart)
                    {
                        yield return new WaitForSeconds(2f);
                        StartExperiment();
                    }
                    else
                    {
                        Debug.Log("[ExperimentRunner] Press Space to start.");
                    }
                    yield break;
                }
            }
            yield return new WaitForSeconds(3f);
        }
    }

    public void StartExperiment()
    {
        if (mode == RunMode.Demo || isRunning) return;
        totalRuns = successRuns = skippedRuns = noiseRuns = 0;
        StartCoroutine(RunExperiment());
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;
        yield return StartCoroutine(RunObservationExp());
        CurrentExperimentMode = "";
        isRunning             = false;
        Debug.Log($"[ExperimentRunner] Done. Regular={successRuns} Skipped={skippedRuns} Noise={noiseRuns} Total={totalRuns}");
        StartCoroutine(PostExperimentDone());
    }

    IEnumerator PostExperimentDone()
    {
        string json = "{\"mode\":\"Experiment\"}";
        using var req = new UnityWebRequest($"{backendUrl}/experiment_done", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();
        Debug.Log("[ExperimentRunner] /experiment_done: " + req.downloadHandler.text);
    }

    IEnumerator RunObservationExp()
    {
        UseVirtualDay         = true;
        CurrentExperimentMode = "experiment";
        CurrentVirtualDay     = 1;
        cameraManager.captureMode = StaticCameraManager.CaptureMode.EventDriven;

        int totalDays = exp_totalObservations / episodesPerVirtualDay;
        int epPerSlot = Mathf.Max(1, episodesPerVirtualDay / TimeSlots.Length);

        Debug.Log($"[Experiment] {totalDays} days | {epPerSlot} ep/slot | skip={skipProbability:P0} | noise every {noiseInterval} ep");

        int episodeCount = 0;

        for (int day = 1; day <= totalDays; day++)
        {
            CurrentVirtualDay = day;

            foreach (var slot in TimeSlots)
            {
                currentVirtualHour = slot.virtualHour;
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
                        if (Random.value < skipProbability)
                        {
                            skippedRuns++;
                        }
                        else
                        {
                            yield return StartCoroutine(RunSequenceEpisode(userMom, momQ[i], slot.virtualHour, episodeCount));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        if (addNoiseEpisodes && episodeCount > 0 && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(RunNoiseEpisode(userMom, slot.virtualHour));
                            noiseRuns++;
                        }
                    }

                    if (i < dadQ.Count)
                    {
                        totalRuns++;
                        if (Random.value < skipProbability)
                        {
                            skippedRuns++;
                        }
                        else
                        {
                            yield return StartCoroutine(RunSequenceEpisode(userDad, dadQ[i], slot.virtualHour, episodeCount));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        if (addNoiseEpisodes && episodeCount > 0 && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(RunNoiseEpisode(userDad, slot.virtualHour));
                            noiseRuns++;
                        }
                    }
                }

                yield return StartCoroutine(PostCheckpoint(day, slot.name, episodeCount));
            }

            Debug.Log($"[Experiment] Day {day}/{totalDays} | success={successRuns} skip={skippedRuns} noise={noiseRuns}");
        }
    }

    IEnumerator RunSequenceEpisode(UserEntity targetUser, BehaviorSequence seq, float virtualHour, int episodeIndex = 0)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);
        SetUsersVirtualHour(virtualHour);

        bool tvOn = TV_ON_BEHAVIORS.Contains(seq.groundTruth);
        yield return StartCoroutine(SetDeviceState("tv", tvOn ? "on" : "off"));

        int lastIdx = seq.actions.Length - 1;
        for (int i = 0; i < lastIdx; i++)
        {
            string midAction = seq.actions[i];
            targetUser.lastAssignedActivity = midAction;
            targetUser.ResetBusy();
            yield return StartCoroutine(targetUser.SwitchActivity(midAction));
            yield return new WaitForSeconds(0.5f);
        }

        string finalAction = seq.actions[lastIdx];
        Transform spot = (targetUser == userMom)
            ? GetMomSpot(seq.groundTruth, episodeIndex)
            : GetDadSpot(seq.groundTruth, episodeIndex);
        if (spot != null) targetUser.overrideSpot = spot;

        targetUser.lastAssignedActivity = finalAction;
        targetUser.ResetBusy();
        yield return StartCoroutine(targetUser.SwitchActivity(finalAction));

        targetUser.lastAssignedActivity = seq.groundTruth;
        yield return new WaitForSeconds(waitAfterCapture);
        targetUser.lastAssignedActivity = "";

        yield return StartCoroutine(SetDeviceState("tv", "off"));

        yield return StartCoroutine(targetUser.ReturnToStanding());

        if (other != null)
        {
            WarpUserToSpot(other);
            other.gameObject.SetActive(true);
        }
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunNoiseEpisode(UserEntity user, float virtualHour)
    {
        string     noise = NoiseActions[Random.Range(0, NoiseActions.Length)];
        UserEntity other = (user == userMom) ? userDad : userMom;
        if (other != null) other.gameObject.SetActive(false);
        if (user  != null) user.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        user.lastAssignedActivity = noise;
        user.ResetBusy();
        yield return StartCoroutine(user.SwitchActivity(noise));
        yield return new WaitForSeconds(waitAfterCapture);
        user.lastAssignedActivity = "";

        yield return StartCoroutine(user.ReturnToStanding());

        if (other != null)
        {
            WarpUserToSpot(other);
            other.gameObject.SetActive(true);
        }
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
        string[] checkActions = { "Watching", "Eating", "Sitting", "Drinking", "Reading", "Typing" };

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
                req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
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
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance ?? FindObjectOfType<StaticCameraManager>();
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

    List<BehaviorSequence> BuildSequenceQueue(BehaviorSequence[] sequences, int totalCount)
    {
        int totalWeight = 0;
        foreach (var s in sequences) totalWeight += s.weight;

        var result    = new List<BehaviorSequence>();
        int allocated = 0;
        for (int i = 0; i < sequences.Length; i++)
        {
            int count = (i == sequences.Length - 1)
                ? Mathf.Max(0, totalCount - allocated)
                : Mathf.Max(0, Mathf.RoundToInt((float)sequences[i].weight / totalWeight * totalCount));
            for (int j = 0; j < count; j++)
                result.Add(sequences[i]);
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
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    string GetSlotName(float hour) =>
        hour >= 22f ? "Night"     :
        hour >= 18f ? "Evening"   :
        hour >= 14f ? "Afternoon" :
        hour >= 11f ? "Noon"      : "Morning";

    void OnGUI()
    {
        if (mode == RunMode.Demo)
        {
            GUI.Label(new Rect(10, 10, 600, 22),
                "[Demo] Skeleton scan running — check Console for values");
            return;
        }

        int    totalDays = exp_totalObservations / episodesPerVirtualDay;
        string dayInfo   = $"  Day={CurrentVirtualDay}/{totalDays}";
        string status    = isRunning ? "" : (flaskReady ? "[Ready] Press Space" : "[Waiting Flask...]");

        GUI.Label(
            new Rect(10, 10, 1200, 22),
            $"[Experiment] {GetSlotName(currentVirtualHour)} {currentVirtualHour:F0}:00{dayInfo}  "
          + $"Skip={skippedRuns}  Noise={noiseRuns}  Regular={successRuns}  "
          + $"Total={totalRuns}/{exp_totalObservations}  {status}");
    }
}