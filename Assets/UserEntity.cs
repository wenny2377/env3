using System.Collections;
using UnityEngine;

/// <summary>
/// UserEntity — 物件直接放在骨架 Hierarchy 裡，不需要 SetParent 或 offset 校正
///
/// 設定方式：
///   在 Hierarchy 裡把 Cup / Book 直接拖進對應骨架節點（RightHand / LeftHand）
///   調整好 localPosition / localRotation 後不會再跑位
///
/// behaviorItems 陣列（只需填 activity 和 item，不需要 boneName / offset）：
///   activity = "Drink"   → item = Cup GameObject
///   activity = "Reading" → item = Book GameObject
///
/// 顯示邏輯：
///   SetActivity("Drink")      → 顯示 Cup，隱藏 Book
///   SetActivity("Reading")    → 隱藏 Cup，顯示 Book
///   SetActivity("Walking")    → 全部隱藏
///   SetActivity("Idle")       → 全部隱藏
///   SetActivity("SittingIdle")→ 全部隱藏
///   SetActivity("Typing")     → 全部隱藏
/// </summary>

[System.Serializable]
public class BehaviorItem
{
    [Tooltip("對應的行為名稱，與 SwitchActivity 參數一致（大小寫忽略）\n例如：Drink / Reading / Typing")]
    public string activity = "Drink";

    [Tooltip("手持物件 GameObject（需已放在骨架子節點裡）")]
    public GameObject item;
}

[RequireComponent(typeof(Animator))]
public class UserEntity : MonoBehaviour
{
    [Header("使用者 ID")]
    public string userID = "User_Mom";

    [Header("定點（Z 軸朝向家具正面）")]
    public Transform drinkSpot;
    public Transform sittingSpot;
    public Transform idleSpot;

    [Header("路點（選填，有牆才填，Y=0）")]
    public Transform[] drinkWaypoints;
    public Transform[] sittingWaypoints;
    public Transform[] idleWaypoints;

    [Header("移動設定")]
    public float walkSpeed = 1.4f;
    public float arrivalThreshold = 0.08f;
    public float rotationSpeed = 8f;

    [Header("Nodding 時長（秒）")]
    public float noddingDuration = 1.5f;

    [Header("手持物件（每個行為一組）")]
    [Tooltip("物件需已放在骨架子節點裡。Walking / Idle 不在列表，自動隱藏全部。")]
    public BehaviorItem[] behaviorItems;

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
    bool isSitting = false;

    // ══════════════════════════════════════════════════════
    void Start()
    {
        anim = GetComponent<Animator>();

        // 全部先隱藏，物件位置由 Hierarchy 決定，不做 SetParent
        if (behaviorItems != null)
            foreach (var bi in behaviorItems)
                if (bi.item != null)
                    bi.item.SetActive(false);

        PlayAnim(stateIdle);
    }

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

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
            default:
                Debug.LogWarning($"[{userID}] 未知 activity: {activity}");
                break;
        }

        IsBusy = false;
    }

    public IEnumerator ReturnToIdle()
    {
        IsBusy = true;
        yield return StartCoroutine(DoReturnToIdle());
        IsBusy = false;
    }

    public IEnumerator Nod()
    {
        PlayAnim(stateNodding);
        yield return new WaitForSeconds(noddingDuration);
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

    IEnumerator DoDrink()
    {
        if (drinkSpot == null) { Warn("drinkSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(drinkSpot.position, drinkWaypoints));
        yield return StartCoroutine(SmoothRotateTo(drinkSpot.forward));
        SetActivity("Drink");
        PlayAnim(stateDrink);
    }

    IEnumerator DoSit()
    {
        if (sittingSpot == null) { Warn("sittingSpot"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        TeleportToSeat();
        SetActivity("SittingIdle");
        PlayAnim(stateSittingIdle);
    }

    IEnumerator DoReading()
    {
        if (sittingSpot == null) { Warn("sittingSpot（Reading）"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        TeleportToSeat();
        SetActivity("Reading");
        PlayAnim(stateReading);
    }

    IEnumerator DoTyping()
    {
        if (sittingSpot == null) { Warn("sittingSpot（Typing）"); yield break; }
        SetActivity("Walking");
        yield return StartCoroutine(WalkVia(GetApproachPos(sittingSpot), sittingWaypoints));
        yield return StartCoroutine(SmoothRotateTo(sittingSpot.forward));
        TeleportToSeat();
        SetActivity("Typing");
        PlayAnim(stateTyping);
    }

    IEnumerator DoReturnToIdle()
    {
        if (isSitting)
        {
            PlayAnim(stateIdle);
            Vector3 p = transform.position; p.y = 0f;
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
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * rotationSpeed);
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
            transform.rotation = Quaternion.Slerp(
                transform.rotation, tgt, Time.deltaTime * rotationSpeed);
            yield return null;
        }
        transform.rotation = tgt;
    }

    // ══════════════════════════════════════════════════════
    // 輔助
    // ══════════════════════════════════════════════════════

    void TeleportToSeat()
    {
        transform.position = sittingSpot.position;
        transform.rotation = sittingSpot.rotation;
        isSitting = true;
    }

    Vector3 GetApproachPos(Transform spot)
    {
        Vector3 p = spot.position - spot.forward * 0.3f;
        p.y = 0f;
        return p;
    }

    /// <summary>
    /// 切換行為並更新物件顯示狀態。
    /// Walking / Idle / SittingIdle / Typing 不在 behaviorItems 裡 → 全部隱藏。
    /// </summary>
    void SetActivity(string a)
    {
        currentActivity = a;
        if (behaviorItems == null) return;

        foreach (var bi in behaviorItems)
        {
            if (bi.item == null) continue;
            bi.item.SetActive(
                string.Equals(bi.activity, a, System.StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    void PlayAnim(string s) => anim.Play(s, 0, 0f);
    void Warn(string s) => Debug.LogWarning($"[{userID}] {s} 未設定");

    // ── Gizmos ────────────────────────────────────────────
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
        foreach (var wp in wps)
        {
            if (wp != null) { Gizmos.DrawLine(prev, wp.position); prev = wp.position; }
        }
        if (final != null) Gizmos.DrawLine(prev, final.position);
    }
}