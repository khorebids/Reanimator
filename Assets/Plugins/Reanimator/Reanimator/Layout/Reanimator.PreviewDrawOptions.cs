#if UNITY_EDITOR
using UnityEditor;

public partial class Reanimator : EditorWindow
{
    private PsdPreviewDrawOptions CreatePreviewDrawOptions()
    {
        var options = PsdPreviewDrawOptions.CreateDefault();
        var spriteMode = previewMode == PreviewMode.Sprite;
        var comfimatorMode = previewMode == PreviewMode.Comfimator;

        options.Navigator = previewNavigator;
        options.Api = Api;
        options.ActivePreviewModule = GetActivePreviewModule();
        options.ActiveDebugPreviewModule = GetActiveDebugPreviewModule();
        options.SpriteAtlasMode = spriteMode;
        options.SpriteAtlasGeometryMode = spriteMode && spriteSheetGeometryMode;
        options.AllowSpriteSelection = !comfimatorMode;
        options.LockSpriteSelection = !comfimatorMode && previewSpriteSelectionLocked;
        options.IsolateSelectedSprite = !comfimatorMode && previewSelectedSpriteIsolated;
        options.DrawBones = !spriteMode && !comfimatorMode;
        options.IsolateBoneModification =
            !spriteMode && !comfimatorMode &&
            bottomToolMode == ReanimatorToolMode.EditBone &&
            isolateBoneModification;
        options.AutoGeometryQuadNetPreviewActive =
            !spriteMode && !comfimatorMode &&
            bottomToolMode == ReanimatorToolMode.AutoGeometry &&
            autoGeometryMode == ReanimatorAutoGeometryMode.QuadNet;
        options.AutoGeometryQuadDensity = autoGeometryQuadDensity;
        options.AutoGeometryQuadRotationDegrees = autoGeometryQuadRotationDegrees;
        options.AutoGeometryReanimaxHardMaskPreviewActive =
            !spriteMode && !comfimatorMode &&
            bottomToolMode == ReanimatorToolMode.AutoGeometry &&
            autoGeometryMode == ReanimatorAutoGeometryMode.Reanimax &&
            autoGeometryReanimaxMode == ReanimatorReanimaxGeometryMode.Hard &&
            autoGeometryReanimaxHardMaskPreview;
        options.AutoGeometryReanimaxHardEdgePointsPreviewActive =
            !spriteMode && !comfimatorMode &&
            bottomToolMode == ReanimatorToolMode.AutoGeometry &&
            autoGeometryMode == ReanimatorAutoGeometryMode.Reanimax &&
            autoGeometryReanimaxMode == ReanimatorReanimaxGeometryMode.Hard &&
            autoGeometryReanimaxHardEdgePointsPreview;
        options.SuppressGeometryOverlay = options.AutoGeometryReanimaxHardEdgePointsPreviewActive;
        options.AutoGeometryReanimaxHardMaskAlphaThreshold = autoGeometryReanimaxHardMaskAlphaThreshold;
        options.AutoGeometryReanimaxHardEdgePositionFilterPixels = autoGeometryReanimaxHardEdgePositionFilterPixels;
        options.AutoGeometryReanimaxHardEdgeMinTurnDegrees = autoGeometryReanimaxHardEdgeMinTurnDegrees;
        options.AutoGeometryReanimaxHardEdgePostPositionFilterPixels = autoGeometryReanimaxHardEdgePostPositionFilterPixels;
        options.AutoGeometryReanimaxHardEdgeMinDistancePixels = autoGeometryReanimaxHardEdgeMinDistancePixels;
        options.AutoGeometryReanimaxHardNormalMidpointPositionFilterPixels = autoGeometryReanimaxHardNormalMidpointPositionFilterPixels;
        options.AutoGeometryReanimaxHardNormalInsetPixels = autoGeometryReanimaxHardNormalInsetPixels;
        options.AutoGeometryReanimaxHardNormalInsetPositionFilterPixels = autoGeometryReanimaxHardNormalInsetPositionFilterPixels;
        options.DebugOverlayMode = visibilityBone ? PsdPreviewDebugOverlayMode.Bone : PsdPreviewDebugOverlayMode.Sprite;
        options.PreviewTestAnimationActive =
            !spriteMode && !comfimatorMode &&
            bottomToolMode == ReanimatorToolMode.PreviewPose &&
            layerPreviewRenderer != null &&
            layerPreviewRenderer.PreviewTestAnimEnabled &&
            ShouldRunContinuousPreviewUpdates();
        options.Visibility.SpriteAlpha = spriteVisibilityValue;
        options.Visibility.BoneAlpha = boneVisibilityValue;
        options.Visibility.VertexAlpha = vertexVisibilityValue;

        var jointStartLongPointsOffset = layerPreviewRenderer != null ? layerPreviewRenderer.JointStartLongPointsOffset : 12f;
        var jointEndLongPointsOffset = layerPreviewRenderer != null ? layerPreviewRenderer.JointEndLongPointsOffset : 12f;
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            options,
            bottomToolMode,
            spriteMode,
            radiusCullRadius,
            radiusCullRemoveContourVertices,
            radiusCullRemoveInnerVertices,
            limbsUseRadiusCull && !limbsBoneMode,
            jointUseRadiusCull,
            jointCPairOffset,
            jointLongPoints,
            jointStartLongPointsOffset,
            jointEndLongPointsOffset);

