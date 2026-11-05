#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void UtilityKillWeightsClearsSelectedSpriteWeights()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("weighted_sprite", 2301, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 64f, 64f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "cachedPath", "Assets/test_kill_weights.psd");
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        AssertFalse(renderer.ClearSelectedSpriteWeights(out _, out _, out var missingSelectionError), "Kill Weights should require a selected sprite.");
        AssertEqual("Select sprite first", missingSelectionError, "Missing selection error.");

        AssertTrue(renderer.SelectLayer(layer), "Fixture layer should become selected.");
        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(8f, 0f),
            new Vector2(0f, 8f)
        });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 0.75f, boneIndex1 = 0, weight1 = 0.25f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });
        SetPrivateField(data, "hasSkinning", true);
        SetPrivateField(data, "lastPoseVersion", 12);
        SetPrivateField(data, "deformedVertices", new[] { Vector2.one });
        SetPrivateField(data, "skinMatrices", new[] { Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        AssertTrue(renderer.ClearSelectedSpriteWeights(out var layerName, out var clearedCount, out var error), $"Kill Weights should clear selected sprite weights. Error={error}");
        AssertEqual("weighted_sprite", layerName, "Cleared layer name.");
        AssertEqual(3, clearedCount, "Cleared vertex weight count.");

        var clearedWeights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertEqual(3, clearedWeights.Length, "Cleared weights should match vertex count.");
        for (var i = 0; i < clearedWeights.Length; i++)
            AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(clearedWeights[i]), $"Cleared weight {i} should be empty.");
        AssertFalse(GetPrivateField<bool>(data, "hasSkinning"), "Kill Weights should disable live skinning on the selected sprite.");
        AssertEqual(-1, GetPrivateField<int>(data, "lastPoseVersion"), "Kill Weights should invalidate pose cache.");
        AssertEqual(null, GetPrivateField<Vector2[]>(data, "deformedVertices"), "Kill Weights should clear deformed vertices.");
        AssertEqual(null, GetPrivateField<Matrix4x4[]>(data, "skinMatrices"), "Kill Weights should clear skin matrices.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Kill Weights should wait for top Apply.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Kill Weights should stage geometry data for Apply.");
    }

    private static void UtilityKillWeightsModuleRequiresSelectedSprite()
    {
        var module = new ReanimatorKillSelectedSpriteWeightsModule();
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(window, "layerPreviewRenderer", renderer);
            var api = new Reanimator.SystemApi(window);

            AssertFalse(module.IsEnabled(api), "Kill Weights utility button should be disabled without a selected sprite.");

            var layer = BuildLayer("selected_sprite", 2302, Vector2.zero);
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            AssertTrue(renderer.SelectLayer(layer), "Fixture layer should become selected.");
            AssertTrue(module.IsEnabled(api), "Kill Weights utility button should be enabled for a selected sprite.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void WeightBrushBoneRemovalCompactsSelectedSpriteSkinning()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;

        var layer = BuildLayer("brush_bone_removal", 2303, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 64f, 64f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "cachedPath", "Assets/test_brush_bone_removal.psd");
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        AssertTrue(renderer.SelectLayer(layer), "Fixture layer should become selected.");

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero, Vector2.right, Vector2.up });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 0.2f, boneIndex1 = 1, weight1 = 0.3f, boneIndex2 = 2, weight2 = 0.5f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 2, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 10, 11, 12 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[]
        {
            Matrix4x4.Translate(Vector3.right),
            Matrix4x4.Translate(Vector3.up),
            Matrix4x4.Translate(Vector3.forward)
        });
        SetPrivateField(data, "hasSkinning", true);
        SetPrivateField(renderer, "selectedBoneIndex", 11);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        AssertTrue(
            renderer.RemoveWeightBrushBoneSlot(1, out var removedVertexCount, out var error),
            $"Right-click removal should delete the selected sprite bone slot. Error={error}");
        AssertEqual(2, removedVertexCount, "Removal should report vertices influenced by the deleted bone.");
        AssertArray(new[] { 10, 12 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Sprite bone map should compact around the deleted slot.");
        AssertEqual(2, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Bind matrices should compact with the sprite bone map.");

        var weights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertFloat(2f / 7f, GetWeight(weights[0], 0), 0.0001f, "First remaining influence should normalize.");
        AssertFloat(5f / 7f, GetWeight(weights[0], 1), 0.0001f, "Higher influence index should shift down and normalize.");
        AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[1]), "Deleted-only vertex should become unweighted.");
        AssertFloat(1f, GetWeight(weights[2], 1), 0.0001f, "Higher-only influence should shift to the compacted index.");
        AssertEqual(-1, GetPrivateField<int>(renderer, "selectedBoneIndex"), "Deleting the selected renderer bone should clear bone selection.");
        AssertEqual(1, undoLabels.Count, "Bone removal should register one Undo operation.");
        AssertEqual("Remove Weight Brush Bone", undoLabels[0], "Bone removal Undo label.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Bone removal should wait for top Apply.");
        AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Bone removal should stage one geometry patch for Apply.");
    }

    private static void WeightBrushAddBonePreservesExistingSlotsAndWeights()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo { index = 0, name = "first", parentId = -1, visible = true, worldRotation = Quaternion.identity },
            new PsdLayerPreviewRenderer.BoneInfo { index = 1, name = "second", parentId = -1, visible = true, worldRotation = Quaternion.identity },
            new PsdLayerPreviewRenderer.BoneInfo { index = 2, name = "added", parentId = -1, visible = true, worldRotation = Quaternion.identity }
        });

        var layer = BuildLayer("brush_bone_add", 2304, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 64f, 64f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        AssertTrue(renderer.SelectLayer(layer), "Fixture layer should become selected.");

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var originalWeights = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 0.75f, boneIndex1 = 1, weight1 = 0.25f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        };
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero, Vector2.right });
        SetPrivateField(data, "boneWeights", originalWeights);
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });
        SetPrivateField(data, "hasSkinning", true);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = data;

        AssertTrue(
            renderer.AddWeightBrushBone(2, out var addedSpriteBone, out var error),
            $"Alt+Shift assignment should append the clicked bone. Error={error}");
        AssertEqual(2, addedSpriteBone, "Added renderer bone should use the appended sprite-bone slot.");
        AssertArray(new[] { 0, 1, 2 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Existing sprite-bone slots should stay intact.");
        var weights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertFloat(0.75f, GetWeight(weights[0], 0), 0.0001f, "Adding a bone should preserve first existing weight.");
        AssertFloat(0.25f, GetWeight(weights[0], 1), 0.0001f, "Adding a bone should preserve second existing weight.");
        AssertFloat(1f, GetWeight(weights[1], 1), 0.0001f, "Adding a bone should preserve other vertex weights.");

        AssertTrue(renderer.AddWeightBrushBone(2, out var existingSpriteBone, out error), $"Re-adding an existing bone should select it. Error={error}");
        AssertEqual(2, existingSpriteBone, "Existing bone should resolve to its original sprite-bone slot.");
        AssertArray(new[] { 0, 1, 2 }, GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), "Re-adding a bone should not create duplicates.");
        AssertEqual(1, undoLabels.Count, "Only the actual append should register Undo.");
        AssertEqual("Assign Weight Brush Bone", undoLabels[0], "Bone append Undo label.");
    }

    private static void VisibilityLayoutVirtualizesRowsWithStableContentMetrics()
    {
        var rows = ReanimatorVisibilityLayoutAlgorithms.CalculateVirtualizedRows(
            rowCount: 80,
            listHeight: 90f,
            scrollY: 360f,
            rowHeight: 18f);

        AssertEqual(80, rows.TotalRows, "Content rows should preserve full list height.");
        AssertEqual(16, rows.FirstRow, "Visible range should include buffered rows before scroll.");
        AssertEqual(29, rows.LastRow, "Visible range should include buffered rows after viewport.");
        AssertFloat(1440f, rows.ContentHeight, 0.0001f, "Content height should be a single reserved layout rect.");

        var emptyViewport = ReanimatorVisibilityLayoutAlgorithms.CalculateVirtualizedRows(
            rowCount: 0,
            listHeight: 54f,
            scrollY: 200f,
            rowHeight: 18f);

        AssertEqual(3, emptyViewport.TotalRows, "Empty filtered lists should still fill the viewport.");
        AssertEqual(0, emptyViewport.FirstRow, "Empty viewport first row.");
        AssertEqual(3, emptyViewport.LastRow, "Empty viewport last row.");
        AssertFloat(54f, emptyViewport.ContentHeight, 0.0001f, "Empty viewport content height.");
    }

    private static void VisibilityLayoutPinsDepthAndColorColumnsDuringHorizontalScroll()
    {
        var rect = new Rect(10f, 20f, 220f, 18f);
        var unscrolled = ReanimatorVisibilityLayoutAlgorithms.CalculatePinnedRowRects(
            rect,
            level: 9,
            horizontalScroll: 0f,
            includeToggle: true,
            eyeWidth: 18f,
            foldoutWidth: 15f,
            toggleWidth: 16f,
            renameWidth: 20f,
            depthWidth: 25f,
            colorWidth: 42f,
            indentWidth: 13f,
            treeMinWidth: 180f,
            gap: 2f,
            rightPadding: 2f);
        var scrolled = ReanimatorVisibilityLayoutAlgorithms.CalculatePinnedRowRects(
            rect,
            level: 9,
            horizontalScroll: 90f,
            includeToggle: true,
            eyeWidth: 18f,
            foldoutWidth: 15f,
            toggleWidth: 16f,
            renameWidth: 20f,
            depthWidth: 25f,
            colorWidth: 42f,
            indentWidth: 13f,
            treeMinWidth: 180f,
            gap: 2f,
            rightPadding: 2f);

        AssertFloat(25f, unscrolled.DepthRect.width, 0.0001f, "Depth column should be the compact production width.");
        AssertFloat(20f, unscrolled.RenameRect.width, 0.0001f, "Generated-bone rename column should fit a compact R button.");
        AssertFloat(42f, unscrolled.ColorRect.width, 0.0001f, "Color column should be the compact production width.");
        AssertFloat(unscrolled.RenameRect.xMax + 2f, unscrolled.DepthRect.x, 0.0001f, "Rename button should sit immediately before depth.");
        AssertRect(unscrolled.DepthRect, scrolled.DepthRect, 0.0001f, "Depth column must stay pinned while tree scrolls horizontally.");
        AssertRect(unscrolled.RenameRect, scrolled.RenameRect, 0.0001f, "Generated-bone rename column must stay pinned while tree scrolls horizontally.");
        AssertRect(unscrolled.ColorRect, scrolled.ColorRect, 0.0001f, "Color column must stay pinned while tree scrolls horizontally.");
        AssertFloat(unscrolled.NameRect.x - 90f, scrolled.NameRect.x, 0.0001f, "Bone name tree should move with horizontal scroll.");

        var deepContentWidth = ReanimatorVisibilityLayoutAlgorithms.CalculateTreeContentWidth(
            maxLevel: 18,
            longestLabelWidth: 80f,
            includeToggle: true,
            eyeWidth: 18f,
            foldoutWidth: 15f,
            toggleWidth: 16f,
            indentWidth: 13f,
            treeMinWidth: 180f);
        AssertTrue(deepContentWidth > unscrolled.TreeViewportWidth, "Deep bone hierarchies should expose horizontal scroll range.");
    }

    private static void BoneHierarchyMarksGeneratedBoneNames()
    {
        var path = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/Reanimator.Bones.cs");
        AssertTrue(File.Exists(path), "Bone hierarchy source should be available for the generated-name UI contract check.");

        var source = File.ReadAllText(path);
        AssertTrue(
            source.Contains("StartsWith(\"bone_\", StringComparison.OrdinalIgnoreCase)"),
            "Bone hierarchy should detect generated bone_ names case-insensitively.");
        AssertTrue(
            source.Contains("new Color(1f, 0.78f, 0.18f, 0.26f)"),
            "Generated bone_ hierarchy rows should draw the yellow warning tint.");
        AssertTrue(
            source.Contains("DrawBoneRenamePresetButton(renameRect, bones, boneId, boneName)"),
            "Generated bone_ hierarchy rows should draw the compact rename preset button.");
        AssertTrue(
            source.Contains("ShowBoneNamePresetMenu(layerPreviewRenderer, bones, boneId)"),
            "Hierarchy rename button should reuse the same preset menu as Bone Edit.");
        AssertTrue(
            source.Contains("renameRect.Contains(e.mousePosition)"),
            "Clicking the hierarchy rename button should not also select the row.");
    }

    private static void VisibilityCacheRestoresSpriteStateAfterLiveReload()
    {
        const string assetPath = "Assets/test_visibility_cache.psd";
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("cached_sprite", 123, Vector2.zero);
        layer.visible = true;
        var node = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "123",
            name = "cached_sprite",
            isGroup = false,
            visible = true,
            layerIndex = 0
        };

        SetPrivateField(renderer, "cachedPath", assetPath);
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "layerHierarchyNodes", new List<PsdLayerPreviewRenderer.LayerHierarchyNode> { node });

        AssertTrue(renderer.SetLayerHierarchyNodeVisible("123", false), "Turning off a sprite should update visibility.");
        AssertFalse(layer.visible, "Layer should be hidden before reload simulation.");
        AssertFalse(node.visible, "Hierarchy node should be hidden before reload simulation.");

        GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Clear();
        GetPrivateField<List<PsdLayerPreviewRenderer.LayerHierarchyNode>>(renderer, "layerHierarchyNodes").Clear();

        var layerVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var hierarchyVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var boneVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryCopyCachedUiVisibilityState", assetPath, layerVisibility, hierarchyVisibility, boneVisibility),
            "Serialized UI visibility cache should survive losing live layer state.");
        AssertTrue(layerVisibility.TryGetValue("123", out var restoredLayerVisible), "Layer visibility should be cached by stable key.");
        AssertFalse(restoredLayerVisible, "Cached layer visibility should restore the hidden state.");
        AssertTrue(hierarchyVisibility.TryGetValue("123", out var restoredNodeVisible), "Hierarchy visibility should be cached by node key.");
        AssertFalse(restoredNodeVisible, "Cached hierarchy visibility should restore the hidden state.");
    }

    private static void VisibilityHierarchyShowsAncestorsWhenChildIsShown()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("leaf_sprite", 124, Vector2.zero);
        layer.visible = false;
        var root = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "root",
            name = "root",
            isGroup = true,
            visible = false
        };
        var group = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "group",
            parentKey = "root",
            name = "group",
            isGroup = true,
            visible = false
        };
        var leaf = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "leaf",
            parentKey = "group",
            name = "leaf_sprite",
            isGroup = false,
            visible = false,
            layerIndex = 0
        };

        SetPrivateField(renderer, "cachedPath", "Assets/test_visibility_ancestors.psd");
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "layerHierarchyNodes", new List<PsdLayerPreviewRenderer.LayerHierarchyNode> { root, group, leaf });

        AssertTrue(renderer.SetLayerHierarchyNodeVisible("leaf", true), "Showing a child node should change visibility.");
        AssertTrue(root.visible, "Showing a child should show the root parent.");
        AssertTrue(group.visible, "Showing a child should show the direct parent.");
        AssertTrue(leaf.visible, "Leaf node should be visible.");
        AssertTrue(layer.visible, "Leaf sprite layer should be visible.");
        AssertTrue(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Visibility parent repair should wait for top Apply.");
    }

    private static void VisibilityRevealSelectedSpriteScrollsList()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layers = new List<PsdLayerPreviewRenderer.LayerInfo>();
        var sortedLayers = GetPrivateList(renderer, "sortedLayers");
        for (var i = 0; i < 12; i++)
        {
            var layer = BuildLayer($"reveal_sprite_{i}", 2400 + i, Vector2.zero);
            layers.Add(layer);
            sortedLayers.Add(layer);
        }

        SetPrivateField(renderer, "layerInfos", layers);
        AssertTrue(renderer.SelectLayer(layers[8]), "Fixture selected sprite should become selected.");

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "visibilityHierarchy", false);
            SetPrivateField(window, "visibilitySearch", "not_the_selected_sprite");
            SetPrivateField(window, "visibilityScroll", Vector2.zero);

            var filteredRows = InvokePrivateMethod(window, "BuildSpriteVisibilityRows");
            InvokePrivateMethod(window, "SyncSpriteVisibilityScrollToSelection", filteredRows, 54f);

            AssertEqual(string.Empty, GetPrivateField<string>(window, "visibilitySearch"), "Reveal should clear a search that hides the selected sprite.");
            AssertFloat(126f, GetPrivateField<Vector2>(window, "visibilityScroll").y, 0.0001f, "Reveal should center the selected sprite row in the flat list.");
            AssertEqual(renderer.SelectedLayerSelectionKey, GetPrivateField<string>(window, "visibilityAutoScrolledLayerSelectionKey"), "Reveal should remember the auto-scrolled selection.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void VisibilityRevealSelectedSpriteExpandsHierarchy()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("nested_selected_sprite", 2500, Vector2.zero);
        var root = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "root",
            name = "root",
            isGroup = true,
            layerIndex = -1
        };
        var group = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "group",
            parentKey = "root",
            name = "group",
            isGroup = true,
            layerIndex = -1
        };
        var leaf = new PsdLayerPreviewRenderer.LayerHierarchyNode
        {
            key = "leaf",
            parentKey = "group",
            name = "nested_selected_sprite",
            isGroup = false,
            layerIndex = 0
        };

        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
        SetPrivateField(renderer, "layerHierarchyNodes", new List<PsdLayerPreviewRenderer.LayerHierarchyNode> { root, group, leaf });
        AssertTrue(renderer.SelectLayer(layer), "Fixture selected sprite should become selected.");

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "visibilityHierarchy", true);
            SetPrivateField(window, "collapsedLayerNodes", new List<string> { "root", "group" });
            SetPrivateField(window, "visibilityScroll", Vector2.zero);

            AssertTrue((bool)InvokePrivateMethod(window, "ExpandSelectedLayerAncestors"), "Reveal should expand collapsed parents of the selected sprite.");
            var collapsedLayerNodes = GetPrivateField<List<string>>(window, "collapsedLayerNodes");
            AssertFalse(collapsedLayerNodes.Contains("root"), "Reveal should expand the root parent.");
            AssertFalse(collapsedLayerNodes.Contains("group"), "Reveal should expand the direct parent.");

            var rows = InvokePrivateMethod(window, "BuildSpriteVisibilityRows");
            InvokePrivateMethod(window, "SyncSpriteVisibilityScrollToSelection", rows, 18f);
            AssertFloat(36f, GetPrivateField<Vector2>(window, "visibilityScroll").y, 0.0001f, "Reveal should scroll to the selected hierarchy row.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }
}
#endif
