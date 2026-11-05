#if UNITY_EDITOR
internal enum ReanimatorToolCancelTarget
{
    None,
    Elbow,
    Limbs,
    Joint,
    Boob,
    Curve,
    CreateEdge,
    EditGeometry,
    RadiusCull,
    WeightSlider,
    WeightBrush,
    VolumeCurve
}

internal static class ReanimatorToolModeAlgorithms
{
    public static int NormalizeHoveredBoneIndex(int boneIndex, int boneCount)
    {
        return boneIndex >= 0 && boneIndex < boneCount ? boneIndex : -1;
    }

    public static ReanimatorToolMode NormalizeToolMode(ReanimatorToolMode mode)
    {
        switch (mode)
        {
            case ReanimatorToolMode.DeprecatedCreateVertex:
                return ReanimatorToolMode.EditGeometry;
            case ReanimatorToolMode.SetPivot:
                return ReanimatorToolMode.PreviewPose;
            default:
                return mode;
        }
    }

    public static ReanimatorToolMode NormalizeDefaultToolMode(ReanimatorToolMode mode)
    {
        mode = NormalizeToolMode(mode);
        return mode == ReanimatorToolMode.NoSettings ? ReanimatorToolMode.PreviewPose : mode;
    }

    public static bool IsEditGeometrySelectionToolMode(ReanimatorToolMode mode)
    {
        mode = NormalizeToolMode(mode);
        return mode == ReanimatorToolMode.EditGeometry ||
               mode == ReanimatorToolMode.CreateEdge ||
               mode == ReanimatorToolMode.SplitEdge ||
               mode == ReanimatorToolMode.BoneInfluence ||
               mode == ReanimatorToolMode.SpriteInfluence;
    }

    public static bool AllowsBonePoseEditing(ReanimatorToolMode mode)
    {
        mode = NormalizeToolMode(mode);
        return mode != ReanimatorToolMode.CreateBone &&
               mode != ReanimatorToolMode.EditBone;
    }

    public static bool AllowsBoneTransformEditing(ReanimatorToolMode mode)
    {
        mode = NormalizeToolMode(mode);
        return mode != ReanimatorToolMode.CreateBone;
    }

    public static bool DeformsSpritesWithBonePose(ReanimatorToolMode mode)
    {
        return AllowsBonePoseEditing(mode);
    }

    public static bool ShouldCancelOnTransition(
        ReanimatorToolMode currentMode,
        ReanimatorToolMode nextMode,
        ReanimatorToolCancelTarget target)
    {
        currentMode = NormalizeToolMode(currentMode);
        nextMode = NormalizeToolMode(nextMode);

        if (currentMode == nextMode)
            return false;

        if (target == ReanimatorToolCancelTarget.EditGeometry)
            return IsEditGeometrySelectionToolMode(currentMode) && !IsEditGeometrySelectionToolMode(nextMode);

        return GetDirectCancelTarget(currentMode) == target;
    }

    private static ReanimatorToolCancelTarget GetDirectCancelTarget(ReanimatorToolMode mode)
    {
        switch (NormalizeToolMode(mode))
        {
            case ReanimatorToolMode.Elbow:
                return ReanimatorToolCancelTarget.Elbow;
            case ReanimatorToolMode.Limbs:
                return ReanimatorToolCancelTarget.Limbs;
            case ReanimatorToolMode.Joint:
                return ReanimatorToolCancelTarget.Joint;
            case ReanimatorToolMode.Boob:
                return ReanimatorToolCancelTarget.Boob;
            case ReanimatorToolMode.Curve:
                return ReanimatorToolCancelTarget.Curve;
            case ReanimatorToolMode.CreateEdge:
                return ReanimatorToolCancelTarget.CreateEdge;
            case ReanimatorToolMode.RadiusCull:
                return ReanimatorToolCancelTarget.RadiusCull;
            case ReanimatorToolMode.WeightSlider:
                return ReanimatorToolCancelTarget.WeightSlider;
            case ReanimatorToolMode.WeightBrush:
                return ReanimatorToolCancelTarget.WeightBrush;
            case ReanimatorToolMode.VolumeCurve:
                return ReanimatorToolCancelTarget.VolumeCurve;
            default:
                return ReanimatorToolCancelTarget.None;
        }
    }
}
#endif
