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
    private static readonly Vector2Int[] ReanimaxHardEdgeMooreNeighborOffsets =
    {
        new Vector2Int(-1, 0),
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1),
        new Vector2Int(1, 0),
        new Vector2Int(1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(-1, -1)
    };

    private void DrawAutoGeometryQuadNetPreviewSquare(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        LayerInfo selectedLayer,
        double density,
        float rotationDegrees)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;
        if (coordinates == null || selectedLayer == null || !selectedLayer.visible)
            return;

        var layerBounds = new Rect(0f, 0f, selectedLayer.sourceRect.width, selectedLayer.sourceRect.height);
        if (layerBounds.width <= 1e-4f || layerBounds.height <= 1e-4f)
            return;

        var targetDoc = coordinates.ViewToDocument(localRect.center);
        var targetLocal = PsdCoordinateSpace.DocumentToLayerLocalPoint(selectedLayer, targetDoc);
        var corners = new Vector2[4];
        if (!ReanimatorQuadNetAlgorithms.TryGetQuadNetCellCornersForTarget(
                layerBounds,
                density,
                rotationDegrees,
                targetLocal,
                corners))
            return;

        var points = new Vector3[5];
        for (var i = 0; i < 4; i++)
            points[i] = coordinates.LayerLocalPointToView(selectedLayer, corners[i]);
        points[4] = points[0];
        if (Vector2.Distance(points[0], points[1]) <= 0.5f || Vector2.Distance(points[1], points[2]) <= 0.5f)
            return;

        var prevColor = Handles.color;
        Handles.color = new Color(1f, 0.46f, 0f, 0.12f);
        Handles.DrawAAConvexPolygon(points[0], points[1], points[2], points[3]);
        Handles.color = new Color(1f, 0.46f, 0f, 0.95f);
        Handles.DrawAAPolyLine(2f, points);
        Handles.color = prevColor;
    }

    private void DrawReanimaxHardMaskPreview(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        LayerInfo selectedLayer,
        float alphaThreshold)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;
        if (coordinates == null || selectedLayer == null || !selectedLayer.visible)
            return;

        var dst = coordinates.LayerToViewRect(selectedLayer);
        if (!IsPreviewRectVisible(dst, localRect))
            return;

        var mask = GetReanimaxHardMaskPreviewTexture(selectedLayer, alphaThreshold);
        if (mask == null)
            return;

        var previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(dst, mask, ScaleMode.StretchToFill, false);
        GUI.color = previousColor;
    }

    private Texture2D GetReanimaxHardMaskPreviewTexture(LayerInfo layer, float alphaThreshold)
    {
        var source = layer != null && layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        if (layer == null || source == null)
            return null;

        var width = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.width), 1, 4096);
        var height = Mathf.Clamp(Mathf.RoundToInt(layer.sourceRect.height), 1, 4096);
        var size = new Vector2Int(width, height);
        var threshold = Mathf.Clamp01(alphaThreshold);
        var uvRect = PsdCoordinateSpace.LayerTextureUvRect(
            layer,
            new Vector2(source.width, source.height),
            false);
        var layerKey = BuildSelectionKey(layer);

        if (reanimaxHardMaskPreviewTexture != null &&
            reanimaxHardMaskPreviewTexture.width == width &&
            reanimaxHardMaskPreviewTexture.height == height &&
            reanimaxHardMaskPreviewSourceTexture == source &&
            reanimaxHardMaskPreviewSize == size &&
            string.Equals(reanimaxHardMaskPreviewLayerKey, layerKey, StringComparison.Ordinal) &&
            Mathf.Abs(reanimaxHardMaskPreviewThreshold - threshold) <= 0.0001f &&
            RectsRoughlyEqual(reanimaxHardMaskPreviewUvRect, uvRect))
            return reanimaxHardMaskPreviewTexture;

        try
        {
            if (reanimaxHardMaskPreviewTexture == null ||
                reanimaxHardMaskPreviewTexture.width != width ||
                reanimaxHardMaskPreviewTexture.height != height)
            {
                if (reanimaxHardMaskPreviewTexture != null)
                    UnityEngine.Object.DestroyImmediate(reanimaxHardMaskPreviewTexture);
                reanimaxHardMaskPreviewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            if (!TryReadReanimaxHardMaskSource(source, uvRect, reanimaxHardMaskPreviewTexture))
                return null;

            var pixels = reanimaxHardMaskPreviewTexture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var value = pixels[i].a / 255f < threshold ? (byte)0 : (byte)255;
                pixels[i] = new Color32(value, value, value, 255);
            }
            reanimaxHardMaskPreviewTexture.SetPixels32(pixels);
            reanimaxHardMaskPreviewTexture.Apply(false, false);
            reanimaxHardMaskPreviewLayerKey = layerKey;
            reanimaxHardMaskPreviewSourceTexture = source;
            reanimaxHardMaskPreviewSize = size;
            reanimaxHardMaskPreviewUvRect = uvRect;
            reanimaxHardMaskPreviewThreshold = threshold;
            return reanimaxHardMaskPreviewTexture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DrawReanimaxHardEdgePointsPreview(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        LayerInfo selectedLayer,
        float alphaThreshold,
        int edgePositionFilterPixels,
        float edgeMinTurnDegrees,
        int edgePostPositionFilterPixels,
        float edgeMinDistancePixels,
        int normalMidpointPositionFilterPixels,
        float normalInsetPixels,
        int normalInsetPositionFilterPixels)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;
        if (coordinates == null || selectedLayer == null || !selectedLayer.visible)
            return;
        if (!IsPreviewRectVisible(coordinates.LayerToViewRect(selectedLayer), localRect))
            return;

        var paths = GetReanimaxHardEdgePointPathsLocal(selectedLayer, alphaThreshold, edgePositionFilterPixels, edgeMinTurnDegrees, edgePostPositionFilterPixels, edgeMinDistancePixels);
        if (paths == null || paths.Length == 0)
            return;

        var previous = Handles.color;
        var outlineColor = new Color(1f, 0.46f, 0f, 0.95f);
        var pointFillColor = new Color(0.55f, 0.92f, 1f, 1f);
        var pointBorderColor = new Color(0.04f, 0.1f, 0.12f, 0.95f);
        Handles.color = outlineColor;
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var points = paths[pathIndex];
            if (points == null || points.Length <= 1)
                continue;

            var linePoints = new Vector3[points.Length + 1];
            for (var i = 0; i < points.Length; i++)
                linePoints[i] = coordinates.LayerLocalPointToView(selectedLayer, points[i]);
            linePoints[linePoints.Length - 1] = linePoints[0];
            Handles.DrawAAPolyLine(2.5f, linePoints);
            DrawShiftedReanimaxHardEdgePath(linePoints, new Vector2(0.75f, 0f), outlineColor);
            DrawShiftedReanimaxHardEdgePath(linePoints, new Vector2(0f, 0.75f), outlineColor);
        }

        DrawTemporaryReanimaxHardNormalProbe(localRect, coordinates, selectedLayer, paths, normalMidpointPositionFilterPixels, normalInsetPixels, normalInsetPositionFilterPixels);

        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var points = paths[pathIndex];
            if (points == null)
                continue;

            for (var i = 0; i < points.Length; i++)
            {
                var view = coordinates.LayerLocalPointToView(selectedLayer, points[i]);
                if (!localRect.Contains(view))
                    continue;
                Handles.color = pointFillColor;
                Handles.DrawSolidDisc(view, Vector3.forward, 3.1f);
                Handles.color = pointBorderColor;
                Handles.DrawWireDisc(view, Vector3.forward, 3.9f);
            }
        }
        Handles.color = previous;
    }

    private struct TemporaryReanimaxHardNormalLine
    {
        public Vector2 Point;
        public Vector2 Direction;
        public Vector2 InwardDirection;
        public Vector2 SegmentStart;
        public Vector2 SegmentEnd;
        public Vector2 Midpoint;
    }

    private static void DrawTemporaryReanimaxHardNormalProbe(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        LayerInfo selectedLayer,
        IReadOnlyList<Vector2[]> paths,
        int midpointPositionFilterPixels,
        float insetPixels,
        int insetPositionFilterPixels)
    {
        if (coordinates == null || selectedLayer == null || paths == null)
            return;

        var layerBounds = new Rect(0f, 0f, selectedLayer.sourceRect.width, selectedLayer.sourceRect.height);
        if (layerBounds.width <= 0.0001f || layerBounds.height <= 0.0001f)
            return;

        var lines = BuildTemporaryReanimaxHardNormalLines(paths, layerBounds);
        if (lines.Count == 0)
            return;

        DrawTemporaryReanimaxHardInsetPointCopies(localRect, coordinates, selectedLayer, paths, lines, layerBounds, insetPixels, insetPositionFilterPixels);

        var filteredMidpoints = BuildTemporaryReanimaxHardNormalMidpoints(paths, lines, midpointPositionFilterPixels);
        var innerPointColor = new Color(0.36f, 0.78f, 1f, 0.95f);
        Handles.color = innerPointColor;
        for (var i = 0; i < filteredMidpoints.Count; i++)
        {
            var view = coordinates.LayerLocalPointToView(selectedLayer, filteredMidpoints[i]);
            if (!localRect.Contains(view))
                continue;

            Handles.DrawSolidDisc(view, Vector3.forward, 2.25f);
        }
    }

    private static List<TemporaryReanimaxHardNormalLine> BuildTemporaryReanimaxHardNormalLines(
        IReadOnlyList<Vector2[]> paths,
        Rect layerBounds)
    {
        var lines = new List<TemporaryReanimaxHardNormalLine>();
        if (paths == null)
            return lines;

        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var points = paths[pathIndex];
            if (points == null || points.Length < 3)
                continue;

            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                var previous = points[(i + points.Length - 1) % points.Length];
                var next = points[(i + 1) % points.Length];
                var tangent = next - previous;
                if (tangent.sqrMagnitude <= 0.0001f)
                    continue;

                var normal = new Vector2(-tangent.y, tangent.x).normalized;
                if (!TryGetTemporaryReanimaxHardNormalSegment(point, normal, layerBounds, paths, out var inwardDirection, out var segmentEnd, out var midpoint))
                    continue;

                lines.Add(new TemporaryReanimaxHardNormalLine
                {
                    Point = point,
                    Direction = normal,
                    InwardDirection = inwardDirection,
                    SegmentStart = point,
                    SegmentEnd = segmentEnd,
                    Midpoint = midpoint
                });
            }
        }

        return lines;
    }

    private static void DrawTemporaryReanimaxHardInsetPointCopies(
        Rect localRect,
        PsdCoordinateSpace coordinates,
        LayerInfo selectedLayer,
        IReadOnlyList<Vector2[]> paths,
        IReadOnlyList<TemporaryReanimaxHardNormalLine> lines,
        Rect layerBounds,
        float insetPixels,
        int insetPositionFilterPixels)
    {
        if (lines == null || lines.Count == 0)
            return;

        var filteredCopies = BuildTemporaryReanimaxHardInsetPointCopies(paths, lines, layerBounds, insetPixels, insetPositionFilterPixels);
        Handles.color = new Color(0.36f, 0.78f, 1f, 0.95f);
        for (var i = 0; i < filteredCopies.Count; i++)
        {
            var view = coordinates.LayerLocalPointToView(selectedLayer, filteredCopies[i]);
            if (!localRect.Contains(view))
                continue;

            Handles.DrawSolidDisc(view, Vector3.forward, 2.6f);
        }
    }

    private static List<Vector2> BuildTemporaryReanimaxHardInsetPointCopies(
        IReadOnlyList<Vector2[]> paths,
        IReadOnlyList<TemporaryReanimaxHardNormalLine> lines,
        Rect layerBounds,
        float insetPixels,
        int insetPositionFilterPixels)
    {
        var copies = new List<Vector2>(lines != null ? lines.Count : 0);
        if (lines == null || lines.Count == 0)
            return copies;

        var inset = Mathf.Clamp(insetPixels, 0f, 100f);
        if (inset <= 0.0001f)
            return copies;

        for (var i = 0; i < lines.Count; i++)
        {
            var copy = lines[i].Point + lines[i].InwardDirection * inset;
            if (!layerBounds.Contains(copy) || !IsPointInsideTemporaryReanimaxHardContour(copy, paths))
                continue;

            copies.Add(copy);
        }

        return FilterNearbyTemporaryReanimaxHardPoints(copies, insetPositionFilterPixels);
    }

    private static List<Vector2> BuildTemporaryReanimaxHardNormalMidpoints(
        IReadOnlyList<Vector2[]> paths,
        IReadOnlyList<TemporaryReanimaxHardNormalLine> lines,
        int midpointPositionFilterPixels)
    {
        var filteredMidpoints = FilterNearbyTemporaryReanimaxHardPoints(
            CollectTemporaryReanimaxHardNormalMidpoints(lines),
            midpointPositionFilterPixels);
        var result = new List<Vector2>(filteredMidpoints.Count);
        var uniqueMidpoints = new HashSet<long>();
        for (var i = 0; i < filteredMidpoints.Count; i++)
        {
            if (!IsPointInsideTemporaryReanimaxHardContour(filteredMidpoints[i], paths) ||
                !AddUniqueTemporaryReanimaxHardIntersection(uniqueMidpoints, filteredMidpoints[i]))
                continue;

            result.Add(filteredMidpoints[i]);
        }

        return result;
    }

    private static List<Vector2> BuildReanimaxHardInternalPointsLocal(
        IReadOnlyList<Vector2[]> paths,
        Rect layerBounds,
        int midpointPositionFilterPixels,
        float insetPixels,
        int insetPositionFilterPixels)
    {
        var result = new List<Vector2>();
        var lines = BuildTemporaryReanimaxHardNormalLines(paths, layerBounds);
        if (lines.Count == 0)
            return result;

        result.AddRange(BuildTemporaryReanimaxHardNormalMidpoints(paths, lines, midpointPositionFilterPixels));
        result.AddRange(BuildTemporaryReanimaxHardInsetPointCopies(paths, lines, layerBounds, insetPixels, insetPositionFilterPixels));
        return result;
    }

    private static List<Vector2> CollectTemporaryReanimaxHardNormalMidpoints(IReadOnlyList<TemporaryReanimaxHardNormalLine> lines)
    {
        var points = new List<Vector2>(lines != null ? lines.Count : 0);
        if (lines == null)
            return points;

        for (var i = 0; i < lines.Count; i++)
            points.Add(lines[i].Midpoint);

        return points;
    }

    private static List<Vector2> FilterNearbyTemporaryReanimaxHardPoints(IReadOnlyList<Vector2> points, int positionFilterPixels)
    {
        var result = new List<Vector2>();
        if (points == null || points.Count == 0)
            return result;

        var radius = Mathf.Clamp(positionFilterPixels, 1, 50);
        result.Add(points[0]);
        var lastKept = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            var current = points[i];
            if ((current - lastKept).sqrMagnitude <= radius * radius)
                continue;

            result.Add(current);
            lastKept = current;
        }

        if (result.Count > 2 && (result[0] - result[result.Count - 1]).sqrMagnitude <= radius * radius)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    private static bool IsPointInsideTemporaryReanimaxHardContour(Vector2 point, IReadOnlyList<Vector2[]> paths)
    {
        if (paths == null)
            return false;

        for (var i = 0; i < paths.Count; i++)
        {
            if (IsPointInsideTemporaryReanimaxHardPolygon(point, paths[i]))
                return true;
        }

        return false;
    }

    private static bool IsPointInsideTemporaryReanimaxHardPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return false;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (IsPointOnTemporaryReanimaxHardSegment(point, a, b))
                return false;

            var crosses = (a.y > point.y) != (b.y > point.y);
            if (!crosses)
                continue;

            var x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (point.x < x)
                inside = !inside;
        }

        return inside;
    }

    private static bool IsPointOnTemporaryReanimaxHardSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        const float epsilon = 0.001f;
        var ab = b - a;
        var ap = point - a;
        var cross = Mathf.Abs(ab.x * ap.y - ab.y * ap.x);
        if (cross > epsilon)
            return false;

        var dot = Vector2.Dot(ap, ab);
        if (dot < -epsilon)
            return false;

        return dot <= ab.sqrMagnitude + epsilon;
    }

    private static bool TryGetTemporaryReanimaxHardNormalSegment(
        Vector2 point,
        Vector2 normal,
        Rect rect,
        IReadOnlyList<Vector2[]> paths,
        out Vector2 inwardDirection,
        out Vector2 segmentEnd,
        out Vector2 midpoint)
    {
        inwardDirection = default;
        segmentEnd = default;
        midpoint = default;
        if (normal.sqrMagnitude <= 0.0001f)
            return false;

        var direction = normal;
        if (!IsPointInsideTemporaryReanimaxHardContour(point + direction * 0.75f, paths))
        {
            if (!IsPointInsideTemporaryReanimaxHardContour(point - direction * 0.75f, paths))
                return false;

            direction = -direction;
        }
        inwardDirection = direction;

        var maxDistance = Mathf.Max(rect.width, rect.height) * 2f;
        var step = Mathf.Max(1f, maxDistance / 512f);
        var lastInsideT = 0f;
        var outsideT = 0f;
        var foundExit = false;
        for (var t = step; t <= maxDistance; t += step)
        {
            var sample = point + direction * t;
            if (rect.Contains(sample) && IsPointInsideTemporaryReanimaxHardContour(sample, paths))
            {
                lastInsideT = t;
                continue;
            }

            outsideT = t;
            foundExit = true;
            break;
        }

        if (!foundExit || outsideT <= 0.0001f)
            return false;

        for (var i = 0; i < 8; i++)
        {
            var t = (lastInsideT + outsideT) * 0.5f;
            var sample = point + direction * t;
            if (rect.Contains(sample) && IsPointInsideTemporaryReanimaxHardContour(sample, paths))
                lastInsideT = t;
            else
                outsideT = t;
        }

        var endT = outsideT + 2f;
        segmentEnd = point + direction * endT;
        midpoint = point + direction * (endT * 0.5f);
        if (!rect.Contains(midpoint))
            return false;

        return (segmentEnd - point).sqrMagnitude > 0.0001f;
    }

    private static bool AddUniqueTemporaryReanimaxHardIntersection(HashSet<long> keys, Vector2 point)
    {
        if (keys == null)
            return false;

        var x = Mathf.RoundToInt(point.x * 100f);
        var y = Mathf.RoundToInt(point.y * 100f);
        var key = ((long)x << 32) ^ (uint)y;
        return keys.Add(key);
    }

    private static void DrawShiftedReanimaxHardEdgePath(Vector3[] linePoints, Vector2 offset, Color color)
    {
        if (linePoints == null || linePoints.Length <= 1)
            return;

        var shifted = new Vector3[linePoints.Length];
        for (var i = 0; i < linePoints.Length; i++)
            shifted[i] = linePoints[i] + new Vector3(offset.x, offset.y, 0f);

        Handles.color = color;
        Handles.DrawAAPolyLine(2.5f, shifted);
    }

    private Vector2[][] GetReanimaxHardEdgePointPathsLocal(LayerInfo layer, float alphaThreshold, int edgePositionFilterPixels, float edgeMinTurnDegrees, int edgePostPositionFilterPixels, float edgeMinDistancePixels)
    {
        var mask = GetReanimaxHardMaskPreviewTexture(layer, alphaThreshold);
        if (layer == null || mask == null)
            return Array.Empty<Vector2[]>();

        var source = layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        var threshold = Mathf.Clamp01(alphaThreshold);
        var positionFilterPixels = Mathf.Clamp(edgePositionFilterPixels, 1, 20);
        var minTurnDegrees = Mathf.Clamp(edgeMinTurnDegrees, 0f, 45f);
        var postPositionFilterPixels = Mathf.Clamp(edgePostPositionFilterPixels, 1, 20);
        var minDistancePixels = Mathf.Clamp(edgeMinDistancePixels, 0f, 20f);
        var uvRect = PsdCoordinateSpace.LayerTextureUvRect(
            layer,
            new Vector2(source != null ? source.width : mask.width, source != null ? source.height : mask.height),
            false);
        var layerKey = BuildSelectionKey(layer);
        var size = new Vector2Int(mask.width, mask.height);
        if (reanimaxHardMaskEdgePointPathsLocal != null &&
            reanimaxHardMaskEdgePointsSourceTexture == source &&
            reanimaxHardMaskEdgePointsSize == size &&
            string.Equals(reanimaxHardMaskEdgePointsLayerKey, layerKey, StringComparison.Ordinal) &&
            Mathf.Abs(reanimaxHardMaskEdgePointsThreshold - threshold) <= 0.0001f &&
            reanimaxHardMaskEdgePointsPositionFilterPixels == positionFilterPixels &&
            Mathf.Abs(reanimaxHardMaskEdgePointsMinTurnDegrees - minTurnDegrees) <= 0.0001f &&
            reanimaxHardMaskEdgePointsPostPositionFilterPixels == postPositionFilterPixels &&
            Mathf.Abs(reanimaxHardMaskEdgePointsMinDistancePixels - minDistancePixels) <= 0.0001f &&
            RectsRoughlyEqual(reanimaxHardMaskEdgePointsUvRect, uvRect))
            return reanimaxHardMaskEdgePointPathsLocal;

        var pixels = mask.GetPixels32();
        var edgePixels = CollectReanimaxHardWhiteEdgePixels(pixels, mask.width, mask.height);

        var simplifiedPixelPaths = SimplifyReanimaxHardEdgePixelPaths(edgePixels, mask.width, mask.height, positionFilterPixels, minTurnDegrees, postPositionFilterPixels, minDistancePixels);
        var paths = new Vector2[simplifiedPixelPaths.Count][];
        for (var pathIndex = 0; pathIndex < simplifiedPixelPaths.Count; pathIndex++)
        {
            var simplifiedPixels = simplifiedPixelPaths[pathIndex];
            var points = new Vector2[simplifiedPixels.Count];
            for (var i = 0; i < simplifiedPixels.Count; i++)
                points[i] = new Vector2(simplifiedPixels[i].x + 0.5f, simplifiedPixels[i].y + 0.5f);
            paths[pathIndex] = points;
        }

        reanimaxHardMaskEdgePointPathsLocal = paths;
        reanimaxHardMaskEdgePointsLayerKey = layerKey;
        reanimaxHardMaskEdgePointsSourceTexture = source;
        reanimaxHardMaskEdgePointsSize = size;
        reanimaxHardMaskEdgePointsUvRect = uvRect;
        reanimaxHardMaskEdgePointsThreshold = threshold;
        reanimaxHardMaskEdgePointsPositionFilterPixels = positionFilterPixels;
        reanimaxHardMaskEdgePointsMinTurnDegrees = minTurnDegrees;
        reanimaxHardMaskEdgePointsPostPositionFilterPixels = postPositionFilterPixels;
        reanimaxHardMaskEdgePointsMinDistancePixels = minDistancePixels;
        return reanimaxHardMaskEdgePointPathsLocal;
    }

    private static List<Vector2Int> SimplifyReanimaxHardEdgePixels(IReadOnlyList<Vector2Int> edgePixels, int width, int height, int positionFilterPixels)
    {
        var result = new List<Vector2Int>(edgePixels != null ? edgePixels.Count : 0);
        var paths = SimplifyReanimaxHardEdgePixelPaths(edgePixels, width, height, positionFilterPixels, 0f, 1, 0f);
        for (var i = 0; i < paths.Count; i++)
            result.AddRange(paths[i]);
        return result;
    }

    private static List<List<Vector2Int>> SimplifyReanimaxHardEdgePixelPaths(IReadOnlyList<Vector2Int> edgePixels, int width, int height, int positionFilterPixels, float minTurnDegrees, int postPositionFilterPixels, float minDistancePixels)
    {
        var result = new List<List<Vector2Int>>();
        if (edgePixels == null || edgePixels.Count <= 2 || width <= 0 || height <= 0)
            return result;

        var remaining = new HashSet<int>();
        for (var i = 0; i < edgePixels.Count; i++)
        {
            var p = edgePixels[i];
            if (p.x < 0 || p.y < 0 || p.x >= width || p.y >= height)
                continue;
            remaining.Add(MakeReanimaxHardEdgePixelKey(p.x, p.y, width));
        }

        while (remaining.Count > 0)
        {
            var component = TakeReanimaxHardEdgeComponent(remaining, width, height);
            if (TryBuildSimplifiedReanimaxHardClosedEdgePath(
                    component,
                    width,
                    positionFilterPixels,
                    minTurnDegrees,
                    postPositionFilterPixels,
                    minDistancePixels,
                    out var path))
                result.Add(path);
        }

        return result;
    }

    private static List<Vector2Int> CollectReanimaxHardWhiteEdgePixels(Color32[] pixels, int width, int height)
    {
        var edgePixels = new List<Vector2Int>(pixels != null ? Mathf.Min(pixels.Length, 4096) : 0);
        if (pixels == null || width <= 0 || height <= 0)
            return edgePixels;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (index < 0 || index >= pixels.Length)
                    continue;
                if (!IsReanimaxHardMaskWhite(pixels[index]))
                    continue;
                if (!HasAdjacentReanimaxHardMaskBlackOrOutOfBounds(pixels, width, height, x, y))
                    continue;

                edgePixels.Add(new Vector2Int(x, y));
            }
        }

        return edgePixels;
    }

    private static bool TryBuildSimplifiedReanimaxHardClosedEdgePath(
        IReadOnlyList<Vector2Int> component,
        int width,
        int positionFilterPixels,
        float minTurnDegrees,
        int postPositionFilterPixels,
        float minDistancePixels,
        out List<Vector2Int> path)
    {
        path = null;
        var ordered = OrderReanimaxHardEdgeComponent(component, width);
        if (!IsClosedReanimaxHardEdgePixelPath(ordered))
            return false;

        var positionFiltered = RemoveNearbyReanimaxHardEdgePixels(ordered, positionFilterPixels);
        var turnFiltered = RemoveLowTurnReanimaxHardEdgePixels(positionFiltered, minTurnDegrees);
        var postPositionFiltered = RemoveNearbyReanimaxHardEdgePixels(turnFiltered, postPositionFilterPixels);
        var distanceRestored = RestoreDistantReanimaxHardEdgePixels(ordered, postPositionFiltered, minDistancePixels, width);
        if (distanceRestored.Count <= 0)
            return false;

        path = distanceRestored;
        return true;
    }

    private static List<Vector2Int> TakeReanimaxHardEdgeComponent(HashSet<int> remaining, int width, int height)
    {
        var component = new List<Vector2Int>();
        if (remaining == null || remaining.Count == 0)
            return component;

        var startKey = 0;
        foreach (var key in remaining)
        {
            startKey = key;
            break;
        }

        var queue = new Queue<int>();
        remaining.Remove(startKey);
        queue.Enqueue(startKey);
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            var p = DecodeReanimaxHardEdgePixelKey(key, width);
            component.Add(p);

            for (var ny = p.y - 1; ny <= p.y + 1; ny++)
            {
                for (var nx = p.x - 1; nx <= p.x + 1; nx++)
                {
                    if (nx == p.x && ny == p.y)
                        continue;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    var nextKey = MakeReanimaxHardEdgePixelKey(nx, ny, width);
                    if (!remaining.Remove(nextKey))
                        continue;
                    queue.Enqueue(nextKey);
                }
            }
        }

        return component;
    }

    private static List<Vector2Int> OrderReanimaxHardEdgeComponent(IReadOnlyList<Vector2Int> component, int width)
    {
        var ordered = new List<Vector2Int>(component != null ? component.Count : 0);
        if (component == null || component.Count == 0)
            return ordered;

        var componentKeys = new HashSet<int>();
        for (var i = 0; i < component.Count; i++)
            componentKeys.Add(MakeReanimaxHardEdgePixelKey(component[i].x, component[i].y, width));

        var start = component[0];
        for (var i = 1; i < component.Count; i++)
        {
            var candidate = component[i];
            if (candidate.y < start.y || (candidate.y == start.y && candidate.x < start.x))
                start = candidate;
        }

        ordered = TraceReanimaxHardEdgeComponent(start, componentKeys, width, component.Count * 8 + 8);
        if (ordered.Count >= 3)
            return ordered;

        return OrderReanimaxHardEdgeComponentByAngle(component);
    }

    private static List<Vector2Int> TraceReanimaxHardEdgeComponent(Vector2Int start, HashSet<int> componentKeys, int width, int maxSteps)
    {
        var ordered = new List<Vector2Int>();
        if (componentKeys == null || componentKeys.Count == 0 || maxSteps <= 0)
            return ordered;

        var current = start;
        var previous = new Vector2Int(start.x - 1, start.y);
        for (var step = 0; step < maxSteps; step++)
        {
            ordered.Add(current);
            if (!TryFindNextReanimaxHardEdgeContourPixel(current, previous, componentKeys, width, out var next, out var nextPrevious))
                break;
            if (next == start)
                break;
            if (ordered.Contains(next))
                break;
            previous = nextPrevious;
            current = next;
        }

        return ordered;
    }

    private static bool TryFindNextReanimaxHardEdgeContourPixel(
        Vector2Int current,
        Vector2Int previous,
        HashSet<int> componentKeys,
        int width,
        out Vector2Int next,
        out Vector2Int nextPrevious)
    {
        next = current;
        nextPrevious = previous;

        var backtrackIndex = IndexOfReanimaxHardEdgeMooreNeighbor(current, previous);
        var startIndex = backtrackIndex >= 0 ? (backtrackIndex + 1) % ReanimaxHardEdgeMooreNeighborOffsets.Length : 0;
        for (var i = 0; i < ReanimaxHardEdgeMooreNeighborOffsets.Length; i++)
        {
            var index = (startIndex + i) % ReanimaxHardEdgeMooreNeighborOffsets.Length;
            var candidate = current + ReanimaxHardEdgeMooreNeighborOffsets[index];
            if (!componentKeys.Contains(MakeReanimaxHardEdgePixelKey(candidate.x, candidate.y, width)))
                continue;

            next = candidate;
            nextPrevious = current + ReanimaxHardEdgeMooreNeighborOffsets[(index + ReanimaxHardEdgeMooreNeighborOffsets.Length - 1) % ReanimaxHardEdgeMooreNeighborOffsets.Length];
            return true;
        }

        return false;
    }

    private static int IndexOfReanimaxHardEdgeMooreNeighbor(Vector2Int current, Vector2Int neighbor)
    {
        var direction = neighbor - current;
        for (var i = 0; i < ReanimaxHardEdgeMooreNeighborOffsets.Length; i++)
        {
            if (ReanimaxHardEdgeMooreNeighborOffsets[i] == direction)
                return i;
        }

        return -1;
    }

    private static List<Vector2Int> OrderReanimaxHardEdgeComponentByAngle(IReadOnlyList<Vector2Int> component)
    {
        var ordered = new List<Vector2Int>(component != null ? component.Count : 0);
        if (component == null || component.Count == 0)
            return ordered;

        var center = Vector2.zero;
        for (var i = 0; i < component.Count; i++)
            center += component[i];
        center /= component.Count;

        ordered.AddRange(component);
        ordered.Sort((a, b) =>
        {
            var angleA = Mathf.Atan2(a.y - center.y, a.x - center.x);
            var angleB = Mathf.Atan2(b.y - center.y, b.x - center.x);
            var angleCompare = angleA.CompareTo(angleB);
            if (angleCompare != 0)
                return angleCompare;

            var distA = (new Vector2(a.x, a.y) - center).sqrMagnitude;
            var distB = (new Vector2(b.x, b.y) - center).sqrMagnitude;
            return distA.CompareTo(distB);
        });
        return ordered;
    }

    private static bool IsClosedReanimaxHardEdgePixelPath(IReadOnlyList<Vector2Int> ordered)
    {
        return ordered != null &&
               ordered.Count >= 3 &&
               AreAdjacentReanimaxHardEdgePixels(ordered[0], ordered[ordered.Count - 1]);
    }

    private static List<Vector2Int> RemoveNearbyReanimaxHardEdgePixels(IReadOnlyList<Vector2Int> ordered, int positionFilterPixels)
    {
        var result = new List<Vector2Int>(ordered != null ? ordered.Count : 0);
        if (ordered == null || ordered.Count <= 1)
        {
            if (ordered != null)
                result.AddRange(ordered);
            return result;
        }

        var radius = Mathf.Clamp(positionFilterPixels, 1, 20);
        var lastKept = ordered[0];
        result.Add(lastKept);
        for (var i = 1; i < ordered.Count; i++)
        {
            var current = ordered[i];
            if (IsWithinReanimaxHardEdgePositionFilter(lastKept, current, radius))
                continue;

            result.Add(current);
            lastKept = current;
        }

        if (result.Count > 2 && IsWithinReanimaxHardEdgePositionFilter(result[0], result[result.Count - 1], radius))
            result.RemoveAt(result.Count - 1);

        return result;
    }

    private static List<Vector2Int> RemoveLowTurnReanimaxHardEdgePixels(IReadOnlyList<Vector2Int> ordered, float minTurnDegrees)
    {
        var result = new List<Vector2Int>(ordered != null ? ordered.Count : 0);
        if (ordered == null || ordered.Count <= 2)
        {
            if (ordered != null)
                result.AddRange(ordered);
            return result;
        }

        var closed = AreAdjacentReanimaxHardEdgePixels(ordered[0], ordered[ordered.Count - 1]);
        var minTurn = Mathf.Clamp(minTurnDegrees, 0f, 45f);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!closed && (i == 0 || i == ordered.Count - 1))
            {
                result.Add(ordered[i]);
                continue;
            }

            var previous = ordered[(i - 1 + ordered.Count) % ordered.Count];
            var current = ordered[i];
            var next = ordered[(i + 1) % ordered.Count];
            var previousDirection = NormalizeReanimaxHardEdgeDirection(previous, current);
            var nextDirection = NormalizeReanimaxHardEdgeDirection(current, next);
            if (previousDirection == nextDirection)
                continue;
            if (minTurn > 0f && GetReanimaxHardEdgeTurnDegrees(previous, current, next) < minTurn)
                continue;

            result.Add(current);
        }

        return result;
    }

    private static List<Vector2Int> RestoreDistantReanimaxHardEdgePixels(
        IReadOnlyList<Vector2Int> originalOrdered,
        IReadOnlyList<Vector2Int> finalLine,
        float minDistancePixels,
        int width)
    {
        var result = new List<Vector2Int>(originalOrdered != null ? originalOrdered.Count : 0);
        if (originalOrdered == null || originalOrdered.Count == 0)
            return result;
        if (finalLine == null || finalLine.Count == 0)
        {
            result.AddRange(originalOrdered);
            return result;
        }

        var minDistance = Mathf.Clamp(minDistancePixels, 0f, 20f);
        if (minDistance <= 0.0001f)
        {
            result.AddRange(finalLine);
            return result;
        }

        var finalKeys = new HashSet<int>();
        for (var i = 0; i < finalLine.Count; i++)
            finalKeys.Add(MakeReanimaxHardEdgePixelKey(finalLine[i].x, finalLine[i].y, width));

        for (var i = 0; i < originalOrdered.Count; i++)
        {
            var point = originalOrdered[i];
            var key = MakeReanimaxHardEdgePixelKey(point.x, point.y, width);
            if (finalKeys.Contains(key) ||
                GetDistanceFromReanimaxHardEdgeLine(point, finalLine) >= minDistance)
                result.Add(point);
        }

        if (result.Count == 0)
            result.AddRange(finalLine);
        return result;
    }

    private static float GetDistanceFromReanimaxHardEdgeLine(Vector2Int point, IReadOnlyList<Vector2Int> line)
    {
        if (line == null || line.Count == 0)
            return 0f;
        if (line.Count == 1)
            return Vector2.Distance(new Vector2(point.x, point.y), new Vector2(line[0].x, line[0].y));

        var p = new Vector2(point.x, point.y);
        var minSqrDistance = float.MaxValue;
        for (var i = 0; i < line.Count; i++)
        {
            var a = line[i];
            var b = line[(i + 1) % line.Count];
            var sqrDistance = GetSqrDistanceFromReanimaxHardEdgeSegment(
                p,
                new Vector2(a.x, a.y),
                new Vector2(b.x, b.y));
            if (sqrDistance < minSqrDistance)
                minSqrDistance = sqrDistance;
        }

        return Mathf.Sqrt(minSqrDistance);
    }

    private static float GetSqrDistanceFromReanimaxHardEdgeSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var abLength = ab.sqrMagnitude;
        if (abLength <= 0.0001f)
            return (point - a).sqrMagnitude;

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / abLength);
        var closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    private static float GetReanimaxHardEdgeTurnDegrees(Vector2Int previous, Vector2Int current, Vector2Int next)
    {
        var a = new Vector2(current.x - previous.x, current.y - previous.y);
        var b = new Vector2(next.x - current.x, next.y - current.y);
        if (a.sqrMagnitude <= 0.0001f || b.sqrMagnitude <= 0.0001f)
            return 0f;

        return Vector2.Angle(a, b);
    }

    private static bool AreAdjacentReanimaxHardEdgePixels(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) <= 1 && Mathf.Abs(a.y - b.y) <= 1;
    }

    private static bool IsWithinReanimaxHardEdgePositionFilter(Vector2Int a, Vector2Int b, int radius)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static Vector2Int NormalizeReanimaxHardEdgeDirection(Vector2Int from, Vector2Int to)
    {
        var dx = to.x - from.x;
        var dy = to.y - from.y;
        if (dx == 0 && dy == 0)
            return Vector2Int.zero;

        var divisor = GreatestCommonReanimaxHardEdgeDivisor(Mathf.Abs(dx), Mathf.Abs(dy));
        return divisor <= 0 ? new Vector2Int(dx, dy) : new Vector2Int(dx / divisor, dy / divisor);
    }

    private static int GreatestCommonReanimaxHardEdgeDivisor(int a, int b)
    {
        while (b != 0)
        {
            var next = a % b;
            a = b;
            b = next;
        }
        return Mathf.Max(1, a);
    }

    private static int MakeReanimaxHardEdgePixelKey(int x, int y, int width)
    {
        return y * width + x;
    }

    private static Vector2Int DecodeReanimaxHardEdgePixelKey(int key, int width)
    {
        return new Vector2Int(key % width, key / width);
    }

    private static bool HasAdjacentReanimaxHardMaskBlackOrOutOfBounds(Color32[] pixels, int width, int height, int x, int y)
    {
        return IsReanimaxHardMaskBlackOrOutOfBoundsAt(pixels, width, height, x - 1, y) ||
               IsReanimaxHardMaskBlackOrOutOfBoundsAt(pixels, width, height, x + 1, y) ||
               IsReanimaxHardMaskBlackOrOutOfBoundsAt(pixels, width, height, x, y - 1) ||
               IsReanimaxHardMaskBlackOrOutOfBoundsAt(pixels, width, height, x, y + 1);
    }

    private static bool IsReanimaxHardMaskBlackOrOutOfBoundsAt(Color32[] pixels, int width, int height, int x, int y)
    {
        if (pixels == null)
            return false;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return true;

        var index = y * width + x;
        return index < 0 || index >= pixels.Length || IsReanimaxHardMaskBlack(pixels[index]);
    }

    private static bool IsReanimaxHardMaskBlack(Color32 color)
    {
        return color.r < 128;
    }

    private static bool IsReanimaxHardMaskWhite(Color32 color)
    {
        return color.r >= 128;
    }

    private static bool TryReadReanimaxHardMaskSource(Texture source, Rect uvRect, Texture2D target)
    {
        if (source == null || target == null || target.width <= 0 || target.height <= 0)
            return false;

        var rt = RenderTexture.GetTemporary(target.width, target.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        var pushedMatrix = false;
        try
        {
            RenderTexture.active = rt;
            GL.PushMatrix();
            pushedMatrix = true;
            GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
            GL.Clear(true, true, Color.clear);
            Graphics.DrawTexture(
                new Rect(0f, 0f, target.width, target.height),
                source,
                uvRect,
                0,
                0,
                0,
                0);
            GL.PopMatrix();
            pushedMatrix = false;

            target.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (pushedMatrix)
                GL.PopMatrix();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private void DrawElbowContourPoints(
        Func<Vector2, Vector2> toCanvas,
        string selectedLayerKey,
        string layerStableKey,
        LayerVertexData data,
        Color pointColor,
        Color connectorColor)
    {
        if (string.IsNullOrEmpty(layerStableKey) || toCanvas == null || elbowLines == null || elbowLines.Count == 0)
            return;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        var prev = Handles.color;
        const float connectorWidth = 1.3f;
        const float radius = 2.4f;
        var arcPoints = new List<Vector2>(24);

        for (var i = 0; i < elbowLines.Count; i++)
        {
            var line = elbowLines[i];
            if (line == null || !line.hasContourPoints)
                continue;
            if (!IsElbowLineForLayer(line, selectedLayerKey, layerStableKey))
                continue;

            var a = line.contourPointA;
            var b = line.contourPointB;
            var center = (a + b) * 0.5f;
            var radialPoints = Mathf.Clamp(ElbowRadialPoints, 1, 20);
            var bend = Mathf.Clamp(ElbowBend, -2f, 2f);
            if (!TryBuildElbowArcInteriorPoints(a, b, radialPoints, bend, arcPoints))
                BuildLinearInteriorPoints(a, b, radialPoints, arcPoints);

            DrawElbowPointWithConnector(toCanvas, data, drawVertices, a, connectorColor, pointColor, radius, connectorWidth);
            DrawElbowPointWithConnector(toCanvas, data, drawVertices, b, connectorColor, pointColor, radius, connectorWidth);
            DrawElbowPointWithConnector(toCanvas, data, drawVertices, center, connectorColor, pointColor, radius, connectorWidth);
            for (var p = 0; p < arcPoints.Count; p++)
                DrawElbowPointWithConnector(toCanvas, data, drawVertices, arcPoints[p], connectorColor, pointColor, radius, connectorWidth);
        }

        Handles.color = prev;
    }

    private void DrawLimbContourPoints(
        Func<Vector2, Vector2> toCanvas,
        string selectedLayerKey,
        string layerStableKey,
        LayerVertexData data,
        Color pointColor,
        Color connectorColor)
    {
        if (string.IsNullOrEmpty(layerStableKey) || toCanvas == null || limbLines == null || limbLines.Count == 0)
            return;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return;

        var prev = Handles.color;
        const float connectorWidth = 1.3f;
        const float radius = 2.4f;
        var previewPoints = new List<Vector2>(64);
        var contourSteps = LimbContourSteps;
        var contourStepDistance = LimbContourStepDistance;
        var contourShift = LimbTowardCenterShift;

        for (var i = 0; i < limbLines.Count; i++)
        {
            var line = limbLines[i];
            if (line == null || !line.hasContourPoints)
                continue;
            if (!IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerKey, layerStableKey))
                continue;
            if (!TryBuildLimbPreviewPoints(line, data, contourSteps, contourStepDistance, contourShift, previewPoints))
                continue;

            if (TryGetLimbNearestBoneGuideSegment(line, out var guideStart, out var guideEnd))
            {
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.95f);
                Handles.DrawAAPolyLine(1.8f, toCanvas(guideStart), toCanvas(guideEnd));
            }

            for (var p = 0; p < previewPoints.Count; p++)
            {
                DrawElbowPointWithConnector(
                    toCanvas,
                    data,
                    drawVertices,
                    previewPoints[p],
                    connectorColor,
                    pointColor,
                    radius,
                    connectorWidth);
            }
        }

        Handles.color = prev;
    }

    private void DrawLimbPreviewPointsOnly(
        Func<Vector2, Vector2> toCanvas,
        LayerInfo selectedLayer,
        float alpha)
    {
        if (selectedLayer == null || toCanvas == null || alpha <= 0f)
            return;

        var selectedLayerKey = BuildSelectionKey(selectedLayer);
        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (string.IsNullOrEmpty(stableKey))
            return;
        if (!TryGetLayerVertexDataForKeys(selectedLayerKey, stableKey, out var data) || data == null)
            return;

        UpdateLimbLinesForLayer(selectedLayerKey, stableKey, data);

        var previewPoints = new List<Vector2>(64);
        var contourSteps = LimbContourSteps;
        var contourStepDistance = LimbContourStepDistance;
        var contourShift = LimbTowardCenterShift;
        var pointColor = new Color(0.55f, 0.92f, 1f, Mathf.Clamp01(alpha));
        const float pointRadius = 2.5f;

        Handles.BeginGUI();
        var prev = Handles.color;
        for (var i = 0; i < limbLines.Count; i++)
        {
            var line = limbLines[i];
            if (line == null || !line.hasContourPoints)
                continue;
            if (!IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerKey, stableKey))
                continue;
            if (!TryBuildLimbPreviewPoints(line, data, contourSteps, contourStepDistance, contourShift, previewPoints))
                continue;

            if (TryGetLimbNearestBoneGuideSegment(line, out var guideStart, out var guideEnd))
            {
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.95f);
                Handles.DrawAAPolyLine(1.8f, toCanvas(guideStart), toCanvas(guideEnd));
            }

            Handles.color = pointColor;
            for (var p = 0; p < previewPoints.Count; p++)
                Handles.DrawSolidDisc(toCanvas(previewPoints[p]), Vector3.forward, pointRadius);
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawAppliedElbowVertices(
        Func<Vector2, Vector2> toCanvas,
        string selectedLayerKey,
        string layerStableKey,
        LayerVertexData data,
        Vector2[] drawVertices,
        Color pointColor,
        Color connectorColor)
    {
        if (toCanvas == null || data == null || drawVertices == null || drawVertices.Length == 0)
            return;
        if (appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return;

        const float connectorWidth = 1.3f;
        const float radius = 2.4f;

        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
                continue;
            if (!IsAssetPathMatch(point.assetPath, cachedPath))
                continue;
            if (!IsLayerMatch(point.layerSelectionKey, point.layerStableKey, selectedLayerKey, layerStableKey))
                continue;

            DrawElbowPointWithConnector(
                toCanvas,
                data,
                drawVertices,
                point.positionDoc,
                connectorColor,
                pointColor,
                radius,
                connectorWidth);
        }
    }

    private void DrawElbowPointWithConnector(
        Func<Vector2, Vector2> toCanvas,
        LayerVertexData data,
        Vector2[] vertices,
        Vector2 pointDoc,
        Color connectorColor,
        Color pointColor,
        float pointRadius,
        float connectorWidth)
    {
        elbowConnectionBuffer.Clear();
        BuildAddVertexStyleConnections(data, vertices, pointDoc, elbowConnectionBuffer);

        var pointCanvas = toCanvas(pointDoc);
        Handles.color = connectorColor;
        for (var i = 0; i < elbowConnectionBuffer.Count; i++)
        {
            var index = elbowConnectionBuffer[i];
            if (index < 0 || index >= vertices.Length)
                continue;
            Handles.DrawAAPolyLine(connectorWidth, pointCanvas, toCanvas(vertices[index]));
        }

        Handles.color = pointColor;
        Handles.DrawSolidDisc(pointCanvas, Vector3.forward, pointRadius);
    }

    private static bool IsElbowLineForLayer(ElbowLine line, string selectionKey, string stableKey)
    {
        if (line == null)
            return false;

        return IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectionKey, stableKey);
    }

    private static bool IsLayerMatch(string itemSelectionKey, string itemStableKey, string selectionKey, string stableKey)
    {
        if (!string.IsNullOrEmpty(selectionKey) &&
            !string.IsNullOrEmpty(itemSelectionKey) &&
            string.Equals(itemSelectionKey, selectionKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(stableKey) && !string.IsNullOrEmpty(itemStableKey))
            return string.Equals(itemStableKey, stableKey, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private void DrawElbowLines(Func<Vector2, Vector2> toCanvas, LayerInfo selectedLayer, float alpha)
    {
        if (selectedLayer == null || toCanvas == null)
            return;
        var selectedLayerKey = BuildSelectionKey(selectedLayer);
        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (string.IsNullOrEmpty(stableKey))
            return;

        Handles.BeginGUI();
        var prev = Handles.color;
        var baseColor = new Color(1f, 0.85f, 0.25f, Mathf.Clamp01(alpha));

        for (var i = 0; i < elbowLines.Count; i++)
        {
            var line = elbowLines[i];
            if (line == null)
                continue;
            if (!IsElbowLineForLayer(line, selectedLayerKey, stableKey))
                continue;

            var lineColor = i == elbowSelectedLineIndex
                ? new Color(1f, 0.65f, 0.1f, Mathf.Clamp01(alpha + 0.1f))
                : baseColor;
            Handles.color = lineColor;
            var a = toCanvas(line.startDoc);
            var b = toCanvas(line.endDoc);
            var handleThickness = i == elbowSelectedLineIndex ? 3f : 2f;
            var handleRadius = BoneShapeDrawer.GetBaseRadius(handleThickness);
            Handles.DrawAAPolyLine(2.2f, a, b);
            Handles.DrawSolidDisc(a, Vector3.forward, handleRadius);
            Handles.DrawSolidDisc(b, Vector3.forward, handleRadius);
        }

        if (elbowIsCreating && string.Equals(elbowCreateLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
        {
            Handles.color = new Color(1f, 0.85f, 0.25f, Mathf.Clamp01(alpha * 0.9f));
            Handles.DrawAAPolyLine(2f, toCanvas(elbowCreateStartDoc), toCanvas(elbowCreateEndDoc));
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawLimbLines(Func<Vector2, Vector2> toCanvas, LayerInfo selectedLayer, float alpha)
    {
        if (selectedLayer == null || toCanvas == null)
            return;
        var selectedLayerKey = BuildSelectionKey(selectedLayer);
        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (string.IsNullOrEmpty(stableKey))
            return;

        Handles.BeginGUI();
        var prev = Handles.color;
        var baseColor = new Color(0.95f, 0.8f, 0.1f, Mathf.Clamp01(alpha));

        for (var i = 0; i < limbLines.Count; i++)
        {
            var line = limbLines[i];
            if (line == null)
                continue;
            if (!IsLayerMatch(line.layerSelectionKey, line.layerStableKey, selectedLayerKey, stableKey))
                continue;

            var lineColor = i == limbSelectedLineIndex
                ? new Color(1f, 0.58f, 0.08f, Mathf.Clamp01(alpha + 0.1f))
                : baseColor;
            Handles.color = lineColor;
            var a = toCanvas(line.startDoc);
            var b = toCanvas(line.endDoc);
            var handleThickness = i == limbSelectedLineIndex ? 3f : 2f;
            var handleRadius = BoneShapeDrawer.GetBaseRadius(handleThickness);
            Handles.DrawAAPolyLine(2.2f, a, b);
            Handles.DrawSolidDisc(a, Vector3.forward, handleRadius);
            Handles.DrawSolidDisc(b, Vector3.forward, handleRadius);

            if (TryGetLimbNearestBoneGuideSegment(line, out var guideStart, out var guideEnd, out var nearestBoneIndex))
            {
                var gs = toCanvas(guideStart);
                var ge = toCanvas(guideEnd);
                Handles.color = new Color(1f, 0.05f, 0.05f, 1f);
                Handles.DrawAAPolyLine(3f, gs, ge);
                Handles.DrawSolidDisc(gs, Vector3.forward, 2.4f);
                Handles.DrawSolidDisc(ge, Vector3.forward, 2.4f);

                if (TryGetLimbNearestChildGuideSegment(line, nearestBoneIndex, out var childGuideStart, out var childGuideEnd))
                {
                    var cgs = toCanvas(childGuideStart);
                    var cge = toCanvas(childGuideEnd);
                    Handles.color = new Color(1f, 0.25f, 0.85f, 1f);
                    Handles.DrawAAPolyLine(3f, cgs, cge);
                    Handles.DrawSolidDisc(cgs, Vector3.forward, 2.4f);
                    Handles.DrawSolidDisc(cge, Vector3.forward, 2.4f);
                }
            }
        }

        if (limbIsCreating && string.Equals(limbCreateLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
        {
            Handles.color = new Color(0.95f, 0.8f, 0.1f, Mathf.Clamp01(alpha * 0.9f));
            Handles.DrawAAPolyLine(2f, toCanvas(limbCreateStartDoc), toCanvas(limbCreateEndDoc));
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawBoobPreview(Func<Vector2, Vector2> toCanvas, LayerInfo selectedLayer, float alpha)
    {
        if (toCanvas == null)
            return;
        if (BoobToolVersion == 1)
        {
            DrawBoobV2Preview(toCanvas, alpha);
            return;
        }
        if (!boobHasPoint || selectedLayer == null)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        var selectedStableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!string.IsNullOrEmpty(boobLayerStableKey) &&
            !string.Equals(boobLayerStableKey, selectedStableKey, StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryGetBoobLayerData(out var sourceData, out _, out _) || sourceData == null)
            return;
        if (!TryBuildBoobGeometry(
                sourceData,
                false,
                out var previewVerticesBase,
                out var previewTriangles,
                out var previewAllEdgePairs,
                out var previewBoundaryEdgePairs,
                out var previewBoneWeights,
                out var previewSpriteBoneMap,
                out _))
            return;
        if (previewVerticesBase == null || previewVerticesBase.Length < 3)
            return;

        var vertexCount = previewVerticesBase.Length;
        var previewVerticesDraw = previewVerticesBase;
        var sourceDrawVertices = GetLayerVerticesForDraw(sourceData);
        if (sourceDrawVertices != null &&
            sourceData.vertices != null &&
            sourceDrawVertices.Length == sourceData.vertices.Length &&
            sourceData.triangles != null &&
            sourceData.triangles.Length >= 3)
        {
            previewVerticesDraw = new Vector2[vertexCount];
            for (var i = 0; i < vertexCount; i++)
            {
                var mapped = previewVerticesBase[i];
                if (PsdCoordinateSpace.TryMapPointBetweenMeshes(sourceData.vertices, sourceDrawVertices, sourceData.triangles, previewVerticesBase[i], out var drawMapped))
                    mapped = drawMapped;
                previewVerticesDraw[i] = mapped;
            }
        }

        DrawBoneInfluenceOnGeometryTriangles(
            toCanvas,
            previewVerticesDraw,
            previewBoneWeights,
            previewTriangles,
            previewSpriteBoneMap,
            alpha,
            int.MaxValue);

        var contourEdgeKeys = BuildEdgeKeySet(previewBoundaryEdgePairs, vertexCount);
        var innerLineColor = new Color(0.35f, 0.98f, 1f, Mathf.Clamp01(alpha));
        var boundaryLineColor = new Color(1f, 0.6f, 0.12f, Mathf.Clamp01(alpha));
        var pointColor = new Color(0.55f, 0.92f, 1f, Mathf.Clamp01(alpha));
        const float thickOffset = 0.65f;
        const float markerHalfSize = 2f;

        Handles.BeginGUI();
        var prev = Handles.color;

        var edgePairCount = previewAllEdgePairs != null ? previewAllEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexEdgeSegments, edgePairCount * 2);
        var edgeWrite = 0;
        for (var i = 0; i < edgePairCount; i++)
        {
            var a = previewAllEdgePairs[i * 2];
            var b = previewAllEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                continue;
            vertexEdgeSegments[edgeWrite++] = toCanvas(previewVerticesDraw[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvas(previewVerticesDraw[b]);
        }
        if (edgeWrite <= 1 && edgePairCount > 0)
        {
            edgeWrite = 0;
            for (var i = 0; i < edgePairCount; i++)
            {
                var a = previewAllEdgePairs[i * 2];
                var b = previewAllEdgePairs[i * 2 + 1];
                if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                    continue;
                vertexEdgeSegments[edgeWrite++] = toCanvas(previewVerticesDraw[a]);
                vertexEdgeSegments[edgeWrite++] = toCanvas(previewVerticesDraw[b]);
            }
        }
        if (edgeWrite > 1)
        {
            Handles.color = innerLineColor;
            Handles.DrawLines(vertexEdgeSegments);
        }

        var boundaryPairCount = previewBoundaryEdgePairs != null ? previewBoundaryEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexBoundarySegments, boundaryPairCount * 2);
        var boundaryWrite = 0;
        for (var i = 0; i < boundaryPairCount; i++)
        {
            var a = previewBoundaryEdgePairs[i * 2];
            var b = previewBoundaryEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            vertexBoundarySegments[boundaryWrite++] = toCanvas(previewVerticesDraw[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvas(previewVerticesDraw[b]);
        }
        if (boundaryWrite > 1)
        {
            Handles.color = boundaryLineColor;
            Handles.DrawLines(vertexBoundarySegments);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }

        EnsureVertexBuffer(ref vertexPointSegments, vertexCount * 4);
        var pointWrite = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = toCanvas(previewVerticesDraw[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }
        if (pointWrite > 1)
        {
            Handles.color = pointColor;
            Handles.DrawLines(vertexPointSegments);
        }

        var centerCanvas = toCanvas(previewVerticesDraw[vertexCount - 1]);
        Handles.color = new Color(1f, 0.1f, 0.1f, Mathf.Clamp01(alpha));
        Handles.DrawSolidDisc(centerCanvas, Vector3.forward, 3.3f);
        Handles.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 0.9f));
        Handles.DrawWireDisc(centerCanvas, Vector3.forward, 4.6f);

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawBoobV2Preview(Func<Vector2, Vector2> toCanvas, float alpha)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;
        if (!boobV2HasTargetA && !boobV2HasTargetB &&
            boobV2ContourADoc.Count < 2 && boobV2ContourBDoc.Count < 2 &&
            boobV2WorkingContourDoc.Count < 2)
        {
            return;
        }

        Handles.BeginGUI();
        var previousColor = Handles.color;
        var cyan = new Color(0.12f, 1f, 0.86f, Mathf.Clamp01(alpha));
        var magenta = new Color(1f, 0.24f, 0.72f, Mathf.Clamp01(alpha));
        if (boobV2HasTargetA)
            DrawBoobV2NippleTarget(toCanvas(boobV2TargetADoc), alpha);
        if (boobV2HasTargetB)
            DrawBoobV2NippleTarget(toCanvas(boobV2TargetBDoc), alpha);
        DrawBoobV2Contour(toCanvas, boobV2ContourADoc, cyan, true);
        DrawBoobV2Contour(toCanvas, boobV2ContourBDoc, magenta, true);
        if (boobV2WorkingContourDoc.Count >= 2)
        {
            var workingColor = boobV2DrawingContour == 1 ? magenta : cyan;
            DrawBoobV2Contour(toCanvas, boobV2WorkingContourDoc, workingColor, false);
        }
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private static void DrawBoobV2NippleTarget(Vector2 point, float alpha)
    {
        var opacity = Mathf.Clamp01(alpha);
        Handles.color = new Color(0f, 0f, 0f, opacity * 0.9f);
        Handles.DrawWireDisc(point, Vector3.forward, 12f);
        Handles.DrawWireDisc(point, Vector3.forward, 6.5f);
        Handles.DrawAAPolyLine(4f, point + Vector2.left * 15f, point + Vector2.right * 15f);
        Handles.DrawAAPolyLine(4f, point + Vector2.down * 15f, point + Vector2.up * 15f);

        Handles.color = new Color(0.2f, 1f, 0.25f, opacity);
        Handles.DrawWireDisc(point, Vector3.forward, 10.5f);
        Handles.DrawWireDisc(point, Vector3.forward, 5f);
        Handles.DrawAAPolyLine(2f, point + Vector2.left * 14f, point + Vector2.right * 14f);
        Handles.DrawAAPolyLine(2f, point + Vector2.down * 14f, point + Vector2.up * 14f);
        Handles.DrawSolidDisc(point, Vector3.forward, 2f);
    }

    private static void DrawBoobV2Contour(
        Func<Vector2, Vector2> toCanvas,
        IReadOnlyList<Vector2> contour,
        Color color,
        bool closed)
    {
        if (contour == null || contour.Count < 2)
            return;
        var extra = closed ? 1 : 0;
        var points = new Vector3[contour.Count + extra];
        for (var i = 0; i < contour.Count; i++)
            points[i] = toCanvas(contour[i]);
        if (closed)
            points[points.Length - 1] = points[0];

        Handles.color = new Color(0f, 0f, 0f, color.a * 0.9f);
        Handles.DrawAAPolyLine(6f, points);
        Handles.color = color;
        Handles.DrawAAPolyLine(3f, points);
        for (var i = 0; i < contour.Count; i++)
            Handles.DrawSolidDisc(points[i], Vector3.forward, 2f);
    }

}
#endif
