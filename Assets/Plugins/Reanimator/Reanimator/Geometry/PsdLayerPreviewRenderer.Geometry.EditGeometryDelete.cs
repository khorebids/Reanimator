#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool DeleteSelectedEditGeometryVertex()
    {
        if (!TryGetSelectedEditGeometryLayerData(out var data, out var selectionKey, out var stableKey))
            return false;
        if (data.vertices == null || data.vertices.Length <= 3)
            return false;

        var selectedCount = NormalizeEditGeometrySelection(data, stableKey);
        if (selectedCount <= 0)
            return false;
        if (data.vertices.Length - selectedCount < 3)
            return false;

        RegisterUndo("Delete Geometry Vertex");
        if (!TryDeleteSelectedVerticesAndRebuild(data, stableKey))
            return false;

        ClearEditGeometryVertexSelection(false);

        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool TryAddEditGeometryVertex(
        string selectionKey,
        string stableKey,
        Vector2 positionDoc,
        bool pointAlreadyInBaseSpace,
        out int vertexIndex)
    {
        vertexIndex = -1;
        if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) || data == null)
            return false;
        if (data.vertices == null || data.vertices.Length < 3)
            return false;

        var basePointDoc = pointAlreadyInBaseSpace ? positionDoc : ConvertPointToBaseDoc(data, positionDoc);
        for (var i = 0; i < data.vertices.Length; i++)
        {
            if ((data.vertices[i] - basePointDoc).sqrMagnitude <= 0.0001f)
                return false;
        }

        RegisterUndo("Create Geometry Vertex");
        var newVertexIndex = data.vertices.Length;
        if (!InsertAppliedVertexIntoLayerData(data, basePointDoc, true))
            return false;

        vertexIndex = newVertexIndex;
        ApplyEditGeometryVertexSelection(selectionKey, stableKey, vertexIndex);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool TryDeleteEditGeometryVertex(string selectionKey, string stableKey, int vertexIndex)
    {
        if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var data) || data == null)
            return false;
        if (data.vertices == null || data.vertices.Length <= 3)
            return false;
        if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
            return false;

        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();
        editGeometrySelectedVertexIndices.Clear();
        editGeometrySelectedVertexIndices.Add(vertexIndex);
        selectedLayerSelectionKey = selectionKey;
        editGeometrySelectedLayerStableKey = stableKey;
        editGeometrySelectedVertexIndex = vertexIndex;

        RegisterUndo("Delete Geometry Vertex");
        if (!TryDeleteSelectedVerticesAndRebuild(data, stableKey))
            return false;

        ClearEditGeometryVertexSelection(false);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }


    private bool TryRemoveNonContourVerticesOutsideContour(LayerVertexData data, string stableKey, out bool removedAny)
    {
        removedAny = false;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return true;

        var vertexCount = data.vertices.Length;
        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
            {
                data.boundaryEdgePairs = fallbackBoundaryPairs;
                contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            }
        }

        if (CountTrue(contourMask) < 3)
            return true;

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return true;

        var removeMask = new bool[vertexCount];
        var removedCount = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            if (contourMask[i])
                continue;
            if (IsPointInsidePolygonOrOnEdge(data.vertices[i], data.vertices, boundaryLoop))
                continue;

            removeMask[i] = true;
            removedCount++;
        }

        if (removedCount <= 0)
            return true;

        var remainingVertexCount = vertexCount - removedCount;
        if (remainingVertexCount < 3)
            return false;

        var previousVertices = data.vertices;
        var previousVertexUvs = data.vertexUvs;
        var previousBoneWeights = data.boneWeights;
        var previousTriangles = data.triangles;
        var previousAllEdgePairs = data.allEdgePairs;
        var previousBoundaryEdgePairs = data.boundaryEdgePairs;
        var previousDeformedVertices = data.deformedVertices;
        var previousSkinMatrices = data.skinMatrices;
        var previousLastPoseVersion = data.lastPoseVersion;
        var previousSelectedVertexIndex = editGeometrySelectedVertexIndex;
        var previousSelectedVertexIndices = editGeometrySelectedVertexIndices != null
            ? new List<int>(editGeometrySelectedVertexIndices)
            : new List<int>();

        void RestoreMovedGeometry()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousVertexUvs;
            data.boneWeights = previousBoneWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdgePairs;
            data.boundaryEdgePairs = previousBoundaryEdgePairs;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.lastPoseVersion = previousLastPoseVersion;
            editGeometrySelectedVertexIndex = previousSelectedVertexIndex;
            if (editGeometrySelectedVertexIndices == null)
                editGeometrySelectedVertexIndices = new List<int>();
            editGeometrySelectedVertexIndices.Clear();
            editGeometrySelectedVertexIndices.AddRange(previousSelectedVertexIndices);
        }

        var remap = new int[vertexCount];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        var keptVertices = new List<Vector2>(remainingVertexCount);
        var keptUvs = new List<Vector2>(remainingVertexCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(remainingVertexCount)
            : null;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;

            remap[i] = keptVertices.Count;
            keptVertices.Add(data.vertices[i]);
            if (data.vertexUvs != null && i < data.vertexUvs.Length)
                keptUvs.Add(data.vertexUvs[i]);
            else
                keptUvs.Add(ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count < 3)
        {
            RestoreMovedGeometry();
            return false;
        }

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        {
            RestoreMovedGeometry();
            return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            RestoreMovedGeometry();
            return false;
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                          data.bindBoneInverseMatrices != null &&
                          data.boneWeights != null &&
                          data.boneWeights.Length == data.vertices.Length &&
                          data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                          data.bindBoneInverseMatrices.Length > 0;

        RemapEditGeometrySelectionAfterVertexCull(remap, stableKey);
        removedAny = true;
        return true;
    }

    private void RemapEditGeometrySelectionAfterVertexCull(IReadOnlyList<int> remap, string stableKey)
    {
        if (editGeometrySelectedVertexIndices == null)
            editGeometrySelectedVertexIndices = new List<int>();

        ApplyEditGeometrySelectionSet(
            stableKey,
            ReanimatorEditGeometrySelectionAlgorithms.RemapSelection(editGeometrySelectedVertexIndices, remap));
    }

    private static bool IsPointInsidePolygonOrOnEdge(
        Vector2 point,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> polygon)
    {
        if (vertices == null || polygon == null || polygon.Count < 3)
            return true;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var ia = polygon[i];
            var ib = polygon[j];
            if (ia < 0 || ib < 0 || ia >= vertices.Count || ib >= vertices.Count)
                continue;

            var a = vertices[ia];
            var b = vertices[ib];
            if (IsEditGeometryPointOnSegment(point, a, b, 0.001f))
                return true;

            var intersects = (a.y > point.y) != (b.y > point.y);
            if (!intersects)
                continue;

            var x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (point.x < x)
                inside = !inside;
        }

        return inside;
    }

    private static bool IsEditGeometryPointOnSegment(Vector2 point, Vector2 a, Vector2 b, float epsilon)
    {
        var ab = b - a;
        var ap = point - a;
        var abLenSqr = ab.sqrMagnitude;
        if (abLenSqr <= epsilon * epsilon)
            return (point - a).sqrMagnitude <= epsilon * epsilon;

        var t = Mathf.Clamp01(Vector2.Dot(ap, ab) / abLenSqr);
        var closest = a + ab * t;
        return (point - closest).sqrMagnitude <= epsilon * epsilon;
    }

    private bool TryDeleteVertexAndRebuild(LayerVertexData data, int removedIndex)
    {
        if (data == null || data.vertices == null || data.vertices.Length <= 3)
            return false;
        if (removedIndex < 0 || removedIndex >= data.vertices.Length)
            return false;

        var previousVertices = data.vertices;
        var previousVertexUvs = data.vertexUvs;
        var previousBoneWeights = data.boneWeights;
        var previousTriangles = data.triangles;
        var previousAllEdgePairs = data.allEdgePairs;
        var previousBoundaryEdgePairs = data.boundaryEdgePairs;
        var previousDeformedVertices = data.deformedVertices;
        var previousSkinMatrices = data.skinMatrices;
        var previousLastPoseVersion = data.lastPoseVersion;

        void RestorePreviousGeometry()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousVertexUvs;
            data.boneWeights = previousBoneWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdgePairs;
            data.boundaryEdgePairs = previousBoundaryEdgePairs;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.lastPoseVersion = previousLastPoseVersion;
        }

        var oldTriangles = data.triangles ?? Array.Empty<int>();
        data.vertices = RemoveElementAt(data.vertices, removedIndex);
        data.vertexUvs = RemoveElementAt(data.vertexUvs, removedIndex);
        data.boneWeights = RemoveElementAt(data.boneWeights, removedIndex);
        data.deformedVertices = null;
        data.skinMatrices = null;
        data.lastPoseVersion = -1;

        var neighborSet = new HashSet<int>();
        var rebuiltTriangles = new List<int>(oldTriangles.Length);
        for (var i = 0; i + 2 < oldTriangles.Length; i += 3)
        {
            var a = oldTriangles[i];
            var b = oldTriangles[i + 1];
            var c = oldTriangles[i + 2];
            var containsRemoved = a == removedIndex || b == removedIndex || c == removedIndex;
            if (containsRemoved)
            {
                if (a != removedIndex) neighborSet.Add(a > removedIndex ? a - 1 : a);
                if (b != removedIndex) neighborSet.Add(b > removedIndex ? b - 1 : b);
                if (c != removedIndex) neighborSet.Add(c > removedIndex ? c - 1 : c);
                continue;
            }

            a = a > removedIndex ? a - 1 : a;
            b = b > removedIndex ? b - 1 : b;
            c = c > removedIndex ? c - 1 : c;
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a)
                continue;
            rebuiltTriangles.Add(a);
            rebuiltTriangles.Add(b);
            rebuiltTriangles.Add(c);
        }

        if (neighborSet.Count >= 3)
            AppendDeletionFillTriangles(data.vertices, neighborSet, rebuiltTriangles);

        data.triangles = SanitizeTriangles(rebuiltTriangles.ToArray(), data.vertices.Length);
        TryRetriangulateKeepingVertexSet(data);
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles.Length < 3)
        {
            RestorePreviousGeometry();
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        return true;
    }

    private bool TryDeleteSelectedVerticesAndRebuild(LayerVertexData data, string stableKey)
    {
        if (data == null || data.vertices == null || data.vertices.Length <= 3)
            return false;
        if (NormalizeEditGeometrySelection(data, stableKey) <= 0)
            return false;

        var vertexCount = data.vertices.Length;
        var removeMask = ReanimatorEditGeometryAlgorithms.BuildRemoveMask(
            editGeometrySelectedVertexIndices,
            vertexCount,
            out var removedCount);

        if (removedCount <= 0 || vertexCount - removedCount < 3)
            return false;

        var previousVertices = data.vertices;
        var previousVertexUvs = data.vertexUvs;
        var previousBoneWeights = data.boneWeights;
        var previousTriangles = data.triangles;
        var previousAllEdgePairs = data.allEdgePairs;
        var previousBoundaryEdgePairs = data.boundaryEdgePairs;
        var previousDeformedVertices = data.deformedVertices;
        var previousSkinMatrices = data.skinMatrices;
        var previousLastPoseVersion = data.lastPoseVersion;

        void RestorePreviousGeometry()
        {
            data.vertices = previousVertices;
            data.vertexUvs = previousVertexUvs;
            data.boneWeights = previousBoneWeights;
            data.triangles = previousTriangles;
            data.allEdgePairs = previousAllEdgePairs;
            data.boundaryEdgePairs = previousBoundaryEdgePairs;
            data.deformedVertices = previousDeformedVertices;
            data.skinMatrices = previousSkinMatrices;
            data.lastPoseVersion = previousLastPoseVersion;
        }

        var boundaryLoop = default(List<int>);
        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out boundaryLoop))
        {
            var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
            {
                BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
                contourMask = BuildContourVertexMask(fallbackBoundaryPairs, vertexCount);
            }

            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);
        }

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out _);

        var keptVertices = new List<Vector2>(vertexCount - removedCount);
        var keptUvs = new List<Vector2>(vertexCount - removedCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(vertexCount - removedCount)
            : null;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;

            keptVertices.Add(data.vertices[i]);
            if (data.vertexUvs != null && i < data.vertexUvs.Length)
                keptUvs.Add(data.vertexUvs[i]);
            else
                keptUvs.Add(ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count >= 3)
            data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        else
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out data.boundaryEdgePairs);

        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        {
            RestorePreviousGeometry();
            return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            RestorePreviousGeometry();
            return false;
        }

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        return true;
    }

    private static T[] RemoveElementAt<T>(T[] source, int index)
    {
        return ReanimatorEditGeometryAlgorithms.RemoveElementAt(source, index);
    }

    private Vector2[] BuildVertexUvsFromLayer(LayerInfo layer, IReadOnlyList<Vector2> vertices)
    {
        if (vertices == null || vertices.Count == 0)
            return Array.Empty<Vector2>();

        var result = new Vector2[vertices.Count];
        if (layer == null)
            return result;

        for (var i = 0; i < vertices.Count; i++)
            result[i] = ComputeUvForPoint(layer, vertices[i]);

        return result;
    }

    private static void AppendDeletionFillTriangles(
        IReadOnlyList<Vector2> vertices,
        HashSet<int> neighborSet,
        List<int> outputTriangles)
    {
        if (vertices == null || neighborSet == null || outputTriangles == null || neighborSet.Count < 3)
            return;

        var neighbors = new List<int>(neighborSet);
        var center = Vector2.zero;
        for (var i = 0; i < neighbors.Count; i++)
            center += vertices[neighbors[i]];
        center /= neighbors.Count;

        neighbors.Sort((lhs, rhs) =>
        {
            var la = Mathf.Atan2(vertices[lhs].y - center.y, vertices[lhs].x - center.x);
            var ra = Mathf.Atan2(vertices[rhs].y - center.y, vertices[rhs].x - center.x);
            return la.CompareTo(ra);
        });

        var windingSign = ComputeSignedAreaFromLoop(vertices, neighbors) >= 0f ? 1f : -1f;
        for (var i = 1; i < neighbors.Count - 1; i++)
            AddOrientedTriangle(outputTriangles, neighbors[0], neighbors[i], neighbors[i + 1], windingSign);
    }

    private static float ComputeSignedAreaFromLoop(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> loop)
    {
        if (vertices == null || loop == null || loop.Count < 3)
            return 0f;

        var area = 0f;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = vertices[loop[i]];
            var b = vertices[loop[(i + 1) % loop.Count]];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }

}
#endif
