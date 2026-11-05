#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool TryCreateEditGeometryEdge(int vertexA, int vertexB, out string error)
    {
        error = null;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
        {
            error = "Select sprite geometry first";
            return false;
        }

        if (data == null || data.vertices == null || data.vertices.Length < 3)
        {
            error = "Geometry is empty";
            return false;
        }

        if (vertexA < 0 || vertexB < 0 || vertexA >= data.vertices.Length || vertexB >= data.vertices.Length)
        {
            error = "Invalid vertex index";
            return false;
        }

        if (vertexA == vertexB)
        {
            error = "Pick two different vertices";
            return false;
        }

        if (HasEdge(data.allEdgePairs, vertexA, vertexB) || HasTriangleEdge(data.triangles, vertexA, vertexB))
        {
            error = "Edge already exists";
            return false;
        }

        var boundaryPairs = data.boundaryEdgePairs;
        if ((boundaryPairs == null || boundaryPairs.Length < 6) && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var fallbackBoundaryPairs);
            boundaryPairs = fallbackBoundaryPairs;
        }

        if (boundaryPairs == null || boundaryPairs.Length < 6)
        {
            error = "Failed to resolve contour";
            return false;
        }

        var sourceVertices = new List<Vector2>(data.vertices);
        var constrainedEdges = new List<Vector2Int>(boundaryPairs.Length / 2 + 1);
        var uniqueEdges = new HashSet<ulong>();
        for (var i = 0; i + 1 < boundaryPairs.Length; i += 2)
        {
            var a = boundaryPairs[i];
            var b = boundaryPairs[i + 1];
            if (!TryAddUniqueConstraintEdge(constrainedEdges, uniqueEdges, a, b, data.vertices.Length))
                continue;
        }

        TryAddUniqueConstraintEdge(constrainedEdges, uniqueEdges, vertexA, vertexB, data.vertices.Length);
        if (constrainedEdges.Count < 3)
        {
            error = "Not enough constraint edges";
            return false;
        }

        if (!TryTriangulateWithUnityTessellation(sourceVertices, constrainedEdges, 0, out var triangulatedVertices, out var triangulatedTriangles, out _))
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        triangulatedTriangles = SanitizeTriangles(triangulatedTriangles, triangulatedVertices.Length);
        if (triangulatedTriangles == null || triangulatedTriangles.Length < 3 || triangulatedVertices.Length != data.vertices.Length)
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        if (!TryBuildVertexRemap(sourceVertices, triangulatedVertices, out var sourceToTri))
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        if (!TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out var triToSource))
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        var remappedTriangles = new int[triangulatedTriangles.Length];
        for (var i = 0; i < triangulatedTriangles.Length; i++)
        {
            var triIndex = triangulatedTriangles[i];
            if (triIndex < 0 || triIndex >= triToSource.Length)
            {
                error = "Triangulation remap failed";
                return false;
            }
            remappedTriangles[i] = triToSource[triIndex];
        }

        remappedTriangles = SanitizeTriangles(remappedTriangles, data.vertices.Length);
        if (remappedTriangles.Length < 3)
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        if (!HasTriangleEdge(remappedTriangles, vertexA, vertexB))
        {
            return TryCreateEditGeometryEdgeFallback(data, selectionKey, stableKey, vertexA, vertexB, out error);
        }

        ApplyCreatedEditGeometryEdge(data, selectionKey, stableKey, vertexA, vertexB, remappedTriangles);
        return true;
    }

    public bool TrySplitEditGeometryEdge(int vertexA, int vertexB, float t, out string error)
    {
        error = null;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
        {
            error = "Select sprite geometry first";
            return false;
        }

        if (data == null || data.vertices == null || data.vertices.Length < 2)
        {
            error = "Geometry is empty";
            return false;
        }

        if (vertexA < 0 || vertexB < 0 || vertexA >= data.vertices.Length || vertexB >= data.vertices.Length || vertexA == vertexB)
        {
            error = "Invalid edge";
            return false;
        }

        if (!HasEdge(data.allEdgePairs, vertexA, vertexB) && !HasTriangleEdge(data.triangles, vertexA, vertexB))
        {
            error = "Pick an existing edge";
            return false;
        }

        t = Mathf.Clamp01(t);
        var point = Vector2.Lerp(data.vertices[vertexA], data.vertices[vertexB], t);
        var oldVertices = data.vertices;
        var oldCount = oldVertices.Length;
        var newIndex = oldCount;

        RegisterUndo("Split Edge");

        Array.Resize(ref data.vertices, oldCount + 1);
        data.vertices[newIndex] = point;

        if (data.vertexUvs == null || data.vertexUvs.Length != oldCount)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, oldVertices);
        Array.Resize(ref data.vertexUvs, oldCount + 1);
        data.vertexUvs[newIndex] = ComputeUvForPoint(data.layer, point);

        EnsureBoneWeightsArraySize(data, oldCount);
        if (data.boneWeights != null && data.boneWeights.Length == oldCount)
        {
            Array.Resize(ref data.boneWeights, oldCount + 1);
            data.boneWeights[newIndex] = InterpolateBoneWeight(data.boneWeights[vertexA], data.boneWeights[vertexB], t, data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0);
        }

        if (data.deformedVertices != null && data.deformedVertices.Length == oldCount)
        {
            Array.Resize(ref data.deformedVertices, oldCount + 1);
            data.deformedVertices[newIndex] = point;
        }

        var splitTriangles = data.triangles != null && data.triangles.Length >= 3 &&
                             SplitTrianglesOnEdge(ref data.triangles, oldVertices, point, newIndex, vertexA, vertexB);
        ReplaceSplitEdgePair(ref data.allEdgePairs, vertexA, vertexB, newIndex);
        ReplaceSplitEdgePair(ref data.boundaryEdgePairs, vertexA, vertexB, newIndex);
        if (splitTriangles)
        {
            OptimizeLayerTriangleQuality(data);
            BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out var triangleBoundaryPairs);
            if (data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 2)
                data.boundaryEdgePairs = triangleBoundaryPairs;
        }
        else
        {
            AddEdgePairUnique(ref data.allEdgePairs, vertexA, newIndex);
            AddEdgePairUnique(ref data.allEdgePairs, newIndex, vertexB);
        }

        MarkSkinningWeightsChanged(data);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        ApplyEditGeometryVertexSelection(selectionKey, stableKey, newIndex);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public bool RetriangulateSelectedGeometry(out string layerName, out int triangleCount, out string error)
    {
        layerName = string.Empty;
        triangleCount = 0;
        error = null;
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
        {
            error = "Select sprite geometry first";
            return false;
        }

        if (data == null)
        {
            error = "Geometry is unavailable";
            return false;
        }

        layerName = data.layer != null && !string.IsNullOrWhiteSpace(data.layer.name)
            ? data.layer.name
            : "Sprite";

        var boundaryPairs = data.boundaryEdgePairs;
        if ((boundaryPairs == null || boundaryPairs.Length < 6) && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, data.vertices != null ? data.vertices.Length : 0, out _, out boundaryPairs);
            data.boundaryEdgePairs = boundaryPairs;
        }

        RegisterUndo("Retriangulate Geometry");
        data.boundaryEdgePairs = boundaryPairs;
        CollapseNearDuplicateRetriangulationVertices(data);
        if (!TryRetriangulateKeepingVertexSet(data) &&
            !TryRetriangulateWithDirectFallback(data) &&
            !TryRetriangulateUsingSanitizedBoundary(data) &&
            !TryRetriangulateUsingConvexRecovery(data))
            RebuildRetriangulationFallbackQuad(data);

        SanitizeRetriangulationTrianglesByArea(data);
        OptimizeLayerTriangleQuality(data);
        RefineRetriangulationLongEdges(data);
        OptimizeLayerTriangleQuality(data);
        // Edge flips may introduce a new long diagonal. Finish by enforcing the
        // local edge-length limit once more so RETri cannot leave visible fans.
        RefineRetriangulationLongEdges(data);
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        SanitizeRetriangulationTrianglesByArea(data);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            if (!TryRetriangulateUsingConvexRecovery(data))
                RebuildRetriangulationFallbackQuad(data);
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);

        triangleCount = data.triangles.Length / 3;
        return true;
    }

    private int CollapseNearDuplicateRetriangulationVertices(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return 0;

        var min = data.vertices[0];
        var max = data.vertices[0];
        for (var i = 1; i < data.vertices.Length; i++)
        {
            min = Vector2.Min(min, data.vertices[i]);
            max = Vector2.Max(max, data.vertices[i]);
        }

        // PSD geometry is stored in document pixels. Scale the tolerance with
        // the sprite size, while never collapsing deliberately close detail.
        var distance = Mathf.Clamp(Vector2.Distance(min, max) * 0.001f, 0.01f, 0.5f);
        if (!TryBuildVertexCullClusters(data.vertices, distance, out var groups, out var remap) ||
            groups == null || remap == null || groups.Count < 3 || groups.Count >= data.vertices.Length)
        {
            return 0;
        }

        var oldCount = data.vertices.Length;
        var newVertices = new Vector2[groups.Count];
        var newUvs = new Vector2[groups.Count];
        var newWeights = new BoneWeight[groups.Count];
        var hasWeights = data.boneWeights != null && data.boneWeights.Length == oldCount;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            newVertices[groupIndex] = AverageVertexCullPoints(data.vertices, group);
            newUvs[groupIndex] = AverageVertexCullUvs(data, group, newVertices[groupIndex]);
            newWeights[groupIndex] = hasWeights
                ? MergeVertexCullBoneWeights(data.boneWeights, group)
                : default;
        }

        data.vertices = newVertices;
        data.vertexUvs = newUvs;
        data.boneWeights = newWeights;
        data.triangles = RemapVertexCullTriangles(data.triangles, remap, newVertices.Length);
        data.allEdgePairs = RemapVertexCullEdgePairs(data.allEdgePairs, remap, newVertices.Length);
        data.boundaryEdgePairs = RemapVertexCullEdgePairs(data.boundaryEdgePairs, remap, newVertices.Length);
        data.triangles = SanitizeTriangles(data.triangles, newVertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        return oldCount - newVertices.Length;
    }

    private static int SanitizeRetriangulationTrianglesByArea(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.triangles == null || data.triangles.Length < 3)
            return 0;

        const float minimumDoubleArea = 0.000002f;
        var source = data.triangles;
        var valid = new List<int>(source.Length);
        for (var triangleIndex = 0; triangleIndex + 2 < source.Length; triangleIndex += 3)
        {
            var a = source[triangleIndex];
            var b = source[triangleIndex + 1];
            var c = source[triangleIndex + 2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= data.vertices.Length || b >= data.vertices.Length || c >= data.vertices.Length)
            {
                continue;
            }

            var ab = data.vertices[b] - data.vertices[a];
            var ac = data.vertices[c] - data.vertices[a];
            var doubleArea = Mathf.Abs(ab.x * ac.y - ab.y * ac.x);
            if (float.IsNaN(doubleArea) || float.IsInfinity(doubleArea) || doubleArea <= minimumDoubleArea)
                continue;

            valid.Add(a);
            valid.Add(b);
            valid.Add(c);
        }

        var removedCount = (source.Length - valid.Count) / 3;
        if (removedCount > 0)
            data.triangles = valid.ToArray();
        return removedCount;
    }

    private int RefineRetriangulationLongEdges(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            data.triangles == null || data.triangles.Length < 3)
        {
            return 0;
        }

        var initialEdges = BuildTriangleEdgeInfos(data.triangles, data.vertices.Length);
        var internalLengths = new List<float>(initialEdges.Count);
        foreach (var edge in initialEdges.Values)
        {
            if (edge.useCount != 2 || edge.a < 0 || edge.b < 0 ||
                edge.a >= data.vertices.Length || edge.b >= data.vertices.Length)
            {
                continue;
            }
            internalLengths.Add(Vector2.Distance(data.vertices[edge.a], data.vertices[edge.b]));
        }
        if (internalLengths.Count == 0)
            return 0;

        internalLengths.Sort();
        var medianLength = internalLengths[internalLengths.Count / 2];
        var targetLength = Mathf.Clamp(medianLength * 2f, 24f, 60f);
        var targetLengthSqr = targetLength * targetLength;
        var originalVertexCount = data.vertices.Length;
        var maxAddedVertices = Mathf.Min(2048, Mathf.Max(64, originalVertexCount * 2));
        var addedVertices = 0;

        while (addedVertices < maxAddedVertices)
        {
            var edges = BuildTriangleEdgeInfos(data.triangles, data.vertices.Length);
            var longestEdge = default(TriangleEdgeInfo);
            var longestLengthSqr = targetLengthSqr;
            var hasLongEdge = false;
            foreach (var edge in edges.Values)
            {
                if (edge.useCount != 2 || edge.a < 0 || edge.b < 0 ||
                    edge.a >= data.vertices.Length || edge.b >= data.vertices.Length)
                {
                    continue;
                }

                var lengthSqr = (data.vertices[edge.a] - data.vertices[edge.b]).sqrMagnitude;
                if (lengthSqr <= longestLengthSqr)
                    continue;
                longestLengthSqr = lengthSqr;
                longestEdge = edge;
                hasLongEdge = true;
            }
            if (!hasLongEdge)
                break;

            var oldCount = data.vertices.Length;
            var midpoint = (data.vertices[longestEdge.a] + data.vertices[longestEdge.b]) * 0.5f;
            Array.Resize(ref data.vertices, oldCount + 1);
            data.vertices[oldCount] = midpoint;

            EnsureBoneWeightsArraySize(data, oldCount);
            if (data.boneWeights != null && data.boneWeights.Length == oldCount)
            {
                var midpointWeight = InterpolateBoneWeight(
                    data.boneWeights[longestEdge.a],
                    data.boneWeights[longestEdge.b],
                    0.5f,
                    data.spriteBoneToRendererBone != null ? data.spriteBoneToRendererBone.Length : 0);
                Array.Resize(ref data.boneWeights, oldCount + 1);
                data.boneWeights[oldCount] = midpointWeight;
            }

            if (data.vertexUvs != null && data.vertexUvs.Length == oldCount)
            {
                var midpointUv = (data.vertexUvs[longestEdge.a] + data.vertexUvs[longestEdge.b]) * 0.5f;
                Array.Resize(ref data.vertexUvs, oldCount + 1);
                data.vertexUvs[oldCount] = midpointUv;
            }

            if (!SplitTrianglesOnEdge(
                    ref data.triangles,
                    data.vertices,
                    midpoint,
                    oldCount,
                    longestEdge.a,
                    longestEdge.b))
            {
                Array.Resize(ref data.vertices, oldCount);
                if (data.boneWeights != null && data.boneWeights.Length == oldCount + 1)
                    Array.Resize(ref data.boneWeights, oldCount);
                if (data.vertexUvs != null && data.vertexUvs.Length == oldCount + 1)
                    Array.Resize(ref data.vertexUvs, oldCount);
                break;
            }

            addedVertices++;
        }

        if (addedVertices <= 0)
            return 0;

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        return addedVertices;
    }

    private bool TryRetriangulateUsingSanitizedBoundary(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        if (TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out var boundaryLoop) &&
            boundaryLoop.Count >= 3)
        {
            data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(boundaryLoop);
            if (TryRetriangulateKeepingVertexSet(data) ||
                TryRetriangulateProjectorAplPreservingBoundaryVertices(data))
            {
                return true;
            }
        }

        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var triangleBoundaryPairs);
        if (!TryBuildBoundaryLoopFromPairs(triangleBoundaryPairs, data.vertices.Length, out boundaryLoop) ||
            boundaryLoop.Count < 3)
        {
            return false;
        }

        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(boundaryLoop);
        return TryRetriangulateKeepingVertexSet(data) ||
               TryRetriangulateProjectorAplPreservingBoundaryVertices(data);
    }

    private bool TryRetriangulateUsingConvexRecovery(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        var sortedUniqueIndices = new List<int>(data.vertices.Length);
        for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
        {
            if (IsFiniteRetriangulationPoint(data.vertices[vertexIndex]))
                sortedUniqueIndices.Add(vertexIndex);
        }
        sortedUniqueIndices.Sort((lhs, rhs) =>
        {
            var x = data.vertices[lhs].x.CompareTo(data.vertices[rhs].x);
            return x != 0 ? x : data.vertices[lhs].y.CompareTo(data.vertices[rhs].y);
        });

        var uniqueIndices = new List<int>(sortedUniqueIndices.Count);
        for (var sortedIndex = 0; sortedIndex < sortedUniqueIndices.Count; sortedIndex++)
        {
            var vertexIndex = sortedUniqueIndices[sortedIndex];
            if (uniqueIndices.Count > 0 &&
                (data.vertices[uniqueIndices[uniqueIndices.Count - 1]] - data.vertices[vertexIndex]).sqrMagnitude <= 1e-8f)
            {
                continue;
            }
            uniqueIndices.Add(vertexIndex);
        }
        if (uniqueIndices.Count < 3)
            return false;

        var uniqueVertices = new List<Vector2>(uniqueIndices.Count);
        for (var uniqueIndex = 0; uniqueIndex < uniqueIndices.Count; uniqueIndex++)
            uniqueVertices.Add(data.vertices[uniqueIndices[uniqueIndex]]);

        var localHull = ReanimatorProjectorAlgorithms.BuildStretchBoundaryLoop(uniqueVertices);
        if (localHull == null || localHull.Count < 3)
            return false;

        var hull = new List<int>(localHull.Count);
        var hullSet = new HashSet<int>();
        for (var hullIndex = 0; hullIndex < localHull.Count; hullIndex++)
        {
            var localIndex = localHull[hullIndex];
            if (localIndex < 0 || localIndex >= uniqueIndices.Count)
                return false;
            var vertexIndex = uniqueIndices[localIndex];
            hull.Add(vertexIndex);
            hullSet.Add(vertexIndex);
        }

        var triangles = new List<int>((uniqueIndices.Count - 2) * 3);
        for (var hullIndex = 1; hullIndex + 1 < hull.Count; hullIndex++)
        {
            triangles.Add(hull[0]);
            triangles.Add(hull[hullIndex]);
            triangles.Add(hull[hullIndex + 1]);
        }

        data.triangles = SanitizeTriangles(triangles.ToArray(), data.vertices.Length);
        if (data.triangles.Length < 3)
            return false;

        for (var uniqueIndex = 0; uniqueIndex < uniqueIndices.Count; uniqueIndex++)
        {
            var vertexIndex = uniqueIndices[uniqueIndex];
            if (hullSet.Contains(vertexIndex))
                continue;
            TryInsertVertexIntoTriangles(data, data.vertices, data.vertices[vertexIndex], vertexIndex);
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles.Length < 3)
            return false;

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        return data.boundaryEdgePairs != null && data.boundaryEdgePairs.Length >= 6;
    }

    private static bool IsFiniteRetriangulationPoint(Vector2 point)
    {
        return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
               !float.IsNaN(point.y) && !float.IsInfinity(point.y);
    }

    private void RebuildRetriangulationFallbackQuad(LayerVertexData data)
    {
        var bounds = data != null && data.layer != null
            ? PsdCoordinateSpace.GetLayerDocumentRect(data.layer)
            : new Rect(0f, 0f, 1f, 1f);
        if (!IsFiniteRetriangulationPoint(bounds.min) || !IsFiniteRetriangulationPoint(bounds.max))
            bounds = new Rect(0f, 0f, 1f, 1f);
        if (bounds.width <= 1e-4f)
            bounds.xMax = bounds.xMin + 1f;
        if (bounds.height <= 1e-4f)
            bounds.yMax = bounds.yMin + 1f;

        var fallbackWeight = default(BoneWeight);
        if (data.boneWeights != null)
        {
            for (var weightIndex = 0; weightIndex < data.boneWeights.Length; weightIndex++)
            {
                if (!ReanimatorProjectorAlgorithms.HasUsableWeight(data.boneWeights[weightIndex]))
                    continue;
                fallbackWeight = data.boneWeights[weightIndex];
                break;
            }
        }

        data.vertices = new[]
        {
            new Vector2(bounds.xMin, bounds.yMin),
            new Vector2(bounds.xMax, bounds.yMin),
            new Vector2(bounds.xMax, bounds.yMax),
            new Vector2(bounds.xMin, bounds.yMax)
        };
        data.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        data.boneWeights = new[] { fallbackWeight, fallbackWeight, fallbackWeight, fallbackWeight };
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
    }

    private bool TryCreateEditGeometryEdgeFallback(
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        int vertexA,
        int vertexB,
        out string error)
    {
        error = null;
        if (data == null || data.vertices == null || data.triangles == null)
        {
            error = "Geometry is empty";
            return false;
        }

        if (!TryBuildTrianglesWithInsertedEdge(data.vertices, data.triangles, vertexA, vertexB, out var triangles))
        {
            error = "Edge could not be created";
            return false;
        }

        triangles = SanitizeTriangles(triangles, data.vertices.Length);
        if (triangles == null || triangles.Length < 3 || !HasTriangleEdge(triangles, vertexA, vertexB))
        {
            error = "Edge could not be created";
            return false;
        }

        ApplyCreatedEditGeometryEdge(data, selectionKey, stableKey, vertexA, vertexB, triangles);
        return true;
    }

    private void ApplyCreatedEditGeometryEdge(
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        int vertexA,
        int vertexB,
        int[] triangles)
    {
        data.triangles = triangles;
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);

        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        editGeometrySelectedVertexIndex = vertexB;
        editGeometrySelectedLayerStableKey = stableKey;
        if (editGeometrySelectedVertexIndices != null)
        {
            editGeometrySelectedVertexIndices.Clear();
            editGeometrySelectedVertexIndices.Add(vertexB);
        }
        createEdgeStartVertexIndex = vertexA;
        createEdgeLayerStableKey = stableKey;
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
    }

    public bool CancelCreateEdgeTool()
    {
        var changed = createEdgeStartVertexIndex >= 0 || !string.IsNullOrEmpty(createEdgeLayerStableKey);
        if (changed)
            RegisterUndo("Cancel Create Edge Tool");
        createEdgeStartVertexIndex = -1;
        createEdgeLayerStableKey = string.Empty;
        return changed;
    }

    public bool HasCreateEdgeStartVertex()
    {
        return createEdgeStartVertexIndex >= 0 && !string.IsNullOrEmpty(createEdgeLayerStableKey);
    }

    private static bool TryAddUniqueConstraintEdge(List<Vector2Int> edges, HashSet<ulong> uniqueKeys, int a, int b, int vertexCount)
    {
        if (edges == null || uniqueKeys == null)
            return false;
        if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return false;

        var key = MakeEdgeKey(a, b);
        if (!uniqueKeys.Add(key))
            return false;

        edges.Add(new Vector2Int(a, b));
        return true;
    }

    private static bool HasEdge(int[] edgePairs, int a, int b)
    {
        if (edgePairs == null || edgePairs.Length < 2)
            return false;
        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
        {
            var e0 = edgePairs[i];
            var e1 = edgePairs[i + 1];
            if ((e0 == a && e1 == b) || (e0 == b && e1 == a))
                return true;
        }

        return false;
    }

    private static void ReplaceSplitEdgePair(ref int[] edgePairs, int a, int b, int newIndex)
    {
        if (edgePairs == null || edgePairs.Length < 2)
            return;

        var rebuilt = new List<int>(edgePairs.Length + 2);
        var replaced = false;
        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
        {
            var e0 = edgePairs[i];
            var e1 = edgePairs[i + 1];
            if ((e0 == a && e1 == b) || (e0 == b && e1 == a))
            {
                rebuilt.Add(e0);
                rebuilt.Add(newIndex);
                rebuilt.Add(newIndex);
                rebuilt.Add(e1);
                replaced = true;
                continue;
            }

            rebuilt.Add(e0);
            rebuilt.Add(e1);
        }

        if (replaced)
            edgePairs = rebuilt.ToArray();
    }

    private BoneWeight InterpolateBoneWeight(BoneWeight a, BoneWeight b, float t, int spriteBoneCount)
    {
        if (spriteBoneCount <= 0)
            return t < 0.5f ? a : b;

        var weights = new Dictionary<int, float>(8);
        AccumulateInterpolatedWeight(weights, a.boneIndex0, a.weight0, spriteBoneCount, 1f - t);
        AccumulateInterpolatedWeight(weights, a.boneIndex1, a.weight1, spriteBoneCount, 1f - t);
        AccumulateInterpolatedWeight(weights, a.boneIndex2, a.weight2, spriteBoneCount, 1f - t);
        AccumulateInterpolatedWeight(weights, a.boneIndex3, a.weight3, spriteBoneCount, 1f - t);
        AccumulateInterpolatedWeight(weights, b.boneIndex0, b.weight0, spriteBoneCount, t);
        AccumulateInterpolatedWeight(weights, b.boneIndex1, b.weight1, spriteBoneCount, t);
        AccumulateInterpolatedWeight(weights, b.boneIndex2, b.weight2, spriteBoneCount, t);
        AccumulateInterpolatedWeight(weights, b.boneIndex3, b.weight3, spriteBoneCount, t);
        NormalizeWeightBrushExistingWeights(weights);
        return BuildWeightBrushBoneWeightFromDictionary(weights, spriteBoneCount, true);
    }

    private static void AccumulateInterpolatedWeight(Dictionary<int, float> weights, int boneIndex, float weight, int spriteBoneCount, float factor)
    {
        if (weights == null || boneIndex < 0 || boneIndex >= spriteBoneCount || weight <= 0f || factor <= 0f)
            return;

        weights.TryGetValue(boneIndex, out var current);
        weights[boneIndex] = current + weight * factor;
    }

    private static bool HasTriangleEdge(int[] triangles, int a, int b)
    {
        if (triangles == null || triangles.Length < 3)
            return false;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var t0 = triangles[i];
            var t1 = triangles[i + 1];
            var t2 = triangles[i + 2];
            if ((t0 == a && t1 == b) || (t0 == b && t1 == a))
                return true;
            if ((t1 == a && t2 == b) || (t1 == b && t2 == a))
                return true;
            if ((t2 == a && t0 == b) || (t2 == b && t0 == a))
                return true;
        }

        return false;
    }

    private static bool TryBuildTrianglesWithInsertedEdge(
        Vector2[] vertices,
        int[] triangles,
        int vertexA,
        int vertexB,
        out int[] result)
    {
        result = null;
        if (vertices == null || triangles == null || vertices.Length < 3 || triangles.Length < 3)
            return false;
        if (vertexA < 0 || vertexB < 0 || vertexA >= vertices.Length || vertexB >= vertices.Length || vertexA == vertexB)
            return false;

        var affected = new bool[triangles.Length / 3];
        var affectedCount = 0;
        var a = vertices[vertexA];
        var b = vertices[vertexB];
        var delta = b - a;
        if (delta.sqrMagnitude <= 0.000001f)
            return false;

        var nearA = a + delta * 0.0005f;
        var nearB = b - delta * 0.0005f;
        for (var tri = 0; tri < affected.Length; tri++)
        {
            var offset = tri * 3;
            var i0 = triangles[offset];
            var i1 = triangles[offset + 1];
            var i2 = triangles[offset + 2];
            if (!IsValidTriangleIndex(i0, vertices.Length) ||
                !IsValidTriangleIndex(i1, vertices.Length) ||
                !IsValidTriangleIndex(i2, vertices.Length))
                continue;

            var p0 = vertices[i0];
            var p1 = vertices[i1];
            var p2 = vertices[i2];
            if (!DoesSegmentTouchTriangleInterior(a, b, nearA, nearB, p0, p1, p2))
                continue;

            affected[tri] = true;
            affectedCount++;
        }

        if (affectedCount <= 0)
            return false;

        var boundaryEdges = BuildAffectedBoundaryEdges(triangles, affected, vertices.Length);
        if (boundaryEdges.Count < 3)
            return false;

        if (!TryBuildBoundaryPathsForInsertedEdge(boundaryEdges, vertexA, vertexB, out var pathA, out var pathB))
            return false;

        var rebuilt = new List<int>(triangles.Length + 12);
        for (var tri = 0; tri < affected.Length; tri++)
        {
            if (affected[tri])
                continue;
            var offset = tri * 3;
            rebuilt.Add(triangles[offset]);
            rebuilt.Add(triangles[offset + 1]);
            rebuilt.Add(triangles[offset + 2]);
        }

        if (!AppendTriangulatedPath(vertices, pathA, rebuilt))
            return false;
        if (!AppendTriangulatedPath(vertices, pathB, rebuilt))
            return false;

        result = rebuilt.ToArray();
        return true;
    }

    private static bool IsValidTriangleIndex(int index, int vertexCount)
    {
        return index >= 0 && index < vertexCount;
    }

    private static bool DoesSegmentTouchTriangleInterior(
        Vector2 a,
        Vector2 b,
        Vector2 nearA,
        Vector2 nearB,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2)
    {
        if (IsPointInTriangleInclusive(nearA, p0, p1, p2) ||
            IsPointInTriangleInclusive(nearB, p0, p1, p2))
            return true;

        return SegmentIntersectsTriangleEdgeInterior(a, b, p0, p1) ||
               SegmentIntersectsTriangleEdgeInterior(a, b, p1, p2) ||
               SegmentIntersectsTriangleEdgeInterior(a, b, p2, p0);
    }

    private static bool IsPointInTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        if (!TryGetBarycentric(p, a, b, c, out var wa, out var wb, out var wc))
            return false;
        const float tolerance = -0.0001f;
        return wa >= tolerance && wb >= tolerance && wc >= tolerance;
    }

    private static bool SegmentIntersectsTriangleEdgeInterior(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (!TryGetSegmentIntersection(a, b, c, d, out var segmentT, out var edgeT))
            return false;
        const float epsilon = 0.0001f;
        return segmentT > epsilon && segmentT < 1f - epsilon && edgeT > epsilon && edgeT < 1f - epsilon;
    }

    private static bool TryGetSegmentIntersection(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out float segmentT,
        out float edgeT)
    {
        segmentT = 0f;
        edgeT = 0f;
        var r = b - a;
        var s = d - c;
        var denominator = Cross2D(r, s);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;

        var diff = c - a;
        segmentT = Cross2D(diff, s) / denominator;
        edgeT = Cross2D(diff, r) / denominator;
        return segmentT >= -0.0001f && segmentT <= 1.0001f && edgeT >= -0.0001f && edgeT <= 1.0001f;
    }

    private static List<Vector2Int> BuildAffectedBoundaryEdges(int[] triangles, bool[] affected, int vertexCount)
    {
        var counts = new Dictionary<ulong, EdgeCount>();
        for (var tri = 0; tri < affected.Length; tri++)
        {
            if (!affected[tri])
                continue;

            var offset = tri * 3;
            AddAffectedEdge(counts, triangles[offset], triangles[offset + 1], vertexCount);
            AddAffectedEdge(counts, triangles[offset + 1], triangles[offset + 2], vertexCount);
            AddAffectedEdge(counts, triangles[offset + 2], triangles[offset], vertexCount);
        }

        var result = new List<Vector2Int>();
        foreach (var kv in counts)
        {
            if (kv.Value.count == 1)
                result.Add(new Vector2Int(kv.Value.a, kv.Value.b));
        }

        return result;
    }

    private static void AddAffectedEdge(Dictionary<ulong, EdgeCount> counts, int a, int b, int vertexCount)
    {
        if (counts == null || a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;

        var key = MakeEdgeKey(a, b);
        if (counts.TryGetValue(key, out var edge))
        {
            edge.count++;
            counts[key] = edge;
            return;
        }

        counts[key] = new EdgeCount { a = a, b = b, count = 1 };
    }

    private static bool TryBuildBoundaryPathsForInsertedEdge(
        IReadOnlyList<Vector2Int> boundaryEdges,
        int vertexA,
        int vertexB,
        out List<int> pathA,
        out List<int> pathB)
    {
        pathA = null;
        pathB = null;
        var adjacency = new Dictionary<int, List<int>>();
        for (var i = 0; i < boundaryEdges.Count; i++)
        {
            AddAdjacency(adjacency, boundaryEdges[i].x, boundaryEdges[i].y);
            AddAdjacency(adjacency, boundaryEdges[i].y, boundaryEdges[i].x);
        }

        if (!adjacency.TryGetValue(vertexA, out var startNeighbors) || startNeighbors.Count < 2)
            return false;

        for (var i = 0; i < startNeighbors.Count; i++)
        {
            if (!TryWalkBoundaryPath(adjacency, vertexA, startNeighbors[i], vertexB, out var firstPath))
                continue;

            for (var j = i + 1; j < startNeighbors.Count; j++)
            {
                if (!TryWalkBoundaryPath(adjacency, vertexA, startNeighbors[j], vertexB, out var secondPath))
                    continue;
                if (!AreDistinctBoundaryPaths(firstPath, secondPath))
                    continue;

                pathA = firstPath;
                pathB = secondPath;
                return true;
            }
        }

        return false;
    }

    private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int a, int b)
    {
        if (!adjacency.TryGetValue(a, out var list))
        {
            list = new List<int>(2);
            adjacency[a] = list;
        }

        if (!list.Contains(b))
            list.Add(b);
    }

    private static bool TryWalkBoundaryPath(
        Dictionary<int, List<int>> adjacency,
        int start,
        int first,
        int end,
        out List<int> path)
    {
        path = new List<int> { start };
        var previous = start;
        var current = first;
        var visited = new HashSet<int> { start };
        var guard = 0;
        while (guard++ < adjacency.Count + 2)
        {
            path.Add(current);
            if (current == end)
                return path.Count >= 3;
            if (!visited.Add(current))
                return false;
            if (!adjacency.TryGetValue(current, out var neighbors) || neighbors.Count == 0)
                return false;

            var next = -1;
            for (var i = 0; i < neighbors.Count; i++)
            {
                var candidate = neighbors[i];
                if (candidate == previous)
                    continue;
                next = candidate;
                break;
            }

            if (next < 0)
                return false;
            previous = current;
            current = next;
        }

        return false;
    }

    private static bool AreDistinctBoundaryPaths(IReadOnlyList<int> lhs, IReadOnlyList<int> rhs)
    {
        if (lhs == null || rhs == null || lhs.Count != rhs.Count)
            return true;
        for (var i = 0; i < lhs.Count; i++)
        {
            if (lhs[i] != rhs[i])
                return true;
        }
        return false;
    }

    private static bool AppendTriangulatedPath(Vector2[] vertices, IReadOnlyList<int> path, List<int> target)
    {
        if (vertices == null || path == null || target == null || path.Count < 3)
            return false;

        var polygon = new List<Vector2>(path.Count);
        for (var i = 0; i < path.Count; i++)
        {
            var vertexIndex = path[i];
            if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                return false;
            polygon.Add(vertices[vertexIndex]);
        }

        var localTriangles = new List<int>();
        if (!TryTriangulateSimplePolygon(polygon, localTriangles) || localTriangles.Count < 3)
            return false;

        for (var i = 0; i + 2 < localTriangles.Count; i += 3)
        {
            target.Add(path[localTriangles[i]]);
            target.Add(path[localTriangles[i + 1]]);
            target.Add(path[localTriangles[i + 2]]);
        }

        return true;
    }

    private struct EdgeCount
    {
        public int a;
        public int b;
        public int count;
    }

}
#endif
