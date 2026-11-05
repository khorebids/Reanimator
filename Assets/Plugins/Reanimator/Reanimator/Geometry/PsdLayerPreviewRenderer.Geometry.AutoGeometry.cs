#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    public bool GenerateAutoGeometryForSelected(
        string assetPath,
        int outlineDetail,
        int alphaTolerance,
        int subdivide,
        bool generateWeights,
        ReanimatorAutoGeometryMode mode,
        ReanimatorReanimaxGeometryMode reanimaxMode,
        double quadDensity,
        float quadRotationDegrees,
        float reanimaxHardMaskAlphaThreshold,
        int reanimaxHardEdgePositionFilterPixels,
        float reanimaxHardEdgeMinTurnDegrees,
        int reanimaxHardEdgePostPositionFilterPixels,
        float reanimaxHardEdgeMinDistancePixels,
        int reanimaxHardNormalMidpointPositionFilterPixels,
        float reanimaxHardNormalInsetPixels,
        int reanimaxHardNormalInsetPositionFilterPixels,
        out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Select PSD/PSB first";
            return false;
        }

        if (string.IsNullOrEmpty(selectedLayerSelectionKey))
        {
            error = "Select sprite first";
            return false;
        }

        var selectedLayer = FindLayerBySelectionKey(layerInfos, selectedLayerSelectionKey);
        if (selectedLayer == null)
        {
            error = "Selected sprite is missing";
            return false;
        }

        var stableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
        if (!TryGetLayerVertexDataForKeys(selectedLayerSelectionKey, stableKey, out var layerData) || layerData == null)
        {
            error = "Sprite geometry data is missing";
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        var dataProvider = importer as ISpriteEditorDataProvider;
        if (importer == null || dataProvider == null)
        {
            error = "PSD importer data provider unavailable";
            return false;
        }

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            error = "Failed to initialize sprite data provider";
            return false;
        }

        var textureDataProvider = dataProvider.GetDataProvider<ITextureDataProvider>();
        if (textureDataProvider == null || textureDataProvider.texture == null)
        {
            error = "Texture data provider unavailable";
            return false;
        }

        var bindings = BuildLayerSpriteBindings(dataProvider);
        if (!TryResolveLayerBinding(bindings, selectedLayerSelectionKey, stableKey, out var binding, out _))
        {
            error = "Sprite binding not found";
            return false;
        }

        Vector2[] localVertices;
        int[] triangles;
        int[] allEdgePairs;
        int[] boundaryEdgePairs;
        var generated = false;
        switch (mode)
        {
            case ReanimatorAutoGeometryMode.QuadNet:
                generated = TryGenerateQuadNetGeometryLocal(
                    textureDataProvider,
                    binding,
                    outlineDetail,
                    alphaTolerance,
                    quadDensity,
                    quadRotationDegrees,
                    out localVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs);
                break;
            case ReanimatorAutoGeometryMode.Reanimax:
                generated = TryGenerateReanimaxGeometryLocal(
                    textureDataProvider,
                    binding,
                    out localVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs,
                    selectedLayer,
                    reanimaxMode,
                    reanimaxHardMaskAlphaThreshold,
                    reanimaxHardEdgePositionFilterPixels,
                    reanimaxHardEdgeMinTurnDegrees,
                    reanimaxHardEdgePostPositionFilterPixels,
                    reanimaxHardEdgeMinDistancePixels,
                    reanimaxHardNormalMidpointPositionFilterPixels,
                    reanimaxHardNormalInsetPixels,
                    reanimaxHardNormalInsetPositionFilterPixels,
                    out error);
                break;
            default:
                generated = TryGenerateAutoGeometryLocal(
                    textureDataProvider,
                    binding,
                    outlineDetail,
                    alphaTolerance,
                    subdivide,
                    out localVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs);
                break;
        }
        if (!generated)
        {
            if (mode == ReanimatorAutoGeometryMode.QuadNet)
            {
                var quadNetError = ConsumeQuadNetLastError();
                error = string.IsNullOrEmpty(quadNetError) ? "Failed to generate QuadNet geometry" : quadNetError;
            }
            else if (mode == ReanimatorAutoGeometryMode.Reanimax)
            {
                error = string.IsNullOrEmpty(error) ? "Failed to generate Reanimax geometry" : error;
            }
            else
            {
                error = "Failed to generate geometry";
            }
            return false;
        }

        var docVertices = new Vector2[localVertices.Length];
        for (var i = 0; i < localVertices.Length; i++)
            docVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(selectedLayer, localVertices[i]);

        var vertexUvs = BuildVertexUvsFromLocalPixels(selectedLayer, localVertices);
        var boneWeights = BuildAutoGeometryWeights(docVertices, layerData, generateWeights);

        RegisterUndo(mode == ReanimatorAutoGeometryMode.QuadNet
            ? "Generate QuadNet Geometry"
            : mode == ReanimatorAutoGeometryMode.Reanimax
                ? "Generate Reanimax Geometry"
                : "Generate Auto Geometry");
        ApplyAutoGeometryToLayerData(
            layerData,
            selectedLayer,
            docVertices,
            vertexUvs,
            triangles,
            allEdgePairs,
            boundaryEdgePairs,
            boneWeights);

        UpsertAutoGeometryPatch(
            assetPath,
            selectedLayerSelectionKey,
            stableKey,
            layerData,
            generateWeights);

        hasPendingChanges = true;
        return true;
    }

    private static bool TryGenerateAutoGeometryLocal(
        ITextureDataProvider textureDataProvider,
        LayerSpriteBinding binding,
        int outlineDetail,
        int alphaTolerance,
        int subdivide,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();

        if (textureDataProvider == null || textureDataProvider.texture == null || binding.rect.width <= 0f || binding.rect.height <= 0f)
            return false;

        textureDataProvider.GetTextureActualWidthAndHeight(out var actualWidth, out var actualHeight);
        if (actualWidth <= 0 || actualHeight <= 0)
            return false;

        var cappedTexture = textureDataProvider.texture;
        var scale = new Vector2(cappedTexture.width / (float)actualWidth, cappedTexture.height / (float)actualHeight);
        var scaleInv = new Vector2(1f / Mathf.Max(1e-4f, scale.x), 1f / Mathf.Max(1e-4f, scale.y));
        var scaledRect = binding.rect;
        scaledRect.min = Vector2.Scale(scaledRect.min, scale);
        scaledRect.max = Vector2.Scale(scaledRect.max, scale);

        var detail = Mathf.Clamp01(outlineDetail / 100f);
        var alpha = (byte)Mathf.Clamp(alphaTolerance, 0, 254);
        if (!TryGenerateOutlineWithAnimationModule(textureDataProvider, cappedTexture, scaledRect, detail, alpha, false, out var paths))
            return false;
        if (paths == null || paths.Length == 0)
            return false;

        var outlineSpace = ResolveGeneratedOutlineSpace(paths, scaleInv, binding.rect);
        var polygons = new List<List<Vector2>>(paths.Length);
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            if (!TryBuildPolygonLocalPoints(paths[pathIndex], scaleInv, binding.rect, outlineSpace, out var polygon))
                continue;

            polygons.Add(polygon);
        }

        if (polygons.Count == 0)
            return false;

        if (!TryTriangulateAutoGeometryPolygons(polygons, subdivide, out localVertices, out triangles))
            return false;
        triangles = SanitizeTriangles(triangles, localVertices.Length);
        if (triangles.Length < 3)
            return false;

        BuildEdgePairs(triangles, localVertices.Length, out allEdgePairs, out boundaryEdgePairs);
        return allEdgePairs.Length >= 2;
    }

    private static bool TryTriangulateAutoGeometryPolygons(
        IReadOnlyList<List<Vector2>> polygons,
        int subdivide,
        out Vector2[] vertices,
        out int[] triangles)
    {
        vertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        if (polygons == null || polygons.Count == 0)
            return false;

        var combinedVertices = new List<Vector2>(256);
        var combinedTriangles = new List<int>(512);
        for (var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
        {
            var polygon = polygons[polygonIndex];
            if (polygon == null || polygon.Count < 3)
                continue;

            var boundaryEdges = new List<Vector2Int>(polygon.Count);
            for (var i = 0; i < polygon.Count; i++)
                boundaryEdges.Add(new Vector2Int(i, (i + 1) % polygon.Count));

            if (!TryTriangulateAutoGeometryPolygon(polygon, boundaryEdges, subdivide, out var polygonVertices, out var polygonTriangles))
                continue;

            polygonTriangles = SanitizeTriangles(polygonTriangles, polygonVertices.Length);
            if (polygonVertices.Length < 3 || polygonTriangles.Length < 3)
                continue;

            var vertexOffset = combinedVertices.Count;
            combinedVertices.AddRange(polygonVertices);
            for (var i = 0; i < polygonTriangles.Length; i++)
                combinedTriangles.Add(vertexOffset + polygonTriangles[i]);
        }

        if (combinedVertices.Count < 3 || combinedTriangles.Count < 3)
            return false;

        vertices = combinedVertices.ToArray();
        triangles = combinedTriangles.ToArray();
        return true;
    }

    private static bool TryTriangulateAutoGeometryPolygon(
        IReadOnlyList<Vector2> polygon,
        IReadOnlyList<Vector2Int> boundaryEdges,
        int subdivide,
        out Vector2[] vertices,
        out int[] triangles)
    {
        vertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        if (polygon == null || polygon.Count < 3)
            return false;

        if (TryTriangulateWithUnityTessellation(polygon, boundaryEdges, subdivide, out vertices, out triangles, out _))
        {
            triangles = SanitizeTriangles(triangles, vertices.Length);
            if (vertices.Length >= 3 &&
                triangles.Length >= 3 &&
                AutoGeometryTriangulationCoversPolygon(polygon, vertices, triangles))
                return true;
        }

        vertices = CopyVector2List(polygon);
        var fallbackTriangles = new List<int>(Mathf.Max(3, (vertices.Length - 2) * 3));
        if (!ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(vertices, fallbackTriangles))
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            return false;
        }

        triangles = SanitizeTriangles(fallbackTriangles.ToArray(), vertices.Length);
        if (vertices.Length < 3 || triangles.Length < 3)
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            return false;
        }

        ReanimatorTriangulationAlgorithms.SubdivideGeneratedGeometry(ref vertices, ref triangles, subdivide);
        triangles = SanitizeTriangles(triangles, vertices.Length);
        return vertices.Length >= 3 && triangles.Length >= 3;
    }

    private static bool AutoGeometryTriangulationCoversPolygon(
        IReadOnlyList<Vector2> polygon,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> triangles)
    {
        if (polygon == null || vertices == null || triangles == null || polygon.Count < 3 || vertices.Count < 3 || triangles.Count < 3)
            return false;

        var polygonArea = Mathf.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(polygon));
        if (polygonArea <= 0.0001f)
            return false;

        var triangleArea = 0f;
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Count || ib >= vertices.Count || ic >= vertices.Count)
                continue;

            triangleArea += Mathf.Abs(SignedAutoGeometryTriangleArea(vertices[ia], vertices[ib], vertices[ic])) * 0.5f;
        }

        return triangleArea >= polygonArea * 0.6f;
    }

    private static float SignedAutoGeometryTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private bool TryGenerateReanimaxGeometryLocal(
        ITextureDataProvider textureDataProvider,
        LayerSpriteBinding binding,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs,
        LayerInfo selectedLayer,
        ReanimatorReanimaxGeometryMode mode,
        float alphaThreshold,
        int edgePositionFilterPixels,
        float edgeMinTurnDegrees,
        int edgePostPositionFilterPixels,
        float edgeMinDistancePixels,
        int normalMidpointPositionFilterPixels,
        float normalInsetPixels,
        int normalInsetPositionFilterPixels,
        out string error)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();
        error = null;

        if (mode == ReanimatorReanimaxGeometryMode.Soft)
        {
            error = "Reanimax Soft: reserved for later";
            return false;
        }

        if (textureDataProvider == null || textureDataProvider.texture == null)
        {
            error = "Reanimax: texture data unavailable";
            return false;
        }

        if (binding.rect.width <= 0f || binding.rect.height <= 0f)
        {
            error = "Reanimax: selected sprite rect is empty";
            return false;
        }

        if (selectedLayer == null || selectedLayer.sourceRect.width <= 0f || selectedLayer.sourceRect.height <= 0f)
        {
            error = "Reanimax: selected layer is unavailable";
            return false;
        }

        var paths = GetReanimaxHardEdgePointPathsLocal(
            selectedLayer,
            alphaThreshold,
            edgePositionFilterPixels,
            edgeMinTurnDegrees,
            edgePostPositionFilterPixels,
            edgeMinDistancePixels);
        if (paths == null || paths.Length == 0)
        {
            error = "Reanimax: no contour points";
            return false;
        }

        var vertices = new List<Vector2>(512);
        var boundaryEdges = new List<Vector2Int>(512);
        var uniqueVertices = new Dictionary<long, int>();
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var points = paths[pathIndex];
            if (points == null || points.Length < 3)
                continue;

            var indexMap = new List<int>(points.Length);
            for (var i = 0; i < points.Length; i++)
            {
                if (!AddUniqueReanimaxHardGeometryVertex(vertices, uniqueVertices, points[i], out var index))
                    continue;
                indexMap.Add(index);
            }

            if (indexMap.Count < 3)
                continue;

            for (var i = 0; i < indexMap.Count; i++)
                boundaryEdges.Add(new Vector2Int(indexMap[i], indexMap[(i + 1) % indexMap.Count]));
        }

        if (vertices.Count < 3 || boundaryEdges.Count < 3)
        {
            error = "Reanimax: contour is too small";
            return false;
        }

        var layerBounds = new Rect(0f, 0f, selectedLayer.sourceRect.width, selectedLayer.sourceRect.height);
        var internalPoints = BuildReanimaxHardInternalPointsLocal(
            paths,
            layerBounds,
            normalMidpointPositionFilterPixels,
            normalInsetPixels,
            normalInsetPositionFilterPixels);
        for (var i = 0; i < internalPoints.Count; i++)
            AddUniqueReanimaxHardGeometryPoint(vertices, uniqueVertices, internalPoints[i]);

        if (!TryTriangulateWithUnityTessellation(vertices, boundaryEdges, 0, out localVertices, out triangles, out _))
        {
            error = "Reanimax: triangulation failed";
            return false;
        }

        triangles = SanitizeTriangles(triangles, localVertices.Length);
        if (triangles.Length < 3)
        {
            error = "Reanimax: no valid triangles";
            return false;
        }

        BuildEdgePairs(triangles, localVertices.Length, out allEdgePairs, out var triangleBoundaryPairs);
        boundaryEdgePairs = FlattenReanimaxHardBoundaryEdges(boundaryEdges, localVertices.Length, triangleBoundaryPairs);
        return allEdgePairs.Length >= 2;
    }

    private static bool AddUniqueReanimaxHardGeometryVertex(
        List<Vector2> vertices,
        Dictionary<long, int> keys,
        Vector2 point,
        out int index)
    {
        index = -1;
        if (vertices == null || keys == null)
            return false;

        var x = Mathf.RoundToInt(point.x * 100f);
        var y = Mathf.RoundToInt(point.y * 100f);
        var key = ((long)x << 32) ^ (uint)y;
        if (keys.TryGetValue(key, out index))
            return true;

        index = vertices.Count;
        keys[key] = index;
        vertices.Add(point);
        return true;
    }

    private static bool AddUniqueReanimaxHardGeometryPoint(
        List<Vector2> vertices,
        Dictionary<long, int> keys,
        Vector2 point)
    {
        if (vertices == null || keys == null)
            return false;

        var x = Mathf.RoundToInt(point.x * 100f);
        var y = Mathf.RoundToInt(point.y * 100f);
        var key = ((long)x << 32) ^ (uint)y;
        if (keys.ContainsKey(key))
            return false;

        keys[key] = vertices.Count;
        vertices.Add(point);
        return true;
    }

    private static int[] FlattenReanimaxHardBoundaryEdges(
        IReadOnlyList<Vector2Int> boundaryEdges,
        int vertexCount,
        int[] fallbackBoundaryPairs)
    {
        if (boundaryEdges == null || boundaryEdges.Count < 3)
            return fallbackBoundaryPairs ?? Array.Empty<int>();

        var result = new List<int>(boundaryEdges.Count * 2);
        for (var i = 0; i < boundaryEdges.Count; i++)
        {
            var edge = boundaryEdges[i];
            if (edge.x < 0 || edge.y < 0 || edge.x >= vertexCount || edge.y >= vertexCount || edge.x == edge.y)
                continue;
            result.Add(edge.x);
            result.Add(edge.y);
        }

        return result.Count >= 6 ? result.ToArray() : fallbackBoundaryPairs ?? Array.Empty<int>();
    }

    private static bool TryGenerateQuadNetGeometryLocal(
        ITextureDataProvider textureDataProvider,
        LayerSpriteBinding binding,
        int outlineDetail,
        int alphaTolerance,
        double density,
        float rotationDegrees,
        out Vector2[] localVertices,
        out int[] triangles,
        out int[] allEdgePairs,
        out int[] boundaryEdgePairs)
    {
        localVertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        allEdgePairs = Array.Empty<int>();
        boundaryEdgePairs = Array.Empty<int>();

        try
        {
            return TryGenerateQuadNetGeometryLocalImpl(
                textureDataProvider,
                binding,
                outlineDetail,
                alphaTolerance,
                density,
                rotationDegrees,
                out localVertices,
                out triangles,
                out allEdgePairs,
                out boundaryEdgePairs);
        }
        catch
        {
            localVertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            allEdgePairs = Array.Empty<int>();
            boundaryEdgePairs = Array.Empty<int>();
            return false;
        }
    }

    private static bool TryBuildPolygonLocalPoints(
        IReadOnlyList<Vector2> rawPath,
        Vector2 scaleInv,
        Rect rect,
        out List<Vector2> polygon)
    {
        return TryBuildPolygonLocalPoints(
            rawPath,
            scaleInv,
            rect,
            null,
            out polygon);
    }

    private static bool TryBuildPolygonLocalPoints(
        IReadOnlyList<Vector2> rawPath,
        Vector2 scaleInv,
        Rect rect,
        GeneratedOutlineSpace? forcedOutlineSpace,
        out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();
        if (rawPath == null || rawPath.Count < 3)
            return false;

        var rawBounds = Rect.zero;
        var scaledPoints = new Vector2[rawPath.Count];
        for (var i = 0; i < rawPath.Count; i++)
        {
            var p = Vector2.Scale(rawPath[i], scaleInv);
            scaledPoints[i] = p;
            if (i == 0)
            {
                rawBounds = new Rect(p.x, p.y, 0f, 0f);
            }
            else
            {
                rawBounds.xMin = Mathf.Min(rawBounds.xMin, p.x);
                rawBounds.yMin = Mathf.Min(rawBounds.yMin, p.y);
                rawBounds.xMax = Mathf.Max(rawBounds.xMax, p.x);
                rawBounds.yMax = Mathf.Max(rawBounds.yMax, p.y);
            }
        }

        var outlineSpace = forcedOutlineSpace ?? ResolveGeneratedOutlineSpace(scaledPoints, rawBounds, rect);
        var rectOffset = rect.size * 0.5f;
        for (var i = 0; i < rawPath.Count; i++)
        {
            var p = scaledPoints[i];
            switch (outlineSpace)
            {
                case GeneratedOutlineSpace.Atlas:
                    p -= rect.position;
                    break;
                case GeneratedOutlineSpace.Centered:
                    p += rectOffset;
                    break;
            }

            p = ClampGeneratedOutlinePointToRect(p, rect);
            if (polygon.Count > 0 && (polygon[polygon.Count - 1] - p).sqrMagnitude <= 0.0001f)
                continue;
            polygon.Add(p);
        }

        while (polygon.Count > 1 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude <= 0.0001f)
            polygon.RemoveAt(polygon.Count - 1);

        return polygon.Count >= 3;
    }

    private static GeneratedOutlineSpace ResolveGeneratedOutlineSpace(
        IReadOnlyList<Vector2[]> paths,
        Vector2 scaleInv,
        Rect rect)
    {
        if (paths == null || paths.Count == 0)
            return ResolveGeneratedOutlineSpace(Rect.zero, rect);

        var points = new List<Vector2>();
        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            if (path == null)
                continue;
            for (var i = 0; i < path.Length; i++)
                points.Add(Vector2.Scale(path[i], scaleInv));
        }

        if (points.Count == 0)
            return ResolveGeneratedOutlineSpace(Rect.zero, rect);

        return ResolveGeneratedOutlineSpace(points, BuildBounds(points), rect);
    }

    private static Rect BuildBounds(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return Rect.zero;

        var min = points[0];
        var max = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Vector2 ClampGeneratedOutlinePointToRect(Vector2 point, Rect rect)
    {
        return new Vector2(
            Mathf.Clamp(point.x, 0f, Mathf.Max(0f, rect.width)),
            Mathf.Clamp(point.y, 0f, Mathf.Max(0f, rect.height)));
    }

    private enum GeneratedOutlineSpace
    {
        Local,
        Atlas,
        Centered
    }

    private static GeneratedOutlineSpace ResolveGeneratedOutlineSpace(IReadOnlyList<Vector2> points, Rect rawBounds, Rect rect)
    {
        if (points == null || points.Count == 0)
            return ResolveGeneratedOutlineSpace(rawBounds, rect);

        var atlasScore = ScoreGeneratedOutlineSpace(points, rect, GeneratedOutlineSpace.Atlas);
        var localScore = ScoreGeneratedOutlineSpace(points, rect, GeneratedOutlineSpace.Local);
        var centeredScore = ScoreGeneratedOutlineSpace(points, rect, GeneratedOutlineSpace.Centered);

        if (atlasScore <= localScore && atlasScore <= centeredScore)
            return GeneratedOutlineSpace.Atlas;
        if (localScore <= centeredScore)
            return GeneratedOutlineSpace.Local;
        return GeneratedOutlineSpace.Centered;
    }

    private static GeneratedOutlineSpace ResolveGeneratedOutlineSpace(Rect rawBounds, Rect rect)
    {
        var localRect = new Rect(0f, 0f, Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        var atlasScore = RectCenterDistanceSqr(rawBounds, rect);
        var localScore = RectCenterDistanceSqr(rawBounds, localRect);
        var centeredScore = rawBounds.center.sqrMagnitude;

        if (atlasScore <= localScore && atlasScore <= centeredScore)
            return GeneratedOutlineSpace.Atlas;
        if (localScore <= centeredScore)
            return GeneratedOutlineSpace.Local;
        return GeneratedOutlineSpace.Centered;
    }

    private static float ScoreGeneratedOutlineSpace(IReadOnlyList<Vector2> points, Rect rect, GeneratedOutlineSpace space)
    {
        var localRect = new Rect(0f, 0f, Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        var rectOffset = rect.size * 0.5f;
        var outsidePenalty = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            switch (space)
            {
                case GeneratedOutlineSpace.Atlas:
                    p -= rect.position;
                    break;
                case GeneratedOutlineSpace.Centered:
                    p += rectOffset;
                    break;
            }

            outsidePenalty += DistanceOutsideRectSqr(p, localRect);
        }

        return outsidePenalty + (space == GeneratedOutlineSpace.Centered ? 0.001f : 0f);
    }

    private static float DistanceOutsideRectSqr(Vector2 point, Rect rect)
    {
        var dx = 0f;
        if (point.x < rect.xMin)
            dx = rect.xMin - point.x;
        else if (point.x > rect.xMax)
            dx = point.x - rect.xMax;

        var dy = 0f;
        if (point.y < rect.yMin)
            dy = rect.yMin - point.y;
        else if (point.y > rect.yMax)
            dy = point.y - rect.yMax;

        return dx * dx + dy * dy;
    }

    private static float RectCenterDistanceSqr(Rect a, Rect b)
    {
        return (a.center - b.center).sqrMagnitude;
    }

    private static Vertex2DMetaData[] BuildAutoGeometryMetaVertices(
        LayerVertexData data,
        LayerSpriteBinding binding,
        IReadOnlyList<Vertex2DMetaData> currentImporterVertices,
        IReadOnlyList<int> currentImporterIndices,
        string forcedImporterMode,
        out string importerMode)
    {
        importerMode = "raw_local";
        if (data == null || data.layer == null || data.vertices == null || data.vertices.Length == 0)
            return Array.Empty<Vertex2DMetaData>();

        if (ReanimatorAssetChangeAlgorithms.TryUseForcedAutoGeometryImporterMode(forcedImporterMode, out var forcedMode))
            importerMode = ReanimatorImporterGeometryAlgorithms.SanitizeImporterWriteMode(forcedMode);
        else if (!string.IsNullOrWhiteSpace(data.importerMode))
            importerMode = ReanimatorImporterGeometryAlgorithms.SanitizeImporterWriteMode(data.importerMode);

        var result = new Vertex2DMetaData[data.vertices.Length];
        for (var i = 0; i < data.vertices.Length; i++)
        {
            result[i].position = ConvertDocToImporterPositionByMode(data.layer, binding, data.vertices[i], importerMode);
            if (data.boneWeights != null && i < data.boneWeights.Length)
                result[i].boneWeight = data.boneWeights[i];
            else
                result[i].boneWeight = default;
        }

        return result;
    }

    private static Vector2 ConvertDocToImporterPositionByMode(
        LayerInfo layer,
        LayerSpriteBinding binding,
        Vector2 docPos,
        string importerMode)
    {
        return PsdCoordinateSpace.LayerDocumentToImporterPositionByMode(
            layer,
            docPos,
            binding.pixelsPerUnit,
            binding.pivotPixels,
            importerMode);
    }

    private static T[] CloneArray<T>(T[] source)
    {
        if (source == null)
            return null;
        if (source.Length == 0)
            return Array.Empty<T>();
        var copy = new T[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static bool TryTriangulateWithUnityTessellation(
        IReadOnlyList<Vector2> sourceVertices,
        IReadOnlyList<Vector2Int> sourceEdges,
        int subdivide,
        out Vector2[] vertices,
        out int[] triangles,
        out Vector2Int[] edges)
    {
        vertices = Array.Empty<Vector2>();
        triangles = Array.Empty<int>();
        edges = Array.Empty<Vector2Int>();
        if (sourceVertices == null || sourceEdges == null || sourceVertices.Count < 3 || sourceEdges.Count < 3)
            return false;

        var triangulationType = FindLoadedType("UnityEditor.U2D.Animation.TriangulationUtility");
        if (triangulationType == null)
            return false;

        var floatVertices = new float2[sourceVertices.Count];
        for (var i = 0; i < sourceVertices.Count; i++)
            floatVertices[i] = sourceVertices[i];
        var intEdges = new int2[sourceEdges.Count];
        for (var i = 0; i < sourceEdges.Count; i++)
            intEdges[i] = new int2(sourceEdges[i].x, sourceEdges[i].y);

        try
        {
            if (Mathf.Clamp(subdivide, 0, 100) > 0)
            {
                var tessellateMethod = triangulationType.GetMethod(
                    "TessellateSafe",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(float),
                        typeof(float),
                        typeof(int),
                        typeof(int),
                        typeof(float2[]).MakeByRefType(),
                        typeof(int2[]).MakeByRefType(),
                        typeof(int[]).MakeByRefType()
                    },
                    null);
                if (tessellateMethod == null)
                    return false;

                var largestAreaFactor = Mathf.Lerp(0.5f, 0.05f, Mathf.Clamp01(subdivide / 100f));
                var args = new object[] { largestAreaFactor, 0f, 10, 100, floatVertices, intEdges, null };
                tessellateMethod.Invoke(null, args);
                floatVertices = args[4] as float2[] ?? Array.Empty<float2>();
                intEdges = args[5] as int2[] ?? Array.Empty<int2>();
                triangles = args[6] as int[] ?? Array.Empty<int>();
            }
            else
            {
                var triangulateSafeMethod = triangulationType.GetMethod(
                    "TriangulateSafe",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(float2[]).MakeByRefType(),
                        typeof(int2[]).MakeByRefType(),
                        typeof(int[]).MakeByRefType()
                    },
                    null);
                var triangulateOk = false;
                if (triangulateSafeMethod != null)
                {
                    var args = new object[] { floatVertices, intEdges, null };
                    var ret = triangulateSafeMethod.Invoke(null, args);
                    triangulateOk = ret is bool value && value;
                    floatVertices = args[0] as float2[] ?? Array.Empty<float2>();
                    intEdges = args[1] as int2[] ?? Array.Empty<int2>();
                    triangles = args[2] as int[] ?? Array.Empty<int>();
                }

                if (!triangulateOk || triangles.Length < 3)
                {
                    var triangulateMethod = triangulationType.GetMethod(
                        "Triangulate",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[]
                        {
                            typeof(int2[]).MakeByRefType(),
                            typeof(float2[]).MakeByRefType(),
                            typeof(int[]).MakeByRefType(),
                            typeof(Allocator)
                        },
                        null);
                    if (triangulateMethod == null)
                        return false;

                    var args = new object[] { intEdges, floatVertices, null, Allocator.Persistent };
                    triangulateMethod.Invoke(null, args);
                    intEdges = args[0] as int2[] ?? Array.Empty<int2>();
                    floatVertices = args[1] as float2[] ?? Array.Empty<float2>();
                    triangles = args[2] as int[] ?? Array.Empty<int>();
                }
            }
        }
        catch
        {
            vertices = Array.Empty<Vector2>();
            triangles = Array.Empty<int>();
            edges = Array.Empty<Vector2Int>();
            return false;
        }

        if (floatVertices.Length < 3 || triangles.Length < 3)
            return false;

        vertices = new Vector2[floatVertices.Length];
        for (var i = 0; i < floatVertices.Length; i++)
            vertices[i] = floatVertices[i];
        edges = new Vector2Int[intEdges.Length];
        for (var i = 0; i < intEdges.Length; i++)
            edges[i] = new Vector2Int(intEdges[i].x, intEdges[i].y);
        return true;
    }

    private static Type FindLoadedType(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
            return null;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType(fullTypeName, false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static bool TryGenerateOutlineWithAnimationModule(
        ITextureDataProvider textureDataProvider,
        Texture2D texture,
        Rect rect,
        float detail,
        byte alphaTolerance,
        bool holeDetection,
        out Vector2[][] paths)
    {
        paths = Array.Empty<Vector2[]>();
        if (textureDataProvider != null)
        {
            try
            {
                var outlineGeneratorType = FindLoadedType("UnityEditor.U2D.Animation.OutlineGenerator");
                var textureDataProviderType = FindLoadedType("UnityEditor.U2D.Sprites.ITextureDataProvider");
                if (outlineGeneratorType != null && textureDataProviderType != null)
                {
                    var method = outlineGeneratorType.GetMethod(
                        "GenerateOutline",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[]
                        {
                            textureDataProviderType,
                            typeof(Rect),
                            typeof(float),
                            typeof(byte),
                            typeof(bool),
                            typeof(Vector2[][]).MakeByRefType()
                        },
                        null);
                    if (method != null)
                    {
                        var instance = Activator.CreateInstance(outlineGeneratorType, true);
                        var args = new object[] { textureDataProvider, rect, detail, alphaTolerance, holeDetection, null };
                        method.Invoke(instance, args);
                        paths = args[5] as Vector2[][] ?? Array.Empty<Vector2[]>();
                        if (paths.Length > 0)
                            return true;
                    }
                }
            }
            catch
            {
                paths = Array.Empty<Vector2[]>();
            }
        }

        return TryGenerateOutline(texture, rect, detail, alphaTolerance, holeDetection, out paths);
    }

    private static bool TryGenerateOutline(
        Texture2D texture,
        Rect rect,
        float detail,
        byte alphaTolerance,
        bool holeDetection,
        out Vector2[][] paths)
    {
        paths = Array.Empty<Vector2[]>();
        if (texture == null)
            return false;

        var spriteUtilityType = Type.GetType("UnityEditor.Sprites.SpriteUtility, UnityEditor");
        if (spriteUtilityType == null)
            return false;

        var method = spriteUtilityType.GetMethod(
            "GenerateOutline",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(Texture2D),
                typeof(Rect),
                typeof(float),
                typeof(byte),
                typeof(bool),
                typeof(Vector2[][]).MakeByRefType()
            },
            null);
        if (method == null)
            return false;

        try
        {
            var args = new object[] { texture, rect, detail, alphaTolerance, holeDetection, null };
            method.Invoke(null, args);
            paths = args[5] as Vector2[][] ?? Array.Empty<Vector2[]>();
            return paths.Length > 0;
        }
        catch
        {
            paths = Array.Empty<Vector2[]>();
            return false;
        }
    }
}
#endif
