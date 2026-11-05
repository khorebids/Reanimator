#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void ProjectHouseInteractionsRebuildFindsSpriteLayers()
    {
        const string assetPath = "Assets/0.Internal/Game/Textures/Map/house_girl_interacions.psd";
        if (AssetImporter.GetAtPath(assetPath) == null)
            return;

        var renderer = new PsdLayerPreviewRenderer();
        InvokePrivateMethod(renderer, "Rebuild", assetPath, false);

        var layers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
        AssertTrue(layers.Count > 0, "House interactions PSD rebuild should find sprite layers for Sprite Sheet mode.");
        AssertTrue(
            layers.Any(layer => layer != null && !string.IsNullOrEmpty(layer.name) && layer.sourceRect.width > 0f && layer.sourceRect.height > 0f),
            "House interactions PSD rebuild should produce drawable named sprite layers.");
    }

    private static void ProjectHeadSpriteDataProviderKeepsOptimizedRect()
    {
        const string assetPath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        if (AssetImporter.GetAtPath(assetPath) is not ISpriteEditorDataProvider dataProvider)
            return;

        var head = FindSpriteRect(dataProvider, "head");
        AssertTrue(head != null, "Project fixture should contain the head sprite.");
        AssertTrue(head.rect.width > 0f && head.rect.height > 0f, "Head SpriteDataProvider rect should stay valid after Apply.");
    }

    private static void ProjectHeadRebuildKeepsOptimizedRect()
    {
        const string assetPath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        var importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null)
            return;

        var expectedHeadRect = importer is ISpriteEditorDataProvider dataProvider
            ? FindSpriteRect(dataProvider, "head")?.rect ?? Rect.zero
            : Rect.zero;
        AssertTrue(TryReadHeadSpriteImportVector(assetPath, "spritePosition", out var expectedHeadPosition), "Project fixture should expose the serialized head spritePosition.");

        var renderer = new PsdLayerPreviewRenderer();
        InvokePrivateMethod(renderer, "Rebuild", assetPath, false);

        var layers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
        var head = layers.FirstOrDefault(layer => layer != null && string.Equals(layer.name, "head", StringComparison.Ordinal));
        AssertTrue(head != null, "Rebuilt project fixture should contain the head layer.");
        if (expectedHeadRect.width > 0f && expectedHeadRect.height > 0f)
            AssertRect(expectedHeadRect, head.sourceRect, 0.0001f, "Rebuilt head rect should match the current SpriteDataProvider rect after focus/cache reload.");
        AssertRect(head.sourceRect, head.textureRect, 0.0001f, "Rebuilt head texture rect should not restore stale imported dimensions.");
        AssertVector(expectedHeadPosition, head.position, 0.0001f, "Rebuilt head sprite position should stay stable after focus/cache reload.");
    }

    private static void SpriteApplyTracksDirtyLayerStateOnly()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var dirty = BuildLayer("dirty-layer", 201, new Vector2(10f, 20f));
        dirty.spriteGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        dirty.sourceRect = new Rect(4f, 5f, 32f, 48f);
        dirty.textureRect = dirty.sourceRect;

        var clean = BuildLayer("clean-layer", 202, new Vector2(100f, 200f));
        clean.spriteGuid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        clean.sourceRect = new Rect(50f, 60f, 70f, 80f);
        clean.textureRect = clean.sourceRect;

        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { dirty, clean });

        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerSpriteState"), "A rebuilt renderer should not start with dirty sprite state.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerPositionState"), "A rebuilt renderer should not start with dirty position state.");
        AssertTrue(renderer.SetLayerSpriteRect(dirty, new Rect(8f, 9f, 64f, 96f)), "Editing one sprite rect should mark that layer as changed.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "HasDirtyLayerSpriteState"), "Sprite rect edit should create dirty sprite state.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerPositionState"), "Sprite rect edit should not dirty spritePosition when the position stays stable.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsLayerSpriteStateDirty", dirty, dirty.spriteGuid), "Edited layer should be dirty by guid.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "IsLayerSpriteStateDirty", clean, clean.spriteGuid), "Unedited layer must not be written back by Apply.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "IsLayerPositionStateDirty", dirty, dirty.spriteGuid), "Edited rect must not make Apply rewrite spritePosition.");

        InvokePrivateMethod(renderer, "ClearDirtyLayerSpriteState");
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerSpriteState"), "Dirty sprite state should clear after successful Apply/Revert.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "HasDirtyLayerPositionState"), "Dirty position state should clear after successful Apply/Revert.");

        AssertTrue(renderer.ApplyPivotPreset(PsdLayerPreviewRenderer.PivotPreset.Center, Vector2.zero), "A real pivot move should change layer positions.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "HasDirtyLayerPositionState"), "Pivot move should create dirty position state.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsLayerPositionStateDirty", dirty, dirty.spriteGuid), "Moved layer position should be dirty by guid.");
    }

    private static void PositionApplyPreservesSpriteRectMetadata()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorPositionApplyTests";
        const string tempPath = tempFolder + "/20_position_apply.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorPositionApplyTests").Length > 0, "Temporary Reanimator position apply folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

        try
        {
            var dataProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            var originalHeadRect = FindSpriteRect(dataProvider, "head")?.rect ?? Rect.zero;
            AssertTrue(originalHeadRect.width > 0f && originalHeadRect.height > 0f, "Temporary PSD fixture should expose the head SpriteDataProvider rect.");
            AssertTrue(TryReadHeadSpriteImportVector(tempPath, "uvTransform", out var originalUvTransform), "Temporary PSD fixture should expose the head uvTransform.");

            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var head = FindLayer(renderer, "head");
            AssertTrue(head != null, "Temporary PSD fixture should contain the head layer.");

            var newPosition = head.position + new Vector2(7f, 11f);
            head.position = newPosition;
            head.sourceRect = new Rect(
                originalHeadRect.x + 101f,
                originalHeadRect.y + 103f,
                originalHeadRect.width + 5f,
                originalHeadRect.height + 7f);
            head.textureRect = head.sourceRect;
            InvokePrivateMethod(renderer, "MarkLayerPositionStateDirty", head);
            SetPrivateField(renderer, "hasPendingChanges", true);

            AssertTrue(renderer.Apply(tempPath, out var error), $"Position-only Apply should succeed. Error={error}");

            dataProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            var appliedHeadRect = FindSpriteRect(dataProvider, "head")?.rect ?? Rect.zero;
            AssertRect(originalHeadRect, appliedHeadRect, 0.0001f, "Position-only Apply must not write stale preview sourceRect to SpriteDataProvider.");
            AssertTrue(TryReadHeadSpriteImportVector(tempPath, "uvTransform", out var appliedUvTransform), "Temporary PSD fixture should expose the head uvTransform after Apply.");
            AssertVector(originalUvTransform, appliedUvTransform, 0.0001f, "Position-only Apply must not rewrite uvTransform from stale preview sourceRect.");

            if (TryReadHeadCharacterPartRect(tempPath, out var characterPartRect))
            {
                AssertEqual(Mathf.RoundToInt(newPosition.x), characterPartRect.x, "Position-only Apply should persist character part x.");
                AssertEqual(Mathf.RoundToInt(newPosition.y), characterPartRect.y, "Position-only Apply should persist character part y.");
                AssertEqual(Mathf.RoundToInt(originalHeadRect.width), characterPartRect.width, "Position-only Apply should preserve character part width.");
                AssertEqual(Mathf.RoundToInt(originalHeadRect.height), characterPartRect.height, "Position-only Apply should preserve character part height.");
            }
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ApplyPreservesUiVisibilityAfterReimport()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorApplyVisibilityTests";
        const string tempPath = tempFolder + "/20_apply_visibility.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorApplyVisibilityTests").Length > 0, "Temporary Reanimator visibility apply folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var head = FindLayer(renderer, "head");
            AssertTrue(head != null, "Temporary PSD fixture should contain the head layer.");

            var layers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
            var hierarchy = GetPrivateField<List<PsdLayerPreviewRenderer.LayerHierarchyNode>>(renderer, "layerHierarchyNodes");
            var headLayerIndex = layers.IndexOf(head);
            var headNode = hierarchy.FirstOrDefault(node => node != null && !node.isGroup && node.layerIndex == headLayerIndex);
            AssertTrue(headNode != null && !string.IsNullOrEmpty(headNode.key), "Temporary PSD fixture should expose the head hierarchy node.");

            AssertTrue(renderer.SetLayerHierarchyNodeVisible(headNode.key, false), "Fixture should hide the head layer before Apply.");
            AssertFalse(head.visible, "Head layer should be hidden before Apply.");

            var changedRect = new Rect(
                head.sourceRect.x,
                head.sourceRect.y + 1f,
                head.sourceRect.width,
                head.sourceRect.height);
            AssertTrue(renderer.SetLayerSpriteRect(head, changedRect), "Fixture should create an importer metadata change for Apply.");
            AssertTrue(renderer.Apply(tempPath, out var error), $"Apply should persist the temporary metadata change. Error={error}");

            var rebuiltHead = FindLayer(renderer, "head");
            AssertTrue(rebuiltHead != null, "Renderer should rebuild the head layer after Apply.");
            AssertFalse(rebuiltHead.visible, "Apply reimport must preserve hidden layer visibility in the live renderer.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void BoneCreateWaitsForTopApply()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorBoneApplyTests";
        const string tempPath = tempFolder + "/20_bone_apply.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorBoneApplyTests").Length > 0, "Temporary Reanimator bone apply folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var docSize = GetPrivateField<Vector2Int>(renderer, "cachedDocumentSize");
            if (docSize.x <= 0 || docSize.y <= 0)
                docSize = new Vector2Int(1024, 1024);

            var localRect = new Rect(0f, 0f, 512f, 512f);
            var coordinates = new PsdCoordinateSpace(localRect, docSize, renderer.Layers, null, PsdPreviewLayoutMode.Skeleton);
            var context = new ReanimatorPreviewContext(localRect, coordinates, renderer.SortedLayers, new PsdPreviewVisibilityOptions());
            var start = coordinates.DocumentToView(new Vector2(16f, 24f));
            var end = coordinates.DocumentToView(new Vector2(96f, 24f));
            AssertTrue(renderer.TryCreateBone(context, start, end, out var createdBoneIndex), "Creating a root bone should succeed.");
            AssertEqual(0, createdBoneIndex, "Temporary PSD fixture should create the first root bone.");
            AssertTrue(renderer.SetBoneDepth(createdBoneIndex, 17), "Temporary root bone order should be editable before Apply.");

            var reloaded = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(reloaded, "Rebuild", tempPath, false);
            AssertTrue(reloaded.Bones == null || reloaded.Bones.Count == 0, "Created bone must not persist or reimport before top Apply.");

            AssertTrue(renderer.Apply(tempPath, out var error), $"Top Apply should persist the created bone. Error={error}");

            reloaded = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(reloaded, "Rebuild", tempPath, false);
            AssertTrue(reloaded.Bones != null && reloaded.Bones.Count > 0, "Reloaded temporary PSD fixture should contain bones after top Apply.");
            AssertEqual("bone_0", reloaded.Bones[0].name, "Created bone should persist after top Apply.");
            AssertEqual(-1, reloaded.Bones[0].parentId, "Created root bone parent should persist after top Apply.");
            AssertEqual(17, reloaded.Bones[0].depth, "Created root bone order should round-trip through SpriteBone.position.z.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectHeadOptimizeApplyPersistsRect()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorSelfTests";
        const string tempPath = tempFolder + "/20_apply_roundtrip.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorSelfTests").Length > 0, "Temporary Reanimator self-test folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

        try
        {
            AssertTrue(TryReadHeadSpriteImportVector(tempPath, "uvTransform", out var stableUvTransform), "Temporary PSD fixture should expose the head uvTransform.");

            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var head = FindLayer(renderer, "head");
            AssertTrue(head != null, "Temporary PSD fixture should contain the head layer.");

            var stablePosition = head.position;
            var originalRect = head.sourceRect;
            var stableVisualAnchor = PsdCoordinateSpace.ResolveSpriteVisualAnchor(stablePosition, originalRect, stableUvTransform);
            var brokenRect = new Rect(
                head.sourceRect.x,
                head.sourceRect.y - 12f,
                head.sourceRect.width,
                head.sourceRect.height);
            var brokenPosition = stablePosition;
            AssertTrue(renderer.SetLayerSpriteRect(head, brokenRect), "Fixture setup should intentionally break the head rect.");
            AssertTrue(renderer.Apply(tempPath, out var error), $"Fixture setup Apply should persist the broken head rect. Error={error}");

            var setupState = DescribeHeadImporterState(tempPath);
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            head = FindLayer(renderer, "head");
            AssertTrue(head != null, "Temporary PSD fixture should still contain the head layer after setup Apply.");
            AssertRect(brokenRect, head.sourceRect, 0.0001f, $"Fixture setup should confirm Apply can persist the intentionally broken rect. {setupState}");
            AssertVector(brokenPosition, head.position, 0.0001f, $"Fixture setup rect Apply should keep spritePosition stable. {setupState}");
            var brokenImportVisualAnchor = PsdCoordinateSpace.ResolveSpriteVisualAnchor(brokenPosition, brokenRect, stableUvTransform);
            AssertHeadImportState(tempPath, stableUvTransform, brokenRect, brokenPosition, brokenImportVisualAnchor, "Fixture setup rect Apply");

            AssertTrue(renderer.SelectLayer(head), "Head layer should be selected for Optimize.");
            AssertTrue(renderer.OptimizeSpriteSheetRectsToGeometry() > 0, "Optimize should repair the intentionally broken head rect.");
            var optimizedRect = head.sourceRect;
            var optimizedPosition = head.position;
            AssertTrue(!ReanimatorAssetChangeAlgorithms.RectsRoughlyEqual(brokenRect, optimizedRect), "Optimize should produce a rect distinct from the broken setup rect.");

            AssertTrue(renderer.GenerateDefaultGeometryForSelected(tempPath, out error), $"Default Geometry should run after Optimize. Error={error}");
            AssertRect(optimizedRect, head.sourceRect, 0.0001f, "Default Geometry must not change the optimized rect before Apply.");
            AssertVector(optimizedPosition, head.position, 0.0001f, "Default Geometry must not change the optimized spritePosition before Apply.");
            AssertVector(stableVisualAnchor, PsdCoordinateSpace.ResolveSpriteVisualAnchor(optimizedPosition, optimizedRect, optimizedRect.position), 0.0001f, "Optimize should keep the same visual anchor before Apply.");

            AssertTrue(renderer.Apply(tempPath, out error), $"Apply should persist Optimize + Default Geometry. Error={error}");

            var dataProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            var dataProviderHead = FindSpriteRect(dataProvider, "head");
            AssertTrue(dataProviderHead != null, "SpriteDataProvider should still contain the head sprite after Apply.");
            AssertRect(optimizedRect, dataProviderHead.rect, 0.0001f, "SpriteDataProvider rect should keep the optimized rect after Apply.");
            var optimizedImportVisualAnchor = PsdCoordinateSpace.ResolveSpriteVisualAnchor(optimizedPosition, optimizedRect, stableUvTransform);
            AssertHeadImportState(tempPath, stableUvTransform, optimizedRect, optimizedPosition, optimizedImportVisualAnchor, "Optimize + Default Geometry Apply");

            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

            var reloaded = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(reloaded, "Rebuild", tempPath, false);
            var reloadedHead = FindLayer(reloaded, "head");
            AssertTrue(reloadedHead != null, "Rebuilt temporary PSD fixture should contain the head layer after Apply.");
            AssertRect(optimizedRect, reloadedHead.sourceRect, 0.0001f, "Rebuild after Apply must keep the optimized head rect.");
            AssertVector(optimizedPosition, reloadedHead.position, 0.0001f, "Rebuild after Apply must keep the optimized spritePosition stable.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectNewPsdLayerOptimizeApplyPersistsRect()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd";
        const string tempFolder = "Assets/__ReanimatorNewLayerTests";
        const string tempPath = tempFolder + "/1.45_body_nude_apply_roundtrip.psd";
        const string layerName = "cl_hoody_r_arm";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorNewLayerTests").Length > 0, "Temporary new-layer test folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the new-layer asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        try
        {
            var setupRenderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(setupRenderer, "Rebuild", tempPath, false);
            var setupLayer = FindLayer(setupRenderer, layerName);
            AssertTrue(setupLayer != null, $"Temporary PSD fixture setup should contain '{layerName}'.");
            AssertTrue(
                setupRenderer.SetLayerSpriteRect(setupLayer, new Rect(182f, 758f, 120f, 160f)),
                "Fixture setup should recreate the uniformly downscaled importer rect.");
            AssertTrue(
                setupRenderer.Apply(tempPath, out var setupError),
                $"Fixture setup should persist the broken importer rect. Error={setupError}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var layer = FindLayer(renderer, layerName);
            AssertTrue(layer != null, $"Temporary PSD fixture should contain '{layerName}'.");
            var beforeOptimize = layer.sourceRect;
            AssertRect(
                new Rect(365f, 1514f, 241f, 321f),
                beforeOptimize,
                0.0001f,
                "A uniformly downscaled new PSD layer should open at its full logical mosaic rect.");
            AssertRect(
                new Rect(182f, 758f, 120f, 160f),
                layer.textureRect,
                0.0001f,
                "The broken importer should sample the correct physical texture pixels before repair and reimport.");
            var shiftedRect = new Rect(beforeOptimize.x + 8f, beforeOptimize.y, beforeOptimize.width, beforeOptimize.height);
            AssertTrue(renderer.SetLayerSpriteRect(layer, shiftedRect), "Fixture setup should shift the new PSD layer before Optimize.");

            AssertTrue(renderer.SelectLayer(layer), "New PSD layer should be selected for Optimize.");
            AssertTrue(renderer.OptimizeSpriteSheetRectsToGeometry() > 0, "Optimize should repair the new PSD layer rect.");
            var optimizedRect = layer.sourceRect;
            AssertTrue(
                !ReanimatorAssetChangeAlgorithms.RectsRoughlyEqual(shiftedRect, optimizedRect),
                "Optimize should move the new PSD layer away from the intentionally shifted rect.");
            AssertRect(beforeOptimize, optimizedRect, 0.0001f, "Optimize should restore the new PSD layer to its original atlas pixels.");

            AssertTrue(renderer.Apply(tempPath, out var error), $"Apply should persist the optimized new PSD layer rect. Error={error}");

            var activeLayer = FindLayer(renderer, layerName);
            AssertTrue(activeLayer != null, "The active preview should rebuild the optimized new PSD layer after Apply.");
            AssertRect(optimizedRect, activeLayer.sourceRect, 0.0001f, "The active preview should keep the optimized source rect after Apply.");
            AssertTrue(activeLayer.textureRect.width > 100f && activeLayer.textureRect.height > 140f, "The active preview should not return to the quarter-scale runtime Sprite rect after Apply.");
            AssertTrue(renderer.IsLayerSelected(activeLayer), "The active preview should keep the rebuilt new PSD layer selected after Apply.");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var dataProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            var persisted = FindSpriteRect(dataProvider, layerName);
            AssertTrue(persisted != null, "SpriteDataProvider should still contain the optimized new PSD layer after reimport.");
            AssertRect(optimizedRect, persisted.rect, 0.0001f, "Apply and reimport should preserve the optimized new PSD layer rect.");

            var reopened = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(reopened, "Rebuild", tempPath, false);
            var reopenedLayer = FindLayer(reopened, layerName);
            AssertTrue(reopenedLayer != null, "Reopened preview should contain the optimized new PSD layer.");
            AssertRect(optimizedRect, reopenedLayer.sourceRect, 0.0001f, "Reopened preview should keep the optimized new PSD layer rect.");
            AssertTrue(reopenedLayer.textureRect.width > 100f && reopenedLayer.textureRect.height > 140f, "Reopened preview should sample the repaired half-scale texture rect, not the stale quarter-scale Sprite rect.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectPsdLayerRepairScalesGeometryWithRect()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd";
        const string tempFolder = "Assets/__ReanimatorRectGeometryRepairTests";
        const string tempPath = tempFolder + "/1.45_body_nude_rect_geometry_repair.psd";
        const string layerName = "cl_hoody_r_arm";
        var staleRect = new Rect(182f, 758f, 120f, 160f);
        var repairedRect = new Rect(365f, 1514f, 241f, 321f);
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorRectGeometryRepairTests").Length > 0, "Temporary rect geometry repair folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy for rect geometry repair.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        try
        {
            var setupRenderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(setupRenderer, "Rebuild", tempPath, false);
            var setupLayer = FindLayer(setupRenderer, layerName);
            AssertTrue(setupLayer != null, $"Rect geometry repair fixture should contain '{layerName}'.");
            AssertTrue(
                setupRenderer.SetLayerSpriteRect(setupLayer, staleRect),
                "Rect geometry repair fixture should recreate the stale sprite rect through the normal edit path.");
            AssertTrue(
                setupRenderer.Apply(tempPath, out var setupError),
                $"Rect geometry repair fixture should persist the stale sprite rect. Error={setupError}");

            var setupProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            AssertTrue(setupProvider != null, "Rect geometry repair fixture should expose a SpriteEditor data provider.");
            setupProvider.InitSpriteEditorDataProvider();
            var setupRect = FindSpriteRect(setupProvider, layerName);
            AssertTrue(setupRect != null, $"Rect geometry repair fixture should contain '{layerName}'.");
            AssertRect(staleRect, setupRect.rect, 0.0001f, "Rect geometry repair fixture should retain the stale sprite rect before repair.");

            var setupMesh = setupProvider.GetDataProvider<ISpriteMeshDataProvider>();
            AssertTrue(setupMesh != null, "Rect geometry repair fixture should expose mesh data.");
            setupMesh.SetVertices(
                setupRect.spriteID,
                new[]
                {
                    new Vertex2DMetaData { position = new Vector2(0f, 0f) },
                    new Vertex2DMetaData { position = new Vector2(120f, 0f) },
                    new Vertex2DMetaData { position = new Vector2(120f, 160f) },
                    new Vertex2DMetaData { position = new Vector2(0f, 160f) }
                });
            setupMesh.SetIndices(setupRect.spriteID, new[] { 0, 1, 2, 0, 2, 3 });
            setupProvider.Apply();

            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            AssertTrue(
                renderer.RepairSpriteSheetImporterParameters(tempPath, out var repairedGroups, out var error),
                $"Repair PSD Params should scale geometry with the repaired rect. Error={error}");
            AssertEqual(1, repairedGroups, "Rect geometry repair should persist exactly one repaired importer group.");

            var repairedProvider = AssetImporter.GetAtPath(tempPath) as ISpriteEditorDataProvider;
            AssertTrue(repairedProvider != null, "Repaired fixture should retain its SpriteEditor data provider.");
            repairedProvider.InitSpriteEditorDataProvider();
            var repairedSpriteRect = FindSpriteRect(repairedProvider, layerName);
            AssertTrue(repairedSpriteRect != null, $"Repaired fixture should retain '{layerName}'.");
            AssertRect(repairedRect, repairedSpriteRect.rect, 0.0001f, "Repair should restore the PSD layer rect and mosaic position.");

            var repairedMesh = repairedProvider.GetDataProvider<ISpriteMeshDataProvider>();
            var repairedVertices = repairedMesh.GetVertices(repairedSpriteRect.spriteID);
            AssertEqual(4, repairedVertices.Length, "Repair should preserve the fixture topology.");
            AssertVector(new Vector2(0f, 0f), repairedVertices[0].position, 0.0001f, "Repair should preserve the local origin.");
            AssertVector(new Vector2(241f, 321f), repairedVertices[2].position, 0.0001f, "Repair should scale vertices into the repaired PSD rect.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectOptimizeAllApplyPersistsEverySpriteRect()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd";
        const string tempFolder = "Assets/__ReanimatorOptimizeAllTests";
        const string tempPath = tempFolder + "/1.45_body_nude_optimize_all.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorOptimizeAllTests").Length > 0, "Temporary Optimize All test folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy for Optimize All.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            renderer.SelectLayer(null);
            AssertTrue(renderer.OptimizeSpriteSheetRectsToGeometry() > 1, "Optimize without a selection should process the real PSD sprite sheet.");

            var optimizedLayers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
            var expectedRects = optimizedLayers
                .Where(layer => layer != null && !string.IsNullOrEmpty(layer.spriteGuid))
                .ToDictionary(layer => layer.spriteGuid, layer => layer.sourceRect, StringComparer.OrdinalIgnoreCase);
            AssertTrue(expectedRects.Count > 1, "Optimize All fixture should track every sprite rect by GUID.");

            AssertTrue(renderer.Apply(tempPath, out var error), $"Apply should persist every Optimize All rect with one save. Error={error}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var reopened = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(reopened, "Rebuild", tempPath, false);
            var reopenedLayers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(reopened, "layerInfos");
            AssertEqual(expectedRects.Count, reopenedLayers.Count(layer => layer != null && expectedRects.ContainsKey(layer.spriteGuid)), "Reopened preview should retain every optimized sprite GUID.");
            for (var i = 0; i < reopenedLayers.Count; i++)
            {
                var layer = reopenedLayers[i];
                if (layer == null || !expectedRects.TryGetValue(layer.spriteGuid, out var expectedRect))
                    continue;
                AssertRect(expectedRect, layer.sourceRect, 0.0001f, $"Optimize All Apply should preserve '{layer.name}' after reimport.");
            }
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectSelectedPsdLayerOptimizeIsIdempotent()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd";
        const string tempFolder = "Assets/__ReanimatorOptimizeIdempotencyTests";
        const string tempPath = tempFolder + "/1.45_body_nude_optimize_idempotency.psd";
        const string layerName = "l_hand_touch";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorOptimizeIdempotencyTests").Length > 0, "Temporary Optimize idempotency folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy for Optimize idempotency.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var layer = FindLayer(renderer, layerName);
            AssertTrue(layer != null, $"Optimize idempotency fixture should contain '{layerName}'.");
            AssertTrue(renderer.SelectLayer(layer), $"Optimize idempotency fixture should select '{layerName}'.");

            var originalRect = layer.sourceRect;
            renderer.OptimizeSpriteSheetRectsToGeometry();
            var firstRect = layer.sourceRect;
            AssertTrue(
                Mathf.Abs(firstRect.center.x - originalRect.center.x) < 10f &&
                Mathf.Abs(firstRect.center.y - originalRect.center.y) < 10f &&
                firstRect.height > originalRect.height * 0.8f,
                "First Optimize should fit the complete l_hand_touch geometry without jumping to or clipping another sprite.");
            renderer.OptimizeSpriteSheetRectsToGeometry();

            AssertRect(firstRect, layer.sourceRect, 0.0001f, "Running Optimize twice should leave the selected PSD layer at the first fitted rect.");
            AssertTrue(renderer.IsLayerSelected(layer), "Running Optimize twice should keep the same PSD layer selected.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ProjectHoodieArmDefaultGeometryCoversSprite()
    {
        const string assetPath = "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd";
        const string layerName = "cl_hoody_r_arm";
        if (AssetImporter.GetAtPath(assetPath) == null)
            return;

        var renderer = new PsdLayerPreviewRenderer();
        InvokePrivateMethod(renderer, "Rebuild", assetPath, false);
        var layer = FindLayer(renderer, layerName);
        AssertTrue(layer != null, $"Default Geometry fixture should contain '{layerName}'.");
        AssertTrue(renderer.SelectLayer(layer), $"Default Geometry fixture should select '{layerName}'.");

        var originalRect = layer.sourceRect;
        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        AssertTrue(
            renderer.GenerateDefaultGeometryForSelected(assetPath, out var error),
            $"Default Geometry should generate for '{layerName}'. Error={error}");
        AssertRect(originalRect, layer.sourceRect, 0.0001f, "Default Geometry should not move or resize the selected sprite rect.");

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        var data = dataBySelectionKey[selectionKey];
        AssertTrue(data != null, "Default Geometry should keep generated layer data under the selected sprite key.");
        var vertices = GetPrivateField<Vector2[]>(data, "vertices");
        var triangles = GetPrivateField<int[]>(data, "triangles");
        AssertTrue(vertices != null && vertices.Length >= 3, "Default Geometry should generate a usable vertex set.");
        AssertTrue(triangles != null && triangles.Length >= 3, "Default Geometry should generate usable triangles.");

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < vertices.Length; i++)
        {
            var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, vertices[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
            AssertTrue(
                local.x >= -0.5f && local.y >= -0.5f &&
                local.x <= originalRect.width + 0.5f && local.y <= originalRect.height + 0.5f,
                "Default Geometry vertices should stay inside the selected sprite rect.");
        }

        var generatedSize = max - min;
        AssertTrue(
            generatedSize.x >= originalRect.width * 0.6f && generatedSize.y >= originalRect.height * 0.6f,
            $"Default Geometry should cover the hoodie arm instead of producing disconnected fragments. Generated={generatedSize} Rect={originalRect.size}");
    }

    private static void ProjectDanceGirlAllInGeometryAvoidsGiantTriangles()
    {
        const string assetPath = "Assets/Projects/Proj15/Dance_girl.psd";
        if (AssetImporter.GetAtPath(assetPath) == null)
            return;

        var renderer = new PsdLayerPreviewRenderer();
        InvokePrivateMethod(renderer, "Rebuild", assetPath, false);
        var layerNames = new[] { "topwear", "front_hair", "l_arm", "skirt_front", "skirt_back" };
        for (var layerIndex = 0; layerIndex < layerNames.Length; layerIndex++)
        {
            var layerName = layerNames[layerIndex];
            var layer = FindLayer(renderer, layerName);
            AssertTrue(layer != null, $"Dance girl fixture should contain '{layerName}'.");
            AssertTrue(renderer.SelectLayer(layer), $"Dance girl fixture should select '{layerName}'.");
            var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
            AssertTrue(
                renderer.GenerateDefaultGeometryForSelected(assetPath, out var error),
                $"ALLIN default geometry should generate for '{layerName}'. Error={error}");

            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            var data = dataBySelectionKey[selectionKey];
            AssertTrue(data != null, $"ALLIN default geometry should retain generated data for '{layerName}'.");
            var vertices = GetPrivateField<Vector2[]>(data, "vertices");
            var triangles = GetPrivateField<int[]>(data, "triangles");
            AssertTrue(vertices != null && vertices.Length >= 3, $"'{layerName}' should have deformation vertices.");
            AssertTrue(triangles != null && triangles.Length >= 3, $"'{layerName}' should have deformation triangles.");

            var maxTriangleArea = 0f;
            var maxEdgeLength = 0f;
            var triangleIncidence = new int[vertices.Length];
            var localVertices = new Vector2[vertices.Length];
            for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                localVertices[vertexIndex] = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, vertices[vertexIndex]);
            for (var triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
            {
                var a = localVertices[triangles[triangleIndex]];
                var b = localVertices[triangles[triangleIndex + 1]];
                var c = localVertices[triangles[triangleIndex + 2]];
                var area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
                maxTriangleArea = Mathf.Max(maxTriangleArea, area);
                maxEdgeLength = Mathf.Max(
                    maxEdgeLength,
                    Mathf.Max(Vector2.Distance(a, b), Mathf.Max(Vector2.Distance(b, c), Vector2.Distance(c, a))));
                triangleIncidence[triangles[triangleIndex]]++;
                triangleIncidence[triangles[triangleIndex + 1]]++;
                triangleIncidence[triangles[triangleIndex + 2]]++;
            }

            var rectArea = Mathf.Max(1f, layer.sourceRect.width * layer.sourceRect.height);
            var rectDiagonal = Mathf.Max(1f, layer.sourceRect.size.magnitude);
            AssertTrue(
                maxTriangleArea <= rectArea * 0.08f,
                $"'{layerName}' contains a giant ALLIN triangle. MaxArea={maxTriangleArea} RectArea={rectArea} Vertices={vertices.Length} Triangles={triangles.Length / 3}");
            AssertTrue(
                maxEdgeLength <= rectDiagonal * 0.15f,
                $"'{layerName}' retains long fan diagonals. MaxEdge={maxEdgeLength} RectDiagonal={rectDiagonal} Vertices={vertices.Length} Triangles={triangles.Length / 3}");
            var maxTriangleIncidence = 0;
            for (var vertexIndex = 0; vertexIndex < triangleIncidence.Length; vertexIndex++)
                maxTriangleIncidence = Mathf.Max(maxTriangleIncidence, triangleIncidence[vertexIndex]);
            AssertTrue(
                maxTriangleIncidence <= 10,
                $"'{layerName}' contains a starburst vertex shared by {maxTriangleIncidence} triangles.");
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "LocalTrianglesFollowOpaquePixels", layer, layer.texture, localVertices, triangles),
                $"'{layerName}' geometry should not bridge disconnected alpha islands or transparent gaps.");

            var firstVertexCount = vertices.Length;
            var firstTriangleIndexCount = triangles.Length;
            AssertTrue(
                renderer.GenerateDefaultGeometryForSelected(assetPath, out error),
                $"Repeated ALLIN default geometry should repair '{layerName}' without failing. Error={error}");
            vertices = GetPrivateField<Vector2[]>(data, "vertices");
            triangles = GetPrivateField<int[]>(data, "triangles");
            AssertEqual(firstVertexCount, vertices.Length, $"Repeated ALLIN should keep '{layerName}' vertex density stable.");
            AssertEqual(firstTriangleIndexCount, triangles.Length, $"Repeated ALLIN should keep '{layerName}' triangle density stable.");

            if (string.Equals(layerName, "skirt_front", StringComparison.Ordinal))
            {
                var snapshotArgs = new object[] { layer, null };
                AssertTrue(
                    (bool)InvokePrivateMethod(renderer, "TryGetDefaultGeometrySnapshot", snapshotArgs),
                    "Dance girl skirt should retain its imported default-geometry snapshot for fallback coverage.");
                var fallbackArgs = new object[]
                {
                    assetPath,
                    layer,
                    selectionKey,
                    (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId),
                    data,
                    true,
                    null
                };
                AssertTrue(
                    (bool)InvokePrivateMethod(renderer, "GenerateDefaultGeometryFromSnapshotFallback", fallbackArgs),
                    "ALLIN snapshot fallback should rebuild the imported skirt mesh instead of restoring its fan topology. Error=" + fallbackArgs[6]);
                vertices = GetPrivateField<Vector2[]>(data, "vertices");
                triangles = GetPrivateField<int[]>(data, "triangles");
                var fallbackLocalVertices = new Vector2[vertices.Length];
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    fallbackLocalVertices[vertexIndex] = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, vertices[vertexIndex]);
                AssertTrue(
                    (bool)InvokePrivateMethod(
                        renderer,
                        "LocalTrianglesFollowOpaquePixels",
                        layer,
                        layer.texture,
                        fallbackLocalVertices,
                        triangles),
                    "ALLIN skirt snapshot fallback must follow the sprite alpha instead of filling its rectangular bounds.");
                var fallbackMaxEdge = 0f;
                var fallbackIncidence = new int[vertices.Length];
                for (var triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                {
                    var a = triangles[triangleIndex];
                    var b = triangles[triangleIndex + 1];
                    var c = triangles[triangleIndex + 2];
                    fallbackIncidence[a]++;
                    fallbackIncidence[b]++;
                    fallbackIncidence[c]++;
                    fallbackMaxEdge = Mathf.Max(
                        fallbackMaxEdge,
                        Mathf.Max(
                            Vector2.Distance(fallbackLocalVertices[a], fallbackLocalVertices[b]),
                            Mathf.Max(
                                Vector2.Distance(fallbackLocalVertices[b], fallbackLocalVertices[c]),
                                Vector2.Distance(fallbackLocalVertices[c], fallbackLocalVertices[a]))));
                }
                var fallbackMaxIncidence = 0;
                for (var vertexIndex = 0; vertexIndex < fallbackIncidence.Length; vertexIndex++)
                    fallbackMaxIncidence = Mathf.Max(fallbackMaxIncidence, fallbackIncidence[vertexIndex]);
                AssertTrue(
                    fallbackMaxEdge <= layer.sourceRect.size.magnitude * 0.15f,
                    "ALLIN skirt snapshot fallback should remove long imported fan diagonals. MaxEdge=" + fallbackMaxEdge +
                    " Diagonal=" + layer.sourceRect.size.magnitude + " Vertices=" + vertices.Length + " Triangles=" + (triangles.Length / 3));
                AssertTrue(
                    fallbackMaxIncidence <= 10,
                    "ALLIN skirt snapshot fallback should remove imported starburst vertices. MaxIncidence=" + fallbackMaxIncidence);
            }
        }
    }

    public static void RunDanceGirlAllInGeometryRegression()
    {
        GeneratedGeometrySubdivisionKeepsValidTriangles();
        ProjectDanceGirlAllInGeometryAvoidsGiantTriangles();
        Debug.Log("Dance girl ALLIN geometry regression passed.");
    }

    private static void ProjectHeadApplyDoesNotLeaveMetaTimestampMismatch()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorApplyTimestampTests";
        const string tempPath = tempFolder + "/20_apply_timestamp.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorApplyTimestampTests").Length > 0, "Temporary Reanimator apply timestamp folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var loggedVersionErrors = new List<string>();
        void CaptureBuildVersionError(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) &&
                condition.IndexOf("Build asset version error", StringComparison.OrdinalIgnoreCase) >= 0 &&
                condition.IndexOf("__ReanimatorApplyTimestampTests", StringComparison.OrdinalIgnoreCase) >= 0)
                loggedVersionErrors.Add(condition);
        }

        Application.logMessageReceived += CaptureBuildVersionError;
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            var head = FindLayer(renderer, "head");
            AssertTrue(head != null, "Temporary PSD fixture should contain the head layer.");

            var changedRect = new Rect(head.sourceRect.x, head.sourceRect.y + 1f, head.sourceRect.width, head.sourceRect.height);
            AssertTrue(renderer.SetLayerSpriteRect(head, changedRect), "Timestamp regression fixture should create an importer metadata change.");
            AssertTrue(renderer.Apply(tempPath, out var error), $"Apply should persist the temporary metadata change. Error={error}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AssertEqual(0, loggedVersionErrors.Count, "Apply should not leave SourceAssetDB and meta timestamps mismatched.");
        }
        finally
        {
            Application.logMessageReceived -= CaptureBuildVersionError;
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ImporterMetadataSaveAndReimportDoesNotLeaveTimestampMismatch()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/20.psd";
        const string tempFolder = "Assets/__ReanimatorImporterSaveTimestampTests";
        const string tempPath = tempFolder + "/20_importer_save_timestamp.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorImporterSaveTimestampTests").Length > 0, "Temporary importer save timestamp folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the project head asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var loggedVersionErrors = new List<string>();
        void CaptureBuildVersionError(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) &&
                condition.IndexOf("Build asset version error", StringComparison.OrdinalIgnoreCase) >= 0 &&
                condition.IndexOf("__ReanimatorImporterSaveTimestampTests", StringComparison.OrdinalIgnoreCase) >= 0)
                loggedVersionErrors.Add(condition);
        }

        Application.logMessageReceived += CaptureBuildVersionError;
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var importer = AssetImporter.GetAtPath(tempPath) as PSDImporter;
            AssertTrue(importer != null, "Temporary importer save fixture should be a PSD importer.");

            var so = new SerializedObject(importer);
            AssertTrue((bool)InvokePrivateMethod(renderer, "RequestPsdLayerReslice", so), "Fixture should dirty importer metadata without using SpriteDataProvider.");
            InvokePrivateMethod(renderer, "ApplyImporterSerializedProperties", so, importer);
            InvokePrivateMethod(renderer, "PersistImporterMetadataAndReimport", tempPath);
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AssertEqual(0, loggedVersionErrors.Count, "Importer metadata save and reimport helper should not leave SourceAssetDB and meta timestamps mismatched.");
        }
        finally
        {
            Application.logMessageReceived -= CaptureBuildVersionError;
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void SofaTdRepairGenerateOptimizeDefaultDoesNotLeaveMetaTimestampMismatch()
    {
        const string sourcePath = "Assets/0.Internal/Game/Textures/Characters/grl/15.sofaTD.psd";
        const string tempFolder = "Assets/__ReanimatorSofaTdTimestampTests";
        const string tempPath = tempFolder + "/15_sofaTD_sequence.psd";
        if (AssetImporter.GetAtPath(sourcePath) == null)
            return;

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorSofaTdTimestampTests").Length > 0, "Temporary SofaTD timestamp folder should be created.");
        AssertTrue(AssetDatabase.CopyAsset(sourcePath, tempPath), "Temporary PSD fixture should copy from the SofaTD asset.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var loggedVersionErrors = new List<string>();
        void CaptureBuildVersionError(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) &&
                condition.IndexOf("Build asset version error", StringComparison.OrdinalIgnoreCase) >= 0 &&
                condition.IndexOf("__ReanimatorSofaTdTimestampTests", StringComparison.OrdinalIgnoreCase) >= 0)
                loggedVersionErrors.Add(condition);
        }

        Application.logMessageReceived += CaptureBuildVersionError;
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);

            AssertTrue(renderer.RepairSpriteSheetImporterParameters(tempPath, out _, out var error), $"Repair PSD Params should complete. Error={error}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            AssertTrue(renderer.GenerateMissingGeometry(tempPath, out _, out error), $"Generate Missing Geometry should complete. Error={error}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
            renderer.OptimizeSpriteSheetRectsToGeometry();
            var generatedDefault = renderer.GenerateDefaultGeometryForSpriteSheet(tempPath, out error);
            AssertTrue(generatedDefault > 0 || string.IsNullOrEmpty(error), $"Default Geometry should complete after Optimize. Error={error}");
            AssertTrue(renderer.Apply(tempPath, out error), $"Apply should persist Optimize + Default Geometry. Error={error}");
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AssertEqual(0, loggedVersionErrors.Count, "Repair/Generate/Optimize/Default/Apply should not leave SourceAssetDB and meta timestamps mismatched.");
        }
        finally
        {
            Application.logMessageReceived -= CaptureBuildVersionError;
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private readonly struct ReleaseFixtureCase
    {
        public readonly string Name;
        public readonly string SourcePath;
        public readonly string TempFileName;
        public readonly string EditLayerName;
        public readonly string[] RequiredLayerNames;
        public readonly int MinimumSpriteLayerCount;

        public ReleaseFixtureCase(
            string name,
            string sourcePath,
            string tempFileName,
            string editLayerName,
            int minimumSpriteLayerCount,
            params string[] requiredLayerNames)
        {
            Name = name;
            SourcePath = sourcePath;
            TempFileName = tempFileName;
            EditLayerName = editLayerName;
            MinimumSpriteLayerCount = minimumSpriteLayerCount;
            RequiredLayerNames = requiredLayerNames ?? Array.Empty<string>();
        }
    }

    private readonly struct ReleaseToolMatrixCase
    {
        public readonly string Tool;
        public readonly string[] RequiredCaseNameFragments;

        public ReleaseToolMatrixCase(string tool, params string[] requiredCaseNameFragments)
        {
            Tool = tool;
            RequiredCaseNameFragments = requiredCaseNameFragments ?? Array.Empty<string>();
        }
    }

    private static void ReleasePsdFixturesRoundTripThroughOpenEditUndoApplyReimport()
    {
        const string tempFolder = "Assets/__ReanimatorReleaseMatrixTests";
        var fixtures = new[]
        {
            new ReleaseFixtureCase(
                "simple body",
                "Assets/0.Internal/Game/Textures/Characters/grl/20.psd",
                "20_release_body.psd",
                "head",
                10,
                "body",
                "head",
                "l_arm",
                "r_arm"),
            new ReleaseFixtureCase(
                "complex hierarchy",
                "Assets/0.Internal/Game/Textures/Characters/grl/15.sofaTD.psd",
                "15_sofaTD_release_complex.psd",
                "body",
                10,
                "body",
                "back hair",
                "front hair",
                "l_arm",
                "r_arm"),
            new ReleaseFixtureCase(
                "clothing over body",
                "Assets/0.Internal/Game/Textures/Characters/grl/1.45_clothness.psd",
                "1_45_clothness_release.psd",
                "cl_towel",
                12,
                "cl_towel",
                "cl_t-shirt",
                "cl_bra_pink",
                "cl_tights"),
            new ReleaseFixtureCase(
                "alternate nude pose with expressions",
                "Assets/0.Internal/Game/Textures/Characters/grl/1.45_body_nude.psd",
                "1_45_body_nude_release.psd",
                "head",
                18,
                "head",
                "l_boob",
                "r_boob",
                "l_leg",
                "r_leg",
                "emo_happy"),
            new ReleaseFixtureCase(
                "side view duplicate names",
                "Assets/0.Internal/Game/Textures/Characters/grl/12.side.psd",
                "12_side_release.psd",
                "handwear",
                14,
                "front hair",
                "head",
                "back hair",
                "handwear",
                "r_leg"),
            new ReleaseFixtureCase(
                "compact locker pose",
                "Assets/0.Internal/Game/Textures/Characters/grl/17.locker.psd",
                "17_locker_release.psd",
                "body",
                10,
                "head",
                "body",
                "l_leg",
                "r_leg",
                "l_hand",
                "r_hand")
        };

        if (AssetDatabase.IsValidFolder(tempFolder))
            AssetDatabase.DeleteAsset(tempFolder);
        AssertTrue(AssetDatabase.CreateFolder("Assets", "__ReanimatorReleaseMatrixTests").Length > 0, "Temporary release matrix folder should be created.");

        try
        {
            for (var i = 0; i < fixtures.Length; i++)
                RunReleaseFixtureRoundTrip(fixtures[i], tempFolder);
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void ReleaseToolMatrixCoversEveryProductionToolScenario()
    {
        var caseNames = new HashSet<string>(BuildCases().Select(test => test.Name), StringComparer.OrdinalIgnoreCase);
        var matrix = new[]
        {
            new ReleaseToolMatrixCase("drag", "drag undo registers once", "drag movement uses one undo group"),
            new ReleaseToolMatrixCase("cancel", "Renderer cancel clears weight tool state", "Renderer cancel clears geometry radius and joint state", "Renderer cancel clears curve boob and volume state"),
            new ReleaseToolMatrixCase("apply", "Apply preserves", "Apply treats zero weights as no writable skinning"),
            new ReleaseToolMatrixCase("repair", "Sprite sheet importer repair requests PSD reslice", "repair generate optimize default"),
            new ReleaseToolMatrixCase("optimize", "Project head optimize apply persists rect", "Sprite sheet geometry optimize fits rect to vertices"),
            new ReleaseToolMatrixCase("slider", "Weight slider preserves one to four influence identities", "Weight slider smooths weights from topology neighbors"),
            new ReleaseToolMatrixCase("brush", "Weight brush extracts active and accumulated weights"),
            new ReleaseToolMatrixCase("radius/joint", "RadiusCull masks contour and inner vertices separately", "Joint geometry resolves directed angle arcs", "Renderer cancel clears geometry radius and joint state"),
            new ReleaseToolMatrixCase("projector", "Projector explicit target filters intersecting clothing sources", "Projector apply resolves target binding by sprite guid"),
            new ReleaseToolMatrixCase("volume", "VolumeCurve apply dirties selected layer only", "VolumeCurve radius falloff works on dense and sparse meshes"),
            new ReleaseToolMatrixCase("bone edit", "Bone create waits for top Apply", "Renderer bone edit setters update transforms with undo")
        };

        for (var i = 0; i < matrix.Length; i++)
        {
            var entry = matrix[i];
            for (var j = 0; j < entry.RequiredCaseNameFragments.Length; j++)
            {
                var fragment = entry.RequiredCaseNameFragments[j];
                AssertTrue(
                    caseNames.Any(name => name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0),
                    $"Release tool matrix '{entry.Tool}' should be covered by a self-test containing '{fragment}'.");
            }
        }
    }

    private static void RunReleaseFixtureRoundTrip(ReleaseFixtureCase fixture, string tempFolder)
    {
        AssertTrue(AssetImporter.GetAtPath(fixture.SourcePath) != null, $"Release fixture source should exist: {fixture.SourcePath}");
        var tempPath = $"{tempFolder}/{fixture.TempFileName}";
        AssertTrue(AssetDatabase.CopyAsset(fixture.SourcePath, tempPath), $"Release fixture '{fixture.Name}' should copy to a temporary PSD.");
        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        var rebuildStart = EditorApplication.timeSinceStartup;
        InvokePrivateMethod(renderer, "Rebuild", tempPath, false);
        AssertTrue(EditorApplication.timeSinceStartup - rebuildStart < 20d, $"{fixture.Name} initial rebuild should stay within the release performance smoke budget.");
        AssertReleaseFixtureOpened(renderer, fixture, $"{fixture.Name} open");

        var editLayer = FindLayer(renderer, fixture.EditLayerName);
        AssertTrue(editLayer != null, $"Release fixture '{fixture.Name}' should contain edit layer '{fixture.EditLayerName}'.");
        var originalRect = ReadSpriteRectFromProvider(tempPath, fixture.EditLayerName);
        AssertRect(originalRect, editLayer.sourceRect, 0.0001f, $"{fixture.Name} preview should open with the serialized importer rect.");

        var editedRect = new Rect(originalRect.x, originalRect.y + 1f, originalRect.width, originalRect.height);
        AssertTrue(renderer.SetLayerSpriteRect(editLayer, editedRect), $"{fixture.Name} edit should dirty the selected sprite rect.");
        AssertArray(new[] { "Edit Sprite Rect" }, undoLabels, $"{fixture.Name} edit should emit one understandable Undo step.");
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "SetLayerSpriteRect", editLayer, originalRect, false),
            $"{fixture.Name} simulated Undo should restore the original sprite rect without adding another Undo step.");
        AssertRect(originalRect, editLayer.sourceRect, 0.0001f, $"{fixture.Name} simulated Undo should restore preview source rect.");
        AssertRect(originalRect, editLayer.textureRect, 0.0001f, $"{fixture.Name} simulated Undo should restore preview texture rect.");
        AssertArray(new[] { "Edit Sprite Rect" }, undoLabels, $"{fixture.Name} simulated Undo should keep one understandable Undo step.");
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "SetLayerSpriteRect", editLayer, editedRect, false),
            $"{fixture.Name} simulated Redo should restore the edited sprite rect without adding another Undo step.");
        AssertRect(editedRect, editLayer.sourceRect, 0.0001f, $"{fixture.Name} simulated Redo should restore preview source rect.");
        AssertRect(editedRect, editLayer.textureRect, 0.0001f, $"{fixture.Name} simulated Redo should restore preview texture rect.");
        AssertArray(new[] { "Edit Sprite Rect" }, undoLabels, $"{fixture.Name} simulated Redo should keep one understandable Undo step.");
        AssertTrue(renderer.Apply(tempPath, out var error), $"{fixture.Name} Apply should persist the edited sprite rect. Error={error}");

        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var serializedRect = ReadSpriteRectFromProvider(tempPath, fixture.EditLayerName);
        AssertRect(editedRect, serializedRect, 0.0001f, $"{fixture.Name} serialized importer rect should survive Apply and forced reimport.");

        var reopened = new PsdLayerPreviewRenderer();
        rebuildStart = EditorApplication.timeSinceStartup;
        InvokePrivateMethod(reopened, "Rebuild", tempPath, false);
        AssertTrue(EditorApplication.timeSinceStartup - rebuildStart < 20d, $"{fixture.Name} reopen rebuild should stay within the release performance smoke budget.");
        AssertReleaseFixtureOpened(reopened, fixture, $"{fixture.Name} reopen");
        var reopenedLayer = FindLayer(reopened, fixture.EditLayerName);
        AssertTrue(reopenedLayer != null, $"{fixture.Name} reopened preview should contain the edited layer.");
        AssertRect(serializedRect, reopenedLayer.sourceRect, 0.0001f, $"{fixture.Name} reopened preview should match serialized importer state.");
        AssertRect(reopenedLayer.sourceRect, reopenedLayer.textureRect, 0.0001f, $"{fixture.Name} reopened preview texture rect should match source rect.");
    }

    private static void AssertReleaseFixtureOpened(PsdLayerPreviewRenderer renderer, ReleaseFixtureCase fixture, string label)
    {
        var layers = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
        AssertTrue(layers != null && layers.Count >= fixture.MinimumSpriteLayerCount, $"{label} should expose enough sprite layers.");

        for (var i = 0; i < fixture.RequiredLayerNames.Length; i++)
            AssertTrue(FindLayer(renderer, fixture.RequiredLayerNames[i]) != null, $"{label} should expose required layer '{fixture.RequiredLayerNames[i]}'.");

        var hierarchy = GetPrivateField<List<PsdLayerPreviewRenderer.LayerHierarchyNode>>(renderer, "layerHierarchyNodes");
        AssertTrue(hierarchy != null && hierarchy.Count >= layers.Count, $"{label} should expose a layer hierarchy for visibility/order comparison.");
    }

    private static Rect ReadSpriteRectFromProvider(string assetPath, string spriteName)
    {
        var dataProvider = AssetImporter.GetAtPath(assetPath) as ISpriteEditorDataProvider;
        AssertTrue(dataProvider != null, $"{assetPath} should expose a SpriteEditor data provider.");
        var spriteRect = FindSpriteRect(dataProvider, spriteName);
        AssertTrue(spriteRect != null, $"{assetPath} should expose SpriteRect '{spriteName}'.");
        AssertTrue(spriteRect.rect.width > 0f && spriteRect.rect.height > 0f, $"{assetPath}:{spriteName} should have a positive serialized rect.");
        return spriteRect.rect;
    }

}
#endif
