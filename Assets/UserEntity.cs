using System.Collections;
using UnityEngine;

/// <summary>
/// UserEntity — S1~S8 設計（無 SitDown/StandUp，純 Transform 移動）
///
/// Mom（客廳）:
///   S1 Drink   : Walk → Kitchen DrinkSpot → Drink(loop)
///   S2 Sit     : Walk → 沙發 SittingSpot → teleport → SittingIdle(loop)
///   S3 Reading : Walk → 沙發 SittingSpot（同位置）→ teleport → Reading(loop)
///   S4 Idle    : 原地 Idle(loop)，baseline，不截圖
///
/// Dad（書房）:
///   S5 Drink   : Walk → 客廳茶几 DrinkSpot → Drink(loop)
///   S6 Sit     : Walk → 書桌椅 SittingSpot → teleport → SittingIdle(loop)
///   S7 Typing  : Walk → 書桌椅 SittingSpot（同位置）→ teleport → Typing(loop)
///   S8 Idle    : 原地 Idle(loop)，baseline，不截圖
///
/// 坐下流程（無 SitDown 動畫）:
///   WalkTo 接近點（椅前 0.3m，Y=0）→ SmoothRotate → teleport 到椅面 → 播 loop
///
/// ReturnToIdle:
///   坐姿 → PlayAnim(Idle) → Y=0 → Walk → IdleSpot
///   站姿 → Walk → IdleSpot
///
/// Waypoints（選填）:
///   Mom：Kitchen + Living Room 完全開放，所有路線直線，全部留空
///   Dad Drink：Dad Room → 門口（下方）→ Living Room → 茶几，需 2 個路點
///     drinkWaypoints[0] = WP1_DoorDadSide   （門框 Dad Room 側，Y=0）
///     drinkWaypoints[1] = WP2_DoorLivingSide （門框 Living Room 側，Y=0）
///     idleWaypoints[0]  = WP2_DoorLivingSide （回程，反向）
///     idleWaypoints[1]  = WP1_DoorDadSide    （回程，反向）
///   Dad Sit/Typing：同房間直線，留空
/// </summary>
[RequireComponent(typeof(Animator))]
public class UserEntity : MonoBehaviour
{
    [Header("使用者 ID")]
    public string userID = "User_Mom";

    [Header("定點（Z 軸朝向家具正面）")]
    [Tooltip("Mom: Kitchen 桌前，Y=0\nDad: 客廳茶几前，Y=0")]
    public Transform drinkSpot;

    [Tooltip("Mom: 客廳沙發椅墊，Y=椅面高度(~0.40)\nDad: 書桌椅椅墊，Y=椅面高度(~0.50)\nSit 和 Reading / Typing 共用同一個 Spot")]
    public Transform sittingSpot;

    [Tooltip("Mom: 客廳入口，Y=0\nDad: 書房入口，Y=0")]
    public Transform idleSpot;

    [Header("路點（選填，有牆才填，Y=0）")]
    [Tooltip("前往 drinkSpot 途中的路點\nMom: 走廊轉角點\nDad: 書房門口 + 走廊點")]
    public Transform[] drinkWaypoints;

    [Tooltip("前往 sittingSpot 途中的路點（通常不需要）")]
    public Transform[] sittingWaypoints;

    [Tooltip("回到 idleSpot 途中的路點（去程反向）\nMom: 留空\nDad: [0]=WP2_DoorLivingSide  [1]=WP1_DoorDadSide")]
    public Transform[] idleWaypoints;

    [Header("移動設定")]
    public float walkSpeed = 1.4f;
    public float arrivalThreshold = 0.08f;
    public float rotationSpeed = 8f;

    [Header("Nodding 時長（秒）")]
    public float noddingDuration = 1.5f;

    [Header("手持物件（選填）")]
    public GameObject heldItem;
    public string rightHandBoneName = "mixamorig:RightHand";
    public Vector3 itemPositionOffset = Vector3.zero;
    public Vector3 itemRotationOffset = Vector3.zero;
    [Tooltip("哪些 Activity 顯示物件，逗號分隔，空白=永遠顯示")]
    public string itemVisibleDuring = "Drink";

    [Header("Animator State 名稱（與 Controller 完全一致）")]
    public string stateIdle = "Idle";
    public string stateWalk = "Walk";
    public string stateDrink = "Drink";
    public string stateSittingIdle = "SittingIdle";
    public string stateReading = "Reading";
    public string stateTyping = "Typing";
    public string stateNodding = "Nodding";

