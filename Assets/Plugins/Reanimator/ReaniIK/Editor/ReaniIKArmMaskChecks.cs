#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Render the real skinned arm in isolation. Does not change scene assets or leave preview settings behind.
public static class ReaniIKArmMaskChecks
{
    public static string Run(ReaniIK rig, string outputPath, bool rightArm = false)
    {
        var mask = rightArm ? rig.rightArmMask : rig.leftArmMask;
        var limb = rightArm ? rig.rightArm : rig.leftArm;
        float savedAmount = mask.amount;
        bool savedFlip = mask.flip;
        float savedSoftness = mask.softness;
        var materials = new Dictionary<SpriteRenderer, Material>();
        mask.Release();
        foreach (var renderer in rig.GetComponentsInChildren<SpriteRenderer>(true))
            materials.Add(renderer, renderer.sharedMaterial);
        SpriteRenderer arm = null;
        int layer = 0;
        var cameraObject = new GameObject("Arm Mask Verification") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        var texture = new RenderTexture(256, 384, 24, RenderTextureFormat.ARGB32);
        var readback = new Texture2D(256, 384, TextureFormat.RGBA32, false);
        var sheet = new Texture2D(1280, 768, TextureFormat.RGBA32, false);
        var previousRT = RenderTexture.active;
        var counts = new List<int>();
        try
        {
            var shader = Shader.Find("Reanimator/Arm Mask");
            var compatibility = typeof(UnityEditor.ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (compatibility != null && (int)compatibility.Invoke(null, new object[] { shader, 0 }) != 0)
                throw new InvalidOperationException("Mask shader is not SRP Batcher compatible; GPU sprite deformation would be lost.");
            mask.amount = 0.5f;
            mask.Apply(rig.transform, limb);
            arm = mask.Renderer;
            if (!arm || mask.Status != null) throw new InvalidOperationException(mask.Status ?? "Arm not found.");
            var maskMaterial = arm.sharedMaterial;
            if (Mathf.Abs(maskMaterial.GetFloat("_SurfaceContrast") - 1f) > .0001f)
                throw new InvalidOperationException("Mask did not preserve neutral contrast.");
            layer = arm.gameObject.layer;
            arm.gameObject.layer = 31;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            var bounds = arm.bounds;
            camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x * 1.5f) * 1.1f;
            camera.transform.position = bounds.center + Vector3.back * 10f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.targetTexture = texture;
            arm.sharedMaterial = materials[arm];
            camera.Render();
            RenderTexture.active = texture;
            readback.ReadPixels(new Rect(0, 0, 256, 384), 0, 0);
            readback.Apply();
            var sourcePixels = readback.GetPixels32();
            arm.sharedMaterial = maskMaterial;
            Color32[] defaultHalf = null;
            for (int row = 0; row < 2; row++)
            {
                int previous = int.MaxValue;
                for (int column = 0; column < 5; column++)
                {
                    mask.amount = column * 0.25f;
                    mask.flip = row == 1;
                    mask.softness = 0f;
                    mask.Apply(rig.transform, limb);
                    if (arm.sharedMaterial != maskMaterial)
                        throw new InvalidOperationException("Slider recreated the arm material at an endpoint.");
                    foreach (var pair in materials)
                        if (pair.Key != arm && pair.Key.sharedMaterial != pair.Value)
                            throw new InvalidOperationException("Mask affected another sprite: " + pair.Key.name);
                    camera.Render();
                    RenderTexture.active = texture;
                    readback.ReadPixels(new Rect(0, 0, 256, 384), 0, 0);
                    readback.Apply();
                    var pixels = readback.GetPixels32();
                    int colorSamples = 0, colorErrors = 0;
                    for (int i = 0; i < pixels.Length; i++)
                        if (pixels[i].a > 250 && sourcePixels[i].a > 250)
                        {
                            colorSamples++;
                            if (Math.Abs(pixels[i].r - sourcePixels[i].r) > 3 ||
                                Math.Abs(pixels[i].g - sourcePixels[i].g) > 3 ||
                                Math.Abs(pixels[i].b - sourcePixels[i].b) > 3) colorErrors++;
                        }
                    if (column < 4 && (colorSamples < 50 || colorErrors > colorSamples / 100))
                    {
                        File.WriteAllBytes(outputPath + ".color.png", readback.EncodeToPNG());
                        readback.SetPixels32(sourcePixels); readback.Apply();
                        File.WriteAllBytes(outputPath + ".source.png", readback.EncodeToPNG());
                        throw new InvalidOperationException("Visible arm color differs from source at " + row + "/" + column + ": " + colorErrors + "/" + colorSamples);
                    }
                    if (row == 0 && column == 0)
                    {
                        // A nearly zero mask must retain the exact same skinned silhouette.
                        mask.amount = 0.000001f;
                        mask.Apply(rig.transform, limb);
                        camera.Render();
                        RenderTexture.active = texture;
                        readback.ReadPixels(new Rect(0, 0, 256, 384), 0, 0);
                        readback.Apply();
                        var masked = readback.GetPixels32();
                        int differences = 0, visible = 0;
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            if (pixels[i].a > 8) visible++;
                            if (Math.Abs(pixels[i].a - masked[i].a) > 32) differences++;
                        }
                        if (differences > Mathf.Max(32, visible / 100))
                        {
                            File.WriteAllBytes(outputPath + ".masked.png", readback.EncodeToPNG());
                            readback.SetPixels32(pixels);
                            readback.Apply();
                            File.WriteAllBytes(outputPath + ".original.png", readback.EncodeToPNG());
                            throw new InvalidOperationException("Mask material changed the " + (rightArm ? "right" : "left") + " skinned arm silhouette: " + differences + " pixels.");
                        }
                    }
                    int count = 0;
                    foreach (var p in pixels) if (p.a > 8) count++;
                    if (column == 0 && count == 0) throw new InvalidOperationException("Arm render is empty.");
                    if (column > 0 && count > previous + 8) throw new InvalidOperationException("Hidden area is not monotonic: row " + row + ", column " + column + ", count " + count + ", previous " + previous);
                    if (column == 4 && count != 0) throw new InvalidOperationException("Amount 1 did not hide the full arm.");
                    previous = count;
                    counts.Add(count);
                    if (column == 2)
                    {
                        if (row == 0) defaultHalf = pixels;
                        else
                        {
                            int differences = 0;
                            for (int i = 0; i < pixels.Length; i++)
                                if (Math.Abs(pixels[i].a - defaultHalf[i].a) > 32) differences++;
                            if (differences < 100) throw new InvalidOperationException("Flip did not reverse the mask.");
                        }
                    }
                    for (int y = 0; y < 384; y++)
                        for (int x = 0; x < 256; x++)
                        {
                            var foreground = (Color)pixels[y * 256 + x];
                            var background = ((x / 16 + y / 16) & 1) == 0
                                ? new Color(0.16f, 0.19f, 0.23f) : new Color(0.22f, 0.25f, 0.29f);
                            var color = Color.Lerp(background, foreground, foreground.a);
                            color.a = 1f;
                            sheet.SetPixel(column * 256 + x, (1 - row) * 384 + y, color);
                        }
                }
            }
            mask.Release();
            foreach (var pair in materials)
                if (pair.Key.sharedMaterial != pair.Value)
                    throw new InvalidOperationException("Material changed outside the arm or was not restored: " + pair.Key.name);
            sheet.Apply();
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            return "PASS: default and flipped 0/25/50/75/100%, monotonic coverage, full hide, material restoration and other-sprite isolation. Pixel counts: " +
                string.Join(", ", counts) + ". Preview: " + outputPath;
        }
        finally
        {
            if (arm) arm.gameObject.layer = layer;
            mask.Release();
            mask.amount = savedAmount;
            mask.flip = savedFlip;
            mask.softness = savedSoftness;
            mask.Apply(rig.transform, limb);
            RenderTexture.active = previousRT;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }
}
#endif
