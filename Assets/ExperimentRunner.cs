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

    [Header("Recognition Experiment Settings")]
    public int rec_samplesPerBehavior = 20;

    [Header("Habit Experiment Settings")]
    public int   exp3_totalObservations  = 300;
    public int   episodesPerVirtualDay   = 10;
    public bool  addNoiseEpisodes        = true;
    public int   noiseInterval           = 10;
    [Range(0f, 1f)]
    public float skipProbability         = 0.2f;

    [Header("Timing Settings")]
    public float waitAfterCapture    = 3.0f;
    public float waitBetweenEpisodes = 2.0f;
    public float minIntervalInSlot   = 1.5f;

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Options")]
    public bool useVirtualHour = true;
    public bool runOnStart     = false;

    // ── Mom Spots（每行為 3 個，固定順序輪流）────────────────────────
    [Header("Mom Spots - Drinking")]
    public Transform[] momDrinkingSpots = new Transform[3];

    [Header("Mom Spots - SittingDrink")]
    public Transform[] momSittingDrinkSpots = new Transform[3];

    [Header("Mom Spots - Eating")]
    public Transform[] momEatingSpots = new Transform[3];

    [Header("Mom Spots - Cooking")]
    public Transform[] momCookingSpots = new Transform[3];

    [Header("Mom Spots - Opening")]
    public Transform[] momOpeningSpots = new Transform[3];

    [Header("Mom Spots - Laying")]
    public Transform[] momLayingSpots = new Transform[3];

    [Header("Mom Spots - Watching")]
    public Transform[] momWatchingSpots = new Transform[3];

    [Header("Mom Spots - Reading")]
    public Transform[] momReadingSpots = new Transform[3];

    [Header("Mom Spots - Cleaning")]
    public Transform[] momCleaningSpots = new Transform[3];

    [Header("Mom Spots - PhoneUse")]
    public Transform[] momPhoneSpots = new Transform[3];

    // ── Dad Spots（每行為 3 個）────────────────────────────────────
    [Header("Dad Spots - Drinking")]
    public Transform[] dadDrinkingSpots = new Transform[3];

    [Header("Dad Spots - SittingDrink")]
    public Transform[] dadSittingDrinkSpots = new Transform[3];

    [Header("Dad Spots - Eating")]
    public Transform[] dadEatingSpots = new Transform[3];

    [Header("Dad Spots - Cooking")]
    public Transform[] dadCookingSpots = new Transform[3];

    [Header("Dad Spots - Opening")]
    public Transform[] dadOpeningSpots = new Transform[3];

    [Header("Dad Spots - Laying")]
    public Transform[] dadLayingSpots = new Transform[3];

    [Header("Dad Spots - Typing")]
    public Transform[] dadTypingSpots = new Transform[3];

    [Header("Dad Spots - Reading")]
    public Transform[] dadReadingSpots = new Transform[3];

    [Header("Dad Spots - Cleaning")]
    public Transform[] dadCleaningSpots = new Transform[3];

    [Header("Dad Spots - PhoneUse")]
    public Transform[] dadPhoneSpots = new Transform[3];

    public enum RunMode { Demo, RecognitionExp, HabitExp }

    public static int  CurrentVirtualDay = 1;
    public static bool   UseVirtualDay        = false;
    public static string CurrentExperimentMode = "";

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

    // ── Experiment1 行為列表 ─────────────────────────────────────
    static readonly string[] MomBehaviors = {
        "Drinking", "SittingDrink", "Eating", "Cooking", "Opening",
        "Laying", "Watching", "Reading", "Cleaning", "PhoneUse",
    };

    static readonly string[] DadBehaviors = {
        "Drinking", "SittingDrink", "Eating", "Cooking", "Opening",
        "Laying", "Typing", "Reading", "Cleaning", "PhoneUse",
    };

    static readonly string[] NoiseActions = { "Standing" };

    // ── 5 個時間段（無 Cleanup）─────────────────────────────────
    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name        = "Morning",
            virtualHour = 7f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Cooking","Eating","Drinking" },
                    groundTruth = "Eating", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking" },
                    groundTruth = "Drinking", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Opening","SittingDrink" },
                    groundTruth = "SittingDrink", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating", weight = 3,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Cooking","Eating","Drinking" },
                    groundTruth = "Eating", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking" },
                    groundTruth = "Drinking", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Opening","Eating" },
                    groundTruth = "Eating", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "SittingDrink" },
                    groundTruth = "SittingDrink", weight = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Noon",
            virtualHour = 12f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching","Laying" },
                    groundTruth = "Laying", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating" },
                    groundTruth = "Eating", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading" },
                    groundTruth = "Reading", weight = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse","Laying" },
                    groundTruth = "Laying", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating" },
                    groundTruth = "Eating", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse", weight = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Afternoon",
            virtualHour = 15f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Reading" },
                    groundTruth = "Reading", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading","PhoneUse" },
                    groundTruth = "Reading", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cleaning" },
                    groundTruth = "Cleaning", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse", weight = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Typing" },
                    groundTruth = "Typing", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Typing","PhoneUse" },
                    groundTruth = "Typing", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading" },
                    groundTruth = "Reading", weight = 1,
                },
            },
        },
        new TimeSlot {
            name        = "Evening",
            virtualHour = 19f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating","Watching" },
                    groundTruth = "Watching", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating","Watching" },
                    groundTruth = "Watching", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching" },
                    groundTruth = "Watching", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking","Watching" },
                    groundTruth = "Watching", weight = 2,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating","PhoneUse" },
                    groundTruth = "PhoneUse", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Eating","PhoneUse" },
                    groundTruth = "PhoneUse", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Cooking","Eating" },
                    groundTruth = "Eating", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Drinking","PhoneUse" },
                    groundTruth = "PhoneUse", weight = 2,
                },
            },
        },
        new TimeSlot {
            name        = "Night",
            virtualHour = 23f,
            momSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "Reading","Laying" },
                    groundTruth = "Laying", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse","Laying" },
                    groundTruth = "Laying", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Watching" },
                    groundTruth = "Watching", weight = 1,
                },
            },
            dadSequences = new BehaviorSequence[] {
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse","Laying" },
                    groundTruth = "Laying", weight = 3,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Laying" },
                    groundTruth = "Laying", weight = 4,
                },
                new BehaviorSequence {
                    actions     = new[]{ "Reading","Laying" },
                    groundTruth = "Laying", weight = 2,
                },
                new BehaviorSequence {
                    actions     = new[]{ "PhoneUse" },
                    groundTruth = "PhoneUse", weight = 1,
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

    void Update()
    {
        if (mode == RunMode.Demo) return;
        if (Input.GetKeyDown(KeyCode.Space) && !isRunning)
            StartExperiment();
        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            CurrentExperimentMode = "";
            isRunning             = false;
            Debug.Log("[ExperimentRunner] Stopped by user.");
        }
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

    void SetUsersVirtualHour(float hour)
    {
        if (userMom != null) userMom.currentVirtualHour = hour;
        if (userDad != null) userDad.currentVirtualHour = hour;
    }

    Transform GetSpot(Transform[] spots, int episodeIndex)
    {
        if (spots == null || spots.Length == 0) return null;
        var valid = System.Array.FindAll(spots, s => s != null);
        if (valid.Length == 0) return null;
        return valid[episodeIndex % valid.Length];
    }

    Transform GetMomSpot(string behavior, int episodeIndex)
    {
        return behavior switch
        {
            "Drinking"    => GetSpot(momDrinkingSpots,    episodeIndex),
            "SittingDrink" => GetSpot(momSittingDrinkSpots, episodeIndex),
            "Eating"      => GetSpot(momEatingSpots,      episodeIndex),
            "Cooking"     => GetSpot(momCookingSpots,     episodeIndex),
            "Opening"     => GetSpot(momOpeningSpots,     episodeIndex),
            "Laying"      => GetSpot(momLayingSpots,      episodeIndex),
            "Watching"    => GetSpot(momWatchingSpots,    episodeIndex),
            "Reading"     => GetSpot(momReadingSpots,     episodeIndex),
            "Cleaning"    => GetSpot(momCleaningSpots,    episodeIndex),
            "PhoneUse"    => GetSpot(momPhoneSpots,       episodeIndex),
            _             => null,
        };
    }

    Transform GetDadSpot(string behavior, int episodeIndex)
    {
        return behavior switch
        {
            "Drinking"    => GetSpot(dadDrinkingSpots,    episodeIndex),
            "SittingDrink" => GetSpot(dadSittingDrinkSpots, episodeIndex),
            "Eating"      => GetSpot(dadEatingSpots,      episodeIndex),
            "Cooking"     => GetSpot(dadCookingSpots,     episodeIndex),
            "Opening"     => GetSpot(dadOpeningSpots,     episodeIndex),
            "Laying"      => GetSpot(dadLayingSpots,      episodeIndex),
            "Typing"      => GetSpot(dadTypingSpots,      episodeIndex),
            "Reading"     => GetSpot(dadReadingSpots,     episodeIndex),
            "Cleaning"    => GetSpot(dadCleaningSpots,    episodeIndex),
            "PhoneUse"    => GetSpot(dadPhoneSpots,       episodeIndex),
            _             => null,
        };
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
            case RunMode.RecognitionExp:
                yield return StartCoroutine(RunRecognitionExp()); break;
            case RunMode.HabitExp:
                yield return StartCoroutine(RunHabitExp()); break;
        }
        CurrentExperimentMode = "";
        isRunning             = false;
        Debug.Log($"[ExperimentRunner] Done. " +
                  $"Regular={successRuns} Skipped={skippedRuns} " +
                  $"Noise={noiseRuns} Total={totalRuns}");
    }

    IEnumerator RunRecognitionExp()
    {
        UseVirtualDay            = false;
        CurrentExperimentMode    = "recognition";
        cameraManager.captureMode = StaticCameraManager.CaptureMode.Manual;
        Debug.Log("[RecognitionExp] Starting: " + (MomBehaviors.Length + DadBehaviors.Length) + " behaviors x " + rec_samplesPerBehavior + " samples x 2 users = " + ((MomBehaviors.Length + DadBehaviors.Length) * rec_samplesPerBehavior) + " episodes");

        Debug.Log($"[Exp1] Mom: {MomBehaviors.Length} behaviors " +
                  $"x {rec_samplesPerBehavior} samples");

        for (int i = 0; i < MomBehaviors.Length; i++)
        {
            string behavior = MomBehaviors[i];
            for (int s = 0; s < rec_samplesPerBehavior; s++)
            {
                Transform spot = GetMomSpot(behavior, s);
                yield return StartCoroutine(
                    RunSingleActionEpisode(userMom, behavior, spot, -1f));
                totalRuns++;
                successRuns++;
            }
        }

        Debug.Log($"[Exp1] Dad: {DadBehaviors.Length} behaviors " +
                  $"x {rec_samplesPerBehavior} samples");

        for (int i = 0; i < DadBehaviors.Length; i++)
        {
            string behavior = DadBehaviors[i];
            for (int s = 0; s < rec_samplesPerBehavior; s++)
            {
                Transform spot = GetDadSpot(behavior, s);
                yield return StartCoroutine(
                    RunSingleActionEpisode(userDad, behavior, spot, -1f));
                totalRuns++;
                successRuns++;
            }
        }
    }

    IEnumerator RunHabitExp()
    {
        UseVirtualDay         = true;
        CurrentExperimentMode = "habit";
        CurrentVirtualDay     = 1;
        cameraManager.captureMode = StaticCameraManager.CaptureMode.EventDriven;

        int totalDays = exp3_totalObservations / episodesPerVirtualDay;
        int epPerSlot = Mathf.Max(1, episodesPerVirtualDay / TimeSlots.Length);

        Debug.Log($"[Exp3] {totalDays} days | {epPerSlot} ep/slot | " +
                  $"skip={skipProbability:P0} | noise every {noiseInterval} ep");

        int episodeCount = 0;

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
                                    userMom, momQ[i],
                                    slot.virtualHour, episodeCount));
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
                                    userDad, dadQ[i],
                                    slot.virtualHour, episodeCount));
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

            Debug.Log($"[Exp3] Day {day}/{totalDays} | " +
                      $"success={successRuns} skip={skippedRuns} " +
                      $"noise={noiseRuns}");
        }
    }

    IEnumerator RunSingleActionEpisode(
        UserEntity targetUser, string action,
        Transform spot, float virtualHour)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);
        SetUsersVirtualHour(virtualHour);

        WarpUserToSpot(targetUser);
        yield return null;

        if (spot != null)
            targetUser.overrideSpot = spot;

        targetUser.lastAssignedActivity = action;
        targetUser.ResetBusy();
        yield return StartCoroutine(targetUser.SwitchActivity(action));

        yield return new WaitForSeconds(waitAfterCapture);

        yield return StartCoroutine(
            cameraManager.TriggerManualCapture(targetUser, action));

        yield return StartCoroutine(targetUser.ReturnToStanding());
        targetUser.lastAssignedActivity = "";

        if (other != null)
        {
            WarpUserToSpot(other);
            other.gameObject.SetActive(true);
        }
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    IEnumerator RunSequenceEpisode(
        UserEntity targetUser, BehaviorSequence seq,
        float virtualHour, int episodeIndex = 0)
    {
        UserEntity other = (targetUser == userMom) ? userDad : userMom;
        if (other      != null) other.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);
        SetUsersVirtualHour(virtualHour);

        // Execute all actions EXCEPT the last one using default spots.
        // This prevents overrideSpot from being consumed by intermediate
        // actions (e.g. Opening in "Opening → Cooking → Eating").
        int lastIdx = seq.actions.Length - 1;
        for (int i = 0; i < lastIdx; i++)
        {
            string midAction = seq.actions[i];
            targetUser.lastAssignedActivity = midAction;
            targetUser.ResetBusy();
            yield return StartCoroutine(targetUser.SwitchActivity(midAction));
            yield return new WaitForSeconds(0.5f);
        }

        // Set overrideSpot only for the final (ground-truth) action.
        // If the sequence has only one action, lastIdx == 0 so the loop
        // above is skipped and this still runs correctly.
        string finalAction = seq.actions[lastIdx];
        Transform spot = (targetUser == userMom)
            ? GetMomSpot(seq.groundTruth, episodeIndex)
            : GetDadSpot(seq.groundTruth, episodeIndex);
        if (spot != null)
            targetUser.overrideSpot = spot;

        targetUser.lastAssignedActivity = finalAction;
        targetUser.ResetBusy();
        yield return StartCoroutine(targetUser.SwitchActivity(finalAction));

        // Wait for camera capture at ground-truth spot.
        targetUser.lastAssignedActivity = seq.groundTruth;
        yield return new WaitForSeconds(waitAfterCapture);
        targetUser.lastAssignedActivity = "";

        if (other != null)
        {
            WarpUserToSpot(other);
            other.gameObject.SetActive(true);
        }
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

        if (other != null)
        {
            WarpUserToSpot(other);
            other.gameObject.SetActive(true);
        }
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

    void PostVirtualHourFireAndForget(float hour) =>
        StartCoroutine(PostVirtualHourRoutine(hour));

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json =
            $"{{\"virtual_hour\":{hour.ToString("F1", InvCulture)}}}";
        var req = new UnityWebRequest(
            $"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler   = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(json));
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

    int GetTargetTotal() => mode switch
    {
        RunMode.RecognitionExp =>
            (MomBehaviors.Length + DadBehaviors.Length)
            * rec_samplesPerBehavior,
        RunMode.HabitExp => exp3_totalObservations,
        _                   => 0,
    };

    void OnGUI()
    {
        if (mode == RunMode.Demo || !isRunning) return;

        int    totalDays = exp3_totalObservations / episodesPerVirtualDay;
        string dayInfo   = UseVirtualDay
            ? $"  Day={CurrentVirtualDay}/{totalDays}" : "";

        GUI.Label(
            new Rect(10, 10, 1200, 22),
            $"[{mode}] {GetSlotName(currentVirtualHour)} " +
            $"{currentVirtualHour:F0}:00{dayInfo}  " +
            $"Skip={skippedRuns}  Noise={noiseRuns}  " +
            $"Regular={successRuns}  " +
            $"Total={totalRuns}/{GetTargetTotal()}  " +
            $"[Space] Start  [Esc] Stop");
    }
}