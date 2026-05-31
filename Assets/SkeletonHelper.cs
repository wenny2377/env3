using UnityEngine;

/// <summary>
/// 掛在 UserEntity 同一個 GameObject 上。
/// 從 Humanoid Animator 讀取骨架關節，計算姿態特徵值，
/// 送進 VirtualCameraBrain 的 POST /predict payload。
///
/// 放置位置：Assets/Scripts/SkeletonHelper.cs
/// 使用方式：在 UserEntity 的 GameObject Inspector 裡 Add Component -> SkeletonHelper
///
/// 實測門檻（User_Mom & User_Dad，Humanoid rig）：
///   Laying          : hip 0.789-0.846  -> lying   (門檻 < 0.860)
///   坐姿群           : hip 0.690-0.742  -> sitting (門檻 < 0.900)
///   站姿群           : hip 0.895-0.993  -> standing
///   arm_elevation 在此動畫集無效（Drinking arm=174°），不做行為命中
/// </summary>
[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    [Header("Hip 姿態門檻（實測校準值）")]
    [Tooltip("hip < 0.900 且 head_pitch < -45° → lying（Laying 實測 head=-83°）")]
    public float lyingHipThreshold   = 0.900f;

    [Tooltip("hip < 0.900 且 head_pitch ≥ -45° → sitting（坐姿群實測 head=0-38°）")]
    public float sittingHipThreshold = 0.900f;

    Animator _anim;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[SkeletonHelper] Animator not found on " + gameObject.name);
        else if (!_anim.isHuman)
            Debug.LogError("[SkeletonHelper] Animator is not Humanoid on " + gameObject.name);
    }

    /// <summary>
    /// Hip joint 的世界座標 Y 值。
    /// 站立約 0.895-0.993，坐姿約 0.690-0.742，躺下約 0.789-0.846。
    /// 回傳 -1 表示讀取失敗。
    /// </summary>
    public float HipHeight()
    {
        if (_anim == null) return -1f;
        var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        return hips != null ? hips.position.y : -1f;
    }

    /// <summary>
    /// 右手腕與右肩的連線向量和 Vector3.up 的夾角（度）。
    /// 此動畫集中 arm_elevation 無法區分行為（Drinking=174°，與站立重疊）。
    /// 保留此方法供未來動畫改善後使用，目前 Flask 端不採用。
    /// 回傳 -1 表示讀取失敗。
    /// </summary>
    public float RightArmElevation()
    {
        if (_anim == null) return -1f;
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        Vector3 armDir = (wrist.position - shoulder.position).normalized;
        return Vector3.Angle(armDir, Vector3.up);
    }

    /// <summary>
    /// 頭部 X 軸歐拉角（俯仰）。
    /// 低頭約 20-40°，抬頭約 -10-0°。
    /// 回傳 -1 表示讀取失敗。
    /// </summary>
    public float HeadPitch()
    {
        if (_anim == null) return -1f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -1f;
        float pitch = head.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        return pitch;
    }

    /// <summary>
    /// 右手腕與頭部的距離（公尺）。
    /// 保留供未來更寫實動畫使用（Drinking 時手靠近嘴巴）。
    /// 回傳 -1 表示讀取失敗。
    /// </summary>
    public float HandToHeadDistance()
    {
        if (_anim == null) return -1f;
        var hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (hand == null || head == null) return -1f;
        return Vector3.Distance(hand.position, head.position);
    }

    /// <summary>
    /// 依 hip_height + head_pitch 判斷 body_position。
    /// 坐姿(0.690-0.742) 比躺姿(0.789-0.846) hip 更低，不能單靠 hip 區分。
    /// 用 head_pitch 輔助：Laying=-83°，Sitting=0-5°，差距極大。
    /// </summary>
    public string BodyPosition()
    {
        float h = HipHeight();
        float p = HeadPitch();
        if (h < 0)          return "unknown";
        if (h >= 0.900f)    return "standing";
        // hip < 0.900：坐/躺重疊區，用 head_pitch 區分
        if (p < -45f)       return "lying";
        return "sitting";
    }

    /// <summary>
    /// 給 VirtualCameraBrain 用：直接輸出 JSON 片段字串。
    /// arm_pose 固定送 "unknown"，因為此動畫集 arm_elevation 無法區分行為。
    /// Flask 端不會採用 arm_pose 做行為命中，只使用 body_pos 修正 body_position。
    /// </summary>
    public string ToJsonFragment()
    {
        float h = HipHeight();
        float a = RightArmElevation();
        float p = HeadPitch();
        float d = HandToHeadDistance();

        string bpos = BodyPosition();

        return
            $"\"hip_height\":{h.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"arm_elevation\":{a.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"head_pitch\":{p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"hand_to_head\":{d.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"body_pos\":\"{bpos}\"," +
            $"\"arm_pose\":\"unknown\",";
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _anim == null) return;

        var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        var hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);

        if (hips != null)
        {
            string bp = BodyPosition();
            Gizmos.color = bp == "lying"   ? Color.green  :
                           bp == "sitting" ? Color.yellow :
                                             Color.cyan;
            Gizmos.DrawWireSphere(hips.position, 0.06f);
        }

        if (head != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(head.position, 0.04f);
        }

        if (hand != null && head != null)
        {
            float d = HandToHeadDistance();
            Gizmos.color = d >= 0 && d < 0.25f ? Color.red : Color.gray;
            Gizmos.DrawLine(hand.position, head.position);
        }

        if (shoulder != null && hand != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(shoulder.position, hand.position);
        }
    }
}