using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// DemoRunner — 口試三階段 Demo（鍵盤手動觸發）
///
/// 鍵盤控制：
///   [1] Phase 1A：User A 說「我餓了」→ RAG 找蘋果 → 機器人去 Kitchen → 回應
///   [2] Phase 1B：User B 說「我餓了」→ RAG 找香蕉 → 機器人去沙發旁 → 回應
///   [3] Phase 2 ：User A 走到沙發坐下 → Manifold 預判 → 機器人主動問
///   [4] Phase 3 ：移走香蕉 → User B 說餓 → Cross-match 失敗 → 改推蘋果
///   [R] Reset   ：全部重置
///
/// 機器人動作：只有走路（Walk），不需要複雜動畫
/// 對話框：OnGUI 顯示，口試時觀眾看得到
///
/// Inspector 必填：
///   userMom（User A）, userDad（User B）
///   robot（機器人 GameObject）
///   kitchenSpot（Kitchen 服務點）
///   sofaSpot（沙發服務點）
///   banana（香蕉 GameObject，Phase 3 隱藏用）
/// </summary>
public class DemoRunner : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector
    // ══════════════════════════════════════════════════════

    [Header("角色")]
    [Tooltip("User A = Mom（客廳）")]
    public UserEntity userMom;
    [Tooltip("User B = Dad（書房）")]
    public UserEntity userDad;

    [Header("機器人")]
    public GameObject robot;
    public float robotSpeed = 1.8f;

    [Header("機器人服務目標點（空物件，Y=0）")]
    public Transform kitchenServiceSpot;   // Phase 1A：去 Kitchen 拿蘋果
    public Transform sofaServiceSpot;      // Phase 1B / Phase 2：去沙發旁
    public Transform robotIdleSpot;        // 機器人待機位置

    [Header("道具 GameObject")]
    [Tooltip("Phase 3 時呼叫 SetActive(false) 模擬移走")]
    public GameObject banana;
    public GameObject apple;

    [Header("後端 URL")]
    public string backendUrl = "http://localhost:5000";

    [Header("時機設定（秒）")]
    public float dialogDelay = 1.5f;   // 對話框出現延遲
    public float robotDispatchDelay = 1.0f; // AI 決策延遲（模擬思考時間）
    public float postNodDelay = 2.0f;   // 點頭後等待

    // ══════════════════════════════════════════════════════
    // 私有成員
    // ══════════════════════════════════════════════════════

    bool isBusy = false;
    string dialogText = "";
    string phaseText = "就緒 — [1] Phase1A  [2] Phase1B  [3] Phase2  [4] Phase3  [R] Reset";

    UnityEngine.AI.NavMeshAgent robotAgent;

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        if (robot != null)
        {
            robotAgent = robot.GetComponent<UnityEngine.AI.NavMeshAgent>();
            // 若沒有 NavMeshAgent 就用純 Transform
        }
    }

    void Update()
    {
        if (isBusy) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) StartCoroutine(Phase1A());
        if (Input.GetKeyDown(KeyCode.Alpha2)) StartCoroutine(Phase1B());
        if (Input.GetKeyDown(KeyCode.Alpha3)) StartCoroutine(Phase2());
        if (Input.GetKeyDown(KeyCode.Alpha4)) StartCoroutine(Phase3());
        if (Input.GetKeyDown(KeyCode.R)) StartCoroutine(ResetAll());
    }

    // ══════════════════════════════════════════════════════
    // OnGUI：對話框 + 狀態提示
    // ══════════════════════════════════════════════════════

    void OnGUI()
    {
        // 下方狀態列
        float w = 560, h = 36;
        GUI.Box(new Rect(Screen.width / 2f - w / 2f, Screen.height - h - 10, w, h), "");
        GUI.Label(new Rect(Screen.width / 2f - w / 2f + 10,
                           Screen.height - h - 8, w - 20, h),
                  isBusy ? $"▶ {phaseText}" : phaseText);

        // 對話框（螢幕中央上方）
        if (!string.IsNullOrEmpty(dialogText))
        {
            float dw = 480, dh = 60;
            float dx = Screen.width / 2f - dw / 2f;
            float dy = Screen.height * 0.12f;
            GUI.Box(new Rect(dx, dy, dw, dh), "");

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                wordWrap = true
            };
            GUI.Label(new Rect(dx + 10, dy + 8, dw - 20, dh - 16), dialogText, style);
        }
    }

    // ══════════════════════════════════════════════════════
    // Phase 1A：User A（Mom）說「我餓了」
    //   RAG 找到蘋果偏好 → 機器人去 Kitchen → 回應
    // ══════════════════════════════════════════════════════

    IEnumerator Phase1A()
    {
        isBusy = true;
        SetPhase("Phase 1A：User A 個人化服務");

        // User A 說話
        ShowDialog("User A：我餓了。");
        yield return new WaitForSeconds(dialogDelay);

        // 後端 RAG 查詢（fire-and-forget，不等回應也能繼續 Demo）
        StartCoroutine(PostInteract("User_Mom", "我餓了"));

        // 模擬 AI 思考
        ShowDialog("系統：查詢歷史記錄… 發現偏好：蘋果 🍎");
        yield return new WaitForSeconds(robotDispatchDelay + 0.5f);

        // 機器人出發去 Kitchen
        ShowDialog("機器人：前往廚房拿蘋果…");
        if (kitchenServiceSpot != null)
            yield return StartCoroutine(MoveRobotTo(kitchenServiceSpot.position));

        // 機器人回應
        ShowDialog("機器人：User A，你喜歡吃蘋果，廚房桌上有。");
        yield return new WaitForSeconds(2.5f);

        ClearDialog();
        SetPhase("Phase 1A 完成。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // Phase 1B：User B（Dad）說「我餓了」
    //   RAG 找到讀書+香蕉偏好 → 機器人去沙發旁 → 回應
    // ══════════════════════════════════════════════════════

    IEnumerator Phase1B()
    {
        isBusy = true;
        SetPhase("Phase 1B：User B 個人化服務");

        ShowDialog("User B：我餓了。");
        yield return new WaitForSeconds(dialogDelay);

        StartCoroutine(PostInteract("User_Dad", "我餓了"));

        ShowDialog("系統：查詢歷史記錄… 發現：讀書時偏好香蕉 🍌");
        yield return new WaitForSeconds(robotDispatchDelay + 0.5f);

        ShowDialog("機器人：前往沙發旁…");
        if (sofaServiceSpot != null)
            yield return StartCoroutine(MoveRobotTo(sofaServiceSpot.position));

        ShowDialog("機器人：User B，你讀書時愛吃香蕉，沙發旁有一根。");
        yield return new WaitForSeconds(2.5f);

        ClearDialog();
        SetPhase("Phase 1B 完成。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // Phase 2：Manifold 主動預判
    //   15:00，User A 走到沙發坐下（不說話）
    //   系統截圖 → Manifold → 比對聚類 → 主動詢問
    // ══════════════════════════════════════════════════════

    IEnumerator Phase2()
    {
        isBusy = true;
        SetPhase("Phase 2：Manifold 主動預判（15:00）");

        ShowDialog("15:00 下午茶時間");
        yield return new WaitForSeconds(1f);

        // User A 走到沙發坐下（觸發截圖）
        ShowDialog("User A 走向沙發…（未說話）");
        StartCoroutine(userMom.SwitchActivity("sit"));

        // 等角色到位（走路 + teleport）
        yield return new WaitForSeconds(3.5f);

        // Manifold 分析
        ShowDialog("Manifold：分析特徵向量 [User_A, 15:00, 沙發, SittingIdle]");
        yield return new WaitForSeconds(1.5f);
        ShowDialog("Manifold：比對聚類 → 落在「User A + 下午 + 沙發 → 蘋果」區域");
        yield return new WaitForSeconds(1.5f);

        // 機器人主動詢問
        ShowDialog("機器人：你現在想吃點蘋果嗎？我可以幫你拿。");
        yield return new WaitForSeconds(dialogDelay);

        // User A 點頭
        ShowDialog("User A 點頭接受 ✓");
        yield return StartCoroutine(userMom.Nod());
        yield return new WaitForSeconds(0.5f);

        // 機器人去 Kitchen 拿蘋果
        ShowDialog("機器人：前往廚房拿蘋果…");
        if (kitchenServiceSpot != null)
            yield return StartCoroutine(MoveRobotTo(kitchenServiceSpot.position));

        ShowDialog("機器人：蘋果拿來了！");
        yield return new WaitForSeconds(postNodDelay);

        ClearDialog();
        SetPhase("Phase 2 完成。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // Phase 3：動態環境感知
    //   移走香蕉 → User B 說餓 → Cross-match 失敗 → 改推蘋果
    // ══════════════════════════════════════════════════════

    IEnumerator Phase3()
    {
        isBusy = true;
        SetPhase("Phase 3：動態環境感知");

        // 移走香蕉
        ShowDialog("【環境變化】沙發旁的香蕉被移走了…");
        if (banana != null) banana.SetActive(false);
        yield return new WaitForSeconds(1.5f);

        // User B 說話
        ShowDialog("User B：我還是很餓，有其他吃的嗎？");
        yield return new WaitForSeconds(dialogDelay);

        StartCoroutine(PostInteract("User_Dad", "我還是很餓，有其他吃的嗎？"));

        // Cross-match 失敗
        ShowDialog("系統：Cross-match 檢查… 香蕉不在場景中！");
        yield return new WaitForSeconds(1.5f);
        ShowDialog("系統：決策修正 → 排除香蕉，改推薦次佳選項：蘋果 🍎");
        yield return new WaitForSeconds(1.5f);

        // 機器人回應並移動
        ShowDialog("機器人：香蕉不見了，要不要改吃廚房的蘋果？");
        if (kitchenServiceSpot != null)
            yield return StartCoroutine(MoveRobotTo(kitchenServiceSpot.position));

        ShowDialog("機器人：廚房的蘋果在這裡！");
        yield return new WaitForSeconds(postNodDelay);

        ClearDialog();
        SetPhase("Phase 3 完成。");
        isBusy = false;
    }

    // ══════════════════════════════════════════════════════
    // Reset
    // ══════════════════════════════════════════════════════

    IEnumerator ResetAll()
    {
        isBusy = true;
        SetPhase("重置中…");
        ClearDialog();

        // 還原香蕉
        if (banana != null) banana.SetActive(true);

        // 機器人回待機
        ResetRobot();

        // 角色回 IdleSpot
        bool momDone = false, dadDone = false;
        StartCoroutine(ReturnUser(userMom, () => momDone = true));
        StartCoroutine(ReturnUser(userDad, () => dadDone = true));
        yield return new WaitUntil(() => momDone && dadDone);

        SetPhase("就緒 — [1] Phase1A  [2] Phase1B  [3] Phase2  [4] Phase3  [R] Reset");
        isBusy = false;
    }

    IEnumerator ReturnUser(UserEntity user, System.Action done)
    {
        if (user != null) yield return StartCoroutine(user.ReturnToIdle());
        done?.Invoke();
    }

    // ══════════════════════════════════════════════════════
    // 機器人移動（純 Transform，無 NavMeshAgent 依賴）
    // ══════════════════════════════════════════════════════

    IEnumerator MoveRobotTo(Vector3 target)
    {
        if (robot == null) yield break;

        target.y = 0f;
        float speed = robotSpeed;

        while (true)
        {
            Vector3 cur = new Vector3(
                robot.transform.position.x, 0f, robot.transform.position.z);
            if (Vector3.Distance(cur, target) <= 0.15f) break;

            robot.transform.position =
                Vector3.MoveTowards(cur, target, speed * Time.deltaTime);

            Vector3 dir = (target - cur).normalized;
            if (dir.sqrMagnitude > 0.001f)
                robot.transform.rotation = Quaternion.Slerp(
                    robot.transform.rotation,
                    Quaternion.LookRotation(dir), Time.deltaTime * 6f);

            yield return null;
        }
        robot.transform.position = new Vector3(target.x, 0f, target.z);
    }

    void ResetRobot()
    {
        if (robot == null || robotIdleSpot == null) return;
        robot.transform.position = robotIdleSpot.position;
        robot.transform.rotation = robotIdleSpot.rotation;
    }

    // ══════════════════════════════════════════════════════
    // 後端呼叫（fire-and-forget）
    // ══════════════════════════════════════════════════════

    IEnumerator PostInteract(string userId, string utterance)
    {
        string json = $"{{\"user_id\":\"{userId}\",\"utterance\":\"{utterance}\"}}";
        using var req = new UnityWebRequest($"{backendUrl}/interact", "POST");
        req.uploadHandler = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();
        // 不等回應，Demo 流程繼續
    }

    // ══════════════════════════════════════════════════════
    // 輔助
    // ══════════════════════════════════════════════════════

    void ShowDialog(string text)
    {
        dialogText = text;
        Debug.Log($"[Demo] {text}");
    }

    void ClearDialog() => dialogText = "";

    void SetPhase(string text)
    {
        phaseText = text;
        Debug.Log($"[Demo] === {text} ===");
    }
}