#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ReanimatorClippedSideAssFixture
{
    private static PsdLayerPreviewRenderer renderer;
    private static List<(string name, int parent, Vector2 start, Vector2 end)> skeleton;
    private static string asset, guid;
    private static int targets;
    private static UnityEngine.Object selection;

    public static string Begin(string source, Reanimator window)
    {
        var api = (Reanimator.SystemApi)typeof(Reanimator).GetProperty("Api", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).GetValue(window);
        skeleton = ReanimatorSkeletronModule.CapturePlacement(api);
        targets = api.PreviewRenderer.SideAssAllInTargets;
        if (skeleton == null) throw new Exception("Capture the failing skeleton placement first.");
        selection = Selection.activeObject;
        asset = AssetDatabase.GenerateUniqueAssetPath("Assets/__ClippedSideAssFixture.psd");
        if (!AssetDatabase.CopyAsset(source, asset)) throw new Exception("Could not create isolated PSD fixture.");
        guid = AssetDatabase.AssetPathToGUID(asset);
        renderer = new PsdLayerPreviewRenderer();
        PsdLayerPreviewRenderer.SideAssJoinFailureProbe = json => File.WriteAllText("Temp/SideAssClippedJoinFailure.json", json);
        renderer.RequestPreviewCacheRefresh(asset);
        return Tick();
    }

    public static string Tick()
    {
        for (int i = 0; i < 100 && renderer.IsPreviewCacheRefreshInProgress; i++) renderer.TickPreviewCacheRefresh(.03);
        if (renderer.IsPreviewCacheRefreshInProgress) return "Pending: " + renderer.PreviewCacheRefreshStatus;
        try
        {
            if (!renderer.TryApplySkeletron(skeleton, out var error, includeHeadControls: true)) throw new Exception(error);
            renderer.ApplyRiggedLimbAutoWeights((1 << 7) | (1 << 8), true);
            renderer.ApplyAutomaticElbowJoints((1 << 2) | (1 << 3));
            renderer.SideAssAllInTargets = targets;
            var first = renderer.ApplySelectedSideAss();
            var second = renderer.ApplySelectedSideAss();
            int expected = ((targets & 1) != 0 ? 1 : 0) + ((targets & 2) != 0 ? 1 : 0);
            if (first.Length != expected || second.Length != expected || expected == 0) throw new Exception("All selected legs must complete, including repeated application.");
            if (!renderer.Apply(asset, false, out error)) throw new Exception("Apply: " + error);
            var cooked = ReanimatorPrefabCooker.CookNextToPsd(asset, first, renderer);
            if (!string.IsNullOrEmpty(cooked.Error) || string.IsNullOrEmpty(cooked.PrefabAssetPath)) throw new Exception("Cook: " + cooked.Error);
            return "PASS: failing PSD and captured skeleton; leg weights, elbow stage, selected Side Ass fans twice, Apply and prefab Cook. Source unchanged.";
        }
        finally
        {
            Selection.activeObject = selection;
            if (!string.IsNullOrEmpty(asset) && asset.StartsWith("Assets/__ClippedSideAssFixture", StringComparison.Ordinal))
            {
                AssetDatabase.DeleteAsset(Path.ChangeExtension(asset, ".prefab"));
                AssetDatabase.DeleteAsset(asset);
                var backup = Path.GetFullPath(asset + ".backups~");
                if (backup.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            if (!string.IsNullOrEmpty(guid) && guid.Length == 32)
                foreach (var folder in new[] { "SideAss", "PreviewBoneRigs" })
                {
                    var path = Path.Combine("Library", "Reanimator", folder, guid + ".json");
                    if (File.Exists(path)) File.Delete(path);
                }
            renderer = null;
            PsdLayerPreviewRenderer.SideAssJoinFailureProbe = null;
        }
    }
}
#endif
