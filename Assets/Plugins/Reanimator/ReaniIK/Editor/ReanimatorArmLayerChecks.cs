#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;

public static class ReanimatorArmLayerChecks
{
    public static string VerifyRearOrderGap(GameObject source)
    {
        var copy=UnityEngine.Object.Instantiate(source);copy.SetActive(false);
        try
        {
            var rig=copy.GetComponent<ReaniIK>();
            int[] first=null;
            for(int pass=0;pass<2;pass++)
            {
                ReanimatorPrefabArmLayers.Configure(rig);
                var sprites=copy.GetComponentsInChildren<SpriteRenderer>(true);
                var body=sprites.Where(s=>s.sprite && new[]{"body","l_leg","r_leg"}.Contains(s.sprite.name)).ToArray();
                int min=body.Min(s=>s.sortingOrder);
                foreach(var mask in new[]{rig.leftArmMask,rig.rightArmMask})
                {
                    Require(mask.backRenderer.sortingOrder<min,"Rear arm must be below body and legs.");
                    foreach(var background in sprites.Where(s=>s!=mask.backRenderer && s!=rig.leftArmMask.backRenderer &&
                        s!=rig.rightArmMask.backRenderer && s!=rig.leftArmMask.frontRenderer && s!=rig.rightArmMask.frontRenderer && s.sortingOrder<min))
                        Require(mask.backRenderer.sortingOrder>background.sortingOrder,"Rear arm ties or falls below lower artwork.");
                }
                var orders=sprites.Select(s=>s.sortingOrder).ToArray();
                Require(first==null || first.SequenceEqual(orders),"Repeated setup shifted rendering orders again.");
                first=orders;
            }
            return "PASS: rear arms have a strict order between background and body; repeated setup is stable.";
        }
        finally{UnityEngine.Object.DestroyImmediate(copy);}
    }
    public static string RunCook(string psdPath)
    {
        var selected = Selection.activeObject;
        string fixture = AssetDatabase.GenerateUniqueAssetPath("Assets/__ArmLayerCookVerification.psd");
        string prefab = System.IO.Path.ChangeExtension(fixture, ".prefab").Replace('\\', '/');
        try
        {
            Require(AssetDatabase.CopyAsset(psdPath, fixture), "Could not copy the PSD fixture.");
            var cooker = typeof(ReaniIK).Assembly.GetType("ReanimatorPrefabCooker");
            var cook = cooker.GetMethod("CookNextToPsd", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            for (int run = 0; run < 2; run++)
            {
                object result = cook.Invoke(null, new object[] { fixture, new[] { "l_arm", "r_arm" }, null });
                var error = (string)result.GetType().GetField("Error").GetValue(result);
                Require(string.IsNullOrEmpty(error), "Cook failed: " + error);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                Require(asset, "Cook did not save the prefab.");
                var rig = asset.GetComponent<ReaniIK>();
                var originals = asset.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer =>
                    renderer != rig.leftArmMask.backRenderer && renderer != rig.rightArmMask.backRenderer).ToArray();
                int max = originals.Max(renderer => renderer == rig.leftArmMask.frontRenderer ? rig.leftArmMask.sourceSortingOrder :
                    renderer == rig.rightArmMask.frontRenderer ? rig.rightArmMask.sourceSortingOrder : renderer.sortingOrder);
                int min = originals.Where(renderer => renderer.sprite &&
                    new[] { "body", "l_leg", "r_leg" }.Contains(renderer.sprite.name)).Min(renderer => renderer.sortingOrder);
                Check(rig, originals.Length + 2, max + 1, min - 1);
            }
            return "PASS: full Cook Prefab twice from a copy of " + psdPath + ", both arm pairs and persisted bindings/orders.";
        }
        finally
        {
            Selection.activeObject = selected;
            AssetDatabase.DeleteAsset(prefab);
            AssetDatabase.DeleteAsset(fixture);
        }
    }

    public static string Run(GameObject source)
    {
        var sourceMaterials = source.GetComponentsInChildren<SpriteRenderer>(true)
            .ToDictionary(renderer => renderer, renderer => renderer.sharedMaterial);
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = "ArmLayerVerification";
        copy.SetActive(false);
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__ArmLayerVerification.prefab");
        GameObject loaded = null;
        try
        {
            var rig = copy.GetComponent<ReaniIK>();
            var originals = copy.GetComponentsInChildren<SpriteRenderer>(true);
            int max = originals.Max(renderer => renderer.sortingOrder);
            int min = originals.Where(renderer => renderer.sprite &&
                new[] { "body", "l_leg", "r_leg" }.Contains(renderer.sprite.name)).Min(renderer => renderer.sortingOrder);
            Require(ReanimatorPrefabArmLayers.Configure(rig) == 2, "First build should add exactly two rear sprites.");
            Check(rig, originals.Length + 2, max + 1, min - 1);
            for (int i = 0; i < 3; i++)
            {
                Require(ReanimatorPrefabArmLayers.Configure(rig) == 0, "Repeated setup duplicated arm sprites.");
                Check(rig, originals.Length + 2, max + 1, min - 1);
            }
            // Recover missing references from the generated hierarchy without duplicating the rear sprites.
            rig.leftArmMask.backRenderer = null;
            rig.rightArmMask.backRenderer = null;
            Require(ReanimatorPrefabArmLayers.Configure(rig) == 0, "Lost rear references should be repaired.");
            Check(rig, originals.Length + 2, max + 1, min - 1);
            var saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
            Require(saved, "Prefab save failed.");
            loaded = PrefabUtility.LoadPrefabContents(path);
            Check(loaded.GetComponent<ReaniIK>(), originals.Length + 2, max + 1, min - 1);
            return "PASS: both arm pairs, exact orders " + (max + 1) + "/" + (min - 1) +
                ", shared skin bindings, three repeated runs, missing-reference repair and prefab save/reload.";
        }
        finally
        {
            if (loaded) PrefabUtility.UnloadPrefabContents(loaded);
            UnityEngine.Object.DestroyImmediate(copy);
            AssetDatabase.DeleteAsset(path);
            foreach (var pair in sourceMaterials)
                Require(pair.Value && pair.Key.sharedMaterial == pair.Value,
                    "Cloning a rig with an active mask damaged the source material: " + pair.Key.name);
        }
    }

    private static void Check(ReaniIK rig, int rendererCount, int frontOrder, int backOrder)
    {
        Require(rig.GetComponentsInChildren<SpriteRenderer>(true).Length == rendererCount, "Unexpected sprite count.");
        foreach (var mask in new[] { rig.leftArmMask, rig.rightArmMask })
        {
            Require(mask.amount == 1f, "Fabricated front arm must be fully hidden through its mask.");
            var front = mask.frontRenderer;
            var back = mask.backRenderer;
            Require(front && back && front != back, "Missing front/rear pair.");
            Require(front.sortingOrder == frontOrder && back.sortingOrder == backOrder, "Arm order drifted.");
            Require(front.sprite == back.sprite, "Arm copies use different sprites.");
            Require(front.sharedMaterial == back.sharedMaterial, "Base materials differ.");
            Require(front.transform.localPosition == back.transform.localPosition &&
                front.transform.localRotation == back.transform.localRotation &&
                front.transform.localScale == back.transform.localScale, "Arm copies are not aligned.");
            var frontSkin = front.GetComponent<SpriteSkin>();
            var backSkin = back.GetComponent<SpriteSkin>();
            Require(frontSkin.rootBone == backSkin.rootBone &&
                frontSkin.boneTransforms.SequenceEqual(backSkin.boneTransforms), "Arm copies do not share the same bones.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
