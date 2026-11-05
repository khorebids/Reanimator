#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void ProjectorStretchDedupesPointsAndBuildsBoundary()
    {
        var vertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 1.5f)
        };

        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(vertices, new Vector2(1.005f, 1.005f)), "Projector dedupe should use tolerance.");
        AssertFalse(ReanimatorProjectorAlgorithms.ContainsStretchPoint(vertices, new Vector2(1.02f, 1.02f)), "Projector dedupe should not merge distant points.");

        var loop = ReanimatorProjectorAlgorithms.BuildStretchBoundaryLoop(vertices);
        AssertArray(new[] { 0, 1, 2, 5, 3 }, loop, "Projector stretch boundary should be convex hull and ignore inner point.");
        AssertFalse(loop.Contains(4), "Projector stretch boundary should exclude inner point.");
        AssertEdgeIndicesValid(ReanimatorEditGeometryAlgorithms.BuildBoundaryPairsFromLoop(loop), vertices.Length, "Projector boundary pairs");

        var weights = new[]
        {
            default,
            new BoneWeight { boneIndex0 = 2, weight0 = 0.75f },
            new BoneWeight { boneIndex0 = 3, weight0 = 0.00001f }
        };
        AssertFalse(ReanimatorProjectorAlgorithms.TryGetSourceVertexWeight(vertices, weights, 0, out _), "Zero source weight should not be usable.");
        AssertTrue(ReanimatorProjectorAlgorithms.TryGetSourceVertexWeight(vertices, weights, 1, out var sourceWeight), "Positive source weight should be usable.");
        AssertEqual(2, sourceWeight.boneIndex0, "Usable source weight index.");
        AssertFalse(ReanimatorProjectorAlgorithms.TryGetSourceVertexWeight(vertices, weights, 2, out _), "Tiny source weight should be ignored.");
        AssertFalse(ReanimatorProjectorAlgorithms.TryGetSourceVertexWeight(vertices, weights, 8, out _), "Out-of-range source weight should fail.");
    }

    private static void ProjectorAplUnionsContoursAndTransfersNearestSourceWeights()
    {
        AssertEqual(0, (int)ReanimatorProjectorStretchMode.Apl, "APL should be the first Projector Stretch mode.");

        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "apl_source",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            }
        });

        var targetLayer = BuildLayer("apl-target", 891, Vector2.zero);
        targetLayer.sourceRect = new Rect(-1f, -2f, 5f, 5f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("apl-source", 892, Vector2.zero);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        sourceLayer.textureRect = targetLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "APL fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);

        var targetData = CreateProjectorContractLayerData(
            renderer,
            targetLayer,
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(2.2f, 0f),
                new Vector2(2.2f, 2f),
                new Vector2(0f, 2f),
                new Vector2(0.5f, 1f)
            },
            null,
            null);
        SetPrivateField(targetData, "triangles", new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 });
        SetPrivateField(targetData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 4, 1, 4, 2, 4, 3, 4 });
        SetPrivateField(targetData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });

        var sourceVertices = new[]
        {
            new Vector2(1f, -1f),
            new Vector2(3f, -1f),
            new Vector2(3f, 1f),
            new Vector2(1f, 1f)
        };
        var sourceData = CreateProjectorContractLayerData(
            renderer,
            sourceLayer,
            sourceVertices,
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(sourceData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(sourceData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorStretch(
                targetKey,
                new[] { sourceKey },
                true,
                false,
                false,
                true,
                1.1f,
                out var insertedCount,
                out var removedCount,
                out _,
                out var error),
            $"APL should build its result from A and the A/B intersections. Error={error}");
        AssertEqual(5, insertedCount, "APL should insert boundary intersections, every internal mesh-edge intersection, and the enclosed B vertex.");
        AssertEqual(2, removedCount, "APL should remove the covered A contour point and the A interior point.");

        var resultVertices = GetPrivateField<Vector2[]>(targetData, "vertices");
        AssertEqual(8, resultVertices.Length, "APL should keep the A contour, every mesh intersection, and the enclosed B point.");
        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(1f, 0f)), "APL should add the lower contour intersection.");
        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(2.2f, 1f)), "APL should add the right contour intersection.");
        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(2f, 0f)), "APL should add the intersection with an internal B triangle edge.");
        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(2.2f, 0.2f)), "APL should add every crossing of an internal B triangle edge.");
        AssertFalse(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(0.5f, 1f)), "APL should delete non-contour A points.");
        AssertTrue(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(1f, 1f)), "APL should transfer B vertices enclosed by the A-derived contour.");
        AssertFalse(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(1f, -1f)), "APL should not let an outside B vertex expand A.");
        AssertFalse(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(3f, -1f)), "APL should not transfer remote B geometry.");
        AssertFalse(ReanimatorProjectorAlgorithms.ContainsStretchPoint(resultVertices, new Vector2(3f, 1f)), "APL should keep B outside the result contour excluded.");

        var boundaryPairs = GetPrivateField<int[]>(targetData, "boundaryEdgePairs");
        AssertEqual(14, boundaryPairs.Length, "APL should rebuild the seven-edge A-derived contour including all internal mesh intersections.");
        AssertTrue(IsProjectorBoundaryPoint(resultVertices, boundaryPairs, new Vector2(1f, 0f)), "APL should keep the lower A/B intersection as an explicit boundary vertex after Vertex Cull.");
        AssertTrue(IsProjectorBoundaryPoint(resultVertices, boundaryPairs, new Vector2(2.2f, 1f)), "APL should keep the right A/B intersection as an explicit boundary vertex after Vertex Cull.");
        AssertTrue(IsProjectorBoundaryPoint(resultVertices, boundaryPairs, new Vector2(2f, 0f)), "APL should keep the internal cyan-edge intersection as an explicit boundary vertex after Vertex Cull.");
        AssertTrue(IsProjectorBoundaryPoint(resultVertices, boundaryPairs, new Vector2(2.2f, 0.2f)), "APL should keep every cyan-edge crossing as an explicit boundary vertex after Vertex Cull.");
        var resultWeights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        for (var i = 0; i < resultWeights.Length; i++)
            AssertTrue(ReanimatorProjectorAlgorithms.HasUsableWeight(resultWeights[i]), $"APL result vertex {i} should receive its nearest B weight.");
        AssertArray(new[] { 0 }, GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone"), "APL should remap the B bone onto A.");
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryRetriangulateProjectorAplPreservingBoundaryVertices", targetData),
            "APL fallback should triangulate the result while preserving every boundary and interior vertex.");

        var recoveryData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("apl-recovery", 895, Vector2.zero),
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(2f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 4f),
                new Vector2(0f, 4f),
                new Vector2(2f, 2f),
                new Vector2(5f, 2f)
            },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(recoveryData, "triangles", Array.Empty<int>());
        SetPrivateField(recoveryData, "allEdgePairs", Array.Empty<int>());
        SetPrivateField(recoveryData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4, 0 });
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryRetriangulateProjectorAplFallback", recoveryData),
            "APL fallback chain should prioritize boundary-safe recovery when one transferred B point is outside A.");
        var recoveryVertices = GetPrivateField<Vector2[]>(recoveryData, "vertices");
        var recoveryTriangles = GetPrivateField<int[]>(recoveryData, "triangles");
        AssertEqual(6, recoveryVertices.Length,
            "APL recovery should prune only the unusable interior B point.");
        AssertTriangleIndicesValid(recoveryTriangles, recoveryVertices.Length, "APL recovery triangulation");
        AssertTrianglesNonDegenerate(recoveryVertices, recoveryTriangles, "APL recovery triangulation");

        var validPlanarData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("apl-valid-planar", 896, Vector2.zero),
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 4f),
                new Vector2(0f, 4f)
            },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(validPlanarData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "IsProjectorAplTriangulationUsable", validPlanarData),
            "APL should accept a planar triangulation that references every vertex.");

        var unreferencedData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("apl-unreferenced", 897, Vector2.zero),
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 4f),
                new Vector2(0f, 4f),
                new Vector2(2f, 2f)
            },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(unreferencedData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "IsProjectorAplTriangulationUsable", unreferencedData),
            "APL should reject a fallback that silently drops a transferred vertex.");

        var crossingData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("apl-crossing", 898, Vector2.zero),
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(2f, 4f),
                new Vector2(0f, 3f),
                new Vector2(4f, 3f),
                new Vector2(2f, -1f)
            },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(crossingData, "triangles", new[] { 0, 1, 2, 3, 4, 5 });
        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "IsProjectorAplTriangulationUsable", crossingData),
            "APL should reject a fallback with crossing triangle edges.");

        var guaranteedData = CreateProjectorContractLayerData(
            renderer,
            BuildLayer("apl-guaranteed", 899, Vector2.zero),
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(4f, 0f),
                new Vector2(4f, 4f),
                new Vector2(0f, 4f)
            },
            null,
            null);
        var guaranteedTriangles = new[] { 0, 1, 2, 0, 2, 3 };
        SetPrivateField(guaranteedData, "triangles", guaranteedTriangles);
        SetPrivateField(guaranteedData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(guaranteedData, "boundaryEdgePairs", new[] { 0, 1 });
        var guaranteedSources = Array.CreateInstance(sourceData.GetType(), 1);
        guaranteedSources.SetValue(sourceData, 0);
        var guaranteedArgs = new object[] { guaranteedData, guaranteedSources, false, 4f, 0, 0, null };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryBuildProjectorAplGeometry", guaranteedArgs),
            "APL should incrementally project B into A when merged contour construction is unavailable.");
        AssertTrue(guaranteedArgs[6] == null, "APL projected fallback should not expose a user-facing error.");
        AssertTrue((int)guaranteedArgs[4] > 0,
            "APL projected fallback should perform real geometry work instead of reporting +0/-0.");
        AssertEqual(0, (int)guaranteedArgs[5], "APL guaranteed path should report no removed vertices.");
        var guaranteedVertices = GetPrivateField<Vector2[]>(guaranteedData, "vertices");
        var guaranteedResultTriangles = GetPrivateField<int[]>(guaranteedData, "triangles");
        AssertEqual(4 + (int)guaranteedArgs[4], guaranteedVertices.Length,
            "APL projected fallback should append every reported vertex to A.");
        AssertTriangleIndicesValid(guaranteedResultTriangles, guaranteedVertices.Length,
            "APL projected fallback triangulation");
        AssertTrianglesNonDegenerate(guaranteedVertices, guaranteedResultTriangles,
            "APL projected fallback triangulation");
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "IsProjectorAplTriangulationUsable", guaranteedData),
            "APL projected fallback should leave a fully referenced planar mesh.");
        var guaranteedWeights = GetPrivateField<BoneWeight[]>(guaranteedData, "boneWeights");
        for (var i = 0; i < guaranteedWeights.Length; i++)
            AssertTrue(ReanimatorProjectorAlgorithms.HasUsableWeight(guaranteedWeights[i]),
                $"APL guaranteed vertex {i} should still receive a B weight.");

        const string productionAplPath =
            "Assets/0.Internal/Game/Textures/Characters/grl/main 1/1.45_body_nude.psd";
        var productionRenderer = new PsdLayerPreviewRenderer();
        InvokePrivateMethod(productionRenderer, "Rebuild", productionAplPath, false);
        var productionPlanArgs = new object[] { null, 0, null };
        AssertTrue(
            (bool)InvokePrivateMethod(productionRenderer, "TryBuildFashionablePlan", productionPlanArgs),
            "Production APL fixture should expose its CL plan.");
        var productionPlan = (System.Collections.IList)productionPlanArgs[0];
        object productionHoodyPlan = null;
        foreach (var planItem in productionPlan)
        {
            if (string.Equals(GetPrivateField<string>(planItem, "LayerName"), "cl_hoody", StringComparison.Ordinal))
            {
                productionHoodyPlan = planItem;
                break;
            }
        }
        AssertTrue(
            productionHoodyPlan != null,
            "Production APL fixture should contain cl_hoody in the over plan.");
        var productionTargetKey = GetPrivateField<string>(productionHoodyPlan, "TargetSelectionKey");
        var productionSourceKeys = GetPrivateField<string[]>(productionHoodyPlan, "SourceSelectionKeys");
        AssertTrue(
            productionRenderer.ApplyProjectorStretch(
                productionTargetKey,
                productionSourceKeys,
                true,
                false,
                false,
                false,
                4f,
                out var productionInserted,
                out _,
                out _,
                out var productionError),
            $"Production cl_hoody APL should apply after repairing its persisted mesh. Error={productionError}");
        AssertTrue(productionInserted > 0,
            "Production cl_hoody APL should perform real geometry work instead of reporting +0/-0.");
        var productionDataByKey = (System.Collections.IDictionary)GetPrivateField<object>(
            productionRenderer,
            "vertexDataByLayerSelectionKey");
        var productionHoodyData = productionDataByKey[productionTargetKey];
        AssertTrue(productionHoodyData != null, "Production cl_hoody result data should remain available.");
        AssertTrue(
            (bool)InvokePrivateMethod(productionRenderer, "IsProjectorAplTriangulationUsable", productionHoodyData),
            "Production cl_hoody APL result should be planar and fully referenced.");
        AssertFalse(
            productionRenderer.ApplyProjectorStretch(
                productionTargetKey,
                productionSourceKeys,
                true,
                false,
                false,
                false,
                4f,
                out _,
                out _,
                out _,
                out var productionRepeatError),
            "Repeated production cl_hoody APL should be an idempotent no-op.");
        AssertEqual("Projector Stretch made no geometry changes.", productionRepeatError,
            "Repeated APL should report an internal no-op status for batch callers.");

        object productionTightsPlan = null;
        foreach (var planItem in productionPlan)
        {
            if (string.Equals(GetPrivateField<string>(planItem, "LayerName"), "cl_tights", StringComparison.Ordinal))
            {
                productionTightsPlan = planItem;
                break;
            }
        }
        AssertTrue(productionTightsPlan != null,
            "Production APL fixture should contain cl_tights in the bot plan.");
        var productionTightsTargetKey = GetPrivateField<string>(productionTightsPlan, "TargetSelectionKey");
        var productionTightsSourceKeys = GetPrivateField<string[]>(productionTightsPlan, "SourceSelectionKeys");
        var productionTightsPrepareArgs = new object[] { productionTightsTargetKey, null };
        AssertTrue(
            (bool)InvokePrivateMethod(productionRenderer, "TryPrepareProjectorAplTargetGeometry", productionTightsPrepareArgs),
            $"Production cl_tights should prepare its actual APL target object. Error={productionTightsPrepareArgs[1]}");
        var productionTightsData = productionDataByKey[productionTightsTargetKey];
        var productionTightsTrianglesBefore = GetPrivateField<int[]>(productionTightsData, "triangles").ToArray();
        var productionTightsVertices = GetPrivateField<Vector2[]>(productionTightsData, "vertices");
        SetPrivateField(productionTightsData, "boneWeights", new BoneWeight[productionTightsVertices.Length]);
        AssertTrue(
            productionRenderer.ApplyProjectorStretch(
                productionTightsTargetKey,
                productionTightsSourceKeys,
                true,
                false,
                false,
                false,
                4f,
                out var productionTightsInserted,
                out var productionTightsRemoved,
                out _,
                out var productionTightsError),
            $"Production cl_tights should copy its source contours, points, and weights. Error={productionTightsError}");
        AssertTrue(productionTightsInserted > 0,
            "Production cl_tights APL must insert the body and leg source points inside its contour.");
        AssertEqual(0, productionTightsRemoved,
            "Production cl_tights APL should preserve its clothing contour points.");
        AssertTrue(string.IsNullOrEmpty(productionTightsError),
            "Successful cl_tights APL should not emit a status message.");
        var productionTightsAppliedData = productionDataByKey[productionTightsTargetKey];
        var productionTightsVerticesAfter = GetPrivateField<Vector2[]>(productionTightsAppliedData, "vertices");
        AssertEqual(
            productionTightsVertices.Length + productionTightsInserted,
            productionTightsVerticesAfter.Length,
            "Production cl_tights should retain its contour and append every reported source point.");
        var productionTightsTrianglesAfter = GetPrivateField<int[]>(productionTightsAppliedData, "triangles");
        AssertFalse(productionTightsTrianglesBefore.SequenceEqual(productionTightsTrianglesAfter),
            "Production cl_tights APL should rebuild connectivity around transferred source points.");
        var productionTightsWeightsAfter = GetPrivateField<BoneWeight[]>(productionTightsAppliedData, "boneWeights");
        for (var weightIndex = 0; weightIndex < productionTightsWeightsAfter.Length; weightIndex++)
        {
            AssertTrue(
                ReanimatorProjectorAlgorithms.HasUsableWeight(productionTightsWeightsAfter[weightIndex]),
                $"Production cl_tights vertex {weightIndex} should receive a projected source weight.");
        }
        AssertTrue(
            productionTightsAppliedData != null &&
            (bool)InvokePrivateMethod(productionRenderer, "IsProjectorAplTriangulationUsable", productionTightsAppliedData),
            "Production cl_tights APL result should be planar and fully referenced.");
        AssertFalse(
            productionRenderer.ApplyProjectorStretch(
                productionTightsTargetKey,
                productionTightsSourceKeys,
                true,
                false,
                false,
                false,
                4f,
                out _,
                out _,
                out _,
                out var productionTightsRepeatError),
            "Repeated production cl_tights APL should be a silent no-op after topology and weights match.");
        AssertEqual("Projector Stretch made no geometry changes.", productionTightsRepeatError,
            "Repeated cl_tights APL should report an internal no-op status for batch callers.");
        var bodyLayer = BuildLayer("body", 893, Vector2.zero);
        var boobLayer = BuildLayer("l_boob", 894, Vector2.zero);
        var bodyPriorityData = CreateProjectorContractLayerData(
            renderer,
            bodyLayer,
            new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f) },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(bodyPriorityData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(bodyPriorityData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        var boobPriorityData = CreateProjectorContractLayerData(
            renderer,
            boobLayer,
            new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) },
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(boobPriorityData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(boobPriorityData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        var prioritySources = Array.CreateInstance(bodyPriorityData.GetType(), 2);
        prioritySources.SetValue(bodyPriorityData, 0);
        prioritySources.SetValue(boobPriorityData, 1);
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsProjectorAplBodySource", bodyPriorityData),
            "APL should identify body as the lower-priority geometry source.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsProjectorAplBoobSource", boobPriorityData),
            "APL should identify l_boob as an override geometry source.");
        AssertTrue((bool)InvokePrivateMethod(renderer, "IsPointInsideProjectorAplBoobUnion", Vector2.zero, prioritySources),
            "APL should suppress body geometry and weights inside boob geometry.");
        AssertFalse((bool)InvokePrivateMethod(renderer, "IsPointInsideProjectorAplBoobUnion", new Vector2(1.5f, 0f), prioritySources),
            "APL should retain body geometry outside boob geometry.");
    }

    private static void FashionableMapsClFoldersToExactAplTargets()
    {
        AssertArray(
            new[] { "body", "l_arm", "r_arm", "l_boob", "r_boob" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("over").ToArray(),
            "Fashionable over targets.");
        AssertArray(
            new[] { "body", "l_boob", "r_boob" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("TOP").ToArray(),
            "Fashionable top targets should be case insensitive.");
        AssertArray(
            new[] { "l_arm" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("l_sleeve").ToArray(),
            "Fashionable l_sleeve targets.");
        AssertArray(
            new[] { "r_arm" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("r_sleeve").ToArray(),
            "Fashionable r_sleeve targets.");
        AssertArray(
            new[] { "body", "l_leg", "r_leg" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("bot").ToArray(),
            "Fashionable bot targets.");
        AssertArray(
            new[] { "body", "l_boob", "r_boob" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("bra").ToArray(),
            "Fashionable bra targets.");
        AssertArray(
            new[] { "body", "l_leg", "r_leg" },
            PsdLayerPreviewRenderer.GetFashionableTargetLayerNames("panty").ToArray(),
            "Fashionable panty targets.");

        var renderer = new PsdLayerPreviewRenderer();
        AssertFalse(renderer.HasFashionableFolder(), "Fashionable should be disabled without a CL hierarchy group.");

        var layers = new List<PsdLayerPreviewRenderer.LayerInfo>();
        var hierarchy = new List<PsdLayerPreviewRenderer.LayerHierarchyNode>
        {
            new PsdLayerPreviewRenderer.LayerHierarchyNode
            {
                key = "group-cl",
                parentKey = string.Empty,
                name = "CL",
                isGroup = true
            },
            new PsdLayerPreviewRenderer.LayerHierarchyNode
            {
                key = "group-over",
                parentKey = "group-cl",
                name = "over",
                isGroup = true
            }
        };
        var layerNames = new[] { "coat", "body", "l_arm", "r_arm", "l_boob", "r_boob" };
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        for (var i = 0; i < layerNames.Length; i++)
        {
            var layer = BuildLayer(layerNames[i], 940 + i, Vector2.zero);
            layers.Add(layer);
            hierarchy.Add(new PsdLayerPreviewRenderer.LayerHierarchyNode
            {
                key = "layer-" + i,
                parentKey = i == 0 ? "group-over" : string.Empty,
                name = layerNames[i],
                isGroup = false,
                layerIndex = i
            });

            var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
            var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            SetPrivateField(data, "layer", layer);
            SetPrivateField(data, "vertices", new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f)
            });
            dataBySelectionKey[selectionKey] = data;
        }

        SetPrivateField(renderer, "layerInfos", layers);
        SetPrivateField(renderer, "layerHierarchyNodes", hierarchy);
        AssertTrue(renderer.HasFashionableFolder(), "Fashionable should enable when a CL hierarchy group exists.");
        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "IsFashionableDefaultRectangleContour", dataBySelectionKey[(string)InvokePrivateMethod(renderer, "BuildSelectionKey", layers[0])]),
            "Fashionable should not classify an arbitrary triangle as a default rectangular contour.");

        var primaryLoopArgs = new object[]
        {
            new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4 },
            new[]
            {
                new Vector2(20f, 20f), new Vector2(21f, 20f), new Vector2(21f, 21f), new Vector2(20f, 21f),
                new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f)
            },
            null
        };
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "TryBuildProjectorAplPrimaryBoundaryLoop", primaryLoopArgs),
            "APL should resolve a primary contour from disconnected Auto Geometry islands.");
        var primaryLoop = (List<int>)primaryLoopArgs[2];
        AssertTrue(primaryLoop.All(index => index >= 4),
            "APL should select the largest clothing island instead of the first tiny alpha island.");

        var planArgs = new object[] { null, 0, null };
        AssertTrue((bool)InvokePrivateMethod(renderer, "TryBuildFashionablePlan", planArgs),
            "Fashionable should build a plan from CL/over.");
        var plan = (System.Collections.IList)planArgs[0];
        AssertEqual(1, plan.Count, "Fashionable should plan every sprite below the matching category folder.");
        AssertEqual("coat", GetPrivateField<string>(plan[0], "LayerName"),
            "Fashionable should keep the clothing sprite as the APL destination.");
        AssertEqual(5, GetPrivateField<string[]>(plan[0], "SourceSelectionKeys").Length,
            "Fashionable over should resolve all five external APL sources.");

        var aplBefore = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var aplAfter = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        var unchangedVertices = new[]
        {
            Vector2.zero,
            Vector2.right,
            Vector2.up
        };
        var unchangedTriangles = new[] { 0, 1, 2 };
        SetPrivateField(aplBefore, "vertices", unchangedVertices);
        SetPrivateField(aplAfter, "vertices", (Vector2[])unchangedVertices.Clone());
        SetPrivateField(aplBefore, "triangles", unchangedTriangles);
        SetPrivateField(aplAfter, "triangles", (int[])unchangedTriangles.Clone());
        SetPrivateField(aplBefore, "boneWeights", new[] { default(BoneWeight), default, default });
        SetPrivateField(aplAfter, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 2, weight0 = 1f },
            default,
            default
        });
        AssertTrue(
            (bool)InvokePrivateMethod(renderer, "HasProjectorStretchLayerChanges", aplBefore, aplAfter),
            "Fashionable APL should apply weight-only changes when its vertex set is unchanged.");
        SetPrivateField(aplAfter, "boneWeights", new[] { default(BoneWeight), default, default });
        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "HasProjectorStretchLayerChanges", aplBefore, aplAfter),
            "Fashionable APL should classify a completely identical result as a no-op.");

        var panelSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Comfimator/Editor/ComfimatorPanel.cs"));
        var autoWeightsButton = panelSource.IndexOf("DrawAutoWeightsButton(host);", StringComparison.Ordinal);
        var fashionableButton = panelSource.IndexOf("DrawFashionableButton(host);", StringComparison.Ordinal);
        var cookButton = panelSource.IndexOf("DrawCookPrefabButton(host);", StringComparison.Ordinal);
        AssertTrue(autoWeightsButton >= 0 && fashionableButton > autoWeightsButton && cookButton > fashionableButton,
            "Comfimator should draw Fashionable after Auto Weights and before Cook Prefab.");
        AssertTrue(panelSource.Contains("!hasClFolder"),
            "Comfimator should disable Fashionable when CL is absent.");
    }

    private static void ProjectorClearTargetWeightsRemovesOldSkinningData()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 7, 8 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.one) });
        SetPrivateField(data, "usesSpriteBindPoses", true);
        SetPrivateField(data, "hasSkinning", true);
        SetPrivateField(data, "lastPoseVersion", 12);
        SetPrivateField(data, "deformedVertices", new[] { new Vector2(1f, 2f) });
        SetPrivateField(data, "skinMatrices", new[] { Matrix4x4.identity });

        InvokePrivateMethod(renderer, "ClearProjectorTargetSkinningData", data, 3);

        var clearedWeights = GetPrivateField<BoneWeight[]>(data, "boneWeights");
        AssertEqual(3, clearedWeights.Length, "Clear target weights should keep target vertex weight array sized.");
        for (var i = 0; i < clearedWeights.Length; i++)
            AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(clearedWeights[i]), $"Cleared target weight {i} should be empty.");
        AssertEqual(0, GetPrivateField<int[]>(data, "spriteBoneToRendererBone").Length, "Clear target weights should remove old sprite bone map.");
        AssertEqual(0, GetPrivateField<Matrix4x4[]>(data, "bindBoneInverseMatrices").Length, "Clear target weights should remove old bind matrices.");
        AssertFalse(GetPrivateField<bool>(data, "usesSpriteBindPoses"), "Clear target weights should remove sprite bind pose mode.");
        AssertFalse(GetPrivateField<bool>(data, "hasSkinning"), "Clear target weights should disable old skinning.");
        AssertEqual(-1, GetPrivateField<int>(data, "lastPoseVersion"), "Clear target weights should invalidate pose cache.");
        AssertEqual(null, GetPrivateField<Vector2[]>(data, "deformedVertices"), "Clear target weights should clear deformed vertices.");
        AssertEqual(null, GetPrivateField<Matrix4x4[]>(data, "skinMatrices"), "Clear target weights should clear skin matrices.");
    }

    private static void ProjectorClearTargetWeightsKeepsRemappedRootChain()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 10f,
                color = Color.white
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "child",
                parentId = 0,
                position = new Vector2(10f, 0f),
                worldPosition = new Vector2(10f, 0f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.white
            }
        });

        var layer = BuildLayer("projector-root-chain", 880, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 64f, 64f);
        layer.textureRect = layer.sourceRect;

        var sourceData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(sourceData, "spriteBoneToRendererBone", new[] { 1 });
        var targetData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(targetData, "layer", layer);
        SetPrivateField(targetData, "pixelsPerUnit", 1f);
        SetPrivateField(targetData, "pivotPixels", Vector2.zero);
        SetPrivateField(targetData, "spriteBoneToRendererBone", Array.Empty<int>());
        SetPrivateField(targetData, "bindBoneInverseMatrices", Array.Empty<Matrix4x4>());

        var args = new object[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            sourceData,
            targetData,
            null,
            null
        };
        AssertTrue((bool)InvokePrivateMethod(renderer, "TryRemapProjectorWeight", args), "Projector remap should accept source child bone.");

        var remappedWeight = (BoneWeight)args[3];
        var expandedMap = GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone");
        AssertArray(new[] { 0, 1 }, expandedMap, "Projector clear should append root before weighted child.");
        AssertEqual(1, remappedWeight.boneIndex0, "Child source weight should remap to child sprite bone after root insertion.");

        var desiredBones = (List<SpriteBone>)InvokePrivateMethod(
            renderer,
            "BuildSpriteBonesForLayerData",
            targetData,
            CreateProjectorTestBinding(layer),
            "raw_local",
            Array.Empty<SpriteBone>(),
            null);
        AssertEqual(2, desiredBones.Count, "Desired sprite bones should include root and child.");
        AssertEqual(-1, desiredBones[0].parentId, "Root sprite bone should stay root.");
        AssertEqual(0, desiredBones[1].parentId, "Child sprite bone should parent to root.");
    }

    private static void ProjectorApplyDoesNotRepairUncoveredVerticesWithTargetBones()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "sit1",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "sit3",
                parentId = -1,
                position = new Vector2(20f, 20f),
                worldPosition = new Vector2(20f, 20f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.magenta
            }
        });

        var targetLayer = BuildLayer("cl_sit3_tights", 883, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("sit1", 884, Vector2.zero);
        sourceLayer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "Projector fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);

        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);
        var targetData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(targetData, "layer", targetLayer);
        SetPrivateField(targetData, "pixelsPerUnit", 1f);
        SetPrivateField(targetData, "pivotPixels", Vector2.zero);
        SetPrivateField(targetData, "vertices", new[]
        {
            new Vector2(0.1f, 0.1f),
            new Vector2(0.8f, 0.1f),
            new Vector2(0.1f, 0.8f),
            new Vector2(20f, 20f),
            new Vector2(21f, 20f),
            new Vector2(20f, 21f)
        });
        SetPrivateField(targetData, "triangles", new[] { 0, 1, 2, 3, 4, 5 });
        SetPrivateField(targetData, "boneWeights", new BoneWeight[6]);
        SetPrivateField(targetData, "spriteBoneToRendererBone", new[] { 1 });
        SetPrivateField(targetData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });

        var sourceData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(sourceData, "layer", sourceLayer);
        SetPrivateField(sourceData, "pixelsPerUnit", 1f);
        SetPrivateField(sourceData, "pivotPixels", Vector2.zero);
        SetPrivateField(sourceData, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f)
        });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(sourceData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(sourceData, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(sourceData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorWeights(new[] { sourceKey }, false, out var weightedCount, out _, out var error),
            $"Projector Apply should transfer source weights. Error={error}");
        AssertEqual(3, weightedCount, "Projector should weight only the covered target island.");

        var targetMap = GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone");
        AssertArray(new[] { 1, 0 }, targetMap, "Projector should append sit1 source bone without deleting the stale target map.");
        var weights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        AssertTrue(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[0]), "Covered target vertex should receive source weight.");
        AssertEqual(1, weights[0].boneIndex0, "Covered target vertex should point at appended sit1 sprite bone, not stale sit3 bone.");
        for (var i = 3; i < weights.Length; i++)
            AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[i]), $"Uncovered target vertex {i} must not be repaired from stale sit3 target bones.");
    }

    private static void ProjectorApplyDoesNotPullNearestSourceTriangleIntoUncoveredVertices()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "source_arm",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            }
        });

        var targetLayer = BuildLayer("target-cloth", 887, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 128f, 128f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("tiny-source", 888, Vector2.zero);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "Projector nearest fallback fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);

        var targetData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(targetData, "layer", targetLayer);
        SetPrivateField(targetData, "pixelsPerUnit", 1f);
        SetPrivateField(targetData, "pivotPixels", Vector2.zero);
        SetPrivateField(targetData, "vertices", new[]
        {
            new Vector2(0.1f, 0.1f),
            new Vector2(64f, 0f),
            new Vector2(0f, 64f)
        });
        SetPrivateField(targetData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(targetData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(targetData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(targetData, "boneWeights", new BoneWeight[3]);
        SetPrivateField(targetData, "spriteBoneToRendererBone", Array.Empty<int>());
        SetPrivateField(targetData, "bindBoneInverseMatrices", Array.Empty<Matrix4x4>());

        var sourceData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(sourceData, "layer", sourceLayer);
        SetPrivateField(sourceData, "pixelsPerUnit", 1f);
        SetPrivateField(sourceData, "pivotPixels", Vector2.zero);
        SetPrivateField(sourceData, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f)
        });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(sourceData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(sourceData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(sourceData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(sourceData, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(sourceData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorWeights(new[] { sourceKey }, true, out var weightedCount, out _, out var error),
            $"Projector Apply should transfer directly covered source weights. Error={error}");
        AssertEqual(1, weightedCount, "Projector should weight only the target vertex inside source geometry.");

        var weights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        AssertTrue(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[0]), "Covered target vertex should receive source weight.");
        AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[1]), "Uncovered target vertex should not pull the nearest source triangle.");
        AssertFalse(ReanimatorProjectorAlgorithms.HasUsableWeight(weights[2]), "Uncovered target vertex should not be smoothed from the covered vertex.");
    }

    private static void ProjectorExpandedCoveragePostProcessFillsMissingVertices()
    {
        AssertTrue(
            new ReanimatorProjectorSettings().PostProcessExpandedCoverage,
            "Projector expanded coverage post process should be enabled by default.");
        AssertTrue(
            new ReanimatorProjectorSettings().SmoothPostProcessWeights,
            "Projector per-iteration smoothing should be enabled by default.");

        var smoothingRenderer = new PsdLayerPreviewRenderer();
        var smoothingData = CreatePrivateNestedInstance(smoothingRenderer, "LayerVertexData");
        SetPrivateField(smoothingData, "vertices", new[] { Vector2.zero, Vector2.right, Vector2.right * 2f });
        SetPrivateField(smoothingData, "allEdgePairs", new[] { 0, 1, 1, 2 });
        SetPrivateField(smoothingData, "triangles", Array.Empty<int>());
        SetPrivateField(smoothingData, "spriteBoneToRendererBone", new[] { 0, 1 });
        var smoothingWeights = new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            default
        };
        AssertTrue(
            (bool)InvokePrivateMethod(smoothingRenderer, "SmoothProjectorWeightsOnce", smoothingData, smoothingWeights),
            "Projector post process should smooth weighted topology vertices.");
        AssertFloat(0.5f, GetWeight(smoothingWeights[0], 0), 0.0001f, "Projector smoothing should retain half of the current vertex weight.");
        AssertFloat(0.5f, GetWeight(smoothingWeights[0], 1), 0.0001f, "Projector smoothing should blend half of the neighbor weight.");
        AssertFalse(
            ReanimatorProjectorAlgorithms.HasUsableWeight(smoothingWeights[2]),
            "Projector smoothing should not fill a vertex that expanded geometry has not covered yet.");

        var scaled = ReanimatorProjectorAlgorithms.ScaleVerticesAroundBoundsCenter(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            1.2f);
        AssertVector(new Vector2(-0.1f, -0.1f), scaled[0], 0.0001f, "Projector post process should scale from the midpoint between extreme points.");
        AssertVector(new Vector2(1.1f, 1.1f), scaled[2], 0.0001f, "Projector post process should expand the opposite extreme symmetrically.");

        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "source_root",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            }
        });

        var targetLayer = BuildLayer("expanded-target", 889, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 2f, 2f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("expanded-source", 890, Vector2.zero);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "Expanded Projector fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);

        var targetVertices = new[]
        {
            new Vector2(0.5f, 0.5f),
            new Vector2(1.04f, 0.4f),
            new Vector2(1.44f, 0.6f)
        };
        var sourceVertices = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        var targetData = CreateProjectorContractLayerData(renderer, targetLayer, targetVertices, null, null);
        var sourceData = CreateProjectorContractLayerData(
            renderer,
            sourceLayer,
            sourceVertices,
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2, 0, 2, 3 });
        SetPrivateField(sourceData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0, 0, 2 });
        SetPrivateField(sourceData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 3, 3, 0 });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorWeights(new[] { sourceKey }, true, out var directCount, out _, out var directError),
            $"Direct Projector pass should weight the covered vertex. Error={directError}");
        AssertEqual(1, directCount, "Direct Projector pass should leave the two outside vertices empty.");

        AssertTrue(
            renderer.ApplyProjectorWeights(targetKey, new[] { sourceKey }, true, true, out var expandedCount, out _, out var expandedError),
            $"Expanded Projector post process should fill vertices reached by iterations 1-10. Error={expandedError}");
        AssertEqual(3, expandedCount, "Expanded Projector post process should fill both initially uncovered vertices.");
        var expandedWeights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        for (var i = 0; i < expandedWeights.Length; i++)
            AssertTrue(ReanimatorProjectorAlgorithms.HasUsableWeight(expandedWeights[i]), $"Expanded Projector target vertex {i} should have a usable weight.");
    }

    private static void ProjectorPreservesExplicitZeroWeightKeyBones()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "source_main",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "source_key",
                parentId = -1,
                position = new Vector2(8f, 0f),
                worldPosition = new Vector2(8f, 0f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.yellow
            }
        });

        var targetLayer = BuildLayer("projector-key-target", 890, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("projector-key-source", 891, Vector2.zero);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "Projector key-bone fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);

        var targetData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(targetData, "layer", targetLayer);
        SetPrivateField(targetData, "pixelsPerUnit", 1f);
        SetPrivateField(targetData, "pivotPixels", Vector2.zero);
        SetPrivateField(targetData, "vertices", new[]
        {
            new Vector2(1f, 1f),
            new Vector2(8f, 1f),
            new Vector2(1f, 8f)
        });
        SetPrivateField(targetData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(targetData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(targetData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(targetData, "boneWeights", new BoneWeight[3]);
        SetPrivateField(targetData, "spriteBoneToRendererBone", Array.Empty<int>());
        SetPrivateField(targetData, "bindBoneInverseMatrices", Array.Empty<Matrix4x4>());

        var sourceWeight = new BoneWeight
        {
            boneIndex0 = 0,
            boneIndex1 = 1,
            weight0 = 1f,
            weight1 = 0f
        };
        var sourceData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(sourceData, "layer", sourceLayer);
        SetPrivateField(sourceData, "pixelsPerUnit", 1f);
        SetPrivateField(sourceData, "pivotPixels", Vector2.zero);
        SetPrivateField(sourceData, "vertices", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(12f, 0f),
            new Vector2(0f, 12f)
        });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(sourceData, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(sourceData, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(sourceData, "boneWeights", new[] { sourceWeight, sourceWeight, sourceWeight });
        SetPrivateField(sourceData, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(sourceData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorWeights(new[] { sourceKey }, true, out var weightedCount, out _, out var error),
            $"Projector Apply should preserve explicit zero-weight key bones. Error={error}");
        AssertEqual(3, weightedCount, "Projector key-bone fixture should weight all covered target vertices.");
        AssertArray(new[] { 0, 1 }, GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone"), "Projector should keep source main and explicit key bones.");
        var weights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        for (var i = 0; i < weights.Length; i++)
        {
            AssertEqual(0, weights[i].boneIndex0, $"Projected target vertex {i} should keep the main source bone.");
            AssertFloat(1f, weights[i].weight0, 0.0001f, $"Projected target vertex {i} main bone weight.");
            AssertEqual(1, weights[i].boneIndex1, $"Projected target vertex {i} should keep the explicit zero-weight key bone.");
            AssertFloat(0f, weights[i].weight1, 0.0001f, $"Projected target vertex {i} key bone weight.");
        }

        var writeData = InvokePrivateMethod(renderer, "BuildImporterSkinningWriteData", targetData);
        AssertArray(new[] { 0, 1 }, GetPrivateField<int[]>(writeData, "spriteBoneToRendererBone"), "Importer write compaction must not drop explicit projector key bones.");
        var writeWeights = GetPrivateField<BoneWeight[]>(writeData, "boneWeights");
        AssertEqual(1, writeWeights[0].boneIndex1, "Importer write should preserve the remapped zero-weight key bone slot.");
        AssertFloat(0f, writeWeights[0].weight1, 0.0001f, "Importer write should preserve the key bone as zero weight.");
    }

    private static void ProjectorClearTargetWeightsWritesOnlySourceBones()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "sit1",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "sit3",
                parentId = -1,
                position = new Vector2(30f, 30f),
                worldPosition = new Vector2(30f, 30f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.magenta
            }
        });

        var targetLayer = BuildLayer("cl_sit3_tights", 885, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 16f, 16f);
        targetLayer.textureRect = targetLayer.sourceRect;
        var sourceLayer = BuildLayer("sit1", 886, Vector2.zero);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "cachedPath", "Assets/projector_clear_source_only.psd");
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });
        AssertTrue(renderer.SelectLayer(targetLayer), "Projector clear fixture target should become selected.");

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var targetStableKey = (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId);
        SetPrivateField(renderer, "editGeometrySelectedLayerStableKey", targetStableKey);
        SetPrivateField(renderer, "weightBrushLayerStableKey", targetStableKey);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);

        var targetData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(targetData, "layer", targetLayer);
        SetPrivateField(targetData, "pixelsPerUnit", 1f);
        SetPrivateField(targetData, "pivotPixels", Vector2.zero);
        SetPrivateField(targetData, "vertices", new[] { new Vector2(1f, 1f), new Vector2(8f, 1f), new Vector2(1f, 8f) });
        SetPrivateField(targetData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(targetData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(targetData, "spriteBoneToRendererBone", new[] { 1 });
        SetPrivateField(targetData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });
        SetPrivateField(targetData, "hasSkinning", true);

        var sourceData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(sourceData, "layer", sourceLayer);
        SetPrivateField(sourceData, "pixelsPerUnit", 1f);
        SetPrivateField(sourceData, "pivotPixels", Vector2.zero);
        SetPrivateField(sourceData, "vertices", new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(0f, 10f) });
        SetPrivateField(sourceData, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(sourceData, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f }
        });
        SetPrivateField(sourceData, "spriteBoneToRendererBone", new[] { 0 });
        SetPrivateField(sourceData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity });

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[sourceKey] = sourceData;

        AssertTrue(
            renderer.ApplyProjectorWeights(new[] { sourceKey }, true, out var weightedCount, out _, out var error),
            $"Projector Apply should transfer source weights after clearing target data. Error={error}");
        AssertEqual(3, weightedCount, "Projector should weight all covered target vertices.");
        AssertArray(new[] { 0 }, GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone"), "Clear target weights should remove stale sit3 bones before source remap.");
        var weights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        for (var i = 0; i < weights.Length; i++)
            AssertEqual(0, weights[i].boneIndex0, $"Projected target vertex {i} should point to the source sit1 bone.");
    }

    private static void ApplyCompactsUnusedSpriteBonesBeforeImporterWrite()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "root",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.white
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "stale_sit3",
                parentId = -1,
                position = new Vector2(30f, 30f),
                worldPosition = new Vector2(30f, 30f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.magenta
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = "painted_sit1",
                parentId = 0,
                position = new Vector2(2f, 0f),
                worldPosition = new Vector2(2f, 0f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            }
        });

        var layer = BuildLayer("cl_sit3_tights", 887, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 16f, 16f);
        layer.textureRect = layer.sourceRect;
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero, Vector2.right });
        SetPrivateField(data, "boneWeights", new[]
        {
            new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            new BoneWeight { boneIndex0 = 1, weight0 = 1f }
        });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 1, 2 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        var writeData = InvokePrivateMethod(renderer, "BuildImporterSkinningWriteData", data);
        AssertArray(new[] { 2 }, GetPrivateField<int[]>(writeData, "spriteBoneToRendererBone"), "Apply writer should drop unused stale target bones without moving the painted bone behind an unweighted parent.");
        var weights = GetPrivateField<BoneWeight[]>(writeData, "boneWeights");
        AssertEqual(0, weights[0].boneIndex0, "Painted source bone should stay the first compacted weight slot.");
        AssertEqual(0, weights[1].boneIndex0, "All painted vertices should use the compacted source bone index.");
    }

    private static void ApplyRebuildsCompactedSpriteBoneSlotFromRendererMapping()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "stale_sit3",
                parentId = -1,
                position = new Vector2(30f, 30f),
                worldPosition = new Vector2(30f, 30f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.magenta
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = "painted_sit1",
                parentId = -1,
                position = new Vector2(2f, 0f),
                worldPosition = new Vector2(2f, 0f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 5f,
                color = Color.cyan
            }
        });

        var layer = BuildLayer("cl_sit3_tights", 889, Vector2.zero);
        layer.sourceRect = new Rect(0f, 0f, 16f, 16f);
        layer.textureRect = layer.sourceRect;
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "vertices", new[] { Vector2.zero });
        SetPrivateField(data, "boneWeights", new[] { new BoneWeight { boneIndex0 = 1, weight0 = 1f } });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 0, 1 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        var writeData = InvokePrivateMethod(renderer, "BuildImporterSkinningWriteData", data);
        AssertArray(new[] { 1 }, GetPrivateField<int[]>(writeData, "spriteBoneToRendererBone"), "Apply writer should keep only the painted renderer bone.");

        var currentBones = new List<SpriteBone>
        {
            new SpriteBone { name = "stale_sit3", parentId = -1, color = Color.magenta },
            new SpriteBone { name = "painted_sit1", parentId = -1, color = Color.cyan }
        };
        var desired = (List<SpriteBone>)InvokePrivateMethod(
            renderer,
            "BuildSpriteBonesForLayerData",
            writeData,
            CreateProjectorTestBinding(layer),
            "raw_local",
            currentBones,
            new Dictionary<int, SpriteBone>());

        AssertEqual(1, desired.Count, "Compacted Apply write should produce a single sprite bone slot.");
        AssertEqual("painted_sit1", desired[0].name, "Compacted slot must be rebuilt from the painted renderer bone instead of reusing stale currentBones[0].");
        var weights = GetPrivateField<BoneWeight[]>(writeData, "boneWeights");
        AssertEqual(0, weights[0].boneIndex0, "Painted vertex should point at the rebuilt painted slot.");
    }

    private static void ApplyTreatsZeroWeightsAsNoWritableSkinning()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "vertices", new[] { Vector2.zero });
        SetPrivateField(data, "boneWeights", new[] { default(BoneWeight) });
        SetPrivateField(data, "spriteBoneToRendererBone", new[] { 4, 5 });
        SetPrivateField(data, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });

        AssertFalse(
            (bool)InvokePrivateMethod(renderer, "HasWritableImporterSkinningData", data),
            "Apply writer should clear old sprite bones when no vertex has usable weights.");
    }

    private static object CreateProjectorTestBinding(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        var renderer = new PsdLayerPreviewRenderer();
        var binding = CreatePrivateNestedInstance(renderer, "LayerSpriteBinding");
        SetPrivateField(binding, "pixelsPerUnit", 1f);
        SetPrivateField(binding, "pivotPixels", Vector2.zero);
        SetPrivateField(binding, "rect", layer != null ? layer.sourceRect : new Rect(0f, 0f, 64f, 64f));
        return binding;
    }

    private static void ProjectorApplyUsesPendingGeometrySnapshot()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var layer = BuildLayer("projector-snapshot", 881, new Vector2(10f, 20f));
        layer.sourceRect = new Rect(0f, 0f, 64f, 64f);
        layer.textureRect = layer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });

        var selectionKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", layer);
        var stableKey = (string)InvokePrivateMethod(renderer, "BuildKey", layer.name, layer.layerId);
        var staleData = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(staleData, "layer", layer);
        SetPrivateField(staleData, "vertices", new[] { new Vector2(900f, 900f) });
        SetPrivateField(staleData, "spriteBoneToRendererBone", new[] { 4, 5 });
        SetPrivateField(staleData, "bindBoneInverseMatrices", new[] { Matrix4x4.identity, Matrix4x4.identity });
        SetPrivateField(staleData, "usesSpriteBindPoses", true);

        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        dataBySelectionKey[selectionKey] = staleData;

        var patch = CreatePrivateNestedInstance(renderer, "AutoGeometryPatch");
        var snapshotVertices = new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(1f, 2f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(30f, 2f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, new Vector2(1f, 30f))
        };
        SetPrivateField(patch, "assetPath", "Assets/projector_snapshot.psd");
        SetPrivateField(patch, "layerSelectionKey", selectionKey);
        SetPrivateField(patch, "layerStableKey", stableKey);
        SetPrivateField(patch, "verticesDoc", snapshotVertices);
        SetPrivateField(patch, "vertexUvs", new[] { Vector2.zero, Vector2.right, Vector2.up });
        SetPrivateField(patch, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(patch, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(patch, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(patch, "boneWeights", new BoneWeight[snapshotVertices.Length]);
        SetPrivateField(patch, "spriteBoneToRendererBone", Array.Empty<int>());
        SetPrivateField(patch, "bindBoneInverseMatrices", Array.Empty<Matrix4x4>());
        SetPrivateField(patch, "usesSpriteBindPoses", false);

        var args = new object[] { patch, null };
        AssertTrue((bool)InvokePrivateMethod(renderer, "TryBuildLayerDataFromAutoGeometryPatch", args), "Projector Apply should build importer data from the pending snapshot.");
        var snapshotData = args[1];
        AssertArray(snapshotVertices, GetPrivateField<Vector2[]>(snapshotData, "vertices"), "Projector Apply should use patch vertices instead of stale live vertices.");
        AssertEqual(0, GetPrivateField<int[]>(snapshotData, "spriteBoneToRendererBone").Length, "Projector Apply snapshot should preserve an intentionally empty bone map.");
        AssertEqual(0, GetPrivateField<int[]>(staleData, "spriteBoneToRendererBone").Length, "Projector Apply should clear stale live bone maps from an empty pending patch.");
        AssertFalse(GetPrivateField<bool>(staleData, "usesSpriteBindPoses"), "Projector Apply should clear stale live sprite bind pose mode.");
    }

    private static void ProjectorApplyResolvesTargetBindingBySpriteGuid()
    {
        var renderer = new PsdLayerPreviewRenderer();
        var wrongLayer = BuildLayer("duplicate-target", 882, new Vector2(0f, 0f));
        wrongLayer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        wrongLayer.textureRect = wrongLayer.sourceRect;
        wrongLayer.spriteGuid = "11111111111111111111111111111111";
        var targetLayer = BuildLayer("duplicate-target", 882, new Vector2(50f, 0f));
        targetLayer.sourceRect = new Rect(50f, 0f, 32f, 32f);
        targetLayer.textureRect = targetLayer.sourceRect;
        targetLayer.spriteGuid = "22222222222222222222222222222222";
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { wrongLayer, targetLayer });

        var wrongKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", wrongLayer);
        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var bindingType = typeof(PsdLayerPreviewRenderer).GetNestedType("LayerSpriteBinding", BindingFlags.NonPublic);
        var bindingsType = typeof(Dictionary<,>).MakeGenericType(typeof(string), bindingType);
        var bindings = (System.Collections.IDictionary)Activator.CreateInstance(bindingsType);
        var wrongBinding = Activator.CreateInstance(bindingType);
        var targetBinding = Activator.CreateInstance(bindingType);
        SetPrivateField(wrongBinding, "spriteId", new GUID("11111111111111111111111111111111"));
        SetPrivateField(targetBinding, "spriteId", new GUID("22222222222222222222222222222222"));
        bindings.Add(wrongKey, wrongBinding);
        bindings.Add(targetKey, targetBinding);

        var patch = CreatePrivateNestedInstance(renderer, "AutoGeometryPatch");
        SetPrivateField(patch, "layerSelectionKey", wrongKey);
        SetPrivateField(patch, "layerStableKey", (string)InvokePrivateMethod(renderer, "BuildKey", targetLayer.name, targetLayer.layerId));
        SetPrivateField(patch, "spriteGuid", targetLayer.spriteGuid);
        SetPrivateField(patch, "layerName", targetLayer.name);
        SetPrivateField(patch, "layerId", targetLayer.layerId);
        SetPrivateField(patch, "layerPosition", targetLayer.position);
        SetPrivateField(patch, "sourceRect", targetLayer.sourceRect);

        var args = new object[] { bindings, patch, null, null };
        AssertTrue((bool)InvokePrivateMethod(renderer, "TryResolveLayerBindingForAutoGeometryPatch", args), "Projector Apply should resolve importer binding by the patch sprite GUID first.");
        var resolvedBinding = args[2];
        var resolvedKey = (string)args[3];
        AssertEqual(targetKey, resolvedKey, "Projector Apply should resolve the target layer selection key, not a stale/wrong key.");
        AssertEqual(
            "22222222222222222222222222222222",
            GetPrivateField<GUID>(resolvedBinding, "spriteId").ToString().Replace("-", string.Empty),
            "Projector Apply should write mesh and bones into the target sprite GUID.");
    }

    private static void ProjectorCandidatePreviewFilterRemovesOffscreenSprites()
    {
        var previewRect = new Rect(0f, 0f, 100f, 80f);

        AssertTrue(
            ReanimatorProjectorModule.IsCandidateViewRectVisible(previewRect, new Rect(20f, 20f, 16f, 16f)),
            "Projector filter should keep sprites fully inside the preview.");
        AssertTrue(
            ReanimatorProjectorModule.IsCandidateViewRectVisible(previewRect, new Rect(90f, 72f, 24f, 16f)),
            "Projector filter should keep sprites that are partially visible in the preview.");
        AssertFalse(
            ReanimatorProjectorModule.IsCandidateViewRectVisible(previewRect, new Rect(101f, 10f, 20f, 20f)),
            "Projector filter should remove sprites to the right of the preview.");
        AssertFalse(
            ReanimatorProjectorModule.IsCandidateViewRectVisible(previewRect, new Rect(10f, -25f, 20f, 20f)),
            "Projector filter should remove sprites above the preview.");
    }

    private static void ProjectorCandidateRowHoverDetectsPreviewTarget()
    {
        var row = new Rect(10f, 20f, 180f, 18f);
        AssertTrue(
            ReanimatorProjectorModule.IsCandidateRowHovered(row, new Event { type = EventType.MouseMove, mousePosition = new Vector2(20f, 25f) }),
            "Projector row hover should detect mouse over candidate row.");
        AssertTrue(
            ReanimatorProjectorModule.IsCandidateRowHovered(row, new Event { type = EventType.Repaint, mousePosition = new Vector2(20f, 25f) }),
            "Projector row hover should work during repaint so preview stays highlighted.");
        AssertFalse(
            ReanimatorProjectorModule.IsCandidateRowHovered(row, new Event { type = EventType.MouseMove, mousePosition = new Vector2(4f, 25f) }),
            "Projector row hover should ignore mouse outside candidate row.");
        AssertFalse(
            ReanimatorProjectorModule.IsCandidateRowHovered(row, new Event { type = EventType.Layout, mousePosition = new Vector2(20f, 25f) }),
            "Projector row hover should not update from layout events.");
        AssertFalse(
            ReanimatorProjectorModule.CanUpdateCandidateHoverFromEvent(new Event { type = EventType.Layout, mousePosition = new Vector2(20f, 25f) }),
            "Projector row hover should keep the last preview target during layout events.");
        AssertFalse(
            ReanimatorProjectorModule.IsCandidateRowHovered(new Rect(0f, 0f, 0f, 18f), new Event { type = EventType.MouseMove, mousePosition = Vector2.zero }),
            "Projector row hover should ignore invalid row rects.");
    }

    private static void ProjectorExplicitTargetFiltersIntersectingClothingSources()
    {
        AssertProjectorExplicitTargetScenario(
            "body",
            "briefs",
            "far_shirt",
            new[]
            {
                new Vector2(2f, 2f),
                new Vector2(8f, 2f),
                new Vector2(2f, 8f)
            },
            new[]
            {
                new Vector2(1f, 1f),
                new Vector2(9f, 1f),
                new Vector2(1f, 9f)
            },
            new[]
            {
                new Vector2(80f, 80f),
                new Vector2(90f, 80f),
                new Vector2(80f, 90f)
            });
        AssertProjectorExplicitTargetScenario(
            "chest",
            "clothes",
            "far_sleeve",
            new[]
            {
                new Vector2(22f, 22f),
                new Vector2(28f, 22f),
                new Vector2(22f, 28f)
            },
            new[]
            {
                new Vector2(20f, 20f),
                new Vector2(30f, 20f),
                new Vector2(20f, 30f)
            },
            new[]
            {
                new Vector2(75f, 10f),
                new Vector2(85f, 10f),
                new Vector2(75f, 20f)
            });
        AssertProjectorExplicitTargetScenario(
            "arm",
            "sleeve",
            "far_clothes",
            new[]
            {
                new Vector2(42f, 4f),
                new Vector2(50f, 4f),
                new Vector2(42f, 12f)
            },
            new[]
            {
                new Vector2(40f, 2f),
                new Vector2(52f, 2f),
                new Vector2(40f, 14f)
            },
            new[]
            {
                new Vector2(4f, 70f),
                new Vector2(14f, 70f),
                new Vector2(4f, 80f)
            });
    }

    private static void ProjectorRejectsDocumentSpaceDistantLayerGeometry()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = "far_source_bone",
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 10f,
                color = Color.magenta
            }
        });

        var targetLayer = BuildLayer("shirt-target", 903, Vector2.zero);
        var sourceLayer = BuildLayer("far-arm-source", 904, new Vector2(500f, 0f));
        targetLayer.sourceRect = new Rect(0f, 0f, 32f, 32f);
        sourceLayer.sourceRect = targetLayer.sourceRect;
        targetLayer.textureRect = targetLayer.sourceRect;
        sourceLayer.textureRect = sourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, sourceLayer });

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var sourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", sourceLayer);
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");

        var targetVertices = new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(targetLayer, new Vector2(0f, 0f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(targetLayer, new Vector2(24f, 0f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(targetLayer, new Vector2(0f, 24f))
        };
        var sourceVertices = new[]
        {
            PsdCoordinateSpace.LayerLocalToDocumentPoint(sourceLayer, new Vector2(0f, 0f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(sourceLayer, new Vector2(24f, 0f)),
            PsdCoordinateSpace.LayerLocalToDocumentPoint(sourceLayer, new Vector2(0f, 24f))
        };

        dataBySelectionKey[targetKey] = CreateProjectorContractLayerData(renderer, targetLayer, targetVertices, null, null);
        dataBySelectionKey[sourceKey] = CreateProjectorContractLayerData(
            renderer,
            sourceLayer,
            sourceVertices,
            new[] { 0 },
            new BoneWeight { boneIndex0 = 0, weight0 = 1f });

        var candidates = renderer.GetProjectorCandidates(targetKey, out var targetName, out var resolvedTargetKey, out var error);
        AssertEqual(null, error, "Distant layer candidate query should not fail.");
        AssertEqual("shirt-target", targetName, "Distant layer target name.");
        AssertEqual(targetKey, resolvedTargetKey, "Distant layer target key.");
        AssertEqual(0, candidates.Length, "Document-space distant source must not be listed as intersecting.");

        AssertFalse(
            renderer.ApplyProjectorWeights(targetKey, new[] { sourceKey }, true, out _, out _, out error),
            "Projector apply must reject document-space distant source geometry.");
        AssertEqual("Selected sources do not intersect target or have no weights.", error, "Distant source apply error.");
    }

    private static void AssertProjectorExplicitTargetScenario(
        string targetLayerName,
        string intersectingSourceName,
        string unrelatedSourceName,
        Vector2[] targetVertices,
        Vector2[] intersectingSourceVertices,
        Vector2[] unrelatedSourceVertices)
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 0,
                name = targetLayerName,
                parentId = -1,
                position = Vector2.zero,
                worldPosition = Vector2.zero,
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 10f,
                color = Color.white
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 1,
                name = intersectingSourceName,
                parentId = -1,
                position = new Vector2(2f, 0f),
                worldPosition = new Vector2(2f, 0f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 8f,
                color = Color.cyan
            },
            new PsdLayerPreviewRenderer.BoneInfo
            {
                index = 2,
                name = unrelatedSourceName,
                parentId = -1,
                position = new Vector2(80f, 80f),
                worldPosition = new Vector2(80f, 80f),
                rotation = Quaternion.identity,
                worldRotation = Quaternion.identity,
                length = 8f,
                color = Color.magenta
            }
        });

        var targetLayer = BuildLayer(targetLayerName, 900, Vector2.zero);
        var intersectingSourceLayer = BuildLayer(intersectingSourceName, 901, Vector2.zero);
        var unrelatedSourceLayer = BuildLayer(unrelatedSourceName, 902, Vector2.zero);
        targetLayer.sourceRect = new Rect(0f, 0f, 128f, 128f);
        intersectingSourceLayer.sourceRect = targetLayer.sourceRect;
        unrelatedSourceLayer.sourceRect = targetLayer.sourceRect;
        targetLayer.textureRect = targetLayer.sourceRect;
        intersectingSourceLayer.textureRect = intersectingSourceLayer.sourceRect;
        unrelatedSourceLayer.textureRect = unrelatedSourceLayer.sourceRect;
        SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { targetLayer, intersectingSourceLayer, unrelatedSourceLayer });

        var targetKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", targetLayer);
        var intersectingSourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", intersectingSourceLayer);
        var unrelatedSourceKey = (string)InvokePrivateMethod(renderer, "BuildSelectionKey", unrelatedSourceLayer);
        var dataBySelectionKey = (System.Collections.IDictionary)GetPrivateField<object>(renderer, "vertexDataByLayerSelectionKey");
        var targetData = CreateProjectorContractLayerData(renderer, targetLayer, targetVertices, null, null);
        var intersectingSourceData = CreateProjectorContractLayerData(renderer, intersectingSourceLayer, intersectingSourceVertices, new[] { 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        var unrelatedSourceData = CreateProjectorContractLayerData(renderer, unrelatedSourceLayer, unrelatedSourceVertices, new[] { 2 }, new BoneWeight { boneIndex0 = 0, weight0 = 1f });
        dataBySelectionKey[targetKey] = targetData;
        dataBySelectionKey[intersectingSourceKey] = intersectingSourceData;
        dataBySelectionKey[unrelatedSourceKey] = unrelatedSourceData;

        AssertTrue(renderer.SelectLayer(unrelatedSourceLayer), $"Fixture current editor selection should be unrelated to explicit target {targetLayerName}.");
        var candidates = renderer.GetProjectorCandidates(targetKey, out var resolvedTargetName, out var resolvedTargetKey, out var error);
        AssertEqual(null, error, $"Explicit target candidate query error for {targetLayerName}.");
        AssertEqual(targetLayerName, resolvedTargetName, $"Explicit target name for {targetLayerName}.");
        AssertEqual(targetKey, resolvedTargetKey, $"Explicit target key for {targetLayerName}.");
        AssertEqual(1, candidates.Length, $"Only physically intersecting source should be listed for {targetLayerName}.");
        AssertEqual(intersectingSourceKey, candidates[0].SelectionKey, $"{targetLayerName} target should list {intersectingSourceName}, not {unrelatedSourceName}.");

        AssertTrue(
            renderer.ApplyProjectorWeights(targetKey, new[] { unrelatedSourceKey, intersectingSourceKey }, true, out var weightedCount, out var appliedTargetName, out error),
            $"Projector apply should use explicit target and intersecting source only for {targetLayerName}. Error={error}");
        AssertEqual(targetLayerName, appliedTargetName, $"Apply should still target {targetLayerName} while current selection is {unrelatedSourceName}.");
        AssertEqual(targetVertices.Length, weightedCount, $"{intersectingSourceName} source should cover all {targetLayerName} fixture vertices.");
        AssertArray(new[] { 1 }, GetPrivateField<int[]>(targetData, "spriteBoneToRendererBone"), $"Target {targetLayerName} should receive only the intersecting {intersectingSourceName} bone.");
        var weights = GetPrivateField<BoneWeight[]>(targetData, "boneWeights");
        for (var i = 0; i < weights.Length; i++)
            AssertEqual(0, weights[i].boneIndex0, $"Projected {targetLayerName} vertex {i} should point at the explicit {intersectingSourceName} source bone slot.");
    }

    private static object CreateProjectorContractLayerData(
        PsdLayerPreviewRenderer renderer,
        PsdLayerPreviewRenderer.LayerInfo layer,
        Vector2[] vertices,
        int[] spriteBoneToRendererBone,
        BoneWeight? weight)
    {
        var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
        SetPrivateField(data, "layer", layer);
        SetPrivateField(data, "pixelsPerUnit", 1f);
        SetPrivateField(data, "pivotPixels", Vector2.zero);
        SetPrivateField(data, "vertices", vertices);
        SetPrivateField(data, "triangles", new[] { 0, 1, 2 });
        SetPrivateField(data, "allEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        SetPrivateField(data, "boundaryEdgePairs", new[] { 0, 1, 1, 2, 2, 0 });
        var vertexCount = vertices != null ? vertices.Length : 0;
        var weights = new BoneWeight[vertexCount];
        if (weight.HasValue)
        {
            for (var i = 0; i < weights.Length; i++)
                weights[i] = weight.Value;
        }
        SetPrivateField(data, "boneWeights", weights);
        SetPrivateField(data, "spriteBoneToRendererBone", spriteBoneToRendererBone ?? Array.Empty<int>());
        SetPrivateField(data, "bindBoneInverseMatrices", BuildIdentityMatrices(spriteBoneToRendererBone != null ? spriteBoneToRendererBone.Length : 0));
        return data;
    }

    private static Matrix4x4[] BuildIdentityMatrices(int count)
    {
        var matrices = new Matrix4x4[Mathf.Max(0, count)];
        for (var i = 0; i < matrices.Length; i++)
            matrices[i] = Matrix4x4.identity;
        return matrices;
    }

    private static bool IsProjectorBoundaryPoint(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> boundaryPairs,
        Vector2 point)
    {
        if (vertices == null || boundaryPairs == null)
            return false;
        for (var pairIndex = 0; pairIndex < boundaryPairs.Count; pairIndex++)
        {
            var vertexIndex = boundaryPairs[pairIndex];
            if (vertexIndex >= 0 && vertexIndex < vertices.Count &&
                (vertices[vertexIndex] - point).sqrMagnitude <= 0.0001f)
                return true;
        }
        return false;
    }

}
#endif
