using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class ExperimentRunner : MonoBehaviour
{
    [Header("Run Mode")]
    public RunMode mode = RunMode.Experiment;

    [Header("Experiment Settings")]
    public bool           autoRunAll     = false;
    public ExperimentType experimentType = ExperimentType.Baseline;

    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Camera System")]
    public StaticCameraManager  cameraManager;
    public VirtualCameraBrain   virtualCameraBrain;
    public DemoCameraController demoCameraController;
    public List<CameraNode>     livingRoomNodes;
    public List<CameraNode>     kitchenNodes;
    public List<CameraNode>     dadRoomNodes;

    [Header("Demo Items")]
    public GameObject demoEgg;
    public GameObject demoMiniPCLight;
    public GameObject demoWater;
    public GameObject demoTVScreen;
    public GameObject demoBowl;
    public Transform  mamiTransform;

    [Header("Demo Spots - Mom")]
    public Transform demoMomOpenSpot;
    public Transform demoMomCookSpot;
    public Transform demoMomEatSpot;
    public Transform demoMomSittingSpot;
    public Transform demoMomWatchSpot;
    public Transform demoMomSeatedDrinkSpot;

    [Header("Demo Spots - Dad")]
    public Transform demoDadTypingSpot;

    [Header("Mom Spots")]
    public Transform momSpot_OpenKitchen;
    public Transform momSpot_CookKitchen;
    public Transform momSpot_EatDining;
    public Transform momSpot_EatSofa;
    public Transform momSpot_CleanKitchen;
    public Transform momSpot_CleanLiving;
    public Transform momSpot_ReadSofa;
    public Transform momSpot_ReadBedroom;
    public Transform momSpot_DrinkKitchen;
    public Transform momSpot_SeatedDrinkSofa;
    public Transform momSpot_LaySofa;
    public Transform momSpot_LayBed;
    public Transform momSpot_WatchSofa;
    public Transform momSpot_PhoneSofa;

    [Header("Dad Spots")]
    public Transform dadSpot_EatDining;
    public Transform dadSpot_EatSofa;
    public Transform dadSpot_TypingDesk;
    public Transform dadSpot_DrinkDesk;
    public Transform dadSpot_SeatedDrinkSofa;
    public Transform dadSpot_PhoneDesk;
    public Transform dadSpot_PhoneSofa;
    public Transform dadSpot_LaySofa;
    public Transform dadSpot_LayBed;
    public Transform dadSpot_WatchSofa;

    const string BACKEND_URL           = "http://localhost:5000";
    const string DB_BASELINE           = "robot_exp_baseline";
    const string DB_CORRUPTION         = "robot_exp_corruption";
    const float  WAIT_BETWEEN_EPISODES = 2.0f;
    const bool   RUN_ON_START          = true;
    const float  DEMO_SCENE_TIME       = 5.0f;

    const float PICKUP_MISS_LIGHT   = 0.15f;
    const float PICKUP_MISS_MEDIUM  = 0.25f;
    const float PICKUP_MISS_HEAVY   = 0.35f;
    const float PUTDOWN_MISS_LIGHT  = 0.05f;
    const float PUTDOWN_MISS_MEDIUM = 0.10f;
    const float PUTDOWN_MISS_HEAVY  = 0.15f;
    const float OBJ_CONFUSE_LIGHT   = 0.10f;
    const float OBJ_CONFUSE_MEDIUM  = 0.15f;
    const float OBJ_CONFUSE_HEAVY   = 0.20f;
    const float SKEL_NOISE_LIGHT    = 5f;
    const float SKEL_NOISE_MEDIUM   = 10f;
    const float SKEL_NOISE_HEAVY    = 15f;

    public enum RunMode        { Experiment, Demo }
    public enum ExperimentType
    {
        Baseline,
        Vlm,
        CorruptionLight,
        CorruptionMedium,
        CorruptionHeavy,
    }
    public enum DayType    { Weekday, Weekend }
    enum DayDiffType { Remove, Replace }

    public static int    CurrentVirtualDay     = 1;
    public static float  CurrentVirtualHour    = 7f;
    public static string CurrentTimeSlot       = "Morning";
    public static bool   UseVirtualDay         = false;
    public static string CurrentExperimentMode = "";
    public static string CurrentSystemMode     = "";

    static readonly DayType[] WeekPattern = new DayType[]
    {
        DayType.Weekday, DayType.Weekday, DayType.Weekday,
        DayType.Weekday, DayType.Weekday,
        DayType.Weekend, DayType.Weekend,
    };

    struct BehaviorEvent
    {
        public string    action;
        public string    animation;
        public float     virtualHour;
        public Transform spot;
    }

    struct DayDiff
    {
        public int         dayIndex;
        public DayDiffType type;
        public string      targetAction;
        public string      replaceWith;
        public string      replaceAnim;
    }

    struct ExperimentSchedule
    {
        public ExperimentType expType;
        public string         collectionSuffix;
        public string         dbName;
        public string         systemMode;
    }

    static readonly ExperimentSchedule[] AllSchedule = new ExperimentSchedule[]
    {
        new ExperimentSchedule {
            expType          = ExperimentType.Baseline,
            collectionSuffix = "_semantic",
            dbName           = DB_BASELINE,
            systemMode       = "semantic"
        },
        new ExperimentSchedule {
            expType          = ExperimentType.Vlm,
            collectionSuffix = "_vlm",
            dbName           = DB_BASELINE,
            systemMode       = "vlm"
        },
        new ExperimentSchedule {
            expType          = ExperimentType.CorruptionLight,
            collectionSuffix = "_corruption_light_semantic",
            dbName           = DB_CORRUPTION,
            systemMode       = "semantic"
        },
        new ExperimentSchedule {
            expType          = ExperimentType.CorruptionMedium,
            collectionSuffix = "_corruption_medium_semantic",
            dbName           = DB_CORRUPTION,
            systemMode       = "semantic"
        },
        new ExperimentSchedule {
            expType          = ExperimentType.CorruptionHeavy,
            collectionSuffix = "_corruption_heavy_semantic",
            dbName           = DB_CORRUPTION,
            systemMode       = "semantic"
        },
    };

    static readonly DayDiff[] MOM_WEEKDAY_DIFFS = new DayDiff[]
    {
        new DayDiff { dayIndex = 1, type = DayDiffType.Remove,
                      targetAction = "UsingPhone" },
        new DayDiff { dayIndex = 2, type = DayDiffType.Replace,
                      targetAction = "Drinking", replaceWith = "SeatedDrinking",
                      replaceAnim  = "SeatedDrinking" },
        new DayDiff { dayIndex = 3, type = DayDiffType.Remove,
                      targetAction = "Reading" },
        new DayDiff { dayIndex = 4, type = DayDiffType.Replace,
                      targetAction = "UsingPhone", replaceWith = "Reading",
                      replaceAnim  = "Reading" },
    };

    static readonly DayDiff[] DAD_WEEKDAY_DIFFS = new DayDiff[]
    {
        new DayDiff { dayIndex = 1, type = DayDiffType.Remove,
                      targetAction = "Drinking" },
        new DayDiff { dayIndex = 2, type = DayDiffType.Replace,
                      targetAction = "UsingPhone", replaceWith = "Watching",
                      replaceAnim  = "Watching" },
        new DayDiff { dayIndex = 3, type = DayDiffType.Remove,
                      targetAction = "UsingPhone" },
        new DayDiff { dayIndex = 4, type = DayDiffType.Replace,
                      targetAction = "Typing", replaceWith = "Drinking",
                      replaceAnim  = "Drinking" },
    };

    static readonly DayDiff[] MOM_WEEKEND_DIFFS = new DayDiff[]
    {
        new DayDiff { dayIndex = 0, type = DayDiffType.Remove,
                      targetAction = "UsingPhone" },
        new DayDiff { dayIndex = 1, type = DayDiffType.Replace,
                      targetAction = "Drinking", replaceWith = "SeatedDrinking",
                      replaceAnim  = "SeatedDrinking" },
    };

    static readonly DayDiff[] DAD_WEEKEND_DIFFS = new DayDiff[]
    {
        new DayDiff { dayIndex = 0, type = DayDiffType.Remove,
                      targetAction = "UsingPhone" },
        new DayDiff { dayIndex = 1, type = DayDiffType.Replace,
                      targetAction = "Watching", replaceWith = "Drinking",
                      replaceAnim  = "SeatedDrinking" },
    };

    static string DbNameFor(ExperimentType t) =>
        (t == ExperimentType.Baseline || t == ExperimentType.Vlm)
            ? DB_BASELINE : DB_CORRUPTION;

    static string SuffixFor(ExperimentType t) => t switch
    {
        ExperimentType.Vlm              => "_vlm",
        ExperimentType.CorruptionLight  => "_corruption_light_semantic",
        ExperimentType.CorruptionMedium => "_corruption_medium_semantic",
        ExperimentType.CorruptionHeavy  => "_corruption_heavy_semantic",
        _                               => "_semantic",
    };

    static string SystemModeFor(ExperimentType t) =>
        t == ExperimentType.Vlm ? "vlm" : "semantic";

    int    successRuns          = 0;
    int    skippedRuns          = 0;
    float  currentVirtualHour   = 7f;
    bool   isRunning            = false;
    bool   flaskReady           = false;
    bool   _demoWaterShouldShow = false;
    string _demoMessage         = "Observing...";

    BehaviorEvent[] MomWeekday() => new BehaviorEvent[]
    {
        new BehaviorEvent { action="Opening",   animation="Opening",        virtualHour=7.0f,  spot=momSpot_OpenKitchen    },
        new BehaviorEvent { action="Cooking",   animation="Cooking",        virtualHour=7.3f,  spot=momSpot_CookKitchen    },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=7.5f,  spot=momSpot_EatDining      },
        new BehaviorEvent { action="Cleaning",  animation="Cleaning",       virtualHour=8.0f,  spot=momSpot_CleanKitchen   },
        new BehaviorEvent { action="Reading",   animation="Reading",        virtualHour=10.0f, spot=momSpot_ReadSofa       },
        new BehaviorEvent { action="Drinking",  animation="Drinking",       virtualHour=10.5f, spot=momSpot_DrinkKitchen   },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=12.0f, spot=momSpot_EatSofa        },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=13.0f, spot=momSpot_LaySofa        },
        new BehaviorEvent { action="Cleaning",  animation="Cleaning",       virtualHour=15.0f, spot=momSpot_CleanLiving    },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=16.0f, spot=momSpot_PhoneSofa      },
        new BehaviorEvent { action="Cooking",   animation="Cooking",        virtualHour=18.0f, spot=momSpot_CookKitchen    },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=18.5f, spot=momSpot_EatDining      },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=19.5f, spot=momSpot_WatchSofa      },
        new BehaviorEvent { action="Drinking",  animation="SeatedDrinking", virtualHour=20.0f, spot=momSpot_SeatedDrinkSofa},
        new BehaviorEvent { action="Reading",   animation="Reading",        virtualHour=21.5f, spot=momSpot_ReadBedroom    },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=23.0f, spot=momSpot_LayBed         },
    };

    BehaviorEvent[] MomWeekend() => new BehaviorEvent[]
    {
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=8.5f,  spot=momSpot_EatDining      },
        new BehaviorEvent { action="Opening",   animation="Opening",        virtualHour=9.0f,  spot=momSpot_OpenKitchen    },
        new BehaviorEvent { action="Cooking",   animation="Cooking",        virtualHour=10.0f, spot=momSpot_CookKitchen    },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=10.5f, spot=momSpot_EatSofa        },
        new BehaviorEvent { action="Reading",   animation="Reading",        virtualHour=11.5f, spot=momSpot_ReadSofa       },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=12.5f, spot=momSpot_PhoneSofa      },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=13.0f, spot=momSpot_EatDining      },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=14.0f, spot=momSpot_LaySofa        },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=16.0f, spot=momSpot_WatchSofa      },
        new BehaviorEvent { action="Drinking",  animation="SeatedDrinking", virtualHour=17.0f, spot=momSpot_SeatedDrinkSofa},
        new BehaviorEvent { action="Cooking",   animation="Cooking",        virtualHour=18.5f, spot=momSpot_CookKitchen    },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=19.0f, spot=momSpot_EatDining      },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=20.0f, spot=momSpot_WatchSofa      },
        new BehaviorEvent { action="Reading",   animation="Reading",        virtualHour=22.0f, spot=momSpot_ReadBedroom    },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=23.5f, spot=momSpot_LayBed         },
    };

    BehaviorEvent[] DadWeekday() => new BehaviorEvent[]
    {
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=7.0f,  spot=dadSpot_EatDining      },
        new BehaviorEvent { action="Typing",    animation="Typing",         virtualHour=8.0f,  spot=dadSpot_TypingDesk     },
        new BehaviorEvent { action="Drinking",  animation="Drinking",       virtualHour=9.0f,  spot=dadSpot_DrinkDesk      },
        new BehaviorEvent { action="Typing",    animation="Typing",         virtualHour=9.5f,  spot=dadSpot_TypingDesk     },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=10.5f, spot=dadSpot_PhoneDesk      },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=12.0f, spot=dadSpot_EatSofa        },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=13.0f, spot=dadSpot_LaySofa        },
        new BehaviorEvent { action="Typing",    animation="Typing",         virtualHour=14.0f, spot=dadSpot_TypingDesk     },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=16.0f, spot=dadSpot_PhoneDesk      },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=18.5f, spot=dadSpot_EatDining      },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=19.5f, spot=dadSpot_WatchSofa      },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=21.0f, spot=dadSpot_PhoneSofa      },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=23.0f, spot=dadSpot_LayBed         },
    };

    BehaviorEvent[] DadWeekend() => new BehaviorEvent[]
    {
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=9.0f,  spot=dadSpot_LayBed         },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=10.0f, spot=dadSpot_EatDining      },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=11.0f, spot=dadSpot_WatchSofa      },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=12.0f, spot=dadSpot_PhoneSofa      },
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=13.0f, spot=dadSpot_EatSofa        },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=14.5f, spot=dadSpot_LaySofa        },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=16.0f, spot=dadSpot_WatchSofa      },
        new BehaviorEvent { action="Drinking",  animation="SeatedDrinking", virtualHour=17.5f, spot=dadSpot_SeatedDrinkSofa},
        new BehaviorEvent { action="Eating",    animation="Eating",         virtualHour=19.0f, spot=dadSpot_EatDining      },
        new BehaviorEvent { action="Watching",  animation="Watching",       virtualHour=20.0f, spot=dadSpot_WatchSofa      },
        new BehaviorEvent { action="UsingPhone",animation="UsingPhone",     virtualHour=21.5f, spot=dadSpot_PhoneSofa      },
        new BehaviorEvent { action="Laying",    animation="Laying",         virtualHour=23.5f, spot=dadSpot_LayBed         },
    };

    BehaviorEvent[] ApplyDayDiff(BehaviorEvent[] baseSchedule, DayDiff[] diffs, int dayIndex)
    {
        var todayDiffs = new List<DayDiff>();
        foreach (var d in diffs)
            if (d.dayIndex == dayIndex)
                todayDiffs.Add(d);

        if (todayDiffs.Count == 0) return baseSchedule;

        var result = new List<BehaviorEvent>(baseSchedule.Length);
        foreach (var ev in baseSchedule)
        {
            BehaviorEvent current = ev;
            bool removed = false;
            foreach (var diff in todayDiffs)
            {
                if (current.action != diff.targetAction) continue;
                if (diff.type == DayDiffType.Remove) { removed = true; break; }
                else
                {
                    current.action    = diff.replaceWith;
                    current.animation = string.IsNullOrEmpty(diff.replaceAnim)
                                       ? diff.replaceWith : diff.replaceAnim;
                }
            }
            if (!removed) result.Add(current);
        }
        return result.ToArray();
    }

    void Start()
    {
        WarpUserToSpot(userMom);
        WarpUserToSpot(userDad);
        InitCamera();
        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);

        if (mode == RunMode.Demo)
        {
            if (demoEgg         != null) demoEgg.SetActive(false);
            if (demoMiniPCLight != null) demoMiniPCLight.SetActive(false);
            if (demoWater       != null) demoWater.SetActive(false);
            if (demoBowl        != null) demoBowl.SetActive(false);
            StartCoroutine(KeepDemoWaterHidden());
            StartCoroutine(RunDemoScan());
        }
        else
        {
            StartCoroutine(PollUntilReady());
        }
    }

    void Update()
    {
        if (mode == RunMode.Demo) return;
        if (flaskReady && !RUN_ON_START && !isRunning &&
            Input.GetKeyDown(KeyCode.Space))
            StartExperiment();
        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            CurrentExperimentMode = "";
            CurrentSystemMode     = "";
            isRunning = false;
        }
    }

    IEnumerator PollUntilReady()
    {
        while (true)
        {
            using var req = UnityWebRequest.Get($"{BACKEND_URL}/ready");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = req.downloadHandler.text;
                if (data.Contains("\"ready\": true") || data.Contains("\"ready\":true"))
                {
                    flaskReady = true;
                    if (RUN_ON_START) { yield return new WaitForSeconds(2f); StartExperiment(); }
                    else Debug.Log("[ExperimentRunner] Press Space to start.");
                    yield break;
                }
            }
            yield return new WaitForSeconds(3f);
        }
    }

    public void StartExperiment()
    {
        if (mode == RunMode.Demo || isRunning) return;
        isRunning = true;
        if (autoRunAll)
            StartCoroutine(RunAllScheduled());
        else
            StartCoroutine(RunSingleExperiment(
                experimentType,
                SuffixFor(experimentType),
                DbNameFor(experimentType),
                SystemModeFor(experimentType)));
    }

    IEnumerator RunAllScheduled()
    {
        for (int i = 0; i < AllSchedule.Length; i++)
        {
            var s = AllSchedule[i];
            Debug.Log($"[Schedule] {i+1}/{AllSchedule.Length}: {s.expType} "
                    + $"system={s.systemMode} db={s.dbName}");
            yield return StartCoroutine(
                RunSingleExperiment(s.expType, s.collectionSuffix, s.dbName, s.systemMode));
            yield return new WaitForSeconds(3f);
        }
        isRunning             = false;
        CurrentExperimentMode = "";
        CurrentSystemMode     = "";
        Debug.Log("[Schedule] All experiments complete!");
    }

    IEnumerator RunSingleExperiment(
        ExperimentType expType, string collSuffix,
        string dbName, string systemMode)
    {
        successRuns = skippedRuns = 0;

        float  pickupRate, putdownRate, objConfuse, skelNoise;
        string expModeStr;
        GetNoiseSettings(expType, out pickupRate, out putdownRate,
                         out objConfuse, out skelNoise, out expModeStr);

        CurrentExperimentMode = expModeStr;
        CurrentSystemMode     = systemMode;

        yield return StartCoroutine(
            PostStartExperiment(expModeStr, collSuffix, dbName, systemMode));

        ApplyNoiseTo(userMom, pickupRate, putdownRate, skelNoise);
        ApplyNoiseTo(userDad, pickupRate, putdownRate, skelNoise);
        var dsm = FindObjectOfType<DynamicSyncManager>();
        if (dsm != null) dsm.objectConfusionRate = objConfuse;

        Debug.Log($"[Experiment] Start: {expModeStr} system={systemMode} "
                + $"db={dbName} suffix={collSuffix}");

        yield return StartCoroutine(RunObservationExp());
        yield return StartCoroutine(PostExperimentDone(expModeStr));

        ApplyNoiseTo(userMom, 0f, 0f, 0f);
        ApplyNoiseTo(userDad, 0f, 0f, 0f);
        if (dsm != null) dsm.objectConfusionRate = 0f;

        Debug.Log($"[Experiment] Done: {expModeStr} success={successRuns} skip={skippedRuns}");

        if (!autoRunAll)
        {
            isRunning             = false;
            CurrentExperimentMode = "";
            CurrentSystemMode     = "";
        }
    }

    IEnumerator RunObservationExp()
    {
        UseVirtualDay     = true;
        CurrentVirtualDay = 1;

        int weekdayCounter = 0;
        int weekendCounter = 0;

        for (int day = 0; day < WeekPattern.Length; day++)
        {
            CurrentVirtualDay = day + 1;
            DayType dayType   = WeekPattern[day];

            BehaviorEvent[] momSchedule;
            BehaviorEvent[] dadSchedule;

            if (dayType == DayType.Weekday)
            {
                momSchedule = ApplyDayDiff(MomWeekday(), MOM_WEEKDAY_DIFFS, weekdayCounter);
                dadSchedule = ApplyDayDiff(DadWeekday(), DAD_WEEKDAY_DIFFS, weekdayCounter);
                weekdayCounter++;
            }
            else
            {
                momSchedule = ApplyDayDiff(MomWeekend(), MOM_WEEKEND_DIFFS, weekendCounter);
                dadSchedule = ApplyDayDiff(DadWeekend(), DAD_WEEKEND_DIFFS, weekendCounter);
                weekendCounter++;
            }

            var allEvents = new List<(UserEntity user, BehaviorEvent ev)>();
            foreach (var ev in momSchedule) allEvents.Add((userMom, ev));
            foreach (var ev in dadSchedule) allEvents.Add((userDad, ev));
            allEvents.Sort((a, b) => a.ev.virtualHour.CompareTo(b.ev.virtualHour));

            foreach (var (user, ev) in allEvents)
            {
                if (user == null) { skippedRuns++; continue; }

                UserEntity other = (user == userMom) ? userDad : userMom;
                if (other != null) other.gameObject.SetActive(false);
                user.gameObject.SetActive(true);

                yield return StartCoroutine(RunBehaviorEvent(user, ev));

                if (other != null)
                {
                    WarpUserToSpot(other);
                    other.gameObject.SetActive(true);
                }
                successRuns++;
            }

            Debug.Log($"[Experiment] Day {day+1}/{WeekPattern.Length} "
                    + $"success={successRuns} skip={skippedRuns}");
        }
    }

    IEnumerator RunBehaviorEvent(UserEntity user, BehaviorEvent ev)
    {
        currentVirtualHour = ev.virtualHour;
        CurrentVirtualHour = ev.virtualHour;
        CurrentTimeSlot    = GetSlotName(ev.virtualHour);
        SetUsersVirtualHour(ev.virtualHour);
        PostVirtualHourFireAndForget(ev.virtualHour);

        if (ev.spot != null) user.overrideSpot = ev.spot;

        bool tvOn = ev.action == "Watching";
        yield return StartCoroutine(SetDeviceState("tv", tvOn ? "on" : "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(tvOn);

        user.lastAssignedActivity = ev.action;
        user.ResetBusy();

        string anim = string.IsNullOrEmpty(ev.animation) ? ev.action : ev.animation;
        yield return StartCoroutine(user.SwitchActivity(anim));

        user.lastAssignedActivity = "";
        yield return StartCoroutine(SetDeviceState("tv", "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(false);
        yield return new WaitForSeconds(WAIT_BETWEEN_EPISODES);
    }

    void GetNoiseSettings(ExperimentType t,
        out float pickup, out float putdown,
        out float obj, out float skel, out string modeStr)
    {
        switch (t)
        {
            case ExperimentType.CorruptionLight:
                pickup=PICKUP_MISS_LIGHT;   putdown=PUTDOWN_MISS_LIGHT;
                obj=OBJ_CONFUSE_LIGHT;      skel=SKEL_NOISE_LIGHT;
                modeStr="corruption_light"; break;
            case ExperimentType.CorruptionMedium:
                pickup=PICKUP_MISS_MEDIUM;  putdown=PUTDOWN_MISS_MEDIUM;
                obj=OBJ_CONFUSE_MEDIUM;     skel=SKEL_NOISE_MEDIUM;
                modeStr="corruption_medium"; break;
            case ExperimentType.CorruptionHeavy:
                pickup=PICKUP_MISS_HEAVY;   putdown=PUTDOWN_MISS_HEAVY;
                obj=OBJ_CONFUSE_HEAVY;      skel=SKEL_NOISE_HEAVY;
                modeStr="corruption_heavy"; break;
            default:
                pickup=0f; putdown=0f; obj=0f; skel=0f;
                modeStr="baseline"; break;
        }
    }

    void ApplyNoiseTo(UserEntity user, float pickup, float putdown, float skel)
    {
        if (user == null) return;
        user.pickupMissRate  = pickup;
        user.putdownMissRate = putdown;
        user.SetSkeletonNoise(skel > 0f, skel);
    }

    IEnumerator KeepDemoWaterHidden()
    {
        while (true)
        {
            if (demoWater != null && !_demoWaterShouldShow && demoWater.activeSelf)
                demoWater.SetActive(false);
            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator RunDemoScan()
    {
        yield return new WaitForSeconds(2.0f);
        yield return StartCoroutine(PostDemoScene(1, "User_Mom"));
        _demoMessage = "Mami is observing Mom...";
        userDad.gameObject.SetActive(false);
        userMom.gameObject.SetActive(true);
        CurrentVirtualHour = 19f;
        CurrentTimeSlot    = "Evening";
        SetUsersVirtualHour(19f);
        PostVirtualHourFireAndForget(19f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        yield return StartCoroutine(SetDeviceState("tv", "off"));
        userMom.overrideSpot = demoMomOpenSpot;
        userMom.ResetBusy();
        yield return StartCoroutine(userMom.SwitchActivity("Opening"));
        yield return new WaitForSeconds(1.0f);
        userMom.ResetBusy();
        if (demoEgg != null) demoEgg.SetActive(true);
        yield return StartCoroutine(userMom.MoveToSpotAndHold("Cooking", demoMomCookSpot));
        yield return new WaitForSeconds(DEMO_SCENE_TIME);
        if (demoEgg != null) demoEgg.SetActive(false);
        yield return userMom.ClearHeldObject();
        if (demoBowl != null) demoBowl.SetActive(false);
        yield return userMom.ClearHeldObject();
        yield return new WaitForSeconds(0.3f);
        yield return StartCoroutine(userMom.MoveToSpotAndHold("Eating", demoMomEatSpot));
        yield return new WaitForSeconds(DEMO_SCENE_TIME);
        yield return userMom.ClearHeldObject();
        if (demoBowl != null) demoBowl.SetActive(true);
        yield return StartCoroutine(userMom.MoveToSpotAndHold("Sitting", demoMomSittingSpot));
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(SetDeviceState("tv", "on"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(true);
        if (demoTVScreen != null) demoTVScreen.SetActive(true);
        userMom.SetAnim("Watching");
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        yield return new WaitForSeconds(2.0f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_TV", userMom);
        yield return new WaitForSeconds(2.0f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        yield return new WaitForSeconds(2.0f);
        yield return StartCoroutine(PostDemoScene(2, "User_Mom"));
        _demoMessage = "Mami detected behavior change...";
        if (demoMiniPCLight != null) demoMiniPCLight.SetActive(true);
        yield return StartCoroutine(PostActionEvent("User_Mom", "Watching", "Sitting", "Evening"));
        yield return new WaitForSeconds(5.0f);
        if (demoMiniPCLight != null) demoMiniPCLight.SetActive(false);
        yield return StartCoroutine(WaitForSceneDone(60f));
        userMom.PreActivateHeldObject("SeatedDrinking");
        userMom.SetAnim("SeatedDrinking");
        yield return new WaitForSeconds(6.0f);
        yield return userMom.ClearHeldObject();
        _demoWaterShouldShow = true;
        if (demoWater != null) demoWater.SetActive(true);
        yield return new WaitForSeconds(0.5f);
        _demoMessage = "Mom calls Mami...";
        yield return userMom.ClearHeldObject();
        userMom.SetAnim("Watching");
        yield return new WaitForSeconds(1.5f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        yield return new WaitForSeconds(2.0f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_MamiCall", userMom);
        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(PostDemoScene(3, "User_Mom"));
        yield return StartCoroutine(WaitForSceneDone(120f));
        yield return new WaitForSeconds(3.0f);
        ReleaseActor(userMom);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        yield return new WaitForSeconds(2.5f);
        _demoMessage = "Dad calls Mami while typing...";
        yield return StartCoroutine(SetDeviceState("tv", "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(false);
        if (demoTVScreen != null) demoTVScreen.SetActive(false);
        yield return userMom.ClearHeldObject();
        yield return StartCoroutine(userMom.ReturnToStanding());
        userMom.gameObject.SetActive(false);
        userDad.gameObject.SetActive(true);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userDad);
        yield return StartCoroutine(userDad.MoveToSpotAndHold("Typing", demoDadTypingSpot));
        yield return new WaitForSeconds(2.0f);
        yield return StartCoroutine(PostDemoScene(4, "User_Dad"));
        yield return StartCoroutine(WaitForSceneDone(120f));
        yield return new WaitForSeconds(1.5f);
        _demoMessage = "Dad asks for cookie...";
        yield return StartCoroutine(PostDemoScene(5, "User_Dad"));
        yield return StartCoroutine(WaitForSceneDone(120f));
        yield return StartCoroutine(PostDemoScene(6, ""));
        _demoMessage = "Mami has learned the preferences of both residents.";
        yield return StartCoroutine(SetDeviceState("tv", "off"));
        if (virtualCameraBrain != null) virtualCameraBrain.SetTVState(false);
        yield return StartCoroutine(userDad.ReturnToStanding());
        userMom.gameObject.SetActive(true);
        WarpUserToSpot(userMom);
        yield return new WaitForSeconds(2.0f);
        if (demoCameraController != null)
            demoCameraController.SetActiveCamera("Cam_Overview", userMom);
        Debug.Log("[Demo] Complete.");
    }

    void ReleaseActor(UserEntity actor)
    {
        if (actor == null) return;
        var agent = actor.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.isStopped = false;
    }

    IEnumerator PostStartExperiment(
        string expMode, string collSuffix, string dbName, string systemMode)
    {
        string json = $"{{\"experiment_mode\":\"{expMode}\","
                    + $"\"ablation_mode\":\"full\","
                    + $"\"collection_suffix\":\"{collSuffix}\","
                    + $"\"db_name\":\"{dbName}\","
                    + $"\"system_mode\":\"{systemMode}\"}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/start_experiment", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
        Debug.Log($"[ExperimentRunner] started: {expMode} system={systemMode} "
                + $"db={dbName} suffix={collSuffix}");
    }

    IEnumerator PostExperimentDone(string expMode)
    {
        string json = $"{{\"experiment_mode\":\"{expMode}\"}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/experiment_done", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 300;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[ExperimentRunner] Queue cleared: {expMode}");
        else
            Debug.LogWarning($"[ExperimentRunner] experiment_done timeout: {req.error}");
    }

    IEnumerator PostDemoScene(int scene, string userId)
    {
        string json = $"{{\"scene\":{scene},\"user_id\":\"{userId}\"}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/demo/scene_ready", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
    }

    IEnumerator PostActionEvent(string userId, string prevAction,
                                string currAction, string timeSlot)
    {
        string json = $"{{\"user_id\":\"{userId}\","
                    + $"\"prev_action\":\"{prevAction}\","
                    + $"\"curr_action\":\"{currAction}\","
                    + $"\"time_slot\":\"{timeSlot}\"}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/demo/action_event", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
    }

    IEnumerator WaitForSceneDone(float maxWait = 120f)
    {
        float waited = 0f;
        while (waited < maxWait)
        {
            using var req = UnityWebRequest.Get($"{BACKEND_URL}/demo/wait_scene_done");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success &&
                req.downloadHandler.text.Contains("\"done\":true"))
            { yield break; }
            yield return new WaitForSeconds(2f);
            waited += 2f;
        }
        Debug.LogWarning("[Demo] WaitForSceneDone timeout");
    }

    IEnumerator SetDeviceState(string label, string state)
    {
        string json = $"{{\"label\":\"{label}\",\"state\":\"{state}\"}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/set_device_state", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    void PostVirtualHourFireAndForget(float hour) =>
        StartCoroutine(PostVirtualHourRoutine(hour));

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour.ToString("F1", JsonUtil.Inv)}}}";
        using var req = new UnityWebRequest($"{BACKEND_URL}/set_virtual_hour", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    void WarpUserToSpot(UserEntity user)
    {
        if (user == null || user.standingSpot == null) return;
        user.transform.position = user.standingSpot.position;
        user.transform.rotation = user.standingSpot.rotation;
        var ag = user.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (ag != null) ag.Warp(user.standingSpot.position);
    }

    void InitCamera()
    {
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                         ?? FindObjectOfType<StaticCameraManager>();
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

    string GetSlotName(float hour) =>
        hour >= 22f ? "Night"     :
        hour >= 18f ? "Evening"   :
        hour >= 14f ? "Afternoon" :
        hour >= 11f ? "Noon"      : "Morning";

    void OnGUI()
    {
        if (mode == RunMode.Demo)
        {
            GUI.Box(new Rect(10, Screen.height - 60, 600, 50), "");
            GUI.Label(new Rect(20, Screen.height - 50, 580, 40),
                $"Mami: {_demoMessage}");
            return;
        }

        string expLabel = autoRunAll ? "Auto All"
            : $"{experimentType} [{SystemModeFor(experimentType)}]";
        string status = isRunning ? "" :
            (flaskReady ? "[Ready] Press Space" : "[Waiting Flask...]");

        GUI.Label(
            new Rect(10, 10, 1400, 22),
            $"[{expLabel}] {GetSlotName(currentVirtualHour)} "
          + $"{currentVirtualHour:F0}:00  "
          + $"Day={CurrentVirtualDay}/{WeekPattern.Length}  "
          + $"Success={successRuns}  Skip={skippedRuns}  {status}");
    }
}