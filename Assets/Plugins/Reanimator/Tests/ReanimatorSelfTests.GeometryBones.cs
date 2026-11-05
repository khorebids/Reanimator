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
    private static void AssetChangeAlgorithmsWriteRectAndVectorFields()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.rect = new Rect(1f, 2f, 3f, 4f);
            fixture.rectLike = new RectLike { x = 5f, y = 6f, width = 7f, height = 8f };
            fixture.vector4 = new Vector4(1f, 2f, 3f, 4f);
            fixture.vector4Like = new Vector4Like { x = 5f, y = 6f, z = 7f, w = 8f };

            var so = new SerializedObject(fixture);
            AssertFalse(ReanimatorAssetChangeAlgorithms.WriteRectIfChanged(so.FindProperty("rect"), fixture.rect), "Unchanged Rect should not report changed.");
            AssertTrue(ReanimatorAssetChangeAlgorithms.WriteRectIfChanged(so.FindProperty("rect"), new Rect(9f, 10f, 11f, 12f)), "Rect should update.");
            AssertTrue(ReanimatorAssetChangeAlgorithms.WriteRectIfChanged(so.FindProperty("rectLike"), new Rect(13f, 14f, 15f, 16f)), "Rect-like fields should update.");
            AssertFalse(ReanimatorAssetChangeAlgorithms.WriteVector4IfChanged(so.FindProperty("vector4"), fixture.vector4), "Unchanged Vector4 should not report changed.");
            AssertTrue(ReanimatorAssetChangeAlgorithms.WriteVector4IfChanged(so.FindProperty("vector4"), new Vector4(2f, 4f, 6f, 8f)), "Vector4 should update.");
            AssertTrue(ReanimatorAssetChangeAlgorithms.WriteVector4IfChanged(so.FindProperty("vector4Like"), new Vector4(3f, 6f, 9f, 12f)), "Vector4-like fields should update.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(new Rect(9f, 10f, 11f, 12f), fixture.rect, "Rect write.");
            AssertFloat(13f, fixture.rectLike.x, 0.0001f, "Rect-like x write.");
            AssertFloat(14f, fixture.rectLike.y, 0.0001f, "Rect-like y write.");
            AssertFloat(15f, fixture.rectLike.width, 0.0001f, "Rect-like width write.");
            AssertFloat(16f, fixture.rectLike.height, 0.0001f, "Rect-like height write.");
            AssertEqual(new Vector4(2f, 4f, 6f, 8f), fixture.vector4, "Vector4 write.");
            AssertFloat(3f, fixture.vector4Like.x, 0.0001f, "Vector4-like x write.");
            AssertFloat(6f, fixture.vector4Like.y, 0.0001f, "Vector4-like y write.");
            AssertFloat(9f, fixture.vector4Like.z, 0.0001f, "Vector4-like z write.");
            AssertFloat(12f, fixture.vector4Like.w, 0.0001f, "Vector4-like w write.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void AssetChangeAlgorithmsWriteIntegerFieldsOnlyWhenChanged()
    {
        var fixture = ScriptableObject.CreateInstance<SerializedPropertyFixture>();
        try
        {
            fixture.intValue = 4;
            fixture.floatValue = 1.5f;
            var so = new SerializedObject(fixture);

            AssertFalse(ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(null, 10), "Null int property should not update.");
            AssertFalse(ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(so.FindProperty("floatValue"), 10), "Non-int property should not update.");
            AssertFalse(ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(so.FindProperty("intValue"), 4), "Unchanged int should not report changed.");
            AssertTrue(ReanimatorAssetChangeAlgorithms.WriteIntIfChanged(so.FindProperty("intValue"), 9), "Changed int should update.");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssertEqual(9, fixture.intValue, "Int write.");
            AssertFloat(1.5f, fixture.floatValue, 0.0001f, "Non-int property should be untouched.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static List<string> BuildCharacterMetaLines()
    {
        return new List<string>
        {
            "TextureImporter:",
            "  spriteSheet:",
            "  characterData:",
            "    bones:",
            "    - name: root",
            "      guid: root-guid",
            "    - name: head",
            "      guid: head-guid",
            "    parts:",
            "    - spritePosition:",
            "        x: 0",
            "        y: 0",
            "        width: 10",
            "        height: 10",
            "      spriteId: aa-bb",
            "      bones: [0, 1]",
            "    - spritePosition:",
            "        x: 10",
            "        y: 0",
            "        width: 10",
            "        height: 10",
            "      m_SpriteID: cc-dd",
            "      bones: [1]",
            "  rigSpriteImportData:"
        };
    }

    private static List<string> BuildDeleteBonesMetaLines()
    {
        return new List<string>
        {
            "TextureImporter:",
            "  spriteSheet:",
            "  characterData:",
            "    bones:",
            "    - name: root",
            "      guid: root-guid",
            "    parts:",
            "    - spritePosition:",
            "      spriteId: sprite-a",
            "      bones: [0]",
            "  sharedRigCharacterData:",
            "    bones:",
            "    - name: shared",
            "      guid: shared-guid",
            "    parts: []",
            "  rigSpriteImportData:",
            "  - name: body",
            "    spriteBone:",
            "    - name: oldSpriteBone",
            "      parentId: -1",
            "    spriteID: sprite-a"
        };
    }

    private static List<string> BuildCopySourceMetaLines()
    {
        return new List<string>
        {
            "TextureImporter:",
            "  spriteSheet:",
            "  characterData:",
            "    bones:",
            "    - name: root",
            "      guid: root-guid",
            "    - name: head",
            "      guid: head-guid",
            "    parts:",
            "    - spritePosition:",
            "      spriteId: source-body",
            "      bones: [0, 1]",
            "    - spritePosition:",
            "      spriteId: source-eye",
            "      bones: [1]",
            "  rigSpriteImportData:",
            "  - name: body",
            "    spriteID: source-body",
            "    rect:",
            "      x: 0",
            "      y: 0",
            "      width: 10",
            "      height: 20",
            "  - name: eye_right",
            "    spriteID: source-eye",
            "    rect:",
            "      x: 10",
            "      y: 0",
            "      width: 5",
            "      height: 5"
        };
    }

    private static List<string> BuildCopyTargetMetaLines()
    {
        return new List<string>
        {
            "TextureImporter:",
            "  spriteSheet:",
            "  characterData:",
            "    bones: []",
            "    parts: []",
            "  rigSpriteImportData:",
            "  - name: front_body",
            "    spriteID: target-body",
            "    rect:",
            "      x: 100",
            "      y: 200",
            "      width: 30",
            "      height: 40",
            "  - name: eye_big",
            "    spriteID: target-eye",
            "    rect:",
            "      x: 130",
            "      y: 205",
            "      width: 6",
            "      height: 7",
            "  textureImporterSettings:"
        };
    }

    private sealed class SerializedPropertyFixture : ScriptableObject
    {
        public string text;
        public float floatValue;
        public int intValue;
        public Vector2 vector2;
        public Vector3 vector3;
        public Vector2Like vector2Like;
        public Rect rect;
        public SpriteImportDataLike spriteLike;
        public PsdLayerDataLike psdLayerLike;
        public PsdLayerImportDataLike[] rigPSDLayers;
        public SpriteImportDataRecordLike[] rigSpriteImportData;
        public SpriteImportDataRecordLike[] sharedRigSpriteImportData;
        public SpriteImportDataRecordLike[] mosaicSpriteImportData;
        public SpriteImportDataRecordLike[] spriteImportData;
        public SpriteImportDataRecordLike[] layeredSpriteImportData;
        public CharacterDataLike characterData;
        public RectLike rectLike;
        public Vector4 vector4;
        public Vector4Like vector4Like;
        public Quaternion quaternion;
        public QuaternionLike quaternionLike;
        public Color color;
        public ColorLike colorLike;
        public ColorNoAlpha colorNoAlpha;
        public bool resliceFromLayer;
        public bool mosaicLayers;
        public bool characterMode;
        public int spriteSizeExpandChanged;
    }

    [Serializable]
    private struct Vector2Like
    {
        public float x;
        public float y;
    }

    [Serializable]
    private struct RectLike
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    private struct SpriteImportDataLike
    {
        public Rect rect;
        public Vector2 uvTransform;
        public Vector2 spritePosition;
    }

    [Serializable]
    private struct PsdLayerDataLike
    {
        public Vector2 mosaicPosition;
    }

    [Serializable]
    private struct PsdLayerImportDataLike
    {
        public string name;
        public string spriteID;
        public Vector2 mosaicPosition;
    }

    [Serializable]
    private struct SpriteImportDataRecordLike
    {
        public string name;
        public Rect rect;
        public string spriteID;
        public Vector2 spritePosition;
        public Vertex2DMetaDataLike[] vertices;
    }

    [Serializable]
    private struct Vertex2DMetaDataLike
    {
        public Vector2 position;
        public BoneWeight boneWeight;
    }

    [Serializable]
    private struct CharacterDataLike
    {
        public SpriteBoneLike[] bones;
        public CharacterPartLike[] parts;
    }

    [Serializable]
    private struct SpriteBoneLike
    {
        public string name;
        public int parentId;
        public Vector3 position;
        public Quaternion rotation;
        public float length;
        public Color color;
    }

    [Serializable]
    private struct CharacterPartLike
    {
        public RectInt spritePosition;
        public string spriteId;
    }

    [Serializable]
    private struct Vector4Like
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    private struct QuaternionLike
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    private struct ColorLike
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [Serializable]
    private struct ColorNoAlpha
    {
        public float r;
        public float g;
        public float b;
    }

    private static void SkinningImportMapsGeneratedBoneNamesByRendererIndex()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>();
        for (var i = 0; i <= 40; i++)
        {
            bones.Add(new PsdLayerPreviewRenderer.BoneInfo
            {
                index = i,
                name = $"character_bone_{i}",
                parentId = -1,
                position = Vector2.zero,
                rotation = Quaternion.identity
            });
        }

        SetPrivateField(renderer, "bones", bones);
        var spriteBones = new[]
        {
            new SpriteBone { name = "bone_40", parentId = -1 },
            new SpriteBone { name = "bone_12", parentId = -1 }
        };

        var map = (int[])InvokePrivateMethod(renderer, "BuildSpriteBoneToRendererBoneMap", spriteBones);
        AssertArray(new[] { 40, 12 }, map, "Generated sprite bone names should map to renderer indices before array-order fallback.");
    }

    private static void SkinningImportNeverOverwritesExactNamesForFlattenedSpriteHierarchy()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo { index = 0, name = "root", parentId = -1, length = 10f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 1, name = "spine", parentId = 0, length = 100f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 2, name = "boobs_root", parentId = 1, length = 40f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 3, name = "nipple_l", parentId = 2, length = 20f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 4, name = "l_arm", parentId = 1, length = 25f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 5, name = "l_arm_2", parentId = 1, length = 30f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 6, name = "l_arm_3", parentId = 5, length = 35f },
            new PsdLayerPreviewRenderer.BoneInfo { index = 7, name = "l_hand_2", parentId = 6, length = 15f }
        };

        SetPrivateField(renderer, "bones", bones);
        var spriteBones = new[]
        {
            new SpriteBone { name = "spine", parentId = -1, length = 100f },
            // body intentionally flattens spine -> boobs_root -> nipple_l
            new SpriteBone { name = "nipple_l", parentId = 0, length = 20f },
            // a partial secondary-arm sprite may flatten omitted attachment bones
            new SpriteBone { name = "l_hand_2", parentId = 0, length = 15f }
        };

        var map = (int[])InvokePrivateMethod(renderer, "BuildSpriteBoneToRendererBoneMap", spriteBones);
        AssertArray(
            new[] { 1, 3, 7 },
            map,
            "Exact nipple/secondary-arm names must survive Apply/Rebuild even when sprite parent links omit intermediate character bones.");
    }

    private static void ImporterGeometryPreviewPrefersImporterSkinningOverSpriteSkinning()
    {
        var importerWeights = new[]
        {
            new BoneWeight { weight0 = 1f, boneIndex0 = 0 },
            new BoneWeight { weight0 = 0.5f, boneIndex0 = 1, weight1 = 0.5f, boneIndex1 = 2 }
        };

        AssertTrue(
            PsdLayerPreviewRenderer.ShouldPreferImporterPreviewSkinning(true, importerWeights, importerWeights.Length),
            "Preview geometry built from importer mesh should keep matching importer weights instead of mixing in Sprite skinning.");
        AssertFalse(
            PsdLayerPreviewRenderer.ShouldPreferImporterPreviewSkinning(false, importerWeights, importerWeights.Length),
            "Sprite-built preview geometry should still be allowed to use Sprite skinning first.");
        AssertFalse(
            PsdLayerPreviewRenderer.ShouldPreferImporterPreviewSkinning(true, importerWeights, importerWeights.Length + 1),
            "Importer weights with a different vertex count must not be preferred.");
        AssertFalse(
            PsdLayerPreviewRenderer.ShouldPreferImporterPreviewSkinning(
                true,
                new[] { new BoneWeight { weight0 = 0f, boneIndex0 = 0 } },
                1),
            "Empty importer weights should not block the Sprite skinning fallback.");
    }

    private static PsdLayerPreviewRenderer.BoneInfo BuildBone(string name, int parentId, Vector2 position, Quaternion rotation)
    {
        return new PsdLayerPreviewRenderer.BoneInfo
        {
            name = name,
            parentId = parentId,
            position = position,
            rotation = rotation
        };
    }

    private static PsdLayerPreviewRenderer.LayerInfo BuildLayer(string name, int layerId, Vector2 position)
    {
        return new PsdLayerPreviewRenderer.LayerInfo
        {
            name = name,
            layerId = layerId,
            position = position
        };
    }
}
#endif
