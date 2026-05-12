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

    [Header("Noise Episodes")]
    public bool addNoiseEpisodes = true;
    public int  noiseInterval    = 10;

    [Header("Skip Probability")]
    [Range(0f, 1f)]
    public float skipProbability = 0.2f;

    public enum RunMode
    {
        Demo, Experiment1, Experiment3, Experiment4
    }

    public static int  CurrentVirtualDay = 1;
    public static bool UseVirtualDay     = false;

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

    static readonly string[] MomBG1Behaviors = {
        "Drinking", "Eating", "Cooking",
        "Laying", "Watching", "Reading",
        "Cleaning", "PhoneUse", "Opening", "SittingDrink",
    };
    static readonly string[] DadBG1Behaviors = {
        "Drinking", "Eating", "Cooking",
        "Typing", "PhoneUse", "Laying",
        "Cleaning", "Reading", "Opening", "SittingDrink",
    };

    static readonly string[] NoiseActions = { "Standing" };

    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name        = "Morning",
            virtualHour = 7f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Cooking","Eating","Drinking" },
                    groundTruth = "Eating",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking" },
                    groundTruth = "Drinking",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Opening","SittingDrink" },
                    groundTruth = "SittingDrink",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating",
                    weight      = 3,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Cooking","Eating","Drinking" },
                    groundTruth = "Eating",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking" },
                    groundTruth = "Drinking",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Eating" },
                    groundTruth = "Eating",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "SittingDrink" },
                    groundTruth = "SittingDrink",
                    weight      = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Noon",
            virtualHour = 12f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching","Laying" },
                    groundTruth = "Laying",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating" },
                    groundTruth = "Eating",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading" },
                    groundTruth = "Reading",
                    weight      = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse","Laying" },
                    groundTruth = "Laying",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating" },
                    groundTruth = "Eating",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Afternoon",
            virtualHour = 15f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Reading" },
                    groundTruth = "Reading",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading","PhoneUse" },
                    groundTruth = "Reading",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cleaning" },
                    groundTruth = "Cleaning",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Typing" },
                    groundTruth = "Typing",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Typing","DadPhone" },
                    groundTruth = "Typing",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "DadPhone" },
                    groundTruth = "PhoneUse",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "DadReading" },
                    groundTruth = "Reading",
                    weight      = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Evening",
            virtualHour = 19f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating","Watching" },
                    groundTruth = "Watching",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating","Watching" },
                    groundTruth = "Watching",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching" },
                    groundTruth = "Watching",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking","Watching" },
                    groundTruth = "Watching",
                    weight      = 2,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating","PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating","PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking","PhoneUse" },
                    groundTruth = "PhoneUse",
                    weight      = 2,
                },
            },
        },
        new TimeSlot {
            name        = "Cleanup",
            virtualHour = 20.5f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cleaning" },
                    groundTruth = "Cleaning",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching" },
                    groundTruth = "Watching",
                    weight      = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cleaning" },
                    groundTruth = "Cleaning",
                    weight      = 1,
                },
                new BehaviorSequence {
                    actions     = new[]{ "DadPhone" },
                    groundTruth = "PhoneUse",
                    weight      = 3,
                },
            },
        },
        new TimeSlot {
            name        = "Night",
            virtualHour = 23f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Reading","Laying" },
                    groundTruth = "Laying",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse","Laying" },
                    groundTruth = "Laying",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching" },
                    groundTruth = "Watching",
                    weight      = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "DadPhone","Laying" },
                    groundTruth = "Laying",
                    weight      = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying",
                    weight      = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "DadReading","Laying" },
                    groundTruth = "Laying",
                    weight      = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "DadPhone" },
                    groundTruth = "PhoneUse",
                    weight      = 1,
                },
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

    void Start()
    {
        WarpUserToSpot(userMom);
        WarpUserToSpot(userDad);

        if (mode == RunMode.Demo)
        {
            if (userMom != null) userMom.gameObject.SetActive(true);
            if (userDad != null) userDad.gameObject.SetActive(true);
            InitCamera();
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
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();
        if (cameraManager == null) return;

        if (kitchenNodes?.Count    > 0)
            cameraManager.RegisterRoomCameras("Kitchen",    kitchenNodes);
        if (livingRoomNodes?.Count > 0)
            cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
        if (dadRoomNodes?.Count    > 0)
            cameraManager.RegisterRoomCameras("DadRoom",    dadRoomNodes);
        if (virtualCameraBrain != null)
            cameraManager.virtualCameraBrain = virtualCameraBrain;
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
        if (mode == RunMode.Demo || isRunning) return;
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
        Debug.Log($"[ExperimentRunner] Done. Regular={successRuns} " +
                  $"Skipped={skippedRuns} Noise={noiseRuns} Total={totalRuns}");
    }

    IEnumerator RunExperiment1()
    {
        UseVirtualDay = false;
        foreach (string b in MomBG1Behaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(
                    RunSingleActionEpisode(userMom, b, -1f));
                totalRuns++;
            }
        foreach (string b in DadBG1Behaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(
                    RunSingleActionEpisode(userDad, b, -1f));
                totalRuns++;
            }
    }

    IEnumerator RunExperiment3()
    {
        UseVirtualDay     = true;
        CurrentVirtualDay = 1;

        int totalDays    = exp3_totalObservations / episodesPerVirtualDay;
        int epPerSlot    = Mathf.Max(1, episodesPerVirtualDay / TimeSlots.Length);
        int episodeCount = 0;

        Debug.Log($"[Experiment3] {totalDays} days | " +
                  $"{epPerSlot} ep/slot | skip={skipProbability:P0}");

        for (int day = 1; day <= totalDays; day++)
        {
            CurrentVirtualDay = day;

            foreach (var slot in TimeSlots)
            {
                currentVirtualHour = slot.virtualHour;
                SetUsersVirtualHour(slot.virtualHour);

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
                            if (useVirtualHour)
                                PostVirtualHourFireAndForget(slot.virtualHour);
                            yield return StartCoroutine(
                                RunSequenceEpisode(
                                    userMom, momQ[i], slot.virtualHour));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        if (addNoiseEpisodes
                            && episodeCount > 0
                            && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(userMom, slot.virtualHour));
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
                                PostVirtualHourFireAndForget(slot.virtualHour);
                            yield return StartCoroutine(
                                RunSequenceEpisode(
                                    userDad, dadQ[i], slot.virtualHour));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        if (addNoiseEpisodes
                            && episodeCount > 0
                            && episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(userDad, slot.virtualHour));
                            noiseRuns++;
                        }
                    }
                }
            }

            Debug.Log($"[Experiment3] Day {day}/{totalDays} | " +
                      $"success={successRuns} skip={skippedRuns} " +
                      $"noise={noiseRuns}");
        }
    }

    IEnumerator RunExperiment4()
    {
        UseVirtualDay = false;

        var pool = new List<(UserEntity, BehaviorSequence, float)>();
        var rng  = new System.Random(42);

        foreach (var slot in TimeSlots)
        {
            var mq = BuildSequenceQueue(slot.momSequences, 3);
            var dq = BuildSequenceQueue(slot.dadSequences, 3);
            foreach (var s in mq) pool.Add((userMom, s, slot.virtualHour));
            foreach (var s in dq) pool.Add((userDad, s, slot.virtualHour));
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int limit = Mathf.Min(exp4_episodes, pool.Count);
        for (int ep = 0; ep < limit; ep++)
        {
            var (user, seq, hour) = pool[ep];
            currentVirtualHour = hour;
            SetUsersVirtualHour(hour);
            if (useVirtualHour) PostVirtualHourFireAndForget(hour);
            yield return StartCoroutine(RunSequenceEpisode(user, seq, hour));
            totalRuns++;
        }
    }

    IEnumerator RunSingleActionEpisode(
        UserEntity targetUser, string action, float virtualHour)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);
        SetUsersVirtualHour(virtualHour);

        WarpUserToSpot(targetUser);
        yield return null;

        targetUser.lastAssignedActivity = action;
        targetUser.ResetBusy();
        yield return StartCoroutine(targetUser.SwitchActivity(action));
        yield return new WaitForSeconds(waitAfterCapture);

        yield return StartCoroutine(targetUser.ReturnToStanding());
        targetUser.lastAssignedActivity = "";

        if (other != null) other.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunSequenceEpisode(
        UserEntity targetUser, BehaviorSequence seq,
        float virtualHour)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);
        SetUsersVirtualHour(virtualHour);

        foreach (string action in seq.actions)
        {
            targetUser.lastAssignedActivity = action;
            targetUser.ResetBusy();
            yield return StartCoroutine(targetUser.SwitchActivity(action));
            yield return new WaitForSeconds(0.3f);
        }

        yield return new WaitForSeconds(waitAfterCapture);
        targetUser.lastAssignedActivity = "";

        if (other != null) other.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunNoiseEpisode(UserEntity user, float virtualHour)
    {
        string noise = NoiseActions[Random.Range(0, NoiseActions.Length)];

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

        if (other != null) other.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

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
            $"{{\"virtual_hour\":{hour.ToString("F1", InvCulture)}}}";
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

    string GetSlotName(float hour) =>
        hour >= 23f ? "Night"     :
        hour >= 20f ? "Cleanup"   :
        hour >= 18f ? "Evening"   :
        hour >= 14f ? "Afternoon" :
        hour >= 11f ? "Noon"      : "Morning";

    int GetTargetTotal() => mode switch
    {
        RunMode.Experiment1 =>
            (MomBG1Behaviors.Length + DadBG1Behaviors.Length)
            * exp1_samplesPerBehavior,
        RunMode.Experiment3 => exp3_totalObservations,
        RunMode.Experiment4 => exp4_episodes,
        _                   => 0,
    };

    void OnGUI()
    {
        if (mode == RunMode.Demo) return;
        if (!isRunning) return;

        int    totalDays = exp3_totalObservations / episodesPerVirtualDay;
        string dayInfo   = UseVirtualDay
            ? $"  Day={CurrentVirtualDay}/{totalDays}" : "";

        GUI.Label(
            new Rect(10, 10, 1100, 22),
            $"[{mode}] {GetSlotName(currentVirtualHour)} " +
            $"{currentVirtualHour:F0}:00{dayInfo}  " +
            $"Skip={skippedRuns}  Noise={noiseRuns}  " +
            $"Regular={successRuns}  " +
            $"Total={totalRuns}/{GetTargetTotal()}  [Esc] Stop");
    }
}