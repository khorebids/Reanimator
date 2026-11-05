#if UNITY_EDITOR
using UnityEngine;

internal static class ReanimatorPivotAlgorithms
{
    public static Vector2 GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset preset)
    {
        switch (preset)
        {
            case PsdLayerPreviewRenderer.PivotPreset.Center: return new Vector2(0.5f, 0.5f);
            case PsdLayerPreviewRenderer.PivotPreset.TopLeft: return new Vector2(0f, 1f);
            case PsdLayerPreviewRenderer.PivotPreset.TopCenter: return new Vector2(0.5f, 1f);
            case PsdLayerPreviewRenderer.PivotPreset.TopRight: return new Vector2(1f, 1f);
            case PsdLayerPreviewRenderer.PivotPreset.LeftCenter: return new Vector2(0f, 0.5f);
            case PsdLayerPreviewRenderer.PivotPreset.RightCenter: return new Vector2(1f, 0.5f);
            case PsdLayerPreviewRenderer.PivotPreset.BottomLeft: return new Vector2(0f, 0f);
            case PsdLayerPreviewRenderer.PivotPreset.BottomCenter: return new Vector2(0.5f, 0f);
            case PsdLayerPreviewRenderer.PivotPreset.BottomRight: return new Vector2(1f, 0f);
            default: return new Vector2(0.5f, 0.5f);
        }
    }
}
#endif
