using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// UserEntity — 最終版（無 SitDown / StandUp 動畫）
///
/// 行為鏈路：
///   Drink  : Walk → 到位 → SmoothRotate → Drink(loop) ← StaticCameraManager 截圖
///   Sit    : Walk → 接近點 → SmoothRotate → agent.enabled=false → teleport 到椅面 → SittingIdle(loop)
///   Typing : Walk → 接近點 → SmoothRotate → agent.enabled=false → teleport → Typing(loop)
///   Reading: Walk → 接近點 → SmoothRotate → agent.enabled=false → teleport → Reading(loop)
///
/// ReturnToIdle（坐姿）:
///   PlayAnim(Idle) → Y=0 → agent.enabled=true → Warp → Walk → IdleSpot
///
/// 為什麼坐下用 agent.enabled=false：
///   agent.isStopped=true 仍會每幀把 Y 貼回地板
///   enabled=false 才能讓 transform.Y 停在椅面高度
///   站起來前必須先 Y=0，再 enabled=true + Warp()，否則位置跳動
///
/// Animator Controller 設定（必做）：
///   States: Idle(Default★) Walk Drink SittingIdle Typing Reading Nodding
///   全部無 Transition 箭頭，無 Parameters
///   Apply Root Motion → OFF（一定要關）
///   每個 Clip → Bake XZ ✅ → Y Based Upon Feet → Loop clip 才勾
///
/// SittingSpot 設定：
///   Position.Y = 椅面高度（沙發約 0.40，辦公椅約 0.50）
///   Position   = 椅墊中心（往椅背退 0.05~0.15m 避免手插桌）
///   Rotation   = 坐下後面對的方向
///   Scene 視窗藍箭頭 = 角色坐下後面對的方向
///
/// 手持物件 offset 調整（Play Mode）：
///   讓角色進入 Drink 狀態 → 在 Scene 拖動杯子到正確位置
///   → 按 C 鍵輸出 offset 數值 → 停止 Play → 填入 Inspector
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
public class UserEntity : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector 欄位
    // ══════════════════════════════════════════════════════

    [Header("使用者 ID")]
    public string userID = "User_Mom";

    [Header("定點空物件（Z 軸朝向家具正面）")]
    [Tooltip("飲水機前 0.5m，Z 軸朝飲水機")]
    public Transform drinkSpot;

    [Tooltip("沙發椅墊中心\nPosition.Y = 椅面高度（沙發約 0.40）\nZ 軸朝角色坐下後面對的方向")]
    public Transform sittingSpot;

    [Tooltip("書桌椅墊中心（Dad 專用）\nPosition.Y = 椅面高度（辦公椅約 0.50）\nZ 軸朝螢幕方向")]
    public Transform typingSpot;

    [Tooltip("沙發或床邊（Mom 專用）\nPosition.Y = 椅面高度")]
    public Transform readingSpot;

    [Tooltip("客廳入口待機點，Y = 0（地板高度）")]
    public Transform idleSpot;

    [Header("NavMesh")]
    [Tooltip("距目標幾公尺算到達")]
    public float arrivalThreshold = 0.4f;

    [Header("轉向速度（6~10，太快有跳轉感）")]
    public float rotationSpeed = 8f;

    [Header("Nodding 動畫時長（秒）")]
    public float noddingDuration = 1.5f;

    // ── 物件跟手 ─────────────────────────────────────────
    [Header("物件跟手")]
    [Tooltip("杯子或其他手持物件，留空則不啟用跟手")]
    public GameObject heldItem;

    [Tooltip("Mixamo 骨架通常是 mixamorig:RightHand\n展開角色 Hierarchy 確認完整名稱")]
    public string rightHandBoneName = "mixamorig:RightHand";

    [Tooltip("物件相對右手骨架的位置偏移\nPlay Mode 按 C 鍵輸出正確值")]
    public Vector3 itemPositionOffset = Vector3.zero;

    [Tooltip("物件相對右手骨架的旋轉偏移")]
    public Vector3 itemRotationOffset = Vector3.zero;

    [Tooltip("哪些 Activity 顯示物件（逗號分隔）\n例如：Drink\n空白 = 永遠顯示")]
    public string itemVisibleDuring = "Drink";

    // ── Animator State 名稱 ──────────────────────────────
    [Header("Animator State 名稱（必須與 Controller 完全一致）")]
    public string stateIdle = "Idle";
    public string stateWalk = "Walk";
    public string stateDrink = "Drink";
    public string stateSittingIdle = "SittingIdle";
    public string stateTyping = "Typing";
    public string stateReading = "Reading";
    public string stateNodding = "Nodding";

    // ══════════════════════════════════════════════════════
    // 公開屬性（StaticCameraManager / ExperimentRunner 讀取）
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// SmartScanRoutine 讀取：
    ///   "Idle" / "Walking" → 不截圖
    ///   "Drink" / "SittingIdle" / "Typing" / "Reading" → 觸發截圖
    /// </summary>
    public string currentActivity { get; private set; } = "Idle";

    public bool IsBusy { get; private set; } = false;

    /// <summary>VirtualCameraBrain 對準的胸口位置（約 1.2m）</summary>
    public Vector3 GetAimPosition() => transform.position + Vector3.up * 1.2f;

    // ══════════════════════════════════════════════════════
    // 私有成員
    // ══════════════════════════════════════════════════════

    Animator anim;
    NavMeshAgent agent;
    Transform rightHandBone;
    string[] showDuring;

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        anim = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = true;
        agent.isStopped = true;

        rightHandBone = FindBoneRecursive(transform, rightHandBoneName);
        if (rightHandBone == null && heldItem != null)
            Debug.LogWarning($"[{userID}] 找不到骨架 '{rightHandBoneName}'\n" +
                             "展開角色 Hierarchy → 找 RightHand → 複製完整名稱填入 Inspector");

        showDuring = string.IsNullOrWhiteSpace(itemVisibleDuring)
            ? new string[0]
            : System.Array.ConvertAll(itemVisibleDuring.Split(','), s => s.Trim());

        SetItemVisible(false);
        PlayAnim(stateIdle);
    }

    // ──────────────────────────────────────────────────────
    // LateUpdate：物件貼合手骨架
    // 必須 LateUpdate：Update() 時 Animator 還沒算完骨架位置
    // ──────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (heldItem == null || rightHandBone == null || !heldItem.activeSelf) return;
        heldItem.transform.position = rightHandBone.TransformPoint(itemPositionOffset);
        heldItem.transform.rotation = rightHandBone.rotation * Quaternion.Euler(itemRotationOffset);
    }

    // Play Mode 按 C 鍵輸出 offset 值，調好後刪除此區塊
