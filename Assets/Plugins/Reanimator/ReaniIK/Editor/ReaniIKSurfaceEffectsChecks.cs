#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ReaniIKSurfaceEffectsChecks
{
    public static string Run(string output)
    {
        var root = new GameObject("Surface effects verification") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
        var texture = new Texture2D(64, 128, TextureFormat.RGBA32, false, true);
        var normal = new Texture2D(64, 128, TextureFormat.RGBA32, false, true);
        var coverage = new Texture2D(64, 128, TextureFormat.RGBA32, false, true);
        var maps = ScriptableObject.CreateInstance<ReaniIKSurfaceMaps>();
        var pixels = new Color[64 * 128]; var normals = new Color[pixels.Length]; var cov = new Color[pixels.Length];
        for (int y = 0; y < 128; y++) for (int x = 0; x < 64; x++)
        {
            int i = y * 64 + x; bool inside = true;
            pixels[i] = new Color(.55f, .43f, .32f, inside ? 1 : 0);
            cov[i] = new Color(1, 1, 1, inside ? 1 : 0);
            var n = new Vector3((x - 32f) / 60, (y - 64f) / 180, 1).normalized;
            normals[i] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
        }
        texture.SetPixels(pixels); texture.Apply(); normal.SetPixels(normals); normal.Apply(); coverage.SetPixels(cov); coverage.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 128), Vector2.one * .5f, 64, 0, SpriteMeshType.FullRect);
        sprite.name = "body";
        var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        var original = renderer.sharedMaterial;
        maps.sprites.Add(new ReaniIKSurfaceMaps.Entry { sprite = sprite, normalAndBlush = normal, coverage = coverage, sweat = true });
        var effects = new ReaniIKSurfaceEffects { maps = maps };
        var cameraObject = new GameObject("Surface effects camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.2f;
        camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); camera.targetTexture = rt;
        var read = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var sheet = new Texture2D(1280, 256, TextureFormat.RGBA32, false, true);
        var oldRT = RenderTexture.active;
        var centers = new List<Color>();
        var lastTime = typeof(ReaniIKSurfaceEffects).GetField("lastTime", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            Require(Shader.Find("Reanimator/Arm Mask").isSupported, "Surface shader unavailable.");
            var generated = ReaniIKSurfaceAuthoring.GenerateNormals(cov, 64, 128, normals);
            for (int i = 0; i < normals.Length; i++) Require(generated[i].a == normals[i].a, "Generating normals changed painted redness.");
            Require(ReaniIKSurfaceMaps.GetUVTransform(sprite, out var ux, out var uy), "Sprite UV transform unavailable.");
            var p = new Vector3(sprite.uv[0].x, sprite.uv[0].y, 1);
            Require(Mathf.Abs(Vector3.Dot(ux, p) - (sprite.vertices[0].x * sprite.pixelsPerUnit + sprite.pivot.x) / sprite.rect.width) < .0001f, "Surface UV does not follow sprite vertices.");
            Shot(0);
            effects.redness = 1; effects.Tick(root.transform); Shot(1);
            Require(centers[1].r <= centers[0].r && centers[1].g < centers[0].g && centers[1].r / Mathf.Max(.001f, centers[1].g) > centers[0].r / Mathf.Max(.001f, centers[0].g), "Multiply redness failed to follow painted alpha: " + centers[0] + " → " + centers[1]);
            effects.redness = 0; effects.sweat = 1;
            int maximum = 0;
            for (int i = 0; i < 240; i++)
            {
                lastTime.SetValue(effects, EditorApplication.timeSinceStartup - .05);
                effects.Tick(root.transform); maximum = Mathf.Max(maximum, effects.ActiveDrops);
                Require(effects.ActiveDrops <= 20, "Sweat exceeded the shared 20-drop pool.");
            }
            Require(maximum == 20, "Sweat stress test did not fill the pool."); Shot(2);
            effects.sweat = 0; effects.tremor = 1; effects.Tick(root.transform); Shot(3);
            Require(root.transform.Find("Tremor outline"), "Tremor did not create an exterior contour.");
            var bursts = root.transform.Find("Tremor outline");
            var burstMesh = bursts.GetComponent<MeshFilter>().sharedMesh;
            Require(effects.ActiveTremorGroups >= 10 && effects.ActiveTremorGroups <= 20, "Tremor must remain a sparse set of 10–20 groups.");
            Require(burstMesh.triangles.Length <= 20 * 3 * 3, "Tremor became a continuous contour instead of small needle groups.");
            Require(bursts.GetComponent<MeshRenderer>().sharedMaterials.All(m => m.mainTexture == texture), "Tremor must inherit the sprite surface texture.");
            Require(read.GetPixels().Count(color => color.a > .1f) > sheet.GetPixels(0, 0, 256, 256).Count(color => color.a > .1f),
                "Tremor contour did not render outside the sprite.");
            effects.darkness = 1; effects.Tick(root.transform); Shot(4);
            Require(centers[4].maxColorComponent < .02f || (centers[4].r < .02f && centers[4].g < .02f && centers[4].b < .02f), "Darkness did not reach black.");
            Require(Mathf.Abs(centers[4].a - centers[0].a) < .02f, "Darkness changed sprite opacity.");
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))); File.WriteAllBytes(output, sheet.EncodeToPNG());
            effects.Release(); Require(renderer.sharedMaterial == original, "Disabling effects did not restore the material.");
            return "PASS: rendered redness from alpha, sprite UV alignment, normals preserve alpha, shared pool reaches but never exceeds 20 drops, exterior tremor mesh, black endpoint preserves opacity, material cleanup. Preview: " + output;
            void Shot(int column)
            {
                camera.Render(); camera.Render(); RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply();
                centers.Add(read.GetPixel(128, 128)); sheet.SetPixels(column * 256, 0, 256, 256, read.GetPixels());
            }
        }
        finally
        {
            effects.Release(); RenderTexture.active = oldRT; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (var obj in new UnityEngine.Object[] { sprite, texture, normal, coverage, maps, rt, read, sheet }) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
