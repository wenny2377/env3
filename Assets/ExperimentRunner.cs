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
    public int exp3_totalObservations  = 300;  
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
    [Tooltip("Episodes per virtual day. Default=10 → 300 episodes = 30 virtual days")]
    public int episodesPerVirtualDay = 10;

    [Header("Noise Episodes Settings")]
    [Tooltip("Add random noise episodes to improve FAT experiment validity")]
    public bool addNoiseEpisodes = true;

    [Tooltip("Insert one noise episode every N regular episodes")]
    public int noiseInterval = 10;

    [Header("Irregular Behavior Settings")]
    [Tooltip(
        "Probability (0~1) that a regular episode is skipped " +
        "to simulate user absence or irregular behavior. " +
        "Default=0.2 means 20% of episodes are skipped.")]
    [Range(0f, 1f)]
    public float skipProbability = 0.2f;  // 新增：隨機缺席機率

    public enum RunMode { Demo, Experiment1, Experiment3, Experiment4 }

    public static int  CurrentVirtualDay = 1;
    public static bool UseVirtualDay     = false;

    static readonly string[] MomNoiseBehaviors = { "Standing", "Walking" };
    static readonly string[] DadNoiseBehaviors = { "Standing", "Walking" };

    static readonly string[] MomBehaviors = { "Drink", "SittingIdle", "Reading" };
    static readonly string[] DadBehaviors = { "Drink", "SittingIdle", "Typing"  };

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
            name="Morning", virtualHour=7f,
            momWeights = new Dictionary<string,int>{{"Drink",3},{"SittingIdle",1},{"Reading",1}},
            dadWeights = new Dictionary<string,int>{{"Drink",2},{"SittingIdle",1},{"Typing",3}}
        },
        new TimeSlot {
            name="Noon", virtualHour=12f,
            momWeights = new Dictionary<string,int>{{"Drink",2},{"SittingIdle",3},{"Reading",2}},
            dadWeights = new Dictionary<string,int>{{"Drink",2},{"SittingIdle",2},{"Typing",2}}
        },
        new TimeSlot {
            name="Afternoon", virtualHour=15f,
            momWeights = new Dictionary<string,int>{{"Drink",1},{"SittingIdle",2},{"Reading",3}},
            dadWeights = new Dictionary<string,int>{{"Drink",1},{"SittingIdle",1},{"Typing",4}}
        },
        new TimeSlot {
            name="Evening", virtualHour=20f,
            momWeights = new Dictionary<string,int>{{"Drink",2},{"SittingIdle",4},{"Reading",3}},
            dadWeights = new Dictionary<string,int>{{"Drink",2},{"SittingIdle",4},{"Typing",1}}
        },
    };

    int   totalRuns          = 0;
    int   successRuns        = 0;
    int   skippedRuns        = 0;  // 新增：記錄跳過次數
    int   noiseRuns          = 0;
    float currentVirtualHour = 7f;
    bool  isRunning          = false;

    void Start()
    {
        if (mode == RunMode.Demo)
        {
            if (userMom != null) userMom.gameObject.SetActive(true);
            if (userDad != null) userDad.gameObject.SetActive(true);
            InitCameraForDemo();
            Debug.Log("[ExperimentRunner] Demo mode — camera observation active.");
            return;
        }

        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();

        if (cameraManager != null)
        {
            if (kitchenNodes?.Count    > 0) cameraManager.RegisterRoomCameras("Kitchen",    kitchenNodes);
            if (livingRoomNodes?.Count > 0) cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
            if (dadRoomNodes?.Count    > 0) cameraManager.RegisterRoomCameras("DadRoom",    dadRoomNodes);
            if (virtualCameraBrain != null) cameraManager.virtualCameraBrain = virtualCameraBrain;
        }

        if (userMom != null) userMom.gameObject.SetActive(true);
        if (userDad != null) userDad.gameObject.SetActive(true);

        if (runOnStart) StartExperiment();
    }

    void InitCameraForDemo()
    {
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                            ?? FindObjectOfType<StaticCameraManager>();

        if (cameraManager == null)
        {
            Debug.LogWarning("[ExperimentRunner] StaticCameraManager not found.");
            return;
        }

        if (kitchenNodes?.Count    > 0) cameraManager.RegisterRoomCameras("Kitchen",    kitchenNodes);
        if (livingRoomNodes?.Count > 0) cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
        if (dadRoomNodes?.Count    > 0) cameraManager.RegisterRoomCameras("DadRoom",    dadRoomNodes);
        if (virtualCameraBrain != null) cameraManager.virtualCameraBrain = virtualCameraBrain;

        Debug.Log("[ExperimentRunner] Demo camera initialized.");
    }

    void Update()
    {
        if (mode == RunMode.Demo) return;
        if (Input.GetKeyDown(KeyCode.Space) && !isRunning) StartExperiment();
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
            Debug.LogWarning("[ExperimentRunner] Cannot start in Demo mode.");
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
            case RunMode.Experiment1: yield return StartCoroutine(RunExperiment1()); break;
            case RunMode.Experiment3: yield return StartCoroutine(RunExperiment3()); break;
            case RunMode.Experiment4: yield return StartCoroutine(RunExperiment4()); break;
        }
        isRunning = false;
        Debug.Log($"[ExperimentRunner] Done. " +
                  $"Regular={successRuns} Skipped={skippedRuns} " +
                  $"Noise={noiseRuns} Total={totalRuns}");
    }

    IEnumerator RunExperiment1()
    {
        UseVirtualDay = false;

        foreach (string behavior in MomBehaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userMom, behavior, -1f));
                totalRuns++;
            }

        foreach (string behavior in DadBehaviors)
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userDad, behavior, -1f));
                totalRuns++;
            }
    }

    // ── Experiment3（核心改動）────────────────────────────────────────
    // 改前：4 個時間段各自跑完所有 episode（Morning × N → Noon × N → ...）
    // 改後：以「天」為單位循環，每天都有 Morning → Noon → Afternoon → Evening
    //       每個 episode 有 skipProbability 的機率跳過（模擬用戶缺席）
    // ──────────────────────────────────────────────────────────────────
    IEnumerator RunExperiment3()
    {
        UseVirtualDay     = true;
        CurrentVirtualDay = 1;

        // 計算總天數
        int totalDays       = exp3_totalObservations / episodesPerVirtualDay;
        int episodesPerDay  = episodesPerVirtualDay;
        // 每天每個時間段各跑幾個 episode
        int epPerSlotPerDay = Mathf.Max(1, episodesPerDay / TimeSlots.Length);

        int episodeCount = 0;

        Debug.Log($"[Experiment3] Start: {totalDays} virtual days, " +
                  $"{epPerSlotPerDay} ep/slot/day, " +
                  $"skipProb={skipProbability:P0}");

        // 以天為單位循環
        for (int day = 1; day <= totalDays; day++)
        {
            CurrentVirtualDay = day;

            // 每天跑 Morning → Noon → Afternoon → Evening
            foreach (var slot in TimeSlots)
            {
                currentVirtualHour = slot.virtualHour;

                // 建立這個時間段的行為佇列
                var momQueue = BuildWeightedQueue(
                    MomBehaviors, slot.momWeights, epPerSlotPerDay);
                var dadQueue = BuildWeightedQueue(
                    DadBehaviors, slot.dadWeights, epPerSlotPerDay);

                int maxLen = Mathf.Max(momQueue.Count, dadQueue.Count);

                for (int i = 0; i < maxLen; i++)
                {
                    // ── Mom episode ──────────────────────────────────
                    if (i < momQueue.Count)
                    {
                        totalRuns++;

                        // 隨機缺席：有 skipProbability 機率跳過
                        if (Random.value < skipProbability)
                        {
                            skippedRuns++;
                            Debug.Log($"[Skip] Day={day} {slot.name} " +
                                      $"Mom {momQueue[i]} (absent)");
                        }
                        else
                        {
                            if (useVirtualHour)
                                PostVirtualHourFireAndForget(slot.virtualHour);

                            yield return StartCoroutine(
                                RunSingleEpisode(userMom, momQueue[i],
                                                 slot.virtualHour));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        // Noise episode（基於成功執行的 episode 數量）
                        if (addNoiseEpisodes &&
                            episodeCount > 0 &&
                            episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(userMom, slot.virtualHour,
                                                MomNoiseBehaviors));
                            noiseRuns++;
                        }
                    }

                    // ── Dad episode ──────────────────────────────────
                    if (i < dadQueue.Count)
                    {
                        totalRuns++;

                        // 隨機缺席
                        if (Random.value < skipProbability)
                        {
                            skippedRuns++;
                            Debug.Log($"[Skip] Day={day} {slot.name} " +
                                      $"Dad {dadQueue[i]} (absent)");
                        }
                        else
                        {
                            if (useVirtualHour)
                                PostVirtualHourFireAndForget(slot.virtualHour);

                            yield return StartCoroutine(
                                RunSingleEpisode(userDad, dadQueue[i],
                                                 slot.virtualHour));
                            yield return new WaitForSeconds(minIntervalInSlot);
                            successRuns++;
                            episodeCount++;
                        }

                        // Noise episode
                        if (addNoiseEpisodes &&
                            episodeCount > 0 &&
                            episodeCount % noiseInterval == 0)
                        {
                            yield return StartCoroutine(
                                RunNoiseEpisode(userDad, slot.virtualHour,
                                                DadNoiseBehaviors));
                            noiseRuns++;
                        }
                    }
                }
            }

            Debug.Log($"[Experiment3] Day {day}/{totalDays} done. " +
                      $"Success={successRuns} Skipped={skippedRuns} " +
                      $"Noise={noiseRuns}");
        }

        Debug.Log($"[Experiment3] Complete. " +
                  $"Regular={successRuns} Skipped={skippedRuns} " +
                  $"Noise={noiseRuns} VirtualDays={CurrentVirtualDay}");
    }

    IEnumerator RunNoiseEpisode(
        UserEntity user, float virtualHour, string[] noiseBehaviors)
    {
        string noiseBehavior = noiseBehaviors[
            Random.Range(0, noiseBehaviors.Length)];

        Debug.Log($"[NoiseEpisode] {user.userID} -> {noiseBehavior} " +
                  $"(day={CurrentVirtualDay})");

        UserEntity otherUser = (user == userMom) ? userDad : userMom;
        if (otherUser != null) otherUser.gameObject.SetActive(false);
        if (user      != null) user.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        yield return StartCoroutine(user.SwitchActivity(noiseBehavior));
        yield return new WaitForSeconds(0.3f);
        yield return new WaitForSeconds(waitAfterCapture);
        yield return StartCoroutine(user.ReturnToIdle());

        if (otherUser != null) otherUser.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);

        Debug.Log($"[NoiseEpisode] Done: {user.userID} {noiseBehavior}");
    }

    IEnumerator RunExperiment4()
    {
        UseVirtualDay = false;

        var pool = new List<(UserEntity user, string behavior, float hour)>();
        var rng  = new System.Random(42);
        int perSlot = exp4_episodes / TimeSlots.Length;

        foreach (var slot in TimeSlots)
        {
            var mq = BuildWeightedQueue(MomBehaviors, slot.momWeights, perSlot / 2);
            var dq = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perSlot / 2);
            foreach (var b in mq) pool.Add((userMom, b, slot.virtualHour));
            foreach (var b in dq) pool.Add((userDad, b, slot.virtualHour));
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
            if (useVirtualHour) PostVirtualHourFireAndForget(hour);
            yield return StartCoroutine(RunSingleEpisode(user, behavior, hour));
            totalRuns++;
        }
    }

    IEnumerator RunSingleEpisode(
        UserEntity targetUser, string behavior, float virtualHour)
    {
        UserEntity otherUser = (targetUser == userMom) ? userDad : userMom;
        if (otherUser  != null) otherUser.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        yield return StartCoroutine(targetUser.SwitchActivity(behavior));

        float jitterX = Random.Range(-0.15f, 0.15f);
        float jitterZ = Random.Range(-0.15f, 0.15f);
        targetUser.transform.position += new Vector3(jitterX, 0f, jitterZ);

        yield return new WaitForSeconds(0.3f);
        yield return new WaitForSeconds(waitAfterCapture);
        yield return StartCoroutine(targetUser.ReturnToIdle());

        if (otherUser != null) otherUser.gameObject.SetActive(true);
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    void PostVirtualHourFireAndForget(float hour) =>
        StartCoroutine(PostVirtualHourRoutine(hour));

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour:F1}}}";
        var req = new UnityWebRequest($"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler   = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    List<string> BuildWeightedQueue(
        string[] behaviors,
        Dictionary<string, int> weights,
        int totalCount)
    {
        int totalWeight = 0;
        foreach (var b in behaviors)
            totalWeight += weights.TryGetValue(b, out int w) ? w : 1;

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

    string GetSlotName(float hour) =>
        hour >= 18f ? "Evening"   :
        hour >= 13f ? "Afternoon" :
        hour >= 10f ? "Noon"      : "Morning";

    int GetTargetTotal() => mode switch
    {
        RunMode.Experiment1 =>
            (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior,
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

        int totalDays = exp3_totalObservations / episodesPerVirtualDay;

        string dayInfo   = UseVirtualDay
            ? $"  Day={CurrentVirtualDay}/{totalDays}"
            : "";
        string skipInfo  = $"  Skip={skippedRuns}";
        string noiseInfo = addNoiseEpisodes ? $"  Noise={noiseRuns}" : "";

        GUI.Label(
            new Rect(10, 10, 900, 22),
            $"[{mode}] {GetSlotName(currentVirtualHour)} " +
            $"{currentVirtualHour:F0}:00" +
            $"{dayInfo}{skipInfo}{noiseInfo}  " +
            $"Regular={successRuns}  " +
            $"Total={totalRuns}/{GetTargetTotal()}  " +
            $"[Esc] Stop"
        );
    }
}