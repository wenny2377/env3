using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ExperimentRunner — 控制四個實驗的執行流程。
///
/// 實驗對應關係（論文編號 vs 原始 code 命名）：
///   Experiment1 (原 Exp1)   → VLM Action Recognition Accuracy
///   Experiment2 (原 Exp3A)  → Habit Memory Accumulation (FAISS convergence)
///   Experiment3 (原 Exp4)   → Behavioral Manifold Learning (UMAP + HDBSCAN)
///   Experiment4 (原 Exp5)   → End-to-End Proactive Service (distribution-based)
///
/// Experiment4 評估設計說明：
///   系統採用 fire-and-forget POST /predict，VLM 推理時間 2–30 秒，
///   proposals 對應的是累積行為模式而非當前 episode。
///   因此不呼叫 /service_response，改用 analyze_exp4.py 的分布比對評估。
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
    public int exp2_repeatCount        = 30;   // User_Mom Drink × 30
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

    // ── Experiment enum (論文命名) ─────────────────────────────────────────
    public enum ExperimentMode
    {
        Experiment1,   // VLM Action Recognition Accuracy
        Experiment2,   // Habit Memory Accumulation
        Experiment3,   // Behavioral Manifold Learning
        Experiment4    // End-to-End Proactive Service (distribution-based)
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
    int   totalRuns        = 0;
    int   successRuns      = 0;
    float currentVirtualHour = 7f;
    bool  isRunning        = false;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    void Start()
    {
        // Camera manager setup
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
            case ExperimentMode.Experiment2: yield return StartCoroutine(RunExperiment2()); break;
            case ExperimentMode.Experiment3: yield return StartCoroutine(RunExperiment3()); break;
            case ExperimentMode.Experiment4: yield return StartCoroutine(RunExperiment4()); break;
        }
        isRunning = false;
        float rate = totalRuns > 0 ? (float)successRuns / totalRuns * 100f : 0f;
        Debug.Log($"[Exp] ■ Finished {mode} | Total:{totalRuns} Success:{successRuns} ({rate:F1}%)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 1: VLM Action Recognition Accuracy
    // 6 behaviors × exp1_samplesPerBehavior(20) = 120 episodes
    // ground_truth 透過 POST /predict payload 的 "activity" 欄位傳給後端
    // 後端寫入 eval_logs → analyze_exp1.py 計算 F1
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment1()
    {
        Debug.Log("[Exp1] VLM Action Recognition Accuracy");
        Debug.Log($"[Exp1] Plan: {MomBehaviors.Length + DadBehaviors.Length} behaviors × {exp1_samplesPerBehavior} = " +
                  $"{(MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior} episodes");

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
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 2: Habit Memory Accumulation
    // User_Mom Drink × exp2_repeatCount(30)
    // 每次 episode 結束後呼叫 /exp_checkpoint 記錄 FAISS similarity
    // → analyze_exp2.py 畫收斂曲線
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment2()
    {
        Debug.Log($"[Exp2] Habit Memory Accumulation — User_Mom Drink × {exp2_repeatCount}");

        for (int i = 0; i < exp2_repeatCount; i++)
        {
            yield return StartCoroutine(RunSingleEpisode(userMom, "Drink", -1f));
            totalRuns++;

            // 每次 episode 後立即查詢 FAISS similarity，記錄收斂過程
            yield return StartCoroutine(
                PostExpCheckpoint(episodeIndex: i + 1, userId: "User_Mom", action: "Drink")
            );

            Debug.Log($"[Exp2] Episode {i+1}/{exp2_repeatCount} done");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 3: Behavioral Manifold Learning
    // 4 time slots × 30 episodes = 120 observations
    // Mom + Dad interleaved per slot，依 TimeSlots 權重建 queue
    // 跑完後執行 python3 generate_umap.py 產出 UMAP scatter + Silhouette Score
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment3()
    {
        int perSlot       = exp3_totalObservations / TimeSlots.Length;   // 30
        int perPersonSlot = perSlot / 2;                                  // 15 per person

        Debug.Log($"[Exp3] Behavioral Manifold Learning");
        Debug.Log($"[Exp3] Plan: {TimeSlots.Length} slots × {perSlot} obs = {exp3_totalObservations} total");

        foreach (var slot in TimeSlots)
        {
            currentVirtualHour = slot.virtualHour;
            Debug.Log($"[Exp3] ── Slot: {slot.name} (hour={slot.virtualHour}) ──");

            var momQueue = BuildWeightedQueue(MomBehaviors, slot.momWeights, perPersonSlot);
            var dadQueue = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perPersonSlot);
            int maxLen   = Mathf.Max(momQueue.Count, dadQueue.Count);

            for (int i = 0; i < maxLen; i++)
            {
                if (i < momQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(
                        RunSingleEpisode(userMom, momQueue[i], slot.virtualHour)
                    );
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }

                if (i < dadQueue.Count)
                {
                    if (useVirtualHour) PostVirtualHourFireAndForget(slot.virtualHour);
                    yield return StartCoroutine(
                        RunSingleEpisode(userDad, dadQueue[i], slot.virtualHour)
                    );
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }

                Debug.Log($"[Exp3] {slot.name} progress: {totalRuns}/{GetTargetTotal()}");
            }
        }

        Debug.Log("[Exp3] ✓ Done. Run: python3 generate_umap.py to produce Figure.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // EXPERIMENT 4: End-to-End Proactive Service
    // 30 shuffled episodes (seed=42)，在 Exp3 的流形基礎上執行。
    //
    // 評估設計：
    //   系統採用 fire-and-forget POST /predict，VLM 推理 2–30 秒，
    //   proposals 對應累積行為模式而非當前 episode。
    //   因此不呼叫 /service_response，改用分布比對評估：
    //     Trigger Rate + Distribution Overlap + Top-Intent Match
    //   詳見 analyze_exp4.py
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunExperiment4()
    {
        Debug.Log($"[Exp4] End-to-End Proactive Service — {exp4_episodes} episodes (seed=42)");
        Debug.Log("[Exp4] NOTE: No /service_response calls. Evaluation via analyze_exp4.py (distribution-based).");
        Debug.Log("[Exp4] NOTE: Prerequisite — run Experiment3 first to populate manifold.");

        // Build cross-slot pool with fixed seed=42 for reproducibility
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

        // Fisher-Yates shuffle with fixed seed
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int limit = Mathf.Min(exp4_episodes, pool.Count);
        Debug.Log($"[Exp4] Pool size: {pool.Count}, running: {limit}");

        for (int ep = 0; ep < limit; ep++)
        {
            var (user, behavior, hour) = pool[ep];
            currentVirtualHour = hour;

            if (useVirtualHour) PostVirtualHourFireAndForget(hour);

            yield return StartCoroutine(RunSingleEpisode(user, behavior, hour));

            totalRuns++;

            string slotName = GetSlotName(hour);
            Debug.Log($"[Exp4] ep{ep+1}/{limit} {user.name}:{behavior} slot={slotName}");

            // 不呼叫 /service_response
            // ManifoldEngine 會在後台收到 /predict 結果後自動觸發提案
            // proposals 累積在 MongoDB service_proposals collection
            // 實驗結束後執行 analyze_exp4.py 進行分布比對評估
        }

        Debug.Log("[Exp4] ✓ Done.");
        Debug.Log("[Exp4] Run: python3 analyze_exp4.py --episodes 30 --out ./results/");
        Debug.Log("[Exp4] Metrics: Trigger Rate | Distribution Overlap | Top-Intent Match");
    }

    // ════════════════════════════════════════════════════════════════════════
    // CORE: RunSingleEpisode
    // 1. SwitchActivity → 角色移動到目標位置
    // 2. 位置 jitter ± 0.4m（模擬真實位置 variance）
    // 3. 穩定 0.3s
    // 4. waitAfterCapture（讓 StaticCameraManager 偵測到行為並觸發截圖）
    // 5. ReturnToIdle + waitBetweenEpisodes
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator RunSingleEpisode(UserEntity user, string behavior, float virtualHour)
    {
        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        // 移動到行為位置
        yield return StartCoroutine(user.SwitchActivity(behavior));

        // 位置 jitter：模擬真實位置 variance
        float jitterX = Random.Range(-0.4f, 0.4f);
        float jitterZ = Random.Range(-0.4f, 0.4f);
        user.transform.position += new Vector3(jitterX, 0f, jitterZ);

        // 穩定幾幀再截圖
        yield return new WaitForSeconds(0.3f);

        // 等待 StaticCameraManager 觸發截圖並 POST /predict（fire-and-forget）
        // VLM 推理在後台非同步進行，Unity 繼續執行
        yield return new WaitForSeconds(waitAfterCapture);

        successRuns++;

        yield return StartCoroutine(user.ReturnToIdle());
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    // ════════════════════════════════════════════════════════════════════════
    // /exp_checkpoint: Exp2 用，記錄每個 episode 的 FAISS similarity
    // 後端需要實作：查詢 User_Mom Drink 的最新 FAISS similarity 並回傳
    // 寫入 exp_checkpoint_logs: {user_id, action, episode, similarity, timestamp}
    // ════════════════════════════════════════════════════════════════════════
    IEnumerator PostExpCheckpoint(int episodeIndex, string userId, string action)
    {
        string json = $"{{\"episode\":{episodeIndex},\"user_id\":\"{userId}\",\"action\":\"{action}\"}}";
        var req = new UnityWebRequest($"{backendUrl}/exp_checkpoint", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 10;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[Exp2] Checkpoint ep{episodeIndex}: {req.downloadHandler.text}");
        }
        else
        {
            Debug.LogWarning($"[Exp2] Checkpoint failed ep{episodeIndex}: {req.error}");
        }
    }

    // ── /set_virtual_hour (fire-and-forget) ───────────────────────────────
    void PostVirtualHourFireAndForget(float hour)
    {
        StartCoroutine(PostVirtualHourRoutine(hour));
    }

    IEnumerator PostVirtualHourRoutine(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour:F1}}}";
        var req = new UnityWebRequest($"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Exp] PostVirtualHour failed: {req.error}");
    }

    // ── BuildWeightedQueue ─────────────────────────────────────────────────
    /// <summary>
    /// 按比例分配 behavior 到 totalCount 個 episode，最後一個 behavior 用餘數修正。
    /// 使用 non-seeded random shuffle（每個 slot 不同但不影響可重現性）。
    /// </summary>
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

        // Non-seeded shuffle per slot
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
        ExperimentMode.Experiment2 => exp2_repeatCount,
        ExperimentMode.Experiment3 => exp3_totalObservations,
        ExperimentMode.Experiment4 => exp4_episodes,
        _ => 0
    };

    // ── OnGUI ──────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!isRunning) return;
        GUI.Label(
            new Rect(10, 10, 600, 22),
            $"[{mode}]  {GetSlotName(currentVirtualHour)} {currentVirtualHour:F0}:00  " +
            $"Progress: {totalRuns} / {GetTargetTotal()}  " +
            $"Success: {successRuns}  [Esc] Stop"
        );
    }
}