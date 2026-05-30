using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    [Header("姿態門檻（Inspector 可調）")]
    [Tooltip("hip_height 低於此值 → lying")]
    public float lyingHipThreshold   = 0.40f;

    [Tooltip("hip_height 低於此值 → sitting（高於 lyingHipThreshold）")]
    public float sittingHipThreshold = 0.65f;

    [Tooltip("arm_elevation 低於此值 → raised（手臂上舉，Drinking）")]
    public float raisedArmAngle      = 30f;

    [Tooltip("arm_elevation 低於此值 → mid（手臂平舉，PhoneUse）")]
    public float midArmAngle         = 65f;

    Animator _anim;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[SkeletonHelper] Animator not found on " + gameObject.name);
    }

    /// <summary>
    /// Hip joint 的世界座標 Y 值。
    /// 站立約 0.85-1.0，坐姿約 0.45-0.65，躺下約 0.10-0.35。
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
    /// 手臂完全上舉 ≈ 0°，水平 ≈ 90°，自然下垂 ≈ 150-180°。
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
    /// 低頭（PhoneUse/Eating）約 20-40°，抬頭（Watching）約 -10-0°。
    /// 注意 Unity 歐拉角範圍是 0-360，低頭是正值，抬頭繞到 340-360。
    /// 回傳 -1 表示讀取失敗。
    /// </summary>
    public float HeadPitch()
    {
        if (_anim == null) return -1f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -1f;

        float pitch = head.eulerAngles.x;
        // 把 270-360 轉成負數，讓低頭=正、抬頭=負
        if (pitch > 180f) pitch -= 360f;
        return pitch;
    }

    /// <summary>
    /// 綜合判斷 body_position（standing / sitting / lying）
    /// </summary>
    public string BodyPosition()
    {
        float h = HipHeight();
        if (h < 0) return "unknown";
        if (h < lyingHipThreshold)   return "lying";
        if (h < sittingHipThreshold) return "sitting";
        return "standing";
    }

    /// <summary>
    /// 綜合判斷 arm_pose（raised / mid / low / unknown）
    /// raised → Drinking
    /// mid    → PhoneUse
    /// low    → Eating / Sitting
    /// </summary>
    public string ArmPose()
    {
        float a = RightArmElevation();
        if (a < 0)              return "unknown";
        if (a < raisedArmAngle) return "raised";
        if (a < midArmAngle)    return "mid";
        return "low";
    }

    /// <summary>
    /// 給 VirtualCameraBrain 用：直接輸出 JSON 片段字串。
    /// 格式："hip_height":0.52,"arm_elevation":45.2,"head_pitch":12.3,"body_pos":"sitting","arm_pose":"low",
    /// </summary>
    public string ToJsonFragment()
    {
        float h = HipHeight();
        float a = RightArmElevation();
        float p = HeadPitch();

        string bpos = BodyPosition();
        string apose = ArmPose();

        return
            $"\"hip_height\":{h.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"arm_elevation\":{a.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"head_pitch\":{p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"body_pos\":\"{bpos}\"," +
            $"\"arm_pose\":\"{apose}\",";
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _anim == null) return;

        var hips     = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var head     = _anim.GetBoneTransform(HumanBodyBones.Head);

        if (hips != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(hips.position, 0.06f);
        }
        if (shoulder != null && wrist != null)
        {
            Gizmos.color = ArmPose() == "raised" ? Color.cyan :
                           ArmPose() == "mid"    ? Color.green : Color.gray;
            Gizmos.DrawLine(shoulder.position, wrist.position);
        }
        if (head != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(head.position, 0.04f);
        }
    }
}