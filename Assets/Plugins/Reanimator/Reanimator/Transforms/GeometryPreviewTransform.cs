#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.Sprites;
using UnityEngine;

internal sealed class GeometryPreviewTransform
{
    public enum ImporterInterpretation
    {
        RawLocal = 0,
        RawPlusPivot = 1,
        RawTimesPpuPlusPivot = 2,
        RawMinusSourceRect = 3,
        RawAsWorld = 4
    }

    private readonly PsdLayerPreviewRenderer.LayerInfo layer;

    public GeometryPreviewTransform(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        this.layer = layer;
    }

    public void FromSpriteVertices(
        IReadOnlyList<Vector2> spriteVertices,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        out Vector2[] localPixels,
        out Vector2[] worldVertices)
    {
        localPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        if (spriteVertices == null || spriteVertices.Count == 0)
            return;

        var count = spriteVertices.Count;
        localPixels = new Vector2[count];
        worldVertices = new Vector2[count];
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        for (var i = 0; i < count; i++)
        {
            var local = spriteVertices[i] * ppu + pivotPixels;
            localPixels[i] = local;
            worldVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
        }
    }

    public void FromSpriteUvs(
        IReadOnlyList<Vector2> spriteUvs,
        float textureWidth,
        float textureHeight,
        Vector2 uvOffsetPixels,
        bool flipY,
        float flipHeight,
        out Vector2[] localPixels,
        out Vector2[] worldVertices)
    {
        localPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        if (spriteUvs == null || spriteUvs.Count == 0)
            return;

        var texW = Mathf.Max(1f, textureWidth);
        var texH = Mathf.Max(1f, textureHeight);
        var count = spriteUvs.Count;
        localPixels = new Vector2[count];
        worldVertices = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var uv = spriteUvs[i];
            var texel = new Vector2(uv.x * texW, uv.y * texH);
            var local = texel - uvOffsetPixels;
            if (flipY)
                local.y = flipHeight - local.y;
            localPixels[i] = local;
            worldVertices[i] = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
        }
    }

    public void FromImporterVertices(
        IReadOnlyList<Vertex2DMetaData> importerVertices,
        ImporterInterpretation interpretation,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        Vector2 sourceRectOffset,
        bool flipLocalY,
        float flipHeight,
        out Vector2[] localPixels,
        out Vector2[] worldVertices)
    {
        localPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        if (importerVertices == null || importerVertices.Count == 0)
            return;

        var count = importerVertices.Count;
        localPixels = new Vector2[count];
        worldVertices = new Vector2[count];
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        for (var i = 0; i < count; i++)
        {
            var raw = importerVertices[i].position;
            Vector2 local;
            Vector2 world;
            switch (interpretation)
            {
                case ImporterInterpretation.RawPlusPivot:
                    local = raw + pivotPixels;
                    world = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
                    break;
                case ImporterInterpretation.RawTimesPpuPlusPivot:
                    local = raw * ppu + pivotPixels;
                    world = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
                    break;
                case ImporterInterpretation.RawMinusSourceRect:
                    local = raw - sourceRectOffset;
                    world = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
                    break;
                case ImporterInterpretation.RawAsWorld:
                    world = raw;
                    local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, world);
                    break;
                default:
                    local = raw;
                    world = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
                    break;
            }

            if (flipLocalY)
            {
                local.y = flipHeight - local.y;
                world = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
            }

            localPixels[i] = local;
            worldVertices[i] = world;
        }
    }
}

