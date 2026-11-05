#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const float ProjectorAplIntersectionEpsilon = 0.0001f;

    private readonly struct FashionableFolderSpec
    {
        public FashionableFolderSpec(string folderName, params string[] targetLayerNames)
        {
            FolderName = folderName ?? string.Empty;
            TargetLayerNames = targetLayerNames ?? System.Array.Empty<string>();
        }

        public readonly string FolderName;
        public readonly string[] TargetLayerNames;
    }

    private sealed class FashionablePlanItem
    {
        public string FolderName;
        public string LayerName;
        public string TargetSelectionKey;
        public string[] SourceSelectionKeys;
    }

    private static readonly FashionableFolderSpec[] FashionableFolderSpecs =
    {
        new FashionableFolderSpec("over", "body", "l_arm", "r_arm", "l_boob", "r_boob"),
        new FashionableFolderSpec("top", "body", "l_boob", "r_boob"),
        new FashionableFolderSpec("l_sleeve", "l_arm"),
        new FashionableFolderSpec("r_sleeve", "r_arm"),
        new FashionableFolderSpec("bot", "body", "l_leg", "r_leg"),
        new FashionableFolderSpec("bra", "body", "l_boob", "r_boob"),
        new FashionableFolderSpec("panty", "body", "l_leg", "r_leg")
    };

    public bool HasFashionableFolder()
    {
        return TryFindFashionableClFolder(out _);
    }

    public string ApplyFashionableProjectorApl()
    {
        if (!TryBuildFashionablePlan(out var plan, out var skipped, out var details))
            throw new System.InvalidOperationException("CL folder not found.");

        var applied = 0;
        var preserved = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            var item = plan[i];
            if (!TryPrepareProjectorAplTargetGeometry(item.TargetSelectionKey, out var prepareError))
            {
                if (details.Count < 5)
                    details.Add(item.FolderName + "/" + item.LayerName + ": " + prepareError + "; using original contour");
            }
            if (ApplyProjectorStretch(
                    item.TargetSelectionKey,
                    item.SourceSelectionKeys,
                    true,
                    false,
                    false,
                    false,
                    4f,
                    out _,
                    out _,
                    out _,
                    out var error))
            {
                applied++;
                continue;
            }

            if (string.Equals(error, ProjectorStretchNoChangesMessage, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            preserved++;
            if (details.Count < 5)
            {
                details.Add(item.FolderName + "/" + item.LayerName + ": " +
                            (string.IsNullOrWhiteSpace(error) ? "original geometry kept" : error));
            }
        }

        var result = "APL applied " + applied + "/" + plan.Count +
                     ", skipped " + skipped +
                     ", kept original " + preserved;
        if (details.Count > 0)
            result += " (" + string.Join("; ", details) + ")";
        return result;
    }

    private bool TryPrepareProjectorAplTargetGeometry(string targetSelectionKey, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(targetSelectionKey) || string.IsNullOrEmpty(cachedPath) ||
            !TryResolveProjectorSource(targetSelectionKey, out _, out var data))
        {
            return true;
        }

        if (!IsFashionableDefaultRectangleContour(data) && IsProjectorAplTriangulationUsable(data))
            return true;

        var previousSelectionKey = selectedLayerSelectionKey;
        selectedLayerSelectionKey = targetSelectionKey;
        try
        {
            if (GenerateAutoGeometryForSelected(
                    cachedPath,
                    10,
                    10,
                    0,
                    false,
                    ReanimatorAutoGeometryMode.Default,
                    ReanimatorReanimaxGeometryMode.Hard,
                    5d,
                    0f,
                    0.5f,
                    3,
                    13f,
                    4,
                    2.8f,
                    30,
                    5f,
                    5,
                    out error))
            {
                return true;
            }

            error = "could not build clothing alpha contour" +
                    (string.IsNullOrWhiteSpace(error) ? string.Empty : ": " + error);
            return false;
        }
        finally
        {
            selectedLayerSelectionKey = previousSelectionKey;
        }
    }

    private static bool IsFashionableDefaultRectangleContour(LayerVertexData data)
    {
        if (data == null || data.layer == null || data.vertices == null ||
            !TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out var loop) ||
            loop.Count != 4)
        {
            return false;
        }

        var rect = PsdCoordinateSpace.GetLayerDocumentRect(data.layer);
        var corners = new[]
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMin, rect.yMax)
        };
        var matched = new bool[corners.Length];
        var tolerance = Mathf.Max(0.25f, Mathf.Min(rect.width, rect.height) * 0.002f);
        var toleranceSqr = tolerance * tolerance;
        for (var loopIndex = 0; loopIndex < loop.Count; loopIndex++)
        {
            var point = data.vertices[loop[loopIndex]];
            var match = -1;
            for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                if (!matched[cornerIndex] && (point - corners[cornerIndex]).sqrMagnitude <= toleranceSqr)
                {
                    match = cornerIndex;
                    break;
                }
            }
            if (match < 0)
                return false;
            matched[match] = true;
        }
        return true;
    }

    internal static IReadOnlyList<string> GetFashionableTargetLayerNames(string folderName)
    {
        var normalized = NormalizeAutoWeightName(folderName);
        for (var i = 0; i < FashionableFolderSpecs.Length; i++)
        {
            if (string.Equals(
                    NormalizeAutoWeightName(FashionableFolderSpecs[i].FolderName),
                    normalized,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return FashionableFolderSpecs[i].TargetLayerNames;
            }
        }

        return System.Array.Empty<string>();
    }

    private bool TryBuildFashionablePlan(
        out List<FashionablePlanItem> plan,
        out int skipped,
        out List<string> details)
    {
        plan = new List<FashionablePlanItem>();
        skipped = 0;
        details = new List<string>();
        if (!TryFindFashionableClFolder(out var clFolder))
            return false;

        var nodesByKey = new Dictionary<string, LayerHierarchyNode>(System.StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var node = layerHierarchyNodes[i];
            if (node != null && !string.IsNullOrEmpty(node.key))
                nodesByKey[node.key] = node;
        }

        for (var specIndex = 0; specIndex < FashionableFolderSpecs.Length; specIndex++)
        {
            var spec = FashionableFolderSpecs[specIndex];
            for (var groupIndex = 0; groupIndex < layerHierarchyNodes.Count; groupIndex++)
            {
                var group = layerHierarchyNodes[groupIndex];
                if (group == null || !group.isGroup ||
                    !string.Equals(group.parentKey, clFolder.key, System.StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        NormalizeAutoWeightName(group.name),
                        NormalizeAutoWeightName(spec.FolderName),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var clothingNodes = CollectFashionableSpriteNodes(group.key, nodesByKey);
                if (!TryResolveFashionableSourceKeys(
                        clFolder.key,
                        spec.TargetLayerNames,
                        nodesByKey,
                        out var sourceKeys,
                        out var missingSource))
                {
                    skipped += clothingNodes.Count;
                    if (details.Count < 5)
                        details.Add(spec.FolderName + ": missing target " + missingSource);
                    continue;
                }

                for (var clothingIndex = 0; clothingIndex < clothingNodes.Count; clothingIndex++)
                {
                    var clothingNode = clothingNodes[clothingIndex];
                    if (!TryGetFashionableLayerSelectionKey(clothingNode, out var targetKey, out var layerName))
                    {
                        skipped++;
                        if (details.Count < 5)
                            details.Add(spec.FolderName + "/" + clothingNode.name + ": no geometry");
                        continue;
                    }

                    plan.Add(new FashionablePlanItem
                    {
                        FolderName = spec.FolderName,
                        LayerName = layerName,
                        TargetSelectionKey = targetKey,
                        SourceSelectionKeys = sourceKeys
                    });
                }
            }
        }

        return true;
    }

    private bool TryFindFashionableClFolder(out LayerHierarchyNode clFolder)
    {
        clFolder = null;
        if (layerHierarchyNodes == null || layerHierarchyNodes.Count == 0)
            return false;

        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var node = layerHierarchyNodes[i];
            if (node == null || !node.isGroup ||
                !string.Equals(
                    NormalizeAutoWeightName(node.name),
                    "cl",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            clFolder = node;
            return true;
        }

        return false;
    }

    private List<LayerHierarchyNode> CollectFashionableSpriteNodes(
        string folderKey,
        IReadOnlyDictionary<string, LayerHierarchyNode> nodesByKey)
    {
        var result = new List<LayerHierarchyNode>();
        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var node = layerHierarchyNodes[i];
            if (node == null || node.isGroup || node.layerIndex < 0 ||
                !IsFashionableDescendantOf(node, folderKey, nodesByKey))
            {
                continue;
            }

            result.Add(node);
        }

        return result;
    }

    private bool TryResolveFashionableSourceKeys(
        string clFolderKey,
        IReadOnlyList<string> targetLayerNames,
        IReadOnlyDictionary<string, LayerHierarchyNode> nodesByKey,
        out string[] sourceKeys,
        out string missingSource)
    {
        sourceKeys = System.Array.Empty<string>();
        missingSource = string.Empty;
        if (targetLayerNames == null || targetLayerNames.Count == 0)
            return false;

        var resolved = new List<string>(targetLayerNames.Count);
        for (var targetIndex = 0; targetIndex < targetLayerNames.Count; targetIndex++)
        {
            var targetName = targetLayerNames[targetIndex];
            var found = false;
            for (var nodeIndex = 0; nodeIndex < layerHierarchyNodes.Count; nodeIndex++)
            {
                var node = layerHierarchyNodes[nodeIndex];
                if (node == null || node.isGroup || node.layerIndex < 0 ||
                    IsFashionableDescendantOf(node, clFolderKey, nodesByKey) ||
                    !string.Equals(
                        NormalizeAutoWeightName(node.name),
                        NormalizeAutoWeightName(targetName),
                        System.StringComparison.OrdinalIgnoreCase) ||
                    !TryGetFashionableLayerSelectionKey(node, out var sourceKey, out _))
                {
                    continue;
                }

                resolved.Add(sourceKey);
                found = true;
                break;
            }

            if (found)
                continue;

            missingSource = targetName;
            return false;
        }

        sourceKeys = resolved.ToArray();
        return sourceKeys.Length == targetLayerNames.Count;
    }

    private bool TryGetFashionableLayerSelectionKey(
        LayerHierarchyNode node,
        out string selectionKey,
        out string layerName)
    {
        selectionKey = string.Empty;
        layerName = string.Empty;
        if (node == null || node.layerIndex < 0 || node.layerIndex >= layerInfos.Count)
            return false;

        var layer = layerInfos[node.layerIndex];
        if (layer == null)
            return false;

        selectionKey = BuildSelectionKey(layer);
        layerName = layer.name ?? node.name ?? string.Empty;
        return !string.IsNullOrEmpty(selectionKey) &&
               vertexDataByLayerSelectionKey != null &&
               vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out var data) &&
               data != null && data.vertices != null && data.vertices.Length >= 3;
    }

    private static bool IsFashionableDescendantOf(
        LayerHierarchyNode node,
        string ancestorKey,
        IReadOnlyDictionary<string, LayerHierarchyNode> nodesByKey)
    {
        if (node == null || string.IsNullOrEmpty(ancestorKey) || nodesByKey == null)
            return false;

        var parentKey = node.parentKey;
        var guard = 0;
        while (!string.IsNullOrEmpty(parentKey) && guard++ <= nodesByKey.Count)
        {
            if (string.Equals(parentKey, ancestorKey, System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (!nodesByKey.TryGetValue(parentKey, out var parent) || parent == null)
                return false;
            parentKey = parent.parentKey;
        }

        return false;
    }

    private sealed class ProjectorAplSegment
    {
        public Vector2 A;
        public Vector2 B;
        public bool IsTarget;
        public int SourceIndex;
        public readonly List<float> Splits = new List<float> { 0f, 1f };
        public readonly List<float> IntersectionSplits = new List<float>();
    }

    private readonly struct ProjectorAplWeightCandidate
    {
        public ProjectorAplWeightCandidate(float distanceSqr, LayerVertexData source, BoneWeight weight)
        {
            DistanceSqr = distanceSqr;
            Source = source;
            Weight = weight;
        }

        public readonly float DistanceSqr;
        public readonly LayerVertexData Source;
        public readonly BoneWeight Weight;
    }

    private bool TryBuildProjectorAplGeometry(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string error)
    {
        var safeGeometry = targetData != null ? ProjectorLayerDataSnapshot.Capture(targetData) : null;
        var safeGeometryUsable = targetData != null && IsProjectorAplTriangulationUsable(targetData);
        if (TryBuildProjectorAplMergedGeometry(
                targetData,
                sources,
                useVertexCull,
                vertexCullDistance,
                out insertedCount,
                out removedCount,
                out error))
        {
            return true;
        }

        if (safeGeometry == null || targetData == null || targetData.vertices == null)
            return false;

        // A dense collection of B constraints may be impossible to tessellate
        // as one graph. Fall back to incremental insertion into A's known-good
        // topology so APL still performs real geometry work without accepting
        // crossed triangles or silently dropping transferred vertices.
        safeGeometry.Restore(targetData);
        if (TryBuildProjectorAplProjectedGeometryFallback(
                targetData,
                sources,
                useVertexCull,
                vertexCullDistance,
                out insertedCount,
                out removedCount))
        {
            error = null;
            return true;
        }

        safeGeometry.Restore(targetData);
        insertedCount = 0;
        removedCount = 0;
        if (safeGeometryUsable && HasProjectorAplGeometryOverlap(targetData, sources))
        {
            error = null;
            return true;
        }
        error = "APL: source geometry does not overlap the target.";
        return false;
    }

    private bool TryBuildProjectorAplProjectedGeometryFallback(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount)
    {
        insertedCount = 0;
        removedCount = 0;
        if (!HasProjectorGeometry(targetData) || sources == null || sources.Count == 0)
            return false;

        if (!IsProjectorAplTriangulationUsable(targetData) &&
            !TryRetriangulateProjectorAplFallback(targetData))
        {
            return false;
        }

        var originalGeometry = ProjectorLayerDataSnapshot.Capture(targetData);
        var candidates = BuildProjectorAplProjectedFallbackCandidates(targetData, sources);
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            if (TryInsertProjectorAplProjectedVertex(targetData, candidates[candidateIndex], sources))
                insertedCount++;
        }

        var previousWeights = CloneArray(targetData.boneWeights) ?? Array.Empty<BoneWeight>();
        var weightsChanged = false;
        var weights = new BoneWeight[targetData.vertices.Length];
        for (var vertexIndex = 0; vertexIndex < targetData.vertices.Length; vertexIndex++)
        {
            if (TryFindNearestProjectorAplWeight(targetData.vertices[vertexIndex], sources, targetData, out var weight))
                weights[vertexIndex] = weight;
            else if (vertexIndex < previousWeights.Length)
                weights[vertexIndex] = previousWeights[vertexIndex];

            if (vertexIndex >= previousWeights.Length ||
                !AreBoneWeightsApproximatelyEqual(weights[vertexIndex], previousWeights[vertexIndex]))
            {
                weightsChanged = true;
            }
        }

        targetData.boneWeights = weights;
        targetData.deformedVertices = null;
        targetData.skinMatrices = null;
        targetData.lastPoseVersion = -1;
        BuildEdgePairs(targetData.triangles, targetData.vertices.Length, out targetData.allEdgePairs, out targetData.boundaryEdgePairs);

        if (useVertexCull && insertedCount > 0)
        {
            var beforeCull = ProjectorLayerDataSnapshot.Capture(targetData);
            var preservedBoundaryPairs = CloneArray(targetData.boundaryEdgePairs) ?? Array.Empty<int>();
            var protectedBoundaryVertices = new HashSet<int>();
            for (var pairIndex = 0; pairIndex < preservedBoundaryPairs.Length; pairIndex++)
                protectedBoundaryVertices.Add(preservedBoundaryPairs[pairIndex]);
            if (!TryApplyProjectorStretchVertexCull(
                    targetData,
                    vertexCullDistance,
                    true,
                    ref preservedBoundaryPairs,
                    protectedBoundaryVertices,
                    out var culledCount,
                    out _) ||
                !IsProjectorAplTriangulationUsable(targetData))
            {
                beforeCull.Restore(targetData);
            }
            else
            {
                removedCount = culledCount;
            }
        }

        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);
        if (!IsProjectorAplTriangulationUsable(targetData) || insertedCount <= 0 && !weightsChanged)
        {
            originalGeometry.Restore(targetData);
            insertedCount = 0;
            removedCount = 0;
            return false;
        }

        return true;
    }

    private static List<Vector2> BuildProjectorAplProjectedFallbackCandidates(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources)
    {
        var candidates = new List<Vector2>();
        var targetSegments = new List<ProjectorAplSegment>();
        AddProjectorAplSegments(targetData, true, -1, targetSegments);
        var sourceSegments = new List<ProjectorAplSegment>();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            AddProjectorAplPrioritizedSourceSegments(sources, sourceIndex, sourceSegments);

        // Boundary crossings go first. Inserting them before interior points
        // keeps every subsequent split inside the original A contour.
        for (var targetSegmentIndex = 0; targetSegmentIndex < targetSegments.Count; targetSegmentIndex++)
        {
            var targetSegment = targetSegments[targetSegmentIndex];
            for (var sourceSegmentIndex = 0; sourceSegmentIndex < sourceSegments.Count; sourceSegmentIndex++)
                AddProjectorAplSegmentIntersections(targetSegment, sourceSegments[sourceSegmentIndex]);
            SortAndDeduplicateProjectorAplSplits(targetSegment.IntersectionSplits);
            for (var splitIndex = 0; splitIndex < targetSegment.IntersectionSplits.Count; splitIndex++)
            {
                var point = Vector2.LerpUnclamped(
                    targetSegment.A,
                    targetSegment.B,
                    targetSegment.IntersectionSplits[splitIndex]);
                AddProjectorAplProjectedCandidate(candidates, targetData.vertices, point);
            }
        }

        var applicableSourceVertexCount = 0;
        // APL order is intentional: contour intersections, then source contour
        // vertices, then the remaining interior topology points, then weights.
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source == null || source.vertices == null || source.boundaryEdgePairs == null)
                continue;
            for (var pairIndex = 0; pairIndex < source.boundaryEdgePairs.Length; pairIndex++)
            {
                var vertexIndex = source.boundaryEdgePairs[pairIndex];
                if (!IsValidVertexIndex(source.vertices, vertexIndex))
                    continue;
                var point = source.vertices[vertexIndex];
                if (IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion(point, sources))
                    continue;
                if (!IsPointInsideProjectorAplTargetContour(targetData, point))
                    continue;
                applicableSourceVertexCount++;
                AddProjectorAplProjectedCandidate(candidates, targetData.vertices, point);
            }
        }

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source == null || source.vertices == null)
                continue;
            for (var vertexIndex = 0; vertexIndex < source.vertices.Length; vertexIndex++)
            {
                var point = source.vertices[vertexIndex];
                if (IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion(point, sources))
                    continue;
                if (!IsPointInsideProjectorAplTargetContour(targetData, point))
                    continue;
                applicableSourceVertexCount++;
                AddProjectorAplProjectedCandidate(candidates, targetData.vertices, point);
            }
        }

        // If A sits inside coarse B triangles, no B vertex may land in A. Use
        // fixed samples derived from B itself. Unlike repeatedly subdividing A,
        // these points are stable and make a second APL invocation idempotent.
        if (applicableSourceVertexCount == 0)
        {
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (!HasProjectorGeometry(source) || source.vertices == null)
                    continue;

                var edgePairs = source.allEdgePairs;
                if (edgePairs == null || edgePairs.Length < 2)
                    BuildEdgePairs(source.triangles, source.vertices.Length, out edgePairs, out _);
                for (var edgeIndex = 0; edgePairs != null && edgeIndex + 1 < edgePairs.Length; edgeIndex += 2)
                {
                    var a = edgePairs[edgeIndex];
                    var b = edgePairs[edgeIndex + 1];
                    if (!IsValidVertexIndex(source.vertices, a) || !IsValidVertexIndex(source.vertices, b))
                        continue;
                    var midpoint = (source.vertices[a] + source.vertices[b]) * 0.5f;
                    if (IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion(midpoint, sources) ||
                        !IsPointInsideProjectorAplTargetContour(targetData, midpoint))
                    {
                        continue;
                    }
                    AddProjectorAplProjectedCandidate(candidates, targetData.vertices, midpoint);
                }

                for (var triangleIndex = 0;
                     source.triangles != null && triangleIndex + 2 < source.triangles.Length;
                     triangleIndex += 3)
                {
                    var a = source.triangles[triangleIndex];
                    var b = source.triangles[triangleIndex + 1];
                    var c = source.triangles[triangleIndex + 2];
                    if (!IsValidVertexIndex(source.vertices, a) ||
                        !IsValidVertexIndex(source.vertices, b) ||
                        !IsValidVertexIndex(source.vertices, c))
                    {
                        continue;
                    }
                    var centroid = (source.vertices[a] + source.vertices[b] + source.vertices[c]) / 3f;
                    if (IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion(centroid, sources) ||
                        !IsPointInsideProjectorAplTargetContour(targetData, centroid))
                    {
                        continue;
                    }
                    AddProjectorAplProjectedCandidate(candidates, targetData.vertices, centroid);
                }
            }
        }

        return candidates;
    }

    private static bool HasProjectorAplGeometryOverlap(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources)
    {
        if (!HasProjectorGeometry(targetData) || sources == null)
            return false;

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (!HasProjectorGeometry(source) || source.vertices == null)
                continue;
            for (var vertexIndex = 0; vertexIndex < source.vertices.Length; vertexIndex++)
            {
                if (IsPointInsideProjectorAplTargetContour(targetData, source.vertices[vertexIndex]))
                    return true;
            }
        }

        if (targetData.vertices != null)
        {
            for (var vertexIndex = 0; vertexIndex < targetData.vertices.Length; vertexIndex++)
            {
                if (IsPointInsideProjectorAplSourceUnion(targetData.vertices[vertexIndex], sources, -1))
                    return true;
            }
        }

        var targetSegments = new List<ProjectorAplSegment>();
        AddProjectorAplSegments(targetData, true, -1, targetSegments);
        var sourceSegments = new List<ProjectorAplSegment>();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            AddProjectorAplPrioritizedSourceSegments(sources, sourceIndex, sourceSegments);
        for (var targetSegmentIndex = 0; targetSegmentIndex < targetSegments.Count; targetSegmentIndex++)
        {
            var targetSegment = targetSegments[targetSegmentIndex];
            for (var sourceSegmentIndex = 0; sourceSegmentIndex < sourceSegments.Count; sourceSegmentIndex++)
            {
                AddProjectorAplSegmentIntersections(targetSegment, sourceSegments[sourceSegmentIndex]);
                if (targetSegment.IntersectionSplits.Count > 0)
                    return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideProjectorAplTargetContour(LayerVertexData targetData, Vector2 point)
    {
        if (targetData == null || targetData.vertices == null)
            return false;
        if (TryBuildBoundaryLoopFromPairs(
                targetData.boundaryEdgePairs,
                targetData.vertices.Length,
                out var boundaryLoop) &&
            boundaryLoop.Count >= 3)
        {
            return IsPointInsidePolygonOrOnEdge(point, targetData.vertices, boundaryLoop);
        }

        return IsPointInsideProjectorGeometry(targetData, point);
    }

    private static void AddProjectorAplProjectedCandidate(
        ICollection<Vector2> candidates,
        IReadOnlyList<Vector2> targetVertices,
        Vector2 point)
    {
        if (candidates == null || !IsFiniteRetriangulationPoint(point) ||
            ContainsProjectorStretchPoint(targetVertices, point))
        {
            return;
        }

        foreach (var existing in candidates)
        {
            if ((existing - point).sqrMagnitude <= 0.0001f)
                return;
        }
        candidates.Add(point);
    }

    private bool TryInsertProjectorAplProjectedVertex(
        LayerVertexData data,
        Vector2 point,
        IReadOnlyList<LayerVertexData> sources)
    {
        if (data == null || data.vertices == null || data.triangles == null ||
            ContainsProjectorStretchPoint(data.vertices, point) ||
            !TryFindContainingTriangle(
                data.triangles,
                data.vertices,
                point,
                out var triangleStart,
                out var ia,
                out var ib,
                out var ic,
                out var dAB,
                out var dBC,
                out var dCA,
                out _))
        {
            return false;
        }

        var oldVertices = data.vertices;
        var newIndex = oldVertices.Length;
        var triangles = data.triangles;
        const float edgeTolerance = 0.001f;
        var minimumEdgeDistance = Mathf.Min(dAB, Mathf.Min(dBC, dCA));
        bool split;
        if (minimumEdgeDistance <= edgeTolerance)
        {
            var edgeA = ia;
            var edgeB = ib;
            if (dBC <= dAB && dBC <= dCA)
            {
                edgeA = ib;
                edgeB = ic;
            }
            else if (dCA <= dAB && dCA <= dBC)
            {
                edgeA = ic;
                edgeB = ia;
            }
            split = SplitTrianglesOnEdge(ref triangles, oldVertices, point, newIndex, edgeA, edgeB);
        }
        else
        {
            split = SplitSingleTriangle(ref triangles, triangleStart, ia, ib, ic, newIndex);
        }

        if (!split)
            return false;

        data.triangles = triangles;
        var vertices = CloneArray(oldVertices) ?? Array.Empty<Vector2>();
        Array.Resize(ref vertices, newIndex + 1);
        vertices[newIndex] = point;
        data.vertices = vertices;

        var uvs = data.vertexUvs != null && data.vertexUvs.Length == newIndex
            ? CloneArray(data.vertexUvs)
            : new Vector2[newIndex];
        Array.Resize(ref uvs, newIndex + 1);
        uvs[newIndex] = ComputeUvForPoint(data.layer, point);
        data.vertexUvs = uvs;

        var weights = data.boneWeights != null && data.boneWeights.Length == newIndex
            ? CloneArray(data.boneWeights)
            : new BoneWeight[newIndex];
        Array.Resize(ref weights, newIndex + 1);
        if (TryFindNearestProjectorAplWeight(point, sources, data, out var weight))
            weights[newIndex] = weight;
        data.boneWeights = weights;
        return true;
    }

    private bool TryBuildProjectorAplMergedGeometry(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        bool useVertexCull,
        float vertexCullDistance,
        out int insertedCount,
        out int removedCount,
        out string error)
    {
        insertedCount = 0;
        removedCount = 0;
        error = null;
        if (!HasProjectorGeometry(targetData) || sources == null || sources.Count == 0)
        {
            error = "APL requires target and source geometry.";
            return false;
        }

        var previousGeometry = ProjectorLayerDataSnapshot.Capture(targetData);
        var originalTargetVertices = CloneArray(targetData.vertices) ?? Array.Empty<Vector2>();
        var targetSegments = new List<ProjectorAplSegment>();
        AddProjectorAplSegments(targetData, true, -1, targetSegments);
        var sourceSegments = new List<ProjectorAplSegment>();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            AddProjectorAplPrioritizedSourceSegments(sources, sourceIndex, sourceSegments);

        if (targetSegments.Count < 3 || sourceSegments.Count < 3)
        {
            error = "APL could not resolve source contours.";
            return false;
        }

        for (var targetSegmentIndex = 0; targetSegmentIndex < targetSegments.Count; targetSegmentIndex++)
        {
            for (var sourceSegmentIndex = 0; sourceSegmentIndex < sourceSegments.Count; sourceSegmentIndex++)
                AddProjectorAplSegmentIntersections(targetSegments[targetSegmentIndex], sourceSegments[sourceSegmentIndex]);
        }

        var vertices = new List<Vector2>();
        var boundaryPairs = new List<int>();
        var boundaryKeys = new HashSet<ulong>();
        var boundaryPath = new List<Vector2>();
        for (var segmentIndex = 0; segmentIndex < targetSegments.Count; segmentIndex++)
        {
            var segment = targetSegments[segmentIndex];
            SortAndDeduplicateProjectorAplSplits(segment.Splits);
            SortAndDeduplicateProjectorAplSplits(segment.IntersectionSplits);
            var outsideIntervals = new bool[Mathf.Max(0, segment.Splits.Count - 1)];
            for (var splitIndex = 0; splitIndex + 1 < segment.Splits.Count; splitIndex++)
            {
                var fromT = segment.Splits[splitIndex];
                var toT = segment.Splits[splitIndex + 1];
                if (toT - fromT <= ProjectorAplIntersectionEpsilon)
                    continue;

                var from = Vector2.LerpUnclamped(segment.A, segment.B, fromT);
                var to = Vector2.LerpUnclamped(segment.A, segment.B, toT);
                if ((to - from).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
                    continue;

                var midpoint = (from + to) * 0.5f;
                outsideIntervals[splitIndex] = !IsPointInsideProjectorAplSourceUnion(midpoint, sources, -1);
            }

            for (var splitIndex = 0; splitIndex < segment.Splits.Count; splitIndex++)
            {
                var split = segment.Splits[splitIndex];
                var isMeshIntersection = ContainsProjectorAplSplit(segment.IntersectionSplits, split);
                var touchesOutsideBefore = splitIndex > 0 && outsideIntervals[splitIndex - 1];
                var touchesOutsideAfter = splitIndex < outsideIntervals.Length && outsideIntervals[splitIndex];
                if (!isMeshIntersection && !touchesOutsideBefore && !touchesOutsideAfter)
                    continue;

                AddProjectorAplPathPoint(
                    boundaryPath,
                    Vector2.LerpUnclamped(segment.A, segment.B, split));
            }
        }

        for (var pointIndex = 0; pointIndex < boundaryPath.Count; pointIndex++)
        {
            AddProjectorAplBoundaryEdge(
                vertices,
                boundaryPairs,
                boundaryKeys,
                boundaryPath[pointIndex],
                boundaryPath[(pointIndex + 1) % boundaryPath.Count]);
        }

        if (boundaryPairs.Count < 6)
        {
            previousGeometry.Restore(targetData);
            error = "APL could not form an A-derived contour.";
            return false;
        }

        if (!TryBuildBoundaryLoopFromPairs(boundaryPairs.ToArray(), vertices.Count, out var resultBoundaryLoop) ||
            resultBoundaryLoop.Count < 3)
        {
            previousGeometry.Restore(targetData);
            error = "APL could not resolve the A-derived contour.";
            return false;
        }

        // B provides transferred geometry and weights, but it must never expand A's contour.
        var resultBoundaryVertices = vertices.ToArray();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source == null || source.vertices == null)
                continue;
            for (var vertexIndex = 0; vertexIndex < source.vertices.Length; vertexIndex++)
            {
                var sourceVertex = source.vertices[vertexIndex];
                if (IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion(sourceVertex, sources))
                    continue;
                if (IsPointInsidePolygonOrOnEdge(sourceVertex, resultBoundaryVertices, resultBoundaryLoop))
                    FindOrAddProjectorAplPoint(vertices, sourceVertex);
            }
        }

        var sourceConstraintPairs = BuildProjectorAplSourceConstraintPairs(
            vertices,
            resultBoundaryVertices,
            resultBoundaryLoop,
            boundaryPairs,
            sources);

        for (var i = 0; i < vertices.Count; i++)
        {
            if (!ContainsProjectorStretchPoint(originalTargetVertices, vertices[i]))
                insertedCount++;
        }
        for (var i = 0; i < originalTargetVertices.Length; i++)
        {
            if (!ContainsProjectorStretchPoint(vertices, originalTargetVertices[i]))
                removedCount++;
        }

        var weights = new BoneWeight[vertices.Count];
        for (var vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            if (TryFindNearestProjectorAplWeight(vertices[vertexIndex], sources, targetData, out var weight))
                weights[vertexIndex] = weight;
        }

        targetData.vertices = vertices.ToArray();
        targetData.vertexUvs = BuildVertexUvsFromLayer(targetData.layer, targetData.vertices);
        targetData.boneWeights = weights;
        targetData.boundaryEdgePairs = boundaryPairs.ToArray();
        targetData.triangles = Array.Empty<int>();
        targetData.allEdgePairs = Array.Empty<int>();
        targetData.deformedVertices = null;
        targetData.skinMatrices = null;
        targetData.lastPoseVersion = -1;

        if (useVertexCull)
        {
            var preservedBoundaryPairs = targetData.boundaryEdgePairs;
            var protectedBoundaryVertices = new HashSet<int>();
            for (var pairIndex = 0; pairIndex < preservedBoundaryPairs.Length; pairIndex++)
                protectedBoundaryVertices.Add(preservedBoundaryPairs[pairIndex]);
            if (!TryApplyProjectorStretchVertexCull(
                    targetData,
                    vertexCullDistance,
                    true,
                    ref preservedBoundaryPairs,
                    protectedBoundaryVertices,
                    out var culledCount,
                    out error))
            {
                previousGeometry.Restore(targetData);
                return false;
            }

            removedCount += culledCount;
        }

        if (!TryRetriangulateProjectorAplWithConstraints(targetData, sourceConstraintPairs) &&
            !TryRetriangulateProjectorAplFallback(targetData))
        {
            previousGeometry.Restore(targetData);
            error = "APL could not triangulate the merged contour.";
            return false;
        }

        targetData.triangles = SanitizeTriangles(targetData.triangles, targetData.vertices.Length);
        if (targetData.triangles.Length < 3)
        {
            previousGeometry.Restore(targetData);
            error = "APL produced no valid triangles.";
            return false;
        }

        BuildEdgePairs(targetData.triangles, targetData.vertices.Length, out targetData.allEdgePairs, out targetData.boundaryEdgePairs);
        EnsureBoneWeightsArraySize(targetData, targetData.vertices.Length);
        return true;
    }

    private bool TryRetriangulateProjectorAplWithConstraints(
        LayerVertexData data,
        IReadOnlyList<int> sourceConstraintPairs)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 6)
        {
            return false;
        }

        var constraints = new List<Vector2Int>();
        var constraintKeys = new HashSet<ulong>();
        AddProjectorAplConstraints(data.boundaryEdgePairs, data.vertices.Length, constraints, constraintKeys);
        AddProjectorAplConstraints(sourceConstraintPairs, data.vertices.Length, constraints, constraintKeys);
        if (constraints.Count < 3)
            return false;

        var sourceVertices = new List<Vector2>(data.vertices);
        if (!TryTriangulateWithUnityTessellation(
                sourceVertices,
                constraints,
                0,
                out var triangulatedVertices,
                out var triangles,
                out _) ||
            triangulatedVertices.Length != data.vertices.Length ||
            !TryBuildVertexRemap(sourceVertices, triangulatedVertices, out var sourceToTri) ||
            !TryBuildInversePermutation(sourceToTri, triangulatedVertices.Length, out var triToSource))
        {
            return false;
        }

        var remappedTriangles = new int[triangles.Length];
        for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
        {
            var triangulatedIndex = triangles[triangleIndex];
            if (triangulatedIndex < 0 || triangulatedIndex >= triToSource.Length)
                return false;
            remappedTriangles[triangleIndex] = triToSource[triangulatedIndex];
        }

        data.triangles = SanitizeTriangles(remappedTriangles, data.vertices.Length);
        if (!IsProjectorAplTriangulationUsable(data))
            return false;

        BuildEdgePairs(data.triangles, data.vertices.Length, out var allPairs, out _);
        var allKeys = new HashSet<ulong>();
        for (var pairIndex = 0; pairIndex + 1 < allPairs.Length; pairIndex += 2)
            allKeys.Add(MakeEdgeKey(allPairs[pairIndex], allPairs[pairIndex + 1]));
        for (var pairIndex = 0; pairIndex + 1 < sourceConstraintPairs.Count; pairIndex += 2)
        {
            if (!allKeys.Contains(MakeEdgeKey(sourceConstraintPairs[pairIndex], sourceConstraintPairs[pairIndex + 1])))
                return false;
        }
        return true;
    }

    private bool TryRetriangulateProjectorAplFallback(LayerVertexData data)
    {
        if (data == null)
            return false;

        var pendingGeometry = ProjectorLayerDataSnapshot.Capture(data);
        if (TryRetriangulateProjectorAplPreservingBoundaryVertices(data) &&
            IsProjectorAplTriangulationUsable(data))
        {
            return true;
        }

        pendingGeometry.Restore(data);
        if (TryRetriangulateKeepingVertexSet(data) && IsProjectorAplTriangulationUsable(data))
            return true;

        pendingGeometry.Restore(data);
        if (TryRetriangulateWithDirectFallback(data) && IsProjectorAplTriangulationUsable(data))
            return true;

        pendingGeometry.Restore(data);
        return false;
    }

    private static void AddProjectorAplConstraints(
        IReadOnlyList<int> pairs,
        int vertexCount,
        ICollection<Vector2Int> constraints,
        ISet<ulong> keys)
    {
        if (pairs == null)
            return;
        for (var pairIndex = 0; pairIndex + 1 < pairs.Count; pairIndex += 2)
        {
            var a = pairs[pairIndex];
            var b = pairs[pairIndex + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b || !keys.Add(MakeEdgeKey(a, b)))
                continue;
            constraints.Add(new Vector2Int(a, b));
        }
    }

    private static List<int> BuildProjectorAplSourceConstraintPairs(
        List<Vector2> vertices,
        IReadOnlyList<Vector2> boundaryVertices,
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<int> boundaryPairs,
        IReadOnlyList<LayerVertexData> sources)
    {
        var result = new List<int>();
        var keys = new HashSet<ulong>();
        if (sources == null)
            return result;

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source == null || source.vertices == null)
                continue;
            var edgePairs = source.allEdgePairs;
            if (edgePairs == null || edgePairs.Length < 2)
                BuildEdgePairs(source.triangles, source.vertices.Length, out edgePairs, out _);
            for (var edgeIndex = 0; edgePairs != null && edgeIndex + 1 < edgePairs.Length; edgeIndex += 2)
            {
                var aIndex = edgePairs[edgeIndex];
                var bIndex = edgePairs[edgeIndex + 1];
                if (!IsValidVertexIndex(source.vertices, aIndex) || !IsValidVertexIndex(source.vertices, bIndex) || aIndex == bIndex)
                    continue;

                var segment = new ProjectorAplSegment { A = source.vertices[aIndex], B = source.vertices[bIndex] };
                for (var boundaryIndex = 0; boundaryIndex + 1 < boundaryPairs.Count; boundaryIndex += 2)
                {
                    var boundaryA = boundaryPairs[boundaryIndex];
                    var boundaryB = boundaryPairs[boundaryIndex + 1];
                    if (boundaryA < 0 || boundaryB < 0 || boundaryA >= boundaryVertices.Count || boundaryB >= boundaryVertices.Count)
                        continue;
                    AddProjectorAplSegmentIntersections(
                        segment,
                        new ProjectorAplSegment { A = boundaryVertices[boundaryA], B = boundaryVertices[boundaryB], IsTarget = true });
                }
                if (IsProjectorAplBodySource(source))
                    AddProjectorAplBoobBoundarySplits(segment, sources);
                SortAndDeduplicateProjectorAplSplits(segment.Splits);
                for (var splitIndex = 0; splitIndex + 1 < segment.Splits.Count; splitIndex++)
                {
                    var from = Vector2.LerpUnclamped(segment.A, segment.B, segment.Splits[splitIndex]);
                    var to = Vector2.LerpUnclamped(segment.A, segment.B, segment.Splits[splitIndex + 1]);
                    if ((to - from).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon ||
                        !IsPointInsidePolygonOrOnEdge((from + to) * 0.5f, boundaryVertices, boundaryLoop) ||
                        IsProjectorAplBodySource(source) && IsPointInsideProjectorAplBoobUnion((from + to) * 0.5f, sources))
                    {
                        continue;
                    }

                    var fromIndex = FindOrAddProjectorAplPoint(vertices, from);
                    var toIndex = FindOrAddProjectorAplPoint(vertices, to);
                    if (fromIndex == toIndex || !keys.Add(MakeEdgeKey(fromIndex, toIndex)))
                        continue;
                    result.Add(fromIndex);
                    result.Add(toIndex);
                }
            }
        }
        return result;
    }

    private bool TryRetriangulateProjectorAplPreservingBoundaryVertices(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            !TryBuildBoundaryLoopFromPairs(data.boundaryEdgePairs, data.vertices.Length, out var boundaryLoop) ||
            boundaryLoop.Count < 3)
        {
            return false;
        }

        var keepBoundary = new bool[boundaryLoop.Count];
        for (var loopIndex = 0; loopIndex < keepBoundary.Length; loopIndex++)
            keepBoundary[loopIndex] = true;

        var changed = true;
        while (changed && CountProjectorAplKeptBoundaryVertices(keepBoundary) > 3)
        {
            changed = false;
            for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
            {
                if (!keepBoundary[loopIndex])
                    continue;
                var previous = FindProjectorAplKeptBoundaryIndex(keepBoundary, loopIndex, -1);
                var next = FindProjectorAplKeptBoundaryIndex(keepBoundary, loopIndex, 1);
                if (previous == loopIndex || next == loopIndex || previous == next)
                    continue;

                var a = data.vertices[boundaryLoop[previous]];
                var b = data.vertices[boundaryLoop[loopIndex]];
                var c = data.vertices[boundaryLoop[next]];
                if (!IsProjectorAplPointOnSegment(b, a, c))
                    continue;

                keepBoundary[loopIndex] = false;
                changed = true;
            }
        }

        var keptLoopPositions = new List<int>();
        var polygon = new List<Vector2>();
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
        {
            if (!keepBoundary[loopIndex])
                continue;
            keptLoopPositions.Add(loopIndex);
            polygon.Add(data.vertices[boundaryLoop[loopIndex]]);
        }
        if (polygon.Count < 3)
            return false;

        var constraints = new List<Vector2Int>(polygon.Count);
        for (var polygonIndex = 0; polygonIndex < polygon.Count; polygonIndex++)
            constraints.Add(new Vector2Int(polygonIndex, (polygonIndex + 1) % polygon.Count));
        int[] triangles;
        if (TryTriangulateWithUnityTessellation(
                polygon,
                constraints,
                0,
                out var triangulatedVertices,
                out var localTriangles,
                out _) &&
            triangulatedVertices.Length == polygon.Count &&
            TryBuildVertexRemap(polygon, triangulatedVertices, out var polygonToTri) &&
            TryBuildInversePermutation(polygonToTri, triangulatedVertices.Length, out var triToPolygon))
        {
            triangles = new int[localTriangles.Length];
            for (var triangleIndex = 0; triangleIndex < localTriangles.Length; triangleIndex++)
            {
                var triangulatedIndex = localTriangles[triangleIndex];
                if (triangulatedIndex < 0 || triangulatedIndex >= triToPolygon.Length)
                    return false;
                triangles[triangleIndex] = boundaryLoop[keptLoopPositions[triToPolygon[triangulatedIndex]]];
            }
        }
        else
        {
            // Unity tessellation can reject otherwise valid clothing contours
            // with dense, nearly-collinear neck/leg segments. Ear clipping is
            // independent of that implementation and preserves A's exact loop.
            var earTriangles = new List<int>();
            if (!TryTriangulateSimplePolygon(polygon, earTriangles) || earTriangles.Count < 3)
                return false;

            triangles = new int[earTriangles.Count];
            for (var triangleIndex = 0; triangleIndex < earTriangles.Count; triangleIndex++)
            {
                var polygonIndex = earTriangles[triangleIndex];
                if (polygonIndex < 0 || polygonIndex >= keptLoopPositions.Count)
                    return false;
                triangles[triangleIndex] = boundaryLoop[keptLoopPositions[polygonIndex]];
            }
        }
        triangles = SanitizeTriangles(triangles, data.vertices.Length);
        if (triangles.Length < 3)
            return false;

        for (var keptIndex = 0; keptIndex < keptLoopPositions.Count; keptIndex++)
        {
            var fromPosition = keptLoopPositions[keptIndex];
            var toPosition = keptLoopPositions[(keptIndex + 1) % keptLoopPositions.Count];
            var edgeStart = boundaryLoop[fromPosition];
            var edgeEnd = boundaryLoop[toPosition];
            var loopPosition = (fromPosition + 1) % boundaryLoop.Count;
            while (loopPosition != toPosition)
            {
                var deferredVertex = boundaryLoop[loopPosition];
                if (!SplitTrianglesOnEdge(
                        ref triangles,
                        data.vertices,
                        data.vertices[deferredVertex],
                        deferredVertex,
                        edgeStart,
                        edgeEnd))
                {
                    return false;
                }
                edgeStart = deferredVertex;
                loopPosition = (loopPosition + 1) % boundaryLoop.Count;
            }
        }

        var boundaryVertices = new HashSet<int>(boundaryLoop);
        data.triangles = triangles;
        for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
        {
            if (boundaryVertices.Contains(vertexIndex))
                continue;
            // A source point may land microscopically outside the rebuilt loop
            // after intersection snapping. It is not part of A's contour, so a
            // single unusable B interior point must not abort the whole batch.
            TryInsertVertexIntoTriangles(data, data.vertices, data.vertices[vertexIndex], vertexIndex);
        }

        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        RemoveProjectorAplUnreferencedInteriorVertices(data);
        return IsProjectorAplTriangulationUsable(data);
    }

    private static int RemoveProjectorAplUnreferencedInteriorVertices(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.triangles == null)
            return 0;

        var referenced = new bool[data.vertices.Length];
        for (var triangleIndex = 0; triangleIndex < data.triangles.Length; triangleIndex++)
        {
            var vertexIndex = data.triangles[triangleIndex];
            if (vertexIndex >= 0 && vertexIndex < referenced.Length)
                referenced[vertexIndex] = true;
        }

        var keptCount = 0;
        for (var vertexIndex = 0; vertexIndex < referenced.Length; vertexIndex++)
        {
            if (referenced[vertexIndex])
                keptCount++;
        }
        if (keptCount == data.vertices.Length || keptCount < 3)
            return 0;

        var remap = new int[data.vertices.Length];
        var vertices = new Vector2[keptCount];
        var uvs = new Vector2[keptCount];
        var weights = new BoneWeight[keptCount];
        var hasUvs = data.vertexUvs != null && data.vertexUvs.Length == data.vertices.Length;
        var hasWeights = data.boneWeights != null && data.boneWeights.Length == data.vertices.Length;
        var next = 0;
        for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
        {
            if (!referenced[vertexIndex])
            {
                remap[vertexIndex] = -1;
                continue;
            }

            remap[vertexIndex] = next;
            vertices[next] = data.vertices[vertexIndex];
            uvs[next] = hasUvs ? data.vertexUvs[vertexIndex] : Vector2.zero;
            weights[next] = hasWeights ? data.boneWeights[vertexIndex] : default;
            next++;
        }

        var removedCount = data.vertices.Length - keptCount;
        data.vertices = vertices;
        data.vertexUvs = hasUvs ? uvs : null;
        data.boneWeights = hasWeights ? weights : null;
        data.triangles = RemapVertexCullTriangles(data.triangles, remap, keptCount);
        data.boundaryEdgePairs = RemapVertexCullEdgePairs(data.boundaryEdgePairs, remap, keptCount);
        data.allEdgePairs = RemapVertexCullEdgePairs(data.allEdgePairs, remap, keptCount);
        return removedCount;
    }

    private static int CountProjectorAplKeptBoundaryVertices(IReadOnlyList<bool> keepBoundary)
    {
        var count = 0;
        for (var index = 0; index < keepBoundary.Count; index++)
        {
            if (keepBoundary[index])
                count++;
        }
        return count;
    }

    private static int FindProjectorAplKeptBoundaryIndex(IReadOnlyList<bool> keepBoundary, int origin, int direction)
    {
        var index = origin;
        for (var guard = 0; guard < keepBoundary.Count; guard++)
        {
            index = (index + direction + keepBoundary.Count) % keepBoundary.Count;
            if (keepBoundary[index])
                return index;
        }
        return origin;
    }

    private static bool ProjectorAplTrianglesReferenceEveryVertex(IReadOnlyList<int> triangles, int vertexCount)
    {
        var referenced = new bool[Mathf.Max(0, vertexCount)];
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            var vertexIndex = triangles[triangleIndex];
            if (vertexIndex >= 0 && vertexIndex < referenced.Length)
                referenced[vertexIndex] = true;
        }
        for (var vertexIndex = 0; vertexIndex < referenced.Length; vertexIndex++)
        {
            if (!referenced[vertexIndex])
                return false;
        }
        return referenced.Length >= 3;
    }

    private static void AddProjectorAplSegments(
        LayerVertexData data,
        bool isTarget,
        int sourceIndex,
        ICollection<ProjectorAplSegment> segments)
    {
        if (data == null || data.vertices == null || segments == null)
            return;

        var edgePairs = isTarget ? data.boundaryEdgePairs : data.allEdgePairs;
        if (edgePairs == null || edgePairs.Length < 2)
        {
            BuildEdgePairs(data.triangles, data.vertices.Length, out var allEdgePairs, out var boundaryEdgePairs);
            edgePairs = isTarget ? boundaryEdgePairs : allEdgePairs;
        }
        if (edgePairs == null)
            return;

        if (isTarget && TryBuildProjectorAplPrimaryBoundaryLoop(edgePairs, data.vertices, out var targetLoop))
        {
            for (var vertexIndex = 0; vertexIndex < targetLoop.Count; vertexIndex++)
            {
                AddProjectorAplSegment(
                    data,
                    targetLoop[vertexIndex],
                    targetLoop[(vertexIndex + 1) % targetLoop.Count],
                    true,
                    sourceIndex,
                    segments);
            }
            return;
        }

        for (var edgeIndex = 0; edgeIndex + 1 < edgePairs.Length; edgeIndex += 2)
        {
            AddProjectorAplSegment(
                data,
                edgePairs[edgeIndex],
                edgePairs[edgeIndex + 1],
                isTarget,
                sourceIndex,
                segments);
        }
    }

    private static void AddProjectorAplPrioritizedSourceSegments(
        IReadOnlyList<LayerVertexData> sources,
        int sourceIndex,
        ICollection<ProjectorAplSegment> segments)
    {
        if (sources == null || sourceIndex < 0 || sourceIndex >= sources.Count)
            return;
        var source = sources[sourceIndex];
        if (!IsProjectorAplBodySource(source))
        {
            AddProjectorAplSegments(source, false, sourceIndex, segments);
            return;
        }

        var rawSegments = new List<ProjectorAplSegment>();
        AddProjectorAplSegments(source, false, sourceIndex, rawSegments);
        for (var segmentIndex = 0; segmentIndex < rawSegments.Count; segmentIndex++)
        {
            var segment = rawSegments[segmentIndex];
            AddProjectorAplBoobBoundarySplits(segment, sources);
            SortAndDeduplicateProjectorAplSplits(segment.Splits);
            for (var splitIndex = 0; splitIndex + 1 < segment.Splits.Count; splitIndex++)
            {
                var from = Vector2.LerpUnclamped(segment.A, segment.B, segment.Splits[splitIndex]);
                var to = Vector2.LerpUnclamped(segment.A, segment.B, segment.Splits[splitIndex + 1]);
                if ((to - from).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon ||
                    IsPointInsideProjectorAplBoobUnion((from + to) * 0.5f, sources))
                {
                    continue;
                }
                segments.Add(new ProjectorAplSegment
                {
                    A = from,
                    B = to,
                    IsTarget = false,
                    SourceIndex = sourceIndex
                });
            }
        }
    }

    private static void AddProjectorAplBoobBoundarySplits(
        ProjectorAplSegment segment,
        IReadOnlyList<LayerVertexData> sources)
    {
        if (segment == null || sources == null)
            return;
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var boob = sources[sourceIndex];
            if (!IsProjectorAplBoobSource(boob) || boob.vertices == null)
                continue;
            var boundaryPairs = boob.boundaryEdgePairs;
            if (boundaryPairs == null || boundaryPairs.Length < 2)
                BuildEdgePairs(boob.triangles, boob.vertices.Length, out _, out boundaryPairs);
            for (var pairIndex = 0; boundaryPairs != null && pairIndex + 1 < boundaryPairs.Length; pairIndex += 2)
            {
                var a = boundaryPairs[pairIndex];
                var b = boundaryPairs[pairIndex + 1];
                if (!IsValidVertexIndex(boob.vertices, a) || !IsValidVertexIndex(boob.vertices, b) || a == b)
                    continue;
                AddProjectorAplSegmentIntersections(
                    segment,
                    new ProjectorAplSegment { A = boob.vertices[a], B = boob.vertices[b] });
            }
        }
    }

    private static bool IsProjectorAplBodySource(LayerVertexData source)
    {
        return source != null && source.layer != null &&
               string.Equals(source.layer.name, "body", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectorAplBoobSource(LayerVertexData source)
    {
        if (source == null || source.layer == null)
            return false;
        return string.Equals(source.layer.name, "l_boob", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(source.layer.name, "r_boob", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPointInsideProjectorAplBoobUnion(
        Vector2 point,
        IReadOnlyList<LayerVertexData> sources)
    {
        if (sources == null)
            return false;
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (IsProjectorAplBoobSource(source) && IsPointInsideProjectorGeometry(source, point))
                return true;
        }
        return false;
    }

    private static bool IsProjectorAplTriangulationUsable(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3 ||
            data.triangles == null || data.triangles.Length < 3 || data.triangles.Length % 3 != 0 ||
            !ProjectorAplTrianglesReferenceEveryVertex(data.triangles, data.vertices.Length))
        {
            return false;
        }

        var edges = new List<Vector2Int>();
        var edgeKeys = new HashSet<ulong>();
        for (var triangleIndex = 0; triangleIndex + 2 < data.triangles.Length; triangleIndex += 3)
        {
            var a = data.triangles[triangleIndex];
            var b = data.triangles[triangleIndex + 1];
            var c = data.triangles[triangleIndex + 2];
            if (!IsValidVertexIndex(data.vertices, a) ||
                !IsValidVertexIndex(data.vertices, b) ||
                !IsValidVertexIndex(data.vertices, c) ||
                a == b || b == c || c == a ||
                Mathf.Abs(Cross(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a])) <=
                ProjectorAplIntersectionEpsilon)
            {
                return false;
            }

            AddProjectorAplValidationEdge(a, b, edges, edgeKeys);
            AddProjectorAplValidationEdge(b, c, edges, edgeKeys);
            AddProjectorAplValidationEdge(c, a, edges, edgeKeys);
        }

        for (var firstEdgeIndex = 0; firstEdgeIndex < edges.Count; firstEdgeIndex++)
        {
            var first = edges[firstEdgeIndex];
            for (var secondEdgeIndex = firstEdgeIndex + 1; secondEdgeIndex < edges.Count; secondEdgeIndex++)
            {
                var second = edges[secondEdgeIndex];
                if (first.x == second.x || first.x == second.y || first.y == second.x || first.y == second.y)
                    continue;
                if (DoProjectorAplEdgesProperlyIntersect(
                        data.vertices[first.x],
                        data.vertices[first.y],
                        data.vertices[second.x],
                        data.vertices[second.y]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void AddProjectorAplValidationEdge(
        int a,
        int b,
        ICollection<Vector2Int> edges,
        ISet<ulong> edgeKeys)
    {
        if (edgeKeys.Add(MakeEdgeKey(a, b)))
            edges.Add(new Vector2Int(a, b));
    }

    private static bool DoProjectorAplEdgesProperlyIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var abC = Cross(b - a, c - a);
        var abD = Cross(b - a, d - a);
        var cdA = Cross(d - c, a - c);
        var cdB = Cross(d - c, b - c);
        return ((abC > ProjectorAplIntersectionEpsilon && abD < -ProjectorAplIntersectionEpsilon) ||
                (abC < -ProjectorAplIntersectionEpsilon && abD > ProjectorAplIntersectionEpsilon)) &&
               ((cdA > ProjectorAplIntersectionEpsilon && cdB < -ProjectorAplIntersectionEpsilon) ||
                (cdA < -ProjectorAplIntersectionEpsilon && cdB > ProjectorAplIntersectionEpsilon));
    }

    private static bool TryBuildProjectorAplPrimaryBoundaryLoop(
        IReadOnlyList<int> boundaryPairs,
        IReadOnlyList<Vector2> vertices,
        out List<int> primaryLoop)
    {
        primaryLoop = new List<int>();
        if (boundaryPairs == null || vertices == null || boundaryPairs.Count < 6 || vertices.Count < 3)
            return false;

        var adjacency = new Dictionary<int, List<int>>();
        for (var pairIndex = 0; pairIndex + 1 < boundaryPairs.Count; pairIndex += 2)
        {
            var a = boundaryPairs[pairIndex];
            var b = boundaryPairs[pairIndex + 1];
            if (a < 0 || b < 0 || a >= vertices.Count || b >= vertices.Count || a == b)
                continue;
            if (!adjacency.TryGetValue(a, out var aNeighbors))
                adjacency[a] = aNeighbors = new List<int>(2);
            if (!adjacency.TryGetValue(b, out var bNeighbors))
                adjacency[b] = bNeighbors = new List<int>(2);
            if (!aNeighbors.Contains(b))
                aNeighbors.Add(b);
            if (!bNeighbors.Contains(a))
                bNeighbors.Add(a);
        }

        var visitedVertices = new HashSet<int>();
        var bestArea = 0f;
        foreach (var candidateStart in adjacency.Keys)
        {
            if (visitedVertices.Contains(candidateStart))
                continue;

            var component = new List<int>();
            var pending = new Queue<int>();
            pending.Enqueue(candidateStart);
            visitedVertices.Add(candidateStart);
            var validComponent = true;
            while (pending.Count > 0)
            {
                var vertex = pending.Dequeue();
                component.Add(vertex);
                if (!adjacency.TryGetValue(vertex, out var neighbors) || neighbors.Count != 2)
                {
                    validComponent = false;
                    continue;
                }
                for (var neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    if (visitedVertices.Add(neighbors[neighborIndex]))
                        pending.Enqueue(neighbors[neighborIndex]);
                }
            }
            if (!validComponent || component.Count < 3)
                continue;

            var start = component[0];
            for (var componentIndex = 1; componentIndex < component.Count; componentIndex++)
                start = Mathf.Min(start, component[componentIndex]);
            var loop = new List<int>(component.Count);
            var previous = -1;
            var current = start;
            for (var guard = 0; guard <= component.Count; guard++)
            {
                loop.Add(current);
                var neighbors = adjacency[current];
                var next = neighbors[0] != previous ? neighbors[0] : neighbors[1];
                previous = current;
                current = next;
                if (current == start)
                    break;
            }
            if (current != start || loop.Count != component.Count)
                continue;

            var area = 0f;
            for (var loopIndex = 0; loopIndex < loop.Count; loopIndex++)
            {
                var a = vertices[loop[loopIndex]];
                var b = vertices[loop[(loopIndex + 1) % loop.Count]];
                area += a.x * b.y - b.x * a.y;
            }
            area = Mathf.Abs(area) * 0.5f;
            if (area <= bestArea)
                continue;
            bestArea = area;
            primaryLoop = loop;
        }

        return primaryLoop.Count >= 3 && bestArea > ProjectorAplIntersectionEpsilon;
    }

    private static void AddProjectorAplSegment(
        LayerVertexData data,
        int a,
        int b,
        bool isTarget,
        int sourceIndex,
        ICollection<ProjectorAplSegment> segments)
    {
        if (!IsValidVertexIndex(data.vertices, a) || !IsValidVertexIndex(data.vertices, b) || a == b)
            return;
        if ((data.vertices[b] - data.vertices[a]).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
            return;

        segments.Add(new ProjectorAplSegment
        {
            A = data.vertices[a],
            B = data.vertices[b],
            IsTarget = isTarget,
            SourceIndex = sourceIndex
        });
    }

    private static void AddProjectorAplBoundaryEdge(
        List<Vector2> vertices,
        ICollection<int> boundaryPairs,
        ISet<ulong> boundaryKeys,
        Vector2 from,
        Vector2 to)
    {
        if ((to - from).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
            return;

        var fromIndex = FindOrAddProjectorAplPoint(vertices, from);
        var toIndex = FindOrAddProjectorAplPoint(vertices, to);
        if (fromIndex == toIndex || !boundaryKeys.Add(MakeEdgeKey(fromIndex, toIndex)))
            return;

        boundaryPairs.Add(fromIndex);
        boundaryPairs.Add(toIndex);
    }

    private static void AddProjectorAplSegmentIntersections(ProjectorAplSegment lhs, ProjectorAplSegment rhs)
    {
        var lhsDirection = lhs.B - lhs.A;
        var rhsDirection = rhs.B - rhs.A;
        var delta = rhs.A - lhs.A;
        var denominator = Cross(lhsDirection, rhsDirection);
        if (Mathf.Abs(denominator) > ProjectorAplIntersectionEpsilon)
        {
            var lhsT = Cross(delta, rhsDirection) / denominator;
            var rhsT = Cross(delta, lhsDirection) / denominator;
            if (lhsT >= -ProjectorAplIntersectionEpsilon && lhsT <= 1f + ProjectorAplIntersectionEpsilon &&
                rhsT >= -ProjectorAplIntersectionEpsilon && rhsT <= 1f + ProjectorAplIntersectionEpsilon)
            {
                AddProjectorAplSplit(lhs.Splits, lhsT);
                AddProjectorAplSplit(rhs.Splits, rhsT);
                if (lhs.IsTarget != rhs.IsTarget)
                {
                    AddProjectorAplSplit(lhs.IsTarget ? lhs.IntersectionSplits : rhs.IntersectionSplits, lhs.IsTarget ? lhsT : rhsT);
                }
            }
            return;
        }

        if (Mathf.Abs(Cross(delta, lhsDirection)) > ProjectorAplIntersectionEpsilon)
            return;

        AddProjectorAplCollinearEndpoint(lhs, rhs.A, rhs.Splits, 0f);
        AddProjectorAplCollinearEndpoint(lhs, rhs.B, rhs.Splits, 1f);
        AddProjectorAplCollinearEndpoint(rhs, lhs.A, lhs.Splits, 0f);
        AddProjectorAplCollinearEndpoint(rhs, lhs.B, lhs.Splits, 1f);
        if (lhs.IsTarget != rhs.IsTarget)
        {
            var target = lhs.IsTarget ? lhs : rhs;
            var source = lhs.IsTarget ? rhs : lhs;
            AddProjectorAplIntersectionPointOnSegment(target, source.A);
            AddProjectorAplIntersectionPointOnSegment(target, source.B);
            if (IsProjectorAplPointOnSegment(target.A, source.A, source.B))
                AddProjectorAplSplit(target.IntersectionSplits, 0f);
            if (IsProjectorAplPointOnSegment(target.B, source.A, source.B))
                AddProjectorAplSplit(target.IntersectionSplits, 1f);
        }
    }

    private static void AddProjectorAplIntersectionPointOnSegment(ProjectorAplSegment target, Vector2 point)
    {
        var direction = target.B - target.A;
        var lengthSqr = direction.sqrMagnitude;
        if (lengthSqr <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
            return;
        var t = Vector2.Dot(point - target.A, direction) / lengthSqr;
        if (t < -ProjectorAplIntersectionEpsilon || t > 1f + ProjectorAplIntersectionEpsilon ||
            !IsProjectorAplPointOnSegment(point, target.A, target.B))
            return;
        AddProjectorAplSplit(target.Splits, t);
        AddProjectorAplSplit(target.IntersectionSplits, t);
    }

    private static bool IsProjectorAplPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var direction = b - a;
        var lengthSqr = direction.sqrMagnitude;
        if (lengthSqr <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
            return (point - a).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon;
        var t = Mathf.Clamp01(Vector2.Dot(point - a, direction) / lengthSqr);
        return (point - (a + direction * t)).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon;
    }

    private static void AddProjectorAplCollinearEndpoint(
        ProjectorAplSegment segment,
        Vector2 point,
        ICollection<float> pointSegmentSplits,
        float pointSegmentT)
    {
        var direction = segment.B - segment.A;
        var lengthSqr = direction.sqrMagnitude;
        if (lengthSqr <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
            return;
        var t = Vector2.Dot(point - segment.A, direction) / lengthSqr;
        if (t < -ProjectorAplIntersectionEpsilon || t > 1f + ProjectorAplIntersectionEpsilon)
            return;

        AddProjectorAplSplit(segment.Splits, t);
        AddProjectorAplSplit(pointSegmentSplits, pointSegmentT);
    }

    private static void AddProjectorAplSplit(ICollection<float> splits, float value)
    {
        if (splits == null)
            return;
        var clamped = Mathf.Clamp01(value);
        foreach (var existing in splits)
        {
            if (Mathf.Abs(existing - clamped) <= ProjectorAplIntersectionEpsilon)
                return;
        }
        splits.Add(clamped);
    }

    private static void SortAndDeduplicateProjectorAplSplits(List<float> splits)
    {
        if (splits == null)
            return;
        splits.Sort();
        for (var i = splits.Count - 2; i >= 0; i--)
        {
            if (Mathf.Abs(splits[i + 1] - splits[i]) <= ProjectorAplIntersectionEpsilon)
                splits.RemoveAt(i + 1);
        }
    }

    private static bool ContainsProjectorAplSplit(IReadOnlyList<float> splits, float value)
    {
        if (splits == null)
            return false;
        for (var splitIndex = 0; splitIndex < splits.Count; splitIndex++)
        {
            if (Mathf.Abs(splits[splitIndex] - value) <= ProjectorAplIntersectionEpsilon)
                return true;
        }
        return false;
    }

    private static void AddProjectorAplPathPoint(ICollection<Vector2> path, Vector2 point)
    {
        if (path == null)
            return;
        foreach (var existing in path)
        {
            if ((existing - point).sqrMagnitude <= ProjectorAplIntersectionEpsilon * ProjectorAplIntersectionEpsilon)
                return;
        }
        path.Add(point);
    }

    private static bool IsPointInsideProjectorAplSourceUnion(
        Vector2 point,
        IReadOnlyList<LayerVertexData> sources,
        int skippedSourceIndex)
    {
        if (sources == null)
            return false;
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (sourceIndex == skippedSourceIndex)
                continue;
            if (IsPointInsideProjectorGeometry(sources[sourceIndex], point))
                return true;
        }
        return false;
    }

    private static int FindOrAddProjectorAplPoint(List<Vector2> vertices, Vector2 point)
    {
        for (var i = 0; i < vertices.Count; i++)
        {
            if ((vertices[i] - point).sqrMagnitude <= 0.0001f)
                return i;
        }
        vertices.Add(point);
        return vertices.Count - 1;
    }

    private bool TryFindNearestProjectorAplWeight(
        Vector2 point,
        IReadOnlyList<LayerVertexData> sources,
        LayerVertexData targetData,
        out BoneWeight weight)
    {
        weight = default;
        if (sources == null)
            return false;

        var pointUsesBoobPriority = IsPointInsideProjectorAplBoobUnion(point, sources);
        var candidates = new List<ProjectorAplWeightCandidate>();

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source == null || source.vertices == null || source.boneWeights == null)
                continue;
            if (pointUsesBoobPriority && !IsProjectorAplBoobSource(source))
                continue;
            var count = Mathf.Min(source.vertices.Length, source.boneWeights.Length);
            for (var vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                var candidateWeight = source.boneWeights[vertexIndex];
                if (!IsUsableVertexWeight(candidateWeight))
                    continue;
                var distanceSqr = (source.vertices[vertexIndex] - point).sqrMagnitude;
                candidates.Add(new ProjectorAplWeightCandidate(distanceSqr, source, candidateWeight));
            }
        }

        candidates.Sort((lhs, rhs) => lhs.DistanceSqr.CompareTo(rhs.DistanceSqr));
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            var previousBoneMap = CloneArray(targetData.spriteBoneToRendererBone);
            var previousBindMatrices = CloneArray(targetData.bindBoneInverseMatrices);
            var previousUsesSpriteBindPoses = targetData.usesSpriteBindPoses;
            if (TryRemapProjectorWeight(candidate.Weight, candidate.Source, targetData, out weight, out _))
                return true;

            targetData.spriteBoneToRendererBone = previousBoneMap;
            targetData.bindBoneInverseMatrices = previousBindMatrices;
            targetData.usesSpriteBindPoses = previousUsesSpriteBindPoses;
            targetData.skinMatrices = null;
            targetData.deformedVertices = null;
        }

        return false;
    }

    private bool TryTransferProjectorAplWeightsToResult(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources)
    {
        if (targetData == null || targetData.vertices == null || targetData.vertices.Length == 0)
            return false;

        var weights = new BoneWeight[targetData.vertices.Length];
        var resolved = new bool[targetData.vertices.Length];
        var resolvedCount = 0;
        for (var vertexIndex = 0; vertexIndex < targetData.vertices.Length; vertexIndex++)
        {
            if (!TryFindNearestProjectorAplWeight(targetData.vertices[vertexIndex], sources, targetData, out var weight) ||
                !IsUsableVertexWeight(weight))
            {
                continue;
            }

            weights[vertexIndex] = weight;
            resolved[vertexIndex] = true;
            resolvedCount++;
        }

        if (resolvedCount == 0)
            return false;

        // Every APL vertex must be skinnable. If a particular source binding
        // cannot be remapped, extend the nearest successfully projected source
        // weight across that isolated vertex instead of leaving a zero weight.
        for (var vertexIndex = 0; vertexIndex < targetData.vertices.Length; vertexIndex++)
        {
            if (resolved[vertexIndex])
                continue;

            var nearestResolvedIndex = -1;
            var nearestDistanceSqr = float.PositiveInfinity;
            for (var candidateIndex = 0; candidateIndex < targetData.vertices.Length; candidateIndex++)
            {
                if (!resolved[candidateIndex])
                    continue;
                var distanceSqr = (targetData.vertices[candidateIndex] - targetData.vertices[vertexIndex]).sqrMagnitude;
                if (distanceSqr >= nearestDistanceSqr)
                    continue;
                nearestDistanceSqr = distanceSqr;
                nearestResolvedIndex = candidateIndex;
            }

            if (nearestResolvedIndex < 0)
                return false;
            weights[vertexIndex] = weights[nearestResolvedIndex];
        }

        targetData.boneWeights = weights;
        targetData.deformedVertices = null;
        targetData.skinMatrices = null;
        targetData.lastPoseVersion = -1;
        return true;
    }
}
#endif
