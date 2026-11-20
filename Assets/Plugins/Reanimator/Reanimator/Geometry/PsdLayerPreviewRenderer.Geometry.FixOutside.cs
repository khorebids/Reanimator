#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool FixGeometryOutsideContours(bool allSprites, out int removed, out int sprites, out string error)
    {
        removed = sprites = 0;
        error = null;
        var targets = new List<LayerVertexData>();
        if (allSprites)
            targets.AddRange(vertexDataByLayerSelectionKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).Where(data => data?.layer != null));
        else if (TryGetSelectedEditGeometryLayerData(out var selected, out _, out _))
            targets.Add(selected);
        else { error = "Select a sprite geometry first."; return false; }
        var changes = new List<(LayerVertexData original, LayerVertexData staged)>();
        foreach (var data in targets.Distinct())
        {
            if (data.vertices == null || data.vertices.Length < 3) continue;
            var staged = CloneProjectorLayerData(data);
            if (!TryCleanGeometryOutsideContours(staged, out int count, out error))
            { removed = 0; error = data.layer.name + ": " + error; return false; }
            if (count == 0) continue;
            changes.Add((data, staged));
            removed += count;
        }
        if (changes.Count == 0) return true;
        RegisterUndo("Fix Geometry Outside Contours");
        foreach (var change in changes) CommitOtherRegion(change.original.layer, change.original, change.staged);
        sprites = changes.Count;
        EndEditGeometryInteraction(clearSelection: true);
        return true;
    }

    private bool TryCleanGeometryOutsideContours(LayerVertexData data, out int removed, out string error)
    {
        removed = 0;
        error = null;
        var boundary = data.boundaryEdgePairs;
        if (boundary == null || boundary.Length < 6)
            BuildEdgePairs(data.triangles, data.vertices.Length, out _, out boundary);
        if (!TryFindGeometryVerticesOutsideContours(data.vertices, boundary, out var remove, out error)) return false;
        removed = remove.Count;
        if (removed == 0) return true;
        var mask = new bool[data.vertices.Length];
        foreach (int index in remove) mask[index] = true;
        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(mask, out _);
        var vertices = new List<Vector2>();
        var uv = new List<Vector2>();
        var weights = new List<BoneWeight>();
        bool hasWeights = data.boneWeights != null && data.boneWeights.Length == data.vertices.Length;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i]) continue;
            vertices.Add(data.vertices[i]);
            uv.Add(data.vertexUvs != null && i < data.vertexUvs.Length ? data.vertexUvs[i] : ComputeUvForPoint(data.layer, data.vertices[i]));
            if (hasWeights) weights.Add(data.boneWeights[i]);
        }
        bool rebuild = data.triangles != null && data.triangles.Any(i => i >= 0 && i < mask.Length && mask[i]);
        data.vertices = vertices.ToArray();
        data.vertexUvs = uv.ToArray();
        if (hasWeights) data.boneWeights = weights.ToArray();
        data.triangles = RemapTrianglesAfterCull(data.triangles, mask, remap, vertices.Count);
        data.boundaryEdgePairs = boundary.Select(i => remap[i]).ToArray();
        if (rebuild && !TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        { error = "Could not rebuild geometry within its contours."; return false; }
        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out _);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        return true;
    }

    internal static bool TryFindGeometryVerticesOutsideContours(Vector2[] vertices, int[] boundary,
        out List<int> outside, out string error)
    {
        outside = new List<int>();
        error = null;
        if (boundary == null || boundary.Length < 6 || boundary.Length % 2 != 0)
        { error = "Geometry has no closed contour."; return false; }
        var degrees = new Dictionary<int, int>();
        var edges = new HashSet<ulong>();
        for (int i = 0; i < boundary.Length; i += 2)
        {
            int a = boundary[i], b = boundary[i + 1];
            if (a < 0 || b < 0 || a >= vertices.Length || b >= vertices.Length || a == b)
            { error = "Invalid contour edge."; return false; }
            if (!edges.Add(MakeEdgeKey(a, b))) continue;
            degrees[a] = degrees.TryGetValue(a, out int da) ? da + 1 : 1;
            degrees[b] = degrees.TryGetValue(b, out int db) ? db + 1 : 1;
        }
        if (degrees.Values.Any(degree => degree != 2))
        { error = "Geometry contour is open or branched."; return false; }
        float extent = Mathf.Max(vertices.Max(v => v.x) - vertices.Min(v => v.x), vertices.Max(v => v.y) - vertices.Min(v => v.y));
        float toleranceSquared = Mathf.Pow(Mathf.Max(.00001f, extent * .000001f), 2);
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            if (degrees.ContainsKey(vertex)) continue;
            var point = vertices[vertex];
            bool inside = false, onEdge = false;
            var visited = new HashSet<ulong>();
            for (int edge = 0; edge < boundary.Length; edge += 2)
            {
                int ai = boundary[edge], bi = boundary[edge + 1];
                if (!visited.Add(MakeEdgeKey(ai, bi))) continue;
                var a = vertices[ai]; var b = vertices[bi]; var delta = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / Mathf.Max(delta.sqrMagnitude, 1e-20f));
                if ((point - (a + delta * t)).sqrMagnitude <= toleranceSquared) { onEdge = true; break; }
                if ((a.y > point.y) != (b.y > point.y) && point.x < a.x + (point.y - a.y) * delta.x / delta.y) inside = !inside;
            }
            if (!inside && !onEdge) outside.Add(vertex);
        }
        return true;
    }
}
#endif
