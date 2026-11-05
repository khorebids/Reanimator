#if UNITY_EDITOR
public enum PsdPreviewDebugOverlayMode
{
    Sprite = 0,
    Bone = 1
}

public sealed class PsdPreviewDrawOptions
{
    public PreviewNavigator Navigator;
    public bool DrawBones = true;
    public bool AllowBonePoseEditing = true;
    public bool AllowBoneTransformEditing = true;
    public bool AllowBoneLengthEditing;
    public bool IsolateBoneModification;
    public bool DeformSpritesWithBonePose = true;
    public bool AllowSpriteSelection = true;
    public bool LockSpriteSelection;
    public bool IsolateSelectedSprite;
    public bool SpriteAtlasMode;
    public bool SpriteAtlasGeometryMode;
    public bool PreviewTestAnimationActive;
    public bool SuppressGeometryOverlay;
    public bool AutoGeometryQuadNetPreviewActive;
    public double AutoGeometryQuadDensity = 5d;
    public float AutoGeometryQuadRotationDegrees;
    public bool AutoGeometryReanimaxHardMaskPreviewActive;
    public bool AutoGeometryReanimaxHardEdgePointsPreviewActive;
    public float AutoGeometryReanimaxHardMaskAlphaThreshold = 0.5f;
    public int AutoGeometryReanimaxHardEdgePositionFilterPixels = 3;
    public float AutoGeometryReanimaxHardEdgeMinTurnDegrees = 13f;
    public int AutoGeometryReanimaxHardEdgePostPositionFilterPixels = 4;
    public float AutoGeometryReanimaxHardEdgeMinDistancePixels = 2.8f;
    public int AutoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30;
    public float AutoGeometryReanimaxHardNormalInsetPixels = 5f;
    public int AutoGeometryReanimaxHardNormalInsetPositionFilterPixels = 5;
    public PsdPreviewDebugOverlayMode DebugOverlayMode = PsdPreviewDebugOverlayMode.Sprite;
    public PsdPreviewVisibilityOptions Visibility { get; private set; }
    public PsdPreviewToolOptions Tools { get; private set; }
    public PsdPreviewRadiusCullOptions RadiusCull { get; private set; }
    public PsdPreviewJointOptions Joint { get; private set; }
    internal Reanimator.SystemApi Api { get; set; }
    internal IReanimatorPreviewModule ActivePreviewModule { get; set; }
    internal IReanimatorDebugPreviewModule ActiveDebugPreviewModule { get; set; }

    private PsdPreviewDrawOptions()
    {
        Visibility = new PsdPreviewVisibilityOptions();
        Tools = new PsdPreviewToolOptions();
        RadiusCull = new PsdPreviewRadiusCullOptions();
        Joint = new PsdPreviewJointOptions();
    }

    public static PsdPreviewDrawOptions CreateDefault()
    {
        return new PsdPreviewDrawOptions();
    }

    public void Sanitize()
    {
        AutoGeometryQuadRotationDegrees = ReanimatorQuadNetAlgorithms.NormalizeQuadNetRotationDegrees(AutoGeometryQuadRotationDegrees);
        AutoGeometryReanimaxHardMaskAlphaThreshold = UnityEngine.Mathf.Clamp01(AutoGeometryReanimaxHardMaskAlphaThreshold);
        AutoGeometryReanimaxHardEdgePositionFilterPixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardEdgePositionFilterPixels, 1, 20);
        AutoGeometryReanimaxHardEdgeMinTurnDegrees = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardEdgeMinTurnDegrees, 0f, 45f);
        AutoGeometryReanimaxHardEdgePostPositionFilterPixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardEdgePostPositionFilterPixels, 1, 20);
        AutoGeometryReanimaxHardEdgeMinDistancePixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardEdgeMinDistancePixels, 0f, 20f);
        AutoGeometryReanimaxHardNormalMidpointPositionFilterPixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardNormalMidpointPositionFilterPixels, 1, 50);
        AutoGeometryReanimaxHardNormalInsetPixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardNormalInsetPixels, 0f, 100f);
        AutoGeometryReanimaxHardNormalInsetPositionFilterPixels = UnityEngine.Mathf.Clamp(AutoGeometryReanimaxHardNormalInsetPositionFilterPixels, 1, 50);
        Visibility.Sanitize();
        RadiusCull.Sanitize();
        Joint.Sanitize();
    }
}
#endif
