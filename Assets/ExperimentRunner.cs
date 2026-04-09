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
    public int exp3_totalObservations  = 120;
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

    public enum RunMode
    {
        Demo,
        Experiment1,
        Experiment3,
        Experiment4,
    }

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
    float currentVirtualHour = 7f;
    bool  isRunning          = false;

    void Start()
    {
        if (mode == RunMode.Demo)
        {
            if (userMom != null) userMom.gameObject.SetActive(true);
            if (userDad != null) userDad.gameObject.SetActive(true);
            Debug.Log("[ExperimentRunner] Demo mode — NavBrain and Highlight active. No experiment running.");
            return;
        }

        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance ?? FindObjectOfType<StaticCameraManager>();

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
            Debug.LogWarning("[ExperimentRunner] Cannot start experiment in Demo mode.");
            return;
        }
        if (isRunning) return;
        totalRuns = successRuns = 0;
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
        Debug.Log($"[ExperimentRunner] Done. {successRuns}/{totalRuns} successful.");
    }

    IEnumerator RunExperiment1()
    {
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

    IEnumerator RunExperiment3()
    {
        int perSlot       = exp3_totalObservations / TimeSlots.Length;
        int perPersonSlot = perSlot / 2;

        foreach (var slot in TimeSlots)
        {
            currentVirtualHour = slot.virtualHour;
            var momQueue = BuildWeightedQueue(MomBehaviors, slot.momWeights, perPersonSlot);
            var dadQueue = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perPersonSlot);
            int maxLen   = Mathf.Max(momQueue.Count, dadQueue.Count);

            for (int i = 0; i < maxLen; i++)
            {
                if (i < momQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(RunSingleEpisode(userMom, momQueue[i], slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }
                if (i < dadQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(RunSingleEpisode(userDad, dadQueue[i], slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }
            }
        }
    }

    IEnumerator RunExperiment4()
    {
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

    IEnumerator RunSingleEpisode(UserEntity targetUser, string behavior, float virtualHour)
    {
        UserEntity otherUser = (targetUser == userMom) ? userDad : userMom;
        if (otherUser  != null) otherUser.gameObject.SetActive(false);
        if (targetUser != null) targetUser.gameObject.SetActive(true);

        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        yield return StartCoroutine(targetUser.SwitchActivity(behavior));

        float jitterX = Random.Range(-0.4f, 0.4f);
        float jitterZ = Random.Range(-0.4f, 0.4f);
        targetUser.transform.position += new Vector3(jitterX, 0f, jitterZ);

        yield return new WaitForSeconds(0.3f);
        yield return new WaitForSeconds(waitAfterCapture);

        successRuns++;

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
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();
    }

    List<string> BuildWeightedQueue(string[] behaviors, Dictionary<string, int> weights, int totalCount)
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
                : Mathf.Max(0, Mathf.RoundToInt((float)w / totalWeight * totalCount));

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
        RunMode.Experiment1 => (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior,
        RunMode.Experiment3 => exp3_totalObservations,
        RunMode.Experiment4 => exp4_episodes,
        _                   => 0,
    };

    void OnGUI()
    {
        if (mode == RunMode.Demo)
        {
            GUI.Label(new Rect(10, 10, 400, 22), "[Demo Mode] NavBrain active. Press nothing.");
            return;
        }
        if (!isRunning) return;
        GUI.Label(
            new Rect(10, 10, 640, 22),
            $"[{mode}] {GetSlotName(currentVirtualHour)} {currentVirtualHour:F0}:00  " +
            $"Progress: {totalRuns}/{GetTargetTotal()}  Success: {successRuns}  [Esc] Stop"
        );
    }
}