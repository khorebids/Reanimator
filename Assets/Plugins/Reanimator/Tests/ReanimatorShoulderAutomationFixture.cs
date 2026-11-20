#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ReanimatorShoulderAutomationFixture
{
    private static PsdLayerPreviewRenderer renderer;
    private static string asset, guid, profile;
    private static UnityEngine.Object previousSelection;
    public static string Begin(string source)
    {
        previousSelection = Selection.activeObject;
        asset = AssetDatabase.GenerateUniqueAssetPath("Assets/__ShoulderAutomationFixture.psd");
        if (!AssetDatabase.CopyAsset(source, asset)) throw new InvalidOperationException("Could not create the shoulder automation fixture.");
        guid = AssetDatabase.AssetPathToGUID(asset);
        profile = File.ReadAllText("ProjectSettings/ReanimatorShoulderSettings.json");
        renderer = new PsdLayerPreviewRenderer(); renderer.RequestPreviewCacheRefresh(asset);
        return Tick();
    }
    public static string Tick()
    {
        if (renderer == null) throw new InvalidOperationException("Start the fixture first.");
        for (int i = 0; i < 100 && renderer.IsPreviewCacheRefreshInProgress; i++) renderer.TickPreviewCacheRefresh(.05);
        if (renderer.IsPreviewCacheRefreshInProgress) return "Pending: " + renderer.PreviewCacheRefreshStatus;
        try
        {
            Require(renderer.GetShoulderDraftError() != null, "Missing manual outlines must be caught by preflight.");
            foreach (string name in new[] { "l_arm", "r_arm" })
            {
                var layer = renderer.FindShoulderSprite(name);
                var bone = renderer.Bones.First(item => item.name == name);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var args = new object[] { layer, null };
                typeof(PsdLayerPreviewRenderer).GetMethod("TryGetLayerVertexData", flags).Invoke(renderer, args);
                var data = args[1];
                var draw = (Vector2[])typeof(PsdLayerPreviewRenderer).GetMethod("GetLayerVerticesForDraw", flags).Invoke(renderer, new[] { data });
                var edges = (int[])data.GetType().GetField("boundaryEdgePairs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(data);
                var axis = (Vector2)(bone.worldRotation * Vector3.right); var normal = new Vector2(-axis.y, axis.x);
                Require(PsdLayerPreviewRenderer.TryGetShoulderReferenceWidth(draw, edges, bone.worldPosition, axis, out var a, out var b), "Fixture arm cross-section is missing.");
                float width = Vector2.Distance(a, b); var origin = bone.worldPosition;
                renderer.SetShoulderContour(name, new[] { origin - axis * width * 2 - normal * width * 2,
                    origin - axis * width * 2 + normal * width * 2, origin + axis * width * .25f + normal * width * 2,
                    origin + axis * width * .25f - normal * width * 2 });
            }
            Require(renderer.GetShoulderDraftError() == null, "Both manual outlines must satisfy preflight.");
            var fresh = new PsdLayerPreviewRenderer();
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PsdLayerPreviewRenderer).GetField("cachedPath", fields).SetValue(fresh, asset);
            typeof(PsdLayerPreviewRenderer).GetField("cachedDocumentSize", fields).SetValue(fresh, renderer.DocumentSize);
            Require(fresh.GetShoulderContour("l_arm").Length == 4 && fresh.GetShoulderContour("r_arm").Length == 4, "Both hand drafts must survive a fresh renderer.");
            renderer.ApplyAllShoulderContours();
            string Fingerprint(string bone)
            {
                var layer = renderer.FindShoulderSprite(bone); var args = new object[] { layer, null };
                typeof(PsdLayerPreviewRenderer).GetMethod("TryGetLayerVertexData", fields).Invoke(renderer, args);
                return (string)typeof(PsdLayerPreviewRenderer).GetMethod("ShoulderFanSignature", fields).Invoke(renderer, new[] { args[1] });
            }
            var left = Fingerprint("l_arm"); var right = Fingerprint("r_arm");
            renderer.ApplyAllShoulderContours(); renderer.ApplyAllShoulderContours();
            Require(left == Fingerprint("l_arm") && right == Fingerprint("r_arm"), "Repeated bilateral automation must preserve both meshes and weights.");
            Require(renderer.Apply(asset, false, out var error), "Fixture Apply failed: " + error);
            var cooked = ReanimatorPrefabCooker.CookNextToPsd(asset, new[] { "l_arm", "r_arm" }, renderer);
            Require(string.IsNullOrEmpty(cooked.Error), "Fixture Cook failed: " + cooked.Error);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cooked.PrefabAssetPath);
            Require(prefab.GetComponentsInChildren<ShoulderAttachmentRig2D>(true).Length == 2, "Prefab must contain both automatic shoulder helpers.");
            Require(profile == File.ReadAllText("ProjectSettings/ReanimatorShoulderSettings.json"), "Automation must preserve the user's saved curve profile.");
            return "PASS: missing-outline preflight, separate persisted arm drafts, both shoulders applied three times without geometry drift, Apply and Cook produce two runtime shoulder helpers, saved curves unchanged.";
        }
        finally
        {
            Selection.activeObject = previousSelection;
            AssetDatabase.DeleteAsset(Path.ChangeExtension(asset, ".prefab")); AssetDatabase.DeleteAsset(asset);
            foreach (string directory in new[] { "ShoulderContours", "PreviewBoneRigs" })
            {
                string file = Path.Combine("Library", "Reanimator", directory, guid + ".json");
                if (File.Exists(file)) File.Delete(file);
            }
            if (profile != null) File.WriteAllText("ProjectSettings/ReanimatorShoulderSettings.json", profile);
            string backup = Path.GetFullPath(asset + ".backups~");
            if (backup.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(backup).StartsWith("__ShoulderAutomationFixture", StringComparison.Ordinal) && Directory.Exists(backup)) Directory.Delete(backup, true);
            renderer = null;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
