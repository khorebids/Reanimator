#if UNITY_EDITOR
using UnityEngine;

internal sealed partial class ReanimatorVolumeCurveModule : IReanimatorDebugPreviewModule
{
    public void DrawDebugPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        var renderer = api != null ? api.PreviewRenderer : null;
        if (renderer == null || context == null || context.Coordinates == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        var curves = renderer.VolumeCurves;
        if (curves == null || curves.Count == 0)
            return;

        for (var curveIndex = 0; curveIndex < curves.Count; curveIndex++)
        {
            var curve = curves[curveIndex];
            if (curve == null || curve.points == null || curve.points.Count == 0)
                continue;

            var selected = curveIndex == renderer.SelectedVolumeCurveIndex;
            var hovered = curveIndex == hoveredCurveIndex;
            DrawCurveDebugLabels(context.Coordinates, curve, curveIndex, selected, hovered);
        }
    }

    private static void DrawCurveDebugLabels(
        PsdCoordinateSpace coordinates,
        PsdLayerPreviewRenderer.VolumeCurve curve,
        int curveIndex,
        bool selected,
        bool hovered)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < curve.points.Count; i++)
        {
            var point = curve.points[i];
            if (point == null)
                continue;

            min = Vector2.Min(min, point.positionDoc);
            max = Vector2.Max(max, point.positionDoc);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return;

        var curveName = string.IsNullOrWhiteSpace(curve.name) ? $"Curve {curveIndex + 1}" : curve.name;
        var header = $"C{curveIndex + 1} {curveName}\npoints={curve.points.Count} closed={curve.closed}";
        var headerColor = hovered ? ReanimatorDebugPreviewGui.Hot : selected ? ReanimatorDebugPreviewGui.Selected : ReanimatorDebugPreviewGui.Neutral;
        ReanimatorDebugPreviewGui.DrawLabel(coordinates.DocumentToView((min + max) * 0.5f) + new Vector2(10f, -34f), header, headerColor);

        for (var i = 0; i < curve.points.Count; i++)
        {
            var point = curve.points[i];
            if (point == null)
                continue;

            var label = $"P{i + 1} {point.inType}->{point.outType}\n{point.positionDoc.x:F1}, {point.positionDoc.y:F1}";
            ReanimatorDebugPreviewGui.DrawLabel(coordinates.DocumentToView(point.positionDoc) + new Vector2(10f, 8f), label, GetPointColor(point.outType, 1f));
        }

        var segmentCount = curve.closed ? curve.points.Count : curve.points.Count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var a = curve.points[i];
            var b = curve.points[(i + 1) % curve.points.Count];
            if (a == null || b == null)
                continue;

            var middle = ReanimatorVolumeCurveAlgorithms.EvaluateSegment(curve, i, 0.5f);
            ReanimatorDebugPreviewGui.DrawLabel(coordinates.DocumentToView(middle) + new Vector2(6f, -18f), $"S{i + 1} {a.outType}", ReanimatorDebugPreviewGui.Segment);
        }
    }
}
#endif
