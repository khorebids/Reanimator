using System;
using UnityEngine;

[Serializable]
public sealed class RigBreathing2D
{
    [Serializable]
    public sealed class Bone
    {
        public Transform bone;
        [Range(0f, 1f)] public float compensation = 1f;

        // Unity can serialize the previewed transforms across an assembly reload.
        // Keep their matching undo snapshot too, so an animated pose never becomes rest pose.
        [SerializeField, HideInInspector] private Transform appliedBone;
        [SerializeField, HideInInspector] private Vector3 originalPosition, originalScale;
        [SerializeField, HideInInspector] private Vector3 appliedPosition, appliedScale;
        [SerializeField, HideInInspector] private Quaternion originalRotation, appliedRotation;

        internal void Apply(Vector3 worldOffset, float scale)
        {
            Apply(worldOffset, Vector3.one * scale);
        }

        internal void Apply(Vector3 worldOffset, Vector3 scale, float rotationDegrees = 0f)
        {
            if (!bone) return;
            appliedBone = bone;
            originalPosition = bone.localPosition;
            originalScale = bone.localScale;
            originalRotation = bone.localRotation;
            appliedPosition = originalPosition +
                (bone.parent ? bone.parent.InverseTransformVector(worldOffset) : worldOffset);
            appliedScale = Vector3.Scale(originalScale, scale);
            appliedRotation = originalRotation * Quaternion.AngleAxis(rotationDegrees, Vector3.forward);
            bone.localPosition = appliedPosition;
            bone.localScale = appliedScale;
            if (rotationDegrees != 0f)
                bone.localRotation = appliedRotation;
        }

        internal void Restore()
        {
            if (!appliedBone) return;
            if (appliedBone.localPosition == appliedPosition) appliedBone.localPosition = originalPosition;
            if (appliedBone.localScale == appliedScale) appliedBone.localScale = originalScale;
            if (appliedBone.localRotation == appliedRotation) appliedBone.localRotation = originalRotation;
            appliedBone = null;
        }
    }

    public bool enabled;
    [Min(0.1f)] public float period = 3f;
    [Min(0f)] public float height = 0.08f;
    [Range(0f, 0.5f)] public float spineExpansion = 0.025f;
    public float spineRotationDegrees;
    public Transform belly;
    public Transform spine;
    public Bone neck = new Bone();
    public Bone leftArm = new Bone();
    public Bone rightArm = new Bone();
    public bool faceEnabled = true;
    public Transform mouth;
    public Transform nose;
    [Tooltip("Local X and Y shrink fractions at full inhale.")]
    public Vector2 mouthCompression = new Vector2(0.2f, 0.05f);
    [Min(0f)] public float mouthLift = 0.01f;
    [Range(0f, 1f)] public float noseExpansion = 0.312f;
    [Min(0f)] public float noseLift = 0.12f;
    public float mouthRotationDegrees;
    public float noseRotationDegrees;

    [NonSerialized] private float phase;
    [SerializeField, HideInInspector] private Bone bellyPose;
    [SerializeField, HideInInspector] private Bone spinePose;
    [SerializeField, HideInInspector] private Bone mouthPose;
    [SerializeField, HideInInspector] private Bone nosePose;

    internal void DiscoverFaceBones(Transform rig)
    {
        if (mouth && nose) return;
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
        {
            if (!mouth && string.Equals(bone.name, "mouth", StringComparison.OrdinalIgnoreCase))
                mouth = bone;
            if (!nose && string.Equals(bone.name, "nose", StringComparison.OrdinalIgnoreCase))
                nose = bone;
        }
    }

    internal void Evaluate(Transform rig, float deltaTime, float frequency, float multiplier)
    {
        var animateFace = faceEnabled && (mouth || nose);
        if (!enabled && !animateFace)
        {
            phase = 0f;
            return;
        }
        var phaseStep = Mathf.Max(0f, deltaTime) * Mathf.Clamp(frequency, 0f, 3f) / Mathf.Max(0.1f, period);
        phase = Mathf.Repeat(phase + phaseStep, 1f);
        var wave = Mathf.Sin(phase * Mathf.PI * 2f);
        multiplier = Mathf.Clamp(multiplier, 0f, 3f);
        if (enabled)
            ApplyBodyBreath(rig, wave * multiplier);
        if (animateFace)
            ApplyFaceBreath(rig, (wave + 1f) * 0.5f * multiplier);
    }

    private void ApplyBodyBreath(Transform rig, float wave)
    {
        Vector3 offset = rig.up * (height * wave);
        float scale = Mathf.Max(0.01f, 1f + Mathf.Clamp(spineExpansion, 0f, 0.5f) * wave);
        bellyPose ??= new Bone();
        spinePose ??= new Bone();
        bellyPose.bone = belly;
        spinePose.bone = spine;
        bellyPose.Apply(offset, 1f);
        spinePose.Apply(Vector3.zero, Vector3.one * scale, spineRotationDegrees * wave);
        ApplyCounterbone(neck, offset, scale);
        ApplyCounterbone(leftArm, offset, scale);
        ApplyCounterbone(rightArm, offset, scale);
    }

    private void ApplyFaceBreath(Transform rig, float inhale)
    {
        mouthPose ??= new Bone();
        nosePose ??= new Bone();
        mouthPose.bone = mouth;
        nosePose.bone = nose;
        mouthPose.Apply(rig.up * (inhale * Mathf.Max(0f, mouthLift)), new Vector3(
            Mathf.Max(0.01f, 1f - inhale * Mathf.Clamp01(mouthCompression.x)),
            Mathf.Max(0.01f, 1f - inhale * Mathf.Clamp01(mouthCompression.y)), 1f),
            mouthRotationDegrees * inhale);
        var noseScale = 1f + inhale * Mathf.Clamp01(noseExpansion);
        nosePose.Apply(rig.up * (inhale * Mathf.Max(0f, noseLift)),
            new Vector3(noseScale, noseScale, 1f), noseRotationDegrees * inhale);
    }

    private void ApplyCounterbone(Bone counterbone, Vector3 offset, float scale)
    {
        Transform bone = counterbone.bone;
        if (!bone || bone == belly || bone == spine) return;
        bool inheritsScale = spine && bone.IsChildOf(spine);
        counterbone.Apply(-offset * counterbone.compensation, inheritsScale ? 1f / scale : 1f);
    }

    internal void Restore()
    {
        nosePose?.Restore();
        mouthPose?.Restore();
        rightArm.Restore();
        leftArm.Restore();
        neck.Restore();
        spinePose?.Restore();
        bellyPose?.Restore();
    }
}
