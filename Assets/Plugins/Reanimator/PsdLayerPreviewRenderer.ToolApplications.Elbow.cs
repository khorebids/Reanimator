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
    public bool ApplySelectedElbow()
    {
        var lineIndex = FindActiveElbowLineIndex();
        if (lineIndex < 0 || lineIndex >= elbowLines.Count)
            return false;

        var line = elbowLines[lineIndex];
        if (line == null)
            return false;

        // Keep existing contour points if they are already valid; refresh only as fallback.
        if (!line.hasContourPoints)
            RefreshElbowLineContour(line);
        if (!line.hasContourPoints)
            return false;

        var radialPoints = Mathf.Clamp(elbowRadialPoints, 1, 20);
        var bend = Mathf.Clamp(elbowBend, -2f, 2f);
        var a = line.contourPointA;
        var b = line.contourPointB;
        var center = (a + b) * 0.5f;
        var arcPoints = new List<Vector2>(radialPoints);
        if (!TryBuildElbowArcInteriorPoints(a, b, radialPoints, bend, arcPoints))
            BuildLinearInteriorPoints(a, b, radialPoints, arcPoints);

        if (!TryGetLayerVertexDataForKeys(line.layerSelectionKey, line.layerStableKey, out var data))
            return false;

        RegisterUndo("Apply Elbow");
        TryAddAppliedVertex(cachedPath, line.layerSelectionKey, line.layerStableKey, a, true);
        for (var i = 0; i < arcPoints.Count; i++)
            TryAddAppliedVertex(cachedPath, line.layerSelectionKey, line.layerStableKey, arcPoints[i], true);
        TryAddAppliedVertex(cachedPath, line.layerSelectionKey, line.layerStableKey, center, true);
        TryAddAppliedVertex(cachedPath, line.layerSelectionKey, line.layerStableKey, b, true);
        RemoveElbowLineAt(lineIndex);
        MarkEditGeometryDataChanged(data, line.layerSelectionKey, line.layerStableKey);
        return true;
    }

    private static bool TryBuildElbowArcInteriorPoints(
        Vector2 a,
        Vector2 b,
        int interiorPointCount,
        float bend,
        List<Vector2> output)
    {
        return ReanimatorLimbElbowAlgorithms.TryBuildElbowArcInteriorPoints(a, b, interiorPointCount, bend, output);
    }

    private static void BuildLinearInteriorPoints(Vector2 a, Vector2 b, int interiorPointCount, List<Vector2> output)
    {
        ReanimatorLimbElbowAlgorithms.BuildLinearInteriorPoints(a, b, interiorPointCount, output);
    }

    private static bool TryComputeCircleFromThreePoints(Vector2 a, Vector2 m, Vector2 b, out Vector2 center)
    {
        return ReanimatorLimbElbowAlgorithms.TryComputeCircleFromThreePoints(a, m, b, out center);
    }

    private static bool TryResolveArcSweepThroughPoint(float angleA, float angleB, float angleM, out float direction, out float sweep)
    {
        return ReanimatorLimbElbowAlgorithms.TryResolveArcSweepThroughPoint(angleA, angleB, angleM, out direction, out sweep);
    }

    private static float PositiveDeltaAngle(float from, float to)
    {
        return ReanimatorLimbElbowAlgorithms.PositiveDeltaAngle(from, to);
    }
}
#endif
