#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool TryGetVertexCullTargetInfo(
        out string layerName,
        out int vertexCount,
        out string error)
    {
        layerName = string.Empty;
        vertexCount = 0;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out _, out _) || data == null)
        {
            error = "Select sprite with geometry.";
            return false;
        }

        layerName = GetLayerDisplayName(data.layer);
        vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount < 3)
        {
            error = "Selected sprite has no usable geometry.";
            return false;
        }

        return true;
    }

    public bool TryAnalyzeVertexCull(
        float distance,
        out string layerName,
        out int vertexCount,
        out int mergeGroupCount,
        out int removedCount,
        out string error)
    {
        layerName = string.Empty;
        vertexCount = 0;
        mergeGroupCount = 0;
        removedCount = 0;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out _, out _) || data == null)
        {
            error = "Select sprite with geometry.";
            return false;
        }

        layerName = GetLayerDisplayName(data.layer);
        vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount < 3)
        {
            error = "Selected sprite has no usable geometry.";
            return false;
        }

        if (!TryBuildVertexCullClusters(data.vertices, distance, out var groups, out _))
            return true;

        mergeGroupCount = CountMergedVertexCullGroups(groups);
        removedCount = vertexCount - groups.Count;
        return true;
    }

    public bool ApplyVertexCull(
        float distance,
        out int mergeGroupCount,
        out int removedCount,
        out string layerName,
        out string error)
    {
        mergeGroupCount = 0;
        removedCount = 0;
        layerName = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite with geometry.";
            return false;
        }

        layerName = GetLayerDisplayName(data.layer);
        if (data.vertices == null || data.vertices.Length < 3)
        {
            error = "Selected sprite has no usable geometry.";
            return false;
        }

        if (!TryBuildVertexCullClusters(data.vertices, distance, out var groups, out var remap))
        {
            error = "No close vertex groups.";
            return false;
        }

        mergeGroupCount = CountMergedVertexCullGroups(groups);
        removedCount = data.vertices.Length - groups.Count;
        if (mergeGroupCount <= 0 || removedCount <= 0)
        {
            error = "No close vertex groups.";
            return false;
        }

        if (groups.Count < 3)
        {
            error = "Too many vertices merged.";
            return false;
        }

        RegisterUndo("Vertex Cull");
        return TryApplyVertexCullClusters(data, selectionKey, stableKey, groups, remap, out error);
    }

    internal void DrawVertexCullPreviewOverlay(ReanimatorPreviewContext context, float distance)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || context == null || context.Coordinates == null)
            return;
        if (!TryGetWeightBrushLayerData(out var data, out _, out var stableKey) || data == null || data.vertices == null || data.vertices.Length < 3)
            return;
        if (!string.IsNullOrEmpty(weightBrushLayerStableKey) &&
            !string.Equals(weightBrushLayerStableKey, stableKey, StringComparison.OrdinalIgnoreCase))
            return;
        if (!TryBuildVertexCullClusters(data.vertices, distance, out var groups, out _))
            return;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            drawVertices = data.vertices;
        var toCanvas = CreateLayerVertexToViewMapper(context.Coordinates, data, drawVertices);
        var alpha = context.Visibility != null ? Mathf.Clamp01(context.Visibility.VertexAlpha) : 1f;

        Handles.BeginGUI();
        var prevColor = Handles.color;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            if (group == null || group.Count <= 1)
                continue;

            var center = Vector2.zero;
            for (var i = 0; i < group.Count; i++)
                center += toCanvas(drawVertices[group[i]]);
            center /= group.Count;

            Handles.color = new Color(1f, 1f, 1f, alpha * 0.85f);
            Handles.DrawWireDisc(center, Vector3.forward, 5f);
            Handles.color = new Color(1f, 0.38f, 0.15f, alpha);
            Handles.DrawWireDisc(center, Vector3.forward, 4f);
            for (var i = 0; i < group.Count; i++)
            {
                var p = toCanvas(drawVertices[group[i]]);
                Handles.DrawDottedLine(p, center, 3f);
                Handles.DrawWireDisc(p, Vector3.forward, 3f);
            }
        }
        Handles.color = prevColor;
        Handles.EndGUI();
    }

    private bool TryApplyVertexCullClusters(
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        IReadOnlyList<List<int>> groups,
        IReadOnlyList<int> remap,
        out string error)
    {
        error = null;
        if (data == null || data.vertices == null || groups == null || remap == null)
        {
            error = "Invalid geometry.";
            return false;
        }

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

        var newVertices = new Vector2[groups.Count];
        var newUvs = new Vector2[groups.Count];
        var newWeights = new BoneWeight[groups.Count];
        var hasWeights = data.boneWeights != null && data.boneWeights.Length == data.vertices.Length;
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
        data.triangles = RemapVertexCullTriangles(previousTriangles, remap, newVertices.Length);
        data.boundaryEdgePairs = RemapVertexCullEdgePairs(previousBoundaryEdgePairs, remap, newVertices.Length);
        data.allEdgePairs = RemapVertexCullEdgePairs(previousAllEdgePairs, remap, newVertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles.Length < 3)
        {
            TryRetriangulateKeepingVertexSet(data);
            data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        }
        else if (!TryRetriangulateKeepingVertexSet(data))
        {
            data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        }

        if (data.triangles.Length < 3 && !TryRetriangulateWithDirectFallback(data))
        {
            RestorePreviousGeometry();
            error = "Failed to rebuild mesh.";
            return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles.Length < 3)
        {
            RestorePreviousGeometry();
            error = "Failed to build valid triangles.";
            return false;
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        EnsureLayerBindMatricesForWeights(data);
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        RemapEditGeometrySelectionAfterVertexCull(remap, stableKey);
        return true;
    }

    private static bool TryBuildVertexCullClusters(
        IReadOnlyList<Vector2> vertices,
        float distance,
        out List<List<int>> groups,
        out int[] remap)
    {
        return ReanimatorVertexCullAlgorithms.TryBuildClusters(vertices, distance, out groups, out remap);
    }

    private static int CountMergedVertexCullGroups(IReadOnlyList<List<int>> groups)
    {
        return ReanimatorVertexCullAlgorithms.CountMergedGroups(groups);
    }

    private static Vector2 AverageVertexCullPoints(IReadOnlyList<Vector2> points, IReadOnlyList<int> group)
    {
        return ReanimatorVertexCullAlgorithms.AveragePoints(points, group);
    }

    private Vector2 AverageVertexCullUvs(LayerVertexData data, IReadOnlyList<int> group, Vector2 fallbackPoint)
    {
        if (data == null || data.vertexUvs == null || data.vertexUvs.Length == 0 || group == null || group.Count == 0)
            return ComputeUvForPoint(data != null ? data.layer : null, fallbackPoint);

        var sum = Vector2.zero;
        var count = 0;
        for (var i = 0; i < group.Count; i++)
        {
            var index = group[i];
            if (index < 0 || index >= data.vertexUvs.Length)
                continue;
            sum += data.vertexUvs[index];
            count++;
        }
        return count > 0 ? sum / count : ComputeUvForPoint(data.layer, fallbackPoint);
    }

    private static BoneWeight MergeVertexCullBoneWeights(IReadOnlyList<BoneWeight> source, IReadOnlyList<int> group)
    {
        return ReanimatorVertexCullAlgorithms.MergeBoneWeights(source, group);
    }

    private static int[] RemapVertexCullTriangles(int[] triangles, IReadOnlyList<int> remap, int vertexCount)
    {
        return ReanimatorVertexCullAlgorithms.RemapTriangles(triangles, remap, vertexCount);
    }

    private static int[] RemapVertexCullEdgePairs(int[] edgePairs, IReadOnlyList<int> remap, int vertexCount)
    {
        return ReanimatorVertexCullAlgorithms.RemapEdgePairs(edgePairs, remap, vertexCount);
    }

    private static int RemapVertexCullIndex(int index, IReadOnlyList<int> remap)
    {
        if (remap == null || index < 0 || index >= remap.Count)
            return -1;
        return remap[index];
    }

    private sealed class VertexCullDisjointSet
    {
        private readonly int[] parent;
        private readonly int[] rank;

        public VertexCullDisjointSet(int count)
        {
            parent = new int[count];
            rank = new int[count];
            for (var i = 0; i < count; i++)
                parent[i] = i;
        }

        public int Find(int value)
        {
            if (parent[value] != value)
                parent[value] = Find(parent[value]);
            return parent[value];
        }

        public void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB)
                return;

            if (rank[rootA] < rank[rootB])
            {
                parent[rootA] = rootB;
                return;
            }

            if (rank[rootA] > rank[rootB])
            {
                parent[rootB] = rootA;
                return;
            }

            parent[rootB] = rootA;
            rank[rootA]++;
        }
    }
}
#endif
