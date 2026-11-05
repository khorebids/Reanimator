#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void BoneHierarchyResolvesWorldAndLocalTransforms()
    {
        var rootRot = Quaternion.AngleAxis(90f, Vector3.forward);
        var childRot = Quaternion.AngleAxis(45f, Vector3.forward);
        var localBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, new Vector2(10f, 5f), rootRot),
            BuildBone("child", 0, new Vector2(2f, 0f), childRot),
            BuildBone("tip", 1, new Vector2(0f, 1f), Quaternion.identity)
        };

        var localTransform = new BonePreviewTransform(false, true);
        localTransform.ResolveAll(localBones);

        AssertVector(new Vector2(10f, 5f), localBones[0].worldPosition, 0.0001f, "Root world position.");
        AssertQuaternion(rootRot, localBones[0].worldRotation, 0.0001f, "Root world rotation.");
        AssertVector(new Vector2(10f, 7f), localBones[1].worldPosition, 0.0001f, "Child world position should rotate local offset by parent.");
        AssertQuaternion(rootRot * childRot, localBones[1].worldRotation, 0.0001f, "Child world rotation.");

        var expectedTipOffset = (rootRot * childRot) * new Vector3(0f, 1f, 0f);
        AssertVector(new Vector2(10f + expectedTipOffset.x, 7f + expectedTipOffset.y), localBones[2].worldPosition, 0.0001f, "Grandchild world position.");
        AssertQuaternion(rootRot * childRot, localBones[2].worldRotation, 0.0001f, "Grandchild world rotation.");

        var worldBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, new Vector2(10f, 5f), rootRot),
            BuildBone("child", 0, new Vector2(10f, 7f), rootRot * childRot)
        };
        localTransform.ConvertWorldToLocal(worldBones);
        AssertVector(new Vector2(10f, 5f), worldBones[0].position, 0.0001f, "Converted root local position.");
        AssertVector(new Vector2(2f, 0f), worldBones[1].position, 0.0001f, "Converted child local position.");
        AssertQuaternion(childRot, worldBones[1].rotation, 0.0001f, "Converted child local rotation.");

        var worldModeBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, new Vector2(1f, 1f), rootRot),
            BuildBone("child", 0, new Vector2(4f, 5f), childRot)
        };
        new BonePreviewTransform(true, true).ResolveAll(worldModeBones);
        AssertVector(new Vector2(4f, 5f), worldModeBones[1].worldPosition, 0.0001f, "World-space mode should not apply parent offset.");
        AssertQuaternion(childRot, worldModeBones[1].worldRotation, 0.0001f, "World-space mode should not apply parent rotation.");
    }

    private static void BonePreviewTransformRespectsVisitedSingleResolve()
    {
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, new Vector2(10f, 5f), Quaternion.AngleAxis(90f, Vector3.forward)),
            BuildBone("child", 0, new Vector2(2f, 0f), Quaternion.AngleAxis(45f, Vector3.forward))
        };

        var child = bones[1];
        child.worldPosition = new Vector2(99f, 88f);
        child.worldRotation = Quaternion.AngleAxis(12f, Vector3.forward);
        bones[1] = child;

        var visited = new[] { false, true };
        new BonePreviewTransform(false, true).ResolveSingle(bones, 1, visited);

        AssertFalse(visited[0], "Unresolved parent visited flag should stay false.");
        AssertTrue(visited[1], "Visited child flag should stay true.");
        AssertVector(new Vector2(99f, 88f), bones[1].worldPosition, 0.0001f, "Visited child world position should not change.");
        AssertQuaternion(Quaternion.AngleAxis(12f, Vector3.forward), bones[1].worldRotation, 0.0001f, "Visited child world rotation should not change.");
    }

    private static void BonePreviewTransformResolvesParentCyclesDefensively()
    {
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("a", 1, new Vector2(1f, 2f), Quaternion.identity),
            BuildBone("b", 0, new Vector2(3f, 4f), Quaternion.identity)
        };

        new BonePreviewTransform(false, true).ResolveAll(bones);

        AssertVector(new Vector2(1f, 2f), bones[0].worldPosition, 0.0001f, "Cycle fallback should keep first bone raw world position.");
        AssertQuaternion(Quaternion.identity, bones[0].worldRotation, 0.0001f, "Cycle fallback should keep first bone raw world rotation.");
        AssertVector(new Vector2(4f, 6f), bones[1].worldPosition, 0.0001f, "Cycle child should resolve from fallback parent.");
        AssertQuaternion(Quaternion.identity, bones[1].worldRotation, 0.0001f, "Cycle child rotation should resolve from fallback parent.");
    }

    private static void BonePreviewTransformConvertsNestedWorldChainToLocal()
    {
        var rootRot = Quaternion.AngleAxis(90f, Vector3.forward);
        var childRot = Quaternion.AngleAxis(45f, Vector3.forward);
        var tipRot = Quaternion.AngleAxis(-30f, Vector3.forward);
        var localBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, new Vector2(10f, 5f), rootRot),
            BuildBone("child", 0, new Vector2(2f, 0f), childRot),
            BuildBone("tip", 1, new Vector2(0f, 1f), tipRot)
        };

        var transform = new BonePreviewTransform(false, true);
        transform.ResolveAll(localBones);
        var worldBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildBone("root", -1, localBones[0].worldPosition, localBones[0].worldRotation),
            BuildBone("child", 0, localBones[1].worldPosition, localBones[1].worldRotation),
            BuildBone("tip", 1, localBones[2].worldPosition, localBones[2].worldRotation)
        };

        transform.ConvertWorldToLocal(worldBones);

        AssertVector(new Vector2(10f, 5f), worldBones[0].position, 0.0001f, "Nested root local position.");
        AssertQuaternion(rootRot, worldBones[0].rotation, 0.0001f, "Nested root local rotation.");
        AssertVector(new Vector2(2f, 0f), worldBones[1].position, 0.0001f, "Nested child local position.");
        AssertQuaternion(childRot, worldBones[1].rotation, 0.0001f, "Nested child local rotation.");
        AssertVector(new Vector2(0f, 1f), worldBones[2].position, 0.0001f, "Nested tip local position.");
        AssertQuaternion(tipRot, worldBones[2].rotation, 0.0001f, "Nested tip local rotation.");
    }

    private static void PivotPresetsMapToNormalizedPositions()
    {
        AssertVector(new Vector2(0.5f, 0.5f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.Center), 0.0001f, "Center pivot.");
        AssertVector(new Vector2(0f, 1f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.TopLeft), 0.0001f, "Top-left pivot.");
        AssertVector(new Vector2(0.5f, 1f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.TopCenter), 0.0001f, "Top-center pivot.");
        AssertVector(new Vector2(1f, 1f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.TopRight), 0.0001f, "Top-right pivot.");
        AssertVector(new Vector2(0f, 0.5f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.LeftCenter), 0.0001f, "Left-center pivot.");
        AssertVector(new Vector2(1f, 0.5f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.RightCenter), 0.0001f, "Right-center pivot.");
        AssertVector(new Vector2(0f, 0f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.BottomLeft), 0.0001f, "Bottom-left pivot.");
        AssertVector(new Vector2(0.5f, 0f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.BottomCenter), 0.0001f, "Bottom-center pivot.");
        AssertVector(new Vector2(1f, 0f), ReanimatorPivotAlgorithms.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.BottomRight), 0.0001f, "Bottom-right pivot.");
        AssertVector(new Vector2(0.5f, 0.5f), PsdLayerPreviewRenderer.GetPresetNormalizedPosition(PsdLayerPreviewRenderer.PivotPreset.RootBonePosition), 0.0001f, "Fallback pivot through public API.");
    }

    private static void BoneMetaParsesInlineVectorsAndPackedColors()
    {
        var parsed = ReanimatorBoneMetaAlgorithms.ParseVector2FromInlineMap("position: {x: 12.5, y: -3.25}");
        AssertVector(new Vector2(12.5f, -3.25f), parsed, 0.0001f, "Inline vector.");
        AssertVector(Vector2.zero, ReanimatorBoneMetaAlgorithms.ParseVector2FromInlineMap("position: {x: 1}"), 0.0001f, "Incomplete vector should fall back.");
        var parsedWithOrder = ReanimatorBoneMetaAlgorithms.ParseVector3FromInlineMap("position: {x: 12.5, y: -3.25, z: 17}");
        AssertVector(new Vector2(12.5f, -3.25f), parsedWithOrder, 0.0001f, "Inline ordered vector xy.");
        AssertFloat(17f, parsedWithOrder.z, 0.0001f, "Inline ordered vector z.");

        var fallback = new Color(0.1f, 0.2f, 0.3f, 0.75f);
        AssertTrue(ReanimatorBoneMetaAlgorithms.TryParseUnityPackedColor("673059850", fallback, out var color), "Packed color should parse.");
        AssertFloat(10f / 255f, color.r, 0.0001f, "Packed color r.");
        AssertFloat(20f / 255f, color.g, 0.0001f, "Packed color g.");
        AssertFloat(30f / 255f, color.b, 0.0001f, "Packed color b.");
        AssertFloat(40f / 255f, color.a, 0.0001f, "Packed color a.");

        AssertTrue(ReanimatorBoneMetaAlgorithms.TryParseUnityPackedColor("197121", fallback, out var zeroAlphaColor), "Zero alpha packed color should parse.");
        AssertFloat(fallback.a, zeroAlphaColor.a, 0.0001f, "Zero alpha should use fallback alpha.");
        AssertFalse(ReanimatorBoneMetaAlgorithms.TryParseUnityPackedColor("not-a-number", fallback, out var invalid), "Invalid packed color should fail.");
        AssertFloat(fallback.a, invalid.a, 0.0001f, "Invalid packed color should keep fallback.");
    }

    private static void BoneOrderSortsPreviewTrianglesLikeUnity()
    {
        var high = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        var low = new BoneWeight { boneIndex0 = 1, weight0 = 1f };
        var triangles = new[] { 0, 1, 2, 3, 4, 5 };
        var sorted = ReanimatorBoneOrderAlgorithms.SortTrianglesByBoneOrder(
            triangles,
            new[] { high, high, high, low, low, low },
            new[] { 1, 0 },
            new[] { -5, 20 });

        AssertArray(new[] { 3, 4, 5, 0, 1, 2 }, sorted, "Lower weighted bone order should draw before higher order.");
        AssertArray(new[] { 0, 1, 2, 3, 4, 5 }, triangles, "Preview sorting must not mutate source geometry.");
    }

    private static void BoneOrderPersistsInSpriteAndCharacterBoneZ()
    {
        var spriteBones = new[]
        {
            new SpriteBone { name = "high", position = new Vector3(1f, 2f, 99f) },
            new SpriteBone { name = "low", position = new Vector3(3f, 4f, 99f) }
        };
        var ordered = ReanimatorBoneOrderAlgorithms.ApplyRendererBoneOrders(
            spriteBones,
            new[] { 1, 0 },
            new[] { -8, 13 },
            out var changed);

        AssertTrue(changed, "Sprite bone order write should report changed z values.");
        AssertVector(new Vector2(1f, 2f), ordered[0].position, 0.0001f, "Sprite bone order write should preserve xy.");
        AssertFloat(0.13f, ordered[0].position.z, 0.0001f, "Mapped high sprite bone z.");
        AssertFloat(-0.08f, ordered[1].position.z, 0.0001f, "Mapped low sprite bone z.");
        AssertEqual(13, ReanimatorBoneOrderAlgorithms.ReadBoneOrder(ordered[0].position), "Scaled sprite bone z should decode to its renderer order.");
        AssertEqual(-8, ReanimatorBoneOrderAlgorithms.ReadBoneOrder(ordered[1].position), "Negative scaled sprite bone z should decode to its renderer order.");
        AssertEqual(17, ReanimatorBoneOrderAlgorithms.ReadBoneOrder(new Vector3(0f, 0f, 17f)), "Legacy integer sprite bone z should remain readable during migration.");

        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.characterData = new CharacterDataLike
            {
                bones = new[]
                {
                    new SpriteBoneLike
                    {
                        name = "root",
                        parentId = -1,
                        position = new Vector3(10f, 20f, 77f),
                        rotation = Quaternion.identity,
                        length = 5f,
                        color = Color.white
                    }
                }
            };
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "root",
                    parentId = -1,
                    position = new Vector2(10f, 20f),
                    rotation = Quaternion.identity,
                    length = 5f,
                    depth = -11,
                    color = Color.white
                }
            });

            var serializedObject = new SerializedObject(fixture);
            AssertTrue(
                (bool)InvokePrivateMethod(renderer, "ApplyBoneFieldsToCharacterData", serializedObject.FindProperty("characterData")),
                "Character bone Apply should persist changed order.");
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            AssertVector(new Vector2(10f, 20f), fixture.characterData.bones[0].position, 0.0001f, "Character bone Apply should preserve xy.");
            AssertFloat(-0.11f, fixture.characterData.bones[0].position.z, 0.0001f, "Character bone Apply should write scaled order to z.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void BoneUtilityExtractsCharacterBonesAndParts()
    {
        var lines = BuildCharacterMetaLines();
        AssertTrue(ReanimatorBoneUtilityAlgorithms.TryExtractBonesBlock(lines, out var bonesBlock, out var boneCount, out var error), "Bones block should extract: " + error);
        AssertEqual(2, boneCount, "Extracted bone count.");
        AssertEqual("    bones:", bonesBlock[0], "Bones block header.");
        AssertTrue(bonesBlock.Any(line => line.TrimStart() == "- name: root"), "Root bone should be copied.");
        AssertTrue(bonesBlock.Any(line => line.TrimStart() == "- name: head"), "Head bone should be copied.");

        var parts = ReanimatorBoneUtilityAlgorithms.ParseCharacterPartBonesBySpriteId(lines);
        AssertEqual(2, parts.Count, "Part mapping count.");
        AssertEqual("[0, 1]", parts["aabb"], "Normalized first sprite id bones.");
        AssertEqual("[1]", parts["ccdd"], "Normalized second sprite id bones.");
    }

    private static void BoneUtilityParsesRigSpritesAndMapsCopiedParts()
    {
        var lines = new List<string>
        {
            "  rigSpriteImportData:",
            "  - name: front_body",
            "    spriteID: 1111-2222",
            "    rect:",
            "      x: 10",
            "      y: 20",
            "      width: 30",
            "      height: 40",
            "  - name: eye_big",
            "    spriteId: 3333-4444",
            "    spritePosition: {x: 7, y: 8}",
            "  textureImporterSettings:"
        };

        var sprites = ReanimatorBoneUtilityAlgorithms.ParseRigSprites(lines);
        AssertEqual(2, sprites.Count, "Parsed rig sprite count.");
        AssertEqual("11112222", sprites[0].spriteId, "Sprite id should normalize.");
        AssertVector(new Vector2(10f, 20f), sprites[0].position, 0.0001f, "Parsed rect position.");
        AssertVector(new Vector2(30f, 40f), sprites[0].size, 0.0001f, "Parsed rect size.");
        AssertVector(new Vector2(7f, 8f), sprites[1].position, 0.0001f, "Parsed inline sprite position.");

        var sourceIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "body", "source-body" },
            { "eye_right", "source-eye" }
        };
        var sourceParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "source-body", "[0, 1]" },
            { "source-eye", "[2]" }
        };
        var copied = ReanimatorBoneUtilityAlgorithms.BuildCopiedCharacterDataBlock(
            "  characterData:",
            new[] { "    bones:", "    - name: root", "      guid: old-guid" },
            sourceParts,
            sourceIds,
            sprites,
            out var mappedParts);

        AssertEqual(2, mappedParts, "Mapped part count.");
        AssertTrue(copied.Any(line => line == "      bones: [0, 1]"), "Body bones should map by source alias.");
        AssertTrue(copied.Any(line => line == "      bones: [2]"), "Eye bones should map by source alias.");
        AssertFalse(copied.Any(line => line == "      guid: old-guid"), "Copied bones should regenerate guids.");
    }

    private static void BoneUtilityReplacesCharacterDataRange()
    {
        var lines = new List<string>
        {
            "TextureImporter:",
            "  spriteSheet:",
            "  characterData:",
            "    bones: []",
            "    parts: []",
            "  rigSpriteImportData:",
            "  - name: body"
        };

        AssertTrue(ReanimatorBoneUtilityAlgorithms.TryFindCharacterDataRange(lines, "characterData:", out var start, out var end), "CharacterData range should be found.");
        AssertEqual(2, start, "CharacterData range start.");
        AssertEqual(5, end, "CharacterData range end.");

        var replacement = ReanimatorBoneUtilityAlgorithms.BuildEmptyCharacterDataBlock("  characterData:", new Vector2(0.25f, 0.75f));
        AssertTrue(ReanimatorBoneUtilityAlgorithms.ReplaceCharacterData(lines, "characterData:", replacement), "CharacterData should replace.");
        AssertEqual("  characterData:", lines[2], "Replacement header.");
        AssertTrue(lines.Any(line => line == "    pivot: {x: 0.25, y: 0.75}"), "Replacement pivot should format invariant.");
        AssertTrue(lines.Any(line => line == "  rigSpriteImportData:"), "Following section should remain.");
    }

    private static void BoneUtilityDeletesAllBonesFromMetaLines()
    {
        var lines = BuildDeleteBonesMetaLines();
        AssertTrue(ReanimatorBoneUtilityAlgorithms.TryDeleteAllBonesFromMetaLines(lines, out var error), "Delete all bones should transform meta: " + error);

        AssertTrue(lines.Any(line => line == "    bones: []"), "Primary characterData bones should be empty.");
        AssertTrue(lines.Any(line => line == "    pivot: {x: 0.5, y: 0}"), "Primary characterData pivot should reset.");
        AssertTrue(lines.Any(line => line == "    pivot: {x: 0, y: 0}"), "Shared rig pivot should reset.");
        AssertFalse(lines.Any(line => line.Contains("- name: root")), "Old primary bones should be removed.");
        AssertFalse(lines.Any(line => line.Contains("- name: shared")), "Old shared bones should be removed.");
        AssertTrue(lines.Any(line => line == "    spriteBone: []"), "Sprite bone block should be collapsed.");
        AssertFalse(lines.Any(line => line.TrimStart() == "- name: oldSpriteBone"), "Nested sprite bone entries should be removed.");
        AssertTrue(lines.Any(line => line == "    spriteID: sprite-a"), "Following rig sprite fields should remain.");
    }

    private static void BoneUtilityCopiesBonesBetweenMetaLines()
    {
        var sourceLines = BuildCopySourceMetaLines();
        var targetLines = BuildCopyTargetMetaLines();

        AssertTrue(
            ReanimatorBoneUtilityAlgorithms.TryCopyBonesBetweenMetaLines(sourceLines, targetLines, out var copiedBones, out var mappedParts, out var error),
            "Copy bones should transform target meta: " + error);

        AssertEqual(2, copiedBones, "Copied bone count.");
        AssertEqual(2, mappedParts, "Mapped target part count.");
        AssertTrue(targetLines.Any(line => line.TrimStart() == "- name: root"), "Root bone should be copied to target.");
        AssertTrue(targetLines.Any(line => line.TrimStart() == "- name: head"), "Head bone should be copied to target.");
        AssertFalse(targetLines.Any(line => line == "      guid: root-guid"), "Root guid should be regenerated.");
        AssertTrue(targetLines.Any(line => line == "      spriteId: targetbody"), "Target body sprite id should normalize.");
        AssertTrue(targetLines.Any(line => line == "      spriteId: targeteye"), "Target eye sprite id should normalize.");
        AssertTrue(targetLines.Any(line => line == "      bones: [0, 1]"), "Target body should receive source body bones.");
        AssertTrue(targetLines.Any(line => line == "      bones: [1]"), "Target eye should receive source eye bones.");
        AssertTrue(targetLines.Any(line => line == "  rigSpriteImportData:"), "Target rig sprite section should remain.");
    }

    private static void SerializedPropertyWriteBackUpdatesScalarAndTransformFields()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.text = "old";
            fixture.floatValue = 1f;
            fixture.intValue = 1;
            fixture.vector2 = new Vector2(1f, 2f);
            fixture.vector3 = new Vector3(3f, 4f, 99f);
            fixture.vector2Like = new Vector2Like { x = 5f, y = 6f };
            fixture.quaternion = Quaternion.identity;
            fixture.quaternionLike = new QuaternionLike { x = 0f, y = 0f, z = 0f, w = 1f };

            var so = new SerializedObject(fixture);
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteStringIfChanged(so.FindProperty("text"), null), "String should write null as empty.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteFloatIfChanged(so.FindProperty("floatValue"), 2.25f), "Float should update.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteFloatIfChanged(so.FindProperty("intValue"), 5.6f), "Integer should update from rounded float.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(so.FindProperty("vector2"), new Vector2(7f, 8f)), "Vector2 should update.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(so.FindProperty("vector3"), new Vector2(9f, 10f)), "Vector3 xy should update.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteVector3IfChanged(so.FindProperty("vector3"), new Vector3(9f, 10f, -7f)), "Vector3 xyz should update.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(so.FindProperty("vector2Like"), new Vector2(11f, 12f)), "Inline vector-like fields should update.");

            var rotation = Quaternion.Euler(0f, 0f, 35f);
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteQuaternionIfChanged(so.FindProperty("quaternion"), rotation), "Quaternion should update.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.WriteQuaternionIfChanged(so.FindProperty("quaternionLike"), rotation), "Inline quaternion-like fields should update.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(string.Empty, fixture.text, "Null string write should become empty.");
            AssertFloat(2.25f, fixture.floatValue, 0.0001f, "Float write.");
            AssertEqual(6, fixture.intValue, "Rounded integer write.");
            AssertVector(new Vector2(7f, 8f), fixture.vector2, 0.0001f, "Vector2 write.");
            AssertVector(new Vector2(9f, 10f), fixture.vector3, 0.0001f, "Vector3 xy write.");
            AssertFloat(-7f, fixture.vector3.z, 0.0001f, "Vector3 z should update.");
            AssertFloat(11f, fixture.vector2Like.x, 0.0001f, "Vector-like x write.");
            AssertFloat(12f, fixture.vector2Like.y, 0.0001f, "Vector-like y write.");
            AssertQuaternion(rotation, fixture.quaternion, 0.0001f, "Quaternion write.");
            AssertQuaternion(rotation, new Quaternion(fixture.quaternionLike.x, fixture.quaternionLike.y, fixture.quaternionLike.z, fixture.quaternionLike.w), 0.0001f, "Quaternion-like write.");

            so.Update();
            AssertFalse(ReanimatorSerializedPropertyAlgorithms.WriteVector2IfChanged(so.FindProperty("vector2"), new Vector2(7f, 8f)), "Unchanged Vector2 should not report changed.");
            AssertFalse(ReanimatorSerializedPropertyAlgorithms.WriteQuaternionIfChanged(so.FindProperty("quaternion"), rotation), "Unchanged Quaternion should not report changed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void SerializedPropertyWriteBackReadsAndWritesColorFields()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.color = Color.white;
            fixture.colorLike = new ColorLike { r = 0.1f, g = 0.2f, b = 0.3f, a = 0.4f };
            fixture.colorNoAlpha = new ColorNoAlpha { r = 0.5f, g = 0.6f, b = 0.7f };

            var so = new SerializedObject(fixture);
            var fallback = new Color(0.9f, 0.8f, 0.7f, 0.66f);
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.TryReadColorProperty(so.FindProperty("color"), fallback, out var color), "Color field should read.");
            AssertColor(Color.white, color, 0.0001f, "Color field read.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.TryReadColorProperty(so.FindProperty("colorLike"), fallback, out var colorLike), "Color-like field should read.");
            AssertColor(new Color(0.1f, 0.2f, 0.3f, 0.4f), colorLike, 0.0001f, "Color-like field read.");
            AssertTrue(ReanimatorSerializedPropertyAlgorithms.TryReadColorProperty(so.FindProperty("colorNoAlpha"), fallback, out var colorNoAlpha), "Color without alpha should read.");
            AssertColor(new Color(0.5f, 0.6f, 0.7f, fallback.a), colorNoAlpha, 0.0001f, "Color without alpha should use fallback alpha.");
            AssertFalse(ReanimatorSerializedPropertyAlgorithms.TryReadColorProperty(so.FindProperty("vector2Like"), fallback, out var invalidColor), "Non-color property should fail.");
            AssertColor(fallback, invalidColor, 0.0001f, "Invalid color read should keep fallback.");

            ReanimatorSerializedPropertyAlgorithms.WriteColorProperty(so.FindProperty("color"), new Color(0.2f, 0.3f, 0.4f, 0.5f));
            ReanimatorSerializedPropertyAlgorithms.WriteColorProperty(so.FindProperty("colorLike"), new Color(0.6f, 0.7f, 0.8f, 0.9f));
            ReanimatorSerializedPropertyAlgorithms.WriteColorProperty(so.FindProperty("colorNoAlpha"), new Color(0.15f, 0.25f, 0.35f, 0.45f));
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertColor(new Color(0.2f, 0.3f, 0.4f, 0.5f), fixture.color, 0.0001f, "Color field write.");
            AssertFloat(0.6f, fixture.colorLike.r, 0.0001f, "Color-like r write.");
            AssertFloat(0.7f, fixture.colorLike.g, 0.0001f, "Color-like g write.");
            AssertFloat(0.8f, fixture.colorLike.b, 0.0001f, "Color-like b write.");
            AssertFloat(0.9f, fixture.colorLike.a, 0.0001f, "Color-like a write.");
            AssertFloat(0.15f, fixture.colorNoAlpha.r, 0.0001f, "Color no-alpha r write.");
            AssertFloat(0.25f, fixture.colorNoAlpha.g, 0.0001f, "Color no-alpha g write.");
            AssertFloat(0.35f, fixture.colorNoAlpha.b, 0.0001f, "Color no-alpha b write.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

}
#endif
