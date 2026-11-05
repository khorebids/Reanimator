#if UNITY_EDITOR
internal static class ReanimatorPreviewDrawOptionsAlgorithms
{
    public static void ApplyToolMode(
        PsdPreviewDrawOptions options,
        ReanimatorToolMode mode,
        bool spriteMode,
        float radiusCullRadius,
        bool radiusCullRemoveContourVertices,
        bool radiusCullRemoveInnerVertices,
        bool limbsUseRadiusCull,
        bool jointUseRadiusCull,
        float jointCPairOffset,
        int jointLongPoints,
        float jointStartLongPointsOffset,
        float jointEndLongPointsOffset)
    {
        if (options == null)
            return;

        mode = ReanimatorToolModeAlgorithms.NormalizeToolMode(mode);

        options.SpriteAtlasMode = spriteMode;
        options.DrawBones = !spriteMode;
        options.AllowBonePoseEditing = !spriteMode && ReanimatorToolModeAlgorithms.AllowsBonePoseEditing(mode);
        options.AllowBoneTransformEditing = !spriteMode && ReanimatorToolModeAlgorithms.AllowsBoneTransformEditing(mode);
        options.AllowBoneLengthEditing = !spriteMode && mode == ReanimatorToolMode.EditBone;
        options.DeformSpritesWithBonePose = !spriteMode && ReanimatorToolModeAlgorithms.DeformsSpritesWithBonePose(mode);
        options.Tools.Clear();

        if (!spriteMode)
            ApplyActiveTool(options.Tools, mode);

        options.RadiusCull.Radius = radiusCullRadius;
        options.RadiusCull.RemoveContourVertices = mode == ReanimatorToolMode.Joint
            ? false
            : radiusCullRemoveContourVertices;
        options.RadiusCull.RemoveInnerVertices = mode == ReanimatorToolMode.Joint
            ? jointUseRadiusCull
            : radiusCullRemoveInnerVertices;
        options.RadiusCull.JointOverlayActive = mode == ReanimatorToolMode.Joint;
        options.RadiusCull.LimbOverlayActive = mode == ReanimatorToolMode.Limbs && limbsUseRadiusCull;
        options.RadiusCull.LimbHandleInputActive = mode == ReanimatorToolMode.Limbs && limbsUseRadiusCull;

        options.Joint.CPairOffset = jointCPairOffset;
        options.Joint.LongPoints = jointLongPoints;
        options.Joint.StartLongPointsOffset = jointStartLongPointsOffset;
        options.Joint.EndLongPointsOffset = jointEndLongPointsOffset;
    }

    private static void ApplyActiveTool(PsdPreviewToolOptions tools, ReanimatorToolMode mode)
    {
        switch (mode)
        {
            case ReanimatorToolMode.AutoGeometry:
                tools.AutoGeometry = true;
                break;
            case ReanimatorToolMode.Elbow:
                tools.Elbow = true;
                break;
            case ReanimatorToolMode.Limbs:
                tools.Limb = true;
                break;
            case ReanimatorToolMode.Boob:
                tools.Boob = true;
                break;
            case ReanimatorToolMode.Curve:
                tools.Curve = true;
                break;
            case ReanimatorToolMode.WeightSlider:
                tools.WeightSlider = true;
                break;
            case ReanimatorToolMode.CreateEdge:
                tools.CreateEdge = true;
                break;
            case ReanimatorToolMode.SplitEdge:
                tools.SplitEdge = true;
                break;
            case ReanimatorToolMode.EditGeometry:
                tools.EditGeometry = true;
                break;
            case ReanimatorToolMode.RadiusCull:
                tools.RadiusCull = true;
                break;
            case ReanimatorToolMode.Joint:
                tools.Joint = true;
                break;
            case ReanimatorToolMode.BoneInfluence:
                tools.BoneInfluence = true;
                break;
            case ReanimatorToolMode.SpriteInfluence:
                tools.SpriteInfluence = true;
                break;
        }
    }
}
#endif