internal static class ReanimatorSkinningAlgorithms
{
    public static Matrix4x4[] BuildRendererBindBoneInverseMatrices(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        IReadOnlyList<int> spriteBoneToRendererBone)
    {
        if (spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0)
            return Array.Empty<Matrix4x4>();

        var result = new Matrix4x4[spriteBoneToRendererBone.Count];
        for (var i = 0; i < spriteBoneToRendererBone.Count; i++)
        {
            var rendererBoneIndex = spriteBoneToRendererBone[i];
            if (bones == null || rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            {
                result[i] = Matrix4x4.identity;
                continue;
            }

            var bone = bones[rendererBoneIndex];
            var bindMatrix = Matrix4x4.TRS(
                new Vector3(bone.worldPosition.x, bone.worldPosition.y, 0f),
                NormalizeRotation(bone.worldRotation),
                Vector3.one);
            result[i] = bindMatrix.inverse;
        }

        return result;
    }

    public static void BuildSkinMatrices(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<Matrix4x4> bindBoneInverseMatrices,
        bool usesSpriteBindPoses,
        PsdLayerPreviewRenderer.LayerInfo layer,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        Matrix4x4[] skinMatrices)
    {
        if (bindBoneInverseMatrices == null || skinMatrices == null)
            return;

        var count = Mathf.Min(bindBoneInverseMatrices.Count, skinMatrices.Length);
        for (var i = 0; i < count; i++)
        {
            var rendererBoneIndex = spriteBoneToRendererBone != null && i < spriteBoneToRendererBone.Count
                ? spriteBoneToRendererBone[i]
                : -1;
            if (bones == null || rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            {
                skinMatrices[i] = Matrix4x4.identity;
                continue;
            }

            var bone = bones[rendererBoneIndex];
            var bonePosition = usesSpriteBindPoses
                ? DocumentToSpriteSkinPosition(layer, bone.worldPosition, pixelsPerUnit, pivotPixels)
                : bone.worldPosition;
            var boneMatrix = Matrix4x4.TRS(
                new Vector3(bonePosition.x, bonePosition.y, 0f),
                NormalizeRotation(bone.worldRotation),
                Vector3.one);
            skinMatrices[i] = boneMatrix * bindBoneInverseMatrices[i];
        }
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.x * rotation.x +
                            rotation.y * rotation.y +
                            rotation.z * rotation.z +
                            rotation.w * rotation.w;
        if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.000001f)
            return Quaternion.identity;

        var inverseLength = 1f / Mathf.Sqrt(lengthSquared);
        return new Quaternion(
            rotation.x * inverseLength,
            rotation.y * inverseLength,
            rotation.z * inverseLength,
            rotation.w * inverseLength);
    }

    public static void DeformVertices(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<Matrix4x4> skinMatrices,
        bool usesSpriteBindPoses,
        PsdLayerPreviewRenderer.LayerInfo layer,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        Vector2[] deformedVertices)
    {
        if (vertices == null || boneWeights == null || skinMatrices == null || deformedVertices == null)
            return;

        var count = Mathf.Min(vertices.Count, Mathf.Min(boneWeights.Count, deformedVertices.Length));
        for (var i = 0; i < count; i++)
        {
            var basePoint = usesSpriteBindPoses
                ? DocumentToSpriteSkinPosition(layer, vertices[i], pixelsPerUnit, pivotPixels)
                : vertices[i];
            if (!IsFinite(basePoint))
            {
                deformedVertices[i] = vertices[i];
                continue;
            }

            var basePos = new Vector3(basePoint.x, basePoint.y, 0f);
            var bw = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(boneWeights[i], skinMatrices.Count, false, true);

            var weightSum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (!IsFinite(weightSum) || weightSum <= 1e-6f)
            {
                deformedVertices[i] = vertices[i];
                continue;
            }

            var skinned = Vector3.zero;
            if (usesSpriteBindPoses)
            {
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex0, bw.weight0);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex1, bw.weight1);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex2, bw.weight2);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex3, bw.weight3);
            }
            else
            {
                var weightSumInv = 1f / weightSum;
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex0, bw.weight0 * weightSumInv);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex1, bw.weight1 * weightSumInv);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex2, bw.weight2 * weightSumInv);
                AddWeightedVertex(ref skinned, basePos, skinMatrices, bw.boneIndex3, bw.weight3 * weightSumInv);
            }

            if (usesSpriteBindPoses)
            {
                var documentPosition = SpriteSkinPositionToDocument(layer, new Vector2(skinned.x, skinned.y), pixelsPerUnit, pivotPixels);
                deformedVertices[i] = IsFinite(documentPosition) ? documentPosition : vertices[i];
            }
            else
            {
                var position = Vector3.Lerp(basePos, skinned, Mathf.Clamp01(weightSum));
                var documentPosition = new Vector2(position.x, position.y);
                deformedVertices[i] = IsFinite(documentPosition) ? documentPosition : vertices[i];
            }
        }
    }

    public static Vector2 DocumentToSpriteSkinPosition(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 documentPosition,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        var local = PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, documentPosition);
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        return (local - pivotPixels) / ppu;
    }

    public static Vector2 SpriteSkinPositionToDocument(
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2 spritePosition,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        var local = spritePosition * ppu + pivotPixels;
        return PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, local);
    }

    private static void AddWeightedVertex(
        ref Vector3 target,
        Vector3 basePosition,
        IReadOnlyList<Matrix4x4> skinMatrices,
        int boneIndex,
        float weight)
    {
        if (!IsFinite(weight) || weight <= 1e-6f || boneIndex < 0 || boneIndex >= skinMatrices.Count)
            return;

        var skinned = skinMatrices[boneIndex].MultiplyPoint3x4(basePosition);
        if (!IsFinite(skinned))
            return;

        var contribution = skinned * weight;
        if (!IsFinite(contribution))
            return;

        var next = target + contribution;
        target = IsFinite(next) ? next : target;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
#endif
