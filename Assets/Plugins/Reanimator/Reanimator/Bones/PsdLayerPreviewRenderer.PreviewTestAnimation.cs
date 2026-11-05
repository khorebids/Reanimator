#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [SerializeField] private bool previewTestAnim;
    [SerializeField] private PreviewTestAnimMode previewTestAnimMode = PreviewTestAnimMode.Simple;
    [SerializeField] private float previewTestAnimPower = 10f;
    [SerializeField] private float previewTestAnimCyclicDepthOffset = 0.25f;
    [SerializeField] private AnimationCurve previewTestAnimHierarchyCurve;
    [SerializeField] private AnimationCurve previewTestAnimCurve;

    [NonSerialized] private bool previewTestAnimApplied;
    [NonSerialized] private readonly List<Quaternion> previewTestAnimSavedRotations = new List<Quaternion>();
    [NonSerialized] private float previewTestAnimLastSample = float.NaN;
    [NonSerialized] private PreviewTestAnimMode previewTestAnimLastMode;
    [NonSerialized] private float previewTestAnimLastCyclicDepthOffset = float.NaN;

    public bool PreviewTestAnimEnabled
    {
        get => previewTestAnim;
        set => previewTestAnim = value;
    }

    public float PreviewTestAnimPower
    {
        get => previewTestAnimPower;
        set => previewTestAnimPower = value;
    }

    public PreviewTestAnimMode PreviewTestAnimPlaybackMode
    {
        get => previewTestAnimMode;
        set => previewTestAnimMode = value;
    }

    public float PreviewTestAnimCyclicDepthOffset
    {
        get => previewTestAnimCyclicDepthOffset;
        set => previewTestAnimCyclicDepthOffset = Mathf.Max(0f, value);
    }

    public AnimationCurve PreviewTestAnimHierarchyCurve
    {
        get
        {
            if (previewTestAnimHierarchyCurve == null || previewTestAnimHierarchyCurve.length == 0)
                previewTestAnimHierarchyCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            return previewTestAnimHierarchyCurve;
        }
        set => previewTestAnimHierarchyCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public AnimationCurve PreviewTestAnimCurve
    {
        get
        {
            if (previewTestAnimCurve == null || previewTestAnimCurve.length == 0)
                previewTestAnimCurve = CreateDefaultPreviewTestAnimCurve();
            return previewTestAnimCurve;
        }
        set => previewTestAnimCurve = value ?? CreateDefaultPreviewTestAnimCurve();
    }

    public void StopPreviewTestAnimation()
    {
        RestorePreviewTestAnimationPose();
    }

    private void UpdatePreviewTestAnimation(bool active)
    {
        if (!active || bones == null || bones.Count == 0)
        {
            RestorePreviewTestAnimationPose();
            return;
        }

        CachePreviewTestAnimationPoseIfNeeded();

        var simpleMode = previewTestAnimMode == PreviewTestAnimMode.Simple;
        var sampleTime = simpleMode
            ? Mathf.PingPong((float)EditorApplication.timeSinceStartup * 0.5f, 1f)
            : Mathf.Repeat((float)EditorApplication.timeSinceStartup * 0.5f, 1f);
        if (previewTestAnimApplied &&
            previewTestAnimMode == previewTestAnimLastMode &&
            Mathf.Abs(previewTestAnimCyclicDepthOffset - previewTestAnimLastCyclicDepthOffset) <= 1e-5f &&
            Mathf.Abs(sampleTime - previewTestAnimLastSample) <= 1e-5f)
            return;

        var maxParentCount = 0;
        for (var i = 0; i < bones.Count; i++)
            maxParentCount = Mathf.Max(maxParentCount, GetPreviewTestAnimationParentCount(i));

        var power = previewTestAnimPower;
        var changed = !previewTestAnimApplied;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var baseRotation = bone.initialRotation.Equals(default(Quaternion)) ? bone.rotation : bone.initialRotation;
            var parentCount = GetPreviewTestAnimationParentCount(i);
            var hierarchyT = maxParentCount > 0 ? parentCount / (float)maxParentCount : 1f;
            var hierarchyValue = PreviewTestAnimHierarchyCurve.Evaluate(hierarchyT);
            var animTime = simpleMode
                ? sampleTime
                : Mathf.Repeat(sampleTime + parentCount * previewTestAnimCyclicDepthOffset, 1f);
            var animValue = PreviewTestAnimCurve.Evaluate(animTime);
            var deltaDegrees = animValue * power * hierarchyValue;
            var targetRotation = baseRotation * Quaternion.AngleAxis(deltaDegrees, Vector3.forward);

            if (!changed && Quaternion.Angle(bone.rotation, targetRotation) > 1e-4f)
                changed = true;

            bone.rotation = targetRotation;
            bones[i] = bone;
        }

        previewTestAnimApplied = true;
        previewTestAnimLastSample = sampleTime;
        previewTestAnimLastMode = previewTestAnimMode;
        previewTestAnimLastCyclicDepthOffset = previewTestAnimCyclicDepthOffset;

        if (!changed)
            return;

        RecomputeBoneWorldTransforms();
        InvalidateBonePose();
        bonePoseAtRest = IsCurrentBonePoseRest();
    }

    private void CachePreviewTestAnimationPoseIfNeeded()
    {
        if (previewTestAnimApplied)
            return;

        previewTestAnimSavedRotations.Clear();
        for (var i = 0; i < bones.Count; i++)
            previewTestAnimSavedRotations.Add(bones[i].rotation);
    }

    private void RestorePreviewTestAnimationPose()
    {
        if (!previewTestAnimApplied || bones == null || bones.Count == 0)
            return;

        var count = Mathf.Min(bones.Count, previewTestAnimSavedRotations.Count);
        for (var i = 0; i < count; i++)
        {
            var bone = bones[i];
            bone.rotation = previewTestAnimSavedRotations[i];
            bones[i] = bone;
        }

        previewTestAnimSavedRotations.Clear();
        previewTestAnimApplied = false;
        previewTestAnimLastSample = float.NaN;
        previewTestAnimLastCyclicDepthOffset = float.NaN;

        RecomputeBoneWorldTransforms();
        InvalidateBonePose();
    }

    private int GetPreviewTestAnimationParentCount(int boneIndex)
    {
        if (bones == null || boneIndex < 0 || boneIndex >= bones.Count)
            return 0;

        var count = 0;
        var current = bones[boneIndex].parentId;
        var guard = 0;
        while (current >= 0 && current < bones.Count && guard++ < bones.Count)
        {
            count++;
            current = bones[current].parentId;
        }

        return count;
    }

    private static AnimationCurve CreateDefaultPreviewTestAnimCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.25f, 1f),
            new Keyframe(0.5f, 0f),
            new Keyframe(0.75f, -1f),
            new Keyframe(1f, 0f));
    }
}
#endif
