#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    private bool SyncBoneOrdersToDataProvider(ISpriteEditorDataProvider dataProvider)
    {
        if (dataProvider == null || bones == null || bones.Count == 0)
            return false;

        var rendererBoneOrders = BuildRendererBoneOrderArray();
        var changed = SyncCharacterBoneOrders(dataProvider, rendererBoneOrders);
        if (!dataProvider.HasDataProvider(typeof(ISpriteBoneDataProvider)) ||
            !dataProvider.HasDataProvider(typeof(ISpriteMeshDataProvider)))
        {
            return changed;
        }

        var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        if (boneProvider == null || meshProvider == null)
            return changed;

        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null)
            return changed;

        for (var spriteIndex = 0; spriteIndex < spriteRects.Length; spriteIndex++)
        {
            var spriteRect = spriteRects[spriteIndex];
            List<SpriteBone> spriteBones;
            try
            {
                spriteBones = boneProvider.GetBones(spriteRect.spriteID);
            }
            catch
            {
                continue;
            }

            if (spriteBones == null || spriteBones.Count == 0)
                continue;

            var spriteBoneToRendererBone = BuildSpriteBoneToRendererBoneMap(spriteBones.ToArray());
            var orderedSpriteBones = ReanimatorBoneOrderAlgorithms.ApplyRendererBoneOrders(
                spriteBones,
                spriteBoneToRendererBone,
                rendererBoneOrders,
                out var spriteBonesChanged);
            if (spriteBonesChanged)
            {
                boneProvider.SetBones(spriteRect.spriteID, orderedSpriteBones);
                changed = true;
            }

            Vertex2DMetaData[] vertices;
            int[] currentIndices;
            try
            {
                vertices = meshProvider.GetVertices(spriteRect.spriteID);
                currentIndices = meshProvider.GetIndices(spriteRect.spriteID);
            }
            catch
            {
                continue;
            }

            if (vertices == null || vertices.Length == 0 || currentIndices == null || currentIndices.Length < 3)
                continue;

            var weights = new BoneWeight[vertices.Length];
            for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                weights[vertexIndex] = vertices[vertexIndex].boneWeight;

            var sortedIndices = ReanimatorBoneOrderAlgorithms.SortTrianglesByBoneOrder(
                currentIndices,
                weights,
                spriteBoneToRendererBone,
                rendererBoneOrders);
            if (ReanimatorBoneOrderAlgorithms.AreIndicesEqual(currentIndices, sortedIndices))
                continue;

            meshProvider.SetIndices(spriteRect.spriteID, sortedIndices);
            changed = true;
        }

        return changed;
    }

    private bool SyncCharacterBoneOrders(ISpriteEditorDataProvider dataProvider, IReadOnlyList<int> rendererBoneOrders)
    {
        if (dataProvider == null || rendererBoneOrders == null ||
            !dataProvider.HasDataProvider(typeof(ICharacterDataProvider)))
        {
            return false;
        }

        var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
        if (characterProvider == null)
            return false;

        CharacterData characterData;
        try
        {
            characterData = characterProvider.GetCharacterData();
        }
        catch
        {
            return false;
        }

        if (characterData.bones == null || characterData.bones.Length == 0)
            return false;

        var changed = false;
        var count = Mathf.Min(characterData.bones.Length, rendererBoneOrders.Count);
        for (var i = 0; i < count; i++)
        {
            var bone = characterData.bones[i];
            var desiredOrder = ReanimatorBoneOrderAlgorithms.EncodeBoneOrder(rendererBoneOrders[i]);
            if (Mathf.Abs(bone.position.z - desiredOrder) <= 0.0001f)
                continue;

            bone.position = new Vector3(bone.position.x, bone.position.y, desiredOrder);
            characterData.bones[i] = bone;
            changed = true;
        }

        if (changed)
            characterProvider.SetCharacterData(characterData);
        return changed;
    }
}
#endif
