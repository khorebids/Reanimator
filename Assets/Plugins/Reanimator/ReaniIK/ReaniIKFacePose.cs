using System;
using UnityEngine;

[Serializable]
public sealed class ReaniIKFacePose
{
    [Min(0f)] public float headTurnDegrees = 45f;
    public Vector2 gazeDistance = new Vector2(0.15f, 0.17f);
    [Min(0f), Tooltip("Upward travel from the middle of the height slider.")]
    public float upperLipLift = 0.2f;
    [Min(0f), Tooltip("Downward travel from the middle of the height slider.")]
    public float upperLipDrop = 0.2f;
    [Range(0f, 0.99f)] public float upperLipWidth = 0.5f;
    [Min(0f), Tooltip("Upward travel from the middle of the height slider.")]
    public float lowerLipLift = 0.2f;
    [Min(0f), Tooltip("Downward travel from the middle of the height slider.")]
    public float lowerLipDrop = 0.2f;
    [Range(0f, 0.99f)] public float lowerLipWidth = 0.5f;
    [Min(0f)] public float lipCornersTravel = 0.2f;
    public float eyelidTravel = 0.23f;

    [HideInInspector] public float headTurn;
    [HideInInspector] public Vector2 gaze;
    [HideInInspector] public float upperLipHeight;
    [HideInInspector] public float upperLipSize;
    [HideInInspector] public float lowerLipHeight;
    [HideInInspector] public float lowerLipSize;
    [HideInInspector] public float lipCornersHeight;
    [HideInInspector] public float eyelidClosure;

    [NonSerialized] private RigBreathing2D.Bone headPose;
    [NonSerialized] private RigBreathing2D.Bone eyesPose;
    [NonSerialized] private RigBreathing2D.Bone upperLipPose;
    [NonSerialized] private RigBreathing2D.Bone lowerLipPose;
    [NonSerialized] private RigBreathing2D.Bone lipCornersPose;
    [NonSerialized] private RigBreathing2D.Bone eyelidsPose;

    public Transform Head => headPose?.bone;
    public Vector3 HeadRestRootPosition { get; private set; }
    public bool HasEyes => eyesPose?.bone != null;
    public bool HasUpperLip => upperLipPose?.bone != null;
    public bool HasLowerLip => lowerLipPose?.bone != null;
    public bool HasLipCorners => lipCornersPose?.bone != null;
    public bool HasEyelids => eyelidsPose?.bone != null;

    public void ResetControls()
    {
        headTurn = 0f;
        gaze = Vector2.zero;
        upperLipHeight = 0f;
        upperLipSize = 0f;
        lowerLipHeight = 0f;
        lowerLipSize = 0f;
        eyelidClosure = 0f;
        lipCornersHeight = 0f;
    }

    internal void DiscoverBones(Transform rig)
    {
        Restore();
        headPose = new RigBreathing2D.Bone();
        eyesPose = new RigBreathing2D.Bone();
        upperLipPose = new RigBreathing2D.Bone();
        lowerLipPose = new RigBreathing2D.Bone();
        eyelidsPose = new RigBreathing2D.Bone();
        lipCornersPose = new RigBreathing2D.Bone();
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
        {
            var pose = bone.name.ToLowerInvariant() switch
            {
                "head" => headPose,
                "eyes" => eyesPose,
                "upper_lip" => upperLipPose,
                "lower_lip" => lowerLipPose,
                "eyelids" => eyelidsPose,
                "lip_corners" => lipCornersPose,
                _ => null
            };
            if (pose != null && !pose.bone)
                pose.bone = bone;
        }
        HeadRestRootPosition = Head ? rig.InverseTransformPoint(Head.position) : Vector3.zero;
    }

    internal void Apply(Transform rig)
    {
        var turn = Mathf.Clamp(headTurn, -1f, 1f) * headTurnDegrees;
        if (turn != 0f)
            headPose?.Apply(Vector3.zero, Vector3.one, turn);

        var offset = Vector2.Scale(Vector2.ClampMagnitude(gaze, 1f), gazeDistance);
        if (offset != Vector2.zero)
            eyesPose?.Apply(rig.right * offset.x + rig.up * offset.y, 1f);

        ApplyLip(upperLipPose, RemapLipHeight(upperLipHeight, upperLipDrop, upperLipLift), upperLipSize, upperLipWidth);
        ApplyLip(lowerLipPose, RemapLipHeight(lowerLipHeight, lowerLipDrop, lowerLipLift), lowerLipSize, lowerLipWidth);
        if (lipCornersHeight != 0f)
            lipCornersPose?.Apply(rig.up * (Mathf.Clamp(lipCornersHeight, -1f, 1f) * lipCornersTravel), 1f);
        if (eyelidClosure != 0f)
            eyelidsPose?.Apply(-rig.up * (Mathf.Clamp(eyelidClosure, -1f, 1f) * eyelidTravel), 1f);

        void ApplyLip(RigBreathing2D.Bone pose, float height, float size, float width)
        {
            if (pose == null || !pose.bone || (height == 0f && size == 0f))
                return;
            var factor = Mathf.Max(0.01f, 1f + Mathf.Clamp(size, -1f, 1f) * width);
            var horizontalUsesX = Mathf.Abs(Vector3.Dot(pose.bone.right, rig.right)) >=
                                  Mathf.Abs(Vector3.Dot(pose.bone.up, rig.right));
            var scale = horizontalUsesX ? new Vector3(factor, 1f, 1f) : new Vector3(1f, factor, 1f);
            pose.Apply(rig.up * height, scale);
        }
    }

    private static float RemapLipHeight(float height, float drop, float lift) =>
        Mathf.Clamp01(height) * Mathf.Max(0f, lift) - Mathf.Clamp01(-height) * Mathf.Max(0f, drop);

    internal void Restore()
    {
        lipCornersPose?.Restore();
        eyelidsPose?.Restore();
        lowerLipPose?.Restore();
        upperLipPose?.Restore();
        eyesPose?.Restore();
        headPose?.Restore();
    }
}
