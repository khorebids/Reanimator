#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ReaniIKSurfaceAuthoring
{
    public static bool IsSkinSprite(Sprite sprite)
    {
        string key = sprite.name.ToLowerInvariant();
        return key == "body" || key == "face" || key == "head" || key == "neck" || key.StartsWith("l_arm") || key.StartsWith("r_arm") || key.StartsWith("l_leg") || key.StartsWith("r_leg");
    }
    public static void InitializeSelection(ReaniIKSurfaceMaps maps)
    {
        if (maps.selectionVersion >= 1) return;
        foreach (var item in maps.sprites) if (item.sprite) item.effectsEnabled = IsSkinSprite(item.sprite);
        maps.selectionVersion = 1;
        EditorUtility.SetDirty(maps); AssetDatabase.SaveAssetIfDirty(maps);
    }
    public static string MapsPath(ReaniIK rig)
    {
        var source = rig.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.sprite && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(r.sprite)));
        if (!source) return null;
        string path = AssetDatabase.GetAssetPath(source.sprite);
        return Path.ChangeExtension(path, ".surfaces.asset").Replace('\\', '/');
    }
    public static void BindExisting(ReaniIK rig)
    {
        if (!rig || rig.surfaceEffects.maps) return;
        var path = MapsPath(rig);
        if (path != null) rig.surfaceEffects.maps = AssetDatabase.LoadAssetAtPath<ReaniIKSurfaceMaps>(path);
    }
    public static ReaniIKSurfaceMaps Ensure(ReaniIK rig)
    {
        BindExisting(rig);
        if (rig.surfaceEffects.maps) return rig.surfaceEffects.maps;
        string path = MapsPath(rig);
        if (path == null) path = AssetDatabase.GenerateUniqueAssetPath("Assets/Reanimator.surfaces.asset");
        var maps = ScriptableObject.CreateInstance<ReaniIKSurfaceMaps>();
        AssetDatabase.CreateAsset(maps, path);
        Undo.RecordObject(rig, "Assign sprite surface masks"); rig.surfaceEffects.maps = maps;
        EditorUtility.SetDirty(rig); PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
        return maps;
    }
    public static Texture2D Readable(Texture source)
    {
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var old = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt); RenderTexture.active = rt;
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); result.Apply(); return result;
        }
        finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
    }
    public static Texture2D Extract(Sprite sprite)
    {
        var atlas = Readable(sprite.texture);
        try
        {
            ReaniIKSurfaceMaps.GetUVTransform(sprite, out var x, out var y);
            float det = x.x * y.y - x.y * y.x;
            if (Mathf.Abs(det) < 1e-10f) throw new InvalidOperationException("Degenerate sprite UV mapping: " + sprite.name);
            int w = Mathf.Max(2, Mathf.RoundToInt(sprite.rect.width)); int h = Mathf.Max(2, Mathf.RoundToInt(sprite.rect.height));
            float scale = Mathf.Min(1, 512f / Mathf.Max(w, h)); w = Mathf.Max(2, Mathf.RoundToInt(w * scale)); h = Mathf.Max(2, Mathf.RoundToInt(h * scale));
            var pixels = new Color[w * h];
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
            {
                float px = (i + .5f) / w - x.z, py = (j + .5f) / h - y.z;
                float u = (px * y.y - x.y * py) / det, v = (x.x * py - px * y.x) / det;
                pixels[j * w + i] = atlas.GetPixelBilinear(u, v);
            }
            var result = new Texture2D(w, h, TextureFormat.RGBA32, false, true); result.SetPixels(pixels); result.Apply(); return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(atlas); }
    }
    public static ReaniIKSurfaceMaps.Entry EnsureEntry(ReaniIKSurfaceMaps maps, Sprite sprite)
    {
        var entry = maps.Find(sprite); if (entry != null && entry.normalAndBlush && entry.coverage) return entry;
        if (entry == null) { entry = new ReaniIKSurfaceMaps.Entry { sprite = sprite, effectsEnabled = IsSkinSprite(sprite) }; maps.sprites.Add(entry); }
        string root = Path.ChangeExtension(AssetDatabase.GetAssetPath(maps), null) + "_textures";
        Directory.CreateDirectory(root);
        string id; long local; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out id, out local);
        string name = string.Concat(sprite.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')) + "_" + local;
        var image = Extract(sprite);
        try
        {
            var coverage = image.GetPixels();
            for (int i = 0; i < coverage.Length; i++) coverage[i] = new Color(1, 1, 1, coverage[i].a);
            image.SetPixels(coverage); image.Apply();
            entry.coverage = WriteTexture(root + "/" + name + "_coverage.png", image);
            if (!entry.normalAndBlush)
            {
                var surface = GenerateNormals(coverage, image.width, image.height, null);
                image.SetPixels(surface); image.Apply();
                entry.normalAndBlush = WriteTexture(root + "/" + name + "_rgba.png", image);
            }
            string key = sprite.name.ToLowerInvariant();
            entry.sweat = key.Contains("arm") || key.Contains("leg") || key.Contains("body") || key.Contains("face") || key.Contains("head") || key.Contains("neck");
            EditorUtility.SetDirty(maps); AssetDatabase.SaveAssetIfDirty(maps); return entry;
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    public static Color[] GenerateNormals(Color[] coverage, int width, int height, Color[] previous)
    {
        var distance = new float[coverage.Length];
        float far = width + height;
        for (int i = 0; i < distance.Length; i++) distance[i] = coverage[i].a < .5f ? 0 : far;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int k = y * width + x;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1) distance[k] = Mathf.Min(distance[k], 1);
            if (x > 0) distance[k] = Mathf.Min(distance[k], distance[k - 1] + 1);
            if (y > 0) distance[k] = Mathf.Min(distance[k], distance[k - width] + 1);
        }
        for (int y = height - 1; y >= 0; y--) for (int x = width - 1; x >= 0; x--)
        {
            int k = y * width + x;
            if (x < width - 1) distance[k] = Mathf.Min(distance[k], distance[k + 1] + 1);
            if (y < height - 1) distance[k] = Mathf.Min(distance[k], distance[k + width] + 1);
        }
        var result = new Color[distance.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int k = y * width + x;
            float dx = (distance[y * width + Mathf.Min(x + 1, width - 1)] - distance[y * width + Mathf.Max(0, x - 1)]) * .5f;
            float dy = (distance[Mathf.Min(y + 1, height - 1) * width + x] - distance[Mathf.Max(0, y - 1) * width + x]) * .5f;
            var n = new Vector3(-dx * .8f, -dy * .8f, 1).normalized;
            result[k] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, previous == null ? 0 : previous[k].a);
        }
        return result;
    }
    public static Texture2D WriteTexture(string path, Texture2D texture)
    {
        File.WriteAllBytes(path, texture.EncodeToPNG()); AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
        importer.isReadable = true; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
