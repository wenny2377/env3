using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    Animator _anim;

    [HideInInspector] public bool skeletonNoiseEnabled = false;

    float[] _episodeOffsets = new float[8];
    string  _currentActivity = "";

    static readonly Dictionary<string, float[]> INTRA_CLASS_STD =
        new Dictionary<string, float[]>
    {
        // [pitch, spine, arm, h2h, wrist_h, wrist_z, hip, knee]
        // sigma_corrupt = 0.5 * sigma_NTU (Shahroudy et al., CVPR 2016)
        // spine/hip/knee: no NTU equivalent, conservative estimate from sensor std
        { "Eating",      new float[]{ 8.621f, 4.310f, 22.285f, 0.164f, 0.170f, 0.116f, 0.008f, 0.008f } },
        { "Drinking",    new float[]{ 9.333f, 4.667f, 21.921f, 0.173f, 0.165f, 0.133f, 0.008f, 0.008f } },
        { "SittingDrink",new float[]{ 9.333f, 4.667f, 21.921f, 0.173f, 0.165f, 0.133f, 0.008f, 0.008f } },
        { "Reading",     new float[]{ 9.307f, 4.654f, 26.774f, 0.139f, 0.255f, 0.182f, 0.008f, 0.008f } },
        { "PhoneUse",    new float[]{ 9.295f, 4.648f,  9.982f, 0.098f, 0.096f, 0.106f, 0.008f, 0.008f } },
        { "Typing",      new float[]{11.743f, 5.872f, 10.204f, 0.104f, 0.096f, 0.112f, 0.008f, 0.008f } },
        { "Cleaning",    new float[]{13.959f, 6.979f, 12.060f, 0.136f, 0.119f, 0.146f, 0.008f, 0.008f } },
        { "Cooking",     new float[]{ 9.307f, 4.654f, 26.774f, 0.139f, 0.255f, 0.182f, 0.008f, 0.008f } },
        { "Watching",    new float[]{18.752f, 9.376f,  8.961f, 0.115f, 0.084f, 0.123f, 0.008f, 0.008f } },
        { "Laying",      new float[]{12.336f, 6.168f, 11.358f, 0.100f, 0.095f, 0.118f, 0.008f, 0.008f } },
        { "Sitting",     new float[]{ 9.222f, 4.611f, 20.740f, 0.102f, 0.206f, 0.175f, 0.008f, 0.008f } },
        { "Opening",     new float[]{ 9.333f, 4.667f, 21.921f, 0.173f, 0.165f, 0.133f, 0.008f, 0.008f } },
        { "Standing",    new float[]{ 9.222f, 4.611f, 20.740f, 0.102f, 0.206f, 0.175f, 0.008f, 0.008f } },
    };

    const float SENSOR_STD_PITCH   = 4.0f;
    const float SENSOR_STD_SPINE   = 2.5f;
    const float SENSOR_STD_ARM     = 3.0f;
    const float SENSOR_STD_H2H     = 0.015f;
    const float SENSOR_STD_WRIST_H = 0.015f;
    const float SENSOR_STD_WRIST_Z = 0.015f;
    const float SENSOR_STD_HIP     = 0.015f;
    const float SENSOR_STD_KNEE    = 0.015f;

    static readonly System.Globalization.CultureInfo Inv =
        System.Globalization.CultureInfo.InvariantCulture;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[SkeletonHelper] Animator not found on " + gameObject.name);
        else if (!_anim.isHuman)
            Debug.LogError("[SkeletonHelper] Animator is not Humanoid on " + gameObject.name);
        ResetEpisodeOffsets();
    }

    public void OnActivityChanged(string activity)
    {
        _currentActivity = activity;
        ResampleEpisodeOffsets(activity);
    }

    void ResetEpisodeOffsets()
    {
        for (int i = 0; i < _episodeOffsets.Length; i++)
            _episodeOffsets[i] = 0f;
    }

    void ResampleEpisodeOffsets(string activity)
    {
        if (!skeletonNoiseEnabled)
        {
            ResetEpisodeOffsets();
            return;
        }
        float[] std;
        if (INTRA_CLASS_STD.TryGetValue(activity, out std))
        {
            for (int i = 0; i < _episodeOffsets.Length; i++)
                _episodeOffsets[i] = SampleGaussian(0f, std[i]);
        }
        else
        {
            ResetEpisodeOffsets();
        }
        Debug.Log($"[SkeletonHelper] offsets for {activity}: " +
                  $"pitch={_episodeOffsets[0]:F2} arm={_episodeOffsets[2]:F2} " +
                  $"h2h={_episodeOffsets[3]:F3}");
    }

    float BodyHeight()
    {
        var head      = _anim.GetBoneTransform(HumanBodyBones.Head);
        var leftFoot  = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
        if (head == null || leftFoot == null || rightFoot == null) return -1f;
        float ankleY = (leftFoot.position.y + rightFoot.position.y) / 2f;
        float h = head.position.y - ankleY;
        return h < 0.3f ? -1f : h;
    }

    float AnkleY()
    {
        var leftFoot  = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
        if (leftFoot == null || rightFoot == null) return -1f;
        return (leftFoot.position.y + rightFoot.position.y) / 2f;
    }

    public float NormalizedHipHeight()
    {
        if (_anim == null) return -1f;
        var hip = _anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hip == null) return -1f;
        float bh     = BodyHeight();
        float ankleY = AnkleY();
        if (bh < 0f || ankleY < 0f) return -1f;
        float normalized = (hip.position.y - ankleY) / bh;
        float noise      = SampleGaussian(0f, SENSOR_STD_HIP) + _episodeOffsets[6];
        return Mathf.Clamp(normalized + noise, 0f, 1f);
    }

    public float NormalizedKneeHeight()
    {
        if (_anim == null) return -1f;
        var leftUpperLeg  = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var leftLowerLeg  = _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var rightUpperLeg = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var rightLowerLeg = _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        if (leftUpperLeg == null || leftLowerLeg == null) return -1f;
        if (rightUpperLeg == null || rightLowerLeg == null) return -1f;
        float bh     = BodyHeight();
        float ankleY = AnkleY();
        if (bh < 0f || ankleY < 0f) return -1f;
        float leftKneeY  = (leftUpperLeg.position.y  + leftLowerLeg.position.y)  / 2f;
        float rightKneeY = (rightUpperLeg.position.y + rightLowerLeg.position.y) / 2f;
        float kneeY      = (leftKneeY + rightKneeY) / 2f;
        float normalized = (kneeY - ankleY) / bh;
        float noise      = SampleGaussian(0f, SENSOR_STD_KNEE) + _episodeOffsets[7];
        return Mathf.Clamp(normalized + noise, 0f, 1f);
    }

    public float HeadPitch()
    {
        if (_anim == null) return -999f;
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return -999f;
        float pitch = head.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        float noise = SampleGaussian(0f, SENSOR_STD_PITCH) + _episodeOffsets[0];
        return pitch + noise;
    }

    public float SpineAngle()
    {
        if (_anim == null) return -1f;
        var hip   = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
        if (hip == null || chest == null) return -1f;
        Vector3 spineDir = (chest.position - hip.position).normalized;
        float angle = Vector3.Angle(spineDir, Vector3.up);
        float noise = SampleGaussian(0f, SENSOR_STD_SPINE) + _episodeOffsets[1];
        return Mathf.Clamp(angle + noise, 0f, 90f);
    }

    public float RightArmElevation()
    {
        if (_anim == null) return -1f;
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var wrist    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        Vector3 armDir = (wrist.position - shoulder.position).normalized;
        float angle = Vector3.Angle(armDir, Vector3.up);
        float noise = SampleGaussian(0f, SENSOR_STD_ARM) + _episodeOffsets[2];
        return Mathf.Clamp(angle + noise, 0f, 180f);
    }

    public float NormalizedHandToHead(bool useLeft = false)
    {
        if (_anim == null) return -1f;
        var hand = _anim.GetBoneTransform(
            useLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var head = _anim.GetBoneTransform(HumanBodyBones.Head);
        if (hand == null || head == null) return -1f;
        float bh = BodyHeight();
        if (bh < 0f) return -1f;
        float dist  = Vector3.Distance(hand.position, head.position);
        float noise = SampleGaussian(0f, SENSOR_STD_H2H) + _episodeOffsets[3];
        return Mathf.Clamp((dist + noise) / bh, 0f, 1.5f);
    }

    public float NormalizedWristHeight(bool useLeft = false)
    {
        if (_anim == null) return -999f;
        var hip   = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var wrist = _anim.GetBoneTransform(
            useLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hip == null || wrist == null) return -999f;
        float bh = BodyHeight();
        if (bh < 0f) return -999f;
        float diff  = wrist.position.y - hip.position.y;
        float noise = SampleGaussian(0f, SENSOR_STD_WRIST_H) + _episodeOffsets[4];
        return Mathf.Clamp((diff + noise) / bh, -1f, 1f);
    }

    public float NormalizedWristRelativeX(bool useLeft = false)
    {
        if (_anim == null) return -999f;
        var hip   = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var wrist = _anim.GetBoneTransform(
            useLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hip == null || wrist == null) return -999f;
        float bh = BodyHeight();
        if (bh < 0f) return -999f;
        float diff  = wrist.position.x - hip.position.x;
        float noise = SampleGaussian(0f, SENSOR_STD_WRIST_Z);
        return Mathf.Clamp((diff + noise) / bh, -1f, 1f);
    }

    public float NormalizedWristRelativeZ(bool useLeft = false)
    {
        if (_anim == null) return -999f;
        var hip   = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var wrist = _anim.GetBoneTransform(
            useLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hip == null || wrist == null) return -999f;
        float bh = BodyHeight();
        if (bh < 0f) return -999f;
        float diff  = wrist.position.z - hip.position.z;
        float noise = SampleGaussian(0f, SENSOR_STD_WRIST_Z) + _episodeOffsets[5];
        return Mathf.Clamp((diff + noise) / bh, -1f, 1f);
    }

    static float SampleGaussian(float mean, float std)
    {
        if (std <= 0f) return mean;
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        return mean + std * z;
    }

    public string ToJsonFragment()
    {
        float hip       = NormalizedHipHeight();
        float knee      = NormalizedKneeHeight();
        float pitch     = HeadPitch();
        float spine     = SpineAngle();
        float arm       = RightArmElevation();
        float r_h2h     = NormalizedHandToHead(useLeft: false);
        float l_h2h     = NormalizedHandToHead(useLeft: true);
        float r_wrist   = NormalizedWristHeight(useLeft: false);
        float l_wrist   = NormalizedWristHeight(useLeft: true);
        float r_wrist_x = NormalizedWristRelativeX(useLeft: false);
        float r_wrist_z = NormalizedWristRelativeZ(useLeft: false);
        float l_wrist_x = NormalizedWristRelativeX(useLeft: true);
        float l_wrist_z = NormalizedWristRelativeZ(useLeft: true);

        return
            $"\"hip_height\":{hip.ToString("F3", Inv)},"             +
            $"\"knee_height\":{knee.ToString("F3", Inv)},"           +
            $"\"head_pitch\":{pitch.ToString("F3", Inv)},"           +
            $"\"spine_angle\":{spine.ToString("F3", Inv)},"          +
            $"\"arm_elevation\":{arm.ToString("F3", Inv)},"          +
            $"\"hand_to_head\":{r_h2h.ToString("F3", Inv)},"         +
            $"\"left_hand_to_head\":{l_h2h.ToString("F3", Inv)},"    +
            $"\"wrist_height\":{r_wrist.ToString("F3", Inv)},"       +
            $"\"left_wrist_height\":{l_wrist.ToString("F3", Inv)},"  +
            $"\"wrist_x\":{r_wrist_x.ToString("F3", Inv)},"          +
            $"\"wrist_z\":{r_wrist_z.ToString("F3", Inv)},"          +
            $"\"left_wrist_x\":{l_wrist_x.ToString("F3", Inv)},"     +
            $"\"left_wrist_z\":{l_wrist_z.ToString("F3", Inv)},";
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || _anim == null) return;

        var hips     = _anim.GetBoneTransform(HumanBodyBones.Hips);
        var head     = _anim.GetBoneTransform(HumanBodyBones.Head);
        var rhand    = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        var lhand    = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var chest    = _anim.GetBoneTransform(HumanBodyBones.Chest);
        var shoulder = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var leftLow  = _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var leftUp   = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);

        if (hips != null)
        {
            float nh    = NormalizedHipHeight();
            float nk    = NormalizedKneeHeight();
            float ratio = (nh > 0f && nk >= 0f) ? nk / nh : -1f;
            string bp   = nh < 0f    ? "unknown"  :
                          nh < 0.20f ? "lying"    :
                          ratio >= 0f && ratio <= 0.52f ? "sitting" : "standing";
            Gizmos.color = bp == "standing" ? Color.cyan   :
                           bp == "sitting"  ? Color.yellow :
                           bp == "lying"    ? Color.green  : Color.gray;
            Gizmos.DrawWireSphere(hips.position, 0.06f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                hips.position + Vector3.up * 0.1f,
                $"hip={nh:F2} knee={nk:F2} ratio={ratio:F2} [{bp}] act={_currentActivity}");
#endif
        }

        if (head != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(head.position, 0.04f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                head.position + Vector3.up * 0.12f,
                $"pitch={HeadPitch():F1}° offset={_episodeOffsets[0]:F1}");
#endif
        }

        if (rhand != null && head != null)
        {
            float h2h = NormalizedHandToHead(false);
            Gizmos.color = h2h >= 0 && h2h < 0.38f ? Color.red : Color.gray;
            Gizmos.DrawLine(rhand.position, head.position);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                (rhand.position + head.position) * 0.5f,
                $"R_h2h={h2h:F2}");
#endif
        }

        if (lhand != null && head != null)
        {
            float lh2h = NormalizedHandToHead(true);
            Gizmos.color = lh2h >= 0 && lh2h < 0.38f ? Color.red : Color.blue;
            Gizmos.DrawLine(lhand.position, head.position);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                (lhand.position + head.position) * 0.5f,
                $"L_h2h={lh2h:F2}");
#endif
        }

        if (shoulder != null && rhand != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(shoulder.position, rhand.position);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(shoulder.position, $"arm={RightArmElevation():F1}°");
#endif
        }

        if (hips != null && chest != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(hips.position, chest.position);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                (hips.position + chest.position) * 0.5f,
                $"spine={SpineAngle():F1}°");
#endif
        }

        if (leftUp != null && leftLow != null)
        {
            Vector3 kneePos = (leftUp.position + leftLow.position) / 2f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(kneePos, 0.04f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                kneePos + Vector3.up * 0.08f,
                $"knee={NormalizedKneeHeight():F2}");
#endif
        }
    }
}