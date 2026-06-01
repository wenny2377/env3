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

<<<<<<< HEAD
    public float HipHeight()
    {
        if (_anim == null) return -1f;
        var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        return hips != null ? hips.position.y : -1f;
    }
=======
    // Returns -1 to simulate real deployment where hip_height is unavailable.
    // In real deployment, body_position is inferred by VLM from camera image.
    public float HipHeight() => -1f;
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48

    public float RightArmElevation()
    {
        if (_anim == null) return -1f;
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        Vector3 armDir = (wrist.position - shoulder.position).normalized;
        return Vector3.Angle(armDir, Vector3.up);
    }

<<<<<<< HEAD
=======
    // Returns head pitch with Gaussian noise (std=8 degrees) to simulate
    // MediaPipe Face Mesh estimation error in real deployment.
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
    public float HeadPitch()
    {
        if (_anim == null) return -999f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -999f;
        float pitch = head.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
<<<<<<< HEAD
        float noise = SampleGaussian(0f, 5f);
=======
        float noise = SampleGaussian(0f, 8f);
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
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

<<<<<<< HEAD
    // Simulates MediaPipe Pose normalized hip height (hip_y / body_height).
    // Gaussian noise (std=0.02) models MediaPipe landmark estimation error.
    // In real deployment, replace with:
    //   (landmark[23].y + landmark[24].y) / 2 / body_height
    // where body_height = distance(landmark[0], landmark[27+28 avg])
    public float NormalizedHipHeight()
    {
        float raw = HipHeight();
        if (raw < 0f) return -1f;
        float noise = SampleGaussian(0f, 0.02f);
        return Mathf.Clamp(raw + noise, 0f, 2f);
    }

    static float SampleGaussian(float mean, float std)
    {
=======
    // Samples Gaussian noise using Box-Muller transform.
    static float SampleGaussian(float mean, float std)
    {
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        return mean + std * z;
    }

<<<<<<< HEAD
    public string ToJsonFragment()
    {
        float h = NormalizedHipHeight();
=======
    // hip_height is omitted from the payload (sent as -1 implicitly via absence).
    // Flask perception_engine receives hip_height=-1 and skips skeleton-based
    // body_position inference, falling back to VLM body_position output.
    // This matches real deployment where MediaPipe replaces the Unity Animator.
    public string ToJsonFragment()
    {
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
        float a = RightArmElevation();
        float p = HeadPitch();
        float d = HandToHeadDistance();

        return
<<<<<<< HEAD
            $"\"hip_height\":{h.ToString("F3", Inv)}," +
=======
            $"\"hip_height\":-1," +
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
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

<<<<<<< HEAD
        if (hips != null)
        {
            float h  = NormalizedHipHeight();
            string bp = h < 0f      ? "unknown"  :
                        h < 0.860f  ? "lying"    :
                        h < 0.900f  ? "sitting"  : "standing";
            Gizmos.color = bp == "lying"    ? Color.green  :
                           bp == "sitting"  ? Color.yellow :
                           bp == "standing" ? Color.cyan   : Color.gray;
            Gizmos.DrawWireSphere(hips.position, 0.06f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                hips.position + Vector3.up * 0.1f,
                $"hip={h:F3} [{bp}]");
#endif
        }

=======
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
        if (head != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(head.position, 0.04f);
            float pitch = HeadPitch();
<<<<<<< HEAD
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                head.position + Vector3.up * 0.12f,
                $"pitch={pitch:F1}°");
#endif
=======
            UnityEditor.Handles.Label(
                head.position + Vector3.up * 0.12f,
                $"HeadPitch: {pitch:F1}°");
>>>>>>> 617704a1c011cdfddf6491439ce18ea51274cb48
        }

        if (hand != null && head != null)
        {
            float dist = HandToHeadDistance();
            Gizmos.color = dist >= 0 && dist < 0.25f ? Color.red : Color.gray;
            Gizmos.DrawLine(hand.position, head.position);
        }

        if (shoulder != null && hand != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(shoulder.position, hand.position);
        }
    }
}
