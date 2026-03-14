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
    public VirtualCameraBrain virtualCameraBrain;

    [Header("Camera Nodes")]
    public List<CameraNode> kitchenNodes;
    public List<CameraNode> livingRoomNodes;
    public List<CameraNode> dadRoomNodes;

    [Header("Experiment Mode")]
    public ExperimentMode mode = ExperimentMode.Exp1_VLM;

    [Header("Experiment Counts")]
    public int exp1_samplesPerBehavior = 20;
    public int exp3a_repeatCount = 30;
    public int exp4_totalObservations = 120;
    public int exp5_episodes = 30;

    [Header("Timing Settings")]
    public float waitAfterCapture = 3.0f;
    public float waitBetweenEpisodes = 2.0f;
    public float minIntervalInSlot = 1.5f;

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Timestamp Settings")]
    public bool useTimestamp = true;

    [Header("Execution")]
    public bool runOnStart = false;

    public enum ExperimentMode
    {
        Exp1_VLM,
        Exp3A_Habit,
        Exp4_Manifold,
        Exp5_EndToEnd
    }

    static readonly string[] MomBehaviors = { "drink", "sit", "reading" };
    static readonly string[] DadBehaviors = { "drink", "sit", "typing" };

    struct TimeSlot
    {
        public string name;
        public float virtualHour;
        public Dictionary<string, int> momWeights;
        public Dictionary<string, int> dadWeights;
    }

    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot {
            name="Morning", virtualHour=7f,
            momWeights = new Dictionary<string,int>{{"drink",3},{"sit",1},{"reading",1}},
            dadWeights = new Dictionary<string,int>{{"drink",2},{"sit",1},{"typing",3}}
        },
        new TimeSlot {
            name="Noon", virtualHour=12f,
            momWeights = new Dictionary<string,int>{{"drink",2},{"sit",3},{"reading",2}},
            dadWeights = new Dictionary<string,int>{{"drink",2},{"sit",2},{"typing",2}}
        },
        new TimeSlot {
            name="Afternoon", virtualHour=15f,
            momWeights = new Dictionary<string,int>{{"drink",1},{"sit",2},{"reading",3}},
            dadWeights = new Dictionary<string,int>{{"drink",1},{"sit",1},{"typing",4}}
        },
        new TimeSlot {
            name="Evening", virtualHour=20f,
            momWeights = new Dictionary<string,int>{{"drink",2},{"sit",4},{"reading",3}},
            dadWeights = new Dictionary<string,int>{{"drink",2},{"sit",4},{"typing",1}}
        },
    };

    int totalRuns = 0;
    int successRuns = 0;
    bool isRunning = false;
    float currentVirtualHour = 7f;

    void Start()
    {
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance;

        if (cameraManager == null)
            cameraManager = FindObjectOfType<StaticCameraManager>();

        if (cameraManager != null)
        {
            if (kitchenNodes != null && kitchenNodes.Count > 0)
                cameraManager.RegisterRoomCameras("Kitchen", kitchenNodes);

            if (livingRoomNodes != null && livingRoomNodes.Count > 0)
                cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);

            if (dadRoomNodes != null && dadRoomNodes.Count > 0)
                cameraManager.RegisterRoomCameras("DadRoom", dadRoomNodes);
        }
        else
        {
            Debug.LogError("[ExperimentRunner] StaticCameraManager not found");
        }

        if (virtualCameraBrain != null && cameraManager != null)
            cameraManager.virtualCameraBrain = virtualCameraBrain;

        if (runOnStart) StartExperiment();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isRunning)
            StartExperiment();

        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            isRunning = false;
            Debug.Log("[Exp] Stopped (Esc)");
        }
    }

    public void StartExperiment()
    {
        if (isRunning)
        {
            Debug.LogWarning("[Exp] Experiment already running");
            return;
        }

        totalRuns = 0;
        successRuns = 0;

        Debug.Log($"[Exp] Starting {mode}");
        StartCoroutine(RunExperiment());
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;

        switch (mode)
        {
            case ExperimentMode.Exp1_VLM:
                yield return StartCoroutine(RunExp1()); break;

            case ExperimentMode.Exp3A_Habit:
                yield return StartCoroutine(RunExp3A()); break;

            case ExperimentMode.Exp4_Manifold:
                yield return StartCoroutine(RunExp4()); break;

            case ExperimentMode.Exp5_EndToEnd:
                yield return StartCoroutine(RunExp5()); break;
        }

        isRunning = false;

        float rate = totalRuns > 0 ? (float)successRuns / totalRuns * 100f : 0f;

        Debug.Log($"[Exp] Finished {mode} Total:{totalRuns} Success:{successRuns} ({rate:F1}%)");
    }

    IEnumerator RunExp1()
    {
        int total = (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior;

        foreach (var b in MomBehaviors)
        {
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userMom, b, -1f));
                totalRuns++;
            }
        }

        foreach (var b in DadBehaviors)
        {
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userDad, b, -1f));
                totalRuns++;
            }
        }
    }

    IEnumerator RunExp3A()
    {
        for (int i = 0; i < exp3a_repeatCount; i++)
        {
            yield return StartCoroutine(RunSingleEpisode(userMom, "drink", -1f));
            totalRuns++;
        }
    }

    IEnumerator RunExp4()
    {
        int perSlot = exp4_totalObservations / TimeSlots.Length;
        int perPersonSlot = perSlot / 2;

        foreach (var slot in TimeSlots)
        {
            currentVirtualHour = slot.virtualHour;

            var momQueue = BuildWeightedQueue(MomBehaviors, slot.momWeights, perPersonSlot);
            var dadQueue = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perPersonSlot);

            int maxLen = Mathf.Max(momQueue.Count, dadQueue.Count);

            for (int i = 0; i < maxLen; i++)
            {
                if (i < momQueue.Count)
                {
                    string b = momQueue[i];

                    if (useTimestamp) PostVirtualHourFireAndForget(slot.virtualHour);

                    yield return StartCoroutine(RunSingleEpisode(userMom, b, slot.virtualHour));

                    yield return new WaitForSeconds(minIntervalInSlot);

                    totalRuns++;
                }

                if (i < dadQueue.Count)
                {
                    string b = dadQueue[i];

                    if (useTimestamp) PostVirtualHourFireAndForget(slot.virtualHour);

                    yield return StartCoroutine(RunSingleEpisode(userDad, b, slot.virtualHour));

                    yield return new WaitForSeconds(minIntervalInSlot);

                    totalRuns++;
                }
            }
        }
    }

    IEnumerator RunExp5()
    {
        var pool = new List<(UserEntity user, string behavior, float hour)>();

        var rng = new System.Random(42);

        foreach (var slot in TimeSlots)
        {
            int count = exp5_episodes / TimeSlots.Length;

            var mq = BuildWeightedQueue(MomBehaviors, slot.momWeights, count / 2);
            var dq = BuildWeightedQueue(DadBehaviors, slot.dadWeights, count / 2);

            foreach (var b in mq) pool.Add((userMom, b, slot.virtualHour));
            foreach (var b in dq) pool.Add((userDad, b, slot.virtualHour));
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int limit = Mathf.Min(exp5_episodes, pool.Count);

        for (int ep = 0; ep < limit; ep++)
        {
            var (user, behavior, hour) = pool[ep];

            currentVirtualHour = hour;

            if (useTimestamp) PostVirtualHourFireAndForget(hour);

            yield return StartCoroutine(RunSingleEpisode(user, behavior, hour));

            totalRuns++;
        }
    }

    IEnumerator RunSingleEpisode(UserEntity user, string behavior, float virtualHour)
    {
        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        yield return StartCoroutine(user.SwitchActivity(behavior));

        yield return new WaitForSeconds(waitAfterCapture);

        successRuns++;

        yield return StartCoroutine(user.ReturnToIdle());

        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    void PostVirtualHourFireAndForget(float hour)
    {
        StartCoroutine(PostVirtualHourRoutine(hour));
    }

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour:F1}}}";

        var req = new UnityWebRequest($"{backendUrl}/set_virtual_hour", "POST");

        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();

        req.SetRequestHeader("Content-Type", "application/json");

        req.timeout = 3;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Exp] PostVirtualHour failed: {req.error}");
    }

    List<string> BuildWeightedQueue(
        string[] behaviors,
        Dictionary<string, int> weights,
        int totalCount)
    {
        int totalWeight = 0;

        foreach (var b in behaviors)
            totalWeight += weights.TryGetValue(b, out int w) ? w : 1;

        var result = new List<string>();

        int allocated = 0;

        for (int i = 0; i < behaviors.Length; i++)
        {
            var b = behaviors[i];

            int w = weights.TryGetValue(b, out int ww) ? ww : 1;

            int count = (i == behaviors.Length - 1)
                ? totalCount - allocated
                : Mathf.RoundToInt((float)w / totalWeight * totalCount);

            count = Mathf.Max(count, 0);

            for (int j = 0; j < count; j++)
                result.Add(b);

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

    void OnGUI()
    {
        if (!isRunning) return;

        string slotName = currentVirtualHour >= 18f ? "Evening"
                        : currentVirtualHour >= 13f ? "Afternoon"
                        : currentVirtualHour >= 10f ? "Noon"
                        : "Morning";

        GUI.Label(new Rect(10, 10, 500, 22),
            $"[{mode}] {slotName} {currentVirtualHour:F0}:00 " +
            $"Progress: {totalRuns} / {GetTargetTotal()} " +
            $"Success: {successRuns} [Esc Stop]");
    }

    int GetTargetTotal() => mode switch
    {
        ExperimentMode.Exp1_VLM =>
            (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior,

        ExperimentMode.Exp3A_Habit =>
            exp3a_repeatCount,

        ExperimentMode.Exp4_Manifold =>
            exp4_totalObservations,

        ExperimentMode.Exp5_EndToEnd =>
            exp5_episodes,

        _ => 0
    };
}