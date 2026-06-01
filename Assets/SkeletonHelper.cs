using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    Animator _anim;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[SkeletonHelper] Animator not found on " + gameObject.name);
        else if (!_anim.isHuman)
            Debug.LogError("[SkeletonHelper] Animator is not Humanoid on " + gameObject.name);
    }

    // Returns -1 to simulate real deployment where hip_height is unavailable.
    // In real deployment, body_position is inferred by VLM from camera image.
    public float HipHeight() => -1f;

    public float RightArmElevation()
    {
        if (_anim == null) return -1f;
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        Vector3 armDir = (wrist.position - shoulder.position).normalized;
        return Vector3.Angle(armDir, Vector3.up);
    }

    // Returns head pitch with Gaussian noise (std=8 degrees) to simulate
    // MediaPipe Face Mesh estimation error in real deployment.
    public float HeadPitch()
    {
        if (_anim == null) return -999f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -999f;
        float pitch = head.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        float noise = SampleGaussian(0f, 8f);
        return pitch + noise;
    }

    public float HandToHeadDistance()
    {
        if (_anim == null) return -1f;
        var hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (hand == null || head == null) return -1f;
        return Vector3.Distance(hand.position, head.position);
    }

    // Samples Gaussian noise using Box-Muller transform.
    static float SampleGaussian(float mean, float std)
    {
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        return mean + std * z;
    }

    // hip_height is omitted from the payload (sent as -1 implicitly via absence).
    // Flask perception_engine receives hip_height=-1 and skips skeleton-based
    // body_position inference, falling back to VLM body_position output.
    // This matches real deployment where MediaPipe replaces the Unity Animator.
    public string ToJsonFragment()
    {
        float a = RightArmElevation();
        float p = HeadPitch();
        float d = HandToHeadDistance();

        return
            $"\"hip_height\":-1," +
            $"\"arm_elevation\":{a.ToString("F3", Inv)}," +
            $"\"head_pitch\":{p.ToString("F3", Inv)}," +
            $"\"hand_to_head\":{d.ToString("F3", Inv)}," +
            $"\"body_pos\":\"unknown\"," +
            $"\"arm_pose\":\"unknown\",";
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _anim == null) return;

        var head     = _anim.GetBoneTransform(HumanBodyBones.Head);
        var hand     = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);

        if (head != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(head.position, 0.04f);
            float pitch = HeadPitch();
            UnityEditor.Handles.Label(
                head.position + Vector3.up * 0.12f,
                $"HeadPitch: {pitch:F1}°");
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
