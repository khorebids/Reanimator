#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

internal static class ReanimatorBoneOrderAlgorithms
{
    // Order is persisted in SpriteBone.position.z because Unity's SpriteBone has no separate order field.
    // Keep the stored value shallow so generated 2D skeletons and their bind poses remain nearly planar.
    public const float StoredOrderScale = 0.01f;

    private readonly struct WeightedTriangle
    {
        public readonly int sourceOffset;
        public readonly float order;

        public WeightedTriangle(int sourceOffset, float order)
        {
            this.sourceOffset = sourceOffset;
            this.order = order;
        }
    }

    public static int ReadBoneOrder(Vector3 position)
    {
        // Older Reanimator assets stored the raw integer order in z. They migrate to the scaled format
        // on the next Apply/Cook while retaining the same order in the editor.
        var legacyOrder = Mathf.Round(position.z);
        if (Mathf.Abs(position.z - legacyOrder) <= 0.0001f)
            return Mathf.RoundToInt(position.z);

        return Mathf.RoundToInt(position.z / StoredOrderScale);
    }

    public static float EncodeBoneOrder(int order)
    {
        return order * StoredOrderScale;
    }

    public static int[] SortTrianglesByBoneOrder(
        IReadOnlyList<int> triangles,
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<int> rendererBoneOrders)
    {
        if (triangles == null || triangles.Count == 0)
            return Array.Empty<int>();

        var result = new int[triangles.Count];
        for (var i = 0; i < triangles.Count; i++)
            result[i] = triangles[i];

        if (triangles.Count < 3 || boneWeights == null || boneWeights.Count == 0 ||
            spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0 ||
            rendererBoneOrders == null || rendererBoneOrders.Count == 0)
        {
            return result;
        }

        var triangleCount = triangles.Count / 3;
        var weightedTriangles = new List<WeightedTriangle>(triangleCount);
        for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            var offset = triangleIndex * 3;
            var order = (
                EvaluateVertexOrder(triangles[offset], boneWeights, spriteBoneToRendererBone, rendererBoneOrders) +
                EvaluateVertexOrder(triangles[offset + 1], boneWeights, spriteBoneToRendererBone, rendererBoneOrders) +
                EvaluateVertexOrder(triangles[offset + 2], boneWeights, spriteBoneToRendererBone, rendererBoneOrders)) / 3f;
            weightedTriangles.Add(new WeightedTriangle(offset, order));
        }

        weightedTriangles.Sort((lhs, rhs) =>
        {
            var comparison = lhs.order.CompareTo(rhs.order);
            return comparison != 0 ? comparison : lhs.sourceOffset.CompareTo(rhs.sourceOffset);
        });

        var writeOffset = 0;
        for (var i = 0; i < weightedTriangles.Count; i++)
        {
            var sourceOffset = weightedTriangles[i].sourceOffset;
            result[writeOffset++] = triangles[sourceOffset];
            result[writeOffset++] = triangles[sourceOffset + 1];
            result[writeOffset++] = triangles[sourceOffset + 2];
        }

        return result;
    }

    public static List<SpriteBone> ApplyRendererBoneOrders(
        IReadOnlyList<SpriteBone> spriteBones,
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<int> rendererBoneOrders,
        out bool changed)
    {
        changed = false;
        var count = spriteBones != null ? spriteBones.Count : 0;
        var result = new List<SpriteBone>(count);
        for (var i = 0; i < count; i++)
        {
            var spriteBone = spriteBones[i];
            if (spriteBoneToRendererBone != null && i < spriteBoneToRendererBone.Count)
            {
                var rendererBoneIndex = spriteBoneToRendererBone[i];
                if (rendererBoneIndex >= 0 && rendererBoneOrders != null && rendererBoneIndex < rendererBoneOrders.Count)
                {
                    var desiredOrder = EncodeBoneOrder(rendererBoneOrders[rendererBoneIndex]);
                    if (Mathf.Abs(spriteBone.position.z - desiredOrder) > 0.0001f)
                    {
                        spriteBone.position = new Vector3(spriteBone.position.x, spriteBone.position.y, desiredOrder);
                        changed = true;
                    }
                }
            }

            result.Add(spriteBone);
        }

        return result;
    }

    public static bool AreIndicesEqual(IReadOnlyList<int> lhs, IReadOnlyList<int> rhs)
    {
        var lhsCount = lhs != null ? lhs.Count : 0;
        var rhsCount = rhs != null ? rhs.Count : 0;
        if (lhsCount != rhsCount)
            return false;

        for (var i = 0; i < lhsCount; i++)
        {
            if (lhs[i] != rhs[i])
                return false;
        }

        return true;
    }

    private static float EvaluateVertexOrder(
        int vertexIndex,
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<int> rendererBoneOrders)
    {
        if (vertexIndex < 0 || vertexIndex >= boneWeights.Count)
            return 0f;

        var weight = boneWeights[vertexIndex];
        return EvaluateChannel(weight.boneIndex0, weight.weight0, spriteBoneToRendererBone, rendererBoneOrders) +
               EvaluateChannel(weight.boneIndex1, weight.weight1, spriteBoneToRendererBone, rendererBoneOrders) +
               EvaluateChannel(weight.boneIndex2, weight.weight2, spriteBoneToRendererBone, rendererBoneOrders) +
               EvaluateChannel(weight.boneIndex3, weight.weight3, spriteBoneToRendererBone, rendererBoneOrders);
    }

    private static float EvaluateChannel(
        int spriteBoneIndex,
        float weight,
        IReadOnlyList<int> spriteBoneToRendererBone,
        IReadOnlyList<int> rendererBoneOrders)
    {
        if (weight <= 0f || spriteBoneIndex < 0 || spriteBoneIndex >= spriteBoneToRendererBone.Count)
            return 0f;

        var rendererBoneIndex = spriteBoneToRendererBone[spriteBoneIndex];
        if (rendererBoneIndex < 0 || rendererBoneIndex >= rendererBoneOrders.Count)
            return 0f;

        return rendererBoneOrders[rendererBoneIndex] * weight;
    }
}
#endif
