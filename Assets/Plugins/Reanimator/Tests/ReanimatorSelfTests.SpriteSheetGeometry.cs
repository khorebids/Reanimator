#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    private static void SpriteRectChangesKeepSelectedLayerStable()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("sprite", 42, new Vector2(10f, 20f));
        layer.spriteGuid = "sprite-guid";
        layer.sourceRect = new Rect(4f, 5f, 32f, 48f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        AssertTrue(renderer.SelectLayer(layer), "Layer should become selected.");
        AssertTrue(renderer.TryGetSelectedLayer(out var selectedBefore), "Selected layer should resolve before rect edit.");
        AssertTrue(ReferenceEquals(layer, selectedBefore), "Selected layer reference before rect edit.");

        AssertTrue(renderer.SetLayerSpriteRect(layer, new Rect(8f, 9f, 64f, 96f)), "Sprite rect edit should report changed.");
        AssertTrue(renderer.TryGetSelectedLayer(out var selectedAfter), "Selected layer should still resolve after rect edit.");
        AssertTrue(ReferenceEquals(layer, selectedAfter), "Selected layer reference after rect edit.");
        AssertTrue(renderer.IsLayerSelected(layer), "Layer should remain selected after source rect key changes.");
        AssertEqual(new Rect(8f, 9f, 64f, 96f), layer.sourceRect, "Source rect should update.");
        AssertEqual(layer.sourceRect, layer.textureRect, "Texture rect should stay synced for sprite sheet edits.");
        AssertVector(new Vector2(10f, 20f), layer.position, 0.0001f, "Sprite rect edits should keep the serialized spritePosition stable.");
    }

    private static void SpriteSheetGeometryOptimizeFitsRectToVertices()
    {
        var layer = BuildLayer("optimized", 43, new Vector2(100f, 200f));
        layer.sourceRect = new Rect(10f, 20f, 100f, 80f);
        layer.textureRect = layer.sourceRect;

        var vertices = new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(12.25f, 5.5f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(80.1f, 60.2f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(34f, 41f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(160f, 20f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(-30f, 10f))
        };

        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToGeometry(layer, vertices, new Vector2(256f, 256f), out var fitted, out var positionDelta),
            "Geometry fit should succeed.");
        AssertRect(new Rect(22f, 25f, 69f, 56f), fitted, 0.0001f, "Fitted sprite rect.");
        AssertVector(new Vector2(12f, 5f), positionDelta, 0.0001f, "Position delta should describe the fitted rect movement.");

        layer.sourceRect = fitted;
        layer.textureRect = fitted;
        var rebasedLocalA = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, vertices[0]);
        var rebasedLocalB = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, vertices[1]);
        AssertVector(new Vector2(12.25f, 5.5f), rebasedLocalA, 0.0001f, "Rect trim should not move document-space geometry.");
        AssertVector(new Vector2(80.1f, 60.2f), rebasedLocalB, 0.0001f, "Rect trim should keep max vertex document-space geometry stable.");

        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToGeometry(layer, Array.Empty<Vector2>(), new Vector2(256f, 256f), out _, out _),
            "Empty geometry should not produce a fitted rect.");
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToGeometry(
                layer,
                new[]
                {
                    PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(160f, 20f)),
                    PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(-30f, 10f))
                },
                new Vector2(256f, 256f),
                out _,
                out _),
            "Only out-of-frame geometry should not produce a fitted rect.");

        var uvs = new[]
        {
            new Vector2(22.25f / 256f, 25.5f / 256f),
            new Vector2(90.1f / 256f, 80.2f / 256f),
            new Vector2(140f / 256f, 55f / 256f),
            new Vector2(5f / 256f, 30f / 256f)
        };
        layer.sourceRect = new Rect(10f, 20f, 100f, 80f);
        layer.textureRect = layer.sourceRect;
        layer.position = new Vector2(100f, 200f);

        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToTextureUvs(layer, uvs, new Vector2(256f, 256f), new Vector2(256f, 256f), out var uvFitted, out var uvPositionDelta),
            "UV geometry fit should succeed.");
        AssertRect(new Rect(22f, 25f, 69f, 56f), uvFitted, 0.0001f, "UV fitted sprite rect should ignore neighboring UVs.");
        AssertVector(new Vector2(12f, 5f), uvPositionDelta, 0.0001f, "UV position delta should preserve document-space geometry after rect trim.");
        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.TryBuildKeepMaskForTextureUvs(layer, uvs, new Vector2(256f, 256f), out var keepMask, out var keptCount),
            "UV keep mask should build.");
        AssertEqual(2, keptCount, "UV keep mask should keep only current sprite frame vertices.");
        AssertTrue(keepMask[0], "First current-frame UV should be kept.");
        AssertTrue(keepMask[1], "Second current-frame UV should be kept.");
        AssertFalse(keepMask[2], "Neighboring right UV should be removed.");
        AssertFalse(keepMask[3], "Neighboring left UV should be removed.");
        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.TryBuildKeepMaskForTextureUvs(uvFitted, uvs, new Vector2(256f, 256f), out var fittedKeepMask, out var fittedKeptCount),
            "Fitted UV keep mask should build.");
        AssertEqual(2, fittedKeptCount, "Fitted rect should still contain all current sprite geometry vertices.");
        AssertTrue(fittedKeepMask[0], "Fitted rect should preserve the first current geometry vertex.");
        AssertTrue(fittedKeepMask[1], "Fitted rect should preserve the second current geometry vertex.");
        AssertFalse(fittedKeepMask[2], "Fitted rect should not preserve neighboring right geometry.");
        AssertFalse(fittedKeepMask[3], "Fitted rect should not preserve neighboring left geometry.");

        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.TryFitRectToTextureUvs(
                layer,
                new[]
                {
                    new Vector2(140f / 256f, 55f / 256f),
                    new Vector2(5f / 256f, 30f / 256f)
                },
                new Vector2(256f, 256f),
                new Vector2(256f, 256f),
                out _,
                out _),
            "Only neighboring UVs should not produce a fitted rect.");
    }

    private static void SpriteSheetOptimizeSnapsShiftedRectToSpritePixels()
    {
        void RunCase(Rect brokenRect, string label, bool makeTextureNoLongerReadable = false)
        {
            var renderer = new PsdLayerPreviewRenderer();
            var texture = new Texture2D(256, 256);
            try
            {
            FillTexture(texture, new Color32(0, 0, 0, 0));
            FillTextureRect(texture, new RectInt(22, 25, 69, 56), new Color32(255, 255, 255, 255));
            FillTextureRect(texture, new RectInt(22, 95, 25, 25), new Color32(255, 255, 255, 255));
            FillTextureRect(texture, new RectInt(120, 25, 30, 30), new Color32(255, 255, 255, 255));
            texture.Apply(false, makeTextureNoLongerReadable);

            var layer = BuildLayer("shifted-optimized", 46, new Vector2(88f, 200f));
            layer.sourceRect = brokenRect;
            layer.textureRect = layer.sourceRect;
            layer.spriteSheetOptimizeReferenceRect = new Rect(10f, 20f, 100f, 80f);
            layer.spriteSheetOptimizeReferencePosition = new Vector2(88f, 200f);
            layer.texture = texture;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            AssertTrue(renderer.SelectLayer(layer), $"{label}: layer should become selected.");
            var selectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");

            var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(data, "layer", layer);
            SetPrivateField(data, "texture", texture);
            SetPrivateField(data, "vertices", new[]
            {
                new Vector2(105f, 210f),
                new Vector2(125f, 215f),
                new Vector2(120f, 245f),
                new Vector2(210f, 260f)
            });
            SetPrivateField(data, "vertexUvs", new[]
            {
                new Vector2(0.9f, 0.9f),
                new Vector2(0.91f, 0.9f),
                new Vector2(0.9f, 0.91f),
                new Vector2(0.91f, 0.91f)
            });
            SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
            SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0, 0, 2, 2, 3, 3, 0 });
            SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
            SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());

            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            dataBySelectionKey[selectionKey] = data;

            AssertEqual(1, renderer.OptimizeSpriteSheetRectsToGeometry(), $"{label}: optimize should update the broken sprite rect.");
            AssertRect(new Rect(22f, 25f, 128f, 95f), layer.sourceRect, 0.0001f, $"{label}: optimize should cover every opaque component in the PSD layer.");
            AssertEqual(layer.sourceRect, layer.textureRect, $"{label}: optimized texture rect should stay synced.");
            var optimizedPosition = new Vector2(88f, 200f);
            AssertVector(optimizedPosition, layer.position, 0.0001f, $"{label}: optimize should keep serialized spritePosition stable.");
            AssertVector(
                optimizedPosition,
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, Vector2.zero),
                0.0001f,
                $"{label}: optimized sprite top-left should keep the reference sprite position.");
            AssertTrue(renderer.IsLayerSelected(layer), $"{label}: layer should remain selected after optimize changes the selection key.");

            var optimizedVertices = GetPrivateField<Vector2[]>(data, "vertices");
            var optimizedUvs = GetPrivateField<Vector2[]>(data, "vertexUvs");
            var optimizedTriangles = GetPrivateField<int[]>(data, "triangles");
            AssertEqual(4, optimizedVertices.Length, $"{label}: optimize should not cull geometry against only one opaque component.");
            AssertEqual(4, optimizedUvs.Length, $"{label}: optimize should keep geometry UVs for every retained vertex.");
            AssertEqual(6, optimizedTriangles.Length, $"{label}: optimize should preserve triangles covered by the full layer rect.");
            AssertTrue(
                optimizedTriangles.All(index => index >= 0 && index < optimizedVertices.Length),
                $"{label}: optimize should leave only valid triangle indices after culling outside geometry.");
            AssertEqual(0, renderer.OptimizeSpriteSheetRectsToGeometry(), $"{label}: second optimize should be idempotent.");
            AssertRect(new Rect(22f, 25f, 128f, 95f), layer.sourceRect, 0.0001f, $"{label}: second optimize should keep the full layer rect.");
            AssertVector(optimizedPosition, layer.position, 0.0001f, $"{label}: second optimize should keep the optimized spritePosition.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        RunCase(new Rect(0f, 0f, 150f, 120f), "enlarged rect");
        RunCase(new Rect(30f, 40f, 20f, 10f), "shrunken rect");
        RunCase(new Rect(120f, 20f, 100f, 80f), "shifted rect", true);
    }

    private static void SpriteSheetOptimizeTargetsSelectedSpriteOrAll()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        var texture = new Texture2D(128, 128);
        try
        {
            FillTexture(texture, new Color32(0, 0, 0, 0));
            FillTextureRect(texture, new RectInt(12, 14, 18, 16), new Color32(255, 255, 255, 255));
            FillTextureRect(texture, new RectInt(82, 84, 22, 20), new Color32(255, 255, 255, 255));
            texture.Apply();

            var selected = BuildLayer("selected-optimize", 47, new Vector2(20f, 30f));
            selected.sourceRect = new Rect(0f, 0f, 64f, 64f);
            selected.textureRect = selected.sourceRect;
            selected.spriteSheetOptimizeReferenceRect = selected.sourceRect;
            selected.spriteSheetOptimizeReferencePosition = selected.position;
            selected.texture = texture;

            var other = BuildLayer("other-optimize", 48, new Vector2(90f, 110f));
            other.sourceRect = new Rect(64f, 64f, 64f, 64f);
            other.textureRect = other.sourceRect;
            other.spriteSheetOptimizeReferenceRect = other.sourceRect;
            other.spriteSheetOptimizeReferencePosition = other.position;
            other.texture = texture;

            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected, other });
            AssertTrue(renderer.SelectLayer(selected), "Selected sprite should become selected.");

            AssertEqual(1, renderer.OptimizeSpriteSheetRectsToGeometry(), "Optimize with a selection should update only the selected sprite.");
            AssertRect(new Rect(12f, 14f, 18f, 16f), selected.sourceRect, 0.0001f, "Selected sprite rect should fit selected sprite alpha.");
            AssertVector(new Vector2(20f, 30f), selected.position, 0.0001f, "Selected sprite position should stay stable when selected rect is optimized.");
            AssertRect(new Rect(64f, 64f, 64f, 64f), other.sourceRect, 0.0001f, "Other sprite rect should stay unchanged while a sprite is selected.");
            AssertVector(new Vector2(90f, 110f), other.position, 0.0001f, "Other sprite position should stay unchanged while a sprite is selected.");
            AssertEqual("Optimize Sprite Sheet Rects", undoLabels[0], "Optimize should register an undo operation.");

            AssertTrue(renderer.SelectLayer(null), "Selection should clear.");
            AssertEqual(1, renderer.OptimizeSpriteSheetRectsToGeometry(), "Optimize without a selection should update all remaining broken sprites.");
            AssertRect(new Rect(82f, 84f, 22f, 20f), other.sourceRect, 0.0001f, "Other sprite rect should fit its alpha after all-sprite optimize.");
            AssertVector(new Vector2(90f, 110f), other.position, 0.0001f, "Other sprite position should stay stable when all-sprite optimize changes its rect.");
            AssertEqual(2, undoLabels.Count, "Each optimize operation should register one undo entry.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetOptimizeAllPreservesLogicalAtlasScale()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(128, 128);
        try
        {
            FillTexture(texture, new Color32(0, 0, 0, 0));
            FillTextureRect(texture, new RectInt(12, 14, 18, 16), new Color32(255, 255, 255, 255));
            FillTextureRect(texture, new RectInt(82, 84, 22, 20), new Color32(255, 255, 255, 255));
            texture.Apply();

            var unscaledNewLayer = BuildLayer("new-unscaled", 147, new Vector2(20f, 30f));
            unscaledNewLayer.sourceRect = new Rect(10f, 10f, 40f, 40f);
            unscaledNewLayer.textureRect = new Rect(5f, 5f, 20f, 20f);
            unscaledNewLayer.spriteSheetOptimizeReferenceRect = unscaledNewLayer.sourceRect;
            unscaledNewLayer.spriteSheetOptimizeReferencePosition = unscaledNewLayer.position;
            unscaledNewLayer.texture = texture;

            var scaledExistingLayer = BuildLayer("existing-scaled", 148, new Vector2(90f, 110f));
            scaledExistingLayer.sourceRect = new Rect(128f, 128f, 128f, 128f);
            scaledExistingLayer.textureRect = new Rect(64f, 64f, 64f, 64f);
            scaledExistingLayer.textureSamplingRect = scaledExistingLayer.textureRect;
            scaledExistingLayer.spriteSheetOptimizeReferenceRect = scaledExistingLayer.sourceRect;
            scaledExistingLayer.spriteSheetOptimizeReferencePosition = scaledExistingLayer.position;
            scaledExistingLayer.texture = texture;

            SetPrivateField(
                renderer,
                "layerInfos",
                new List<PsdLayerPreviewRenderer.LayerInfo> { unscaledNewLayer, scaledExistingLayer });

            AssertEqual(2, renderer.OptimizeSpriteSheetRectsToGeometry(), "Optimize without a selection should process every sprite across mixed coordinate scales.");
            AssertRect(new Rect(12f, 14f, 18f, 16f), unscaledNewLayer.sourceRect, 0.0001f, "New unscaled layer should fit physical PSD pixels directly.");
            AssertRect(new Rect(12f, 14f, 18f, 16f), unscaledNewLayer.textureRect, 0.0001f, "New unscaled layer sampling rect should remain physical.");
            AssertRect(new Rect(164f, 168f, 44f, 40f), scaledExistingLayer.sourceRect, 0.0001f, "Existing layer should keep logical 2x atlas coordinates after Optimize.");
            AssertRect(new Rect(82f, 84f, 22f, 20f), scaledExistingLayer.textureRect, 0.0001f, "Existing layer should keep physical texture sampling coordinates after Optimize.");
            AssertVector(new Vector2(20f, 30f), unscaledNewLayer.position, 0.0001f, "New layer document position should remain stable.");
            AssertVector(new Vector2(90f, 110f), scaledExistingLayer.position, 0.0001f, "Existing layer document position should remain stable.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetOptimizeApplyStatePreservesSkinningLayout()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(256, 256);
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            FillTexture(texture, new Color32(0, 0, 0, 0));
            FillTextureRect(texture, new RectInt(22, 25, 69, 56), new Color32(255, 255, 255, 255));
            texture.Apply();

            var referenceRect = new Rect(10f, 20f, 100f, 80f);
            var referencePosition = new Vector2(88f, 200f);
            var layer = BuildLayer("optimize-apply", 51, referencePosition);
            layer.sourceRect = referenceRect;
            layer.textureRect = referenceRect;
            layer.spriteSheetOptimizeReferenceRect = referenceRect;
            layer.spriteSheetOptimizeReferencePosition = referencePosition;
            layer.texture = texture;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            AssertTrue(renderer.SelectLayer(layer), "Layer should become selected.");

            AssertEqual(1, renderer.OptimizeSpriteSheetRectsToGeometry(), "Optimize should fit the selected sprite.");
            var optimizedDocumentRect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
            AssertRect(new Rect(22f, 25f, 69f, 56f), layer.sourceRect, 0.0001f, "Optimized source rect.");
            AssertRect(new Rect(88f, 200f, 69f, 56f), optimizedDocumentRect, 0.0001f, "Optimized document rect should keep the sprite position stable while the texture rect changes.");

            fixture.spriteLike = new SpriteImportDataLike
            {
                rect = referenceRect,
                uvTransform = referenceRect.position,
                spritePosition = referencePosition
            };
            fixture.psdLayerLike = new PsdLayerDataLike
            {
                mosaicPosition = referenceRect.position
            };

            var so = new SerializedObject(fixture);
            var spriteProp = so.FindProperty("spriteLike");
            AssertTrue(
                ReanimatorAssetChangeAlgorithms.WriteRectIfChanged(spriteProp.FindPropertyRelative("rect"), layer.sourceRect),
                "Apply state should write optimized serialized rect.");
            AssertFalse(
                ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(spriteProp.FindPropertyRelative("spritePosition"), layer.position),
                "Apply state should leave the stable spritePosition unchanged while the texture rect changes.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(layer.sourceRect, fixture.spriteLike.rect, "Applied sprite rect should match optimized rect.");
            AssertVector(layer.position, fixture.spriteLike.spritePosition, 0.0001f, "Applied sprite position should track the optimized rect delta.");

            var providerRect = new SpriteRect
            {
                name = layer.name,
                rect = referenceRect,
                pivot = layer.pivot,
                alignment = (SpriteAlignment)layer.alignment,
                border = layer.border
            };
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteRectWithLayerIfChanged", providerRect, layer),
                "Data provider SpriteRect should update to the optimized rect before dataProvider.Apply.");
            AssertEqual(layer.sourceRect, providerRect.rect, "Data provider SpriteRect should match optimized rect.");

            var reloadedLayer = BuildLayer("optimize-apply", 51, fixture.spriteLike.spritePosition);
            reloadedLayer.sourceRect = providerRect.rect;
            reloadedLayer.textureRect = providerRect.rect;
            AssertRect(
                optimizedDocumentRect,
                PsdCoordinateSpace.GetLayerDocumentRect(reloadedLayer),
                0.0001f,
                "Reloaded Skinning Editor layer rect should match the optimized layout after Apply.");
            AssertVector(
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, Vector2.zero),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(reloadedLayer, Vector2.zero),
                0.0001f,
                "Reloaded Skinning Editor sprite pixels should not shift after Apply.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetOptimizeThenDefaultGeometryKeepsSpriteStable()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(256, 256);
        try
        {
            FillTexture(texture, new Color32(0, 0, 0, 0));
            FillTextureRect(texture, new RectInt(22, 25, 69, 56), new Color32(255, 255, 255, 255));
            texture.Apply();

            var stablePosition = new Vector2(326f, 646f);
            var originalRect = new Rect(10f, 20f, 100f, 80f);
            var optimizedRect = new Rect(22f, 25f, 69f, 56f);
            var brokenRect = new Rect(0f, 0f, 150f, 120f);
            var layer = BuildLayer("optimize-then-default", 82, stablePosition);
            layer.sourceRect = originalRect;
            layer.textureRect = originalRect;
            layer.spriteSheetOptimizeReferenceRect = originalRect;
            layer.spriteSheetOptimizeReferencePosition = stablePosition;
            layer.texture = texture;
            layer.spriteGuid = "optimize-default-guid";
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

            var defaultLocalVertices = new[]
            {
                new Vector2(5f, 6f),
                new Vector2(95f, 8f),
                new Vector2(82f, 70f),
                new Vector2(10f, 75f)
            };
            var defaultDocVertices = defaultLocalVertices
                .Select(local => PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local))
                .ToArray();
            var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(data, "layer", layer);
            SetPrivateField(data, "texture", texture);
            SetPrivateField(data, "vertices", defaultDocVertices);
            SetPrivateField(data, "vertexUvs", defaultLocalVertices
                .Select(local => new Vector2(
                    (originalRect.x + local.x) / texture.width,
                    (originalRect.y + local.y) / texture.height))
                .ToArray());
            SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
            SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 0, 2, 2, 3, 0, 3, 3, 0 });
            SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
            SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());
            InvokePrivateMethod(renderer, "StoreDefaultGeometrySnapshot", layer, data);

            layer.sourceRect = brokenRect;
            layer.textureRect = brokenRect;
            AssertTrue(renderer.SelectLayer(layer), "Layer should become selected after rect corruption.");
            var selectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            dataBySelectionKey[selectionKey] = data;

            SetPrivateField(data, "vertices", new[]
            {
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(25f, 30f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(55f, 40f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(80f, 70f)),
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(130f, 110f))
            });
            SetPrivateField(data, "vertexUvs", Array.Empty<Vector2>());
            SetPrivateField(data, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
            SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 0, 2, 2, 3, 0, 3, 3, 0 });
            SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
            SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());

            AssertEqual(1, renderer.OptimizeSpriteSheetRectsToGeometry(), "Optimize should restore the rect from sprite pixels.");
            AssertRect(optimizedRect, layer.sourceRect, 0.0001f, "Optimize should fit the rect to sprite alpha.");
            var optimizedPosition = stablePosition;
            AssertVector(optimizedPosition, layer.position, 0.0001f, "Optimize should keep serialized spritePosition stable while uvTransform tracks the rect.");
            AssertTrue(renderer.IsLayerSelected(layer), "Selection should survive optimize selection-key rewrite.");

            AssertTrue(
                renderer.GenerateDefaultGeometryForSelected("Assets/missing-test.psd", out var error),
                $"Default geometry should regenerate selected geometry after optimize. Error={error}");
            AssertRect(optimizedRect, layer.sourceRect, 0.0001f, "Default Geometry must keep the optimized rect.");
            AssertEqual(layer.sourceRect, layer.textureRect, "Default Geometry must keep source and texture rects synced.");
            AssertVector(optimizedPosition, layer.position, 0.0001f, "Default Geometry must keep the optimized spritePosition.");
            AssertVector(
                optimizedPosition,
                PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, Vector2.zero),
                0.0001f,
                "Optimize -> Default Geometry should keep the optimized sprite top-left document point stable.");

            var restoredVertices = GetPrivateField<Vector2[]>(data, "vertices");
            AssertEqual(defaultLocalVertices.Length, restoredVertices.Length, "Default geometry should restore the snapshot vertex count.");
            for (var i = 0; i < restoredVertices.Length; i++)
            {
                var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, restoredVertices[i]);
                AssertTrue(local.x >= -0.0001f && local.x <= optimizedRect.width + 0.0001f, $"Restored vertex {i} x should stay inside the optimized rect.");
                AssertTrue(local.y >= -0.0001f && local.y <= optimizedRect.height + 0.0001f, $"Restored vertex {i} y should stay inside the optimized rect.");

                var normalized = new Vector2(defaultLocalVertices[i].x / originalRect.width, defaultLocalVertices[i].y / originalRect.height);
                var expectedLocal = new Vector2(normalized.x * optimizedRect.width, normalized.y * optimizedRect.height);
                AssertVector(expectedLocal, local, 0.0001f, $"Restored vertex {i} should remap from the original rect into the optimized rect.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetDefaultGeometryRestoresImportedSelectedOnly()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(128, 128);
        try
        {
            var selected = BuildLayer("selected-default", 44, new Vector2(100f, 200f));
            selected.sourceRect = new Rect(10f, 20f, 30f, 40f);
            selected.textureRect = selected.sourceRect;
            selected.texture = texture;
            var other = BuildLayer("other-default", 45, new Vector2(300f, 400f));
            other.sourceRect = new Rect(50f, 60f, 20f, 20f);
            other.textureRect = other.sourceRect;
            other.texture = texture;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected, other });

            AssertTrue(renderer.SelectLayer(other), "Other layer should become selected.");
            var otherSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
            AssertTrue(renderer.SelectLayer(selected), "Selected layer should become selected.");
            var selectedSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");

            var selectedData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(selectedData, "layer", selected);
            SetPrivateField(selectedData, "texture", texture);
            var defaultLocalVertices = new[]
            {
                new Vector2(3f, 4f),
                new Vector2(24f, 2f),
                new Vector2(29f, 20f),
                new Vector2(16f, 38f),
                new Vector2(2f, 25f)
            };
            var defaultVertices = new Vector2[defaultLocalVertices.Length];
            for (var i = 0; i < defaultLocalVertices.Length; i++)
                defaultVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, defaultLocalVertices[i]);
            var defaultUvs = new[]
            {
                new Vector2((10f + 3f) / 128f, (20f + 4f) / 128f),
                new Vector2((10f + 24f) / 128f, (20f + 2f) / 128f),
                new Vector2((10f + 29f) / 128f, (20f + 20f) / 128f),
                new Vector2((10f + 16f) / 128f, (20f + 38f) / 128f),
                new Vector2((10f + 2f) / 128f, (20f + 25f) / 128f)
            };
            var defaultTriangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4 };
            var defaultAllEdges = new[] { 0, 1, 1, 2, 0, 2, 2, 3, 0, 3, 3, 4, 0, 4 };
            var defaultBoundaryEdges = new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 0 };
            var defaultWeights = new[]
            {
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                new BoneWeight { boneIndex0 = 3, weight0 = 1f }
            };
            SetPrivateField(selectedData, "vertices", defaultVertices);
            SetPrivateField(selectedData, "vertexUvs", defaultUvs);
            SetPrivateField(selectedData, "triangles", defaultTriangles);
            SetPrivateField(selectedData, "allEdgePairs", defaultAllEdges);
            SetPrivateField(selectedData, "boundaryEdgePairs", defaultBoundaryEdges);
            SetPrivateField(selectedData, "boneWeights", defaultWeights);
            InvokePrivateMethod(renderer, "StoreDefaultGeometrySnapshot", selected, selectedData);

            selected.sourceRect = new Rect(30f, 40f, 12f, 16f);
            selected.textureRect = selected.sourceRect;
            selected.position = new Vector2(140f, 260f);
            AssertTrue(renderer.SelectLayer(selected), "Selected layer should remain selectable after rect change.");
            selectedSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
            SetPrivateField(selectedData, "vertices", new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, new Vector2(15f, 15f)) });
            SetPrivateField(selectedData, "vertexUvs", new[] { new Vector2(0.2f, 0.2f) });
            SetPrivateField(selectedData, "triangles", Array.Empty<int>());
            SetPrivateField(selectedData, "allEdgePairs", Array.Empty<int>());
            SetPrivateField(selectedData, "boundaryEdgePairs", Array.Empty<int>());
            SetPrivateField(selectedData, "boneWeights", Array.Empty<BoneWeight>());

            var otherVertices = new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(4f, 5f)) };
            var otherData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(otherData, "layer", other);
            SetPrivateField(otherData, "texture", texture);
            SetPrivateField(otherData, "vertices", otherVertices);
            SetPrivateField(otherData, "vertexUvs", new[] { new Vector2(0.6f, 0.6f) });
            SetPrivateField(otherData, "triangles", Array.Empty<int>());
            SetPrivateField(otherData, "allEdgePairs", Array.Empty<int>());
            SetPrivateField(otherData, "boundaryEdgePairs", Array.Empty<int>());
            SetPrivateField(otherData, "boneWeights", Array.Empty<BoneWeight>());

            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            dataBySelectionKey[selectedSelectionKey] = selectedData;
            dataBySelectionKey[otherSelectionKey] = otherData;

            AssertTrue(
                renderer.GenerateDefaultGeometryForSelected("Assets/test.psd", out var error),
                $"Default geometry should generate for selected sprite. Error={error}");

            var vertices = GetPrivateField<Vector2[]>(selectedData, "vertices");
            var uvs = GetPrivateField<Vector2[]>(selectedData, "vertexUvs");
            var triangles = GetPrivateField<int[]>(selectedData, "triangles");
            var allEdges = GetPrivateField<int[]>(selectedData, "allEdgePairs");
            var boundaryEdges = GetPrivateField<int[]>(selectedData, "boundaryEdgePairs");
            var weights = GetPrivateField<BoneWeight[]>(selectedData, "boneWeights");
            AssertEqual(defaultVertices.Length, vertices.Length, "Default geometry vertex count.");
            for (var i = 0; i < defaultVertices.Length; i++)
            {
                var normalized = new Vector2(defaultLocalVertices[i].x / 30f, defaultLocalVertices[i].y / 40f);
                var expectedLocal = new Vector2(normalized.x * selected.sourceRect.width, normalized.y * selected.sourceRect.height);
                var expectedVertex = PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, expectedLocal);
                var expectedUv = new Vector2(
                    (selected.textureRect.x + expectedLocal.x) / 128f,
                    (selected.textureRect.y + expectedLocal.y) / 128f);
                AssertVector(expectedVertex, vertices[i], 0.0001f, $"Default imported vertex {i} should be remapped inside current rect.");
                AssertVector(expectedUv, uvs[i], 0.0001f, $"Default imported UV {i} should be remapped inside current rect.");
                AssertEqual(defaultWeights[i].boneIndex0, weights[i].boneIndex0, $"Default imported weight bone {i}.");
                AssertFloat(defaultWeights[i].weight0, weights[i].weight0, 0.0001f, $"Default imported weight value {i}.");
            }

            AssertArray(defaultTriangles, triangles, "Default imported triangles.");
            AssertArray(defaultAllEdges, allEdges, "Default imported all edges.");
            AssertArray(defaultBoundaryEdges, boundaryEdges, "Default imported boundary edges.");

            var unchangedOtherVertices = GetPrivateField<Vector2[]>(otherData, "vertices");
            AssertEqual(1, unchangedOtherVertices.Length, "Other layer vertex count should stay unchanged.");
            AssertVector(otherVertices[0], unchangedOtherVertices[0], 0.0001f, "Other layer vertex should stay unchanged.");
            AssertEqual(1, GetPrivateListCount(renderer, "autoGeometryPatches"), "Default geometry should create one selected-layer patch.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetDefaultGeometryTargetsSelectedSpriteOrAll()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        var texture = new Texture2D(128, 128);
        try
        {
            var selected = BuildLayer("selected-default-target", 49, new Vector2(10f, 20f));
            selected.sourceRect = new Rect(5f, 6f, 30f, 40f);
            selected.textureRect = selected.sourceRect;
            selected.texture = texture;

            var other = BuildLayer("other-default-target", 50, new Vector2(70f, 80f));
            other.sourceRect = new Rect(60f, 64f, 20f, 24f);
            other.textureRect = other.sourceRect;
            other.texture = texture;

            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { selected, other });
            AssertTrue(renderer.SelectLayer(other), "Other sprite should become selected.");
            var otherSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");
            AssertTrue(renderer.SelectLayer(selected), "Selected sprite should become selected.");
            var selectedSelectionKey = GetPrivateField<string>(renderer, "selectedLayerSelectionKey");

            object CreateDefaultSnapshot(PsdLayerPreviewRenderer.LayerInfo layer, Vector2[] localVertices, int[] triangles)
            {
                var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
                var docVertices = localVertices
                    .Select(local => PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local))
                    .ToArray();
                var uvs = localVertices
                    .Select(local => new Vector2(
                        (layer.textureRect.x + local.x) / texture.width,
                        (layer.textureRect.y + local.y) / texture.height))
                    .ToArray();

                SetPrivateField(data, "layer", layer);
                SetPrivateField(data, "texture", texture);
                SetPrivateField(data, "vertices", docVertices);
                SetPrivateField(data, "vertexUvs", uvs);
                SetPrivateField(data, "triangles", triangles);
                SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
                SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
                SetPrivateField(data, "boneWeights", Array.Empty<BoneWeight>());
                InvokePrivateMethod(renderer, "StoreDefaultGeometrySnapshot", layer, data);
                return data;
            }

            var selectedData = CreateDefaultSnapshot(
                selected,
                new[] { new Vector2(2f, 3f), new Vector2(25f, 5f), new Vector2(12f, 35f) },
                new[] { 0, 1, 2 });
            var otherData = CreateDefaultSnapshot(
                other,
                new[] { new Vector2(1f, 2f), new Vector2(18f, 4f), new Vector2(8f, 22f) },
                new[] { 0, 1, 2 });

            SetPrivateField(selectedData, "vertices", new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(selected, new Vector2(7f, 7f)) });
            SetPrivateField(otherData, "vertices", new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(9f, 9f)) });
            var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
            dataBySelectionKey[selectedSelectionKey] = selectedData;
            dataBySelectionKey[otherSelectionKey] = otherData;

            AssertEqual(1, renderer.GenerateDefaultGeometryForSpriteSheet("Assets/test.psd", out var error), $"Default geometry with a selection should generate only selected. Error={error}");
            AssertEqual(3, GetPrivateField<Vector2[]>(selectedData, "vertices").Length, "Selected sprite default geometry should be restored.");
            AssertEqual(1, GetPrivateField<Vector2[]>(otherData, "vertices").Length, "Other sprite geometry should stay untouched while selected sprite is active.");
            AssertEqual("Generate Default Geometry", undoLabels[0], "Default geometry should register an undo operation.");

            AssertEqual(2, renderer.GenerateDefaultGeometryForAllSprites("Assets/test.psd", out error), $"Forced all-sprite default geometry should ignore selection. Error={error}");
            AssertEqual(3, GetPrivateField<Vector2[]>(otherData, "vertices").Length, "Forced all-sprite default geometry should restore other sprites even while a sprite is selected.");
            AssertEqual(2, GetPrivateListCount(renderer, "autoGeometryPatches"), "Forced all-sprite default geometry should create/update both sprite patches.");

            SetPrivateField(otherData, "vertices", new[] { PsdCoordinateSpace.LayerLocalToDocumentPoint(other, new Vector2(9f, 9f)) });
            AssertTrue(renderer.SelectLayer(null), "Selection should clear.");
            AssertEqual(2, renderer.GenerateDefaultGeometryForSpriteSheet("Assets/test.psd", out error), $"Default geometry without a selection should generate for all sprites. Error={error}");
            AssertEqual(3, GetPrivateField<Vector2[]>(otherData, "vertices").Length, "Other sprite default geometry should be restored when no sprite is selected.");
            AssertEqual(2, GetPrivateListCount(renderer, "autoGeometryPatches"), "All-sprite default geometry should create/update both sprite patches.");
            AssertEqual(3, undoLabels.Count, "Each default geometry operation should register one undo entry.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SpriteSheetGeometrySelectionFiltersVisibleGeometry()
    {
        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldDisplayGeometryForSelection("layer-a", string.Empty),
            "No selected layer should show all geometry.");
        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldDisplayGeometryForSelection("layer-a", "LAYER-A"),
            "Selected matching layer should show geometry case-insensitively.");
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldDisplayGeometryForSelection("layer-b", "layer-a"),
            "Selected non-matching layer should hide geometry.");
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldDisplayGeometryForSelection(string.Empty, "layer-a"),
            "Empty layer key should not match a selected layer.");
    }

    private static void SpriteSheetDefaultGeometryUsesCurrentSpriteRect()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("default-current-rect", 78, new Vector2(40f, 50f));
        layer.sourceRect = new Rect(22f, 25f, 69f, 56f);
        layer.textureRect = new Rect(12f, 15f, 69f, 56f);
        layer.textureSamplingRect = new Rect(11f, 12.5f, 34.5f, 28f);
        layer.spriteSheetOptimizeReferenceRect = new Rect(0f, 0f, 160f, 140f);
        layer.spriteSheetOptimizeReferencePosition = new Vector2(10f, 20f);
        layer.pivot = new Vector2(0.25f, 0.75f);

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "pixelsPerUnit", 2f);

        var binding = InvokePrivateMethod(
            renderer,
            "BuildDefaultGeometryBindingForLayer",
            layer,
            data,
            new Vector2(256f, 256f));
        var rect = GetPrivateField<Rect>(binding, "rect");
        var ppu = GetPrivateField<float>(binding, "pixelsPerUnit");
        var pivot = GetPrivateField<Vector2>(binding, "pivotPixels");

        AssertRect(layer.sourceRect, rect, 0.0001f, "Default geometry generation should use the logical importer rect, not the scaled preview sampling rect.");
        AssertFloat(2f, ppu, 0.0001f, "Default geometry generation should keep existing pixels-per-unit.");
        AssertVector(new Vector2(17.25f, 42f), pivot, 0.0001f, "Default geometry generation should compute pivot inside current rect size.");

        layer.sourceRect = new Rect(365f, 815f, 306f, 381f);
        layer.textureRect = layer.sourceRect;
        layer.textureSamplingRect = new Rect(182.5f, 407.5f, 153f, 190.5f);
        binding = InvokePrivateMethod(
            renderer,
            "BuildDefaultGeometryBindingForLayer",
            layer,
            data,
            new Vector2(1024f, 2048f));
        var fallbackArgs = new object[]
        {
            layer,
            data,
            new Vector2(1024f, 2048f),
            binding,
            null,
            null
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryBuildDefaultGeometrySamplingFallbackBindingForLayer", fallbackArgs),
            "Default geometry should fall back to the physical texture sampling rect when the logical rect cannot be sampled.");
        var samplingBinding = fallbackArgs[4];
        var samplingRect = GetPrivateField<Rect>(samplingBinding, "rect");
        var samplingToLogicalScale = (Vector2)fallbackArgs[5];
        AssertRect(layer.textureSamplingRect, samplingRect, 0.0001f, "Default geometry fallback should sample the real atlas rect.");
        AssertVector(new Vector2(2f, 2f), samplingToLogicalScale, 0.0001f, "Default geometry fallback should remap physical outline pixels into logical sprite pixels.");

        var scaledVertices = (Vector2[])InvokePrivateMethod(
            renderer,
            "ScaleDefaultGeometryLocalVertices",
            new[]
            {
                new Vector2(2f, 3f),
                new Vector2(20f, 30f)
            },
            samplingToLogicalScale);
        AssertVector(new Vector2(4f, 6f), scaledVertices[0], 0.0001f, "Default geometry fallback scaled vertex 0.");
        AssertVector(new Vector2(40f, 60f), scaledVertices[1], 0.0001f, "Default geometry fallback scaled vertex 1.");
    }

    private static void SpriteSheetAutoGeometryTriangulatesDisconnectedOutlinesSeparately()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var polygons = new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(8f, 0f),
                new Vector2(8f, 8f),
                new Vector2(0f, 8f)
            },
            new List<Vector2>
            {
                new Vector2(32f, 0f),
                new Vector2(40f, 0f),
                new Vector2(40f, 8f),
                new Vector2(32f, 8f)
            }
        };
        var args = new object[] { polygons, 0, null, null };

        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryTriangulateAutoGeometryPolygons", args),
            "Auto geometry should triangulate disconnected outline islands.");
        var vertices = (Vector2[])args[2];
        var triangles = (int[])args[3];
        AssertTrue(vertices.Length >= 6, "Disconnected auto geometry should keep vertices from both islands.");
        AssertTrue(triangles.Length >= 6, "Disconnected auto geometry should keep triangles from both islands.");
        AssertTriangleIndicesValid(triangles, vertices.Length, "Disconnected auto geometry triangles");

        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]];
            var b = vertices[triangles[i + 1]];
            var c = vertices[triangles[i + 2]];
            var allLeft = a.x <= 8.001f && b.x <= 8.001f && c.x <= 8.001f;
            var allRight = a.x >= 31.999f && b.x >= 31.999f && c.x >= 31.999f;
            AssertTrue(allLeft || allRight, $"Disconnected auto geometry triangle {i / 3} should not bridge separate outline islands.");
        }
    }

    private static void SpriteSheetAutoGeometryRejectsPartialOutlineTriangulation()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var polygon = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(100f, 0f),
            new Vector2(100f, 100f),
            new Vector2(0f, 100f)
        };
        var partialVertices = new[]
        {
            new Vector2(4f, 4f),
            new Vector2(14f, 4f),
            new Vector2(4f, 14f)
        };
        var partialTriangles = new[] { 0, 1, 2 };
        var fullVertices = polygon.ToArray();
        var fullTriangles = new[] { 0, 1, 2, 0, 2, 3 };

        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "AutoGeometryTriangulationCoversPolygon", polygon, partialVertices, partialTriangles),
            "Auto geometry should reject triangulation that covers only a small part of the outline.");
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "AutoGeometryTriangulationCoversPolygon", polygon, fullVertices, fullTriangles),
            "Auto geometry should accept triangulation that covers the outline.");
    }

    private static void SpriteSheetDefaultGeometryOutlinePointsStayLayerLocal()
    {
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryBuildPolygonLocalPoints",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException("TryBuildPolygonLocalPoints not found.");

        var rect = new Rect(100f, 200f, 20f, 10f);
        var expected = new[]
        {
            new Vector2(3f, 2f),
            new Vector2(18f, 2f),
            new Vector2(18f, 9f),
            new Vector2(3f, 9f)
        };

        void AssertOutline(Vector2[] rawPath, string label, Vector2[] expectedPolygon = null)
        {
            expectedPolygon ??= expected;
            var args = new object[] { rawPath, Vector2.one, rect, null };
            AssertTrue((bool)method.Invoke(null, args), label + " should build polygon.");
            var polygon = (List<Vector2>)args[3];
            AssertEqual(expectedPolygon.Length, polygon.Count, label + " polygon count.");
            for (var i = 0; i < expectedPolygon.Length; i++)
                AssertVector(expectedPolygon[i], polygon[i], 0.0001f, label + " polygon point " + i);
        }

        AssertOutline(
            new[]
            {
                new Vector2(103f, 202f),
                new Vector2(118f, 202f),
                new Vector2(118f, 209f),
                new Vector2(103f, 209f)
            },
            "Atlas-space outline");
        AssertOutline(
            expected,
            "Local-space outline");
        AssertOutline(
            new[]
            {
                new Vector2(3f, 2f),
                new Vector2(8f, 2f),
                new Vector2(8f, 5f),
                new Vector2(3f, 5f)
            },
            "Small local-space outline near rect edge",
            new[]
            {
                new Vector2(3f, 2f),
                new Vector2(8f, 2f),
                new Vector2(8f, 5f),
                new Vector2(3f, 5f)
            });
        AssertOutline(
            new[]
            {
                new Vector2(-7f, -3f),
                new Vector2(8f, -3f),
                new Vector2(8f, 4f),
                new Vector2(-7f, 4f)
            },
            "Centered outline");
    }

    private static void SpriteSheetDefaultGeometryClampsGeneratedOutlineToRect()
    {
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryBuildPolygonLocalPoints",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException("TryBuildPolygonLocalPoints not found.");

        var rect = new Rect(100f, 200f, 20f, 10f);
        var rawPath = new[]
        {
            new Vector2(-33f, -22f),
            new Vector2(153f, -22f),
            new Vector2(153f, 333f),
            new Vector2(-33f, 333f)
        };

        var args = new object[] { rawPath, Vector2.one, rect, null };
        AssertTrue((bool)method.Invoke(null, args), "Out-of-rect generated outline should still build polygon.");
        var polygon = (List<Vector2>)args[3];
        var expected = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(20f, 0f),
            new Vector2(20f, 10f),
            new Vector2(0f, 10f)
        };
        AssertEqual(expected.Length, polygon.Count, "Clamped generated outline polygon count.");
        for (var i = 0; i < expected.Length; i++)
            AssertVector(expected[i], polygon[i], 0.0001f, "Clamped generated outline polygon point " + i);
    }

    private static void SpriteSheetDefaultGeometryUsesOneOutlineSpaceForMultipleIslands()
    {
        var outlineSpaceType = typeof(PsdLayerPreviewRenderer).GetNestedType("GeneratedOutlineSpace", BindingFlags.NonPublic);
        if (outlineSpaceType == null)
            throw new InvalidOperationException("GeneratedOutlineSpace not found.");

        var resolveMethod = typeof(PsdLayerPreviewRenderer).GetMethod(
            "ResolveGeneratedOutlineSpace",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(IReadOnlyList<Vector2[]>), typeof(Vector2), typeof(Rect) },
            null);
        var buildMethod = typeof(PsdLayerPreviewRenderer).GetMethod(
            "TryBuildPolygonLocalPoints",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[]
            {
                typeof(IReadOnlyList<Vector2>),
                typeof(Vector2),
                typeof(Rect),
                typeof(Nullable<>).MakeGenericType(outlineSpaceType),
                typeof(List<Vector2>).MakeByRefType()
            },
            null);
        if (resolveMethod == null || buildMethod == null)
            throw new InvalidOperationException("Multi-island outline helpers not found.");

        var rect = new Rect(100f, 200f, 200f, 200f);
        var largeIsland = new[]
        {
            new Vector2(120f, 210f),
            new Vector2(290f, 210f),
            new Vector2(290f, 250f),
            new Vector2(120f, 250f)
        };
        var smallIsland = new[]
        {
            new Vector2(265f, 350f),
            new Vector2(295f, 350f),
            new Vector2(295f, 380f),
            new Vector2(265f, 380f)
        };
        IReadOnlyList<Vector2[]> paths = new[] { largeIsland, smallIsland };

        var sharedSpace = resolveMethod.Invoke(null, new object[] { paths, Vector2.one, rect });
        var nullableSharedSpace = Activator.CreateInstance(
            typeof(Nullable<>).MakeGenericType(outlineSpaceType),
            sharedSpace);
        var args = new object[]
        {
            (IReadOnlyList<Vector2>)smallIsland,
            Vector2.one,
            rect,
            nullableSharedSpace,
            null
        };

        AssertTrue((bool)buildMethod.Invoke(null, args), "Small island should build with shared outline space.");
        var polygon = (List<Vector2>)args[4];
        AssertEqual(4, polygon.Count, "Small island polygon count.");
        AssertVector(new Vector2(165f, 150f), polygon[0], 0.0001f, "Small island should keep atlas-relative position.");
        AssertVector(new Vector2(195f, 180f), polygon[2], 0.0001f, "Small island opposite point.");
    }

    private static void SpriteSheetRectDragUndoRegistersOnce()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(1024, 2048);
        var undoLabels = new List<string>();
        renderer.UndoRequest = undoLabels.Add;
        try
        {
            InvokePrivateMethod(renderer, "RegisterSpriteRectDragUndoOnce");
            InvokePrivateMethod(renderer, "RegisterSpriteRectDragUndoOnce");
            AssertEqual(1, undoLabels.Count, "Sprite rect drag should register undo once while active.");
            AssertEqual("Edit Sprite Rect", undoLabels[0], "Sprite rect drag undo label.");
            AssertTrue(GetPrivateField<bool>(renderer, "spriteRectDragUndoRegistered"), "Sprite rect undo flag should be set.");

            InvokePrivateMethod(renderer, "EndSpriteRectDrag");
            AssertFalse(GetPrivateField<bool>(renderer, "spriteRectDragUndoRegistered"), "Sprite rect undo flag should reset when drag ends.");

            InvokePrivateMethod(renderer, "RegisterSpriteRectDragUndoOnce");
            AssertEqual(2, undoLabels.Count, "A new sprite rect drag should register a new undo entry.");

            var upperAtlasLayer = BuildLayer("upper-atlas", 849, Vector2.zero);
            upperAtlasLayer.sourceRect = new Rect(4f, 3814f, 325f, 267f);
            upperAtlasLayer.textureRect = new Rect(2f, 1907f, 162.5f, 133.5f);
            SetPrivateField(renderer, "cachedTexture", texture);
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { upperAtlasLayer });
            AssertEqual(
                new Vector2Int(1024, 4081),
                (Vector2Int)InvokePrivateMethod(renderer, "GetPreviewAtlasSize"),
                "Sprite rect drag bounds should use the logical atlas extent instead of the half-scale preview texture.");

            var inputSource = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath,
                "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.BoneSpriteSelection.cs"));
            AssertTrue(
                inputSource.Contains("spriteRectDragStartRect = hitLayer.sourceRect;"),
                "Sprite rect drag should start from sourceRect, the same rect SetLayerSpriteRect edits.");
            AssertFalse(
                inputSource.Contains("var nextKey = hitLayer != null ? BuildSelectionKey(hitLayer) : string.Empty;"),
                "Sprite atlas empty clicks should not clear selectedLayerSelectionKey and make tools target every rect.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

}
#endif
