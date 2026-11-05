#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal sealed partial class ReanimatorFaceModule : ReanimatorToolModeModule, IReanimatorPreviewModule
{
    public ReanimatorFaceModule()
        : base("face.main", "Geometry", 30, 120, "Face", ReanimatorToolMode.Face, 70, (api, height) => api?.DrawFaceSettings(height))
    {
    }

    public bool AllowBoneInputAfterPreviewInput => true;
    public bool OverridesSelectedGeometryPreview => false;

    public void HandlePreviewInput(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
    }

    public void DrawPreviewOverlay(Reanimator.SystemApi api, ReanimatorPreviewContext context)
    {
        if (api == null || api.ActiveFaceSubtoolIndex != 0 || context == null || context.Coordinates == null)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        var renderer = api.PreviewRenderer;
        if (renderer == null ||
            !renderer.TryGetSelectedLayer(out var layer) ||
            !renderer.TryAnalyzeSelectedFaceAlphaMask(0.5f, out var analysis) ||
            analysis == null ||
            !analysis.HasContour)
            return;

        Handles.BeginGUI();
        var previousColor = Handles.color;
        var hasHeadDirection = renderer.TryGetFaceBoneDirectionInMask("head", layer, analysis, out var headDirection);
        renderer.TryGetFaceGeometryBoundaryInMask(
            layer,
            analysis,
            out var geometryBoundaryVertices,
            out var geometryBoundaryEdgePairs);
        var hasEyelidsPreviewSkin = renderer.TryGetFaceVirtualBoneSkinMatrix(
            "eyelids",
            out var eyelidsPreviewSkinMatrix);
        ReanimatorFaceEyePointExpansion leftExpansion = null;
        ReanimatorFaceEyePointExpansion rightExpansion = null;
        if (hasHeadDirection && analysis.HasLeftHole)
        {
            leftExpansion = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
                analysis,
                analysis.LeftHole,
                api.FaceEyesPointDensity,
                headDirection,
                api.FaceEyesPointOffset,
                api.FaceEyesOutlineOffset,
                geometryBoundaryVertices,
                geometryBoundaryEdgePairs,
                api.FaceEyesWeightCurve);
        }
        if (hasHeadDirection && analysis.HasRightHole)
        {
            rightExpansion = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
                analysis,
                analysis.RightHole,
                api.FaceEyesPointDensity,
                headDirection,
                api.FaceEyesPointOffset,
                api.FaceEyesOutlineOffset,
                geometryBoundaryVertices,
                geometryBoundaryEdgePairs,
                api.FaceEyesWeightCurve);
        }
        ReanimatorFaceAlphaMaskAlgorithms.ApplySharedEyeOutlineCollisionStop(
            api.FaceEyesOutlineOffset,
            leftExpansion,
            rightExpansion);

        if (analysis.HasLeftHole)
        {
            DrawAlphaHoleTarget(context, layer, analysis, analysis.LeftHole.Center, new Color(0.1f, 0.95f, 1f, 1f));
            if (leftExpansion != null)
                DrawAlphaHoleHeadDirectedPoints(context, layer, analysis, leftExpansion, hasEyelidsPreviewSkin, eyelidsPreviewSkinMatrix, new Color(0.1f, 0.95f, 1f, 1f));
        }
        if (analysis.HasRightHole)
        {
            DrawAlphaHoleTarget(context, layer, analysis, analysis.RightHole.Center, new Color(1f, 0.82f, 0.1f, 1f));
            if (rightExpansion != null)
                DrawAlphaHoleHeadDirectedPoints(context, layer, analysis, rightExpansion, hasEyelidsPreviewSkin, eyelidsPreviewSkinMatrix, new Color(1f, 0.82f, 0.1f, 1f));
        }
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private static void DrawAlphaHoleTarget(
        ReanimatorPreviewContext context,
        PsdLayerPreviewRenderer.LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2 maskPoint,
        Color color)
    {
        var viewPoint = FaceMaskPointToView(context, layer, analysis, maskPoint);

        Handles.color = new Color(0f, 0f, 0f, 0.8f);
        Handles.DrawWireDisc(viewPoint, Vector3.forward, 10.5f);
        Handles.DrawWireDisc(viewPoint, Vector3.forward, 5.5f);
        Handles.color = color;
        Handles.DrawWireDisc(viewPoint, Vector3.forward, 9f);
        Handles.DrawWireDisc(viewPoint, Vector3.forward, 4f);
        Handles.DrawAAPolyLine(2f, viewPoint + Vector2.left * 13f, viewPoint + Vector2.right * 13f);
        Handles.DrawAAPolyLine(2f, viewPoint + Vector2.down * 13f, viewPoint + Vector2.up * 13f);
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, 1.8f);
    }

    private static void DrawAlphaHoleHeadDirectedPoints(
        ReanimatorPreviewContext context,
        PsdLayerPreviewRenderer.LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        ReanimatorFaceEyePointExpansion expansion,
        bool applyEyelidsPreviewSkin,
        Matrix4x4 eyelidsPreviewSkinMatrix,
        Color color)
    {
        if (expansion.BaseContour.Length < 3 ||
            expansion.DuplicatePoints.Length < 2 ||
            expansion.OutlineContour.Length != expansion.BaseContour.Length)
            return;

        DrawClosedFaceEyeContour(
            context,
            layer,
            analysis,
            expansion.BaseContour,
            color,
            4.2f,
            2.2f,
            applyEyelidsPreviewSkin,
            eyelidsPreviewSkinMatrix,
            expansion.BasePointWeights);
        DrawOpenFaceEyeContour(
            context,
            layer,
            analysis,
            expansion.DuplicatePoints,
            new Color(0.75f, 1f, 0.35f, 1f),
            3.6f,
            1.8f,
            applyEyelidsPreviewSkin,
            eyelidsPreviewSkinMatrix,
            expansion.DuplicatePointWeights);

        var zeroView = FaceMaskPointToView(context, layer, analysis, expansion.ZeroWeightPoint);
        var centerView = FaceMaskPointToView(context, layer, analysis, expansion.EyeCenter);
        Handles.color = new Color(0.45f, 1f, 0.2f, 0.55f);
        Handles.DrawAAPolyLine(1.2f, centerView, zeroView);
        DrawFaceEyePoint(zeroView, new Color(0.45f, 1f, 0.2f, 1f), 2.4f);
        Handles.Label(zeroView + new Vector2(5f, -8f), "0");

        for (var i = 0; i < expansion.BaseContour.Length; i++)
        {
            if (expansion.Regions[i] == ReanimatorFaceEyePointRegion.Lower)
                continue;
            var baseWeight = i < expansion.BasePointWeights.Length ? expansion.BasePointWeights[i] : 0f;
            var baseView = FaceMaskPointToView(
                context,
                layer,
                analysis,
                expansion.BaseContour[i],
                applyEyelidsPreviewSkin,
                eyelidsPreviewSkinMatrix,
                baseWeight);
            DrawFaceEyePoint(baseView, color, 1.65f);
        }

        for (var i = 0; i < expansion.DuplicatePoints.Length; i++)
        {
            var sourceIndex = expansion.DuplicateSourceIndices[i];
            var sourceWeight = sourceIndex < expansion.BasePointWeights.Length
                ? expansion.BasePointWeights[sourceIndex]
                : 0f;
            var sourceView = FaceMaskPointToView(
                context,
                layer,
                analysis,
                expansion.BaseContour[sourceIndex],
                applyEyelidsPreviewSkin,
                eyelidsPreviewSkinMatrix,
                sourceWeight);
            var duplicateWeight = i < expansion.DuplicatePointWeights.Length
                ? expansion.DuplicatePointWeights[i]
                : 0f;
            var offsetView = FaceMaskPointToView(
                context,
                layer,
                analysis,
                expansion.DuplicatePoints[i],
                applyEyelidsPreviewSkin,
                eyelidsPreviewSkinMatrix,
                duplicateWeight);
            var regionColor = expansion.Regions[sourceIndex] == ReanimatorFaceEyePointRegion.Edge
                ? new Color(1f, 0.2f, 0.75f, 1f)
                : new Color(0.2f, 0.9f, 1f, 1f);
            Handles.color = new Color(regionColor.r, regionColor.g, regionColor.b, 0.45f);
            Handles.DrawAAPolyLine(1.2f, sourceView, offsetView);
            DrawFaceEyePoint(offsetView, regionColor, 2.1f);
        }

        var outlineColor = new Color(1f, 0.12f, 0.12f, 1f);
        DrawClosedFaceEyeContour(
            context,
            layer,
            analysis,
            expansion.OutlineContour,
            outlineColor,
            5f,
            2.6f);
        for (var i = 0; i < expansion.OutlineContour.Length; i++)
        {
            var viewPoint = FaceMaskPointToView(context, layer, analysis, expansion.OutlineContour[i]);
            DrawFaceEyePoint(viewPoint, outlineColor, 1.8f);
        }

        var collisionColor = new Color(0.1f, 1f, 0.2f, 1f);
        for (var i = 0; i < expansion.OutlineCollisionPoints.Length; i++)
        {
            var viewPoint = FaceMaskPointToView(context, layer, analysis, expansion.OutlineCollisionPoints[i]);
            DrawFaceEyePoint(viewPoint, collisionColor, 4f);
            Handles.color = collisionColor;
            Handles.DrawWireDisc(viewPoint, Vector3.forward, 7f);
        }
    }

    private static void DrawClosedFaceEyeContour(
        ReanimatorPreviewContext context,
        PsdLayerPreviewRenderer.LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2[] points,
        Color color,
        float shadowWidth,
        float lineWidth,
        bool applyPreviewSkin = false,
        Matrix4x4 previewSkinMatrix = default,
        float[] previewSkinWeights = null)
    {
        var viewContour = new Vector3[points.Length + 1];
        for (var i = 0; i < points.Length; i++)
            viewContour[i] = FaceMaskPointToView(
                context,
                layer,
                analysis,
                points[i],
                applyPreviewSkin,
                previewSkinMatrix,
                previewSkinWeights != null && i < previewSkinWeights.Length
                    ? previewSkinWeights[i]
                    : 0f);
        viewContour[points.Length] = viewContour[0];
        Handles.color = new Color(0f, 0f, 0f, 0.85f);
        Handles.DrawAAPolyLine(shadowWidth, viewContour);
        Handles.color = color;
        Handles.DrawAAPolyLine(lineWidth, viewContour);
    }

    private static void DrawOpenFaceEyeContour(
        ReanimatorPreviewContext context,
        PsdLayerPreviewRenderer.LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2[] points,
        Color color,
        float shadowWidth,
        float lineWidth,
        bool applyPreviewSkin = false,
        Matrix4x4 previewSkinMatrix = default,
        float[] previewSkinWeights = null)
    {
        var viewContour = new Vector3[points.Length];
        for (var i = 0; i < points.Length; i++)
            viewContour[i] = FaceMaskPointToView(
                context,
                layer,
                analysis,
                points[i],
                applyPreviewSkin,
                previewSkinMatrix,
                previewSkinWeights != null && i < previewSkinWeights.Length
                    ? previewSkinWeights[i]
                    : 0f);
        Handles.color = new Color(0f, 0f, 0f, 0.85f);
        Handles.DrawAAPolyLine(shadowWidth, viewContour);
        Handles.color = color;
        Handles.DrawAAPolyLine(lineWidth, viewContour);
    }

    private static void DrawFaceEyePoint(Vector2 viewPoint, Color color, float radius)
    {
        Handles.color = new Color(0f, 0f, 0f, 0.85f);
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, radius + 1.3f);
        Handles.color = color;
        Handles.DrawSolidDisc(viewPoint, Vector3.forward, radius);
    }

    private static Vector2 FaceMaskPointToView(
        ReanimatorPreviewContext context,
        PsdLayerPreviewRenderer.LayerInfo layer,
        ReanimatorFaceAlphaMaskAnalysis analysis,
        Vector2 maskPoint,
        bool applyPreviewSkin = false,
        Matrix4x4 previewSkinMatrix = default,
        float previewSkinWeight = 0f)
    {
        var scaleX = layer.sourceRect.width / Mathf.Max(1f, analysis.Width);
        var scaleY = layer.sourceRect.height / Mathf.Max(1f, analysis.Height);
        var localPoint = new Vector2(maskPoint.x * scaleX, maskPoint.y * scaleY);
        var clampedWeight = Mathf.Clamp01(previewSkinWeight);
        if (applyPreviewSkin && clampedWeight > 0.0001f)
        {
            var documentPoint = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, localPoint);
            var weightedDocumentPoint = ReanimatorFaceAlphaMaskAlgorithms.ApplyVirtualBoneSkinWeight(
                documentPoint,
                previewSkinMatrix,
                clampedWeight);
            localPoint = PsdCoordinateSpace.DocumentToLayerLocalPoint(
                layer,
                weightedDocumentPoint);
        }
        return context.Coordinates.LayerLocalPointToView(layer, localPoint);
    }
}
#endif
