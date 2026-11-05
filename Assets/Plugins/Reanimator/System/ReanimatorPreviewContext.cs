#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

internal sealed class ReanimatorPreviewContext
{
    public ReanimatorPreviewContext(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> drawList,
        PsdPreviewVisibilityOptions visibility)
    {
        LocalRect = localRect;
        Coordinates = coordinates;
        DrawList = drawList;
        Visibility = visibility;
    }

    public Rect LocalRect { get; }
    public PsdCoordinateSpace Coordinates { get; }
    public IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> DrawList { get; }
    public PsdPreviewVisibilityOptions Visibility { get; }
}
#endif
