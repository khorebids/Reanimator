#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;

// SpriteSkin's outline sorter requires oppositely directed shared edges.
// Normalize topology, rather than signed area: posed triangles can legitimately
// fold over, and changing their geometry or weights would destroy prior tools.
public static class ReanimatorMeshWinding
{
    private readonly struct Edge
    {
        public readonly int triangle, from, to;
        public Edge(int triangle, int from, int to) { this.triangle = triangle; this.from = from; this.to = to; }
    }
    public static int[] Normalize(int[] indices, out int flipped)
    {
        flipped = 0;
        if (indices == null || indices.Length == 0) return indices;
        if (indices.Length % 3 != 0) throw new ArgumentException("Triangle index count must be a multiple of three.");
        int count = indices.Length / 3;
        var edges = new Dictionary<(int, int), List<Edge>>();
        var neighbors = new List<(int triangle, bool opposite)>[count];
        for (int t = 0; t < count; t++)
        {
            neighbors[t] = new List<(int, bool)>();
            for (int j = 0; j < 3; j++)
            {
                int a = indices[t * 3 + j], b = indices[t * 3 + (j + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!edges.TryGetValue(key, out var list)) edges.Add(key, list = new List<Edge>(2));
                list.Add(new Edge(t, a, b));
            }
        }
        foreach (var pair in edges.Values)
        {
            if (pair.Count != 2) continue;
            var a = pair[0]; var b = pair[1]; bool same = a.from == b.from;
            neighbors[a.triangle].Add((b.triangle, same)); neighbors[b.triangle].Add((a.triangle, same));
        }
        var assigned = new bool[count]; var flip = new bool[count];
        var queue = new Queue<int>(); var component = new List<int>();
        for (int seed = 0; seed < count; seed++)
        {
            if (assigned[seed]) continue;
            component.Clear(); assigned[seed] = true; queue.Enqueue(seed); int ones = 0;
            while (queue.Count > 0)
            {
                int t = queue.Dequeue(); component.Add(t); if (flip[t]) ones++;
                foreach (var link in neighbors[t])
                {
                    bool required = flip[t] ^ link.opposite;
                    if (assigned[link.triangle])
                    {
                        if (flip[link.triangle] != required) throw new InvalidOperationException("Mesh has non-orientable triangle adjacency.");
                        continue;
                    }
                    assigned[link.triangle] = true; flip[link.triangle] = required; queue.Enqueue(link.triangle);
                }
            }
            // Keep the majority orientation of every connected component.
            if (ones > component.Count - ones) foreach (int t in component) flip[t] = !flip[t];
        }
        var result = (int[])indices.Clone();
        for (int t = 0; t < count; t++) if (flip[t])
        {
            int temp = result[t * 3 + 1]; result[t * 3 + 1] = result[t * 3 + 2]; result[t * 3 + 2] = temp; flipped++;
        }
        return result;
    }
    public static int Repair(ISpriteEditorDataProvider provider)
    {
        var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>(); if (mesh == null) return 0;
        int total = 0;
        foreach (var sprite in provider.GetSpriteRects())
        {
            var indices = mesh.GetIndices(sprite.spriteID);
            var normalized = Normalize(indices, out int changed);
            if (changed == 0) continue;
            mesh.SetIndices(sprite.spriteID, normalized); total += changed;
        }
        return total;
    }
    public static int RepairAsset(string path)
    {
        var importer = AssetImporter.GetAtPath(path);
        var provider = importer as ISpriteEditorDataProvider;
        if (provider == null) throw new InvalidOperationException("No sprite data provider for " + path);
        provider.InitSpriteEditorDataProvider(); int count = Repair(provider);
        if (count > 0)
        {
            provider.Apply(); EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        return count;
    }
}
#endif
