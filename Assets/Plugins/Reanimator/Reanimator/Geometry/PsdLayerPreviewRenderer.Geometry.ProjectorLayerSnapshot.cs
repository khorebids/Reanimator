#if UNITY_EDITOR
using System;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private sealed class ProjectorLayerDataSnapshot
    {
        private readonly Vector2[] vertices;
        private readonly Vector2[] vertexUvs;
        private readonly BoneWeight[] boneWeights;
        private readonly int[] triangles;
        private readonly int[] allEdgePairs;
        private readonly int[] boundaryEdgePairs;
        private readonly int[] spriteBoneToRendererBone;
        private readonly Matrix4x4[] bindBoneInverseMatrices;
        private readonly bool usesSpriteBindPoses;
        private readonly bool hasSkinning;
        private readonly Vector2[] deformedVertices;
        private readonly Matrix4x4[] skinMatrices;
        private readonly int lastPoseVersion;

        public int[] BoundaryEdgePairs => boundaryEdgePairs;

        private ProjectorLayerDataSnapshot(LayerVertexData source)
        {
            vertices = source.vertices;
            vertexUvs = source.vertexUvs;
            boneWeights = source.boneWeights;
            triangles = source.triangles;
            allEdgePairs = source.allEdgePairs;
            boundaryEdgePairs = source.boundaryEdgePairs;
            spriteBoneToRendererBone = source.spriteBoneToRendererBone;
            bindBoneInverseMatrices = source.bindBoneInverseMatrices;
            usesSpriteBindPoses = source.usesSpriteBindPoses;
            hasSkinning = source.hasSkinning;
            deformedVertices = source.deformedVertices;
            skinMatrices = source.skinMatrices;
            lastPoseVersion = source.lastPoseVersion;
        }

        public static ProjectorLayerDataSnapshot Capture(LayerVertexData source)
        {
            return new ProjectorLayerDataSnapshot(source);
        }

        public void Restore(LayerVertexData target)
        {
            if (target == null)
                return;

            target.vertices = vertices;
            target.vertexUvs = vertexUvs;
            target.boneWeights = boneWeights;
            target.triangles = triangles;
            target.allEdgePairs = allEdgePairs;
            target.boundaryEdgePairs = boundaryEdgePairs;
            target.spriteBoneToRendererBone = spriteBoneToRendererBone;
            target.bindBoneInverseMatrices = bindBoneInverseMatrices;
            target.usesSpriteBindPoses = usesSpriteBindPoses;
            target.hasSkinning = hasSkinning;
            target.deformedVertices = deformedVertices;
            target.skinMatrices = skinMatrices;
            target.lastPoseVersion = lastPoseVersion;
        }
    }

    private static LayerVertexData CloneProjectorLayerData(LayerVertexData source)
    {
        if (source == null)
            return null;

        return new LayerVertexData
        {
            layer = source.layer,
            texture = source.texture,
            pixelsPerUnit = source.pixelsPerUnit,
            pivotPixels = source.pivotPixels,
            vertices = CloneArray(source.vertices) ?? Array.Empty<Vector2>(),
            vertexUvs = CloneArray(source.vertexUvs) ?? Array.Empty<Vector2>(),
            triangles = CloneArray(source.triangles) ?? Array.Empty<int>(),
            allEdgePairs = CloneArray(source.allEdgePairs) ?? Array.Empty<int>(),
            boundaryEdgePairs = CloneArray(source.boundaryEdgePairs) ?? Array.Empty<int>(),
            boneWeights = CloneArray(source.boneWeights) ?? Array.Empty<BoneWeight>(),
            spriteBoneToRendererBone = CloneArray(source.spriteBoneToRendererBone) ?? Array.Empty<int>(),
            bindBoneInverseMatrices = CloneArray(source.bindBoneInverseMatrices) ?? Array.Empty<Matrix4x4>(),
            usesSpriteBindPoses = source.usesSpriteBindPoses,
            hasSkinning = source.hasSkinning,
            lastPoseVersion = source.lastPoseVersion,
            deformedVertices = null,
            skinMatrices = null
        };
    }
}
#endif
