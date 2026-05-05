using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    public List<CameraNode> kitchenNodes;
    public List<CameraNode> livingRoomNodes;
    public List<CameraNode> dadRoomNodes;

    [Header("Run Mode")]
    public RunMode mode = RunMode.Demo;

    [Header("Experiment Counts")]
    public int exp1_samplesPerBehavior = 20;
    public int exp3_totalObservations  = 360;
    public int exp4_episodes           = 30;

    [Header("Timing Settings")]
    public float waitAfterCapture    = 3.0f;
    public float waitBetweenEpisodes = 2.0f;
    public float minIntervalInSlot   = 1.5f;

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Options")]
    public bool useVirtualHour = true;
    public bool runOnStart     = false;

    [Header("Virtual Day Settings")]
    public int episodesPerVirtualDay = 12;

    [Header("Noise Episodes Settings")]
    public bool addNoiseEpisodes = true;
    public int  noiseInterval    = 10;

    [Header("Irregular Behavior Settings")]
    [Range(0f, 1f)]
    public float skipProbability = 0.2f;

    public enum RunMode
    {
        Demo, Experiment1, Experiment3, Experiment4
    }

    public static int  CurrentVirtualDay = 1;
    public static bool UseVirtualDay     = false;

    // ── Behavior definitions ─────────────────────────────────────
    // Activity strings must match UserEntity.SwitchActivity cases
    static readonly string[] MomNoiseBehaviors = { "Standing" };
    static readonly string[] DadNoiseBehaviors = { "Standing" };

    static readonly string[] MomBehaviors =
        { "Drink", "Laying", "Reading", "Watching" };
    static readonly string[] DadBehaviors =
        { "Drink", "Laying", "Typing", "PhoneUse" };

    struct TimeSlot
    {
        public string name;
        public float  virtualHour;
        public Dictionary<string, int> momWeights;
        public Dictionary<string, int> dadWeights;
    }

    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name        = "Morning",
            virtualHour = 7f,
            momWeights  = new Dictionary<string, int> {
                { "Drink",4 },{ "Laying",1 },
                { "Reading",1 },{ "Watching",1 },
            },
            dadWeights  = new Dictionary<string, int> {
                { "Drink",4 },{ "Laying",1 },
                { "Typing",1 },{ "PhoneUse",1 },
            },
        },
        new TimeSlot {
            name        = "Noon",
            virtualHour = 12f,
            momWeights  = new Dictionary<string, int> {
                { "Drink",1 },{ "Laying",4 },
                { "Reading",1 },{ "Watching",1 },
            },
            dadWeights  = new Dictionary<string, int> {
                { "Drink",1 },{ "Laying",4 },
                { "Typing",1 },{ "PhoneUse",1 },
            },
        },
        new TimeSlot {
            name        = "Afternoon",
            virtualHour = 15f,
            momWeights  = new Dictionary<string, int> {
                { "Drink",1 },{ "Laying",1 },
                { "Reading",4 },{ "Watching",1 },
            },
            dadWeights  = new Dictionary<string, int> {
                { "Drink",1 },{ "Laying",1 },
                { "Typing",4 },{ "PhoneUse",1 },
            },
        },
        new TimeSlot {
            name        = "Evening",
            virtualHour = 20f,
            momWeights  = new Dictionary<string, int> {
                { "Drink",1 },{ "Laying",2 },
                { "Reading",1 },{ "Watching",4 },
            },
            dadWeights  = new Dictionary<string, int> {
                { "Drink",2 },{ "Laying",2 },
                { "Typing",1 },{ "PhoneUse",4 },
            },
        },
    };

    int   totalRuns          = 0;
    int   successRuns        = 0;
    int   skippedRuns        = 0;
    int   noiseRuns          = 0;
    float currentVirtualHour = 7f;
    bool  isRunning          = false;

    static readonly System.Globalization.CultureInfo InvCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    // ── Unity lifecycle ──────────────────────────────────────────
    void Start()
    {
        // ── Set initial positions from standingSpot ──────────────
        // Prevents (0,0,0) causing wrong room assignment in
        // FindCamerasForUser at experiment start.
        if (userMom != null && userMom.standingSpot != null)
        {
            userMom.transform.position =
                userMom.standingSpot.position;
            userMom.transform.rotation =
                userMom.standingSpot.rotation;
            Debug.Log(
                $"[ExperimentRunner] userMom placed at " +
                $"standingSpot {userMom.standingSpot.position}");
        }

        if (userDad != null && userDad.standingSpot != null)
        {
            userDad.transform.position =
                userDad.standingSpot.position;
            userDad.transform.rotation =
                userDad.standingSpot.rotation;
            Debug.Log(
                $"[ExperimentRunner] userDad placed at " +
                $"standingSpot {userDad.standingSpot.position}");
        }

        if (mode == RunMode.Demo)
        {
            if (userMom != null) userMom.gameObject.SetActive(true);
            if (userDad != null) userDad.gameObject.SetActive(true);
            InitCamera();
            Debug.Log("[ExperimentRunner] Demo mode.");
            return;
        }

        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();
        InitCamera();

        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);

        if (runOnStart) StartExperiment();
    }

    void InitCamera()
    {
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning(
                "[ExperimentRunner] StaticCameraManager not found.");
            return;
        }
        if (kitchenNodes?.Count > 0)
            cameraManager.RegisterRoomCameras(
                "Kitchen", kitchenNodes);
        if (livingRoomNodes?.Count > 0)
            cameraManager.RegisterRoomCameras(
                "LivingRoom", livingRoomNodes);
        if (dadRoomNodes?.Count > 0)
            cameraManager.RegisterRoomCameras(
                "DadRoom", dadRoomNodes);
        if (virtualCameraBrain != null)
            cameraManager.virtualCameraBrain = virtualCameraBrain;
        Debug.Log("[ExperimentRunner] Camera initialized.");
    }

    void Update()
    {
        if (mode == RunMode.Demo) return;
        if (Input.GetKeyDown(KeyCode.Space) && !isRunning)
            StartExperiment();
        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            isRunning = false;
        }
    }

    public void StartExperiment()
    {
        if (mode == RunMode.Demo)
        {
            Debug.LogWarning(
                "[ExperimentRunner] Cannot start in Demo mode.");
            return;
        }
        if (isRunning) return;
        totalRuns = successRuns = skippedRuns = noiseRuns = 0;
        StartCoroutine(RunExperiment());
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;
        switch (mode)
        {
            case RunMode.Experiment1:
                yield return StartCoroutine(RunExperiment1()); break;
            case RunMode.Experiment3:
                yield return StartCoroutine(RunExperiment3()); break;
            case RunMode.Experiment4:
                yield return StartCoroutine(RunExperiment4()); break;
        }
        isRunning = false;
        Debug.Log(
            $"[ExperimentRunner] Done. " +
            $"Regular={successRuns} Skipped={skippedRuns} " +
            $"Noise={noiseRuns} Total={totalRuns}");
    }

    // ── Experiment 1 ─────────────────────────────────────────────
    IEnumerator RunExperiment1()
    {
        UseVirtualDay = false;
        foreach (string b in MomBehaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(
                    RunSingleEpisode(userMom, b, -1f));
                totalRuns++;
            }
        foreach (string b in DadBehaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(
                    RunSingleEpisode(userDad, b, -1f));
                totalRuns++;
            }
    }

    // ── Experiment 3 ─────────────────────────────────────────────
    IEnumerator RunExperiment3()
    {
        UseVirtualDay     = true;
        CurrentVirtualDay = 1;

        int totalDays =
            exp3_totalObservations / episodesPerVirtualDay;
        int epPerSlot =
            Mathf.Max(1, episodesPerVirtualDay / TimeSlots.Length);
        int episodeCount = 0;

        Debug.Log(
            $"[Experiment3] {totalDays} days, " +
            $"{epPerSlot} ep/slot, skip={skipProbability:P0}");

        for (int day = 1; day <= totalDays; day++)
        {
            CurrentVirtualDay = day;

            foreach (var slot in TimeSlots)
            {
                currentVirtualHour = slot.virtualHour;
                SetUsersVirtualHour(slot.virtualHour);

                var momQ = BuildWeightedQueue(
                    MomBehaviors, slot.momWeights, epPerSlot);
                var dadQ = BuildWeightedQueue(
                    DadBehaviors, slot.dadWeights, epPerSlot);
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
                            if (useVirtualHour)
                                PostVirtualHourFireAndForget(
                                    slot.virtualHour);
                            yield return StartCoroutine(
                                RunSingleEpisode(
                                    userMom, momQ[i],
                                    slot.virtualHour));
                            yield return new WaitForSeconds(
                                minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }
                        if (addNoiseEpisodes
                            && episodeCount > 0
                            && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(
                                    userMom, slot.virtualHour,
                                    MomNoiseBehaviors));
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
                            if (useVirtualHour)
                                PostVirtualHourFireAndForget(
                                    slot.virtualHour);
                            yield return StartCoroutine(
                                RunSingleEpisode(
                                    userDad, dadQ[i],
                                    slot.virtualHour));
                            yield return new WaitForSeconds(
                                minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }
                        if (addNoiseEpisodes
                            && episodeCount > 0
                            && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(
                                    userDad, slot.virtualHour,
                                    DadNoiseBehaviors));
                            noiseRuns++;
                        }
                    }
                }
            }

            Debug.Log(
                $"[Experiment3] Day {day}/{totalDays} | " +
                $"success={successRuns} skip={skippedRuns} " +
                $"noise={noiseRuns}");
        }
    }

    // ── Experiment 4 ─────────────────────────────────────────────
    IEnumerator RunExperiment4()
    {
        UseVirtualDay = false;

        var pool    = new List<(UserEntity, string, float)>();
        var rng     = new System.Random(42);
        int perSlot = exp4_episodes / TimeSlots.Length;

        foreach (var slot in TimeSlots)
        {
            var mq = BuildWeightedQueue(
                MomBehaviors, slot.momWeights, perSlot / 2);
            var dq = BuildWeightedQueue(
                DadBehaviors, slot.dadWeights, perSlot / 2);
            foreach (var b in mq)
                pool.Add((userMom, b, slot.virtualHour));
            foreach (var b in dq)
                pool.Add((userDad, b, slot.virtualHour));
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int limit = Mathf.Min(exp4_episodes, pool.Count);
        for (int ep = 0; ep < limit; ep++)
        {
            var (user, behavior, hour) = pool[ep];
            currentVirtualHour = hour;
            SetUsersVirtualHour(hour);
            if (useVirtualHour)
                PostVirtualHourFireAndForget(hour);
            yield return StartCoroutine(
                RunSingleEpisode(user, behavior, hour));
            totalRuns++;
        }
    }

    // ── Episode runners ──────────────────────────────────────────
    IEnumerator RunNoiseEpisode(
        UserEntity user, float virtualHour,
        string[] noiseBehaviors)
    {
        string noise = noiseBehaviors[
            Random.Range(0, noiseBehaviors.Length)];

        UserEntity other = (user == userMom) ? userDad : userMom;
        if (other != null) other.gameObject.SetActive(false);
        if (user  != null) user.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        user.lastAssignedActivity = noise;
        yield return StartCoroutine(user.SwitchActivity(noise));
        yield return new WaitForSeconds(waitAfterCapture);
        yield return StartCoroutine(user.ReturnToStanding());
        user.lastAssignedActivity = "";

        if (other != null) other.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunSingleEpisode(
        UserEntity targetUser, string behavior,
        float virtualHour)
    {
        UserEntity other =
            (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        SetUsersVirtualHour(virtualHour);

        targetUser.lastAssignedActivity = behavior;
        yield return StartCoroutine(
            targetUser.SwitchActivity(behavior));

        yield return new WaitForSeconds(waitAfterCapture);
        yield return StartCoroutine(
            targetUser.ReturnToStanding());
        targetUser.lastAssignedActivity = "";

        if (other != null) other.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    // ── Virtual hour helpers ─────────────────────────────────────
    void SetUsersVirtualHour(float hour)
    {
        if (userMom != null) userMom.currentVirtualHour = hour;
        if (userDad != null) userDad.currentVirtualHour = hour;
    }

    void PostVirtualHourFireAndForget(float hour) =>
        StartCoroutine(PostVirtualHourRoutine(hour));

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json =
            $"{{\"virtual_hour\":" +
            $"{hour.ToString("F1", InvCulture)}}}";
        var req = new UnityWebRequest(
            $"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler =
            new UnityEngine.Networking.DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    // ── Weighted queue builder ───────────────────────────────────
    List<string> BuildWeightedQueue(
        string[] behaviors,
        Dictionary<string, int> weights,
        int totalCount)
    {
        int totalWeight = 0;
        foreach (var b in behaviors)
            totalWeight +=
                weights.TryGetValue(b, out int w) ? w : 1;

        var result    = new List<string>();
        int allocated = 0;

        for (int i = 0; i < behaviors.Length; i++)
        {
            var b     = behaviors[i];
            int w     = weights.TryGetValue(b, out int ww) ? ww : 1;
            int count = (i == behaviors.Length - 1)
                ? Mathf.Max(0, totalCount - allocated)
                : Mathf.Max(0, Mathf.RoundToInt(
                    (float)w / totalWeight * totalCount));
            for (int j = 0; j < count; j++) result.Add(b);
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

    // ── GUI ──────────────────────────────────────────────────────
    string GetSlotName(float hour) =>
        hour >= 18f ? "Evening"   :
        hour >= 13f ? "Afternoon" :
        hour >= 10f ? "Noon"      : "Morning";

    int GetTargetTotal() => mode switch
    {
        RunMode.Experiment1 =>
            (MomBehaviors.Length + DadBehaviors.Length)
            * exp1_samplesPerBehavior,
        RunMode.Experiment3 => exp3_totalObservations,
        RunMode.Experiment4 => exp4_episodes,
        _                   => 0,
    };

    void OnGUI()
    {
        if (mode == RunMode.Demo)
        {
            GUI.Label(new Rect(10, 100, 500, 22),
                "[Demo Mode] Camera observation active.");
            return;
        }
        if (!isRunning) return;

        int totalDays =
            exp3_totalObservations / episodesPerVirtualDay;
        string dayInfo = UseVirtualDay
            ? $"  Day={CurrentVirtualDay}/{totalDays}" : "";

        GUI.Label(
            new Rect(10, 10, 900, 22),
            $"[{mode}] {GetSlotName(currentVirtualHour)} " +
            $"{currentVirtualHour:F0}:00{dayInfo}  " +
            $"Skip={skippedRuns}  Noise={noiseRuns}  " +
            $"Regular={successRuns}  " +
            $"Total={totalRuns}/{GetTargetTotal()}  [Esc] Stop");
    }
}