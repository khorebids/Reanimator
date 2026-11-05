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
    private static void BoobV2ManualContourIsDefaultAndSmoothsClosedStroke()
    {
        var renderer = new PsdLayerPreviewRenderer();
        AssertEqual(1, renderer.BoobToolVersion, "Boob V2 should be the default tool version.");
        AssertEqual(0, renderer.BoobV2Selection, "Boob V2 should explicitly start with the viewer-left nipple selected.");
        AssertEqual(32, renderer.BoobV2ContourDensity, "Boob V2 should start with a useful contour density.");

        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "BuildSmoothedClosedBoobV2Contour",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(IReadOnlyList<Vector2>), typeof(int), typeof(int) },
            null);
        AssertTrue(method != null, "Boob V2 should expose deterministic closed-stroke smoothing internally.");
        var raw = new List<Vector2>
        {
            new Vector2(-10f, -8f),
            new Vector2(8f, -10f),
            new Vector2(12f, 6f),
            new Vector2(0f, 12f),
            new Vector2(-12f, 5f)
        };
        var smoothed = method != null
            ? method.Invoke(null, new object[] { raw, 2, 24 }) as List<Vector2>
            : null;
        AssertTrue(smoothed != null && smoothed.Count == 24,
            "Boob V2 should resample a freehand stroke to the requested density.");
        var dense = method != null
            ? method.Invoke(null, new object[] { raw, 2, 80 }) as List<Vector2>
            : null;
        AssertTrue(dense != null && dense.Count == 80,
            "Boob V2 density should also update already available stroke data to a denser contour.");
    }

    private static void FullBoobsLocalCullPreservesPriorToolVerticesOutsideContours()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var leftContour = new List<Vector2>
        {
            new Vector2(-10f, -10f),
            new Vector2(10f, -10f),
            new Vector2(10f, 10f),
            new Vector2(-10f, 10f)
        };
        var rightContour = new List<Vector2>
        {
            new Vector2(90f, -10f),
            new Vector2(110f, -10f),
            new Vector2(110f, 10f),
            new Vector2(90f, 10f)
        };
        var cullMethod = typeof(PsdLayerPreviewRenderer).GetMethod(
            "ShouldCullFullBoobVertex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        AssertTrue(cullMethod != null, "FullBoobs local cull predicate should be available for regression coverage.");
        var contourArgs = new object[] { new Vector2(10f, 0f), leftContour, rightContour, 1f, 1f, false };
        AssertTrue(cullMethod != null && (bool)cullMethod.Invoke(null, contourArgs),
            "An old vertex exactly on the new breast contour should be culled.");
        AssertFalse((bool)contourArgs[5], "A point on the contour should be classified through the local contour band.");
        var nearArgs = new object[] { new Vector2(10.5f, 0f), leftContour, rightContour, 1f, 1f, false };
        AssertTrue(cullMethod != null && (bool)cullMethod.Invoke(null, nearArgs),
            "The nearest conflicting vertex just outside the breast contour should be culled.");
        var farArgs = new object[] { new Vector2(15f, 0f), leftContour, rightContour, 1f, 1f, false };
        AssertFalse(cullMethod != null && (bool)cullMethod.Invoke(null, farArgs),
            "A previous-tool vertex beyond the narrow outer band must not be culled.");

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var vertices = new[]
        {
            new Vector2(-50f, -50f),
            new Vector2(50f, -50f),
            new Vector2(50f, 50f),
            new Vector2(-50f, 50f),
            new Vector2(-12f, 4f),
            new Vector2(-23f, 4f),
            new Vector2(0f, -28f)
        };
        var uvs = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0.2f, 0.4f),
            new Vector2(0.1f, 0.4f),
            new Vector2(0.5f, 0.12f)
        };
        var weights = new BoneWeight[vertices.Length];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i].boneIndex0 = i;
            weights[i].weight0 = 1f;
        }
        SetPrivateField(data, "vertices", vertices);
        SetPrivateField(data, "vertexUvs", uvs);
        SetPrivateField(data, "boneWeights", weights);
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 6, 4, 5 });
        SetPrivateField(data, "triangles", new[] { 0, 1, 6, 4, 5, 6 });

        var removeMask = new[] { false, false, false, false, true, true, false };
        var boundaryLoop = new List<int> { 0, 1, 2, 3 };
        var method = typeof(PsdLayerPreviewRenderer).GetMethod(
            "CompactFullBoobGeometryKeepingOutside",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        AssertTrue(method != null, "FullBoobs local compaction should be available for regression coverage.");
        var args = new object[]
        {
            data,
            removeMask,
            boundaryLoop,
            leftContour,
            rightContour,
            1f,
            1f,
            0,
            null,
            null
        };
        var compacted = method != null && (bool)method.Invoke(renderer, args);
        AssertTrue(compacted, "FullBoobs local compaction should keep a valid body mesh.");
        AssertEqual(5, (int)args[7], "Only the two locally masked vertices should be removed.");

        var keptVertices = GetPrivateField<Vector2[]>(data, "vertices");
        var keptUvs = GetPrivateField<Vector2[]>(data, "vertexUvs");
        var keptWeights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertEqual(5, keptVertices.Length, "Outer boundary plus the prior-tool vertex should survive.");
        AssertVector(vertices[6], keptVertices[4], 0.0001f,
            "A prior-tool vertex far outside both breast contours must survive unchanged.");
        AssertVector(uvs[6], keptUvs[4], 0.0001f,
            "The retained prior-tool vertex UV must survive unchanged.");
        AssertEqual(6, keptWeights[4].boneIndex0,
            "The retained prior-tool vertex weight identity must survive unchanged.");
        AssertFloat(1f, keptWeights[4].weight0, 0.0001f,
            "The retained prior-tool vertex weight value must survive unchanged.");
        var retainedConstraints = args[8] as List<Vector2Int>;
        AssertTrue(
            retainedConstraints != null && retainedConstraints.Any(edge =>
                (edge.x == 0 && edge.y == 4) || (edge.x == 4 && edge.y == 0)),
            "A previous-tool edge fully outside the local breast areas should survive as a tessellation constraint.");
        var retainedTriangles = args[9] as List<int>;
        AssertArray(new[] { 0, 1, 4 }, retainedTriangles,
            "A previous-tool triangle fully outside the local breast areas should survive exactly.");
    }

    private static void FullBoobsRepairsTouchingContoursAndCrossingRetainedEdges()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var leftCenter = new Vector2(-2f, 0f);
        var rightCenter = new Vector2(2f, 0f);
        var overlappingLeft = new[]
        {
            new Vector2(-5f, -4f),
            new Vector2(1f, -4f),
            new Vector2(1f, 4f),
            new Vector2(-5f, 4f)
        };
        var overlappingRight = new[]
        {
            new Vector2(-1f, -4f),
            new Vector2(5f, -4f),
            new Vector2(5f, 4f),
            new Vector2(-1f, 4f)
        };
        var repairArgs = new object[]
        {
            leftCenter,
            rightCenter,
            overlappingLeft,
            overlappingRight,
            null,
            null
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryRepairFullBoobContourPair", repairArgs),
            "FullBoobs should deterministically repair touching or overlapping manual contours.");
        var repairedLeft = repairArgs[4] as Vector2[];
        var repairedRight = repairArgs[5] as Vector2[];
        AssertTrue(repairedLeft != null && repairedLeft.Length >= 3,
            "FullBoobs repaired left contour should remain usable.");
        AssertTrue(repairedRight != null && repairedRight.Length >= 3,
            "FullBoobs repaired right contour should remain usable.");
        AssertTrue(repairedLeft.Max(point => point.x) < repairedRight.Min(point => point.x),
            "FullBoobs repaired contour loops should have a strict non-touching gap.");

        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var vertices = new[]
        {
            new Vector2(-20f, -20f),
            new Vector2(20f, -20f),
            new Vector2(20f, 20f),
            new Vector2(-20f, 20f),
            new Vector2(-8f, -4f),
            new Vector2(-2f, -4f),
            new Vector2(-2f, 4f),
            new Vector2(-8f, 4f),
            new Vector2(2f, -4f),
            new Vector2(8f, -4f),
            new Vector2(8f, 4f),
            new Vector2(2f, 4f)
        };
        SetPrivateField(data, "vertices", vertices);
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        var constraints = new List<Vector2Int>
        {
            // A stale retained diagonal crosses both new rings. It is a
            // recoverable topology hint, not a reason to abort FullBoobs.
            new Vector2Int(0, 2),
            new Vector2Int(4, 5),
            new Vector2Int(5, 6),
            new Vector2Int(6, 7),
            new Vector2Int(7, 4),
            new Vector2Int(8, 9),
            new Vector2Int(9, 10),
            new Vector2Int(10, 11),
            new Vector2Int(11, 8)
        };
        AssertTrue(
            (bool)InvokePrivateMethod(
                renderer,
                "TryRetriangulateFullBoobsWithConstraints",
                data,
                constraints,
                4,
                1),
            "FullBoobs should retry without a crossing retained edge and complete triangulation.");
        var triangles = GetPrivateField<int[]>(data, "triangles");
        AssertTrue(triangles != null && triangles.Length >= 3,
            "FullBoobs recovered triangulation should produce body triangles.");
        AssertTriangleIndicesValid(triangles, vertices.Length,
            "FullBoobs recovered triangulation");

        var filterVertices = new[]
        {
            new Vector2(-1f, -1f),
            new Vector2(1f, -1f),
            new Vector2(1f, 1f),
            new Vector2(-1f, 1f),
            new Vector2(3f, 0f)
        };
        var farContour = new[]
        {
            new Vector2(10f, -1f),
            new Vector2(12f, -1f),
            new Vector2(12f, 1f),
            new Vector2(10f, 1f)
        };
        var filtered = InvokePrivateMethod(
            renderer,
            "BuildFullBoobCombinedTriangles",
            filterVertices,
            new[] { 1, 4, 2, 0, 1, 2 },
            new[] { filterVertices[0], filterVertices[1], filterVertices[2], filterVertices[3] },
            farContour,
            Array.Empty<int>(),
            Array.Empty<int>()) as int[];
        AssertArray(new[] { 1, 4, 2 }, filtered,
            "FullBoobs recovery should remove a triangle inside the breast while retaining an outside triangle that shares its ring edge.");
    }

    private static void VertexCullClustersRemapTopologySafely()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0.04f, 0.01f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        AssertTrue(
            ReanimatorVertexCullAlgorithms.TryBuildClusters(vertices, 0.08f, out var groups, out var remap),
            "Close vertices should form at least one merge group.");
        AssertEqual(4, groups.Count, "Merged vertex count.");
        AssertEqual(1, ReanimatorVertexCullAlgorithms.CountMergedGroups(groups), "Merged group count.");
        AssertEqual(0, remap[0], "First close vertex remap.");
        AssertEqual(0, remap[1], "Second close vertex remap.");
        AssertEqual(1, remap[2], "Third vertex remap.");
        AssertEqual(2, remap[3], "Fourth vertex remap.");
        AssertEqual(3, remap[4], "Fifth vertex remap.");
        AssertVector(new Vector2(0.02f, 0.005f), ReanimatorVertexCullAlgorithms.AveragePoints(vertices, groups[0]), 0.0001f, "Merged vertex average.");

        var triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 9, 2, 3 };
        var remappedTriangles = ReanimatorVertexCullAlgorithms.RemapTriangles(triangles, remap, groups.Count);
        AssertArray(new[] { 0, 1, 2, 0, 2, 3 }, remappedTriangles, "Degenerate and invalid triangles should be removed.");
        AssertTriangleIndicesValid(remappedTriangles, groups.Count, "Remapped triangles");

        var edgePairs = new[] { 0, 1, 1, 2, 0, 2, 2, 3, 4, 9, 3, 4, 4, 3 };
        var remappedEdges = ReanimatorVertexCullAlgorithms.RemapEdgePairs(edgePairs, remap, groups.Count);
        AssertArray(new[] { 0, 1, 1, 2, 2, 3 }, remappedEdges, "Degenerate, duplicate, and invalid edges should be removed.");
        AssertEdgeIndicesValid(remappedEdges, groups.Count, "Remapped edges");
    }

    private static void VertexCullWeightMergeKeepsNormalizedTopFour()
    {
        var weights = new[]
        {
            new BoneWeight
            {
                boneIndex0 = 0, weight0 = 0.45f,
                boneIndex1 = 1, weight1 = 0.35f,
                boneIndex2 = 2, weight2 = 0.2f,
                boneIndex3 = 7, weight3 = -0.5f
            },
            new BoneWeight
            {
                boneIndex0 = 1, weight0 = 0.45f,
                boneIndex1 = 3, weight1 = 0.35f,
                boneIndex2 = 4, weight2 = 0.2f,
                boneIndex3 = -1, weight3 = 1f
            },
            new BoneWeight
            {
                boneIndex0 = 5, weight0 = 0.8f,
                boneIndex1 = 2, weight1 = 0.2f,
                boneIndex2 = 9, weight2 = 0f,
                boneIndex3 = 10, weight3 = 0.00001f
            }
        };

        var merged = ReanimatorVertexCullAlgorithms.MergeBoneWeights(weights, new[] { 0, 1, 2 });
        AssertTrue(
            (merged.boneIndex0 == 1 && merged.boneIndex1 == 5) ||
            (merged.boneIndex0 == 5 && merged.boneIndex1 == 1),
            "Two strongest equal influences should occupy the first two slots.");
        AssertEqual(0, merged.boneIndex2, "Third strongest merged influence index.");
        AssertEqual(2, merged.boneIndex3, "Fourth strongest merged influence index.");
        AssertFloat(1f, SumWeight(merged), 0.0001f, "Merged weight should be normalized.");
        AssertTrue(merged.weight0 >= merged.weight1 && merged.weight1 >= merged.weight2 && merged.weight2 >= merged.weight3, "Weights should be sorted by strength.");
        AssertTrue(GetWeight(merged, 1) > 0f, "Bone 1 influence should be preserved.");
        AssertTrue(GetWeight(merged, 5) > 0f, "Bone 5 influence should be preserved.");
        AssertFloat(0f, GetWeight(merged, 3), 0.0001f, "Fifth strongest influence should be dropped.");
        AssertFloat(0f, GetWeight(merged, 4), 0.0001f, "Fifth influence should be dropped.");
        AssertFloat(0f, GetWeight(merged, 7), 0.0001f, "Negative source influence should be ignored.");
    }

    private static void RadiusCullMasksContourAndInnerVerticesSeparately()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.85f, 0.5f)
        };
        var contourMask = ReanimatorRadiusCullAlgorithms.BuildContourMask(
            new[] { 0, 1, 1, 2, 2, 3, 3, 0, 9, 1, 2, -1 },
            vertices.Length);
        AssertArray(new[] { true, true, true, true, false, false }, contourMask, "Contour mask should ignore invalid edge indices.");
        AssertEqual(4, ReanimatorRadiusCullAlgorithms.CountTrue(contourMask), "Contour vertex count.");

        var innerRemove = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
            vertices,
            contourMask,
            new Vector2(0.5f, 0.5f),
            0.21f,
            false,
            true,
            out var innerRemoved);
        AssertEqual(1, innerRemoved, "Inner-only radius cull should remove one inner vertex.");
        AssertArray(new[] { false, false, false, false, true, false }, innerRemove, "Inner-only remove mask.");
        AssertArray(new[] { 0, 1, 2, 3, -1, 4 }, ReanimatorRadiusCullAlgorithms.BuildRemap(innerRemove, out var innerKept), "Inner-only remap.");
        AssertEqual(5, innerKept, "Inner-only kept count.");

        var contourRemove = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
            vertices,
            contourMask,
            Vector2.zero,
            0.05f,
            true,
            false,
            out var contourRemoved);
        AssertEqual(1, contourRemoved, "Contour-only radius cull should remove one contour vertex.");
        AssertArray(new[] { true, false, false, false, false, false }, contourRemove, "Contour-only remove mask.");
        AssertArray(new[] { -1, 0, 1, 2, 3, 4 }, ReanimatorRadiusCullAlgorithms.BuildRemap(contourRemove, out var contourKept), "Contour-only remap.");
        AssertEqual(5, contourKept, "Contour-only kept count.");

        var disabledRemove = ReanimatorRadiusCullAlgorithms.BuildRemoveMask(
            vertices,
            contourMask,
            new Vector2(0.5f, 0.5f),
            2f,
            false,
            false,
            out var disabledRemoved);
        AssertEqual(0, disabledRemoved, "Disabled radius cull should remove no vertices.");
        AssertArray(new[] { false, false, false, false, false, false }, disabledRemove, "Disabled remove mask.");
    }

    private static void CurveWeightsPreserveOtherInfluenceCapacity()
    {
        var source = new BoneWeight
        {
            boneIndex0 = 0, weight0 = 0.2f,
            boneIndex1 = 2, weight1 = 0.25f,
            boneIndex2 = 3, weight2 = 0.15f,
            boneIndex3 = 4, weight3 = 0.1f
        };

        var clamped = ReanimatorCurveWeightAlgorithms.ClampChildWeight(source, 0, 1, 0.8f);
        AssertFloat(0.5f, clamped, 0.0001f, "Child weight should clamp to capacity left by all non-curve influences.");

        var weight = ReanimatorCurveWeightAlgorithms.BuildPreservingOthers(source, 0, 1, 0.8f, 4);
        AssertEqual(0, weight.boneIndex0, "First curve bone index.");
        AssertEqual(1, weight.boneIndex1, "Second curve bone index.");
        AssertEqual(2, weight.boneIndex2, "Primary preserved other influence.");
        AssertEqual(3, weight.boneIndex3, "Secondary preserved other influence.");
        AssertFloat(0f, weight.weight0, 0.0001f, "First curve bone should use remaining capacity.");
        AssertFloat(0.6f, weight.weight1, 0.0001f, "Second curve bone should be clamped.");
        AssertFloat(0.25f, weight.weight2, 0.0001f, "First other influence should be preserved.");
        AssertFloat(0.15f, weight.weight3, 0.0001f, "Second other influence should be preserved.");
        AssertFloat(1f, SumWeight(weight), 0.0001f, "Curve weight should stay normalized when source leaves enough capacity.");
        AssertFloat(0f, GetWeight(weight, 4), 0.0001f, "Out-of-range sprite bone should be ignored.");

        var saturatedSource = new BoneWeight
        {
            boneIndex0 = 2, weight0 = 0.4f,
            boneIndex1 = 3, weight1 = 0.3f,
            boneIndex2 = 4, weight2 = 0.2f,
            boneIndex3 = 5, weight3 = 0.1f
        };
        var saturated = ReanimatorCurveWeightAlgorithms.BuildPreservingOthers(saturatedSource, 0, 1, 0.5f, 6);
        AssertFloat(0f, saturated.weight0, 0.0001f, "First curve bone should get no capacity when other influences fill the weight.");
        AssertFloat(0f, saturated.weight1, 0.0001f, "Second curve bone should get no capacity when other influences fill the weight.");
        AssertEqual(2, saturated.boneIndex2, "Top other influence should be kept.");
        AssertEqual(3, saturated.boneIndex3, "Second top other influence should be kept.");
        AssertFloat(1f, saturated.weight2 + saturated.weight3, 0.0001f, "Kept other influences should be scaled to full other capacity.");
        AssertFloat(0f, GetWeight(saturated, 4), 0.0001f, "Third other influence should be folded into kept top two.");
        AssertFloat(0f, GetWeight(saturated, 5), 0.0001f, "Fourth other influence should be folded into kept top two.");

        var corruptSource = new BoneWeight
        {
            boneIndex0 = 2, weight0 = 3.8209f,
            boneIndex1 = 1, weight1 = 0f,
            boneIndex2 = 0, weight2 = 1f,
            boneIndex3 = 2, weight3 = 0.1791f
        };
        var repaired = ReanimatorCurveWeightAlgorithms.BuildPreservingOthers(corruptSource, 0, 1, 0.4f, 3);
        AssertFloat(1f, SumWeight(repaired), 0.0001f, "Curve weight should repair overweight source weights before preserving others.");
        AssertTrue(
            repaired.weight0 <= 1f && repaired.weight1 <= 1f && repaired.weight2 <= 1f && repaired.weight3 <= 1f,
            "Curve weight channels should not exceed one after repairing corrupt input.");
    }

    private static void EditGeometrySelectionNormalizeAndRemapIsStable()
    {
        var normalized = ReanimatorEditGeometrySelectionAlgorithms.NormalizeSelection(
            new[] { 3, 1, 3, -1, 9, 2 },
            4,
            5);
        AssertArray(new[] { 1, 3, 2 }, normalized, "Normalize should remove invalid indices and keep the last duplicate.");

        var fallback = ReanimatorEditGeometrySelectionAlgorithms.NormalizeSelection(
            Array.Empty<int>(),
            4,
            5);
        AssertArray(new[] { 4 }, fallback, "Normalize should fall back to primary selection.");

        var empty = ReanimatorEditGeometrySelectionAlgorithms.NormalizeSelection(
            new[] { -1, 8 },
            2,
            0);
        AssertEqual(0, empty.Count, "Normalize should clear selection when vertex count is empty.");

        var remapped = ReanimatorEditGeometrySelectionAlgorithms
            .RemapSelection(new[] { 0, 2, 5, 2, 3, -1 }, new[] { 0, -1, 1, 1 })
            .OrderBy(index => index)
            .ToArray();
        AssertArray(new[] { 0, 1 }, remapped, "Remap should drop removed indices and deduplicate merged selections.");
    }

    private static void EditGeometryDeleteRemapsTrianglesAndBoundarySafely()
    {
        var removeMask = ReanimatorEditGeometryAlgorithms.BuildRemoveMask(
            new[] { 2, 4, 2, -1, 9 },
            6,
            out var removedCount);
        AssertEqual(2, removedCount, "Duplicate and invalid selected vertices should not inflate removed count.");
        AssertArray(new[] { false, false, true, false, true, false }, removeMask, "Remove mask from selected vertices.");

        var remap = ReanimatorRadiusCullAlgorithms.BuildRemap(removeMask, out var keptCount);
        AssertEqual(4, keptCount, "Kept vertex count after delete.");
        AssertArray(new[] { 0, 1, -1, 2, -1, 3 }, remap, "Delete remap.");

        var triangles = ReanimatorEditGeometryAlgorithms.RemapTrianglesAfterCull(
            new[] { 0, 1, 2, 0, 3, 5, 1, 4, 5, 3, 5, 9, 1, 5, 3 },
            removeMask,
            remap);
        AssertArray(new[] { 0, 2, 3, 1, 3, 2 }, triangles, "Delete should remove triangles with deleted or invalid vertices.");
        AssertTriangleIndicesValid(triangles, keptCount, "Delete remapped triangles");

        var loop = ReanimatorEditGeometryAlgorithms.BuildFilteredBoundaryLoop(
            new[] { 0, 1, 2, 3, 4, 5, 0 },
            removeMask,
            remap);
        AssertArray(new[] { 0, 1, 2, 3 }, loop, "Filtered boundary loop should remove deleted vertices and trailing duplicate.");

        var boundaryPairs = ReanimatorEditGeometryAlgorithms.BuildBoundaryPairsFromLoop(loop);
        AssertArray(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, boundaryPairs, "Boundary pairs from filtered loop.");
        AssertEdgeIndicesValid(boundaryPairs, keptCount, "Delete remapped boundary");

        AssertArray(new[] { 10, 20, 40, 60 }, ReanimatorEditGeometryAlgorithms.CompactByRemoveMask(new[] { 10, 20, 30, 40, 50, 60 }, removeMask), "Compact should preserve non-removed payload order.");
        AssertArray(new[] { 10, 20, 40, 50, 60 }, ReanimatorEditGeometryAlgorithms.RemoveElementAt(new[] { 10, 20, 30, 40, 50, 60 }, 2), "RemoveElementAt should remove one payload item.");
    }

    private static void TriangulationHandlesConcavePolygonsAndWindingSafely()
    {
        var concave = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 3f),
            new Vector2(0f, 3f)
        };

        var triangles = new List<int>();
        AssertTrue(ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(concave, triangles), "Concave polygon should triangulate.");
        AssertEqual((concave.Length - 2) * 3, triangles.Count, "Concave polygon triangle index count.");
        AssertTriangleIndicesValid(triangles, concave.Length, "Concave triangulation");
        AssertTrianglesNonDegenerate(concave, triangles, "Concave triangulation");
        AssertFloat(Mathf.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(concave)), SumTriangleArea(concave, triangles), 0.0001f, "Concave triangulation area.");

        var reversed = new[]
        {
            concave[5],
            concave[4],
            concave[3],
            concave[2],
            concave[1],
            concave[0]
        };
        var reversedTriangles = new List<int>();
        AssertTrue(ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(reversed, reversedTriangles), "Reversed polygon should triangulate.");
        AssertEqual((reversed.Length - 2) * 3, reversedTriangles.Count, "Reversed polygon triangle index count.");
        AssertTriangleIndicesValid(reversedTriangles, reversed.Length, "Reversed triangulation");
        AssertTrianglesNonDegenerate(reversed, reversedTriangles, "Reversed triangulation");
        AssertFloat(Mathf.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(reversed)), SumTriangleArea(reversed, reversedTriangles), 0.0001f, "Reversed triangulation area.");

        var degenerateTriangles = new List<int>();
        AssertFalse(
            ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(
                new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f) },
                degenerateTriangles),
            "Collinear polygon should not triangulate.");
        AssertEqual(0, degenerateTriangles.Count, "Degenerate triangulation should not add indices.");

        var renderer = new PsdLayerPreviewRenderer();
        var recoveryVertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(6f, 0f),
            new Vector2(6f, 6f),
            new Vector2(0f, 6f),
            new Vector2(3f, 3f),
            new Vector2(3f, 0f),
            new Vector2(1f, 4f)
        };
        var recoveryData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("retri-recovery", 936, Vector2.zero),
            recoveryVertices,
            null,
            null);
        SetPrivateField(recoveryData, "boundaryEdgePairs", new[]
        {
            0, 2,
            2, 1,
            1, 3,
            3, 0,
            0, 2,
            -1, 99
        });
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryRetriangulateUsingConvexRecovery", recoveryData),
            "ReTri convex recovery should rebuild a usable mesh from contradictory constraints.");
        var recoveryTriangles = GetPrivateField<int[]>(recoveryData, "triangles");
        AssertTrue(recoveryTriangles.Length >= 3, "ReTri convex recovery should always emit triangles.");
        AssertTriangleIndicesValid(recoveryTriangles, recoveryVertices.Length, "ReTri convex recovery");
        AssertTrianglesNonDegenerate(recoveryVertices, recoveryTriangles, "ReTri convex recovery");
        var referencedRecoveryVertices = new HashSet<int>(recoveryTriangles);
        for (var vertexIndex = 0; vertexIndex < recoveryVertices.Length; vertexIndex++)
            AssertTrue(referencedRecoveryVertices.Contains(vertexIndex), $"ReTri recovery should preserve usable vertex {vertexIndex}.");

        var degenerateRecoveryData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("retri-degenerate", 937, Vector2.zero),
            new[] { Vector2.zero, Vector2.right },
            null,
            null);
        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "TryRetriangulateUsingConvexRecovery", degenerateRecoveryData),
            "ReTri convex recovery should identify a completely degenerate vertex set.");
        InvokePrivateMethod(renderer, "RebuildRetriangulationFallbackQuad", degenerateRecoveryData);
        var fallbackVertices = GetPrivateField<Vector2[]>(degenerateRecoveryData, "vertices");
        var fallbackTriangles = GetPrivateField<int[]>(degenerateRecoveryData, "triangles");
        AssertEqual(4, fallbackVertices.Length, "ReTri terminal recovery should rebuild a quad.");
        AssertEqual(6, fallbackTriangles.Length, "ReTri terminal recovery should always emit two triangles.");
        AssertTriangleIndicesValid(fallbackTriangles, fallbackVertices.Length, "ReTri terminal recovery");
        AssertTrianglesNonDegenerate(fallbackVertices, fallbackTriangles, "ReTri terminal recovery");

        const string productionShirtPath =
            "Assets/0.Internal/Game/Textures/Characters/grl/main 1/1.45_body_nude.psd";
        var productionShirt = AssetDatabase.LoadAllAssetsAtPath(productionShirtPath)
            .OfType<Sprite>()
            .FirstOrDefault(sprite => string.Equals(sprite.name, "cl_t-shirt", StringComparison.Ordinal));
        AssertTrue(productionShirt != null, "ReTri production fixture cl_t-shirt should be importable.");
        var productionVertices = productionShirt.vertices.Select(vertex => vertex * 100f).ToArray();
        var productionTriangles = productionShirt.triangles.Select(index => (int)index).ToArray();
        var productionHubBefore = GetMaximumTriangleUse(productionTriangles, productionVertices.Length);
        ReanimatorVertexInsertionAlgorithms.OptimizeTrianglesByEdgeFlips(
            productionVertices,
            productionTriangles,
            new HashSet<ulong>());
        var productionHubAfter = GetMaximumTriangleUse(productionTriangles, productionVertices.Length);
        var productionBoundaryEdges = BuildTestBoundaryEdges(productionTriangles, productionVertices.Length);
        var productionData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("cl_t-shirt", 938, Vector2.zero),
            productionVertices,
            null,
            null);
        SetPrivateField(productionData, "triangles", productionTriangles);
        SetPrivateField(
            productionData,
            "boundaryEdgePairs",
            productionBoundaryEdges.SelectMany(edge => new[] { edge.x, edge.y }).ToArray());
        var productionLongestBefore = GetMaximumInternalEdgeLength(productionVertices, productionTriangles);
        var collapsedProductionVertices = (int)InvokePrivateMethod(
            renderer,
            "CollapseNearDuplicateRetriangulationVertices",
            productionData);
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryRetriangulateKeepingVertexSet", productionData),
            "ReTri should rebuild cl_t-shirt after collapsing near-duplicate vertices.");
        InvokePrivateMethod(renderer, "SanitizeRetriangulationTrianglesByArea", productionData);
        InvokePrivateMethod(renderer, "OptimizeLayerTriangleQuality", productionData);
        var firstAddedProductionVertices = (int)InvokePrivateMethod(
            renderer,
            "RefineRetriangulationLongEdges",
            productionData);
        InvokePrivateMethod(renderer, "OptimizeLayerTriangleQuality", productionData);
        var finalAddedProductionVertices = (int)InvokePrivateMethod(
            renderer,
            "RefineRetriangulationLongEdges",
            productionData);
        InvokePrivateMethod(renderer, "SanitizeRetriangulationTrianglesByArea", productionData);
        var refinedProductionVertices = GetPrivateField<Vector2[]>(productionData, "vertices");
        var refinedProductionTriangles = GetPrivateField<int[]>(productionData, "triangles");
        var productionLongestAfter = GetMaximumInternalEdgeLength(
            refinedProductionVertices,
            refinedProductionTriangles);
        AssertTrue(
            productionHubAfter < productionHubBefore &&
            collapsedProductionVertices > 0 &&
            firstAddedProductionVertices > 0 &&
            productionLongestAfter <= 60.001f &&
            productionLongestAfter < productionLongestBefore,
            $"ReTri should dismantle the cl_t-shirt triangle fan. Before={productionHubBefore} " +
            $"Flips={productionHubAfter} Collapsed={collapsedProductionVertices} " +
            $"Added={firstAddedProductionVertices}+{finalAddedProductionVertices} " +
            $"Longest={productionLongestBefore:F2}->{productionLongestAfter:F2}.");
        AssertTriangleIndicesValid(
            refinedProductionTriangles,
            refinedProductionVertices.Length,
            "ReTri production cl_t-shirt");
        AssertTrianglesNonDegenerate(
            refinedProductionVertices,
            refinedProductionTriangles,
            "ReTri production cl_t-shirt");
    }

    private static int GetMaximumTriangleUse(IReadOnlyList<int> triangles, int vertexCount)
    {
        if (triangles == null || vertexCount <= 0)
            return 0;

        var useCounts = new int[vertexCount];
        for (var index = 0; index < triangles.Count; index++)
        {
            var vertexIndex = triangles[index];
            if (vertexIndex >= 0 && vertexIndex < vertexCount)
                useCounts[vertexIndex]++;
        }

        var maximum = 0;
        for (var vertexIndex = 0; vertexIndex < useCounts.Length; vertexIndex++)
            maximum = Mathf.Max(maximum, useCounts[vertexIndex]);
        return maximum;
    }

    private static List<Vector2Int> BuildTestBoundaryEdges(IReadOnlyList<int> triangles, int vertexCount)
    {
        var edgeUses = new Dictionary<ulong, Vector3Int>();
        if (triangles == null)
            return new List<Vector2Int>();

        for (var triangleIndex = 0; triangleIndex + 2 < triangles.Count; triangleIndex += 3)
        {
            var a = triangles[triangleIndex];
            var b = triangles[triangleIndex + 1];
            var c = triangles[triangleIndex + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                continue;
            AddTestEdgeUse(edgeUses, a, b);
            AddTestEdgeUse(edgeUses, b, c);
            AddTestEdgeUse(edgeUses, c, a);
        }

        var result = new List<Vector2Int>();
        foreach (var edge in edgeUses.Values)
        {
            if (edge.z == 1)
                result.Add(new Vector2Int(edge.x, edge.y));
        }
        return result;
    }

    private static float GetMaximumInternalEdgeLength(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> triangles)
    {
        if (vertices == null || triangles == null)
            return 0f;
        var edges = BuildTestBoundaryEdges(triangles, vertices.Count);
        var boundaryKeys = new HashSet<ulong>();
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            var min = Mathf.Min(edges[edgeIndex].x, edges[edgeIndex].y);
            var max = Mathf.Max(edges[edgeIndex].x, edges[edgeIndex].y);
            boundaryKeys.Add(((ulong)(uint)min << 32) | (uint)max);
        }

        var maximum = 0f;
        for (var triangleIndex = 0; triangleIndex + 2 < triangles.Count; triangleIndex += 3)
        {
            var a = triangles[triangleIndex];
            var b = triangles[triangleIndex + 1];
            var c = triangles[triangleIndex + 2];
            foreach (var edge in new[] { new Vector2Int(a, b), new Vector2Int(b, c), new Vector2Int(c, a) })
            {
                var min = Mathf.Min(edge.x, edge.y);
                var max = Mathf.Max(edge.x, edge.y);
                if (min < 0 || max >= vertices.Count || boundaryKeys.Contains(((ulong)(uint)min << 32) | (uint)max))
                    continue;
                maximum = Mathf.Max(maximum, Vector2.Distance(vertices[min], vertices[max]));
            }
        }
        return maximum;
    }

    private static void AddTestEdgeUse(Dictionary<ulong, Vector3Int> edgeUses, int a, int b)
    {
        var min = Mathf.Min(a, b);
        var max = Mathf.Max(a, b);
        var key = ((ulong)(uint)min << 32) | (uint)max;
        if (edgeUses.TryGetValue(key, out var edge))
        {
            edge.z++;
            edgeUses[key] = edge;
            return;
        }
        edgeUses[key] = new Vector3Int(min, max, 1);
    }

    private static void ReanimaxEdgeTracingRejectsOpenTails()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var black = new Color32(0, 0, 0, 255);
        var white = new Color32(255, 255, 255, 255);
        var touchingMaskEdge = new Color32[6 * 6];
        for (var i = 0; i < touchingMaskEdge.Length; i++)
            touchingMaskEdge[i] = black;
        for (var y = 1; y <= 4; y++)
        {
            for (var x = 0; x <= 3; x++)
                touchingMaskEdge[y * 6 + x] = white;
        }

        var touchingEdgePixels = InvokeReanimaxHardWhiteEdgePixelCollector(renderer, touchingMaskEdge, 6, 6);
        AssertTrue(touchingEdgePixels.Count > 0, "White Reanimax edge collection should keep silhouettes that touch the mask border.");
        var touchingEdgePaths = InvokeReanimaxHardEdgePixelPathSimplifier(renderer, touchingEdgePixels, 6, 6);
        AssertEqual(1, touchingEdgePaths.Count, "White Reanimax edge collection should still form a usable contour at the mask border.");

        var openTail = new List<Vector2Int>();
        for (var x = 2; x <= 12; x++)
            openTail.Add(new Vector2Int(x, 5));

        var shortOpenPaths = InvokeReanimaxHardEdgePixelPathSimplifier(
            renderer,
            new[] { new Vector2Int(2, 5), new Vector2Int(3, 5) },
            20,
            20);
        AssertEqual(0, shortOpenPaths.Count, "Short open Reanimax edge tails must not be emitted.");

        var openPaths = InvokeReanimaxHardEdgePixelPathSimplifier(
            renderer,
            openTail,
            20,
            20);
        AssertEqual(0, openPaths.Count, "Open Reanimax edge tails must not be promoted to closed geometry paths.");

        var closedRing = new List<Vector2Int>();
        for (var x = 4; x <= 10; x++)
        {
            closedRing.Add(new Vector2Int(x, 4));
            closedRing.Add(new Vector2Int(x, 10));
        }
        for (var y = 5; y <= 9; y++)
        {
            closedRing.Add(new Vector2Int(4, y));
            closedRing.Add(new Vector2Int(10, y));
        }

        var closedPaths = InvokeReanimaxHardEdgePixelPathSimplifier(
            renderer,
            closedRing,
            20,
            20);
        AssertEqual(1, closedPaths.Count, "Closed Reanimax contour should remain eligible.");
        AssertTrue(closedPaths[0].Count >= 4, "Closed Reanimax contour should keep enough points to build geometry.");
    }

    private static List<Vector2Int> InvokeReanimaxHardWhiteEdgePixelCollector(
        PsdLayerPreviewRenderer renderer,
        Color32[] pixels,
        int width,
        int height)
    {
        return (List<Vector2Int>)InvokePrivateMethod(
            renderer,
            "CollectReanimaxHardWhiteEdgePixels",
            pixels,
            width,
            height);
    }

    private static List<List<Vector2Int>> InvokeReanimaxHardEdgePixelPathSimplifier(
        PsdLayerPreviewRenderer renderer,
        IReadOnlyList<Vector2Int> edgePixels,
        int width,
        int height)
    {
        return (List<List<Vector2Int>>)InvokePrivateMethod(
            renderer,
            "SimplifyReanimaxHardEdgePixelPaths",
            edgePixels,
            width,
            height,
            1,
            0f,
            1,
            0f);
    }

    private static void GeneratedGeometrySubdivisionKeepsValidTriangles()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(6f, 0f),
            new Vector2(0f, 6f)
        };
        var triangles = new[] { 0, 1, 2 };
        var originalArea = SumTriangleArea(vertices, triangles);

        ReanimatorTriangulationAlgorithms.SubdivideGeneratedGeometry(ref vertices, ref triangles, 80);

        AssertTrue(vertices.Length > 3, "Subdivision should add center vertices.");
        AssertTrue(triangles.Length > 3, "Subdivision should split triangles.");
        AssertTriangleIndicesValid(triangles, vertices.Length, "Subdivided triangles");
        AssertTrianglesNonDegenerate(vertices, triangles, "Subdivided triangles");
        AssertFloat(originalArea, SumTriangleArea(vertices, triangles), 0.001f, "Subdivision should preserve total area.");

        var conformingVertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 4f),
            new Vector2(0f, 4f)
        };
        var conformingTriangles = new[] { 0, 1, 2, 0, 2, 3 };
        var conformingArea = SumTriangleArea(conformingVertices, conformingTriangles);

        ReanimatorTriangulationAlgorithms.SubdivideGeneratedGeometryConforming(
            ref conformingVertices,
            ref conformingTriangles,
            1);

        AssertEqual(9, conformingVertices.Length, "Conforming subdivision should share one midpoint across the common diagonal.");
        AssertEqual(24, conformingTriangles.Length, "Conforming subdivision should replace two triangles with eight triangles.");
        AssertTriangleIndicesValid(conformingTriangles, conformingVertices.Length, "Conforming subdivided triangles");
        AssertTrianglesNonDegenerate(conformingVertices, conformingTriangles, "Conforming subdivided triangles");
        AssertFloat(conformingArea, SumTriangleArea(conformingVertices, conformingTriangles), 0.001f, "Conforming subdivision should preserve total area.");

        var fanVertices = new Vector2[12];
        for (var i = 0; i < fanVertices.Length; i++)
        {
            var angle = i * Mathf.PI * 2f / fanVertices.Length;
            fanVertices[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        var fanTriangles = new int[(fanVertices.Length - 2) * 3];
        for (var i = 1; i < fanVertices.Length - 1; i++)
        {
            var triangleStart = (i - 1) * 3;
            fanTriangles[triangleStart] = 0;
            fanTriangles[triangleStart + 1] = i;
            fanTriangles[triangleStart + 2] = i + 1;
        }

        AssertTrue(
            ReanimatorTriangulationAlgorithms.RetriangulateGeneratedGeometry(fanVertices, ref fanTriangles),
            "Retriangulation should replace a boundary-vertex fan.");
        var fanIncidence = new int[fanVertices.Length];
        for (var i = 0; i < fanTriangles.Length; i++)
            fanIncidence[fanTriangles[i]]++;
        var maximumFanIncidence = 0;
        for (var i = 0; i < fanIncidence.Length; i++)
            maximumFanIncidence = Mathf.Max(maximumFanIncidence, fanIncidence[i]);
        AssertTrue(maximumFanIncidence < 10, "Retriangulation should remove the original starburst vertex.");
        AssertTriangleIndicesValid(fanTriangles, fanVertices.Length, "Retriangulated fan triangles");
        AssertTrianglesNonDegenerate(fanVertices, fanTriangles, "Retriangulated fan triangles");
    }

    private static void ImporterGeometrySanitizesPreferredAndFallbackIndices()
    {
        var sanitized = ReanimatorImporterGeometryAlgorithms.SanitizeTriangles(
            new[] { 0, 1, 2, 1, 1, 2, -1, 2, 3, 1, 2, 9, 0, 3, 2, 0 },
            4);
        AssertArray(new[] { 0, 1, 2, 0, 3, 2 }, sanitized, "Sanitized triangles should skip invalid, degenerate, and incomplete entries.");

        var preferred = ReanimatorImporterGeometryAlgorithms.BuildImporterIndices(
            new[] { 0, 1, 2, 2, 2, 3 },
            4,
            new[] { 0, 2, 3 });
        AssertArray(new[] { 0, 1, 2 }, preferred, "Valid preferred triangles should win after sanitizing degenerates.");

        var fallback = ReanimatorImporterGeometryAlgorithms.BuildImporterIndices(
            new[] { 0, 1, 8 },
            4,
            new[] { 0, 2, 3, 3, 2, 1, 2, 2, 1 });
        AssertArray(new[] { 0, 2, 3, 3, 2, 1 }, fallback, "Out-of-range preferred triangles should fall back.");

        var nearest = ReanimatorImporterGeometryAlgorithms.FindNearestReferenceVertexIndex(
            new[] { new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(0f, 5f) },
            2,
            new Vector2(4.5f, 0.1f));
        AssertEqual(1, nearest, "Nearest reference should respect reference count.");
    }

    private static void ImporterGeometryMapsDocumentPointsThroughReferenceTriangles()
    {
        var referenceDoc = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f),
            new Vector2(10f, 10f)
        };
        var importer = new[]
        {
            BuildImporterVertex(new Vector2(100f, 100f)),
            BuildImporterVertex(new Vector2(200f, 100f)),
            BuildImporterVertex(new Vector2(100f, 200f)),
            BuildImporterVertex(new Vector2(200f, 200f))
        };
        var indices = new[] { 0, 1, 2, 1, 3, 2 };

        AssertTrue(
            ReanimatorImporterGeometryAlgorithms.TryMapPointToImporterByTriangles(referenceDoc, importer, indices, new Vector2(2f, 3f), 4, out var firstMapped),
            "Point in first reference triangle should map.");
        AssertVector(new Vector2(120f, 130f), firstMapped, 0.0001f, "First triangle mapped point.");

        AssertTrue(
            ReanimatorImporterGeometryAlgorithms.TryMapPointToImporterByTriangles(referenceDoc, importer, indices, new Vector2(8f, 7f), 4, out var secondMapped),
            "Point in second reference triangle should map.");
        AssertVector(new Vector2(180f, 170f), secondMapped, 0.0001f, "Second triangle mapped point.");

        AssertFalse(
            ReanimatorImporterGeometryAlgorithms.TryMapPointToImporterByTriangles(referenceDoc, importer, indices, new Vector2(12f, 7f), 4, out _),
            "Point outside reference triangles should not map.");
        AssertFalse(
            ReanimatorImporterGeometryAlgorithms.TryMapPointToImporterByTriangles(referenceDoc, importer, new[] { 0, 1, 2 }, new Vector2(8f, 7f), 3, out _),
            "Reference count should limit usable mapping vertices.");
    }

    private static void VertexInsertionSplitsContainingTriangleSafely()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(0f, 4f),
            new Vector2(1f, 1f)
        };
        var triangles = new[] { 0, 1, 2 };
        var originalArea = SumTriangleArea(vertices, triangles);

        AssertTrue(ReanimatorVertexInsertionAlgorithms.SplitSingleTriangle(ref triangles, 0, 0, 1, 2, 3), "Single triangle should split.");
        AssertArray(new[] { 0, 1, 3, 1, 2, 3, 2, 0, 3 }, triangles, "Single triangle split indices.");
        AssertTriangleIndicesValid(triangles, vertices.Length, "Single triangle split");
        AssertTrianglesNonDegenerate(vertices, triangles, "Single triangle split");
        AssertFloat(originalArea, SumTriangleArea(vertices, triangles), 0.0001f, "Single triangle split should preserve area.");
    }

    private static void VertexInsertionSplitsSharedEdgeTrianglesSafely()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(2f, 0f),
            new Vector2(0f, 2f),
            new Vector2(2f, 2f),
            new Vector2(1f, 1f)
        };
        var triangles = new[] { 0, 1, 2, 2, 1, 3 };
        var originalArea = SumTriangleArea(vertices, triangles);

        AssertTrue(
            ReanimatorVertexInsertionAlgorithms.SplitTrianglesOnEdge(ref triangles, vertices, vertices[4], 4, 1, 2),
            "Shared edge should split both adjacent triangles.");
        AssertEqual(12, triangles.Length, "Shared edge split index count.");
        AssertTriangleIndicesValid(triangles, vertices.Length, "Shared edge split");
        AssertTrianglesNonDegenerate(vertices, triangles, "Shared edge split");
        AssertFloat(originalArea, SumTriangleArea(vertices, triangles), 0.0001f, "Shared edge split should preserve area.");
        AssertTrue(ReanimatorVertexInsertionAlgorithms.TryGetThirdVertexForEdge(2, 1, 3, 1, 2, out var third), "Third vertex should resolve from reversed edge.");
        AssertEqual(3, third, "Third vertex from reversed edge.");
    }

    private static void VertexInsertionUpdatesBoundaryLoopNearEdge()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(2f, 0f),
            new Vector2(2f, 2f),
            new Vector2(0f, 2f)
        };
        var boundary = new[] { 0, 1, 1, 2, 2, 3, 3, 0 };

        AssertTrue(
            ReanimatorVertexInsertionAlgorithms.TryInsertVertexIntoBoundaryLoop(boundary, vertices, new Vector2(2f, 1f), 4, out var updated),
            "Boundary insertion should snap to nearest edge.");
        AssertEdgeIndicesValid(updated, 5, "Updated boundary");
        AssertTrue(HasEdgePair(updated, 1, 4), "Updated boundary should connect previous edge start to new vertex.");
        AssertTrue(HasEdgePair(updated, 4, 2), "Updated boundary should connect new vertex to previous edge end.");
        AssertFalse(HasEdgePair(updated, 1, 2), "Updated boundary should replace the split edge.");

        AssertFalse(
            ReanimatorVertexInsertionAlgorithms.TryInsertVertexIntoBoundaryLoop(boundary, vertices, new Vector2(5f, 5f), 4, out _),
            "Distant point should not snap into boundary.");
    }

    private static void WeightSliderBuildsLineAndTriangleWeights()
    {
        var weights = new float[4];
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) },
                2,
                new Vector2(2.5f, 5f),
                weights),
            "Two point slider should build weights.");
        AssertFloat(0.75f, weights[0], 0.0001f, "Line weight A.");
        AssertFloat(0.25f, weights[1], 0.0001f, "Line weight B.");
        AssertFloat(1f, SumWeights(weights, 2), 0.0001f, "Line weights should normalize.");

        Array.Clear(weights, 0, weights.Length);
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f) },
                3,
                new Vector2(2f, 3f),
                weights),
            "Triangle slider should build barycentric weights.");
        AssertFloat(0.5f, weights[0], 0.0001f, "Triangle weight A.");
        AssertFloat(0.2f, weights[1], 0.0001f, "Triangle weight B.");
        AssertFloat(0.3f, weights[2], 0.0001f, "Triangle weight C.");
        AssertFloat(1f, SumWeights(weights, 3), 0.0001f, "Triangle weights should normalize.");

        var clamped = ReanimatorWeightSliderAlgorithms.ClampPointToTriangle(
            new Vector2(20f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f));
        AssertVector(new Vector2(10f, 0f), clamped, 0.0001f, "Outside triangle point should clamp to nearest edge point.");
    }

    private static void WeightSliderPreviewDragPreservesGrabOffsetAndRejectsCollapsedPoints()
    {
        var weights = new float[4];
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) },
                2,
                new Vector2(10.5f, 0f),
                new Vector2(6f, 0f),
                weights),
            "Preview drag should compensate for grabbing beside the selected vertex.");
        AssertFloat(0.55f, weights[0], 0.0001f, "Grab-offset drag should preserve a continuous first weight.");
        AssertFloat(0.45f, weights[1], 0.0001f, "Grab-offset drag should preserve a continuous second weight.");

        Array.Clear(weights, 0, weights.Length);
        AssertFalse(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(4f, 7f), new Vector2(4f, 7f) },
                2,
                new Vector2(5f, 7f),
                Vector2.zero,
                weights),
            "Collapsed preview points should not force a two-bone weight to 1/0.");
    }

    private static void WeightSliderMultiPreviewDragUsesPerVertexCursorOffsets()
    {
        var firstWeights = new float[4];
        var secondWeights = new float[4];
        var mouseAfterSharedDelta = new Vector2(104f, 0f);

        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(10f, 0f) },
                2,
                mouseAfterSharedDelta,
                new Vector2(98f, 0f),
                firstWeights),
            "First selected vertex should resolve the shared drag in its own preview range.");
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(20f, 0f) },
                2,
                mouseAfterSharedDelta,
                new Vector2(85f, 0f),
                secondWeights),
            "Second selected vertex should resolve the shared drag in its own preview range.");

        AssertFloat(0.6f, firstWeights[1], 0.0001f, "First vertex should move four pixels from its own starting point.");
        AssertFloat(0.95f, secondWeights[1], 0.0001f, "Second vertex should move the same four pixels from its own starting point.");
    }

    private static void WeightSliderPreviewDragHandlesCoincidentAndCollinearMaxima()
    {
        var coincidentWeights = new float[4];
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 0f) },
                3,
                new Vector2(10f, 0f),
                new[] { 0.5f, 0.2f, 0.3f },
                coincidentWeights),
            "Three-bone preview drag should remain editable when two maxima coincide.");
        AssertFloat(0f, coincidentWeights[0], 0.0001f, "Dragging to the coincident maximum should remove the opposite influence.");
        AssertFloat(0.4f, coincidentWeights[1], 0.0001f, "First coincident bone should preserve its source share.");
        AssertFloat(0.6f, coincidentWeights[2], 0.0001f, "Second coincident bone should preserve its source share.");

        var collinearWeights = new float[4];
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(
                new[] { new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(10f, 0f) },
                3,
                new Vector2(5f, 2f),
                collinearWeights),
            "Three distinct collinear maxima should use line interpolation instead of rejecting drag.");
        AssertFloat(0f, collinearWeights[0], 0.0001f, "Collinear middle maximum left weight.");
        AssertFloat(1f, collinearWeights[1], 0.0001f, "Collinear middle maximum should remain reachable.");
        AssertFloat(0f, collinearWeights[2], 0.0001f, "Collinear middle maximum right weight.");
    }

    private static void WeightSliderBuildsQuadMeanValueWeights()
    {
        var points = new[]
        {
            new Vector2(1f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f)
        };
        var weights = new float[4];

        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(points, 4, new Vector2(0.5f, 0.5f), weights),
            "Quad slider should build mean-value weights.");
        for (var i = 0; i < 4; i++)
            AssertFloat(0.25f, weights[i], 0.0001f, $"Center quad weight {i}.");
        AssertFloat(1f, SumWeights(weights, 4), 0.0001f, "Quad center weights should normalize.");

        Array.Clear(weights, 0, weights.Length);
        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildDragWeights(points, 4, new Vector2(2f, 0.5f), weights),
            "Quad slider should clamp outside points to polygon edge.");
        AssertFloat(0.5f, weights[0], 0.0001f, "Right edge top weight.");
        AssertFloat(0f, weights[1], 0.0001f, "Right edge opposite weight.");
        AssertFloat(0.5f, weights[2], 0.0001f, "Right edge bottom weight.");
        AssertFloat(0f, weights[3], 0.0001f, "Right edge opposite weight.");
        AssertFloat(1f, SumWeights(weights, 4), 0.0001f, "Quad clamped weights should normalize.");
    }

    private static void WeightSliderPreservesOneToFourInfluenceIdentities()
    {
        var map = new[] { 10, 11, 12, 13 };
        var single = new BoneWeight
        {
            boneIndex0 = 2,
            weight0 = 1f,
            boneIndex1 = 2,
            boneIndex2 = 2,
            boneIndex3 = 2
        };
        AssertArray(new[] { 2 }, ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(single, map), "Single influence should not invent trailing default bones.");

        var unityDefaultTrailingZero = new BoneWeight
        {
            boneIndex0 = 2,
            weight0 = 1f,
            boneIndex1 = 0,
            weight1 = 0f,
            boneIndex2 = 0,
            weight2 = 0f,
            boneIndex3 = 0,
            weight3 = 0f
        };
        AssertArray(new[] { 2 }, ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(unityDefaultTrailingZero, map), "Default bone 0 zero slots should not become slider bones.");

        var twoWithZero = new BoneWeight
        {
            boneIndex0 = 1,
            weight0 = 1f,
            boneIndex1 = 2,
            weight1 = 0f,
            boneIndex2 = 1,
            weight2 = 0f,
            boneIndex3 = 7,
            weight3 = 0.4f
        };
        AssertArray(new[] { 1, 2 }, ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(twoWithZero, map), "Explicit zero second influence should stay editable.");

        var threeWithZero = new BoneWeight
        {
            boneIndex0 = 1,
            weight0 = 0.75f,
            boneIndex1 = 2,
            weight1 = 0.25f,
            boneIndex2 = 3,
            weight2 = 0f,
            boneIndex3 = 1,
            weight3 = 0f
        };
        AssertArray(new[] { 1, 2, 3 }, ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(threeWithZero, map), "Explicit zero third influence should stay editable.");

        var fourWithZero = new BoneWeight
        {
            boneIndex0 = 0,
            weight0 = 0.5f,
            boneIndex1 = 1,
            weight1 = 0.25f,
            boneIndex2 = 2,
            weight2 = 0.25f,
            boneIndex3 = 3,
            weight3 = 0f
        };
        AssertArray(new[] { 0, 1, 2, 3 }, ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(fourWithZero, map), "Explicit zero fourth influence should stay editable.");
    }

    private static void WeightSliderSanitizesOverweightBoneWeights()
    {
        var corrupt = new BoneWeight
        {
            boneIndex0 = 2, weight0 = 3.8209f,
            boneIndex1 = 1, weight1 = 0f,
            boneIndex2 = 0, weight2 = 1f,
            boneIndex3 = 2, weight3 = 0.1791f
        };

        var sanitized = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(corrupt, 3, false, true);
        AssertFloat(1f, SumWeight(sanitized), 0.0001f, "Overweight vertex weights should be scaled back to one.");
        AssertFloat(0.8f, GetWeight(sanitized, 2), 0.0001f, "Duplicate overweight channels should merge before scaling.");
        AssertFloat(0.2f, GetWeight(sanitized, 0), 0.0001f, "Remaining positive channel should keep its relative share.");
        AssertArray(
            new[] { 2, 0, 1 },
            ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(sanitized, new[] { 10, 11, 12 }),
            "Explicit zero-weight bone should remain editable after overweight repair.");
    }

    private static void WeightSliderStableSlotsKeepBoneIdentityWhileDragging()
    {
        var reorderedSource = new BoneWeight
        {
            boneIndex0 = 1,
            weight0 = 0.61225f,
            boneIndex1 = 0,
            weight1 = 0.38775f
        };

        ReanimatorWeightSliderAlgorithms.BuildStableSlotState(
            reorderedSource,
            2,
            new[] { 0, 1, -1, -1 },
            out var boneIndices,
            out var weights);

        AssertArray(new[] { 0, 1, -1, -1 }, boneIndices, "Dragging should preserve each slider row's bone identity.");
        AssertFloat(0.38775f, weights[0], 0.0001f, "First stable row should receive its bone's current weight.");
        AssertFloat(0.61225f, weights[1], 0.0001f, "Second stable row should receive its bone's current weight.");
    }

    private static void WeightSliderPreviewDragKeepsZeroWeightIdentities()
    {
        var map = new[] { 10, 11, 12 };
        var weight = new BoneWeight
        {
            boneIndex0 = 2,
            weight0 = 1f,
            boneIndex1 = 1,
            weight1 = 0f,
            boneIndex2 = 0,
            weight2 = 0f,
            boneIndex3 = 2,
            weight3 = 0f
        };

        var activeBones = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(weight, map);
        AssertArray(
            new[] { 2, 1 },
            activeBones,
            "Weight Slider preview drag should preserve explicit zero-weight bone identities.");
        AssertFalse(
            activeBones.Contains(0),
            "Weight Slider preview drag should not invent a bone from an unused trailing slot.");
    }

    private static void WeightSliderPreviewDragKeepsExplicitZeroIndexBoneAtEndpoint()
    {
        var endpointWeight = ReanimatorWeightSliderAlgorithms.BuildBoneWeightFromExplicitSlots(
            new[] { 2, 0, -1, -1 },
            new[] { 1f, 0f, 0f, 0f },
            3,
            true);

        AssertArray(
            new[] { 0, 2 },
            ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(endpointWeight, new[] { 10, 11, 12 }),
            "Endpoint drag should keep an explicit zero-weight bone 0 alongside the full-weight bone.");
        AssertFloat(0f, GetWeight(endpointWeight, 0), 0.0001f, "Explicit bone 0 should remain at zero weight.");
        AssertFloat(1f, GetWeight(endpointWeight, 2), 0.0001f, "Dragged endpoint bone should reach full weight.");

        var sanitizedAgain = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(endpointWeight, 3, true, true);
        AssertArray(
            new[] { 0, 2 },
            ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(sanitizedAgain, new[] { 10, 11, 12 }),
            "Repeated preview sanitization should not remove the explicit zero-weight bone 0.");
    }

    private static void WeightSliderSmoothsWeightsFromTopologyNeighbors()
    {
        var source = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        };

        AssertTrue(
            ReanimatorWeightSliderAlgorithms.TryBuildSmoothedBoneWeight(
                source,
                0,
                new[] { 0, 1, 0, 2 },
                null,
                2,
                0.5f,
                out var smoothed),
            "Slider smoothing should use selected vertex topology neighbors.");
        AssertFloat(0.5f, GetWeight(smoothed, 0), 0.0001f, "Slider smoothing current bone weight.");
        AssertFloat(0.5f, GetWeight(smoothed, 1), 0.0001f, "Slider smoothing neighbor bone weight.");
        AssertFloat(1f, SumWeight(smoothed), 0.0001f, "Slider smoothing should normalize.");
    }

    private static void WeightBrushExtractsActiveAndAccumulatedWeights()
    {
        var weight = new BoneWeight
        {
            boneIndex0 = 1,
            weight0 = 0.2f,
            boneIndex1 = 1,
            weight1 = 0.3f,
            boneIndex2 = 2,
            weight2 = 0.5f,
            boneIndex3 = 5,
            weight3 = 0.4f
        };

        var active = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(weight, new[] { 10, 11, 12, 13 });
        AssertArray(new[] { 1, 2 }, active, "Active brush bones should skip duplicates and out-of-range channels.");

        var dictionary = ReanimatorWeightSliderAlgorithms.BuildWeightBrushWeightDictionary(weight, 4);
        AssertEqual(2, dictionary.Count, "Brush dictionary valid bone count.");
        AssertTrue(dictionary.TryGetValue(1, out var first), "Brush dictionary should include duplicate bone.");
        AssertFloat(0.5f, first, 0.0001f, "Brush dictionary should accumulate duplicate bone weights.");
        AssertTrue(dictionary.TryGetValue(2, out var second), "Brush dictionary should include second bone.");
        AssertFloat(0.5f, second, 0.0001f, "Brush dictionary second bone weight.");
        AssertFalse(dictionary.ContainsKey(5), "Brush dictionary should ignore out-of-range bones.");
    }

    private static void WeightBrushBoneRemovalDropsInfluenceAndRemapsIndices()
    {
        var source = new BoneWeight
        {
            boneIndex0 = 0,
            weight0 = 0.2f,
            boneIndex1 = 1,
            weight1 = 0.3f,
            boneIndex2 = 2,
            weight2 = 0.5f
        };

        var remapped = ReanimatorWeightSliderAlgorithms.RemoveBoneInfluenceAndRemap(source, 1, 3);
        AssertFloat(2f / 7f, GetWeight(remapped, 0), 0.0001f, "Bone before removed slot should keep its index and normalized share.");
        AssertFloat(5f / 7f, GetWeight(remapped, 1), 0.0001f, "Bone after removed slot should shift down and keep its normalized share.");
        AssertFloat(1f, SumWeight(remapped), 0.0001f, "Remaining influences should normalize after deleting a bone.");

        var removedOnly = ReanimatorWeightSliderAlgorithms.RemoveBoneInfluenceAndRemap(
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            1,
            3);
        AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(removedOnly), "A vertex influenced only by the deleted bone should become unweighted.");
    }

    private static void WeightBrushRemCanReduceSingleNormalizedBone()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var source = new BoneWeight
        {
            boneIndex0 = 1,
            weight0 = 1f,
            boneIndex1 = 1,
            boneIndex2 = 1,
            boneIndex3 = 1
        };

        var rem = (BoneWeight)InvokePrivateMethod(
            renderer,
            "BuildWeightBrushBoneWeight",
            source,
            1,
            0.75f,
            2,
            true,
            true);
        AssertFloat(0.75f, GetWeight(rem, 1), 0.0001f, "Rem should not normalize a single selected bone back to full weight.");
        AssertFloat(0.75f, SumWeight(rem), 0.0001f, "Rem should allow partial single-bone weight while subtracting.");

        var add = (BoneWeight)InvokePrivateMethod(
            renderer,
            "BuildWeightBrushBoneWeight",
            source,
            1,
            0.75f,
            2,
            true,
            false);
        AssertFloat(1f, GetWeight(add, 1), 0.0001f, "Non-rem normalized single-bone edits should keep full normalized weight.");
    }

    private static void WeightBrushResolvesSelectedSpriteBoneWithoutZeroFallback()
    {
        var map = new[] { 10, 20, 30 };
        AssertEqual(
            -1,
            ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(-1, -1, map),
            "Brush should not silently fall back to sprite bone 0 when no bone is selected.");
        AssertEqual(
            2,
            ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(-1, 30, map),
            "Selected renderer bone should resolve to its mapped sprite bone.");
        AssertEqual(
            1,
            ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(1, -1, map),
            "Valid requested sprite bone should be used when no renderer bone is selected.");
        AssertEqual(
            1,
            ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(1, 30, map),
            "Valid requested sprite bone should not be overridden by stale selected renderer bone.");
        AssertEqual(
            -1,
            ReanimatorWeightBrushAlgorithms.ResolveSpriteBoneIndex(7, -1, map),
            "Out-of-range requested sprite bone should not clamp to sprite bone 0.");
    }

    private static void WeightBrushCapLimitsAndRedistributesInfluences()
    {
        var source = new BoneWeight
        {
            boneIndex0 = 3,
            weight0 = 0.4f,
            boneIndex1 = 1,
            weight1 = 0.3f,
            boneIndex2 = 2,
            weight2 = 0.2f,
            boneIndex3 = 0,
            weight3 = 0.1f
        };

        var capped = ReanimatorWeightBrushAlgorithms.CapInfluences(source, 4, 2);
        AssertFloat(0.55f, GetWeight(capped, 3), 0.0001f, "Cap should add half of the discarded weight to the strongest bone.");
        AssertFloat(0.45f, GetWeight(capped, 1), 0.0001f, "Cap should add half of the discarded weight to the second strongest bone.");
        AssertFloat(0f, GetWeight(capped, 2), 0.0001f, "Cap should remove the third influence.");
        AssertFloat(0f, GetWeight(capped, 0), 0.0001f, "Cap should remove the fourth influence.");
        AssertFloat(1f, SumWeight(capped), 0.0001f, "Cap should preserve normalized total weight.");

        var alreadyBelowCap = new BoneWeight { boneIndex0 = 2, weight0 = 0.75f };
        var unchanged = ReanimatorWeightBrushAlgorithms.CapInfluences(alreadyBelowCap, 4, 2);
        AssertFloat(0.75f, GetWeight(unchanged, 2), 0.0001f, "Cap should not renormalize a vertex already below the influence limit.");

        var tied = ReanimatorWeightBrushAlgorithms.CapInfluences(
            new BoneWeight
            {
                boneIndex0 = 3,
                weight0 = 0.25f,
                boneIndex1 = 2,
                weight1 = 0.25f,
                boneIndex2 = 1,
                weight2 = 0.25f,
                boneIndex3 = 0,
                weight3 = 0.25f
            },
            4,
            2);
        AssertFloat(0.5f, GetWeight(tied, 0), 0.0001f, "Equal cap weights should keep the lower bone index deterministically.");
        AssertFloat(0.5f, GetWeight(tied, 1), 0.0001f, "Equal cap weights should keep the next lower bone index deterministically.");
    }

    private static void WeightBrushCapCountSerializes()
    {
        var source = new PsdLayerPreviewRenderer { WeightBrushCapInfluenceCount = 2 };
        var json = JsonUtility.ToJson(source);
        var restored = new PsdLayerPreviewRenderer();
        JsonUtility.FromJsonOverwrite(json, restored);

        AssertEqual(2, restored.WeightBrushCapInfluenceCount, "Weight brush cap count should survive renderer serialization.");
        restored.WeightBrushCapInfluenceCount = 8;
        AssertEqual(3, restored.WeightBrushCapInfluenceCount, "Weight brush cap count should clamp to three.");
        restored.WeightBrushCapInfluenceCount = -2;
        AssertEqual(1, restored.WeightBrushCapInfluenceCount, "Weight brush cap count should clamp to one.");
    }

    private static void QuadNetFallbackTriangulatesContourSafely()
    {
        var contour = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 3f),
            new Vector2(0f, 3f)
        };

        AssertTrue(
            ReanimatorQuadNetAlgorithms.TryBuildContourFallback(contour, out var vertices, out var triangles, out var boundary),
            "QuadNet fallback should triangulate concave contour.");
        AssertEqual(contour.Length, vertices.Length, "QuadNet fallback vertex count.");
        AssertTriangleIndicesValid(triangles, vertices.Length, "QuadNet fallback triangles");
        AssertTrianglesNonDegenerate(vertices, triangles, "QuadNet fallback triangles");
        AssertFloat(Mathf.Abs(ReanimatorTriangulationAlgorithms.ComputeSignedArea(contour)), SumTriangleArea(vertices, triangles), 0.0001f, "QuadNet fallback area.");
        AssertEdgeIndicesValid(boundary, vertices.Length, "QuadNet fallback boundary");
        AssertEqual(contour.Length * 2, boundary.Length, "QuadNet fallback boundary edge count.");
    }

    private static void QuadNetRemapsVerticesAndEdgeKeysSafely()
    {
        var source = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1.08f, 0.06f),
            new Vector2(9f, 9f),
            new Vector2(2f, 2f)
        };
        var target = new[]
        {
            new Vector2(2f, 2f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f)
        };

        AssertTrue(ReanimatorQuadNetAlgorithms.TryBuildVertexRemap(source, target, out var remap), "QuadNet vertex remap should build.");
        AssertArray(new[] { 2, 1, -1, 0 }, remap, "QuadNet vertex remap.");

        var edgeKeys = new HashSet<ulong>
        {
            ReanimatorQuadNetAlgorithms.MakeEdgeKey(0, 1),
            ReanimatorQuadNetAlgorithms.MakeEdgeKey(1, 0),
            ReanimatorQuadNetAlgorithms.MakeEdgeKey(1, 2),
            ReanimatorQuadNetAlgorithms.MakeEdgeKey(0, 3),
            ReanimatorQuadNetAlgorithms.MakeEdgeKey(3, 3)
        };
        var pairs = ReanimatorQuadNetAlgorithms.RemapEdgeKeysToPairs(edgeKeys, remap, target.Length);
        AssertEdgeIndicesValid(pairs, target.Length, "QuadNet remapped edge pairs");
        AssertEqual(4, pairs.Length, "QuadNet remapped edge pair count.");
        AssertTrue(HasEdgePair(pairs, 2, 1), "QuadNet remap should keep mapped source edge.");
        AssertTrue(HasEdgePair(pairs, 2, 0), "QuadNet remap should keep second mapped source edge.");
        AssertFalse(HasEdgePair(pairs, 1, -1), "QuadNet remap should skip unmapped source edge.");
    }

    private static void QuadNetSelectsPrimaryContourAndClassifiesPoints()
    {
        var paths = new[]
        {
            new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) },
            new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 2f), new Vector2(0f, 2f), new Vector2(0f, 0f) }
        };

        AssertTrue(
            ReanimatorQuadNetAlgorithms.TrySelectPrimaryContour(paths, new Vector2(2f, 1f), new Vector2(1f, -1f), out var contour),
            "QuadNet should select largest primary contour.");
        AssertEqual(4, contour.Count, "Primary contour should drop duplicate closing point.");
        AssertVector(new Vector2(1f, -1f), contour[0], 0.0001f, "Primary contour transformed point 0.");
        AssertVector(new Vector2(7f, 1f), contour[2], 0.0001f, "Primary contour transformed point 2.");
        AssertTrue(ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(new Vector2(3f, 0f), contour), "Point inside primary contour.");
        AssertTrue(ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(new Vector2(7f, 0f), contour), "Point on primary contour edge.");
        AssertFalse(ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(new Vector2(8f, 0f), contour), "Point outside primary contour.");
        AssertTrue(
            ReanimatorQuadNetAlgorithms.IsPointOnAnyGridLine(new Vector2(2f, 3.00005f), new[] { 1f, 2f }, new[] { 3f }),
            "Point should match grid line by epsilon.");
        AssertFalse(
            ReanimatorQuadNetAlgorithms.IsPointOnAnyGridLine(new Vector2(2.01f, 3.01f), new[] { 1f, 2f }, new[] { 3f }),
            "Point should not match distant grid line.");
    }

    private static void QuadNetComputesGridStepFromDensity()
    {
        var bounds = new Rect(0f, 0f, 160f, 240f);
        AssertFloat(80f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, 0.01d), 0.0001f, "Minimum density step.");
        AssertFloat(20f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, 1d), 0.0001f, "Pivot density step.");
        AssertFloat(4f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, 5d), 0.0001f, "Maximum density step.");
        AssertFloat(4f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, 99d), 0.0001f, "Density should clamp high.");
        AssertFloat(80f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(bounds, -5d), 0.0001f, "Density should clamp low.");
        AssertFloat(1f, ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(new Rect(0f, 0f, 0f, 100f), 1d), 0.0001f, "Tiny bounds should use minimum dimension floor.");
    }

    private static void QuadNetPreviewCellSnapsToGenerationGrid()
    {
        var bounds = new Rect(10f, 20f, 160f, 240f);
        AssertTrue(
            ReanimatorQuadNetAlgorithms.TryGetQuadNetCellForTarget(
                bounds,
                1d,
                new Vector2(83f, 153f),
                out var cellMin,
                out var cellMax),
            "QuadNet preview cell should resolve.");
        AssertVector(new Vector2(80f, 140f), cellMin, 0.0001f, "Preview cell min should snap to generation grid lines.");
        AssertVector(new Vector2(100f, 160f), cellMax, 0.0001f, "Preview cell max should snap to generation grid lines.");

        AssertTrue(
            ReanimatorQuadNetAlgorithms.TryGetQuadNetCellForTarget(
                bounds,
                1d,
                new Vector2(-500f, 999f),
                out var clampedMin,
                out var clampedMax),
            "QuadNet preview cell should resolve for off-layer view center.");
        AssertFloat(0f, clampedMin.x, 0.0001f, "Off-layer preview cell should clamp to nearest generation-grid x cell.");
        AssertFloat(240f, clampedMin.y, 0.0001f, "Off-layer preview cell should clamp to nearest generation-grid y cell.");
        AssertFloat(20f, clampedMax.x - clampedMin.x, 0.0001f, "Clamped preview cell width should match generation step.");
        AssertFloat(20f, clampedMax.y - clampedMin.y, 0.0001f, "Clamped preview cell height should match generation step.");

        var rotatedCorners = new Vector2[4];
        AssertTrue(
            ReanimatorQuadNetAlgorithms.TryGetQuadNetCellCornersForTarget(
                new Rect(0f, 0f, 100f, 100f),
                1d,
                45f,
                new Vector2(50f, 50f),
                rotatedCorners),
            "Rotated QuadNet preview cell should resolve.");
        AssertTrue(Mathf.Abs(rotatedCorners[1].y - rotatedCorners[0].y) > 0.1f, "Rotated preview cell should not stay axis-aligned.");
        AssertFloat(
            Vector2.Distance(rotatedCorners[0], rotatedCorners[1]),
            Vector2.Distance(rotatedCorners[2], rotatedCorners[3]),
            0.0001f,
            "Rotated preview cell opposite edges should keep the same generation step.");
        var layerFrame = new Rect(0f, 0f, 100f, 100f);
        var smallContourBounds = new Rect(35f, 35f, 30f, 30f);
        var layerStep = ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(
            ReanimatorQuadNetAlgorithms.ComputeQuadNetGridBounds(layerFrame, 45f),
            1d);
        var contourStep = ReanimatorQuadNetAlgorithms.ComputeQuadNetStep(
            ReanimatorQuadNetAlgorithms.ComputeQuadNetGridBounds(smallContourBounds, 45f),
            1d);
        AssertTrue(Mathf.Abs(layerStep - contourStep) > 0.1f, "Fixture should distinguish layer-frame grid from contour-bounds grid.");
        AssertFloat(
            layerStep,
            Vector2.Distance(rotatedCorners[0], rotatedCorners[1]),
            0.0001f,
            "Rotated preview marker should use the same layer-frame grid step as QuadNet generation.");
        AssertFloat(5f, ReanimatorQuadNetAlgorithms.NormalizeQuadNetRotationDegrees(725f), 0.0001f, "QuadNet rotation should wrap high values.");
        AssertFloat(350f, ReanimatorQuadNetAlgorithms.NormalizeQuadNetRotationDegrees(-10f), 0.0001f, "QuadNet rotation should wrap negative values.");
    }

    private static void JointGeometryResolvesDirectedAngleArcs()
    {
        var start = DegreesToRadians(350f);
        var ten = DegreesToRadians(10f);
        var thirty = DegreesToRadians(30f);
        var twoHundred = DegreesToRadians(200f);

        AssertFloat(DegreesToRadians(20f), ReanimatorJointGeometryAlgorithms.GetDirectedAngleDistance(start, ten, true), 0.0001f, "Forward directed angle should wrap.");
        AssertFloat(DegreesToRadians(20f), ReanimatorJointGeometryAlgorithms.GetDirectedAngleDistance(ten, start, false), 0.0001f, "Backward directed angle should wrap.");
        AssertTrue(ReanimatorJointGeometryAlgorithms.IsAngleOnDirectedArc(ten, start, DegreesToRadians(40f), true, 0f), "Forward arc should include wrapped angle.");
        AssertFalse(ReanimatorJointGeometryAlgorithms.IsAngleOnDirectedArc(twoHundred, start, DegreesToRadians(40f), true, 0f), "Forward arc should exclude far angle.");
        AssertTrue(ReanimatorJointGeometryAlgorithms.IsAngleOnDirectedArc(thirty, ten, DegreesToRadians(40f), true, 0.0001f), "Forward arc should include direct angle.");
        AssertTrue(ReanimatorJointGeometryAlgorithms.IsAngleOnDirectedArc(start, ten, DegreesToRadians(25f), false, 0.0001f), "Backward arc should include wrapped angle.");
    }

    private static void JointGeometryResolvesPerimeterArcs()
    {
        AssertTrue(ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(2f, 9f, 4f, 10f, true, 0f), "Forward perimeter arc should wrap.");
        AssertFalse(ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(5f, 9f, 4f, 10f, true, 0f), "Forward perimeter arc should exclude outside distance.");
        AssertTrue(ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(8f, 1f, 4f, 10f, false, 0f), "Backward perimeter arc should wrap.");
        AssertFalse(ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(5f, 1f, 4f, 10f, false, 0f), "Backward perimeter arc should exclude outside distance.");
        AssertFalse(ReanimatorJointGeometryAlgorithms.IsPerimeterDistanceOnArc(0f, 0f, 1f, 0f, true, 0f), "Zero perimeter should reject.");
    }

    private static void JointGeometryFindsNearestContourRayIntersection()
    {
        var vertices = new[]
        {
            new Vector2(-2f, -2f),
            new Vector2(2f, -2f),
            new Vector2(2f, 2f),
            new Vector2(-2f, 2f),
            new Vector2(4f, -4f),
            new Vector2(4f, 4f)
        };
        var boundary = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5 };

        AssertTrue(
            ReanimatorJointGeometryAlgorithms.TryGetNearestContourRayIntersection(vertices, boundary, Vector2.zero, 0f, out var rightHit),
            "Ray should hit nearest right contour edge.");
        AssertVector(new Vector2(2f, 0f), rightHit, 0.0001f, "Nearest right ray hit.");

        AssertTrue(
            ReanimatorJointGeometryAlgorithms.TryGetNearestContourRayIntersection(vertices, boundary, Vector2.zero, DegreesToRadians(90f), out var topHit),
            "Ray should hit top contour edge.");
        AssertVector(new Vector2(0f, 2f), topHit, 0.0001f, "Top ray hit.");

        AssertFalse(
            ReanimatorJointGeometryAlgorithms.TryGetNearestContourRayIntersection(vertices, new[] { 0, 1, -1, 2, 4, 9 }, Vector2.zero, DegreesToRadians(45f), out _),
            "Invalid or missed boundary pairs should fail.");
    }

    private static void JointV2CalculatesBoneAngleRelativeToInitialPose()
    {
        var initial = Quaternion.AngleAxis(30f, Vector3.forward);
        AssertFloat(
            45f,
            ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
                initial,
                Quaternion.AngleAxis(75f, Vector3.forward)),
            0.0001f,
            "Joint V2 should report a positive local rotation from the initial pose.");
        AssertFloat(
            -60f,
            ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
                initial,
                Quaternion.AngleAxis(-30f, Vector3.forward)),
            0.0001f,
            "Joint V2 should report a negative local rotation from the initial pose.");
        AssertFloat(
            0f,
            ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(default, default),
            0.0001f,
            "Joint V2 should handle missing serialized rotations safely.");
    }

    private static void JointV2PrioritizesBodyParentSprite()
    {
        AssertEqual(
            1,
            ReanimatorJointGeometryAlgorithms.SelectPreferredParentSpriteIndex(
                new[] { "torso", "body", "shirt" }),
            "Joint V2 should give the body sprite absolute priority among parent-bone users.");
        AssertEqual(
            0,
            ReanimatorJointGeometryAlgorithms.SelectPreferredParentSpriteIndex(
                new[] { "torso", "shirt" }),
            "Joint V2 should keep the first parent-bone user when body is absent.");
        AssertEqual(
            -1,
            ReanimatorJointGeometryAlgorithms.SelectPreferredParentSpriteIndex(null),
            "Joint V2 should report no body candidate when no sprite uses the parent bone.");
    }

    private static void JointV2IgnoresHiddenSpriteCandidates()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "hips", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "l_leg", 0, new Vector2(10f, 0f))
        });
        SetPrivateField(renderer, "selectedBoneIndex", 1);

        var hiddenLeg = BuildLayer("cl_leggins", 930, Vector2.zero);
        hiddenLeg.visible = false;
        var visibleLeg = BuildLayer("l_leg", 931, Vector2.zero);
        visibleLeg.visible = true;
        var hiddenBody = BuildLayer("body", 932, Vector2.zero);
        hiddenBody.visible = false;
        var visibleTorso = BuildLayer("torso", 933, Vector2.zero);
        visibleTorso.visible = true;

        var hiddenLegData = BuildJointV2CandidateData(renderer, hiddenLeg, 1);
        var visibleLegData = BuildJointV2CandidateData(renderer, visibleLeg, 1);
        var hiddenBodyData = BuildJointV2CandidateData(renderer, hiddenBody, 0);
        var visibleTorsoData = BuildJointV2CandidateData(renderer, visibleTorso, 0);
        var dataBySelectionKey =
            (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey["hidden-leg"] = hiddenLegData;
        dataBySelectionKey["visible-leg"] = visibleLegData;
        dataBySelectionKey["hidden-body"] = hiddenBodyData;
        dataBySelectionKey["visible-torso"] = visibleTorsoData;
        SetPrivateField(renderer, "selectedLayerSelectionKey", "hidden-leg");

        var currentArgs = new object[] { null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryResolveJointV2CurrentBoneLayer", currentArgs),
            "Joint V2 should find a visible moving-bone sprite.");
        AssertTrue(
            ReferenceEquals(visibleLegData, currentArgs[0]),
            "Joint V2 should ignore a hidden selected sprite and use the visible candidate.");

        var bodyArgs = new object[] { 0, null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryResolveJointV2BodyLayer", bodyArgs),
            "Joint V2 should find a visible parent-bone sprite.");
        AssertTrue(
            ReferenceEquals(visibleTorsoData, bodyArgs[1]),
            "Joint V2 should ignore a hidden body even though its name normally has priority.");
    }

    private static object BuildJointV2CandidateData(
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.LayerInfo layer,
        int rendererBoneIndex)
    {
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { rendererBoneIndex });
        return data;
    }

    private static void JointV2KeepsCurrentAndParentSpritesDistinct()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "hips", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "l_leg", 0, new Vector2(10f, 0f))
        });
        SetPrivateField(renderer, "selectedBoneIndex", 1);

        var body = BuildLayer("body", 934, Vector2.zero);
        body.visible = true;
        var leg = BuildLayer("l_leg", 935, Vector2.zero);
        leg.visible = true;
        var bodyData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(bodyData, "layer", body);
        SetPrivateField(bodyData, "boneWeights", new[]
        {
            new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = 0.8f,
                boneIndex1 = 1,
                weight1 = 0.2f
            }
        });
        SetPrivateField(bodyData, "spriteBoneToRendererBone", new[] { 0, 1 });
        var legData = BuildJointV2CandidateData(renderer, leg, 1);
        var dataBySelectionKey =
            (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey["body"] = bodyData;
        dataBySelectionKey["leg"] = legData;
        SetPrivateField(renderer, "selectedLayerSelectionKey", "body");

        var bodyArgs = new object[] { 0, null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryResolveJointV2BodyLayer", bodyArgs),
            "Joint V2 should resolve the visible body as its parent sprite.");
        AssertTrue(ReferenceEquals(bodyData, bodyArgs[1]), "Joint V2 parent sprite should be body.");

        var currentArgs = new object[] { null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryResolveJointV2CurrentBoneLayer", currentArgs),
            "Joint V2 should resolve a different moving sprite.");
        AssertTrue(
            ReferenceEquals(legData, currentArgs[0]),
            "Joint V2 should never reuse its parent sprite as the moving sprite.");
    }

    private static void JointV2MovesDynamicBaseWhilePreservingTipAndChildren()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var rendererBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "hips", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "l_leg", 0, new Vector2(1f, 0f)),
            BuildVisibleWorldBone(2, "l_shin", 1, new Vector2(4f, 0f))
        };
        rendererBones[0].length = 1f;
        rendererBones[1].length = 4f;
        rendererBones[2].length = 2f;
        SetPrivateField(renderer, "bones", rendererBones);
        SetPrivateField(renderer, "selectedBoneIndex", 1);
        SetPrivateField(renderer, "bonesTreatAsWorldSpace", false);
        SetPrivateField(renderer, "rotateLocalBoneOffsetsByParent", true);
        InvokePrivateMethod(renderer, "RecomputeBoneWorldTransforms");

        var originalBase = rendererBones[1].worldPosition;
        var originalTip = rendererBones[1].worldPosition + Vector2.right * rendererBones[1].length;
        var originalChildPosition = rendererBones[2].worldPosition;
        var originalChildRotation = rendererBones[2].worldRotation;
        var targetBase = new Vector2(2f, 1f);

        AssertVector(
            originalBase,
            (Vector2)InvokePrivateMethod(renderer, "GetJointV2InterpolatedBaseTarget", targetBase, 0f),
            0.0001f,
            "Joint V2 base offset zero should preserve the original base.");
        AssertVector(
            Vector2.Lerp(originalBase, targetBase, 0.5f),
            (Vector2)InvokePrivateMethod(renderer, "GetJointV2InterpolatedBaseTarget", targetBase, 0.5f),
            0.0001f,
            "Joint V2 base offset one half should place the base midway to the center.");
        AssertVector(
            targetBase,
            (Vector2)InvokePrivateMethod(renderer, "GetJointV2InterpolatedBaseTarget", targetBase, 1f),
            0.0001f,
            "Joint V2 base offset one should place the base at the center.");

        var virtualPoseArgs = new object[] { targetBase, null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryBuildJointV2VirtualBonePose", virtualPoseArgs),
            "Joint V2 should build a separate derived bone pose for preview.");
        var virtualBones = (List<PsdLayerPreviewRenderer.BoneInfo>)virtualPoseArgs[1];
        AssertVector(originalBase, rendererBones[1].worldPosition, 0.0001f,
            "Joint V2 virtual preview must not feed its result back into the source bone.");
        AssertVector(targetBase, virtualBones[1].worldPosition, 0.0001f,
            "Joint V2 virtual preview should place its derived base at the red center.");
        var virtualDirection = virtualBones[1].worldRotation * Vector3.right;
        var virtualTip = virtualBones[1].worldPosition +
                         new Vector2(virtualDirection.x, virtualDirection.y) * virtualBones[1].length;
        AssertVector(originalTip, virtualTip, 0.0001f,
            "Joint V2 virtual preview should preserve the source tip.");
        AssertVector(originalChildPosition, virtualBones[2].worldPosition, 0.0001f,
            "Joint V2 virtual preview should preserve child positions.");

        AssertTrue((bool)InvokePrivateMethod(
            renderer,
            "MoveJointV2DynamicBoneBasePreservingTipAndChildren",
            targetBase),
            "Joint V2 preview should move the dynamic bone before tool Apply.");

        AssertVector(targetBase, rendererBones[1].worldPosition, 0.0001f,
            "Joint V2 should move the dynamic bone base to its red center point.");
        var movedDirection = rendererBones[1].worldRotation * Vector3.right;
        var movedTip = rendererBones[1].worldPosition +
                       new Vector2(movedDirection.x, movedDirection.y) * rendererBones[1].length;
        AssertVector(originalTip, movedTip, 0.0001f,
            "Joint V2 should keep the dynamic bone tip in place.");
        AssertVector(originalChildPosition, rendererBones[2].worldPosition, 0.0001f,
            "Joint V2 should keep direct children in place.");
        AssertFloat(0f, Quaternion.Angle(originalChildRotation, rendererBones[2].worldRotation), 0.0001f,
            "Joint V2 should keep direct child rotations unchanged.");
        AssertFloat(
            0f,
            ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
                rendererBones[1].initialRotation,
                rendererBones[1].rotation),
            0.0001f,
            "Joint V2 preview base movement should preserve the dynamic bone's relative pose angle.");
    }

    private static void JointV2RotatesGeometryForSamplePoses()
    {
        var pivot = new Vector2(2f, 3f);
        var point = new Vector2(4f, 3f);
        AssertVector(
            point,
            ReanimatorJointGeometryAlgorithms.RotatePointAround(point, pivot, 0f),
            0.0001f,
            "Joint V2 zero-degree sample should preserve geometry.");
        AssertVector(
            new Vector2(2f, 1f),
            ReanimatorJointGeometryAlgorithms.RotatePointAround(point, pivot, -90f),
            0.0001f,
            "Joint V2 negative ninety-degree sample should rotate clockwise around the bone base.");
        AssertFloat(
            -35f,
            ReanimatorJointGeometryAlgorithms.GetSamplePoseDeltaDegrees(35f, 0f),
            0.0001f,
            "Joint V2 zero-degree sample should first cancel the current preview rotation.");
        AssertFloat(
            -45f,
            ReanimatorJointGeometryAlgorithms.GetSamplePoseDeltaDegrees(0f, -45f),
            0.0001f,
            "Joint V2 negative forty-five sample should rotate from the default preview pose.");
        AssertVector(
            new Vector2(3.4142135f, 1.5857865f),
            ReanimatorJointGeometryAlgorithms.RotatePointAround(point, pivot, -45f),
            0.0001f,
            "Joint V2 attached contact should follow the moving bone from its zero-degree bind point.");
        AssertFloat(
            0f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2DynamicWeight(-2f, -2f, 2f),
            0.0001f,
            "Joint V2 static-side generated vertices should start at zero dynamic weight.");
        AssertFloat(
            0.5f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2DynamicWeight(0f, -2f, 2f),
            0.0001f,
            "Joint V2 center vertex should blend both bones evenly.");
        AssertFloat(
            1f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2DynamicWeight(2f, -2f, 2f),
            0.0001f,
            "Joint V2 dynamic-side generated vertices should end at full dynamic weight.");
        AssertFloat(
            0.5f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2ChainWeight(1f, 0f),
            0.0001f,
            "Joint V2 contour chain should be evenly blended at its white midpoint.");
        AssertFloat(
            0.75f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2ChainWeight(1f, 0.5f),
            0.0001f,
            "Joint V2 contour chain should interpolate by contour subdivision.");
        AssertFloat(
            1f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2ChainWeight(1f, 1f),
            0.0001f,
            "Joint V2 contour chain should preserve its exact outer endpoint weight.");
        var firstIterationWeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        AssertFloat(
            firstIterationWeightCurve.Evaluate(0.25f),
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(
                firstIterationWeightCurve,
                0.25f),
            0.0001f,
            "Joint V2 first iteration should sample its serialized weight curve.");
        AssertFloat(
            0.25f,
            ReanimatorJointGeometryAlgorithms.EvaluateJointV2WeightCurve(null, 0.25f),
            0.0001f,
            "Joint V2 missing first-iteration curve should fall back to linear weights.");
    }

    private static void JointV2DetectsShallowOverlapAndOffsetsSpecialAnchorsAlongContour()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var rendererBones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "hips", -1, new Vector2(-10f, 0f)),
            BuildVisibleWorldBone(1, "leg", 0, Vector2.zero)
        };
        rendererBones[0].length = 5f;
        rendererBones[1].length = 5f;
        SetPrivateField(renderer, "bones", rendererBones);
        SetPrivateField(renderer, "selectedBoneIndex", 1);

        var shallowContour = new List<Vector2>
        {
            new Vector2(-0.1f, -2f),
            new Vector2(2f, -2f),
            new Vector2(2f, 2f),
            new Vector2(-0.1f, 2f)
        };
        var shallowArgs = new object[]
        {
            Vector2.zero,
            Vector2.up,
            Vector2.down,
            shallowContour,
            false,
            Vector2.zero
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryClassifyJointV2Connection", shallowArgs),
            "Joint V2 should classify a connection when the static bone is available.");
        AssertTrue((bool)shallowArgs[4],
            "Joint V2 should mark a connection special when its one-fifth probe leaves the moving contour.");

        var deepContour = new List<Vector2>
        {
            new Vector2(-1f, -2f),
            new Vector2(2f, -2f),
            new Vector2(2f, 2f),
            new Vector2(-1f, 2f)
        };
        var deepArgs = new object[]
        {
            Vector2.zero,
            Vector2.up,
            Vector2.down,
            deepContour,
            false,
            Vector2.zero
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryClassifyJointV2Connection", deepArgs),
            "Joint V2 should classify a sufficiently deep connection.");
        AssertFalse((bool)deepArgs[4],
            "Joint V2 should keep the normal algorithm when its one-fifth probe remains inside the contour.");

        var squareVertices = new[]
        {
            new Vector2(-2f, -2f),
            new Vector2(2f, -2f),
            new Vector2(2f, 2f),
            new Vector2(-2f, 2f)
        };
        var anchorArgs = new object[]
        {
            Vector2.zero,
            Vector2.up * 2f,
            Vector2.left,
            0.5f,
            new List<Vector2>(squareVertices),
            new List<float> { 4f, 4f, 4f, 4f },
            16f,
            Vector2.zero
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryBuildJointV2SpecialAnchor", anchorArgs),
            "Joint V2 special mode should find a green contour intersection.");
        var greenPoint = (Vector2)anchorArgs[7];
        AssertTrue(greenPoint.x > 0.1f,
            "Joint V2 special anchor should choose the contour direction away from the static bone.");
        AssertFloat(1f, greenPoint.x, 0.0001f,
            "Joint V2 half-distance should offset by half of the full red-to-orange contour distance.");

        var longLegLoop = new List<Vector2>
        {
            new Vector2(-2f, -20f),
            new Vector2(2f, -20f),
            new Vector2(2f, 2f),
            new Vector2(-2f, 2f)
        };
        var specialContourPoints = new List<Vector2>();
        InvokePrivateMethod(
            renderer,
            "AddJointV2SpecialContourPointsByDistance",
            Vector2.zero,
            new Vector2(2f, 0f),
            longLegLoop,
            new List<float> { 4f, 22f, 4f, 22f },
            52f,
            Vector2.zero,
            0f,
            specialContourPoints);
        AssertEqual(8, specialContourPoints.Count,
            "Joint V2 special mode should build two four-point chains around each green anchor.");
        for (var i = 0; i < specialContourPoints.Count; i++)
        {
            AssertTrue(specialContourPoints[i].y >= -2.001f,
                "Joint V2 special magenta points should use local contour distance instead of escaping toward a distant heel intersection.");
        }
    }

    private static void JointV2WeightCurvesSerialize()
    {
        var source = ScriptableObject.CreateInstance<Reanimator>();
        var restored = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var defaultRegular = GetPrivateField<AnimationCurve>(source, "jointV2FirstIterationWeightCurve");
            var defaultSpecial = GetPrivateField<AnimationCurve>(source, "jointV2SpecialWeightCurve");
            AssertTrue(defaultRegular != null, "Joint V2 should create its tuned regular curve by default.");
            AssertEqual(2, defaultRegular.length, "Joint V2 tuned regular curve should preserve both captured keys.");
            AssertFloat(0.2830229f, defaultRegular.keys[0].value, 0.000001f,
                "Joint V2 regular default should preserve the tuned first weight.");
            AssertTrue(defaultSpecial != null, "Joint V2 should create its tuned special curve by default.");
            AssertEqual(6, defaultSpecial.length, "Joint V2 tuned special curve should preserve all captured keys.");
            AssertFloat(0.556594849f, defaultSpecial.keys[2].value, 0.000001f,
                "Joint V2 special default should preserve the tuned middle weight.");
            AssertFloat(0.1f, GetPrivateField<float>(source, "jointV2BoneBaseOffset"), 0.0001f,
                "Joint V2 should use the tuned bone-base offset by default.");
            AssertFloat(0.5f, GetPrivateField<float>(source, "jointV2SpecialJointDistance"), 0.0001f,
                "Joint V2 should use the tuned special contour distance by default.");

            var expected = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.35f, 0.8f),
                new Keyframe(1f, 1f));
            var expectedSpecial = new AnimationCurve(
                new Keyframe(0f, 0.1f),
                new Keyframe(0.6f, 0.25f),
                new Keyframe(1f, 0.9f));
            SetPrivateField(source, "jointV2FirstIterationWeightCurve", expected);
            SetPrivateField(source, "jointV2SpecialWeightCurve", expectedSpecial);
            SetPrivateField(source, "jointV2BoneBaseOffset", 0.37f);
            SetPrivateField(source, "jointV2SpecialJointDistance", 0.68f);

            var curveResolver = new PsdLayerPreviewRenderer();
            AssertTrue(
                ReferenceEquals(
                    expected,
                    InvokePrivateMethod(
                        curveResolver,
                        "ResolveJointV2WeightCurve",
                        false,
                        expected,
                        expectedSpecial)),
                "Regular Joint V2 geometry should use the regular weight curve.");
            AssertTrue(
                ReferenceEquals(
                    expectedSpecial,
                    InvokePrivateMethod(
                        curveResolver,
                        "ResolveJointV2WeightCurve",
                        true,
                        expected,
                        expectedSpecial)),
                "Special Joint V2 geometry should use the special weight curve.");

            var json = JsonUtility.ToJson(source);
            AssertTrue(
                json.Contains("jointV2FirstIterationWeightCurve"),
                "Joint V2 first weight curve should be included in serialized window state.");
            AssertTrue(
                json.Contains("jointV2SpecialWeightCurve"),
                "Joint V2 special weight curve should be included in serialized window state.");
            AssertFalse(
                json.Contains("jointV2SecondTest"),
                "Removed Joint V2 second-iteration state must not remain serialized.");
            AssertTrue(
                json.Contains("jointV2BoneBaseOffset"),
                "Joint V2 normalized bone-base offset should be included in serialized window state.");
            AssertTrue(
                json.Contains("jointV2SpecialJointDistance"),
                "Joint V2 special-joint distance should be included in serialized window state.");
            JsonUtility.FromJsonOverwrite(json, restored);

            var actual = GetPrivateField<AnimationCurve>(restored, "jointV2FirstIterationWeightCurve");
            AssertTrue(actual != null, "Joint V2 first weight curve should survive deserialization.");
            AssertEqual(3, actual.length, "Joint V2 first weight curve should preserve every key.");
            AssertFloat(0.35f, actual.keys[1].time, 0.0001f, "Joint V2 first weight curve should preserve key time.");
            AssertFloat(0.8f, actual.keys[1].value, 0.0001f, "Joint V2 first weight curve should preserve key value.");
            var actualSpecial = GetPrivateField<AnimationCurve>(restored, "jointV2SpecialWeightCurve");
            AssertTrue(actualSpecial != null, "Joint V2 special weight curve should survive deserialization.");
            AssertEqual(3, actualSpecial.length, "Joint V2 special weight curve should preserve every key.");
            AssertFloat(0.6f, actualSpecial.keys[1].time, 0.0001f,
                "Joint V2 special weight curve should preserve key time.");
            AssertFloat(0.25f, actualSpecial.keys[1].value, 0.0001f,
                "Joint V2 special weight curve should preserve key value.");
            AssertFloat(
                0.37f,
                GetPrivateField<float>(restored, "jointV2BoneBaseOffset"),
                0.0001f,
                "Joint V2 normalized bone-base offset should survive deserialization.");
            AssertFloat(
                0.68f,
                GetPrivateField<float>(restored, "jointV2SpecialJointDistance"),
                0.0001f,
                "Joint V2 special-joint distance should survive deserialization.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(restored);
        }
    }

}
#endif
