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
    private static void SpriteSheetDataProviderSpriteRectSyncsOptimizedState()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("synced-sprite", 79, new Vector2(100f, 205f));
        layer.sourceRect = new Rect(22f, 25f, 69f, 56f);
        layer.textureRect = layer.sourceRect;
        layer.pivot = new Vector2(0.25f, 0.75f);
        layer.alignment = (int)SpriteAlignment.Custom;
        layer.border = new Vector4(1f, 2f, 3f, 4f);

        var spriteRect = new SpriteRect
        {
            name = "old-sprite",
            rect = new Rect(10f, 20f, 100f, 80f),
            pivot = new Vector2(0.5f, 0.5f),
            alignment = SpriteAlignment.Center,
            border = Vector4.zero
        };

        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "SyncSpriteRectWithLayerIfChanged", spriteRect, layer),
            "Data provider SpriteRect sync should report changed for optimized state.");
        AssertEqual(layer.name, spriteRect.name, "SpriteRect name should sync.");
        AssertRect(layer.sourceRect, spriteRect.rect, 0.0001f, "SpriteRect rect should sync.");
        AssertVector(layer.pivot, spriteRect.pivot, 0.0001f, "SpriteRect pivot should sync.");
        AssertEqual((SpriteAlignment)layer.alignment, spriteRect.alignment, "SpriteRect alignment should sync.");
        AssertVector(layer.border, spriteRect.border, 0.0001f, "SpriteRect border should sync.");

        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "SyncSpriteRectWithLayerIfChanged", spriteRect, layer),
            "Unchanged data provider SpriteRect should not report changed.");
    }

    private static void SpriteSheetImporterUvTransformSyncsOptimizedRect()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            var layer = BuildLayer("uv-synced-sprite", 80, new Vector2(326f, 646f));
            var optimizedRect = new Rect(4f, 1294f, 341f, 309f);
            var stalePreviewRect = new Rect(4f, 1306f, 341f, 309f);
            layer.sourceRect = optimizedRect;
            layer.textureRect = layer.sourceRect;

            fixture.spriteLike = new SpriteImportDataLike
            {
                rect = optimizedRect,
                uvTransform = stalePreviewRect.position,
                spritePosition = layer.position
            };
            fixture.psdLayerLike = new PsdLayerDataLike
            {
                mosaicPosition = stalePreviewRect.position
            };

            var so = new SerializedObject(fixture);
            var spriteProp = so.FindProperty("spriteLike");
            var psdLayerProp = so.FindProperty("psdLayerLike");
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteUvTransformWithLayerIfChanged", spriteProp, layer),
                "UV transform sync should report changed when importer UV uses the old rect position.");
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteMosaicPositionWithLayerIfChanged", psdLayerProp, layer, optimizedRect.position),
                "Mosaic position sync should report changed when PSD layer uses the old rect position.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertVector(optimizedRect.position, fixture.spriteLike.uvTransform, 0.0001f, "UV transform should match the serialized optimized rect position.");
            AssertVector(optimizedRect.position, fixture.psdLayerLike.mosaicPosition, 0.0001f, "Mosaic position should match the serialized optimized rect position.");
            AssertVector(layer.position, fixture.spriteLike.spritePosition, 0.0001f, "UV sync should not change document sprite position.");

            so.Update();
            AssertFalse(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteUvTransformWithLayerIfChanged", so.FindProperty("spriteLike"), layer),
                "Unchanged UV transform should not report changed.");
            AssertFalse(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteMosaicPositionWithLayerIfChanged", so.FindProperty("psdLayerLike"), layer, optimizedRect.position),
                "Unchanged mosaic position should not report changed.");

            fixture.spriteLike.uvTransform = stalePreviewRect.position;
            fixture.psdLayerLike.mosaicPosition = stalePreviewRect.position;
            so.Update();
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteUvTransformIfChanged", so.FindProperty("spriteLike"), optimizedRect.position),
                "UV transform sync should not require a live preview layer when repairing serialized importer state.");
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncSpriteMosaicPositionIfChanged", so.FindProperty("psdLayerLike"), optimizedRect.position),
                "Mosaic sync should not require a live preview layer when repairing serialized importer state.");
            so.ApplyModifiedPropertiesWithoutUndo();
            AssertVector(optimizedRect.position, fixture.spriteLike.uvTransform, 0.0001f, "Layer-free UV repair should match serialized rect position.");
            AssertVector(optimizedRect.position, fixture.psdLayerLike.mosaicPosition, 0.0001f, "Layer-free mosaic repair should match serialized rect position.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteSheetImporterAtlasRepairSyncsPsdLayerCache()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.rigPSDLayers = new[]
            {
                new PsdLayerImportDataLike
                {
                    name = "l_arm",
                    spriteID = "b70b4b71b72405547ac7a70b9effee4f",
                    mosaicPosition = new Vector2(4f, 460f)
                }
            };

            var so = new SerializedObject(fixture);
            var desiredAtlasPositions = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase)
            {
                ["b70b4b71b72405547ac7a70b9effee4f"] = new Vector2(53f, 538f)
            };

            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncPsdLayerMosaicPositionsToSpriteImportData", so, desiredAtlasPositions),
                "Repair should update stale PSD layer mosaic cache to the same atlas position as the sprite import rect.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertVector(
                new Vector2(53f, 538f),
                fixture.rigPSDLayers[0].mosaicPosition,
                0.0001f,
                "PSD layer mosaic position should match the repaired sprite import rect position.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteSheetApplyPreservesPsdLayerMosaicAnchor()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            const string spriteName = "cl_hoody_r_arm";
            const string spriteGuid = "7ec20e15d59bd4d44838f8d9d4aaa087";
            var staleAtlasRect = new Rect(365f, 1514f, 120f, 160f);
            var optimizedRect = new Rect(182f, 758f, 120f, 160f);
            var layer = BuildLayer(spriteName, 10, new Vector2(772f, 1214f));
            layer.spriteGuid = spriteGuid;
            layer.sourceRect = staleAtlasRect;
            layer.textureRect = staleAtlasRect;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

            AssertTrue(renderer.SetLayerSpriteRect(layer, optimizedRect), "Fixture setup should mark the optimized sprite rect dirty.");

            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = spriteName,
                    rect = staleAtlasRect,
                    spriteID = spriteGuid,
                    spritePosition = layer.position
                }
            };
            fixture.rigPSDLayers = new[]
            {
                new PsdLayerImportDataLike
                {
                    name = spriteName,
                    spriteID = spriteGuid,
                    mosaicPosition = staleAtlasRect.position
                }
            };

            var so = new SerializedObject(fixture);
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "ApplySpriteChangesToSerializedImporter", so, null),
                "Normal Apply should persist the optimized sprite rect.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertRect(
                optimizedRect,
                fixture.rigSpriteImportData[0].rect,
                0.0001f,
                "Apply should persist the optimized sprite import rect.");
            AssertVector(
                staleAtlasRect.position,
                fixture.rigPSDLayers[0].mosaicPosition,
                0.0001f,
                "Apply must preserve the PSD layer packing anchor so reimport keeps the optimized rect offset.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteSheetImporterRepairRequestsPsdReslice()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.resliceFromLayer = false;
            fixture.spriteSizeExpandChanged = 0;

            var so = new SerializedObject(fixture);
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "RequestPsdLayerReslice", so),
                "Repairing stale PSD importer rects should ask PSDImporter to reslice from layer data on reimport.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertTrue(fixture.resliceFromLayer, "resliceFromLayer should be requested.");
            AssertEqual(1, fixture.spriteSizeExpandChanged, "spriteSizeExpandChanged should be requested for integer-backed importer fields.");

            so.Update();
            AssertFalse(
                (bool)InvokePrivateMethod(renderer, "RequestPsdLayerReslice", so),
                "Requesting reslice twice should be idempotent.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteSheetImporterRepairSyncsCharacterDataAfterReslice()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "l_arm",
                    rect = new Rect(4f, 460f, 382f, 262f),
                    spriteID = "b70b4b71b72405547ac7a70b9effee4f",
                    spritePosition = new Vector2(150f, 78f)
                }
            };
            fixture.characterData = new CharacterDataLike
            {
                parts = new[]
                {
                    new CharacterPartLike
                    {
                        spriteId = "b70b4b71b72405547ac7a70b9effee4f",
                        spritePosition = new RectInt(248, 234, 333, 151)
                    }
                }
            };

            var so = new SerializedObject(fixture);
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "SyncCharacterPartRectsFromCurrentSpriteImportData", so),
                "Repair finalization should update character parts from the current resliced sprite import data.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(
                new RectInt(150, 78, 382, 262),
                fixture.characterData.parts[0].spritePosition,
                "CharacterData part bounds should match current spritePosition and rect size.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void PsdPostImportSyncKeepsOptimizedSpriteImportData()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "l_arm",
                    rect = new Rect(4f, 460f, 382f, 262f),
                    spriteID = "b70b4b71b72405547ac7a70b9effee4f",
                    spritePosition = new Vector2(150f, 78f)
                }
            };
            fixture.characterData = new CharacterDataLike
            {
                parts = new[]
                {
                    new CharacterPartLike
                    {
                        spriteId = "b70b4b71b72405547ac7a70b9effee4f",
                        spritePosition = new RectInt(199, 156, 333, 151)
                    }
                }
            };

            var so = new SerializedObject(fixture);
            AssertTrue(
                ReanimatorPsdSpritePositionSync.SyncCharacterDataFromSpriteImportData(so, fixture),
                "Passive PSD import sync should repair stale CharacterData from current sprite import data.");

            AssertEqual(
                new RectInt(150, 78, 382, 262),
                fixture.characterData.parts[0].spritePosition,
                "CharacterData should match optimized sprite import data after passive import sync.");
            AssertRect(
                new Rect(4f, 460f, 382f, 262f),
                fixture.rigSpriteImportData[0].rect,
                0.0001f,
                "Passive import sync must not move optimized sprite import rects back to stale CharacterData.");
            AssertVector(
                new Vector2(150f, 78f),
                fixture.rigSpriteImportData[0].spritePosition,
                0.0001f,
                "Passive import sync must not move optimized sprite import position back to stale CharacterData.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteListResolverSkipsEmptyMosaicData()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.mosaicLayers = true;
            fixture.mosaicSpriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.spriteImportData = new[]
            {
                new SpriteImportDataRecordLike()
            };
            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "real_sprite",
                    rect = new Rect(4f, 8f, 32f, 64f),
                    spriteID = "real-sprite-guid",
                    spritePosition = new Vector2(10f, 20f)
                }
            };

            var so = new SerializedObject(fixture);
            var resolved = (SerializedProperty)InvokePrivateMethod(renderer, "ResolveSpriteList", so);
            AssertTrue(resolved != null, "Sprite list resolver should return a list.");
            AssertEqual("rigSpriteImportData", resolved.propertyPath, "Empty mosaic data must not hide populated rig sprite data.");
            AssertEqual(1, resolved.arraySize, "Resolved rig sprite data should expose the real sprite entries.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteListResolverUsesLayeredSpriteImportData()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.mosaicLayers = true;
            fixture.mosaicSpriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.rigSpriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.sharedRigSpriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.spriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.layeredSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "layered_sprite",
                    rect = new Rect(12f, 16f, 48f, 72f),
                    spriteID = "layered-sprite-guid",
                    spritePosition = new Vector2(20f, 30f)
                }
            };

            var so = new SerializedObject(fixture);
            var resolved = (SerializedProperty)InvokePrivateMethod(renderer, "ResolveSpriteList", so);
            AssertTrue(resolved != null, "Sprite list resolver should return a list for Unity 6 layered PSD data.");
            AssertEqual("layeredSpriteImportData", resolved.propertyPath, "Unity 6 layered sprite data should be a valid sprite list fallback.");
            AssertEqual(1, resolved.arraySize, "Resolved layered sprite data should expose the real sprite entries.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SpriteListResolverPrefersLayeredOverStaleLegacyData()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.characterMode = true;
            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "back merged",
                    rect = new Rect(4f, 4f, 4096f, 4096f),
                    spriteID = "merged-guid",
                    spritePosition = Vector2.zero
                }
            };
            fixture.spriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "back merged",
                    rect = new Rect(4f, 4f, 4096f, 4096f),
                    spriteID = "merged-guid",
                    spritePosition = Vector2.zero
                }
            };
            fixture.layeredSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "back merged",
                    rect = new Rect(4f, 4f, 2048f, 2048f),
                    spriteID = "merged-guid",
                    spritePosition = Vector2.zero
                }
            };

            var so = new SerializedObject(fixture);
            var resolved = (SerializedProperty)InvokePrivateMethod(renderer, "ResolveSpriteList", so);
            AssertTrue(resolved != null, "Sprite list resolver should return a list when layered and legacy data both exist.");
            AssertEqual("layeredSpriteImportData", resolved.propertyPath, "Unity 6 layered sprite data should win over stale legacy rig/sprite import data.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void OldEditorMetadataStageMirrorsLayeredSpriteWeightsToLegacyLists()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.characterMode = true;
            fixture.rigSpriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.spriteImportData = Array.Empty<SpriteImportDataRecordLike>();
            fixture.layeredSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "weighted_layer",
                    rect = new Rect(12f, 16f, 48f, 72f),
                    spriteID = "layered-weighted-guid",
                    spritePosition = new Vector2(20f, 30f),
                    vertices = new[]
                    {
                        new Vertex2DMetaDataLike
                        {
                            position = new Vector2(2f, 3f),
                            boneWeight = new BoneWeight
                            {
                                boneIndex0 = 1,
                                weight0 = 0.75f,
                                boneIndex1 = 2,
                                weight1 = 0.25f
                            }
                        }
                    }
                }
            };

            var so = new SerializedObject(fixture);
            AssertTrue(
                ReanimatorOldEditorMetadataStager.StageLegacySpriteImportDataFromCurrentLists(so),
                "Old editor compatibility stage should mirror populated layered sprite data into legacy lists.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(1, fixture.rigSpriteImportData.Length, "Legacy rig sprite data should receive the layered sprite.");
            AssertEqual(1, fixture.spriteImportData.Length, "Legacy sprite data should receive the layered sprite.");
            AssertEqual("weighted_layer", fixture.rigSpriteImportData[0].name, "Legacy rig sprite name should match layered data.");
            AssertEqual(1, fixture.rigSpriteImportData[0].vertices.Length, "Legacy rig sprite vertices should be copied.");
            AssertFloat(
                0.75f,
                fixture.rigSpriteImportData[0].vertices[0].boneWeight.weight0,
                0.0001f,
                "Legacy rig sprite vertex weight0 should match layered data.");
            AssertEqual(
                2,
                fixture.spriteImportData[0].vertices[0].boneWeight.boneIndex1,
                "Legacy sprite data should preserve secondary bone identity.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void OldEditorMetadataStagePreservesSpriteImportPlaceholder()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.characterMode = true;
            fixture.rigSpriteImportData = new[]
            {
                new SpriteImportDataRecordLike
                {
                    name = "head",
                    rect = new Rect(12f, 16f, 48f, 72f),
                    spriteID = "head-guid",
                    spritePosition = new Vector2(20f, 30f)
                }
            };
            fixture.spriteImportData = new[]
            {
                new SpriteImportDataRecordLike()
            };

            var so = new SerializedObject(fixture);
            AssertFalse(
                ReanimatorOldEditorMetadataStager.StageLegacySpriteImportDataFromCurrentLists(so),
                "Old editor compatibility stage should preserve a single empty spriteImportData placeholder.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(1, fixture.rigSpriteImportData.Length, "Rig sprite data should keep the real sprite list.");
            AssertEqual("head", fixture.rigSpriteImportData[0].name, "Rig sprite data should remain unchanged.");
            AssertEqual(1, fixture.spriteImportData.Length, "Legacy spriteImportData placeholder should not expand into the real sprite list.");
            AssertTrue(string.IsNullOrEmpty(fixture.spriteImportData[0].name), "Legacy spriteImportData placeholder should keep its empty name.");
            AssertEqual(Rect.zero, fixture.spriteImportData[0].rect, "Legacy spriteImportData placeholder should keep its zero rect.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void LayerSpriteBindingResolverPrefersSpriteGuid()
    {
        var texture = new Texture2D(64, 64);
        var first = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), Vector2.zero);
        var target = Sprite.Create(texture, new Rect(32f, 0f, 16f, 16f), Vector2.zero);
        try
        {
            first.name = "duplicate";
            target.name = "duplicate";

            var layer = BuildLayer("duplicate", 501, Vector2.zero);
            layer.sourceRect = first.rect;
            layer.spriteGuid = "22222222222222222222222222222222";

            var sprites = new List<Sprite> { first, target };
            var indexByGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["22222222222222222222222222222222"] = 1
            };

            var resolvedByGuid = ReanimatorLayerSpriteBindingResolver.ResolveSpriteIndex(
                sprites,
                new[] { false, false },
                indexByGuid,
                layer);
            AssertEqual(1, resolvedByGuid, "Layer sprite resolver should prefer the layer sprite GUID before name/rect fallback.");

            layer.spriteGuid = string.Empty;
            var resolvedByFallback = ReanimatorLayerSpriteBindingResolver.ResolveSpriteIndex(
                sprites,
                new[] { false, false },
                indexByGuid,
                layer);
            AssertEqual(0, resolvedByFallback, "Layer sprite resolver should still fall back to name/rect when a layer has no GUID.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void DirtyLayerLookupIgnoresNameFallback()
    {
        var dirty = BuildLayer("duplicate", 701, new Vector2(5f, 7f));
        dirty.spriteGuid = string.Empty;
        dirty.sourceRect = new Rect(1f, 2f, 11f, 13f);
        var duplicate = BuildLayer("duplicate", 702, new Vector2(20f, 30f));
        duplicate.spriteGuid = string.Empty;
        duplicate.sourceRect = new Rect(3f, 4f, 17f, 19f);
        var unique = BuildLayer("unique", 0, new Vector2(40f, 50f));
        unique.spriteGuid = string.Empty;
        unique.sourceRect = new Rect(6f, 8f, 23f, 29f);

        var lookup = ReanimatorDirtyLayerLookupBuilder.Build(
            new[] { dirty, duplicate, unique },
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "701", "702", "703" });

        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.spriteImportData = new[]
            {
                new SpriteImportDataRecordLike { name = "duplicate" },
                new SpriteImportDataRecordLike { name = "unique" }
            };

            var so = new SerializedObject(fixture);
            var spriteList = so.FindProperty("spriteImportData");
            var duplicateElement = spriteList.GetArrayElementAtIndex(0);
            var uniqueElement = spriteList.GetArrayElementAtIndex(1);

            AssertEqual(
                null,
                ReanimatorDirtyLayerLookupBuilder.ResolveFromSerializedSprite(duplicateElement, lookup, string.Empty),
                "Dirty lookup must not resolve duplicate sprite names by name fallback.");
            AssertEqual(
                null,
                ReanimatorDirtyLayerLookupBuilder.ResolveFromSerializedSprite(uniqueElement, lookup, string.Empty),
                "Dirty lookup must not resolve unique sprite names by name fallback after PSD structure changes.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

}
#endif
