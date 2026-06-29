using UnityEngine;
using System.Text;

[RequireComponent(typeof(Animator))]
public class SkeletonHelper : MonoBehaviour
{
    Animator _anim;
    bool     _noiseEnabled  = false;
    float    _noiseStd      = 15f;
    float    _cachedBodyHeight = -1f;

    readonly StringBuilder _sb = new StringBuilder(256);

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[SkeletonHelper] Animator not found on " + gameObject.name);
        else if (!_anim.isHuman)
            Debug.LogError("[SkeletonHelper] Animator is not Humanoid on " + gameObject.name);
    }

    public void OnActivityChanged(string activity)
    {
        _cachedBodyHeight = -1f;
    }

    public void SetSkeletonNoise(bool enabled, float std = 15f)
    {
        _noiseEnabled = enabled;
        _noiseStd     = std;
    }

    Transform Bone(HumanBodyBones b) => _anim?.GetBoneTransform(b);

    Vector3 Mid(Transform a, Transform b)
    {
        if (a == null || b == null) return Vector3.zero;
        return (a.position + b.position) * 0.5f;
    }

    float Angle(Vector3 a, Vector3 b)
    {
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return -1f;
        return Vector3.Angle(a, b);
    }

    float BodyHeight()
    {
        if (_cachedBodyHeight > 0f) return _cachedBodyHeight;
        var head   = Bone(HumanBodyBones.Head);
        var lAnkle = Bone(HumanBodyBones.LeftFoot);
        var rAnkle = Bone(HumanBodyBones.RightFoot);
        if (head == null || lAnkle == null || rAnkle == null) return 1.7f;
        float ankleY = (lAnkle.position.y + rAnkle.position.y) * 0.5f;
        float h      = head.position.y - ankleY;
        if (h > 0.3f) { _cachedBodyHeight = h; return _cachedBodyHeight; }
        return 1.7f;
    }

    Vector3 BodyAxis()
    {
        var lShoulder = Bone(HumanBodyBones.LeftUpperArm);
        var rShoulder = Bone(HumanBodyBones.RightUpperArm);
        var lHip      = Bone(HumanBodyBones.LeftUpperLeg);
        var rHip      = Bone(HumanBodyBones.RightUpperLeg);
        if (lShoulder == null || rShoulder == null || lHip == null || rHip == null)
            return Vector3.zero;
        return Mid(lShoulder, rShoulder) - Mid(lHip, rHip);
    }

    public float BodyAxisAngle() => Angle(BodyAxis(), Vector3.up);

    public float HeadPitch()
    {
        var head      = Bone(HumanBodyBones.Head);
        var lShoulder = Bone(HumanBodyBones.LeftUpperArm);
        var rShoulder = Bone(HumanBodyBones.RightUpperArm);
        if (head == null || lShoulder == null || rShoulder == null) return -1f;
        return Angle(head.position - Mid(lShoulder, rShoulder), BodyAxis());
    }

    public float HandToHead(bool useLeft)
    {
        var wrist = Bone(useLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var head  = Bone(HumanBodyBones.Head);
        if (wrist == null || head == null) return -1f;
        float bh = BodyHeight();
        if (bh <= 0f) return -1f;
        return Vector3.Distance(wrist.position, head.position) / bh;
    }

    public float KneeHipRatio()
    {
        var lKnee  = Bone(HumanBodyBones.LeftLowerLeg);
        var rKnee  = Bone(HumanBodyBones.RightLowerLeg);
        var lAnkle = Bone(HumanBodyBones.LeftFoot);
        var rAnkle = Bone(HumanBodyBones.RightFoot);
        var lHip   = Bone(HumanBodyBones.LeftUpperLeg);
        var rHip   = Bone(HumanBodyBones.RightUpperLeg);
        if (lKnee == null || rKnee == null || lAnkle == null ||
            rAnkle == null || lHip == null || rHip == null) return -1f;
        float lDenom = lHip.position.y - lAnkle.position.y;
        float rDenom = rHip.position.y - rAnkle.position.y;
        if (Mathf.Abs(lDenom) < 0.01f || Mathf.Abs(rDenom) < 0.01f) return -1f;
        float lRatio = (lKnee.position.y - lAnkle.position.y) / lDenom;
        float rRatio = (rKnee.position.y - rAnkle.position.y) / rDenom;
        return (lRatio + rRatio) * 0.5f;
    }

    public float ArmElevation(bool useLeft)
    {
        var shoulder = Bone(useLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var wrist    = Bone(useLeft ? HumanBodyBones.LeftHand      : HumanBodyBones.RightHand);
        if (shoulder == null || wrist == null) return -1f;
        return Angle(wrist.position - shoulder.position, BodyAxis());
    }

    float MaybeNoise(float val, float std)
    {
        if (!_noiseEnabled || val < 0f) return val;
        return val + SampleGaussian(std);
    }

    static float SampleGaussian(float std)
    {
        float u1 = UnityEngine.Random.value;
        while (u1 <= 0f) u1 = UnityEngine.Random.value;
        float u2 = UnityEngine.Random.value;
        while (u2 <= 0f) u2 = UnityEngine.Random.value;
        return std * Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
    }

    public string ToJsonFragment()
    {
        float distStd  = _noiseStd * 0.003f;
        float ratioStd = _noiseStd * 0.001f;

        float bodyAxis  = MaybeNoise(BodyAxisAngle(),    _noiseStd);
        float headPitch = MaybeNoise(HeadPitch(),        _noiseStd);
        float rH2h      = MaybeNoise(HandToHead(false),  distStd);
        float lH2h      = MaybeNoise(HandToHead(true),   distStd);
        float kneeHip   = MaybeNoise(KneeHipRatio(),     ratioStd);
        float rArm      = MaybeNoise(ArmElevation(false), _noiseStd);
        float lArm      = MaybeNoise(ArmElevation(true),  _noiseStd);

        _sb.Clear();
        _sb.Append("\"body_axis_angle\":");    _sb.Append(bodyAxis.ToString("F3",  JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"head_pitch\":");         _sb.Append(headPitch.ToString("F3", JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"hand_to_head\":");       _sb.Append(rH2h.ToString("F3",     JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"left_hand_to_head\":");  _sb.Append(lH2h.ToString("F3",     JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"knee_hip_ratio\":");     _sb.Append(kneeHip.ToString("F3",  JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"arm_elevation\":");      _sb.Append(rArm.ToString("F3",     JsonUtil.Inv)); _sb.Append(',');
        _sb.Append("\"left_arm_elevation\":"); _sb.Append(lArm.ToString("F3",     JsonUtil.Inv)); _sb.Append(',');
        return _sb.ToString();
    }
}