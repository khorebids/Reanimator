#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private bool TryBuildLimbGeneratedPoints(
        LimbLine line,
        LayerVertexData data,
        int contourSteps,
        float contourStepDistance,
        float contourShift,
        List<LimbGeneratedPoint> output,
        List<Vector2> contourPoints)
    {
        if (output == null || contourPoints == null)
            return false;

        output.Clear();
        contourPoints.Clear();
        if (line == null || data == null || !line.hasContourPoints)
            return false;

        if (!TryCollectLimbContourPoints(line, data, contourSteps, contourStepDistance, contourPoints))
            return false;

        var center = GetLimbContourMidpoint(line);
        var lineDirection = GetLimbGuideDirection(line);
        for (var i = 0; i < contourPoints.Count; i++)
            AddLimbGeneratedPoint(output, contourPoints[i], i, LimbGeneratedPointType.Contour);
        AddLimbGeneratedPoint(output, center, -1, LimbGeneratedPointType.Center);

        var shiftAmount = Mathf.Clamp01(contourShift);
        if (shiftAmount > 0.0001f)
        {
            for (var i = 0; i < contourPoints.Count; i++)
            {
                var fromPointToCenter = center - contourPoints[i];
                var alongLineToCenter = Vector2.Dot(fromPointToCenter, lineDirection);
                var shifted = contourPoints[i] + lineDirection * (alongLineToCenter * shiftAmount);
                AddLimbGeneratedPoint(output, shifted, i, LimbGeneratedPointType.Shifted);
            }
        }

        return output.Count > 0 && contourPoints.Count > 0;
    }

    private static bool AddLimbGeneratedPoint(
        List<LimbGeneratedPoint> points,
        Vector2 pointDoc,
        int contourSourceIndex,
        LimbGeneratedPointType type,
        float tolerance = ReanimatorLimbElbowAlgorithms.GeometryPointMergeTolerance)
    {
        if (points == null)
            return false;

        var toleranceSqr = tolerance * tolerance;
        for (var i = 0; i < points.Count; i++)
        {
            if ((points[i].pointDoc - pointDoc).sqrMagnitude <= toleranceSqr)
                return false;
        }

        points.Add(new LimbGeneratedPoint
        {
            pointDoc = pointDoc,
            contourSourceIndex = contourSourceIndex,
            type = type
        });
        return true;
    }

    private bool TryRebuildCulledLimbBoundary(
        LayerVertexData sourceData,
        LayerVertexData targetData,
        IReadOnlyList<Vector2> contourPointsDoc,
        float radius,
        bool removeInnerVertices)
    {
        if (sourceData == null || targetData == null ||
            sourceData.vertices == null || sourceData.vertices.Length < 3 ||
            targetData.vertices == null || targetData.vertices.Length < 3 ||
            contourPointsDoc == null || contourPointsDoc.Count == 0)
        {
            return false;
        }

        var sourceVertexCount = sourceData.vertices.Length;
        if (!TryBuildBoundaryLoopFromPairs(sourceData.boundaryEdgePairs, sourceVertexCount, out var sourceBoundaryLoop))
            return false;

        var contourMask = BuildContourVertexMask(sourceData.boundaryEdgePairs, sourceVertexCount);
        var removeMask = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
            sourceData.vertices,
            contourMask,
            radiusCullCenterDoc,
            radius,
            true,
            removeInnerVertices,
            out _);
        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);

        var replacementPoints = new List<Vector2>(contourPointsDoc.Count);
        var replacementVertexIndices = new List<int>(contourPointsDoc.Count);
        var mergeToleranceSqr =
            ReanimatorLimbElbowAlgorithms.GeometryPointMergeTolerance *
            ReanimatorLimbElbowAlgorithms.GeometryPointMergeTolerance;
        for (var i = 0; i < contourPointsDoc.Count; i++)
        {
            var point = ConvertPointToBaseDoc(sourceData, contourPointsDoc[i]);
            var vertexIndex = FindNearestVertexIndex(point, targetData.vertices);
            if (vertexIndex < 0 || vertexIndex >= targetData.vertices.Length)
                continue;
            if ((targetData.vertices[vertexIndex] - point).sqrMagnitude > mergeToleranceSqr)
                continue;

            replacementPoints.Add(point);
            replacementVertexIndices.Add(vertexIndex);
        }

        var replacementLoop = ReanimatorLimbElbowAlgorithms.BuildContourReplacementLoop(
            sourceBoundaryLoop,
            sourceData.vertices,
            removeMask,
            remap,
            replacementPoints,
            replacementVertexIndices,
            targetData.vertices.Length);
        var replacementPairs = BuildBoundaryPairsFromLoop(replacementLoop);
        if (replacementPairs == null || replacementPairs.Length < 6)
            return false;

        targetData.boundaryEdgePairs = replacementPairs;
        return true;
    }

}
#endif
