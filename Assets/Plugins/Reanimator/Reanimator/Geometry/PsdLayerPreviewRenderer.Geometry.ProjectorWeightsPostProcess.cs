#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const int ProjectorPostProcessIterationCount = 10;
    private const float ProjectorPostProcessScaleStep = 0.1f;
    private const float ProjectorPostProcessSmoothStrength = 0.5f;

    private void ApplyExpandedProjectorCoveragePostProcess(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        BoneWeight[] nextWeights,
        bool[] projectedVertexMask,
        bool smoothWeights,
        ref int weightedCount,
        ref bool changed)
    {
        var vertexCount = targetData != null && targetData.vertices != null ? targetData.vertices.Length : 0;
        if (vertexCount <= 0 ||
            sources == null ||
            sources.Count == 0 ||
            nextWeights == null ||
            nextWeights.Length != vertexCount ||
            projectedVertexMask == null ||
            projectedVertexMask.Length != vertexCount)
        {
            return;
        }

        for (var iteration = 1; iteration <= ProjectorPostProcessIterationCount; iteration++)
        {
            if (!HasMissingProjectorWeights(nextWeights, vertexCount))
                break;

            var scale = 1f + ProjectorPostProcessScaleStep * iteration;
            var expandedSources = BuildExpandedProjectorSources(sources, scale);
            for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (IsUsableVertexWeight(nextWeights[vertexIndex]))
                    continue;

                var point = targetData.vertices[vertexIndex];
                for (var sourceIndex = 0; sourceIndex < expandedSources.Count; sourceIndex++)
                {
                    var expandedSource = expandedSources[sourceIndex];
                    if (!IsPointInsideProjectorGeometry(expandedSource, point))
                        continue;
                    if (!TryTransferWeightFromSource(point, expandedSource, true, out var sourceWeight))
                        continue;
                    if (!TryRemapProjectorWeight(sourceWeight, expandedSource, targetData, out sourceWeight, out var expandedTargetBones))
                        continue;

                    if (expandedTargetBones || !AreBoneWeightsApproximatelyEqual(nextWeights[vertexIndex], sourceWeight))
                        changed = true;

                    nextWeights[vertexIndex] = sourceWeight;
                    projectedVertexMask[vertexIndex] = true;
                    weightedCount++;
                    break;
                }
            }

            if (smoothWeights && SmoothProjectorWeightsOnce(targetData, nextWeights))
                changed = true;
        }
    }

    private static bool SmoothProjectorWeightsOnce(
        LayerVertexData targetData,
        BoneWeight[] weights)
    {
        var vertexCount = targetData != null && targetData.vertices != null ? targetData.vertices.Length : 0;
        var spriteBoneCount = targetData != null && targetData.spriteBoneToRendererBone != null
            ? targetData.spriteBoneToRendererBone.Length
            : 0;
        if (vertexCount <= 0 || weights == null || weights.Length != vertexCount || spriteBoneCount <= 0)
            return false;

        var sourceWeights = (BoneWeight[])weights.Clone();
        var changed = false;
        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            // Keep uncovered vertices empty so smoothing cannot replace the expanded-geometry coverage pass.
            if (!IsUsableVertexWeight(sourceWeights[vertexIndex]))
                continue;

            if (!ReanimatorWeightSliderAlgorithms.TryBuildSmoothedBoneWeight(
                    sourceWeights,
                    vertexIndex,
                    targetData.allEdgePairs,
                    targetData.triangles,
                    spriteBoneCount,
                    ProjectorPostProcessSmoothStrength,
                    out var smoothed))
            {
                continue;
            }

            if (AreBoneWeightsApproximatelyEqual(sourceWeights[vertexIndex], smoothed))
                continue;

            weights[vertexIndex] = smoothed;
            changed = true;
        }

        return changed;
    }

    private static List<LayerVertexData> BuildExpandedProjectorSources(
        IReadOnlyList<LayerVertexData> sources,
        float scale)
    {
        var result = new List<LayerVertexData>(sources != null ? sources.Count : 0);
        if (sources == null)
            return result;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (!HasProjectorGeometry(source))
                continue;

            var expanded = CloneProjectorLayerData(source);
            expanded.vertices = ReanimatorProjectorAlgorithms.ScaleVerticesAroundBoundsCenter(source.vertices, scale);
            result.Add(expanded);
        }

        return result;
    }

    private static bool HasMissingProjectorWeights(IReadOnlyList<BoneWeight> weights, int vertexCount)
    {
        if (weights == null || weights.Count < vertexCount)
            return true;

        for (var i = 0; i < vertexCount; i++)
        {
            if (!IsUsableVertexWeight(weights[i]))
                return true;
        }

        return false;
    }
}
#endif
