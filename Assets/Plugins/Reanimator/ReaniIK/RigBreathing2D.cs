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

        [NonSerialized] private Transform appliedBone;
        [NonSerialized] private Vector3 originalPosition, originalScale;
        [NonSerialized] private Vector3 appliedPosition, appliedScale;

        internal void Apply(Vector3 worldOffset, float scale)
        {
            if (!bone) return;
            appliedBone = bone;
            originalPosition = bone.localPosition;
            originalScale = bone.localScale;
            appliedPosition = originalPosition +
                (bone.parent ? bone.parent.InverseTransformVector(worldOffset) : worldOffset);
            appliedScale = originalScale * scale;
            bone.localPosition = appliedPosition;
            bone.localScale = appliedScale;
        }

        internal void Restore()
        {
            if (!appliedBone) return;
            if (appliedBone.localPosition == appliedPosition) appliedBone.localPosition = originalPosition;
            if (appliedBone.localScale == appliedScale) appliedBone.localScale = originalScale;
            appliedBone = null;
        }
    }

    public bool enabled;
    [Min(0.1f)] public float period = 3f;
    [Min(0f)] public float height = 0.08f;
    [Range(0f, 0.5f)] public float spineExpansion = 0.025f;
    public Transform belly;
    public Transform spine;
    public Bone neck = new Bone();
    public Bone leftArm = new Bone();
    public Bone rightArm = new Bone();

    [NonSerialized] private float phase;
    [NonSerialized] private Bone bellyPose;
    [NonSerialized] private Bone spinePose;

    internal void Evaluate(Transform rig, float deltaTime)
    {
        if (!enabled)
        {
            phase = 0f;
            return;
        }
        phase = Mathf.Repeat(phase + Mathf.Max(0f, deltaTime) / Mathf.Max(0.1f, period), 1f);
        float wave = Mathf.Sin(phase * Mathf.PI * 2f);
        Vector3 offset = rig.up * (height * wave);
        float scale = 1f + Mathf.Clamp(spineExpansion, 0f, 0.5f) * wave;
        bellyPose ??= new Bone();
        spinePose ??= new Bone();
        bellyPose.bone = belly;
        spinePose.bone = spine;
        bellyPose.Apply(offset, 1f);
        spinePose.Apply(Vector3.zero, scale);
        ApplyCounterbone(neck, offset, scale);
        ApplyCounterbone(leftArm, offset, scale);
        ApplyCounterbone(rightArm, offset, scale);
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
        rightArm.Restore();
        leftArm.Restore();
        neck.Restore();
        spinePose?.Restore();
        bellyPose?.Restore();
    }
}
