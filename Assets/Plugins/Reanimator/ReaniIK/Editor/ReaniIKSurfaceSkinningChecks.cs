#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;

public static class ReaniIKSurfaceSkinningChecks
{
    public static string Status { get; private set; }
    public static string Run(GameObject source, string output, string spriteName = "l_arm")
    {
        if (Status == "Running") throw new InvalidOperationException("Skinning verification is already running.");
        Status = "Running";
        Execute(source, output, spriteName);
        return Status;
    }
    private static async void Execute(GameObject source, string output, string spriteName)
    {
        try { Status = await RunAsync(source, output, spriteName); }
        catch (Exception error) { Status = "FAIL: " + error.Message; }
    }
    private static async System.Threading.Tasks.Task Settle()
    {
        for (int frame = 0; frame < 5; frame++)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(40);
        }
    }
    private static async System.Threading.Tasks.Task<string> RunAsync(GameObject source, string output, string spriteName)
    {
        var clone = UnityEngine.Object.Instantiate(source);
        clone.name = "Surface GPU skinning regression"; clone.hideFlags = HideFlags.HideAndDontSave;
        var rig = clone.GetComponent<ReaniIK>(); rig.enabled = false;
        rig.leftArmMask.Release(); rig.rightArmMask.Release(); rig.surfaceEffects.Release();
        foreach (var tr in clone.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 31;
        var arm = clone.GetComponentsInChildren<SpriteRenderer>(true).First(r => r.name == spriteName);
        foreach (var renderer in clone.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = renderer == arm;
        var skin = arm.GetComponent<SpriteSkin>(); skin.alwaysUpdate = true;
        var bone = spriteName == "body" ? clone.GetComponentsInChildren<Transform>(true).First(t => t.name == "spine") : rig.leftArm.upper;
        if (!bone) throw new InvalidOperationException("Skinning test bone missing.");
        var rest = bone.localRotation;
        var reference = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var cameraObject = new GameObject("GPU skinning camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
        var rt = new RenderTexture(384, 384, 24, RenderTextureFormat.ARGB32); camera.targetTexture = rt;
        camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var read = new Texture2D(384, 384, TextureFormat.RGBA32, false);
        var sheet = new Texture2D(1536, 384, TextureFormat.RGBA32, false);
        var oldRT = RenderTexture.active;
        var type = typeof(SpriteSkin).Assembly.GetType("UnityEngine.U2D.Animation.DeformationManager");
        var manager = type.GetProperty("instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        var update = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        Color[][] frames = new Color[4][];
        try
        {
            arm.sharedMaterial = reference;
            // Regression for blocks left behind even after effects were disabled.
            var legacy = new MaterialPropertyBlock(); legacy.SetFloat("_SurfaceAspect", 1); arm.SetPropertyBlock(legacy);
            ReaniIKSurfaceEffects.RemoveLegacyPropertyBlocks(clone.transform);
            Require(!arm.HasPropertyBlock(), "Legacy effect MPB was not removed.");
            await Settle();
            update.Invoke(manager, null);
            var bounds = arm.bounds; camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.8f;
            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 10);
            for (int pose = 0; pose < 2; pose++)
            {
                bone.localRotation = rest * Quaternion.Euler(0, 0, pose * 35);
                await Settle();
                update.Invoke(manager, null); Capture(pose);
            }
            rig.surfaceEffects.redness = .6f; rig.surfaceEffects.darkness = .2f; rig.surfaceEffects.sweat = .5f;
            rig.surfaceEffects.maps = null; rig.surfaceEffects.tremor = 0;
            for (int pose = 0; pose < 2; pose++)
            {
                bone.localRotation = rest * Quaternion.Euler(0, 0, pose * 35);
                rig.surfaceEffects.Tick(clone.transform);
                Require(!arm.HasPropertyBlock(), "Surface effects disabled SRP batching through an MPB.");
                await Settle();
                // Material replacement queues a SpriteSkin system rebind; let
                // that registration complete before comparing rendered poses.
                update.Invoke(manager, null); update.Invoke(manager, null); Capture(pose + 2);
            }
            int moved = 0;
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))); File.WriteAllBytes(output, sheet.EncodeToPNG());
            for (int i = 0; i < frames[0].Length; i++) if (Mathf.Abs(frames[0][i].a - frames[1][i].a) > .1f) moved++;
            Require(moved > 200, "Reference SpriteSkin did not move enough to validate GPU deformation.");
            for (int pose = 0; pose < 2; pose++)
            {
                int mismatch = 0;
                for (int i = 0; i < frames[pose].Length; i++) if (Mathf.Abs(frames[pose][i].a - frames[pose + 2][i].a) > .1f) mismatch++;
                Require(mismatch < 100, "Effects shader diverged from reference SpriteSkin at pose " + pose + ": " + mismatch + " pixels.");
            }
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))); File.WriteAllBytes(output, sheet.EncodeToPNG());
            return "PASS: real SpriteSkin manager, 35-degree bone movement changes " + moved + " pixels; effect shader matches reference silhouettes at both poses; no MPBs; legacy blocks removed. " + output;
            void Capture(int index)
            {
                camera.Render(); RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
                frames[index] = read.GetPixels(); sheet.SetPixels(index * 384, 0, 384, 384, frames[index]);
            }
        }
        finally
        {
            rig.surfaceEffects.Release(); RenderTexture.active = oldRT; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(clone); UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (var item in new UnityEngine.Object[] { reference, rt, read, sheet }) UnityEngine.Object.DestroyImmediate(item);
            update.Invoke(manager, null);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
