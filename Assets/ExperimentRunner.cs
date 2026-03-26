using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ExperimentRunner — 控制 Unity 端三個實驗的執行流程。
///
/// 五個論文實驗與 Unity 的關係：
///   Experiment1 → Unity 跑  → analyze_exp1.py
///   Experiment2 → Python-only（Exp1 資料即可）→ analyze_exp2.py
///   Experiment3 → Unity 跑  → analyze_exp3.py
///   Experiment4 → Unity 跑  → analyze_exp4.py
///   Experiment5 → Python-only（app.py 跑著即可）→ analyze_exp5.py
///
/// Experiment2（Behavioral Scene Graph）說明：
///   Exp1 跑完後，semantic_memories / scene_snapshots / dynamic_objects
///   已有足夠資料。直接執行 analyze_exp2.py 即可，不需要 Unity。
///
/// Experiment5（模糊需求查詢）說明：
///   12 個固定自然語言查詢發送到 /interact，記錄回答準確率。
///   app.py 跑著即可，直接執行 analyze_exp5.py。
///
/// Experiment4 評估設計說明：
///   系統採用 fire-and-forget POST /predict，VLM 推理時間 2–30 秒，
///   proposals 對應的是累積行為模式而非當前 episode。
///   因此不呼叫 /service_response，改用 analyze_exp4.py 的分布比對評估。
///   指標：Trigger Rate | Distribution Overlap | Top-Intent Match
/// </summary>
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

    [Header("Experiment Mode")]
    public ExperimentMode mode = ExperimentMode.Experiment1;

    [Header("Experiment Counts")]
    public int exp1_samplesPerBehavior = 20;   // 6 behaviors × 20 = 120 episodes
    public int exp3_totalObservations  = 120;  // 4 slots × 30 episodes
    public int exp4_episodes           = 30;   // shuffled, seed=42

    [Header("Timing Settings")]
    public float waitAfterCapture    = 3.0f;
    public float waitBetweenEpisodes = 2.0f;
    public float minIntervalInSlot   = 1.5f;

    [Header("Backend URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Options")]
    public bool useVirtualHour = true;
    public bool runOnStart     = false;

    // ── Experiment enum ────────────────────────────────────────────────────
    public enum ExperimentMode
    {
        Experiment1,   // VLM Action Recognition Accuracy
        Experiment3,   // Behavioral Manifold Learning
        Experiment4    // End-to-End Proactive Service
        // Experiment2 → analyze_exp2.py（Python-only）
        // Experiment5 → analyze_exp5.py（Python-only）
    }

    // ── Behavior sets ──────────────────────────────────────────────────────
    static readonly string[] MomBehaviors = { "Drink", "SittingIdle", "Reading" };
    static readonly string[] DadBehaviors = { "Drink", "SittingIdle", "Typing"  };

    // ── Time slots (Exp3 & Exp4) ───────────────────────────────────────────
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

    // ── Runtime state ──────────────────────────────────────────────────────
    int   totalRuns          = 0;
    int   successRuns        = 0;
    float currentVirtualHour = 7f;
    bool  isRunning          = false;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    void Start()
    {
        if (cameraManager == null)
            cameraManager = StaticCameraManager.Instance
                         ?? FindObjectOfType<StaticCameraManager>();

        if (cameraManager != null)
        {
            if (kitchenNodes?.Count    > 0) cameraManager.RegisterRoomCameras("Kitchen",    kitchenNodes);
            if (livingRoomNodes?.Count > 0) cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
            if (dadRoomNodes?.Count    > 0) cameraManager.RegisterRoomCameras("DadRoom",    dadRoomNodes);

            if (virtualCameraBrain != null)
                cameraManager.virtualCameraBrain = virtualCameraBrain;
        }
        else
        {
            Debug.LogError("[ExperimentRunner] StaticCameraManager not found");
        }

        if (runOnStart) StartExperiment();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isRunning) StartExperiment();
        if (Input.GetKeyDown(KeyCode.Escape) && isRunning)
        {
            StopAllCoroutines();
            isRunning = false;
            Debug.Log("[Exp] Stopped by user (Esc)");
        }
    }

    // ── Public entry ───────────────────────────────────────────────────────
    public void StartExperiment()
    {
        if (isRunning) { Debug.LogWarning("[Exp] Already running"); return; }
        totalRuns = successRuns = 0;
        Debug.Log($"[Exp] ▶ Starting {mode}");
        StartCoroutine(RunExperiment());
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;
        switch (mode)
        {
            case ExperimentMode.Experiment1: yield return StartCoroutine(RunExperiment1()); break;
            case ExperimentMode.Experiment3: yield return StartCoroutine(RunExperiment3()); break;
            case ExperimentMode.Experiment4: yield return StartCoroutine(RunExperiment4()); break;
        }
        isRunning = false;
        float rate = totalRuns > 0 ? (float)successRuns / totalRuns * 100f : 0f;
        Debug.Log($"[Exp] ■ Finished {mode} | Total:{totalRuns} Success:{successRuns} ({rate:F1}%)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 1: VLM Action Recognition Accuracy
    //
    // Unity 做什麼：
    //   6 種行為 × 20 次 = 120 episodes
    //   每次 SwitchActivity → 拍照 → fire-and-forget POST /predict
    //   /predict payload 的 "activity" 欄位 = ground_truth
    //   後端 log_eval() 寫入 eval_logs
    //
    // ※ Experiment2 和 Experiment5 共用這份資料，不需要額外跑 Unity
    //
    // Exp1 完成後執行：
    //   python3 analyze_exp/analyze_exp1.py --out ./results/
    //   python3 analyze_exp/analyze_exp2.py --out ./results/
    //   python3 analyze_exp/analyze_exp5.py --out ./results/
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment1()
    {
        Debug.Log("[Exp1] VLM Action Recognition Accuracy");
        Debug.Log($"[Exp1] Plan: {MomBehaviors.Length + DadBehaviors.Length} behaviors "
                + $"× {exp1_samplesPerBehavior} = "
                + $"{(MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior} episodes");
        Debug.Log("[Exp1] NOTE: Exp2 (Scene Graph) and Exp5 (Fuzzy Query) share this data.");

        foreach (string behavior in MomBehaviors)
        {
            Debug.Log($"[Exp1] Mom — {behavior} × {exp1_samplesPerBehavior}");
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userMom, behavior, -1f));
                totalRuns++;
                Debug.Log($"[Exp1] {totalRuns}/{GetTargetTotal()} Mom:{behavior} ep{i+1}");
            }
        }

        foreach (string behavior in DadBehaviors)
        {
            Debug.Log($"[Exp1] Dad — {behavior} × {exp1_samplesPerBehavior}");
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                yield return StartCoroutine(RunSingleEpisode(userDad, behavior, -1f));
                totalRuns++;
                Debug.Log($"[Exp1] {totalRuns}/{GetTargetTotal()} Dad:{behavior} ep{i+1}");
            }
        }

        Debug.Log("[Exp1] ✓ Done. Wait 60s for VLM to finish, then run:");
        Debug.Log("[Exp1]   python3 analyze_exp/analyze_exp1.py --out ./results/");
        Debug.Log("[Exp1]   python3 analyze_exp/analyze_exp2.py --out ./results/");
        Debug.Log("[Exp1]   python3 analyze_exp/analyze_exp5.py --out ./results/");
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 3: Behavioral Manifold Learning
    //
    // Unity 做什麼：
    //   4 time slots × 30 episodes = 120 observations
    //   Mom + Dad 在每個時段按時段權重交替跑行為
    //   每次 POST /predict 帶 virtual_hour → ManifoldEngine 記錄時段特徵
    //   後端每 50 筆自動 refit UMAP + HDBSCAN
    //
    // 完成後執行：
    //   python3 analyze_exp/analyze_exp3.py --out ./results/
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment3()
    {
        int perSlot       = exp3_totalObservations / TimeSlots.Length;  // 30
        int perPersonSlot = perSlot / 2;                                 // 15 per person

        Debug.Log("[Exp3] Behavioral Manifold Learning");
        Debug.Log($"[Exp3] Plan: {TimeSlots.Length} slots × {perSlot} obs "
                + $"= {exp3_totalObservations} total");

        foreach (var slot in TimeSlots)
        {
            currentVirtualHour = slot.virtualHour;
            Debug.Log($"[Exp3] ── Slot: {slot.name} ({slot.virtualHour:F0}:00) ──");

            var momQueue = BuildWeightedQueue(MomBehaviors, slot.momWeights, perPersonSlot);
            var dadQueue = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perPersonSlot);
            int maxLen   = Mathf.Max(momQueue.Count, dadQueue.Count);

            for (int i = 0; i < maxLen; i++)
            {
                if (i < momQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(
                        RunSingleEpisode(userMom, momQueue[i], slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }

                if (i < dadQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(
                        RunSingleEpisode(userDad, dadQueue[i], slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }

                Debug.Log($"[Exp3] {slot.name} progress: {totalRuns}/{GetTargetTotal()}");
            }
        }

        Debug.Log("[Exp3] ✓ Done.");
        Debug.Log("[Exp3] Run: python3 analyze_exp/analyze_exp3.py --out ./results/");
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 4: End-to-End Proactive Service
    //
    // Unity 做什麼：
    //   在 Experiment3 的流形基礎上（不清空 MongoDB），
    //   跑 30 個打亂順序的行為（seed=42）。
    //   每次 POST /predict → ManifoldEngine 投影 → 若 C ≥ 0.60 自動發提案
    //   提案累積在 service_proposals collection
    //
    // 評估設計（為什麼不用 per-episode 對應）：
    //   fire-and-forget 架構 → VLM 推理 2–30 秒 → Unity 已在跑後面的 episode
    //   proposals 對應累積模式，不是當前 episode
    //   改用整體分布比對：
    //     Trigger Rate         = 觸發提案數 / 30
    //     Distribution Overlap = Bhattacharyya(intent 分布, GT 行為分布)
    //     Top-Intent Match     = 最常觸發 intent == 最常出現 GT 行為 (✓/✗)
    //   不呼叫 /service_response
    //
    // 完成後執行：
    //   python3 analyze_exp/analyze_exp4.py --episodes 30 --out ./results/
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment4()
    {
        Debug.Log($"[Exp4] End-to-End Proactive Service — {exp4_episodes} episodes (seed=42)");
        Debug.Log("[Exp4] Prerequisite: run Experiment3 first (manifold must exist).");
        Debug.Log("[Exp4] No /service_response — distribution-based evaluation.");

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

        // Fisher-Yates shuffle, seed=42
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int limit = Mathf.Min(exp4_episodes, pool.Count);
        Debug.Log($"[Exp4] Pool: {pool.Count}, running: {limit}");

        for (int ep = 0; ep < limit; ep++)
        {
            var (user, behavior, hour) = pool[ep];
            currentVirtualHour = hour;

            if (useVirtualHour) PostVirtualHourFireAndForget(hour);
            yield return StartCoroutine(RunSingleEpisode(user, behavior, hour));
            totalRuns++;

            Debug.Log($"[Exp4] ep{ep+1}/{limit} "
                    + $"{user.name}:{behavior} slot={GetSlotName(hour)}");
        }

        Debug.Log("[Exp4] ✓ Done.");
        Debug.Log("[Exp4] Run: python3 analyze_exp/analyze_exp4.py --episodes 30 --out ./results/");
        Debug.Log("[Exp4] Metrics: Trigger Rate | Distribution Overlap | Top-Intent Match");
    }

    // ════════════════════════════════════════════════════════════════════════
    // CORE: RunSingleEpisode
    //   1. SwitchActivity  → 角色移動到目標位置 + 播動畫
    //   2. Position jitter ± 0.4m（模擬真實位置 variance）
    //   3. Wait 0.3s（動畫穩定）
    //   4. waitAfterCapture（StaticCameraManager 截圖 → POST /predict，fire-and-forget）
    //   5. ReturnToIdle + waitBetweenEpisodes
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunSingleEpisode(UserEntity user, string behavior, float virtualHour)
    {
        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        yield return StartCoroutine(user.SwitchActivity(behavior));

        float jitterX = Random.Range(-0.4f, 0.4f);
        float jitterZ = Random.Range(-0.4f, 0.4f);
        user.transform.position += new Vector3(jitterX, 0f, jitterZ);

        yield return new WaitForSeconds(0.3f);
        yield return new WaitForSeconds(waitAfterCapture);

        successRuns++;

        yield return StartCoroutine(user.ReturnToIdle());
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    // ── /set_virtual_hour (fire-and-forget) ───────────────────────────────
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

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Exp] PostVirtualHour failed: {req.error}");
    }

    // ── BuildWeightedQueue ─────────────────────────────────────────────────
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

    // ── Helpers ────────────────────────────────────────────────────────────
    string GetSlotName(float hour) =>
        hour >= 18f ? "Evening"   :
        hour >= 13f ? "Afternoon" :
        hour >= 10f ? "Noon"      : "Morning";

    int GetTargetTotal() => mode switch
    {
        ExperimentMode.Experiment1 =>
            (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior,
        ExperimentMode.Experiment3 => exp3_totalObservations,
        ExperimentMode.Experiment4 => exp4_episodes,
        _ => 0
    };

    // ── OnGUI ──────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!isRunning) return;
        GUI.Label(
            new Rect(10, 10, 640, 22),
            $"[{mode}]  {GetSlotName(currentVirtualHour)} {currentVirtualHour:F0}:00  "
          + $"Progress: {totalRuns}/{GetTargetTotal()}  "
          + $"Success: {successRuns}  [Esc] Stop"
        );
    }
}