    // ── 公開屬性 ──────────────────────────────────────────
    public string currentActivity { get; private set; } = "Idle";
    public bool IsBusy { get; private set; } = false;
    public Vector3 GetAimPosition() => transform.position + Vector3.up * 1.2f;

    // ── 私有 ──────────────────────────────────────────────
    Animator anim;
    Transform rightHandBone;
    string[] showDuring;
    bool isSitting = false;

    // ══════════════════════════════════════════════════════
    void Start()
    {
        anim = GetComponent<Animator>();
        rightHandBone = FindBoneRecursive(transform, rightHandBoneName);
        if (rightHandBone == null && heldItem != null)
            Debug.LogWarning($"[{userID}] 找不到骨架 '{rightHandBoneName}'");

        showDuring = string.IsNullOrWhiteSpace(itemVisibleDuring)
            ? new string[0]
            : System.Array.ConvertAll(itemVisibleDuring.Split(','), s => s.Trim());

        SetItemVisible(false);
        PlayAnim(stateIdle);
    }

    void LateUpdate()
    {
        if (heldItem == null || rightHandBone == null || !heldItem.activeSelf) return;
        heldItem.transform.position = rightHandBone.TransformPoint(itemPositionOffset);
        heldItem.transform.rotation = rightHandBone.rotation * Quaternion.Euler(itemRotationOffset);
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C) && heldItem != null && rightHandBone != null)
        {
            Debug.Log($"[{userID}] posOffset = " +
                rightHandBone.InverseTransformPoint(heldItem.transform.position));
            Debug.Log($"[{userID}] rotOffset = " +
                (Quaternion.Inverse(rightHandBone.rotation) *
                 heldItem.transform.rotation).eulerAngles);
        }
    }
