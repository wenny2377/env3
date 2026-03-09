using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ExperimentRunner.cs
/// 掛在 Hierarchy → SystemManagers 空物件上。
/// Inspector 選擇實驗後按 Play，自動執行所有測試，不需要手動按鍵。
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    public enum ExperimentMode
    {
        None,
        Exp1_VLM,           // 實驗一：VLM 行為辨識（150 筆）
        Exp2_Binding,       // 實驗二：家具綁定消融（同 Exp1 流程）
        Exp3A_WeightAccum,  // 實驗三A：習慣 weight 累積（30 次）
        Exp3B_Sequence,     // 實驗三B：行為序列 5 天
        Exp5_EndToEnd,      // 實驗五：端到端情境
    }

    [Header("── 實驗設定 ──")]
    public ExperimentMode experimentMode = ExperimentMode.Exp1_VLM;

    [Header("── 用戶 Entity（從 Hierarchy 拖入）──")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("── 時間設定 ──")]
    [Tooltip("每次 SwitchActivity 後等待相機拍照並送出 /predict 的時間（秒）\nCPU 模式建議 35s")]
    public float waitAfterAction = 35f;

    [Tooltip("實驗三B 每個行為維持的秒數")]
    public float sequenceStepDuration = 35f;

    [Tooltip("每組測試之間的間隔（秒）")]
    public float betweenGroupDelay = 3f;

    [Header("── Flask 位址 ──")]
    public string flaskUrl = "http://127.0.0.1:5000";

    // ── 內部狀態 ──
    private int totalSteps = 0;
    private int currentStep = 0;
    private bool isRunning = false;
    private string currentStatus = "";

    private struct TestCase
    {
        public UserEntity user;
        public string activity;
        public int repeatCount;
    }

    private readonly string[] momDaySequence =
        { "sleeping","typing","drinking","sitting","typing","sleeping" };

    private readonly string[] dadDaySequence =
        { "sleeping","typing","swinging","drinking","sleeping" };

    // ─────────────────────────────────────────────
    void Start()
    {
        if (experimentMode == ExperimentMode.None) return;
        StartCoroutine(RunExperiment());
    }

    IEnumerator RunExperiment()
    {
        isRunning = true;
        Debug.Log($"[ExperimentRunner] ▶ 開始：{experimentMode}");
        yield return new WaitForSeconds(2f);   // 等場景載入

        switch (experimentMode)
        {
            case ExperimentMode.Exp1_VLM:
            case ExperimentMode.Exp2_Binding:
                yield return StartCoroutine(RunExp1());
                break;
            case ExperimentMode.Exp3A_WeightAccum:
                yield return StartCoroutine(RunExp3A());
                break;
            case ExperimentMode.Exp3B_Sequence:
                yield return StartCoroutine(RunExp3B());
                break;
            case ExperimentMode.Exp5_EndToEnd:
                yield return StartCoroutine(RunExp5());
                break;
        }

        isRunning = false;
        currentStatus = "✅ 完成！";
        Debug.Log($"[ExperimentRunner] ✅ 實驗完成：{experimentMode}");
    }

    // ─────────────────────────────────────────────
    // 實驗一 / 二：VLM 行為辨識
    // ─────────────────────────────────────────────
    IEnumerator RunExp1()
    {
        var cases = BuildExp1Cases();
        totalSteps = 0;
        foreach (var tc in cases) totalSteps += tc.repeatCount;
        currentStep = 0;

        foreach (var tc in cases)
        {
            for (int i = 0; i < tc.repeatCount; i++)
            {
                currentStep++;
                currentStatus = $"{tc.user.userID} → {tc.activity} ({i+1}/{tc.repeatCount})";
                Debug.Log($"[Exp1] {currentStep}/{totalSteps} | {currentStatus}");

                tc.user.SwitchActivity(tc.activity);
                yield return new WaitForSeconds(waitAfterAction);
                tc.user.HideAllModels();
                yield return new WaitForSeconds(1.5f);
            }

            Debug.Log($"[Exp1] 群組完成：{tc.user.userID} {tc.activity}");
            yield return new WaitForSeconds(betweenGroupDelay);
        }
    }

    List<TestCase> BuildExp1Cases()
    {
        var list = new List<TestCase>();

        if (userMom != null)
        {
            // 對應 Unity 子模型：mom_drinking, mom_sitting, mom_sleeping, mom_typing
            list.Add(new TestCase { user = userMom, activity = "drinking", repeatCount = 10 });
            list.Add(new TestCase { user = userMom, activity = "sitting",  repeatCount = 10 });
            list.Add(new TestCase { user = userMom, activity = "sleeping", repeatCount = 10 });
            list.Add(new TestCase { user = userMom, activity = "typing",   repeatCount = 10 });
        }

        if (userDad != null)
        {
            // 對應 Unity 子模型：dad_drinking, dad_swinging, dad_sleeping, dad_typing
            list.Add(new TestCase { user = userDad, activity = "drinking", repeatCount = 10 });
            list.Add(new TestCase { user = userDad, activity = "swinging", repeatCount = 10 });
            list.Add(new TestCase { user = userDad, activity = "sleeping", repeatCount = 10 });
            list.Add(new TestCase { user = userDad, activity = "typing",   repeatCount = 10 });
        }

        return list;
    }

    // ─────────────────────────────────────────────
    // 實驗三A：習慣 Weight 累積（User_Mom drinking × 30）
    // ─────────────────────────────────────────────
    IEnumerator RunExp3A()
    {
        if (userMom == null) { Debug.LogError("[Exp3A] userMom 未設定！"); yield break; }

        int total = 30, checkEvery = 5;
        totalSteps = total; currentStep = 0;

        for (int i = 1; i <= total; i++)
        {
            currentStep = i;
            currentStatus = $"User_Mom drinking ({i}/{total})";
            Debug.Log($"[Exp3A] 第 {i}/{total} 次");

            userMom.SwitchActivity("drinking");
            yield return new WaitForSeconds(waitAfterAction);
            userMom.HideAllModels();
            yield return new WaitForSeconds(1.5f);

            if (i % checkEvery == 0)
            {
                Debug.Log($"[Exp3A] ── Checkpoint {i} 次 ──");
                yield return StartCoroutine(
                    NotifyFlask($"/exp_checkpoint?experiment=exp3a&step={i}&user=User_Mom&action=drinking")
                );
                yield return new WaitForSeconds(2f);
            }
        }
    }

    // ─────────────────────────────────────────────
    // 實驗三B：行為序列（5 天）
    // ─────────────────────────────────────────────
    IEnumerator RunExp3B()
    {
        int totalDays = 5;
        totalSteps = totalDays * (momDaySequence.Length + dadDaySequence.Length);
        currentStep = 0;

        for (int day = 1; day <= totalDays; day++)
        {
            Debug.Log($"[Exp3B] ══ Day {day}/{totalDays} ══");

            if (userMom != null)
                yield return StartCoroutine(RunSequence(userMom, momDaySequence, day, "Mom"));

            yield return new WaitForSeconds(3f);

            if (userDad != null)
                yield return StartCoroutine(RunSequence(userDad, dadDaySequence, day, "Dad"));

            yield return StartCoroutine(
                NotifyFlask($"/exp_checkpoint?experiment=exp3b&day={day}")
            );

            yield return new WaitForSeconds(5f);
        }
    }

    IEnumerator RunSequence(UserEntity user, string[] sequence, int day, string label)
    {
        for (int s = 0; s < sequence.Length; s++)
        {
            currentStep++;
            currentStatus = $"{label} Day{day} {sequence[s]} ({s+1}/{sequence.Length})";
            Debug.Log($"[Exp3B] {currentStatus}");

            user.SwitchActivity(sequence[s]);
            yield return new WaitForSeconds(sequenceStepDuration);
            user.HideAllModels();
            yield return new WaitForSeconds(1.5f);
        }
    }

    // ─────────────────────────────────────────────
    // 實驗五：端到端情境
    // ─────────────────────────────────────────────
    IEnumerator RunExp5()
    {
        totalSteps = 25; currentStep = 0;

        // 情境 A
        Debug.Log("[Exp5] ── 情境 A：User_Mom drinking × 5 ──");
        if (userMom != null)
        {
            for (int i = 0; i < 5; i++)
            {
                currentStep++;
                currentStatus = $"情境A drinking ({i+1}/5)";
                userMom.SwitchActivity("drinking");
                yield return new WaitForSeconds(waitAfterAction);
                userMom.HideAllModels();
                yield return new WaitForSeconds(2f);
            }
        }
        yield return new WaitForSeconds(3f);

        // 情境 B：用各自有的行為來建立個人化差異
        Debug.Log("[Exp5] ── 情境 B：個人化習慣建立 ──");
        for (int i = 0; i < 10; i++)
        {
            if (userMom != null)
            {
                currentStep++;
                currentStatus = $"情境B Mom drinking ({i+1}/10)";
                userMom.SwitchActivity("drinking");
                yield return new WaitForSeconds(waitAfterAction);
                userMom.HideAllModels();
                yield return new WaitForSeconds(1.5f);
            }
            if (userDad != null)
            {
                currentStep++;
                currentStatus = $"情境B Dad swinging ({i+1}/10)";
                userDad.SwitchActivity("swinging");
                yield return new WaitForSeconds(waitAfterAction);
                userDad.HideAllModels();
                yield return new WaitForSeconds(1.5f);
            }
        }
        yield return new WaitForSeconds(3f);

        // 情境 C
        Debug.Log("[Exp5] ── 情境 C：User_Mom 完整一天 ──");
        if (userMom != null)
        {
            // 只用 Mom 有的子模型
            string[] fullDay = { "sleeping", "typing", "drinking", "sitting", "sleeping" };
            yield return StartCoroutine(RunSequence(userMom, fullDay, 1, "Mom"));
        }

        Debug.Log("[Exp5] ✅ 資料收集完畢！請開啟 interact_client.py 進行對話測試");
    }

    // ─────────────────────────────────────────────
    // 工具：通知 Flask checkpoint
    // ─────────────────────────────────────────────
    IEnumerator NotifyFlask(string path)
    {
        using (var req = UnityWebRequest.Get(flaskUrl + path))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
                Debug.Log($"[ExperimentRunner] ✓ {path}");
            else
                Debug.LogWarning($"[ExperimentRunner] Checkpoint 失敗：{req.error}（繼續執行）");
        }
    }

    // ─────────────────────────────────────────────
    // 畫面顯示進度
    // ─────────────────────────────────────────────
    void OnGUI()
    {
        if (!isRunning && currentStatus != "✅ 完成！") return;
        GUI.color = isRunning ? Color.cyan : Color.green;
        GUI.Box(new Rect(10, 10, 400, 52), "");
        GUI.color = Color.white;
        GUI.Label(new Rect(18, 18, 380, 18), $"[ExperimentRunner] {experimentMode}");
        GUI.Label(new Rect(18, 36, 380, 18),
            totalSteps > 0 ? $"步驟 {currentStep}/{totalSteps}：{currentStatus}" : currentStatus);
    }
}