#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyFixGeometry()
    {
        var r = new PsdLayerPreviewRenderer();
        LayerVertexData Make(string name, int id)
        {
            var layer = new LayerInfo { name = name, layerId = id, sourceRect = new Rect(0, 0, 10, 10), textureRect = new Rect(0, 0, 10, 10) };
            var data = new LayerVertexData
            {
                layer = layer,
                vertices = new[] { Vector2.zero, new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10), new Vector2(5, 5), new Vector2(20, 5) },
                vertexUvs = Enumerable.Range(0, 6).Select(i => new Vector2(i * .1f, .3f)).ToArray(),
                boneWeights = Enumerable.Range(0, 6).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1 }).ToArray(),
                triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 },
                boundaryEdgePairs = new[] { 0, 1, 1, 2, 2, 3, 3, 0 }
            };
            r.layerInfos.Add(layer);
            r.vertexDataByLayerSelectionKey.Add(BuildSelectionKey(layer), data);
            return data;
        }
        var first = Make("first", 1); var second = Make("second", 2);
        var old = CloneProjectorLayerData(first);
        r.SelectLayer(first.layer);
        int undoCalls = 0;
        r.UndoRequest = _ =>
        {
            if (undoCalls == 0 && first.vertices.Length != 6) throw new Exception("Undo was registered after modifying geometry.");
            undoCalls++;
        };
        if (!r.FixGeometryOutsideContours(false, out int count, out int sprites, out var error) || count != 1 || sprites != 1 || second.vertices.Length != 6 || undoCalls != 1)
            throw new Exception("Selected geometry scope or undo mismatch: " + error);
        for (int i = 0; i < first.vertices.Length; i++)
            if (first.vertices[i] != old.vertices[i] || first.vertexUvs[i] != old.vertexUvs[i] || !first.boneWeights[i].Equals(old.boneWeights[i]))
                throw new Exception("Fix Geo changed retained coordinates, UVs or weights.");
        if (!r.FixGeometryOutsideContours(true, out count, out sprites, out error) || count != 1 || sprites != 1 || undoCalls != 2)
            throw new Exception("All geometry scope mismatch: " + error);
        if (!r.FixGeometryOutsideContours(true, out count, out sprites, out error) || count != 0 || undoCalls != 2)
            throw new Exception("Repeat cleanup must be a no-op.");
        // An inner loop is a hole, an external disconnected loop is a separate
        // island; neither may be replaced by a single guessed convex contour.
        var points = new[] { new Vector2(0,0),new Vector2(10,0),new Vector2(10,10),new Vector2(0,10),
            new Vector2(3,3),new Vector2(7,3),new Vector2(7,7),new Vector2(3,7),
            new Vector2(20,0),new Vector2(30,0),new Vector2(30,10),new Vector2(20,10),
            new Vector2(1,1),new Vector2(5,5),new Vector2(25,5),new Vector2(15,5),new Vector2(0,5) };
        var edges = new[] { 0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,8,9,9,10,10,11,11,8 };
        if (!TryFindGeometryVerticesOutsideContours(points, edges, out var outside, out error) || !outside.SequenceEqual(new[] { 13, 15 }))
            throw new Exception("Hole, island, or boundary-edge classification mismatch: " + error);
        // A malformed operation must leave every original mesh untouched.
        second.vertices = old.vertices.ToArray();
        second.boundaryEdgePairs = new[] { 0, 1, 1, 2, 2, 3 };
        if (r.FixGeometryOutsideContours(true, out count, out sprites, out error) || second.vertices.Length != 6 || undoCalls != 2)
            throw new Exception("Malformed cleanup modified geometry or registered Undo.");
        return "PASS: selected/all scopes, counts, single Undo before commit, retained UV/weight/contour data, holes/islands/on-edge vertices, repeat no-op and atomic failure.";
    }

    public string VerifyFixGeometryOnCurrentCharacter()
    {
        var scratch = new PsdLayerPreviewRenderer();
        int injected = 0;
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var data = CloneProjectorLayerData(pair.Value);
            if (data.layer == null || data.vertices == null || data.vertices.Length < 3) continue;
            scratch.layerInfos.Add(data.layer);
            var point = new Vector2(data.vertices.Max(v => v.x) + 100, data.vertices.Max(v => v.y) + 100);
            data.vertices = data.vertices.Concat(new[] { point }).ToArray();
            data.vertexUvs = data.vertexUvs.Concat(new[] { Vector2.zero }).ToArray();
            if (data.boneWeights != null && data.boneWeights.Length == data.vertices.Length - 1)
                data.boneWeights = data.boneWeights.Concat(new[] { data.boneWeights[0] }).ToArray();
            scratch.vertexDataByLayerSelectionKey.Add(pair.Key, data);
            injected++;
        }
        if (!scratch.FixGeometryOutsideContours(true, out int count, out int sprites, out var error) || count < injected || sprites != injected)
            throw new Exception("Character cleanup failed: " + error + "; removed " + count + ", injected " + injected);
        if (!scratch.FixGeometryOutsideContours(true, out int repeat, out _, out error) || repeat != 0)
            throw new Exception("Character repeat cleanup changed geometry: " + error);
        return "PASS: " + injected + " actual character meshes cleaned on copies; " + count + " outside vertices removed, repeat no-op. Source unchanged.";
    }
}
#endif