#endif

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// activity: "drink"|"sit"|"reading"|"typing"|"idle"
    /// ExperimentRunner / DemoRunner 呼叫
    /// </summary>
    public IEnumerator SwitchActivity(string activity)
    {
        if (IsBusy) yield break;
        IsBusy = true;

        switch (activity.ToLower())
        {
            case "drink": yield return StartCoroutine(DoDrink()); break;
            case "sit": yield return StartCoroutine(DoSit()); break;
            case "reading": yield return StartCoroutine(DoReading()); break;
            case "typing": yield return StartCoroutine(DoTyping()); break;
            case "idle": SetActivity("Idle"); PlayAnim(stateIdle); break;
            default: Debug.LogWarning($"[{userID}] 未知 activity: {activity}"); break;
        }

        IsBusy = false;
    }

    /// <summary>走回 IdleSpot（ExperimentRunner 每次 episode 後呼叫）</summary>
    public IEnumerator ReturnToIdle()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToIdle());
        IsBusy = false;
    }

    /// <summary>點頭（DemoRunner Phase2 使用）</summary>
    public IEnumerator Nod()
    {
        PlayAnim(stateNodding);
        yield return new WaitForSeconds(noddingDuration);
        // 恢復目前 loop 動畫
        PlayAnim(currentActivity switch
        {
            "Drink" => stateDrink,
            "SittingIdle" => stateSittingIdle,
            "Reading" => stateReading,
            "Typing" => stateTyping,
            _ => stateIdle
        });
    }

    public void SetAnim(string s) => PlayAnim(s);

    // ══════════════════════════════════════════════════════
    // 行為鏈路
    // ══════════════════════════════════════════════════════

    // S1/S5：Drink
    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(drinkSpot.position, drinkWaypoints));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drink");
        PlayAnim(stateDrink);
        // StaticCameraManager 自動截圖；ExperimentRunner 等 waitAfterCapture 後呼叫 ReturnToIdle
    }

    // S2/S6：Sit（teleport 到椅面，無 SitDown 動畫）
    IEnumerator DoSit()
    {
        if (sittingSpot == null) { Warn("sittingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        transform.position = sittingSpot.position;
        transform.rotation = sittingSpot.rotation;
        isSitting = true;
        SetActivity("SittingIdle");
        PlayAnim(stateSittingIdle);
    }

    // S3：Reading（同一個 sittingSpot，不同動畫）
    IEnumerator DoReading()
    {
        if (sittingSpot == null) { Warn("sittingSpot（Reading 用）"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        transform.position = sittingSpot.position;
        transform.rotation = sittingSpot.rotation;
        isSitting = true;
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    // S7：Typing（同一個 sittingSpot，不同動畫）
    IEnumerator DoTyping()
    {
        if (sittingSpot == null) { Warn("sittingSpot（Typing 用）"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        transform.position = sittingSpot.position;
        transform.rotation = sittingSpot.rotation;
        isSitting = true;
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    // ReturnToIdle
    IEnumerator DoReturnToIdle()
    {
        SetItemVisible(false);

        if (isSitting)
        {
            PlayAnim(stateIdle);
            SetActivity("Idle");
            Vector3 p = transform.position;
            p.y = 0f;
            transform.position = p;
            isSitting = false;
        }

        if (idleSpot != null)
        {
            SetActivity("Walking");
            yield return StartCoroutine(WalkVia(idleSpot.position, idleWaypoints));
        }

        SetActivity("Idle");
        PlayAnim(stateIdle);
    }

    // ══════════════════════════════════════════════════════
    // 移動
    // ══════════════════════════════════════════════════════

    IEnumerator WalkVia(Vector3 target, Transform[] wps)
    {
        if (wps != null)
            foreach (var wp in wps)
                if (wp != null)
                    yield return StartCoroutine(WalkTo(wp.position));
        yield return StartCoroutine(WalkTo(target));
    }

    IEnumerator WalkTo(Vector3 target)
    {
        target.y = 0f;
        transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
        PlayAnim(stateWalk);

        while (true)
        {
            Vector3 cur = new Vector3(transform.position.x, 0f, transform.position.z);
            if (Vector3.Distance(cur, target) <= arrivalThreshold) break;
            transform.position = Vector3.MoveTowards(cur, target, walkSpeed * Time.deltaTime);
            Vector3 dir = (target - cur).normalized;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.position = new Vector3(target.x, 0f, target.z);
    }

    IEnumerator SmoothRotateTo(Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) yield break;
        Quaternion tgt = Quaternion.LookRotation(fwd.normalized);
        while (Quaternion.Angle(transform.rotation, tgt) > 0.5f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, tgt,
                Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.rotation = tgt;
    }

    // ══════════════════════════════════════════════════════
    // 輔助
    // ══════════════════════════════════════════════════════

    Vector3 GetApproachPos(Transform spot)
    {
        Vector3 p = spot.position - spot.forward * 0.3f;
        p.y = 0f;
        return p;
    }

    void SetActivity(string a)
    {
        currentActivity = a;
        if (showDuring.Length == 0) return;
        SetItemVisible(System.Array.IndexOf(showDuring, a) >= 0);
    }

    void PlayAnim(string s) => anim.Play(s, 0, 0f);
    void SetItemVisible(bool v) { if (heldItem != null) heldItem.SetActive(v); }
    void Warn(string s) => Debug.LogWarning($"[{userID}] {s} 未設定");

    Transform FindBoneRecursive(Transform parent, string name)
    {
        foreach (Transform c in parent)
        {
            if (c.name == name || c.name.EndsWith(name)) return c;
            var f = FindBoneRecursive(c, name);
            if (f != null) return f;
        }
        return null;
    }

    void OnDrawGizmos()
    {
        DrawSpot(drinkSpot, Color.yellow, "Drink");
        DrawSpot(sittingSpot, Color.green, "Sit/Read/Type");
        if (idleSpot != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(idleSpot.position, 0.15f);
        }
        DrawWaypointPath(drinkWaypoints, drinkSpot, Color.yellow);
        DrawWaypointPath(sittingWaypoints, sittingSpot, Color.green);
    }

    void DrawSpot(Transform spot, Color c, string label)
    {
        if (spot == null) return;
        Gizmos.color = c;
        Gizmos.DrawWireSphere(spot.position, 0.2f);
        Gizmos.DrawRay(spot.position, spot.forward * 0.8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(spot.position + Vector3.up * 0.35f, label);
#endif
    }

    void DrawWaypointPath(Transform[] wps, Transform final, Color c)
    {
        if (wps == null || wps.Length == 0 || idleSpot == null) return;
        Gizmos.color = new Color(c.r, c.g, c.b, 0.4f);
        Vector3 prev = idleSpot.position;
        foreach (var wp in wps) { if (wp != null) { Gizmos.DrawLine(prev, wp.position); prev = wp.position; } }
        if (final != null) Gizmos.DrawLine(prev, final.position);
    }
}