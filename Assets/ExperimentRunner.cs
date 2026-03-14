using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ExperimentRunner ¡X ¥‏¦Û°Ê¹ךÅח¸}¥»
///
/// ¥|­Ó¹ךÅח¼Ò¦¡¡G
///   Exp1_VLM      VLM ¿כÃÑ·Ç½T²v¡]Mom/Dad ¦U¦ז¬° x 20 ¦¸¡^
///   Exp3A_Habit   ²‗÷D°O¾Ð²Ö¿n¡]Mom Drink x 30¡AÆ[¹מ FAISS ¬Û¦ü«×¤W¤É¡^
///   Exp4_Manifold Manifold Warmup + ®É¬q¼ÒÀÀ¡]4 ®É¬q x 30¡^
///   Exp5_EndToEnd ÷Ý¨ל÷Ý×A°È¦¨¥\²v¡]30 episodes¡Aseed=42¡^
///
/// ¨Ï¥Î¤ט¦¡¡G
///   Inspector ³]©w¦n«ב«צ Play ¡ק µ¥ Space Áה©Î¤Ä¿ן runOnStart
///   Esc ±j¨מ°±¤מ
///
/// ÷I¹Ï¬yµ{¡]ÃצÁה¡^¡G
///   ExperimentRunner ¥u­t³dÅ‎¨¤¦ג¨«¨ל©wÂI¨Ãµ¥«Ý
///   ÷I¹Ï¥Ñ StaticCameraManager ×÷ SmartScanRoutine ¦Û°Ê°»´תÄ²µo
///   ¤£»Ý­n¤ג°Ê©I¥s÷I¹Ï¡A¥u­n½T«O¬Û¾ק¤w×`¥U§Y¥i
///
/// Inspector ¥²¶ס¡G
///   userMom, userDad, cameraManager, virtualCameraBrain
///   ¦U©Ð¶¡¬Û¾ק²M³ז¡]kitchenNodes / livingRoomNodes / studyNodes¡^
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Inspector Äז¦ל
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    [Header("¹ךÅח¹ן¶H¡]¥²¶ס¡^")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("¬Û¾ק¨t²Î¡]¥²¶ס¡^")]
    public StaticCameraManager cameraManager;
    public VirtualCameraBrain virtualCameraBrain;

    [Header("¦U©Ð¶¡¬Û¾ק¸`ÂI¡]©ל¤J CameraNode ×«¥ף¡^")]
    [Tooltip("¼p©Ð / «ÈÆU / ®Ñ©Ð¦U 2~4 ¥x\n·|¦b Start() ¦Û°Ê×`¥U¨ל StaticCameraManager")]
    public List<CameraNode> kitchenNodes;
    public List<CameraNode> livingRoomNodes;
    public List<CameraNode> studyNodes;

    [Header("¹ךÅח¼Ò¦¡")]
    public ExperimentMode mode = ExperimentMode.Exp1_VLM;

    [Header("¦U¹ךÅח¦¸¼Æ")]
    public int exp1_samplesPerBehavior = 20;   // 6 ¦ז¬° ¡Ñ 20 = 120
    public int exp3a_repeatCount = 30;   // Mom Drink ¡Ñ 30
    public int exp4_totalObservations = 120;  // 4 ®É¬q ¡Ñ 30
    public int exp5_episodes = 30;

    [Header("®É§Ç±±¨מ¡]¬ם¡^")]
    [Tooltip("¨¤¦ג¨ל¹F©wÂI«ב¡Aµ¥³o»ע¦h¬ם\nÅ‎ StaticCameraManager §¹¦¨÷I¹Ï©M POST")]
    public float waitAfterCapture = 3.0f;

    [Tooltip("¨C­Ó episode ¤§¶¡×÷¶¡¹j")]
    public float waitBetweenEpisodes = 2.0f;

    [Tooltip("¦P¤@®É¬q¤÷¨ג­Ó episode ¤§¶¡×÷³Ì¤p¶¡¹j")]
    public float minIntervalInSlot = 1.5f;

    [Header("«ב÷Ý URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("Exp4/5¡G¬O§_±Ò¥ÎµךÀÀ®É¶¡ÂW°O")]
    public bool useTimestamp = true;

    [Header("°ץ¦ז±±¨מ")]
    [Tooltip("¤Ä¿ן«ב Play ¦Û°Ê¶}©l¡]¤£»Ý«צ Space¡^")]
    public bool runOnStart = false;

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¹ךÅח¼Ò¦¡
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    public enum ExperimentMode
    {
        Exp1_VLM,
        Exp3A_Habit,
        Exp4_Manifold,
        Exp5_EndToEnd
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¦ז¬°©w¸q & ®É¬q°t¸m
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    static readonly string[] MomBehaviors = { "drink", "sit", "reading" };
    static readonly string[] DadBehaviors = { "drink", "sit", "typing" };

    struct TimeSlot
    {
        public string name;
        public float virtualHour;
        public Dictionary<string, int> momWeights;
        public Dictionary<string, int> dadWeights;
    }

    // ¥|­Ó®É¬q×÷¦ז¬°¥[Åv¡]¼Æ¦r¶V¤j¡A¸Ó®É¬q¶V±`¥X²{¦¹¦ז¬°¡^
    static readonly TimeSlot[] TimeSlots = new TimeSlot[]
    {
        new TimeSlot { name="Morning",   virtualHour=7f,
            momWeights = new Dictionary<string,int>{ {"drink",3},{"sit",1},{"reading",1} },
            dadWeights = new Dictionary<string,int>{ {"drink",2},{"sit",1},{"typing",3}  } },
        new TimeSlot { name="Noon",      virtualHour=12f,
            momWeights = new Dictionary<string,int>{ {"drink",2},{"sit",3},{"reading",2} },
            dadWeights = new Dictionary<string,int>{ {"drink",2},{"sit",2},{"typing",2}  } },
        new TimeSlot { name="Afternoon", virtualHour=15f,
            momWeights = new Dictionary<string,int>{ {"drink",1},{"sit",2},{"reading",3} },
            dadWeights = new Dictionary<string,int>{ {"drink",1},{"sit",1},{"typing",4}  } },
        new TimeSlot { name="Evening",   virtualHour=20f,
            momWeights = new Dictionary<string,int>{ {"drink",2},{"sit",4},{"reading",3} },
            dadWeights = new Dictionary<string,int>{ {"drink",2},{"sit",4},{"typing",1}  } },
    };

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // °ץ¦ז´Á×¬÷A
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    int totalRuns = 0;
    int successRuns = 0;
    bool isRunning = false;
    float currentVirtualHour = 7f;

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Unity ¥Í©R¶g´Á
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    void Start()
    {
        // ¢w¢w ×`¥U¬Û¾ק¨ל StaticCameraManager ¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w
        // ½T«O StaticCameraManager ¯א§ה¨ל¬Û¾ק¡A¶¶§Ç¦b SmartScanRoutine ¤§«e
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
            if (studyNodes != null && studyNodes.Count > 0)
                cameraManager.RegisterRoomCameras("Study", studyNodes);
        }
        else
        {
            Debug.LogError("[ExperimentRunner] §ה¤£¨ל StaticCameraManager¡I\n" +
                           "½Ð¦b³ץ´÷¤¤«Ø¤@­Ó×Å×«¥ף¨Ã±¾¤W StaticCameraManager.cs");
        }

        // ¢w¢w ×`¤J VirtualCameraBrain µ¹ StaticCameraManager ¢w¢w
        if (virtualCameraBrain != null && cameraManager != null)
            cameraManager.virtualCameraBrain = virtualCameraBrain;

        // ¢w¢w ¦Û°Ê¶}©l ¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w¢w
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
            Debug.Log("[Exp] ±j¨מ°±¤מ¡]Esc¡^");
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¤½¶}±Ò°Ê¤¶­±
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    public void StartExperiment()
    {
        if (isRunning)
        {
            Debug.LogWarning("[Exp] ¹ךÅח¥¿¦b°ץ¦ז¤¤¡A½Ðµ¥­Ô§¹¦¨©Î«צ Esc °±¤מ");
            return;
        }
        totalRuns = 0;
        successRuns = 0;
        Debug.Log($"[Exp] שששששששששששש ¶}©l {mode} שששששששששששש");
        StartCoroutine(RunExperiment());
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¥D¬yµ{
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

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
        Debug.Log($"[Exp] שששששששששששש §¹¦¨ {mode}  " +
                  $"Á`:{totalRuns}  ¦¨¥\:{successRuns}  ({rate:F1}%) שששששששששששש");
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Exp1¡GVLM ¿כÃÑ·Ç½T²v
    //   ¦U¦ז¬°³sÄע¶]§¹¡]per-class ¶¶§Ç¡A¤ט«K¬Ý confusion matrix¡^
    //   ¤£±a timestamp
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator RunExp1()
    {
        int total = (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior;
        Debug.Log($"[Exp1] ¥Ø¼Ð¡G{total} ¦¸¡]¨C¦ז¬° {exp1_samplesPerBehavior} ¦¸¡^");

        foreach (var b in MomBehaviors)
        {
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                Debug.Log($"[Exp1] Mom.{b}  {i + 1}/{exp1_samplesPerBehavior}");
                yield return StartCoroutine(RunSingleEpisode(userMom, b, -1f));
                totalRuns++;
            }
        }

        foreach (var b in DadBehaviors)
        {
            for (int i = 0; i < exp1_samplesPerBehavior; i++)
            {
                Debug.Log($"[Exp1] Dad.{b}  {i + 1}/{exp1_samplesPerBehavior}");
                yield return StartCoroutine(RunSingleEpisode(userDad, b, -1f));
                totalRuns++;
            }
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Exp3A¡G²‗÷D°O¾Ð²Ö¿n¡]Mom Drink ¡Ñ 30¡^
    //   «ב÷Ý¨C¦¸°O¿‎ FAISS similarity¡Aµe¤W¤É¦±½u
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator RunExp3A()
    {
        Debug.Log($"[Exp3A] Mom Drink ¡Ñ {exp3a_repeatCount}");

        for (int i = 0; i < exp3a_repeatCount; i++)
        {
            Debug.Log($"[Exp3A] {i + 1}/{exp3a_repeatCount}");
            yield return StartCoroutine(RunSingleEpisode(userMom, "drink", -1f));
            totalRuns++;
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Exp4¡GManifold Warmup + ®É¬q¼ÒÀÀ
    //   4 ®É¬q ¡Ñ (Mom 15 + Dad 15) = 120 ¦¸
    //   ¨Ì®É¬qÅv­«¿ן¦ז¬°¡AMom/Dad ¥ז´À
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator RunExp4()
    {
        int perSlot = exp4_totalObservations / TimeSlots.Length;   // 30
        int perPersonSlot = perSlot / 2;                                  // 15

        Debug.Log($"[Exp4] 4 ®É¬q ¡Ñ {perSlot}¡]¨C¤H {perPersonSlot}¡^= {exp4_totalObservations} ¦¸");

        foreach (var slot in TimeSlots)
        {
            currentVirtualHour = slot.virtualHour;
            Debug.Log($"[Exp4] ¢w¢w {slot.name} ({slot.virtualHour:F0}:00) ¢w¢w");

            var momQueue = BuildWeightedQueue(MomBehaviors, slot.momWeights, perPersonSlot);
            var dadQueue = BuildWeightedQueue(DadBehaviors, slot.dadWeights, perPersonSlot);
            int maxLen = Mathf.Max(momQueue.Count, dadQueue.Count);

            for (int i = 0; i < maxLen; i++)
            {
                if (i < momQueue.Count)
                {
                    string b = momQueue[i];
                    Debug.Log($"[Exp4] Mom.{b} @ {slot.name} {slot.virtualHour:F0}:00");
                    if (useTimestamp) yield return StartCoroutine(PostVirtualHour(slot.virtualHour));
                    yield return StartCoroutine(RunSingleEpisode(userMom, b, slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }

                if (i < dadQueue.Count)
                {
                    string b = dadQueue[i];
                    Debug.Log($"[Exp4] Dad.{b} @ {slot.name} {slot.virtualHour:F0}:00");
                    if (useTimestamp) yield return StartCoroutine(PostVirtualHour(slot.virtualHour));
                    yield return StartCoroutine(RunSingleEpisode(userDad, b, slot.virtualHour));
                    yield return new WaitForSeconds(minIntervalInSlot);
                    totalRuns++;
                }
            }
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // Exp5¡G÷Ý¨ל÷Ý×A°È¦¨¥\²v
    //   ÀH¾ק¶¶§Ç¡]seed=42¡^¡AProactiveServiceManager ½ü¸‗´£®×
    //   ±µ Exp4 ×÷ DB¡]Manifold ¤w warmup¡^
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator RunExp5()
    {
        Debug.Log($"[Exp5] ÷Ý¨ל÷Ý ¡X {exp5_episodes} episodes¡]seed=42¡^");

        // «Ø episode ¦À¡]fixed seed «OÃÒ¥i­«²{¡^
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

        // Fisher-Yates shuffle
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
            Debug.Log($"[Exp5] Episode {ep + 1}/{limit}: {user.userID}.{behavior}");

            if (useTimestamp) yield return StartCoroutine(PostVirtualHour(hour));
            yield return StartCoroutine(RunSingleEpisode(user, behavior, hour));
            totalRuns++;
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ³ז¦¸ Episode ®Ö¤‗¬yµ{
    //
    // ¬yµ{¡G
    //   1. SwitchActivity ¡ק ¨¤¦ג¨«¨ל©wÂI¡B¶i¤J loop °Êµe
    //   2. waitAfterCapture ¬ם¡]Å‎ StaticCameraManager §¹¦¨÷I¹Ï©M POST¡^
    //   3. ReturnToIdle ¡ק ¨¤¦ג¨«¦^«Ý¾קÂI
    //   4. waitBetweenEpisodes ¬ם¶¡¹j
    //
    // Ãצ©ף virtualHour¡G
    //   ¶Çµ¹ VirtualCameraBrain.SetVirtualHour()¡AÅ‎÷I¹Ï POST ±a¥¿½T®É¶¡
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator RunSingleEpisode(UserEntity user, string behavior, float virtualHour)
    {
        // ×`¤J®É¶¡¡]Å‎÷I¹Ï®É±a¥¿½T×÷ virtual_hour¡^
        if (virtualCameraBrain != null && virtualHour >= 0f)
            virtualCameraBrain.SetVirtualHour(virtualHour);

        // 1. ¨¤¦ג¨«¨ל©wÂI¨Ã¶i¤J loop °Êµe
        yield return StartCoroutine(user.SwitchActivity(behavior));

        // 2. µ¥«Ý÷I¹Ï§¹¦¨¡]StaticCameraManager °»´ת¨ל×¬÷A§ןÅÜ«ב¦Û°Ê÷I¹Ï¡^
        //    waitAfterCapture ¹w³] 3 ¬ם¡Gsettle(0.5) + ÷I¹Ï(0.5) + POST(1.0) + buffer(1.0)
        yield return new WaitForSeconds(waitAfterCapture);

        successRuns++;

        // 3. ¨«¦^«Ý¾קÂI
        yield return StartCoroutine(user.ReturnToIdle());

        // 4. episode ¶¡¹j
        yield return new WaitForSeconds(waitBetweenEpisodes);
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // »²§U¡GPOST µךÀÀ®É¶¡¨ל«ב÷Ý¡]Å‎«ב÷Ý°O¿‎ sin/cos ®É¶¡¯S¼x¡^
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    IEnumerator PostVirtualHour(float hour)
    {
        string json = $"{{\"virtual_hour\":{hour:F1}}}";
        var req = new UnityWebRequest($"{backendUrl}/set_virtual_hour", "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        // ¥¢±Ñ¤£¤¤Â_¹ךÅח¡A¥u log
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Exp] PostVirtualHour ¥¢±Ñ: {req.error}¡]Flask ¬O§_¦b°ץ¦ז¡H¡^");
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // «Ø¥‗¥[Åv¦ז¬°¦מ¦C¡]Fisher-Yates shuffle¡^
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

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
            for (int j = 0; j < count; j++) result.Add(b);
            allocated += count;
        }

        // Shuffle
        var rng = new System.Random();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // OnGUI¡G¥×¤W¨¤¶i«×Åד¥Ü
    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש

    void OnGUI()
    {
        if (!isRunning) return;

        string slotName = currentVirtualHour >= 18f ? "Evening"
                        : currentVirtualHour >= 13f ? "Afternoon"
                        : currentVirtualHour >= 10f ? "Noon"
                        : "Morning";

        GUI.Label(new Rect(10, 10, 500, 22),
            $"[{mode}]  {slotName} {currentVirtualHour:F0}:00  " +
            $"¶i«×: {totalRuns} / {GetTargetTotal()}  " +
            $"¦¨¥\: {successRuns}  [Esc °±¤מ]");
    }

    int GetTargetTotal() => mode switch
    {
        ExperimentMode.Exp1_VLM =>
            (MomBehaviors.Length + DadBehaviors.Length) * exp1_samplesPerBehavior,
        ExperimentMode.Exp3A_Habit => exp3a_repeatCount,
        ExperimentMode.Exp4_Manifold => exp4_totalObservations,
        ExperimentMode.Exp5_EndToEnd => exp5_episodes,
        _ => 0
    };
}