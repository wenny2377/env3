using UnityEngine;
[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    [Header("Hip 姿態門檻（實測校準值，勿隨意調整）")]
    [Tooltip("hip_height 低於此值 -> lying（Laying 實測: 0.789-0.846）")]
    public float lyingHipThreshold = 0.860f;

    [Tooltip("hip_height 低於此值 -> sitting（坐姿群實測: 0.690-0.742）")]
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

    public float HipHeight()
    {
        if (_anim == null) return -1f;
        var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        return hips != null ? hips.position.y : -1f;
    }

    public float RightArmElevation()
    {
        if (_anim == null) return -1f;
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        Vector3 armDir = (wrist.position - shoulder.position).normalized;
        return Vector3.Angle(armDir, Vector3.up);
    }

    public float HeadPitch()
    {
        if (_anim == null) return -1f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -1f;
        float pitch = head.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        return pitch;
    }

    public float HandToHeadDistance()
    {
        if (_anim == null) return -1f;
        var hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (hand == null || head == null) return -1f;
        return Vector3.Distance(hand.position, head.position);
    }


    public string BodyPosition()
    {
        float h = HipHeight();
        if (h < 0) return "unknown";
        if (h < lyingHipThreshold) return "lying";
        if (h < sittingHipThreshold) return "sitting";
        return "standing";
    }

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
            Gizmos.color = bp == "lying" ? Color.green :
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