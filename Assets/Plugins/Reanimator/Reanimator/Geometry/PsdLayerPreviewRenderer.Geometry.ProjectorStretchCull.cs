#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const string ProjectorStretchNoChangesMessage = "Projector Stretch made no geometry changes.";

    public bool ApplyProjectorVertexCull(
        IReadOnlyList<string> sourceSelectionKeys,
        out int removedCount,
        out string targetLayerName,
        out string error)
    {
        removedCount = 0;
        targetLayerName = string.Empty;
        error = null;
        if (!TryGetWeightBrushLayerData(out _, out var targetSelectionKey, out _))
        {
            error = "Select sprite with geometry.";
            return false;
        }

        return ApplyProjectorVertexCull(targetSelectionKey, sourceSelectionKeys, out removedCount, out targetLayerName, out error);
    }

    public bool ApplyProjectorVertexCull(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        out int removedCount,
        out string targetLayerName,
        out string error)
    {
        removedCount = 0;
        targetLayerName = string.Empty;
        error = null;

        if (!TryGetProjectorTargetAndSources(
                targetSelectionKey,
                sourceSelectionKeys,
                false,
                out var targetData,
                out var resolvedTargetSelectionKey,
                out var targetStableKey,
                out var sources,
                out targetLayerName,
                out error))
        {
            return false;
        }

        return TryCullProjectorTargetVertices(
            targetData,
            resolvedTargetSelectionKey,
            targetStableKey,
            sources,
            true,
            true,
            "Projector Vertex Cull",
            out removedCount,
            out error);
    }

    public bool ApplyProjectorStretch(
        IReadOnlyList<string> sourceSelectionKeys,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string targetLayerName,
        out string error)
    {
        insertedCount = 0;
        removedCount = 0;
        targetLayerName = string.Empty;
        error = null;
        if (!TryGetWeightBrushLayerData(out _, out var targetSelectionKey, out _))
        {
            error = "Select sprite with geometry.";
            return false;
        }

        return ApplyProjectorStretch(targetSelectionKey, sourceSelectionKeys, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out insertedCount, out removedCount, out targetLayerName, out error);
    }

    public bool ApplyProjectorStretch(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string targetLayerName,
        out string error)
    {
        return ApplyProjectorStretch(
            targetSelectionKey,
            sourceSelectionKeys,
            false,
            sourceInsideTargetOnly,
            preserveContour,
            useVertexCull,
            vertexCullDistance,
            out insertedCount,
            out removedCount,
            out targetLayerName,
            out error);
    }

    public bool ApplyProjectorStretch(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool useApl,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string targetLayerName,
        out string error)
    {
        insertedCount = 0;
        removedCount = 0;
        targetLayerName = string.Empty;
        error = null;

        if (useApl && !TryPrepareProjectorAplTargetGeometry(targetSelectionKey, out error))
            return false;

        if (!TryGetProjectorTargetAndSources(
                targetSelectionKey,
                sourceSelectionKeys,
                false,
                out var targetData,
                out var resolvedTargetSelectionKey,
                out var targetStableKey,
                out var sources,
                out targetLayerName,
                out error))
        {
            return false;
        }

        var dryRun = CloneProjectorLayerData(targetData);
        if (!TryBuildProjectorStretchMergedGeometry(dryRun, sources, useApl, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out insertedCount, out removedCount, out error))
            return false;
        if (useApl && !TryTransferProjectorAplWeightsToResult(dryRun, sources))
        {
            error = null;
            return false;
        }

        // APL can change triangle connectivity and/or weights without changing
        // the vertex set. Only reject a dry run when every persisted value is
        // already identical to the projected result.
        if (insertedCount <= 0 && removedCount <= 0)
        {
            if (!HasProjectorStretchLayerChanges(targetData, dryRun))
            {
                error = ProjectorStretchNoChangesMessage;
                return false;
            }
        }

        RegisterUndo("Projector Stretch");
        if (!TryBuildProjectorStretchMergedGeometry(targetData, sources, useApl, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out insertedCount, out removedCount, out error))
            return false;
        if (useApl && !TryTransferProjectorAplWeightsToResult(targetData, sources))
        {
            error = null;
            return false;
        }

        if (targetData.vertexUvs == null || targetData.vertexUvs.Length != targetData.vertices.Length)
            targetData.vertexUvs = BuildVertexUvsFromLayer(targetData.layer, targetData.vertices);
        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);
        EnsureLayerBindMatricesForWeights(targetData);
        MarkSkinningWeightsChanged(targetData);
        MarkEditGeometryDataChanged(targetData, resolvedTargetSelectionKey, targetStableKey);
        return true;
    }

    private static bool HasProjectorStretchLayerChanges(LayerVertexData source, LayerVertexData candidate)
    {
        if (source == null || candidate == null)
            return source != candidate;

        if (HasProjectorStretchTopologyChanges(source, candidate) ||
            !ProjectorIntArraysMatch(source.spriteBoneToRendererBone, candidate.spriteBoneToRendererBone))
        {
            return true;
        }

        var sourceWeights = source.boneWeights;
        var candidateWeights = candidate.boneWeights;
        if (sourceWeights == null || candidateWeights == null)
            return sourceWeights != candidateWeights;
        if (sourceWeights.Length != candidateWeights.Length)
            return true;
        for (var i = 0; i < sourceWeights.Length; i++)
        {
            if (!AreBoneWeightsApproximatelyEqual(sourceWeights[i], candidateWeights[i]))
                return true;
        }
        return false;
    }

    private static bool HasProjectorStretchTopologyChanges(LayerVertexData source, LayerVertexData candidate)
    {
        if (source == null || candidate == null)
            return source != candidate;

        return !ProjectorVectorArraysMatch(source.vertices, candidate.vertices) ||
               !ProjectorIntArraysMatch(source.triangles, candidate.triangles) ||
               !ProjectorIntArraysMatch(source.allEdgePairs, candidate.allEdgePairs) ||
               !ProjectorIntArraysMatch(source.boundaryEdgePairs, candidate.boundaryEdgePairs);
    }

    private static bool ProjectorVectorArraysMatch(IReadOnlyList<Vector2> lhs, IReadOnlyList<Vector2> rhs)
    {
        if (lhs == null || rhs == null)
            return lhs == rhs;
        if (lhs.Count != rhs.Count)
            return false;
        for (var i = 0; i < lhs.Count; i++)
        {
            if ((lhs[i] - rhs[i]).sqrMagnitude > 0.00000001f)
                return false;
        }
        return true;
    }

    private static bool ProjectorIntArraysMatch(IReadOnlyList<int> lhs, IReadOnlyList<int> rhs)
    {
        if (lhs == null || rhs == null)
            return lhs == rhs;
        if (lhs.Count != rhs.Count)
            return false;
        for (var i = 0; i < lhs.Count; i++)
        {
            if (lhs[i] != rhs[i])
                return false;
        }
        return true;
    }

    internal void DrawProjectorStretchPreviewOverlay(
        ReanimatorPreviewContext context,
        IReadOnlyList<string> sourceSelectionKeys,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance)
    {
        if (!TryGetWeightBrushLayerData(out _, out var targetSelectionKey, out _))
            return;

        DrawProjectorStretchPreviewOverlay(targetSelectionKey, context, sourceSelectionKeys, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance);
    }

    internal void DrawProjectorStretchPreviewOverlay(
        string targetSelectionKey,
        ReanimatorPreviewContext context,
        IReadOnlyList<string> sourceSelectionKeys,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance)
    {
        DrawProjectorStretchPreviewOverlay(
            targetSelectionKey,
            context,
            sourceSelectionKeys,
            false,
            sourceInsideTargetOnly,
            preserveContour,
            useVertexCull,
            vertexCullDistance);
    }

    internal void DrawProjectorStretchPreviewOverlay(
        string targetSelectionKey,
        ReanimatorPreviewContext context,
        IReadOnlyList<string> sourceSelectionKeys,
        bool useApl,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || context == null || context.Coordinates == null)
            return;
        if (sourceSelectionKeys == null || sourceSelectionKeys.Count == 0)
            return;
        if (!TryGetProjectorTargetAndSources(
                targetSelectionKey,
                sourceSelectionKeys,
                false,
                out var targetData,
                out _,
                out _,
                out var sources,
                out _,
                out _))
        {
            return;
        }

        var previewData = CloneProjectorLayerData(targetData);
        if (!TryBuildProjectorStretchMergedGeometry(previewData, sources, useApl, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out var insertedCount, out var removedCount, out _) ||
            insertedCount <= 0 && removedCount <= 0)
            return;

        DrawProjectorStretchPreviewGeometry(context, targetData, previewData);
    }

    internal void DrawProjectorCandidateHoverOverlay(ReanimatorPreviewContext context, string sourceSelectionKey)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || context == null || context.Coordinates == null)
            return;
        if (string.IsNullOrEmpty(sourceSelectionKey))
            return;
        if (!TryResolveProjectorSource(sourceSelectionKey, out _, out var sourceData) || sourceData == null)
            return;

        DrawProjectorCandidateHoverGeometry(context, sourceData);
    }

    private bool TryBuildProjectorStretchMergedGeometry(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        bool useApl,
        bool sourceInsideTargetOnly,
        bool preserveContour,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string error)
    {
        insertedCount = 0;
        removedCount = 0;
        error = null;
        if (!HasProjectorGeometry(targetData))
        {
            error = "Selected sprite has no geometry.";
            return false;
        }

        if (sources == null || sources.Count == 0)
        {
            error = "Select source geometry.";
            return false;
        }

        if (useApl)
        {
            return TryBuildProjectorAplGeometry(
                targetData,
                sources,
                useVertexCull,
                vertexCullDistance,
                out insertedCount,
                out removedCount,
                out error);
        }

        var previousGeometry = ProjectorLayerDataSnapshot.Capture(targetData);

        var sourceVertices = targetData.vertices ?? Array.Empty<Vector2>();
        var vertices = new List<Vector2>(sourceVertices.Length);
        var uvs = new List<Vector2>(sourceVertices.Length);
        var weights = new List<BoneWeight>(sourceVertices.Length);
        var hasTargetWeights = targetData.boneWeights != null && targetData.boneWeights.Length == sourceVertices.Length;

        for (var i = 0; i < sourceVertices.Length; i++)
        {
            vertices.Add(sourceVertices[i]);
            uvs.Add(targetData.vertexUvs != null && i < targetData.vertexUvs.Length
                ? targetData.vertexUvs[i]
                : ComputeUvForPoint(targetData.layer, sourceVertices[i]));
            weights.Add(hasTargetWeights ? targetData.boneWeights[i] : default);
        }

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var sourceData = sources[sourceIndex];
            if (!HasProjectorGeometry(sourceData) || sourceData.vertices == null)
                continue;

            for (var vertexIndex = 0; vertexIndex < sourceData.vertices.Length; vertexIndex++)
            {
                var point = sourceData.vertices[vertexIndex];
                if (sourceInsideTargetOnly && !IsPointInsideProjectorGeometry(targetData, point))
                    continue;
                if (ContainsProjectorStretchPoint(vertices, point))
                    continue;

                var weight = default(BoneWeight);
                if (TryGetProjectorSourceVertexWeight(sourceData, vertexIndex, out var sourceWeight) &&
                    TryRemapProjectorWeight(sourceWeight, sourceData, targetData, out var remappedWeight, out _))
                {
                    weight = remappedWeight;
                }
                else if (hasTargetWeights)
                {
                    var nearestTarget = FindNearestVertexIndex(point, sourceVertices);
                    if (nearestTarget >= 0 && nearestTarget < targetData.boneWeights.Length)
                        weight = targetData.boneWeights[nearestTarget];
                }

                vertices.Add(point);
                uvs.Add(ComputeUvForPoint(targetData.layer, point));
                weights.Add(weight);
                insertedCount++;
            }
        }

        if (insertedCount <= 0)
        {
            previousGeometry.Restore(targetData);
            error = ProjectorStretchNoChangesMessage;
            return false;
        }

        int[] preservedBoundaryPairs = null;
        if (preserveContour)
        {
            preservedBoundaryPairs = CloneArray(previousGeometry.BoundaryEdgePairs) ?? Array.Empty<int>();
            if (preservedBoundaryPairs.Length < 6)
            {
                previousGeometry.Restore(targetData);
                error = "Selected sprite has no contour to preserve.";
                return false;
            }
        }
        else
        {
            var boundaryLoop = BuildProjectorStretchBoundaryLoop(vertices);
            if (boundaryLoop.Count < 3)
            {
                previousGeometry.Restore(targetData);
                error = "Projector: merged contour is unavailable.";
                return false;
            }

            preservedBoundaryPairs = BuildBoundaryPairsFromLoop(boundaryLoop);
        }

        targetData.vertices = vertices.ToArray();
        targetData.vertexUvs = uvs.ToArray();
        targetData.boneWeights = weights.ToArray();
        targetData.boundaryEdgePairs = preservedBoundaryPairs;
        targetData.triangles = Array.Empty<int>();
        targetData.allEdgePairs = Array.Empty<int>();
        targetData.deformedVertices = null;
        targetData.skinMatrices = null;
        targetData.lastPoseVersion = -1;

        if (useVertexCull &&
            !TryApplyProjectorStretchVertexCull(targetData, vertexCullDistance, preserveContour, ref preservedBoundaryPairs, out removedCount, out error))
        {
            previousGeometry.Restore(targetData);
            return false;
        }

        if (!TryRetriangulateKeepingVertexSet(targetData) && !TryRetriangulateWithDirectFallback(targetData))
        {
            previousGeometry.Restore(targetData);
            error = preserveContour
                ? "Projector: preserved-contour geometry is unavailable."
                : "Projector: merged geometry is unavailable.";
            return false;
        }

        targetData.triangles = SanitizeTriangles(targetData.triangles, targetData.vertices.Length);
        if (targetData.triangles.Length < 3)
        {
            previousGeometry.Restore(targetData);
            error = "Projector: valid merged triangles are unavailable.";
            return false;
        }

        BuildEdgePairs(targetData.triangles, targetData.vertices.Length, out targetData.allEdgePairs, out var rebuiltBoundaryPairs);
        targetData.boundaryEdgePairs = preserveContour ? preservedBoundaryPairs : rebuiltBoundaryPairs;
        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);
        return true;
    }

    private bool TryApplyProjectorStretchVertexCull(
        LayerVertexData data,
        float distance,
        bool preserveContour,
        ref int[] preservedBoundaryPairs,
        out int removedCount,
        out string error)
    {
        return TryApplyProjectorStretchVertexCull(
            data,
            distance,
            preserveContour,
            ref preservedBoundaryPairs,
            null,
            out removedCount,
            out error);
    }

    private bool TryApplyProjectorStretchVertexCull(
        LayerVertexData data,
        float distance,
        bool preserveContour,
        ref int[] preservedBoundaryPairs,
        ISet<int> protectedVertexIndices,
        out int removedCount,
        out string error)
    {
        removedCount = 0;
        error = null;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return true;
        if (!TryBuildVertexCullClusters(data.vertices, distance, out var groups, out var remap))
            return true;

        if (protectedVertexIndices != null && protectedVertexIndices.Count > 0)
            SplitProjectorStretchProtectedCullGroups(groups, data.vertices.Length, protectedVertexIndices, out remap);

        removedCount = data.vertices.Length - groups.Count;
        if (removedCount <= 0)
            return true;
        if (groups.Count < 3)
        {
            error = "Vertex Cull merged too many stretch vertices.";
            return false;
        }

        var newVertices = new Vector2[groups.Count];
        var newUvs = new Vector2[groups.Count];
        var newWeights = new BoneWeight[groups.Count];
        var hasWeights = data.boneWeights != null && data.boneWeights.Length == data.vertices.Length;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            newVertices[groupIndex] = AverageVertexCullPoints(data.vertices, group);
            newUvs[groupIndex] = AverageVertexCullUvs(data, group, newVertices[groupIndex]);
            newWeights[groupIndex] = hasWeights
                ? MergeVertexCullBoneWeights(data.boneWeights, group)
                : default;
        }

        data.vertices = newVertices;
        data.vertexUvs = newUvs;
        data.boneWeights = newWeights;
        data.triangles = RemapVertexCullTriangles(data.triangles, remap, newVertices.Length);
        data.allEdgePairs = RemapVertexCullEdgePairs(data.allEdgePairs, remap, newVertices.Length);

        var remappedBoundaryPairs = RemapVertexCullEdgePairs(data.boundaryEdgePairs, remap, newVertices.Length);
        if (preserveContour)
        {
            preservedBoundaryPairs = RemapVertexCullEdgePairs(preservedBoundaryPairs, remap, newVertices.Length);
            if (preservedBoundaryPairs == null || preservedBoundaryPairs.Length < 6)
            {
                error = "Vertex Cull collapsed preserved contour.";
                return false;
            }
            data.boundaryEdgePairs = preservedBoundaryPairs;
        }
        else
        {
            data.boundaryEdgePairs = remappedBoundaryPairs;
        }

        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        return true;
    }

    private static void SplitProjectorStretchProtectedCullGroups(
        List<List<int>> groups,
        int vertexCount,
        ISet<int> protectedVertexIndices,
        out int[] remap)
    {
        var protectedGroups = new List<List<int>>(groups.Count);
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var unprotected = new List<int>();
            for (var memberIndex = 0; memberIndex < group.Count; memberIndex++)
            {
                var vertexIndex = group[memberIndex];
                if (protectedVertexIndices.Contains(vertexIndex))
                    protectedGroups.Add(new List<int> { vertexIndex });
                else
                    unprotected.Add(vertexIndex);
            }

            if (unprotected.Count > 0)
                protectedGroups.Add(unprotected);
        }

        groups.Clear();
        groups.AddRange(protectedGroups);
        remap = new int[Mathf.Max(0, vertexCount)];
        for (var vertexIndex = 0; vertexIndex < remap.Length; vertexIndex++)
            remap[vertexIndex] = -1;
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            for (var memberIndex = 0; memberIndex < group.Count; memberIndex++)
                remap[group[memberIndex]] = groupIndex;
        }
    }

    private static bool ContainsProjectorStretchPoint(IReadOnlyList<Vector2> vertices, Vector2 point)
    {
        return ReanimatorProjectorAlgorithms.ContainsStretchPoint(vertices, point);
    }

    private static bool TryGetProjectorSourceVertexWeight(LayerVertexData sourceData, int vertexIndex, out BoneWeight weight)
    {
        return ReanimatorProjectorAlgorithms.TryGetSourceVertexWeight(
            sourceData != null ? sourceData.vertices : null,
            sourceData != null ? sourceData.boneWeights : null,
            vertexIndex,
            out weight);
    }

    private static List<int> BuildProjectorStretchBoundaryLoop(IReadOnlyList<Vector2> vertices)
    {
        return ReanimatorProjectorAlgorithms.BuildStretchBoundaryLoop(vertices);
    }

    private void DrawProjectorStretchPreviewGeometry(
        ReanimatorPreviewContext context,
        LayerVertexData targetData,
        LayerVertexData previewData)
    {
        if (context == null || context.Coordinates == null || targetData == null || previewData == null)
            return;
        if (previewData.vertices == null || previewData.vertices.Length < 3)
            return;

        var alpha = context.Visibility != null ? Mathf.Clamp01(context.Visibility.VertexAlpha) : 1f;
        if (alpha <= 0.0001f)
            return;

        var toCanvasVertex = CreateLayerVertexToViewMapper(context.Coordinates, previewData, previewData.vertices);
        DrawBoneInfluenceOnGeometryTriangles(
            toCanvasVertex,
            previewData.vertices,
            previewData,
            alpha,
            int.MaxValue,
            0.55f);

        var vertexCount = previewData.vertices.Length;
        var contourEdgeKeys = BuildEdgeKeySet(previewData.boundaryEdgePairs, vertexCount);
        var innerLineColor = new Color(0.1f, 0.95f, 1f, Mathf.Clamp01(alpha));
        var boundaryLineColor = new Color(1f, 0.82f, 0.14f, Mathf.Clamp01(alpha));
        var pointColor = new Color(0.45f, 1f, 0.65f, Mathf.Clamp01(alpha));
        const float markerHalfSize = 2.6f;
        const float thickOffset = 0.7f;

        Handles.BeginGUI();
        var prev = Handles.color;

        var edgePairCount = previewData.allEdgePairs != null ? previewData.allEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexEdgeSegments, edgePairCount * 2);
        var edgeWrite = 0;
        for (var i = 0; i < edgePairCount; i++)
        {
            var a = previewData.allEdgePairs[i * 2];
            var b = previewData.allEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                continue;

            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewData.vertices[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(previewData.vertices[b]);
        }

        if (edgeWrite > 1)
        {
            Handles.color = innerLineColor;
            Handles.DrawLines(vertexEdgeSegments);
        }

        var boundaryPairCount = previewData.boundaryEdgePairs != null ? previewData.boundaryEdgePairs.Length / 2 : 0;
        EnsureVertexBuffer(ref vertexBoundarySegments, boundaryPairCount * 2);
        var boundaryWrite = 0;
        for (var i = 0; i < boundaryPairCount; i++)
        {
            var a = previewData.boundaryEdgePairs[i * 2];
            var b = previewData.boundaryEdgePairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;

            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(previewData.vertices[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(previewData.vertices[b]);
        }

        if (boundaryWrite > 1)
        {
            Handles.color = boundaryLineColor;
            Handles.DrawLines(vertexBoundarySegments);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }

        EnsureVertexBuffer(ref vertexPointSegments, vertexCount * 4);
        var pointWrite = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = toCanvasVertex(previewData.vertices[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }

        if (pointWrite > 1)
        {
            Handles.color = pointColor;
            Handles.DrawLines(vertexPointSegments);
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private bool TryGetProjectorTargetAndSources(
        string explicitTargetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool requireWeights,
        out LayerVertexData targetData,
        out string targetSelectionKey,
        out string targetStableKey,
        out List<LayerVertexData> sources,
        out string targetLayerName,
        out string error)
    {
        targetData = null;
        targetSelectionKey = string.Empty;
        targetStableKey = string.Empty;
        sources = new List<LayerVertexData>();
        targetLayerName = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(explicitTargetSelectionKey))
        {
            error = "Set target sprite first.";
            return false;
        }

        if (sourceSelectionKeys == null || sourceSelectionKeys.Count == 0)
        {
            error = "Select source geometry.";
            return false;
        }

        targetSelectionKey = explicitTargetSelectionKey;
        if (!TryResolveProjectorLayer(targetSelectionKey, out targetStableKey, out targetData) || targetData == null)
        {
            error = "Set a valid target sprite.";
            return false;
        }

        targetLayerName = GetLayerDisplayName(targetData.layer);
        if (!HasProjectorGeometry(targetData))
        {
            error = "Target sprite has no geometry.";
            return false;
        }

        sources = BuildProjectorSourceList(sourceSelectionKeys, targetSelectionKey, targetStableKey, requireWeights, targetData);
        if (sources.Count == 0)
        {
            error = "Selected sources do not intersect target or have no geometry.";
            return false;
        }

        return true;
    }

    private bool TryCullProjectorTargetVertices(
        LayerVertexData targetData,
        string targetSelectionKey,
        string targetStableKey,
        IReadOnlyList<LayerVertexData> sources,
        bool failIfNoVertices,
        bool registerUndo,
        string undoName,
        out int removedCount,
        out string error)
    {
        removedCount = 0;
        error = null;
        if (targetData == null || targetData.vertices == null || targetData.vertices.Length < 3)
        {
            error = "Selected sprite has no geometry.";
            return false;
        }

        var vertexCount = targetData.vertices.Length;
        var removeMask = new bool[vertexCount];
        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            var point = targetData.vertices[vertexIndex];
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                if (!IsPointInsideProjectorGeometry(sources[sourceIndex], point))
                    continue;

                removeMask[vertexIndex] = true;
                break;
            }
        }

        for (var i = 0; i < removeMask.Length; i++)
        {
            if (removeMask[i])
                removedCount++;
        }

        if (removedCount <= 0)
        {
            if (failIfNoVertices)
            {
                error = "No target vertices covered by selected geometry.";
                return false;
            }

            return true;
        }

        if (registerUndo && !string.IsNullOrEmpty(undoName))
            RegisterUndo(undoName);

        return TryCullVerticesByMask(targetData, targetSelectionKey, targetStableKey, removeMask, out removedCount, out error);
    }

    private bool TryCullVerticesByMask(
        LayerVertexData data,
        string selectionKey,
        string stableKey,
        bool[] removeMask,
        out int removedCount,
        out string error)
    {
        removedCount = 0;
        error = null;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
        {
            error = "Geometry is empty.";
            return false;
        }

        var vertexCount = data.vertices.Length;
        if (removeMask == null || removeMask.Length != vertexCount)
        {
            error = "Cull mask is invalid.";
            return false;
        }

        var contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
        if (CountTrue(contourMask) < 3 && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out _, out var fallbackBoundaryPairs);
            if (fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6)
            {
                data.boundaryEdgePairs = fallbackBoundaryPairs;
                contourMask = BuildContourVertexMask(data.boundaryEdgePairs, vertexCount);
            }
        }

        if (CountTrue(contourMask) < 3)
        {
            error = "Projector: contour is unavailable.";
            return false;
        }

        for (var i = 0; i < removeMask.Length; i++)
        {
            if (removeMask[i])
                removedCount++;
        }

        if (removedCount <= 0)
        {
            error = "No vertices matched.";
            return false;
        }

        var remainingVertexCount = vertexCount - removedCount;
        if (remainingVertexCount < 3)
        {
            error = "Too many vertices removed.";
            return false;
        }

        if (!TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, vertexCount, out var boundaryLoop))
            boundaryLoop = BuildFallbackBoundaryLoop(data.vertices, contourMask);

        var remap = new int[vertexCount];
        for (var i = 0; i < remap.Length; i++)
            remap[i] = -1;

        var keptVertices = new List<Vector2>(remainingVertexCount);
        var keptUvs = new List<Vector2>(remainingVertexCount);
        var keptWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount
            ? new List<BoneWeight>(remainingVertexCount)
            : null;
        for (var i = 0; i < vertexCount; i++)
        {
            if (removeMask[i])
                continue;

            remap[i] = keptVertices.Count;
            keptVertices.Add(data.vertices[i]);
            keptUvs.Add(data.vertexUvs != null && i < data.vertexUvs.Length
                ? data.vertexUvs[i]
                : ComputeUvForPoint(data.layer, data.vertices[i]));
            if (keptWeights != null)
                keptWeights.Add(data.boneWeights[i]);
        }

        var newBoundaryLoop = BuildFilteredBoundaryLoop(boundaryLoop, removeMask, remap);
        if (newBoundaryLoop.Count < 3)
        {
            var remappedContourVertices = new List<int>(remainingVertexCount);
            for (var i = 0; i < contourMask.Length; i++)
            {
                if (!contourMask[i] || removeMask[i] || remap[i] < 0)
                    continue;
                remappedContourVertices.Add(remap[i]);
            }
            newBoundaryLoop = BuildFallbackBoundaryLoop(keptVertices, remappedContourVertices);
        }

        if (newBoundaryLoop.Count < 3)
        {
            error = "Contour collapsed after cull.";
            return false;
        }

        data.vertices = keptVertices.ToArray();
        data.vertexUvs = keptUvs.ToArray();
        data.boneWeights = keptWeights != null ? keptWeights.ToArray() : Array.Empty<BoneWeight>();
        data.boundaryEdgePairs = BuildBoundaryPairsFromLoop(newBoundaryLoop);
        data.triangles = RemapTrianglesAfterCull(data.triangles, removeMask, remap, data.vertices.Length);
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;

        if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
        {
            error = "Projector: rebuilt mesh is unavailable.";
            return false;
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            error = "Projector: valid triangles are unavailable.";
            return false;
        }

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        data.hasSkinning = data.spriteBoneToRendererBone != null &&
                           data.bindBoneInverseMatrices != null &&
                           data.boneWeights.Length == data.vertices.Length &&
                           data.bindBoneInverseMatrices.Length == data.spriteBoneToRendererBone.Length &&
                           data.bindBoneInverseMatrices.Length > 0;

        if (string.Equals(selectedLayerSelectionKey, selectionKey, StringComparison.OrdinalIgnoreCase) &&
            editGeometrySelectedVertexIndex >= 0 &&
            editGeometrySelectedVertexIndex < remap.Length)
        {
            editGeometrySelectedVertexIndex = remap[editGeometrySelectedVertexIndex];
            if (editGeometrySelectedVertexIndices != null)
            {
                editGeometrySelectedVertexIndices.Clear();
                if (editGeometrySelectedVertexIndex >= 0)
                    editGeometrySelectedVertexIndices.Add(editGeometrySelectedVertexIndex);
            }
        }

        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

}
#endif