#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C) && heldItem != null && rightHandBone != null)
        {
            Vector3 localPos = rightHandBone.InverseTransformPoint(heldItem.transform.position);
            Vector3 localRot = (Quaternion.Inverse(rightHandBone.rotation) * heldItem.transform.rotation).eulerAngles;
            Debug.Log($"[{userID}] itemPositionOffset = {localPos}");
            Debug.Log($"[{userID}] itemRotationOffset = {localRot}");
        }
    }
#endif

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 走到定點 → 執行行為（loop 狀態）
    /// activity: "drink" | "sit" | "typing" | "reading" | "idle"
    /// </summary>
    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        switch (activity.ToLower())
        {
            case "drink": yield return StartCoroutine(DoDrink()); break;
            case "sit": yield return StartCoroutine(DoSit()); break;
            case "typing": yield return StartCoroutine(DoTyping()); break;
            case "reading": yield return StartCoroutine(DoReading()); break;
            case "idle": yield return StartCoroutine(DoReturnToIdle()); break;
            default:
                Debug.LogWarning($"[{userID}] 未知 activity: {activity}");
                break;
        }

        IsBusy = false;
    }

    /// <summary>結束行為，走回 IdleSpot</summary>
    public IEnumerator ReturnToIdle()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToIdle());
        IsBusy = false;
    }

    /// <summary>點頭接受提案（DemoRunner 呼叫）</summary>
    public IEnumerator Nod()
    {
        PlayAnim(stateNodding);
        yield return new WaitForSeconds(noddingDuration);

        string resume = currentActivity switch
        {
            "Drink" => stateDrink,
            "SittingIdle" => stateSittingIdle,
            "Typing" => stateTyping,
            "Reading" => stateReading,
            _ => stateIdle
        };
        PlayAnim(resume);
    }

    /// <summary>StaticCameraManager 直接設定 Animator State</summary>
    public void SetAnim(string stateName) => PlayAnim(stateName);

    // ══════════════════════════════════════════════════════
    // 行為鏈路（私有）
    // ══════════════════════════════════════════════════════

    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Debug.LogWarning($"[{userID}] drinkSpot 未設定"); yield break; }

        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(drinkSpot.position));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));

        SetActivity("Drink");
        PlayAnim(stateDrink);
        // StaticCameraManager 偵測到 "Drink" 後會自動觸發截圖
        // ExperimentRunner 等 waitAfterCapture 秒後才呼叫 ReturnToIdle
        // 此處不自動返回，讓外部控制停留時間
    }

    IEnumerator DoSit()
    {
        if (sittingSpot == null) { Debug.LogWarning($"[{userID}] sittingSpot 未設定"); yield break; }

        SetActivity("Walking");
        // NavMesh 只在地板（Y=0），接近點強制 Y=0
        yield return StartCoroutine(NavWalkTo(GetApproachPos(sittingSpot)));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));

        // agent.enabled=false → Y 不再被 NavMesh 強制貼地
        agent.enabled = false;
        transform.position = sittingSpot.position;   // teleport 到椅面高度
        transform.rotation = sittingSpot.rotation;

        SetActivity("SittingIdle");
        PlayAnim(stateSittingIdle);
    }

    IEnumerator DoTyping()
    {
        if (typingSpot == null) { Debug.LogWarning($"[{userID}] typingSpot 未設定（Dad 才需要）"); yield break; }

        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(GetApproachPos(typingSpot)));
        yield return StartCoroutine(SmoothRotateTo(typingSpot.forward));

        agent.enabled = false;
        transform.position = typingSpot.position;
        transform.rotation = typingSpot.rotation;

        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoReading()
    {
        if (readingSpot == null) { Debug.LogWarning($"[{userID}] readingSpot 未設定（Mom 才需要）"); yield break; }

        SetActivity("Walking");
        yield return StartCoroutine(NavWalkTo(GetApproachPos(readingSpot)));
        yield return StartCoroutine(SmoothRotateTo(readingSpot.forward));

        agent.enabled = false;
        transform.position = readingSpot.position;
        transform.rotation = readingSpot.rotation;

        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    // 坐姿 → 站立正確順序：
    //   1. PlayAnim(Idle) → 切站姿動畫
    //   2. Y = 0          → 回地板（必須在 agent.enabled=true 之前）
    //   3. agent.enabled = true
    //   4. agent.Warp()   → 告知 NavMesh 新位置（不 Warp 會卡死）
    //   5. Walk → IdleSpot
    IEnumerator DoReturnToIdle()
    {
        SetItemVisible(false);

        bool wasSitting = currentActivity == "SittingIdle" ||
                          currentActivity == "Typing" ||
                          currentActivity == "Reading";

        if (wasSitting)
        {
            PlayAnim(stateIdle);
            SetActivity("Idle");

            Vector3 standPos = transform.position;
            standPos.y = 0f;
            transform.position = standPos;

            agent.enabled = true;
            agent.Warp(standPos);
        }

        if (idleSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(NavWalkTo(idleSpot.position));
        }

        SetActivity("Idle");
        PlayAnim(stateIdle);
    }

    // ══════════════════════════════════════════════════════
    // NavMesh 移動
    // ══════════════════════════════════════════════════════

    IEnumerator NavWalkTo(Vector3 targetPos)
    {
        // 若上次坐下關閉了 agent，先重啟
        if (!agent.enabled)
        {
            Vector3 p = transform.position;
            p.y = 0f;
            transform.position = p;
            agent.enabled = true;
            agent.Warp(p);
        }

        agent.updateRotation = true;
        agent.isStopped = false;
        agent.SetDestination(targetPos);
        PlayAnim(stateWalk);

        // 等路徑計算完成
        yield return null;
        yield return new WaitUntil(() => !agent.pathPending);

        // 等到達（remainingDistance 有時初始為 0，多等一幀）
        yield return null;
        while (!agent.pathPending && agent.remainingDistance > arrivalThreshold)
            yield return null;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.updateRotation = false;   // 交還給 SmoothRotateTo
    }

    IEnumerator SmoothRotateTo(Vector3 targetForward)
    {
        targetForward.y = 0f;
        if (targetForward.sqrMagnitude < 0.001f) yield break;

        Quaternion targetRot = Quaternion.LookRotation(targetForward.normalized);
        while (Quaternion.Angle(transform.rotation, targetRot) > 0.5f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot,
                Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.rotation = targetRot;
    }

    // ══════════════════════════════════════════════════════
    // 輔助方法
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// NavMesh 接近點：spot 正前方 0.3m，Y 強制為 0
    /// NavMesh 烘焙只在地板，傳入椅面 Y 會找不到路徑
    /// </summary>
    Vector3 GetApproachPos(Transform spot)
    {
        Vector3 pos = spot.position - spot.forward * 0.3f;
        pos.y = 0f;
        return pos;
    }

    void SetActivity(string activity)
    {
        currentActivity = activity;
        if (showDuring.Length == 0) return;
        SetItemVisible(System.Array.IndexOf(showDuring, activity) >= 0);
    }

    void PlayAnim(string stateName)
    {
        anim.Play(stateName, 0, 0f);
    }

    void SetItemVisible(bool visible)
    {
        if (heldItem != null) heldItem.SetActive(visible);
    }

    /// <summary>
    /// 遞迴搜尋骨架，支援完整名稱和後綴匹配
    /// "RightHand" 可匹配 "mixamorig:RightHand"
    /// </summary>
    Transform FindBoneRecursive(Transform parent, string boneName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == boneName || child.name.EndsWith(boneName))
                return child;
            var found = FindBoneRecursive(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════
    // Gizmo：Scene 視窗顯示定點位置與朝向
    // 球體 = 定點位置，箭頭 = 角色坐下後面對的方向
    // ══════════════════════════════════════════════════════

    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot, Color.yellow, "Drink");
        DrawSpot(sittingSpot, Color.green, "Sit");
        DrawSpot(typingSpot, Color.cyan, "Typing");
        DrawSpot(readingSpot, Color.magenta, "Reading");

        if (idleSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(idleSpot.position, 0.15f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(idleSpot.position + Vector3.up * 0.3f, "Idle");
#endif
        }
    }

    void DrawSpot(Transform spot, Color color, string label)
    {
        if (spot == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(spot.position, 0.2f);
        Gizmos.DrawRay(spot.position, spot.forward * 0.8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(spot.position + Vector3.up * 0.35f, label);
#endif
    }
}