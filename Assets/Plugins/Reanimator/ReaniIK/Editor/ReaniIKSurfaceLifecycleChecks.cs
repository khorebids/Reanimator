#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

public static class ReaniIKSurfaceLifecycleChecks
{
    public static string Run()
    {
        var root = new GameObject("Surface lifecycle check") { hideFlags = HideFlags.HideAndDontSave };
        var texture = new Texture2D(16, 32, TextureFormat.RGBA32, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 32), Vector2.one * .5f, 16, 0, SpriteMeshType.FullRect);
        sprite.name = "body";
        var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        var source = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        renderer.sharedMaterial = source;
        var effects = new ReaniIKSurfaceEffects();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                effects.tremor = 1; effects.Tick(root.transform);
                Require(renderer.sharedMaterial && renderer.sharedMaterial != source, "Effect material was not assigned.");
                effects.tremor = 0; effects.Tick(root.transform);
                Require(renderer.sharedMaterial == source, "Disabling tremor failed to restore its original material.");
            }
            effects.tremor = 1; effects.Tick(root.transform);
            var outline = typeof(ReaniIKSurfaceEffects).GetField("outline", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effects);
            var draw = outline.GetType().GetMethod("Draw");
            draw.Invoke(outline, new object[] { root.transform, 1f, .004f, 15, 9f, 100f, 0f });
            var mesh = root.transform.Find("Tremor outline").GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            float fullSize = Vector3.Distance(vertices[1], (vertices[0] + vertices[2]) * .5f);
            draw.Invoke(outline, new object[] { root.transform, .5f, .004f, 15, 9f, 100f, 0f });
            vertices = mesh.vertices;
            float halfSize = Vector3.Distance(vertices[1], (vertices[0] + vertices[2]) * .5f);
            Require(Mathf.Abs(fullSize - halfSize) < .00001f, "The quantity slider changed peak size.");
            effects.tremorGroups = 15; effects.tremor = .5f;
            Require(effects.TargetTremorGroups == 8 && effects.ActiveTremorGroups <= 8, "Half slider must request half the maximum groups.");
            effects.tremor = 0;
            Require(effects.TargetTremorGroups == 0, "Zero slider must request zero groups.");
            // A destroyed/restored reference must never be installed as a null material.
            UnityEngine.Object.DestroyImmediate(source);
            effects.Tick(root.transform);
            Require(renderer.sharedMaterial && renderer.sharedMaterial.shader && renderer.sharedMaterial.shader.isSupported &&
                renderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader", "Lost original material caused a magenta/null fallback.");
            renderer.sharedMaterial = null;
            effects.RepairMissingMaterials(root.transform);
            Require(renderer.sharedMaterial, "Existing null-material state was not repaired.");
            return "PASS: three enable/disable cycles preserve the original material, destroyed/null originals recover safely, zero/half/full quantity semantics, peak size independent of slider.";
        }
        finally
        {
            effects.Release(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture); if (source) UnityEngine.Object.DestroyImmediate(source);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