        if (options.Tools.Limb && limbsBoneMode)
        {
            options.Tools.Limb = false;
            options.Tools.LimbBone = true;
            options.Tools.LimbBoneChild = limbsChildBoneIndex;
            options.Tools.LimbBoneCull = limbsUseRadiusCull;
            options.AllowBoneTransformEditing = false;
            options.AllowBonePoseEditing = false;
        }

        options.Joint.AlgorithmMode = jointAlgorithmMode;
        options.Joint.WeightCurve = jointV2FirstIterationWeightCurve;
        options.Joint.SpecialWeightCurve = jointV2SpecialWeightCurve;
        options.Joint.SpecialJointDistance = UnityEngine.Mathf.Clamp01(jointV2SpecialJointDistance);
        options.Joint.BoneBaseOffset = UnityEngine.Mathf.Clamp01(jointV2BoneBaseOffset);
        if (options.Tools.Joint && options.Joint.AlgorithmMode == 1)
        {
            options.RadiusCull.JointOverlayActive = false;
            options.RadiusCull.RemoveInnerVertices = false;
        }

        if (layerPreviewRenderer != null)
            layerPreviewRenderer.CurveUseAbsoluteGeometry = options.Tools.Curve && curveSpaceMode == 1;

        if (comfimatorMode)
        {
            options.DrawBones = false;
            options.AllowBonePoseEditing = false;
            options.AllowBoneTransformEditing = false;
            options.AllowBoneLengthEditing = false;
            options.ActivePreviewModule = null;
            options.ActiveDebugPreviewModule = null;
            options.Tools.Clear();
        }

        return options;
    }

    private IReanimatorPreviewModule GetActivePreviewModule()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        if (modules == null)
            return null;

        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i] is IReanimatorPreviewModule previewModule && previewModule.IsActive(Api))
                return previewModule;
        }

        return null;
    }

    private bool ShouldDrawInfluenceOverlayForCurrentMode()
    {
        switch (ReanimatorToolModeAlgorithms.NormalizeToolMode(bottomToolMode))
        {
            case ReanimatorToolMode.WeightSlider:
            case ReanimatorToolMode.WeightBrush:
            case ReanimatorToolMode.Curve:
            case ReanimatorToolMode.Projector:
            case ReanimatorToolMode.BoneInfluence:
            case ReanimatorToolMode.SpriteInfluence:
            case ReanimatorToolMode.VolumeCurve:
                return true;
            default:
                return false;
        }
    }

    private IReanimatorDebugPreviewModule GetActiveDebugPreviewModule()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        if (modules == null)
            return null;

        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i] is IReanimatorDebugPreviewModule debugModule && debugModule.IsActive(Api))
                return debugModule;
        }

        return null;
    }
}
#endif

