using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DemoRunner — 口試 Demo 三情境手動觸發腳本
///
/// 口試流程（鍵盤觸發）：
///   [1] 情境一：Mom 喝水 → 機器人送水杯 → Mom 點頭
///   [2] 情境二：Dad 打字 → 機器人送咖啡 → Dad 點頭
///   [3] 情境三：Mom 閱讀 → Manifold 主動預判 → Mom 點頭
///   [4] 情境四：Mom + Dad 同時行動（雙人 Demo）
///   [R] 全部重置回 Idle
///
/// 設計原則：
///   - 每個情境獨立，不依賴前一個情境
///   - IsBusy 防止重複觸發
///   - 全部操作都是 Coroutine，不阻塞主執行緒
///   - 機器人移動（RobotController）為可選，未掛就跳過
///
/// Inspector 必填：
///   userMom, userDad
///   cameraManager（StaticCameraManager）
///
/// Inspector 選填：
///   robot（機器人的 NavMeshAgent 物件）
///   robotServiceItem（機器人手持物件）
///   kitchenTableSpot / sofaSpot（機器人目標點）
///
/// 提示 UI：
///   在 OnGUI 顯示目前狀態和可用按鍵，Debug.Log 同步輸出
/// </summary>
public class DemoRunner : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector 欄位
    // ══════════════════════════════════════════════════════

    [Header("角色（必填）")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("StaticCameraManager（必填）")]
    public StaticCameraManager cameraManager;

    [Header("相機節點（每個房間 2~4 台）")]
    public List<CameraNode> kitchenNodes;
    public List<CameraNode> livingRoomNodes;
    public List<CameraNode> studyNodes;

    [Header("機器人（選填，未填則跳過機器人動作）")]
    [Tooltip("機器人 GameObject，掛有 NavMeshAgent")]
    public GameObject robot;

    [Tooltip("機器人移動速度（m/s）")]
    public float robotSpeed = 2f;

    [Header("機器人目標點（空物件，Z 軸朝角色）")]
    [Tooltip("廚房桌前（情境一：Mom 喝水服務點）")]
    public Transform kitchenServiceSpot;

    [Tooltip("沙發前（情境三：Mom 閱讀服務點）")]
    public Transform sofaServiceSpot;

    [Tooltip("書桌前（情境二：Dad 打字服務點）")]
    public Transform deskServiceSpot;

    [Header("機器人手持物件（選填）")]
    [Tooltip("水杯 / 咖啡杯等，自動 SetActive 控制顯示")]
    public GameObject robotServiceItem;

    [Header("Demo 時機設定（秒）")]
    [Tooltip("角色到位後等幾秒截圖（讓動畫穩定）")]
    public float captureDelay = 2.0f;

    [Tooltip("截圖後等幾秒才讓機器人出發（模擬 AI 決策時間）")]
    public float robotDispatchDelay = 1.5f;

    [Tooltip("機器人到達後等幾秒讓角色點頭")]
    public float nodDelay = 0.5f;

    [Tooltip("點頭後等幾秒再 Reset（讓觀眾看清楚）")]
    public float postNodDelay = 2.0f;

    // ══════════════════════════════════════════════════════
    // 私有成員
    // ══════════════════════════════════════════════════════

    bool isBusy = false;
    string statusText = "準備就緒。按 [1][2][3][4] 觸發情境，[R] 重置";
    UnityEngine.AI.NavMeshAgent robotAgent;

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        // 注冊相機到 StaticCameraManager
        if (cameraManager != null)
        {
            if (kitchenNodes != null && kitchenNodes.Count > 0)
                cameraManager.RegisterRoomCameras("Kitchen", kitchenNodes);
            if (livingRoomNodes != null && livingRoomNodes.Count > 0)
                cameraManager.RegisterRoomCameras("LivingRoom", livingRoomNodes);
            if (studyNodes != null && studyNodes.Count > 0)
                cameraManager.RegisterRoomCameras("Study", studyNodes);
        }

        // 取機器人 NavMeshAgent
        if (robot != null)
        {
            robotAgent = robot.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (robotAgent != null) robotAgent.speed = robotSpeed;
        }

        // 隱藏機器人手持物件
        SetRobotItemVisible(false);
    }

    void Update()
    {
        if (isBusy) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) StartCoroutine(Demo1_MomDrink());
        if (Input.GetKeyDown(KeyCode.Alpha2)) StartCoroutine(Demo2_DadTyping());
        if (Input.GetKeyDown(KeyCode.Alpha3)) StartCoroutine(Demo3_MomReadingManifold());
        if (Input.GetKeyDown(KeyCode.Alpha4)) StartCoroutine(Demo4_BothUsers());
        if (Input.GetKeyDown(KeyCode.R)) StartCoroutine(ResetAll());
    }

    // ──────────────────────────────────────────────────────
    // OnGUI：左下角提示面板
    // ──────────────────────────────────────────────────────
    void OnGUI()
    {
        float w = 480f, h = 140f;
        float x = 20f, y = Screen.height - h - 20f;

        GUI.Box(new Rect(x, y, w, h), "");
        GUILayout.BeginArea(new Rect(x + 10, y + 10, w - 20, h - 20));

        GUI.color = isBusy ? Color.yellow : Color.white;
        GUILayout.Label(statusText);
        GUI.color = Color.white;

        GUILayout.Space(6);
        GUI.color = new Color(0.7f, 1f, 0.7f);
        GUILayout.Label("[1] 情境一：Mom 喝水   [2] 情境二：Dad 打字");
        GUILayout.Label("[3] 情境三：Mom 閱讀（Manifold）   [4] 情境四：雙人");
        GUILayout.Label("[R] 全部重置回 Idle");
        GUI.color = Color.white;

        GUILayout.EndArea();
    }

    // ══════════════════════════════════════════════════════
    // 情境一：Mom 喝水 → 機器人送水 → Mom 點頭
    // ══════════════════════════════════════════════════════
    IEnumerator Demo1_MomDrink()
    {
        isBusy = true;
        SetStatus("情境一：Mom 走向飲水機...");

        // 1. Mom 走到喝水點
        yield return StartCoroutine(userMom.SwitchActivity("drink"));

        SetStatus("情境一：截圖中 → 送 /predict...");
        yield return new WaitForSeconds(captureDelay);

        // 2. 機器人出發（模擬 AI 收到 /predict 回應後）
        SetStatus("情境一：AI 決策完成，機器人出發...");
        yield return new WaitForSeconds(robotDispatchDelay);

        if (kitchenServiceSpot != null)
        {
            SetRobotItemVisible(true);
            yield return StartCoroutine(MoveRobotTo(kitchenServiceSpot.position));
        }

        // 3. Mom 點頭接受服務
        yield return new WaitForSeconds(nodDelay);
        SetStatus("情境一：Mom 點頭接受服務 ✓");
        yield return StartCoroutine(userMom.Nod());

        yield return new WaitForSeconds(postNodDelay);
        SetRobotItemVisible(false);
        ResetRobotPosition();

        SetStatus("情境一完成。按 [R] 重置或繼續下一情境。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // 情境二：Dad 打字 → 機器人送咖啡 → Dad 點頭
    // ══════════════════════════════════════════════════════
    IEnumerator Demo2_DadTyping()
    {
        isBusy = true;
        SetStatus("情境二：Dad 走向書桌...");

        yield return StartCoroutine(userDad.SwitchActivity("typing"));

        SetStatus("情境二：截圖中 → 送 /predict...");
        yield return new WaitForSeconds(captureDelay);

        SetStatus("情境二：AI 決策完成，機器人出發...");
        yield return new WaitForSeconds(robotDispatchDelay);

        if (deskServiceSpot != null)
        {
            SetRobotItemVisible(true);
            yield return StartCoroutine(MoveRobotTo(deskServiceSpot.position));
        }

        yield return new WaitForSeconds(nodDelay);
        SetStatus("情境二：Dad 點頭接受服務 ✓");
        yield return StartCoroutine(userDad.Nod());

        yield return new WaitForSeconds(postNodDelay);
        SetRobotItemVisible(false);
        ResetRobotPosition();

        SetStatus("情境二完成。按 [R] 重置或繼續下一情境。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // 情境三：Mom 閱讀 → Manifold 主動預判 → 機器人預先到位 → Mom 點頭
    //
    // 說明這個情境的特色（在 GUI 顯示）：
    //   「Manifold Learning 已積累足夠行為記錄，
    //     在 Mom 尚未開始閱讀前就預判她的意圖，
    //     機器人提前移動到沙發旁等待」
    // ══════════════════════════════════════════════════════
    IEnumerator Demo3_MomReadingManifold()
    {
        isBusy = true;
        SetStatus("情境三（Manifold 預判）：Mom 走向沙發...");

        // 機器人「提前」出發（Manifold 預判，不等截圖）
        SetStatus("情境三：Manifold 信心度 ≥ 0.80 → 機器人提前出發！");
        SetRobotItemVisible(true);
        Coroutine robotMove = null;
        if (sofaServiceSpot != null)
            robotMove = StartCoroutine(MoveRobotTo(sofaServiceSpot.position));

        // Mom 同時走向閱讀點
        yield return StartCoroutine(userMom.SwitchActivity("reading"));

        // 等機器人也到達（若還沒到）
        if (robotMove != null)
            yield return robotMove;

        SetStatus("情境三：機器人已在沙發旁等待 → Mom 點頭 ✓");
        yield return new WaitForSeconds(nodDelay);
        yield return StartCoroutine(userMom.Nod());

        yield return new WaitForSeconds(postNodDelay);
        SetRobotItemVisible(false);
        ResetRobotPosition();

        SetStatus("情境三完成（Manifold 預判示範成功）。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // 情境四：Mom + Dad 同時行動
    // ══════════════════════════════════════════════════════
    IEnumerator Demo4_BothUsers()
    {
        isBusy = true;
        SetStatus("情境四：Mom 閱讀 + Dad 坐下（同時）...");

        // 兩人同時出發（不等對方完成）
        bool momDone = false, dadDone = false;
        StartCoroutine(RunUser(userMom, "reading", () => momDone = true));
        StartCoroutine(RunUser(userDad, "sit", () => dadDone = true));

        // 等兩人都到達目標
        yield return new WaitUntil(() => momDone && dadDone);

        SetStatus("情境四：兩人都就定位，截圖中...");
        yield return new WaitForSeconds(captureDelay);

        SetStatus("情境四：AI 同時識別兩個使用者 ✓");
        yield return new WaitForSeconds(postNodDelay);

        SetStatus("情境四完成。按 [R] 重置。");
        isBusy = false;
    }

    IEnumerator RunUser(UserEntity user, string activity, System.Action onDone)
    {
        yield return StartCoroutine(user.SwitchActivity(activity));
        onDone?.Invoke();
    }

    // ══════════════════════════════════════════════════════
    // 全部重置
    // ══════════════════════════════════════════════════════
    IEnumerator ResetAll()
    {
        isBusy = true;
        SetStatus("重置中...");

        SetRobotItemVisible(false);
        ResetRobotPosition();

        // 兩人同時走回 Idle
        bool momDone = false, dadDone = false;
        StartCoroutine(ReturnUser(userMom, () => momDone = true));
        StartCoroutine(ReturnUser(userDad, () => dadDone = true));
        yield return new WaitUntil(() => momDone && dadDone);

        SetStatus("重置完成。[1][2][3][4] 觸發情境，[R] 重置");
        isBusy = false;
    }

    IEnumerator ReturnUser(UserEntity user, System.Action onDone)
    {
        if (user != null)
            yield return StartCoroutine(user.ReturnToIdle());
        onDone?.Invoke();
    }

    // ══════════════════════════════════════════════════════
    // 機器人移動（NavMeshAgent）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 讓機器人走到指定位置
    /// 若沒有機器人 GameObject 直接跳過（yield break）
    /// </summary>
    IEnumerator MoveRobotTo(Vector3 targetPos)
    {
        if (robotAgent == null) yield break;

        robotAgent.isStopped = false;
        robotAgent.SetDestination(targetPos);

        // 等路徑計算
        yield return null;
        yield return new WaitUntil(() => !robotAgent.pathPending);

        // 等到達
        while (!robotAgent.pathPending && robotAgent.remainingDistance > 0.35f)
            yield return null;

        robotAgent.isStopped = true;
        robotAgent.velocity = Vector3.zero;
    }

    /// <summary>機器人走回場景原點（或自訂待機點）</summary>
    void ResetRobotPosition()
    {
        if (robotAgent == null || robot == null) return;
        robotAgent.isStopped = true;
        robotAgent.Warp(Vector3.zero); // 改成機器人待機點
    }

    // ══════════════════════════════════════════════════════
    // 輔助方法
    // ══════════════════════════════════════════════════════

    void SetStatus(string msg)
    {
        statusText = msg;
        Debug.Log($"[DemoRunner] {msg}");
    }

    void SetRobotItemVisible(bool visible)
    {
        if (robotServiceItem != null)
            robotServiceItem.SetActive(visible);
    }
}