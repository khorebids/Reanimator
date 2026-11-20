#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ReanimatorElbowParentFixture
{
    private static PsdLayerPreviewRenderer renderer;
    private static string asset, guid;
    private static UnityEngine.Object selection;
    public static string Begin(string source)
    {
        selection = Selection.activeObject;
        asset = AssetDatabase.GenerateUniqueAssetPath("Assets/__ElbowParentFixture.psd");
        if (!AssetDatabase.CopyAsset(source, asset)) throw new InvalidOperationException("Could not copy elbow fixture.");
        guid = AssetDatabase.AssetPathToGUID(asset);
        renderer = new PsdLayerPreviewRenderer(); renderer.RequestPreviewCacheRefresh(asset);
        return Tick();
    }
    public static string Tick()
    {
        for (int i = 0; i < 100 && renderer.IsPreviewCacheRefreshInProgress; i++) renderer.TickPreviewCacheRefresh(.03);
        if (renderer.IsPreviewCacheRefreshInProgress) return "Pending: " + renderer.PreviewCacheRefreshStatus;
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = typeof(PsdLayerPreviewRenderer);
            int validated = 0;
            foreach (string name in new[] { "l_arm", "r_arm", "l_leg", "r_leg" })
            {
                var layer = renderer.Layers.First(l => l.name == name);
                int child = renderer.Bones.ToList().FindIndex(b => b.name == name + "_1");
                type.GetField("selectedBoneIndex", flags).SetValue(renderer, child);
                type.GetMethod("SelectLayer", flags).Invoke(renderer, new object[] { layer });
                var planArgs = new object[] { null, null };
                Require((bool)type.GetMethod("TryBuildAutomaticElbowPlan", flags).Invoke(renderer, planArgs), name + ": " + planArgs[1]);
                var plan = planArgs[0];
                var contour = (Vector2[][])plan.GetType().GetField("parentContour", flags).GetValue(plan);
                var apply = new object[] { null };
                Require((bool)type.GetMethod("ApplyCorrectiveElbow", flags).Invoke(renderer, apply), name + ": " + apply[0]);
                var args = new object[] { layer, null }; type.GetMethod("TryGetLayerVertexData", flags).Invoke(renderer, args);
                var data = args[1];
                var draw = (Vector2[])type.GetMethod("GetLayerVerticesForDraw", flags).Invoke(renderer, new[] { data });
                var weights = (BoneWeight[])data.GetType().GetField("boneWeights", flags).GetValue(data);
                var mapping = (int[])data.GetType().GetField("spriteBoneToRendererBone", flags).GetValue(data);
                int parent = renderer.Bones[child].parentId;
                int slot = Array.IndexOf(mapping, parent);
                foreach (var side in contour)
                {
                    Vector2 target = side[side.Length - 1]; int nearest = Enumerable.Range(0, draw.Length).OrderBy(i => (draw[i] - target).sqrMagnitude).First();
                    Require(Vector2.Distance(draw[nearest], target) < .05f, name + ": parent endpoint was not retained.");
                    var w = weights[nearest]; float upper = (w.boneIndex0 == slot ? w.weight0 : 0) + (w.boneIndex1 == slot ? w.weight1 : 0) + (w.boneIndex2 == slot ? w.weight2 : 0) + (w.boneIndex3 == slot ? w.weight3 : 0);
                    Require(upper > .999f, name + ": far parent endpoint must be fully parent weighted.");
                }
                foreach (var w in weights) Require(Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) < .001f, name + ": unnormalized weight.");
                validated++;
            }
            Require(renderer.Apply(asset, false, out var error), "Fixture Apply: " + error);
            var cooked = ReanimatorPrefabCooker.CookNextToPsd(asset, new[] { "l_arm", "r_arm", "l_leg", "r_leg" }, renderer);
            Require(string.IsNullOrEmpty(cooked.Error), "Fixture Cook: " + cooked.Error);
            return "PASS: parent-side transitions applied and checked on " + validated + " real limbs; endpoints fully parent weighted; normalized weights; Apply and Cook succeed.";
        }
        finally
        {
            Selection.activeObject = selection;
            AssetDatabase.DeleteAsset(Path.ChangeExtension(asset, ".prefab")); AssetDatabase.DeleteAsset(asset);
            string recipe = Path.Combine("Library", "Reanimator", "PreviewBoneRigs", guid + ".json"); if (File.Exists(recipe)) File.Delete(recipe);
            string backup = Path.GetFullPath(asset + ".backups~");
            if (backup.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(backup).StartsWith("__ElbowParentFixture", StringComparison.Ordinal) && Directory.Exists(backup)) Directory.Delete(backup, true);
            renderer = null;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
