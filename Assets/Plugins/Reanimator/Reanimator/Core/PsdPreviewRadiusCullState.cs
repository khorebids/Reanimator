#if UNITY_EDITOR
using UnityEngine;

internal sealed class PsdPreviewRadiusCullState
{
    public float InputRadius { get; private set; }
    public float PreviewRadius { get; private set; }
    public bool InputActive { get; private set; }
    public bool OverlayActive { get; private set; }
    public bool LimbHandleInputActive { get; private set; }
    public bool LimbPreviewActive { get; private set; }
    public bool JointPreviewActive { get; private set; }
    public bool SyncToActiveLimbLine { get; private set; }

    public bool ShouldDrawGeometryPreview(bool hasSelectedLayer)
    {
        return hasSelectedLayer && (InputActive || LimbPreviewActive || JointPreviewActive);
    }

    public static PsdPreviewRadiusCullState Create(
        PsdPreviewDrawOptions options,
        bool hasRadiusOverride,
        float radiusOverride)
    {
        var radius = hasRadiusOverride ? radiusOverride : options.RadiusCull.Radius;
        radius = Mathf.Max(0.01f, radius);

        return new PsdPreviewRadiusCullState
        {
            InputRadius = radius,
            PreviewRadius = radius,
            InputActive = options.Tools.RadiusCull,
            OverlayActive = options.Tools.RadiusCull ||
                            (options.Tools.Limb && options.RadiusCull.LimbOverlayActive),
            LimbHandleInputActive = options.Tools.Limb &&
                                    options.RadiusCull.LimbOverlayActive &&
                                    options.RadiusCull.LimbHandleInputActive,
            LimbPreviewActive = options.Tools.Limb && options.RadiusCull.LimbOverlayActive,
            JointPreviewActive = options.Tools.Joint && options.RadiusCull.JointOverlayActive,
            SyncToActiveLimbLine = options.Tools.Limb && options.RadiusCull.LimbOverlayActive
        };
    }
}
#endif
