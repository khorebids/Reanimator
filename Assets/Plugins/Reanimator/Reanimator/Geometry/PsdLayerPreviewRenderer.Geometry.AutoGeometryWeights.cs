#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private const string ForbiddenAutoWeightBoneName = "boobsroot";

    public bool ClearSelectedSpriteWeights(out string layerName, out int clearedCount, out string error)
    {
        layerName = string.Empty;
        clearedCount = 0;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite first";
            return false;
        }

        layerName = data.layer != null && !string.IsNullOrEmpty(data.layer.name) ? data.layer.name : "<unknown>";
        if (data.vertices == null || data.vertices.Length == 0)
        {
            error = "Selected sprite has no geometry";
            return false;
        }

        var nextWeights = new BoneWeight[data.vertices.Length];
        var changed = data.boneWeights == null || data.boneWeights.Length != nextWeights.Length;
        if (!changed)
        {
            for (var i = 0; i < data.boneWeights.Length; i++)
            {
                if (!ReanimatorProjectorAlgorithms.HasUsableWeight(data.boneWeights[i]))
                    continue;

                changed = true;
                break;
            }
        }

        if (!changed)
        {
            clearedCount = nextWeights.Length;
            return true;
        }

        RegisterUndo("Kill Sprite Weights");
        data.boneWeights = nextWeights;
        data.hasSkinning = false;
        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        clearedCount = nextWeights.Length;
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private int ClearAllSpriteWeightsForAutoSkeleton()
    {
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return 0;

        var clearedCount = 0;
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var selectionKey = pair.Key;
            var data = pair.Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
                continue;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var changed = data.boneWeights == null || data.boneWeights.Length != nextWeights.Length ||
                          (data.spriteBoneToRendererBone != null && data.spriteBoneToRendererBone.Length > 0) ||
                          (data.bindBoneInverseMatrices != null && data.bindBoneInverseMatrices.Length > 0);
            if (!changed)
            {
                for (var i = 0; i < data.boneWeights.Length; i++)
                {
                    if (!ReanimatorProjectorAlgorithms.HasUsableWeight(data.boneWeights[i]))
                        continue;

                    changed = true;
                    break;
                }
            }

            data.boneWeights = nextWeights;
            // Auto Skeleton replaces the complete renderer-bone list. Keeping a
            // sprite map or bind poses from the previous skeleton makes the new
            // bone indices point through obsolete inverse matrices. The error is
            // especially visible after nipple anchors move: the body can deform
            // out of view until a full preview-cache rebuild recreates bindings.
            // Drop every skeleton-dependent cache so Auto Weights binds each
            // sprite from scratch against the newly generated bones.
            data.spriteBoneToRendererBone = Array.Empty<int>();
            data.bindBoneInverseMatrices = Array.Empty<Matrix4x4>();
            data.usesSpriteBindPoses = false;
            data.hasSkinning = false;
            data.lastPoseVersion = -1;
            data.deformedVertices = null;
            data.skinMatrices = null;
            data.boneOrderTriangles = null;
            data.boneOrderSourceTriangles = null;
            data.boneOrderSourceWeights = null;
            data.lastBoneOrderVersion = -1;
            clearedCount += nextWeights.Length;

            if (changed)
            {
                var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
                MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            }
        }

        return clearedCount;
    }

    public bool ApplyAutoWeightsToSelectedLayer(out int weightedCount, out string layerName, out string error)
    {
        weightedCount = 0;
        layerName = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite first";
            return false;
        }

        layerName = data.layer != null && !string.IsNullOrEmpty(data.layer.name) ? data.layer.name : "<unknown>";
        if (data.vertices == null || data.vertices.Length == 0)
        {
            error = "Selected sprite has no geometry";
            return false;
        }

        if (IsNeckAutoWeightLayer(data))
        {
            if (bones == null || bones.Count == 0)
            {
                error = "Selected neck sprite needs spine and neck bones";
                return false;
            }

            var boneMap = BuildNeckAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length < 2)
            {
                error = "Selected neck sprite needs spine and neck bones";
                return false;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;
            if (!TryBuildNeckVerticalBoneWeights(data, out var neckWeights, out weightedCount) || weightedCount <= 0)
            {
                error = "Auto Weights found no neck height";
                return false;
            }

            var neckChanged = data.boneWeights == null || data.boneWeights.Length != neckWeights.Length;
            if (!neckChanged)
            {
                for (var i = 0; i < neckWeights.Length; i++)
                {
                    if (AreBoneWeightsApproximatelyEqual(data.boneWeights[i], neckWeights[i]))
                        continue;

                    neckChanged = true;
                    break;
                }
            }

            if (!neckChanged)
                return true;

            RegisterUndo("Auto Weights Neck");
            data.boneWeights = neckWeights;
            MarkSkinningWeightsChanged(data);
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            return true;
        }

        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0 || bones == null || bones.Count == 0)
        {
            error = "Selected sprite has no bones";
            return false;
        }

        var nextWeights = new BoneWeight[data.vertices.Length];
        for (var i = 0; i < data.vertices.Length; i++)
        {
            if (!TryBuildWeightFromBones(data.vertices[i], data, out nextWeights[i]))
                nextWeights[i] = default;
            else
                weightedCount++;
        }

        if (weightedCount <= 0)
        {
            error = "Auto Weights found no mapped bones";
            return false;
        }

        var changed = data.boneWeights == null || data.boneWeights.Length != nextWeights.Length;
        if (!changed)
        {
            for (var i = 0; i < nextWeights.Length; i++)
            {
                if (AreBoneWeightsApproximatelyEqual(data.boneWeights[i], nextWeights[i]))
                    continue;

                changed = true;
                break;
            }
        }

        if (!changed)
            return true;

        RegisterUndo("Auto Weights");
        data.boneWeights = nextWeights;
        EnsureLayerBindMatricesForWeights(data);
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    public string ApplyDeterministicAutoWeightsToAllSprites()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            var boneMap = BuildDeterministicAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            BoneWeight[] nextWeights;
            int layerWeightedVertices;
            if (IsNeckAutoWeightLayer(data))
            {
                if (!TryBuildNeckVerticalBoneWeights(data, out nextWeights, out layerWeightedVertices))
                {
                    skippedLayers++;
                    continue;
                }
            }
            else if (IsFrontHairAutoWeightLayer(data))
            {
                if (!TryBuildFrontHairVerticalBoneWeights(data, out nextWeights, out layerWeightedVertices))
                {
                    skippedLayers++;
                    continue;
                }
            }
            else if (IsBackHairAutoWeightLayer(data))
            {
                if (!TryBuildBackHairVerticalBoneWeights(data, out nextWeights, out layerWeightedVertices))
                {
                    skippedLayers++;
                    continue;
                }
            }
            else
            {
                nextWeights = new BoneWeight[data.vertices.Length];
                layerWeightedVertices = 0;
                for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
                {
                    if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                    {
                        nextWeights[vertexIndex] = weight;
                        if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                            layerWeightedVertices++;
                    }
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " layers";
    }

    public string ApplyLeftArmAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Left Arm");

        var weightedLayers = 0;
        var primaryWeightedLayers = 0;
        var secondaryWeightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonLeftArmLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsLeftArmAutoWeightLayer(data))
            {
                nonLeftArmLayers++;
                continue;
            }

            var boneMap = BuildLeftArmAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var layerWeightedVertices = 0;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                {
                    nextWeights[vertexIndex] = weight;
                    if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                        layerWeightedVertices++;
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            if (IsSecondaryArmAutoWeightLayer(data, "l"))
                secondaryWeightedLayers++;
            else
                primaryWeightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        var primaryLimbResults = ApplyArmLimbsAfterAutoWeights(
            LeftArmAutoLimbJointSpecs,
            primaryWeightedLayers,
            "left primary arm not present");
        var secondaryLimbResults = ApplyArmLimbsAfterAutoWeights(
            LeftSecondaryArmAutoLimbJointSpecs,
            secondaryWeightedLayers,
            "left second arm not present");
        EnsureAutoArmLimbsApplied("left", primaryWeightedLayers, primaryLimbResults[0], primaryLimbResults[1]);
        EnsureAutoArmLimbsApplied("left second", secondaryWeightedLayers, secondaryLimbResults[0], secondaryLimbResults[1]);
        return "left arm midpoint-nearest isolated-pair weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " left-arm candidates, ignored " + nonLeftArmLayers + " other layers; " +
               primaryLimbResults[0] + "; " + primaryLimbResults[1] + "; " +
               secondaryLimbResults[0] + "; " + secondaryLimbResults[1];
    }

    public string ApplyBothArmAutoWeights()
    {
        return ApplyLeftArmAutoWeights() + "; " + ApplyRightArmAutoWeights();
    }

    public string ApplyRiggedLimbAutoWeights()
    {
        var result = ApplyHeadAutoWeights() + "; " +
                     ApplyNeckAutoWeights() + "; " +
                     ApplyFrontHairAutoWeights() + "; " +
                     ApplyBackHairAutoWeights() + "; " +
                     ApplyBodyAutoWeights() + "; " +
                     ApplyLeftArmAutoWeights() + "; " +
                     ApplyRightArmAutoWeights() + "; " +
                     ApplyLeftLegAutoWeights() + "; " +
                     ApplyRightLegAutoWeights();

        result += "; " +
                  ApplyLegHipJointV2AfterAutoWeights(LeftHipAutoJointSpec) + "; " +
                  ApplyLegHipJointV2AfterAutoWeights(RightHipAutoJointSpec);

        var forbiddenBindings = CountForbiddenAutoWeightBoneBindings();
        if (forbiddenBindings > 0)
        {
            throw new InvalidOperationException(
                "Auto Weights invariant failed: boobs_root is mapped on " +
                forbiddenBindings + " sprite layer(s).");
        }

        var crossPairBindings = CountCrossArmPairBoneBindings();
        if (crossPairBindings > 0)
        {
            throw new InvalidOperationException(
                "Auto Weights invariant failed: arm bones from one pair are mapped on " +
                crossPairBindings + " sprite layer(s) belonging to the other pair.");
        }

        return result + "; boobs_root excluded from Auto Weights; arm-pair weights isolated";
    }

    public string ApplyHeadAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Head");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonHeadLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!TryGetExactHeadAutoWeightBoneName(data, out var boneName))
            {
                nonHeadLayers++;
                continue;
            }

            var boneMap = BuildSingleNamedAutoWeightBoneMap(boneName);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var singleWeight = BuildSingleBoneWeight(0);
            for (var vertexIndex = 0; vertexIndex < nextWeights.Length; vertexIndex++)
                nextWeights[vertexIndex] = singleWeight;

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += nextWeights.Length;
        }

        return "head exact weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " head candidates, ignored " + nonHeadLayers + " other layers";
    }

    public string ApplyNeckAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Neck");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonNeckLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsNeckAutoWeightLayer(data))
            {
                nonNeckLayers++;
                continue;
            }

            var boneMap = BuildNeckAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length < 2)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            if (!TryBuildNeckVerticalBoneWeights(data, out var nextWeights, out var layerWeightedVertices))
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "neck vertical spine-neck weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " neck candidates, ignored " + nonNeckLayers + " other layers";
    }

    public string ApplyFrontHairAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Front Hair");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonFrontHairLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsFrontHairAutoWeightLayer(data))
            {
                nonFrontHairLayers++;
                continue;
            }

            var boneMap = BuildFrontHairAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length < 2)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            if (!TryBuildFrontHairVerticalBoneWeights(data, out var nextWeights, out var layerWeightedVertices))
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "front hair vertical head-front_hair weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " front-hair candidates, ignored " + nonFrontHairLayers + " other layers";
    }

    public string ApplyBackHairAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Back Hair");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonBackHairLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsBackHairAutoWeightLayer(data))
            {
                nonBackHairLayers++;
                continue;
            }

            var boneMap = BuildBackHairAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length < 2)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            if (!TryBuildBackHairVerticalBoneWeights(data, out var nextWeights, out var layerWeightedVertices))
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "back hair vertical head-back_hair weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " back-hair candidates, ignored " + nonBackHairLayers + " other layers";
    }

    public string ApplyBodyAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Body");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonBodyLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsBodyAutoWeightLayer(data))
            {
                nonBodyLayers++;
                continue;
            }

            var boneMap = BuildBodyAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var layerWeightedVertices = 0;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                {
                    nextWeights[vertexIndex] = weight;
                    if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                        layerWeightedVertices++;
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "body midpoint-nearest whitelist weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " body candidates, ignored " + nonBodyLayers + " other layers";
    }

    public string ApplyRightArmAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Right Arm");

        var weightedLayers = 0;
        var primaryWeightedLayers = 0;
        var secondaryWeightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonRightArmLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsRightArmAutoWeightLayer(data))
            {
                nonRightArmLayers++;
                continue;
            }

            var boneMap = BuildRightArmAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var layerWeightedVertices = 0;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                {
                    nextWeights[vertexIndex] = weight;
                    if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                        layerWeightedVertices++;
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            if (IsSecondaryArmAutoWeightLayer(data, "r"))
                secondaryWeightedLayers++;
            else
                primaryWeightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        var primaryLimbResults = ApplyArmLimbsAfterAutoWeights(
            RightArmAutoLimbJointSpecs,
            primaryWeightedLayers,
            "right primary arm not present");
        var secondaryLimbResults = ApplyArmLimbsAfterAutoWeights(
            RightSecondaryArmAutoLimbJointSpecs,
            secondaryWeightedLayers,
            "right second arm not present");
        EnsureAutoArmLimbsApplied("right", primaryWeightedLayers, primaryLimbResults[0], primaryLimbResults[1]);
        EnsureAutoArmLimbsApplied("right second", secondaryWeightedLayers, secondaryLimbResults[0], secondaryLimbResults[1]);
        return "right arm midpoint-nearest isolated-pair weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " right-arm candidates, ignored " + nonRightArmLayers + " other layers; " +
               primaryLimbResults[0] + "; " + primaryLimbResults[1] + "; " +
               secondaryLimbResults[0] + "; " + secondaryLimbResults[1];
    }

    private string[] ApplyArmLimbsAfterAutoWeights(
        AutoLimbJointSpec[] specs,
        int weightedLayers,
        string absentStatus)
    {
        if (weightedLayers > 0)
            return ApplyPreparedAutoLimbSequenceAfterAutoWeights(specs);

        return new[] { absentStatus, absentStatus };
    }

    private static void EnsureAutoArmLimbsApplied(
        string side,
        int weightedLayers,
        string elbowResult,
        string wristResult)
    {
        if (weightedLayers <= 0)
            return;

        var elbowApplied = !string.IsNullOrEmpty(elbowResult) &&
                           elbowResult.IndexOf(side + " elbow limb applied", StringComparison.OrdinalIgnoreCase) >= 0;
        var wristApplied = !string.IsNullOrEmpty(wristResult) &&
                           wristResult.IndexOf(side + " wrist limb applied", StringComparison.OrdinalIgnoreCase) >= 0;
        if (elbowApplied && wristApplied)
            return;

        throw new InvalidOperationException(
            side + " arm Limbs invariant failed: " + elbowResult + "; " + wristResult);
    }

    public string ApplyLeftLegAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Left Leg");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonLeftLegLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsLeftLegAutoWeightLayer(data))
            {
                nonLeftLegLayers++;
                continue;
            }

            var boneMap = BuildLeftLegAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var layerWeightedVertices = 0;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                {
                    nextWeights[vertexIndex] = weight;
                    if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                        layerWeightedVertices++;
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "left leg midpoint-nearest whitelist weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " left-leg candidates, ignored " + nonLeftLegLayers + " other layers; " + ApplyLeftLegKneeAutoLimbAfterAutoWeights() + "; " + ApplyLeftLegAnkleAutoLimbAfterAutoWeights();
    }

    public string ApplyRightLegAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            throw new InvalidOperationException("Auto Weights needs bones.");
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            throw new InvalidOperationException("Auto Weights needs sprite geometry.");

        RegisterUndo("Auto Weights Right Leg");

        var weightedLayers = 0;
        var weightedVertices = 0;
        var skippedLayers = 0;
        var nonRightLegLayers = 0;
        var layers = new List<KeyValuePair<string, LayerVertexData>>(vertexDataByLayerSelectionKey);
        for (var i = 0; i < layers.Count; i++)
        {
            var selectionKey = layers[i].Key;
            var data = layers[i].Value;
            if (data == null || data.vertices == null || data.vertices.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            if (!IsRightLegAutoWeightLayer(data))
            {
                nonRightLegLayers++;
                continue;
            }

            var boneMap = BuildRightLegAutoWeightBoneMap(data);
            if (boneMap == null || boneMap.Length == 0)
            {
                skippedLayers++;
                continue;
            }

            data.spriteBoneToRendererBone = boneMap;
            data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(boneMap);
            data.usesSpriteBindPoses = false;

            var nextWeights = new BoneWeight[data.vertices.Length];
            var layerWeightedVertices = 0;
            for (var vertexIndex = 0; vertexIndex < data.vertices.Length; vertexIndex++)
            {
                if (TryBuildMidpointNearestBoneWeight(data.vertices[vertexIndex], data, out var weight))
                {
                    nextWeights[vertexIndex] = weight;
                    if (ReanimatorProjectorAlgorithms.HasUsableWeight(weight))
                        layerWeightedVertices++;
                }
            }

            if (layerWeightedVertices <= 0)
            {
                skippedLayers++;
                continue;
            }

            data.boneWeights = nextWeights;
            MarkSkinningWeightsChanged(data);
            var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
            MarkEditGeometryDataChanged(data, selectionKey, stableKey);
            weightedLayers++;
            weightedVertices += layerWeightedVertices;
        }

        return "right leg midpoint-nearest whitelist weighted " + weightedVertices + " vertices on " + weightedLayers + " layers, skipped " + skippedLayers + " right-leg candidates, ignored " + nonRightLegLayers + " other layers; " + ApplyRightLegKneeAutoLimbAfterAutoWeights() + "; " + ApplyRightLegAnkleAutoLimbAfterAutoWeights();
    }

    private bool TryBuildMidpointNearestBoneWeight(Vector2 pointDoc, LayerVertexData sourceData, out BoneWeight weight)
    {
        weight = default;
        if (sourceData == null || sourceData.spriteBoneToRendererBone == null || sourceData.spriteBoneToRendererBone.Length == 0 || bones == null || bones.Count == 0)
            return false;

        var bestSpriteBoneIndex = -1;
        var bestDistanceSqr = float.MaxValue;
        for (var spriteBoneIndex = 0; spriteBoneIndex < sourceData.spriteBoneToRendererBone.Length; spriteBoneIndex++)
        {
            var rendererBoneIndex = sourceData.spriteBoneToRendererBone[spriteBoneIndex];
            if (!CanUseAutoWeightRendererBone(rendererBoneIndex))
                continue;

            var midpoint = (bones[rendererBoneIndex].worldPosition + GetBoneEnd(rendererBoneIndex)) * 0.5f;
            var distanceSqr = (pointDoc - midpoint).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestSpriteBoneIndex = spriteBoneIndex;
            }
        }

        if (bestSpriteBoneIndex < 0)
            return false;

        weight = new BoneWeight
        {
            boneIndex0 = bestSpriteBoneIndex,
            boneIndex1 = bestSpriteBoneIndex,
            boneIndex2 = bestSpriteBoneIndex,
            boneIndex3 = bestSpriteBoneIndex,
            weight0 = 1f,
            weight1 = 0f,
            weight2 = 0f,
            weight3 = 0f
        };
        return true;
    }

    public string ApplyDefaultAutoLimbJointsAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "limbs skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "limbs skipped: no geometry";

        var applied = 0;
        var skipped = 0;
        var errors = 0;
        var specs = DefaultAutoLimbJointSpecs;
        for (var i = 0; i < specs.Length; i++)
        {
            if (TryApplyDefaultAutoLimbJoint(specs[i], out var error))
            {
                applied++;
            }
            else
            {
                if (!string.IsNullOrEmpty(error) &&
                    !error.EndsWith("missing bone pair", StringComparison.Ordinal) &&
                    !error.EndsWith("no target layer", StringComparison.Ordinal))
                {
                    errors++;
                }
                skipped++;
            }
        }

        return "limbs applied " + applied + "/" + specs.Length + ", skipped " + skipped + ", errors " + errors;
    }

    private string ApplyLeftArmAutoLimbJointsAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left arm limbs skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left arm limbs skipped: no geometry";

        var applied = 0;
        var skipped = 0;
        var errors = 0;
        var details = new List<string>();
        var specs = LeftArmAutoLimbJointSpecs;
        for (var i = 0; i < specs.Length; i++)
        {
            if (TryApplyDefaultAutoLimbJoint(specs[i], out var error))
            {
                applied++;
                continue;
            }

            skipped++;
            if (!string.IsNullOrEmpty(error) &&
                !error.EndsWith("missing bone pair", StringComparison.Ordinal) &&
                !error.EndsWith("no target layer", StringComparison.Ordinal))
            {
                errors++;
            }

            if (!string.IsNullOrEmpty(error) && details.Count < 2)
                details.Add(error);
        }

        var result = "left arm limbs applied " + applied + "/" + specs.Length + ", skipped " + skipped + ", errors " + errors;
        if (details.Count > 0)
            result += " (" + string.Join("; ", details) + ")";
        return result;
    }

    private string ApplyLeftArmElbowAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left elbow limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left elbow limb skipped: no geometry";

        var spec = LeftArmAutoLimbJointSpecs[0];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return string.IsNullOrEmpty(error) ? "left elbow limb applied" : "left elbow limb applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "left elbow limb skipped"
            : "left elbow limb skipped: " + error;
    }

    private string ApplyLeftArmWristAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left wrist limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left wrist limb skipped: no geometry";

        var spec = LeftArmAutoLimbJointSpecs[1];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return string.IsNullOrEmpty(error) ? "left wrist limb applied" : "left wrist limb applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "left wrist limb skipped"
            : "left wrist limb skipped: " + error;
    }

    private string ApplyLeftArmBodyJointAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left shoulder joint skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left shoulder joint skipped: no geometry";

        if (TryApplyAutoJoint(LeftShoulderAutoJointSpec, out var error))
            return string.IsNullOrEmpty(error)
                ? "left shoulder joint applied"
                : "left shoulder joint applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "left shoulder joint skipped"
            : "left shoulder joint skipped: " + error;
    }

    private string ApplyRightArmElbowAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "right elbow limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "right elbow limb skipped: no geometry";

        var spec = RightArmAutoLimbJointSpecs[0];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return string.IsNullOrEmpty(error) ? "right elbow limb applied" : "right elbow limb applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "right elbow limb skipped"
            : "right elbow limb skipped: " + error;
    }

    private string ApplyRightArmWristAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "right wrist limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "right wrist limb skipped: no geometry";

        var spec = RightArmAutoLimbJointSpecs[1];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return string.IsNullOrEmpty(error) ? "right wrist limb applied" : "right wrist limb applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "right wrist limb skipped"
            : "right wrist limb skipped: " + error;
    }

    private string ApplyRightArmBodyJointAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "right shoulder joint skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "right shoulder joint skipped: no geometry";

        if (TryApplyAutoJoint(RightShoulderAutoJointSpec, out var error))
            return string.IsNullOrEmpty(error)
                ? "right shoulder joint applied"
                : "right shoulder joint applied: " + error;

        return string.IsNullOrEmpty(error)
            ? "right shoulder joint skipped"
            : "right shoulder joint skipped: " + error;
    }

    private string ApplyLeftLegKneeAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left knee limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left knee limb skipped: no geometry";

        var spec = LeftLegAutoLimbJointSpecs[0];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return "left knee limb applied";

        return string.IsNullOrEmpty(error)
            ? "left knee limb skipped"
            : "left knee limb skipped: " + error;
    }

    private string ApplyLeftLegAnkleAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "left ankle limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "left ankle limb skipped: no geometry";

        var spec = LeftLegAutoLimbJointSpecs[1];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return "left ankle limb applied";

        return string.IsNullOrEmpty(error)
            ? "left ankle limb skipped"
            : "left ankle limb skipped: " + error;
    }

    private string ApplyLegHipJointV2AfterAutoWeights(AutoJointSpec spec)
    {
        if (bones == null || bones.Count == 0)
            return spec.Label + " Joint V2 skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return spec.Label + " Joint V2 skipped: no geometry";

        if (!TryFindRendererBoneByNames(spec.ChildBoneNames, out var childBoneIndex))
            return spec.Label + " Joint V2 skipped: missing child bone";
        if (!TryFindAutoJointTargetLayer(
                spec,
                childBoneIndex,
                out var selectionKey,
                out _,
                out _))
        {
            return spec.Label + " Joint V2 skipped: no target layer";
        }

        var oldSelectionKey = selectedLayerSelectionKey;
        var oldSelectedBoneIndex = selectedBoneIndex;
        try
        {
            selectedLayerSelectionKey = selectionKey;
            selectedBoneIndex = childBoneIndex;
            if (ApplyJointV2(
                    ReanimatorJointV2Defaults.CreateWeightCurve(),
                    ReanimatorJointV2Defaults.CreateSpecialWeightCurve(),
                    ReanimatorJointV2Defaults.SpecialJointDistance,
                    ReanimatorJointV2Defaults.BoneBaseOffset,
                    out var error))
            {
                return spec.Label + " Joint V2 applied";
            }

            return string.IsNullOrEmpty(error)
                ? spec.Label + " Joint V2 skipped"
                : spec.Label + " Joint V2 skipped: " + error;
        }
        finally
        {
            selectedLayerSelectionKey = oldSelectionKey;
            selectedBoneIndex = oldSelectedBoneIndex;
        }
    }

    private string ApplyRightLegKneeAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "right knee limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "right knee limb skipped: no geometry";

        var spec = RightLegAutoLimbJointSpecs[0];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return "right knee limb applied";

        return string.IsNullOrEmpty(error)
            ? "right knee limb skipped"
            : "right knee limb skipped: " + error;
    }

    private string ApplyRightLegAnkleAutoLimbAfterAutoWeights()
    {
        if (bones == null || bones.Count == 0)
            return "right ankle limb skipped: no bones";
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return "right ankle limb skipped: no geometry";

        var spec = RightLegAutoLimbJointSpecs[1];
        if (TryApplyDefaultAutoLimbJoint(spec, out var error))
            return "right ankle limb applied";

        return string.IsNullOrEmpty(error)
            ? "right ankle limb skipped"
            : "right ankle limb skipped: " + error;
    }

    private static readonly AutoLimbJointSpec[] DefaultAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "left elbow",
            new[] { "l_arm" },
            new[] { "l_elbow", "l_arm" },
            new[] { "larm", "leftarm", "lelbow" }),
        new AutoLimbJointSpec(
            "right elbow",
            new[] { "r_arm" },
            new[] { "r_elbow", "r_arm" },
            new[] { "rarm", "rightarm", "relbow" }),
        new AutoLimbJointSpec(
            "left knee",
            new[] { "l_leg" },
            new[] { "l_knee", "l_leg" },
            new[] { "lleg", "leftleg", "lknee", "lthigh" }),
        new AutoLimbJointSpec(
            "right knee",
            new[] { "r_leg" },
            new[] { "r_knee", "r_leg" },
            new[] { "rleg", "rightleg", "rknee", "rthigh" })
    };

    private static readonly AutoLimbJointSpec[] LeftArmAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "left elbow",
            new[] { "l_arm" },
            new[] { "l_elbow", "l_arm" },
            new[] { "larm", "leftarm", "lelbow", "elbow" },
            2,
            new[] { "larm", "leftarm", "arml" }),
        new AutoLimbJointSpec(
            "left wrist",
            new[] { "l_elbow", "l_arm" },
            new[] { "l_hand" },
            new[] { "larm", "leftarm", "lhand", "lefthand", "lwrist", "wrist" },
            1,
            new[] { "larm", "leftarm", "arml" })
    };

    private static readonly AutoLimbJointSpec[] RightArmAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "right elbow",
            new[] { "r_arm" },
            new[] { "r_elbow", "r_arm" },
            new[] { "rarm", "rightarm", "relbow", "elbow" },
            2,
            new[] { "rarm", "rightarm", "armr" }),
        new AutoLimbJointSpec(
            "right wrist",
            new[] { "r_elbow", "r_arm" },
            new[] { "r_hand" },
            new[] { "rarm", "rightarm", "rhand", "righthand", "rwrist", "wrist" },
            1,
            new[] { "rarm", "rightarm", "armr" })
    };

    private static readonly AutoLimbJointSpec[] LeftSecondaryArmAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "left second elbow",
            new[] { "l_arm_2" },
            new[] { "l_arm_3" },
            new[] { "larm2", "leftarm2", "arm2l" },
            2,
            new[] { "larm2", "leftarm2", "arm2l" }),
        new AutoLimbJointSpec(
            "left second wrist",
            new[] { "l_arm_3" },
            new[] { "l_hand_2" },
            new[] { "larm2", "leftarm2", "arm2l" },
            1,
            new[] { "larm2", "leftarm2", "arm2l" })
    };

    private static readonly AutoLimbJointSpec[] RightSecondaryArmAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "right second elbow",
            new[] { "r_arm_2" },
            new[] { "r_arm_3" },
            new[] { "rarm2", "rightarm2", "arm2r" },
            2,
            new[] { "rarm2", "rightarm2", "arm2r" }),
        new AutoLimbJointSpec(
            "right second wrist",
            new[] { "r_arm_3" },
            new[] { "r_hand_2" },
            new[] { "rarm2", "rightarm2", "arm2r" },
            1,
            new[] { "rarm2", "rightarm2", "arm2r" })
    };

    private static readonly AutoLimbJointSpec[] LeftLegAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "left knee",
            new[] { "l_leg" },
            new[] { "l_knee", "l_leg" },
            new[] { "lleg", "leftleg", "lknee", "knee" },
            2,
            new[] { "lleg", "leftleg", "legl" }),
        new AutoLimbJointSpec(
            "left ankle",
            new[] { "l_knee", "l_leg" },
            new[] { "l_foot" },
            new[] { "lleg", "leftleg", "lfoot", "leftfoot", "lankle", "ankle" },
            2,
            new[] { "lleg", "leftleg", "legl" })
    };

    private static readonly AutoLimbJointSpec[] RightLegAutoLimbJointSpecs =
    {
        new AutoLimbJointSpec(
            "right knee",
            new[] { "r_leg" },
            new[] { "r_knee", "r_leg" },
            new[] { "rleg", "rightleg", "rknee", "knee" },
            2,
            new[] { "rleg", "rightleg", "legr" }),
        new AutoLimbJointSpec(
            "right ankle",
            new[] { "r_knee", "r_leg" },
            new[] { "r_foot" },
            new[] { "rleg", "rightleg", "rfoot", "rightfoot", "rankle", "ankle" },
            2,
            new[] { "rleg", "rightleg", "legr" })
    };

    private static readonly AutoJointSpec LeftShoulderAutoJointSpec = new AutoJointSpec(
        "left shoulder",
        new[] { "l_arm" },
        new[] { "larm", "leftarm", "lshoulder", "shoulder" },
        new[] { "larm", "leftarm", "arml" },
        new[] { "neck" });

    private static readonly AutoJointSpec RightShoulderAutoJointSpec = new AutoJointSpec(
        "right shoulder",
        new[] { "r_arm" },
        new[] { "rarm", "rightarm", "rshoulder", "shoulder" },
        new[] { "rarm", "rightarm", "armr" },
        new[] { "neck" });

    private static readonly AutoJointSpec LeftHipAutoJointSpec = new AutoJointSpec(
        "left hip",
        new[] { "l_leg" },
        new[] { "lleg", "leftleg", "lhip", "hip" },
        new[] { "lleg", "leftleg", "legl" },
        new[] { "hips" });

    private static readonly AutoJointSpec RightHipAutoJointSpec = new AutoJointSpec(
        "right hip",
        new[] { "r_leg" },
        new[] { "rleg", "rightleg", "rhip", "hip" },
        new[] { "rleg", "rightleg", "legr" },
        new[] { "hips" });

    private readonly struct AutoLimbJointSpec
    {
        public readonly string Label;
        public readonly string[] ParentBoneNames;
        public readonly string[] ChildBoneNames;
        public readonly string[] LayerNameTerms;
        public readonly int ContourSteps;
        public readonly string[] PrimaryLayerNames;

        public AutoLimbJointSpec(string label, string[] parentBoneNames, string[] childBoneNames, string[] layerNameTerms, int contourSteps = 2, string[] primaryLayerNames = null)
        {
            Label = label ?? string.Empty;
            ParentBoneNames = parentBoneNames ?? Array.Empty<string>();
            ChildBoneNames = childBoneNames ?? Array.Empty<string>();
            LayerNameTerms = layerNameTerms ?? Array.Empty<string>();
            ContourSteps = Mathf.Max(1, contourSteps);
            PrimaryLayerNames = primaryLayerNames ?? Array.Empty<string>();
        }
    }

    private readonly struct PreparedAutoLimbJoint
    {
        public readonly AutoLimbJointSpec Spec;
        public readonly int ParentBoneIndex;
        public readonly int ChildBoneIndex;
        public readonly string SelectionKey;
        public readonly LimbLine Line;
        public readonly float CullRadius;

        public PreparedAutoLimbJoint(
            AutoLimbJointSpec spec,
            int parentBoneIndex,
            int childBoneIndex,
            string selectionKey,
            LimbLine line,
            float cullRadius)
        {
            Spec = spec;
            ParentBoneIndex = parentBoneIndex;
            ChildBoneIndex = childBoneIndex;
            SelectionKey = selectionKey ?? string.Empty;
            Line = line;
            CullRadius = cullRadius;
        }
    }

    private readonly struct AutoJointSpec
    {
        public readonly string Label;
        public readonly string[] ChildBoneNames;
        public readonly string[] LayerNameTerms;
        public readonly string[] PrimaryLayerNames;
        public readonly string[] DirectionBoneNames;

        public AutoJointSpec(string label, string[] childBoneNames, string[] layerNameTerms, string[] primaryLayerNames, string[] directionBoneNames = null)
        {
            Label = label ?? string.Empty;
            ChildBoneNames = childBoneNames ?? Array.Empty<string>();
            LayerNameTerms = layerNameTerms ?? Array.Empty<string>();
            PrimaryLayerNames = primaryLayerNames ?? Array.Empty<string>();
            DirectionBoneNames = directionBoneNames ?? Array.Empty<string>();
        }
    }

    private bool TryApplyAutoJoint(AutoJointSpec spec, out string error)
    {
        error = null;
        if (!TryFindRendererBoneByNames(spec.ChildBoneNames, out var childBoneIndex))
        {
            error = spec.Label + ": missing child bone";
            return false;
        }

        var parentBoneIndex = bones[childBoneIndex] != null ? bones[childBoneIndex].parentId : -1;
        if (parentBoneIndex < 0 || parentBoneIndex >= bones.Count || bones[parentBoneIndex] == null)
        {
            error = spec.Label + ": child bone has no parent";
            return false;
        }

        if (!TryFindAutoJointTargetLayer(
                spec,
                childBoneIndex,
                out var selectionKey,
                out var stableKey,
                out var data))
        {
            error = spec.Label + ": no target layer";
            return false;
        }

        var oldSelectionKey = selectedLayerSelectionKey;
        var oldStableKey = radiusCullLayerStableKey;
        var oldHasPoint = radiusCullHasPoint;
        var oldCenter = radiusCullCenterDoc;
        var oldNearestBone = jointNearestBoneIndex;
        var oldStartAngle = jointStartAngleRad;
        var oldEndAngle = jointEndAngleRad;
        try
        {
            var boneBaseCenter = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[childBoneIndex]));
            var center = boneBaseCenter;
            var radius = 0f;
            selectedLayerSelectionKey = selectionKey;
            radiusCullLayerStableKey = stableKey;
            radiusCullHasPoint = true;
            jointNearestBoneIndex = childBoneIndex;

            if (!TryResolveAutoJointStrictBlueIntersections(
                    spec,
                    data,
                    boneBaseCenter,
                    parentBoneIndex,
                    childBoneIndex,
                    out center,
                    out radius,
                    out var startAngle,
                    out var endAngle))
            {
                var parentDirection = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[parentBoneIndex])) - center;
                if (parentDirection.sqrMagnitude <= 1e-8f)
                    parentDirection = MapAutoJointDrawPointToBase(data, GetBoneEnd(parentBoneIndex)) - center;
                var centerAngle = parentDirection.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(parentDirection.y, parentDirection.x)
                    : 0f;
                if (TryResolveAutoJointDirectionBoneBase(data, center, spec.DirectionBoneNames, out var specDirection))
                    centerAngle = Mathf.Atan2(specDirection.y, specDirection.x);
                SetJointAnglesAround(centerAngle, 30f * Mathf.Deg2Rad);
                radius = CalculateAutoJointRadius(data, center, childBoneIndex, parentBoneIndex);
            }
            else
            {
                jointStartAngleRad = startAngle;
                jointEndAngleRad = endAngle;
                radius = CalculateAutoJointRadius(data, center, childBoneIndex, parentBoneIndex, radius);
            }

            radiusCullCenterDoc = center;
            if (ApplyJointStitch(
                    radius,
                    true,
                    false,
                    true,
                    8f,
                    4,
                    JointStartLongPointsOffset * 2f,
                    JointEndLongPointsOffset * 2f,
                    out var applyError))
            {
                return true;
            }

            error = spec.Label + ": " + applyError;
            return false;
        }
        finally
        {
            selectedLayerSelectionKey = oldSelectionKey;
            radiusCullLayerStableKey = oldStableKey;
            radiusCullHasPoint = oldHasPoint;
            radiusCullCenterDoc = oldCenter;
            jointNearestBoneIndex = oldNearestBone;
            jointStartAngleRad = oldStartAngle;
            jointEndAngleRad = oldEndAngle;
        }
    }

    private bool TryApplyAutoJointGeometryDirect(
        LayerVertexData sourceData,
        string selectionKey,
        string stableKey,
        Vector2 center,
        float radius,
        int childBoneIndex,
        out string error)
    {
        error = null;
        if (sourceData == null)
        {
            error = "missing target data";
            return false;
        }

        var workingData = CloneProjectorLayerData(sourceData);
        if (workingData == null)
        {
            error = "failed to clone target data";
            return false;
        }

        var beforeVertices = sourceData.vertices != null ? sourceData.vertices.Length : 0;
        var beforeTriangles = sourceData.triangles != null ? sourceData.triangles.Length / 3 : 0;
        var beforeEdges = sourceData.allEdgePairs != null ? sourceData.allEdgePairs.Length / 2 : 0;
        var oldNearestBone = jointNearestBoneIndex;
        try
        {
            jointNearestBoneIndex = childBoneIndex;
            if (!TryResolveJointInfluenceSpriteBones(workingData, center, out var parentSpriteBone, out var nearestSpriteBone, true))
            {
                error = "failed to resolve joint influence bones";
                return false;
            }

            var geometryApplied = TryApplyJointExtendedGeometry(
                    workingData,
                    center,
                    radius,
                    parentSpriteBone,
                    nearestSpriteBone,
                    8f,
                    4,
                    JointStartLongPointsOffset,
                    JointEndLongPointsOffset,
                    out var affectedContourVertices,
                    out var insertedVertices,
                    true);
            if (TryApplyAutoShoulderSeamJointGeometry(
                    workingData,
                    center,
                    radius,
                    childBoneIndex,
                    parentSpriteBone,
                    nearestSpriteBone,
                    out var seamAffectedVertices,
                    out var seamInsertedVertices))
            {
                affectedContourVertices += seamAffectedVertices;
                insertedVertices += seamInsertedVertices;
                geometryApplied = true;
            }

            if (!geometryApplied)
            {
                error = "no contour vertices in joint sector";
                return false;
            }

            if (affectedContourVertices <= 0 && insertedVertices <= 0)
            {
                error = "no contour vertices in joint sector";
                return false;
            }

            RegisterUndo("Auto Joint Left Shoulder");
            sourceData.vertices = workingData.vertices;
            sourceData.vertexUvs = workingData.vertexUvs;
            sourceData.triangles = workingData.triangles;
            sourceData.allEdgePairs = workingData.allEdgePairs;
            sourceData.boundaryEdgePairs = workingData.boundaryEdgePairs;
            sourceData.boneWeights = workingData.boneWeights;
            sourceData.spriteBoneToRendererBone = workingData.spriteBoneToRendererBone;
            sourceData.bindBoneInverseMatrices = workingData.bindBoneInverseMatrices;
            sourceData.usesSpriteBindPoses = workingData.usesSpriteBindPoses;
            sourceData.hasSkinning = workingData.spriteBoneToRendererBone != null &&
                                    workingData.bindBoneInverseMatrices != null &&
                                    workingData.boneWeights != null &&
                                    workingData.vertices != null &&
                                    workingData.boneWeights.Length == workingData.vertices.Length &&
                                    workingData.bindBoneInverseMatrices.Length == workingData.spriteBoneToRendererBone.Length &&
                                    workingData.bindBoneInverseMatrices.Length > 0;
            sourceData.lastPoseVersion = -1;
            sourceData.deformedVertices = null;
            sourceData.skinMatrices = null;
            MarkEditGeometryDataChanged(sourceData, selectionKey, stableKey);
            var afterVertices = sourceData.vertices != null ? sourceData.vertices.Length : 0;
            var afterTriangles = sourceData.triangles != null ? sourceData.triangles.Length / 3 : 0;
            var afterEdges = sourceData.allEdgePairs != null ? sourceData.allEdgePairs.Length / 2 : 0;
            if (afterVertices == beforeVertices && afterTriangles == beforeTriangles && afterEdges == beforeEdges)
            {
                error = GetLayerDisplayName(sourceData.layer) +
                        " produced no mesh delta: v " + beforeVertices + "->" + afterVertices +
                        ", t " + beforeTriangles + "->" + afterTriangles +
                        ", e " + beforeEdges + "->" + afterEdges;
                return false;
            }

            error = GetLayerDisplayName(sourceData.layer) +
                    " v " + beforeVertices + "->" + afterVertices +
                    ", t " + beforeTriangles + "->" + afterTriangles +
                    ", e " + beforeEdges + "->" + afterEdges;
            return true;
        }
        finally
        {
            jointNearestBoneIndex = oldNearestBone;
        }
    }

    private bool TryApplyAutoShoulderSeamJointGeometry(
        LayerVertexData data,
        Vector2 center,
        float radius,
        int childBoneIndex,
        int parentSpriteBone,
        int nearestSpriteBone,
        out int affectedVertices,
        out int insertedVertices)
    {
        affectedVertices = 0;
        insertedVertices = 0;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (childBoneIndex < 0 || bones == null || childBoneIndex >= bones.Count || bones[childBoneIndex] == null)
            return false;

        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 6)
            return false;

        var childEnd = MapAutoJointDrawPointToBase(data, GetBoneEnd(childBoneIndex));
        var armAxis = childEnd - center;
        if (armAxis.sqrMagnitude <= 1e-8f)
            armAxis = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[childBoneIndex])) - center;
        if (armAxis.sqrMagnitude <= 1e-8f)
            return false;

        armAxis.Normalize();
        var seamDirection = new Vector2(-armAxis.y, armAxis.x).normalized;
        if (!TryFindAutoShoulderContourPair(data, boundaryPairs, center, seamDirection, radius, out var startHit, out var endHit))
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (!TryEnsureAutoShoulderVertexIndex(data, startHit, BuildSingleBoneWeight(parentSpriteBone), true, ref insertedVertices, out var startIndex))
            return false;
        if (!TryEnsureAutoShoulderVertexIndex(data, endHit, BuildSingleBoneWeight(parentSpriteBone), true, ref insertedVertices, out var endIndex))
            return false;
        if (!TryEnsureAutoShoulderVertexIndex(data, center, BuildSingleBoneWeight(nearestSpriteBone), false, ref insertedVertices, out var centerIndex))
            return false;
        var innerPoint = center + armAxis * Mathf.Clamp(radius * 0.2f, 4f, 16f);
        if (!TryEnsureAutoShoulderVertexIndex(data, innerPoint, BuildSingleBoneWeight(nearestSpriteBone), false, ref insertedVertices, out var innerIndex))
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        SetNearestJointVertexWeight(data, startHit, parentSpriteBone, nearestSpriteBone, 1f, radius, ref affectedVertices);
        SetNearestJointVertexWeight(data, endHit, parentSpriteBone, nearestSpriteBone, 1f, radius, ref affectedVertices);
        SetNearestJointVertexWeight(data, center, parentSpriteBone, nearestSpriteBone, 0.5f, radius, ref affectedVertices);
        SetNearestJointVertexWeight(data, innerPoint, parentSpriteBone, nearestSpriteBone, 0.25f, radius, ref affectedVertices);

        const float cPairOffset = 8f;
        InsertAutoShoulderOffsetPoint(data, center, startHit, cPairOffset, parentSpriteBone, nearestSpriteBone, radius, ref affectedVertices, ref insertedVertices);
        InsertAutoShoulderOffsetPoint(data, center, endHit, cPairOffset, parentSpriteBone, nearestSpriteBone, radius, ref affectedVertices, ref insertedVertices);

        data.lastPoseVersion = -1;
        data.deformedVertices = null;
        data.skinMatrices = null;
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
        {
            if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
                return false;
        }

        AppendAutoShoulderTriangle(data, startIndex, centerIndex, innerIndex, ref insertedVertices);
        AppendAutoShoulderTriangle(data, endIndex, innerIndex, centerIndex, ref insertedVertices);
        data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
        AddEdgePairUnique(ref data.allEdgePairs, centerIndex, startIndex);
        AddEdgePairUnique(ref data.allEdgePairs, centerIndex, endIndex);
        if (data.vertexUvs == null || data.vertexUvs.Length != data.vertices.Length)
            data.vertexUvs = BuildVertexUvsFromLayer(data.layer, data.vertices);
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        return affectedVertices > 0 || insertedVertices > 0;
    }

    private static void AppendAutoShoulderTriangle(LayerVertexData data, int a, int b, int c, ref int insertedVertices)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return;
        if (a < 0 || b < 0 || c < 0 || a >= data.vertices.Length || b >= data.vertices.Length || c >= data.vertices.Length)
            return;
        if (a == b || b == c || c == a)
            return;

        var area2 = Mathf.Abs(Cross2(data.vertices[b] - data.vertices[a], data.vertices[c] - data.vertices[a]));
        if (area2 <= 0.001f)
            return;

        var source = data.triangles ?? Array.Empty<int>();
        for (var i = 0; i + 2 < source.Length; i += 3)
        {
            if (TriangleHasSameIndices(source[i], source[i + 1], source[i + 2], a, b, c))
                return;
        }

        var oldLength = source.Length;
        Array.Resize(ref source, oldLength + 3);
        source[oldLength] = a;
        source[oldLength + 1] = b;
        source[oldLength + 2] = c;
        data.triangles = source;
        insertedVertices++;
    }

    private static bool TriangleHasSameIndices(int a0, int b0, int c0, int a1, int b1, int c1)
    {
        return (a0 == a1 || a0 == b1 || a0 == c1) &&
               (b0 == a1 || b0 == b1 || b0 == c1) &&
               (c0 == a1 || c0 == b1 || c0 == c1);
    }

    private static float Cross2(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private bool TryEnsureAutoShoulderVertexIndex(
        LayerVertexData data,
        Vector2 point,
        BoneWeight weight,
        bool contourPoint,
        ref int insertedVertices,
        out int vertexIndex)
    {
        vertexIndex = -1;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        vertexIndex = FindNearestVertexIndex(point, data.vertices);
        const float snapDistanceSqr = 0.0001f;
        if (vertexIndex < 0 || (data.vertices[vertexIndex] - point).sqrMagnitude > snapDistanceSqr)
        {
            vertexIndex = AppendAutoShoulderVertex(data, point, weight);
            if (vertexIndex < 0)
                return false;
            insertedVertices++;
        }

        if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
            return false;
        if ((data.vertices[vertexIndex] - point).sqrMagnitude > Mathf.Max(0.01f, snapDistanceSqr * 16f))
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (data.boneWeights != null && vertexIndex < data.boneWeights.Length)
            data.boneWeights[vertexIndex] = weight;
        return true;
    }

    private int AppendAutoShoulderVertex(LayerVertexData data, Vector2 point, BoneWeight weight)
    {
        if (data == null || data.vertices == null)
            return -1;

        var oldCount = data.vertices.Length;
        Array.Resize(ref data.vertices, oldCount + 1);
        data.vertices[oldCount] = point;

        if (data.vertexUvs == null || data.vertexUvs.Length != oldCount)
        {
            var nextUvs = new Vector2[oldCount + 1];
            if (data.vertexUvs != null)
                Array.Copy(data.vertexUvs, nextUvs, Mathf.Min(data.vertexUvs.Length, oldCount));
            data.vertexUvs = nextUvs;
        }
        else
        {
            Array.Resize(ref data.vertexUvs, oldCount + 1);
        }
        data.vertexUvs[oldCount] = ComputeUvForPoint(data.layer, point);

        if (data.boneWeights == null || data.boneWeights.Length != oldCount)
        {
            var nextWeights = new BoneWeight[oldCount + 1];
            if (data.boneWeights != null)
                Array.Copy(data.boneWeights, nextWeights, Mathf.Min(data.boneWeights.Length, oldCount));
            data.boneWeights = nextWeights;
        }
        else
        {
            Array.Resize(ref data.boneWeights, oldCount + 1);
        }
        data.boneWeights[oldCount] = weight;

        if (data.deformedVertices != null)
        {
            if (data.deformedVertices.Length == oldCount)
            {
                Array.Resize(ref data.deformedVertices, oldCount + 1);
                data.deformedVertices[oldCount] = point;
            }
            else
            {
                data.deformedVertices = null;
            }
        }

        return oldCount;
    }

    private readonly struct AutoShoulderContourCandidate
    {
        public readonly Vector2 Point;
        public readonly float DistanceSqr;
        public readonly float SignedDistance;

        public AutoShoulderContourCandidate(Vector2 point, float distanceSqr, float signedDistance)
        {
            Point = point;
            DistanceSqr = distanceSqr;
            SignedDistance = signedDistance;
        }
    }

    private bool TryFindAutoShoulderContourPair(
        LayerVertexData data,
        IReadOnlyList<int> boundaryPairs,
        Vector2 center,
        Vector2 seamDirection,
        float radius,
        out Vector2 startHit,
        out Vector2 endHit)
    {
        startHit = Vector2.zero;
        endHit = Vector2.zero;
        if (data == null || data.vertices == null || data.vertices.Length < 3 || boundaryPairs == null || boundaryPairs.Count < 6)
            return false;
        if (seamDirection.sqrMagnitude <= 1e-8f)
            return false;

        if (TryFindSignedAutoShoulderLineHits(data.vertices, boundaryPairs, center, seamDirection, out startHit, out endHit))
            return true;

        var candidates = new List<AutoShoulderContourCandidate>(Mathf.Max(8, boundaryPairs.Count / 2));
        for (var i = 0; i + 1 < boundaryPairs.Count; i += 2)
        {
            var aIndex = boundaryPairs[i];
            var bIndex = boundaryPairs[i + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= data.vertices.Length || bIndex >= data.vertices.Length || aIndex == bIndex)
                continue;

            var point = ClosestPointOnSegment(center, data.vertices[aIndex], data.vertices[bIndex]);
            var delta = point - center;
            candidates.Add(new AutoShoulderContourCandidate(point, delta.sqrMagnitude, Vector2.Dot(delta, seamDirection)));
        }

        if (candidates.Count < 2)
            return false;

        candidates.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));
        var maxCandidates = Mathf.Min(candidates.Count, 32);
        var minSeparation = Mathf.Max(2f, Mathf.Min(12f, radius * 0.12f));
        var minSeparationSqr = minSeparation * minSeparation;
        var bestScore = float.PositiveInfinity;
        var found = false;
        for (var i = 0; i < maxCandidates; i++)
        {
            for (var j = i + 1; j < maxCandidates; j++)
            {
                var separationSqr = (candidates[i].Point - candidates[j].Point).sqrMagnitude;
                if (separationSqr < minSeparationSqr)
                    continue;

                var oppositeSides = candidates[i].SignedDistance * candidates[j].SignedDistance <= 0f;
                var score = candidates[i].DistanceSqr + candidates[j].DistanceSqr + (oppositeSides ? 0f : 100000f);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                startHit = candidates[i].Point;
                endHit = candidates[j].Point;
                found = true;
            }
        }

        return found;
    }

    private static bool TryFindSignedAutoShoulderLineHits(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> boundaryPairs,
        Vector2 center,
        Vector2 seamDirection,
        out Vector2 positiveHit,
        out Vector2 negativeHit)
    {
        positiveHit = Vector2.zero;
        negativeHit = Vector2.zero;
        var bestPositiveT = float.PositiveInfinity;
        var bestNegativeT = float.NegativeInfinity;
        for (var i = 0; i + 1 < boundaryPairs.Count; i += 2)
        {
            var aIndex = boundaryPairs[i];
            var bIndex = boundaryPairs[i + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= vertices.Count || bIndex >= vertices.Count || aIndex == bIndex)
                continue;

            if (!ReanimatorJointGeometryAlgorithms.TryIntersectInfiniteLineSegment(
                    center,
                    seamDirection,
                    vertices[aIndex],
                    vertices[bIndex],
                    out var lineT,
                    out var hit))
            {
                continue;
            }

            if (lineT > 1e-4f && lineT < bestPositiveT)
            {
                bestPositiveT = lineT;
                positiveHit = hit;
            }
            else if (lineT < -1e-4f && lineT > bestNegativeT)
            {
                bestNegativeT = lineT;
                negativeHit = hit;
            }
        }

        return !float.IsPositiveInfinity(bestPositiveT) && !float.IsNegativeInfinity(bestNegativeT);
    }

    private void InsertAutoShoulderOffsetPoint(
        LayerVertexData data,
        Vector2 center,
        Vector2 contourPoint,
        float offset,
        int parentSpriteBone,
        int nearestSpriteBone,
        float radius,
        ref int affectedVertices,
        ref int insertedVertices)
    {
        var toCenter = center - contourPoint;
        if (toCenter.sqrMagnitude <= 1e-8f)
            return;

        var point = contourPoint + toCenter.normalized * Mathf.Max(0f, offset);
        if ((point - center).sqrMagnitude > radius * radius)
            return;

        if (TryEnsureJointInteriorVertex(data, point, BuildSingleBoneWeight(parentSpriteBone), out var changed) && changed)
            insertedVertices++;
        EnsureBoneWeightsArraySize(data, data.vertices != null ? data.vertices.Length : 0);
        SetNearestJointVertexWeight(data, point, parentSpriteBone, nearestSpriteBone, 0.75f, radius, ref affectedVertices);
    }

    private void SetNearestJointVertexWeight(
        LayerVertexData data,
        Vector2 point,
        int parentSpriteBone,
        int nearestSpriteBone,
        float parentRatio,
        float radius,
        ref int affectedVertices)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return;

        var vertexIndex = FindNearestVertexIndex(point, data.vertices);
        if (vertexIndex < 0)
            return;
        if ((data.vertices[vertexIndex] - point).sqrMagnitude > Mathf.Max(0.01f, radius * radius))
            return;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        SetJointVertexWeightByParentRatio(data, vertexIndex, parentSpriteBone, nearestSpriteBone, parentRatio);
        affectedVertices++;
    }

    private Vector2 MapAutoJointDrawPointToBase(LayerVertexData data, Vector2 drawPoint)
    {
        if (data != null && TryMapJointDrawPointToBaseDoc(data, drawPoint, out var mappedBaseDoc))
            return mappedBaseDoc;
        return data != null ? ConvertPointToBaseDoc(data, drawPoint) : drawPoint;
    }

    private bool TryResolveAutoJointStrictBlueIntersections(
        AutoJointSpec spec,
        LayerVertexData data,
        Vector2 boneBaseCenter,
        int parentBoneIndex,
        int childBoneIndex,
        out Vector2 center,
        out float radius,
        out float startAngle,
        out float endAngle)
    {
        center = boneBaseCenter;
        radius = 0f;
        startAngle = 0f;
        endAngle = 0f;

        var centerLineDirection = Vector2.zero;
        if (TryResolveAutoJointDirectionBoneBase(data, boneBaseCenter, spec.DirectionBoneNames, out var specDirection))
            centerLineDirection = specDirection;
        if (centerLineDirection.sqrMagnitude <= 1e-8f &&
            parentBoneIndex >= 0 &&
            parentBoneIndex < bones.Count &&
            bones[parentBoneIndex] != null)
        {
            centerLineDirection = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[parentBoneIndex])) - boneBaseCenter;
        }
        if (centerLineDirection.sqrMagnitude <= 1e-8f &&
            childBoneIndex >= 0 &&
            childBoneIndex < bones.Count &&
            bones[childBoneIndex] != null)
        {
            centerLineDirection = MapAutoJointDrawPointToBase(data, GetBoneEnd(childBoneIndex)) - boneBaseCenter;
        }
        if (centerLineDirection.sqrMagnitude <= 1e-8f)
            return false;

        centerLineDirection.Normalize();
        var hasBodyPair = TryResolveAutoJointBodyIntersectionPair(data, boneBaseCenter, out var hitA, out var hitB);
        var seamDirection = new Vector2(-centerLineDirection.y, centerLineDirection.x);
        var hasContourCutPair = TryResolveAutoJointContourCutPair(data, boneBaseCenter, seamDirection, out var cutA, out var cutB);
        if (!hasBodyPair && !hasContourCutPair)
            return false;

        if (!hasBodyPair || ShouldPreferAutoJointContourCutPair(boneBaseCenter, hitA, hitB, cutA, cutB, hasContourCutPair))
        {
            hitA = cutA;
            hitB = cutB;
        }

        if (!TryFindEquidistantAutoJointCenterOnLine(boneBaseCenter, centerLineDirection, hitA, hitB, out center))
            return false;

        var startVector = hitA - center;
        var endVector = hitB - center;
        var startDistance = startVector.magnitude;
        var endDistance = endVector.magnitude;
        radius = (startDistance + endDistance) * 0.5f;
        if (radius <= 0.01f || startVector.sqrMagnitude <= 1e-8f || endVector.sqrMagnitude <= 1e-8f)
            return false;

        startAngle = NormalizeAngleRad(Mathf.Atan2(startVector.y, startVector.x));
        endAngle = NormalizeAngleRad(Mathf.Atan2(endVector.y, endVector.x));
        return true;
    }

    private bool TryResolveAutoJointContourCutPair(
        LayerVertexData data,
        Vector2 centerHint,
        Vector2 seamDirection,
        out Vector2 hitA,
        out Vector2 hitB)
    {
        hitA = Vector2.zero;
        hitB = Vector2.zero;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (seamDirection.sqrMagnitude <= 1e-8f)
            return false;

        seamDirection.Normalize();
        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 6)
            return false;

        var hasPositive = false;
        var hasNegative = false;
        var bestPositiveT = float.PositiveInfinity;
        var bestNegativeT = float.NegativeInfinity;
        var positiveHit = Vector2.zero;
        var negativeHit = Vector2.zero;
        for (var i = 0; i + 1 < boundaryPairs.Length; i += 2)
        {
            var aIndex = boundaryPairs[i];
            var bIndex = boundaryPairs[i + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= data.vertices.Length || bIndex >= data.vertices.Length || aIndex == bIndex)
                continue;

            if (!ReanimatorJointGeometryAlgorithms.TryIntersectInfiniteLineSegment(
                    centerHint,
                    seamDirection,
                    data.vertices[aIndex],
                    data.vertices[bIndex],
                    out var lineT,
                    out var hit))
            {
                continue;
            }

            if (lineT > 1e-4f && lineT < bestPositiveT)
            {
                bestPositiveT = lineT;
                positiveHit = hit;
                hasPositive = true;
            }
            else if (lineT < -1e-4f && lineT > bestNegativeT)
            {
                bestNegativeT = lineT;
                negativeHit = hit;
                hasNegative = true;
            }
        }

        if (!hasPositive || !hasNegative)
            return false;
        if ((positiveHit - negativeHit).sqrMagnitude < 4f)
            return false;

        hitA = positiveHit;
        hitB = negativeHit;
        return true;
    }

    private static bool ShouldPreferAutoJointContourCutPair(
        Vector2 centerHint,
        Vector2 bodyA,
        Vector2 bodyB,
        Vector2 cutA,
        Vector2 cutB,
        bool hasContourCutPair)
    {
        if (!hasContourCutPair)
            return false;

        var bodySeparation = Vector2.Distance(bodyA, bodyB);
        var cutSeparation = Vector2.Distance(cutA, cutB);
        if (bodySeparation <= 0.0001f)
            return true;

        var bodyMidDistance = Vector2.Distance((bodyA + bodyB) * 0.5f, centerHint);
        var cutMidDistance = Vector2.Distance((cutA + cutB) * 0.5f, centerHint);
        return cutSeparation > bodySeparation * 1.2f && cutMidDistance <= bodyMidDistance + 12f;
    }

    private bool TryResolveAutoJointBodyIntersectionPair(
        LayerVertexData data,
        Vector2 centerHint,
        out Vector2 hitA,
        out Vector2 hitB)
    {
        hitA = Vector2.zero;
        hitB = Vector2.zero;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;
        if (!TryFindAutoJointBodyLayer(centerHint, out var bodyData) || bodyData == null || bodyData.vertices == null || bodyData.vertices.Length < 3)
            return false;

        var limbBoundary = ResolveBoundaryPairsForJointIntersections(data);
        var bodyBoundary = ResolveBoundaryPairsForJointIntersections(bodyData);
        if (limbBoundary == null || limbBoundary.Length < 6 || bodyBoundary == null || bodyBoundary.Length < 6)
            return false;

        var intersections = new List<Vector2>();
        CollectAutoJointContourIntersections(data.vertices, limbBoundary, bodyData.vertices, bodyBoundary, intersections);
        return TryPickAutoJointIntersectionPair(intersections, centerHint, out hitA, out hitB);
    }

    private static bool TryFindEquidistantAutoJointCenterOnLine(
        Vector2 linePoint,
        Vector2 lineDirection,
        Vector2 hitA,
        Vector2 hitB,
        out Vector2 center)
    {
        center = linePoint;
        if (lineDirection.sqrMagnitude <= 1e-8f)
            return false;

        lineDirection.Normalize();
        var hitDelta = hitB - hitA;
        var denominator = 2f * Vector2.Dot(lineDirection, hitDelta);
        if (Mathf.Abs(denominator) <= 1e-6f)
            return false;

        var numerator = hitB.sqrMagnitude - hitA.sqrMagnitude - 2f * Vector2.Dot(linePoint, hitDelta);
        var t = numerator / denominator;
        if (float.IsNaN(t) || float.IsInfinity(t))
            return false;

        center = linePoint + lineDirection * t;
        return true;
    }

    private bool TryResolveAutoJointDirectionBoneBase(LayerVertexData data, Vector2 center, IReadOnlyList<string> directionBoneNames, out Vector2 direction)
    {
        direction = Vector2.zero;
        if (directionBoneNames == null || directionBoneNames.Count == 0)
            return false;
        if (!TryFindRendererBoneByNames(directionBoneNames, out var directionBoneIndex))
            return false;
        if (directionBoneIndex < 0 || bones == null || directionBoneIndex >= bones.Count || bones[directionBoneIndex] == null)
            return false;

        var directionBoneBase = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[directionBoneIndex]));
        direction = directionBoneBase - center;
        if (direction.sqrMagnitude <= 1e-8f)
            return false;

        direction.Normalize();
        return true;
    }

    private bool TryResolveAutoJointBodyIntersectionDirection(LayerVertexData armData, Vector2 center, out Vector2 direction)
    {
        direction = Vector2.zero;
        if (armData == null || armData.vertices == null || armData.vertices.Length < 3)
            return false;
        if (!TryFindAutoJointBodyLayer(center, out var bodyData) || bodyData == null || bodyData.vertices == null || bodyData.vertices.Length < 3)
            return false;

        var armBoundary = ResolveBoundaryPairsForJointIntersections(armData);
        var bodyBoundary = ResolveBoundaryPairsForJointIntersections(bodyData);
        if (armBoundary == null || armBoundary.Length < 6 || bodyBoundary == null || bodyBoundary.Length < 6)
            return false;

        var intersections = new List<Vector2>();
        CollectAutoJointContourIntersections(armData.vertices, armBoundary, bodyData.vertices, bodyBoundary, intersections);
        if (intersections.Count < 2)
            return false;

        if (!TryPickAutoJointIntersectionPair(intersections, center, out var a, out var b))
            return false;

        var midpoint = (a + b) * 0.5f;
        direction = midpoint - center;
        if (direction.sqrMagnitude <= 1e-8f)
            return false;

        direction.Normalize();
        return true;
    }

    private bool TryFindAutoJointBodyLayer(Vector2 center, out LayerVertexData bodyData)
    {
        bodyData = null;
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return false;

        var bestScore = float.PositiveInfinity;
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.vertices == null || candidate.vertices.Length < 3)
                continue;

            var layerName = candidate.layer != null ? candidate.layer.name : string.Empty;
            var normalized = NormalizeAutoWeightName(layerName);
            if (string.IsNullOrEmpty(normalized))
                continue;
            if (ContainsAny(normalized, "clothes", "clotness", "shirt", "top", "arm", "hand", "leg", "hair", "head"))
                continue;
            if (!string.Equals(normalized, "body", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "torso", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "belly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bounds = BuildAutoLimbBounds(candidate.vertices);
            var score = bounds.Contains(center)
                ? Mathf.Abs(center.x - bounds.center.x) + Mathf.Abs(center.y - bounds.center.y)
                : 1000f + DistancePointToRect(center, bounds);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bodyData = candidate;
        }

        return bodyData != null;
    }

    private static void CollectAutoJointContourIntersections(
        IReadOnlyList<Vector2> armVertices,
        IReadOnlyList<int> armBoundary,
        IReadOnlyList<Vector2> bodyVertices,
        IReadOnlyList<int> bodyBoundary,
        List<Vector2> intersections)
    {
        if (armVertices == null || armBoundary == null || bodyVertices == null || bodyBoundary == null || intersections == null)
            return;

        for (var armIndex = 0; armIndex + 1 < armBoundary.Count; armIndex += 2)
        {
            var armAIndex = armBoundary[armIndex];
            var armBIndex = armBoundary[armIndex + 1];
            if (armAIndex < 0 || armBIndex < 0 || armAIndex >= armVertices.Count || armBIndex >= armVertices.Count || armAIndex == armBIndex)
                continue;

            var armA = armVertices[armAIndex];
            var armB = armVertices[armBIndex];
            for (var bodyIndex = 0; bodyIndex + 1 < bodyBoundary.Count; bodyIndex += 2)
            {
                var bodyAIndex = bodyBoundary[bodyIndex];
                var bodyBIndex = bodyBoundary[bodyIndex + 1];
                if (bodyAIndex < 0 || bodyBIndex < 0 || bodyAIndex >= bodyVertices.Count || bodyBIndex >= bodyVertices.Count || bodyAIndex == bodyBIndex)
                    continue;

                if (!TryIntersectAutoJointSegments(armA, armB, bodyVertices[bodyAIndex], bodyVertices[bodyBIndex], out var hit))
                    continue;

                AddUniqueAutoJointIntersection(intersections, hit);
            }
        }
    }

    private static bool TryIntersectAutoJointSegments(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
    {
        hit = Vector2.zero;
        var ab = b - a;
        var cd = d - c;
        var denom = Cross2(ab, cd);
        if (Mathf.Abs(denom) <= 1e-6f)
            return false;

        var ca = c - a;
        var t = Cross2(ca, cd) / denom;
        var u = Cross2(ca, ab) / denom;
        if (t < -1e-4f || t > 1f + 1e-4f || u < -1e-4f || u > 1f + 1e-4f)
            return false;

        hit = a + ab * t;
        return true;
    }

    private static void AddUniqueAutoJointIntersection(List<Vector2> intersections, Vector2 point)
    {
        const float mergeDistanceSqr = 0.25f;
        for (var i = 0; i < intersections.Count; i++)
        {
            if ((intersections[i] - point).sqrMagnitude <= mergeDistanceSqr)
                return;
        }

        intersections.Add(point);
    }

    private static bool TryPickAutoJointIntersectionPair(IReadOnlyList<Vector2> intersections, Vector2 center, out Vector2 a, out Vector2 b)
    {
        a = Vector2.zero;
        b = Vector2.zero;
        if (intersections == null || intersections.Count < 2)
            return false;

        var bestScore = float.PositiveInfinity;
        var found = false;
        const float minSeparationSqr = 4f;
        for (var i = 0; i < intersections.Count; i++)
        {
            for (var j = i + 1; j < intersections.Count; j++)
            {
                var separationSqr = (intersections[i] - intersections[j]).sqrMagnitude;
                if (separationSqr < minSeparationSqr)
                    continue;

                var midpoint = (intersections[i] + intersections[j]) * 0.5f;
                var score = (midpoint - center).sqrMagnitude + separationSqr * 0.001f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                a = intersections[i];
                b = intersections[j];
                found = true;
            }
        }

        return found;
    }

    private bool TryApplyDefaultAutoLimbJoint(AutoLimbJointSpec spec, out string error)
    {
        if (!TryPrepareDefaultAutoLimbJoint(spec, out var prepared, out error))
            return false;

        return TryApplyPreparedAutoLimbJoint(prepared, out error);
    }

    private string[] ApplyPreparedAutoLimbSequenceAfterAutoWeights(IReadOnlyList<AutoLimbJointSpec> specs)
    {
        if (specs == null || specs.Count == 0)
            return Array.Empty<string>();

        var prepared = new PreparedAutoLimbJoint[specs.Count];
        var results = new string[specs.Count];
        var allPrepared = true;
        for (var i = 0; i < specs.Count; i++)
        {
            if (TryPrepareDefaultAutoLimbJoint(specs[i], out prepared[i], out var prepareError))
                continue;

            allPrepared = false;
            results[i] = specs[i].Label + " limb skipped: " + prepareError;
        }

        if (!allPrepared)
        {
            for (var i = 0; i < results.Length; i++)
            {
                if (string.IsNullOrEmpty(results[i]))
                    results[i] = specs[i].Label + " limb skipped: paired joint preparation aborted";
            }
            return results;
        }

        for (var i = 0; i < prepared.Length; i++)
        {
            if (TryApplyPreparedAutoLimbJoint(prepared[i], out var applyDetail))
            {
                results[i] = specs[i].Label + " limb applied" +
                             (string.IsNullOrEmpty(applyDetail) ? string.Empty : ": " + applyDetail);
            }
            else
            {
                results[i] = specs[i].Label + " limb skipped: " + applyDetail;
            }
        }

        return results;
    }

    private bool TryPrepareDefaultAutoLimbJoint(
        AutoLimbJointSpec spec,
        out PreparedAutoLimbJoint prepared,
        out string error)
    {
        prepared = default;
        error = null;
        if (!TryFindRendererBonePairByNamePrefixes(
                spec.ParentBoneNames,
                spec.ChildBoneNames,
                out var parentBoneIndex,
                out var childBoneIndex))
        {
            error = spec.Label + ": missing bone pair";
            return false;
        }

        if (!TryFindAutoLimbTargetLine(
                spec,
                parentBoneIndex,
                childBoneIndex,
                out var selectionKey,
                out var line,
                out var targetError))
        {
            error = spec.Label + ": " + targetError;
            return false;
        }

        var oldContourStepDistance = LimbContourStepDistance;
        try
        {
            LimbContourStepDistance = 16f;
            if (!TryCalculateAutoLimbCullRadius(line, spec.ContourSteps, out var cullRadius))
            {
                error = spec.Label + ": failed to resolve cull radius";
                return false;
            }

            prepared = new PreparedAutoLimbJoint(
                spec,
                parentBoneIndex,
                childBoneIndex,
                selectionKey,
                line,
                cullRadius);
            return true;
        }
        finally
        {
            LimbContourStepDistance = oldContourStepDistance;
        }
    }

    private bool TryApplyPreparedAutoLimbJoint(PreparedAutoLimbJoint prepared, out string error)
    {
        error = null;
        var spec = prepared.Spec;
        var line = prepared.Line;
        var oldContourSteps = LimbContourSteps;
        var oldContourStepDistance = LimbContourStepDistance;
        var oldTowardCenterShift = LimbTowardCenterShift;
        var oldSelectionKey = selectedLayerSelectionKey;
        var oldSelectedLineIndex = limbSelectedLineIndex;
        var lineIndex = -1;
        try
        {
            LimbContourSteps = spec.ContourSteps;
            LimbContourStepDistance = 16f;
            LimbTowardCenterShift = 0.35f;
            selectedLayerSelectionKey = prepared.SelectionKey;
            limbLines.Add(line);
            lineIndex = limbLines.Count - 1;
            limbSelectedLineIndex = lineIndex;

            if (ApplySelectedLimb(true, prepared.CullRadius, true, true, out var applyError))
            {
                var layerName = line != null && TryGetLayerVertexDataForLimbLine(line, out var targetData) && targetData != null && targetData.layer != null
                    ? targetData.layer.name
                    : prepared.SelectionKey;
                var parentName = prepared.ParentBoneIndex >= 0 && prepared.ParentBoneIndex < bones.Count && bones[prepared.ParentBoneIndex] != null
                    ? bones[prepared.ParentBoneIndex].name
                    : "<invalid>";
                var childName = prepared.ChildBoneIndex >= 0 && prepared.ChildBoneIndex < bones.Count && bones[prepared.ChildBoneIndex] != null
                    ? bones[prepared.ChildBoneIndex].name
                    : "<invalid>";
                var anchor = line != null ? line.requiredJointAnchorDoc : Vector2.zero;
                error = "layer=" + layerName +
                        ", bones=" + parentName + "->" + childName +
                        ", anchor=(" + anchor.x.ToString("F2", CultureInfo.InvariantCulture) +
                        ", " + anchor.y.ToString("F2", CultureInfo.InvariantCulture) + ")";
                ReanimatorLog.Trace("[Reanimator][Limbs][Auto] " + spec.Label + " applied: " + error, true);
                return true;
            }

            error = spec.Label + ": " + applyError;
            return false;
        }
        finally
        {
            if (lineIndex >= 0 && lineIndex < limbLines.Count && ReferenceEquals(limbLines[lineIndex], line))
                RemoveLimbLineAt(lineIndex);
            LimbContourSteps = oldContourSteps;
            LimbContourStepDistance = oldContourStepDistance;
            LimbTowardCenterShift = oldTowardCenterShift;
            selectedLayerSelectionKey = oldSelectionKey;
            limbSelectedLineIndex = oldSelectedLineIndex >= 0 && oldSelectedLineIndex < limbLines.Count
                ? oldSelectedLineIndex
                : -1;
        }
    }

    private bool TryCalculateAutoLimbCullRadius(LimbLine line, int contourSteps, out float radius)
    {
        radius = 0f;
        if (line == null)
            return false;
        if (!line.hasContourPoints)
            RefreshLimbLineContour(line);
        if (!line.hasContourPoints)
            return false;
        if (!TryGetLayerVertexDataForLimbLine(line, out var data) || data == null)
            return false;

        var contourPoints = new List<Vector2>();
        if (!TryCollectLimbContourPoints(line, data, Mathf.Max(1, contourSteps), LimbContourStepDistance, contourPoints) || contourPoints.Count == 0)
            return false;

        var centerBase = ConvertPointToBaseDoc(data, GetLimbContourMidpoint(line));
        var maxDistance = 0f;
        for (var i = 0; i < contourPoints.Count; i++)
        {
            var pointBase = ConvertPointToBaseDoc(data, contourPoints[i]);
            maxDistance = Mathf.Max(maxDistance, Vector2.Distance(centerBase, pointBase));
        }

        radius = Mathf.Max(0.01f, maxDistance + 1f);
        return radius > 0.01f;
    }

    private bool TryFindRendererBoneByNames(IReadOnlyList<string> names, out int boneIndex)
    {
        boneIndex = -1;
        if (names == null || bones == null)
            return false;

        for (var nameIndex = 0; nameIndex < names.Count; nameIndex++)
        {
            var normalizedTarget = NormalizeAutoWeightName(names[nameIndex]);
            if (string.IsNullOrEmpty(normalizedTarget))
                continue;

            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null)
                    continue;
                if (!string.Equals(NormalizeAutoWeightName(bone.name), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    continue;

                boneIndex = i;
                return true;
            }
        }

        return false;
    }

    private bool TryFindRendererBonePairByNamePrefixes(
        IReadOnlyList<string> parentNamePrefixes,
        IReadOnlyList<string> childNamePrefixes,
        out int parentBoneIndex,
        out int childBoneIndex)
    {
        parentBoneIndex = -1;
        childBoneIndex = -1;
        if (parentNamePrefixes == null || childNamePrefixes == null || bones == null)
            return false;

        var bestScore = int.MaxValue;
        for (var parentIndex = 0; parentIndex < bones.Count; parentIndex++)
        {
            var parent = bones[parentIndex];
            if (parent == null ||
                !TryScoreBoneNamePrefix(parent.name, parentNamePrefixes, out var parentScore))
            {
                continue;
            }

            for (var childIndex = 0; childIndex < bones.Count; childIndex++)
            {
                var child = bones[childIndex];
                if (child == null || child.parentId != parentIndex ||
                    !TryScoreBoneNamePrefix(child.name, childNamePrefixes, out var childScore))
                {
                    continue;
                }

                var candidateScore = parentScore + childScore;
                if (candidateScore >= bestScore)
                    continue;

                bestScore = candidateScore;
                parentBoneIndex = parentIndex;
                childBoneIndex = childIndex;
            }
        }

        return parentBoneIndex >= 0 && childBoneIndex >= 0;
    }

    private static bool TryScoreBoneNamePrefix(
        string boneName,
        IReadOnlyList<string> namePrefixes,
        out int score)
    {
        score = int.MaxValue;
        var normalizedBoneName = NormalizeAutoWeightName(boneName);
        if (string.IsNullOrEmpty(normalizedBoneName) || namePrefixes == null)
            return false;

        for (var prefixIndex = 0; prefixIndex < namePrefixes.Count; prefixIndex++)
        {
            var normalizedPrefix = NormalizeAutoWeightName(namePrefixes[prefixIndex]);
            if (string.IsNullOrEmpty(normalizedPrefix) ||
                !normalizedBoneName.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffixLength = normalizedBoneName.Length - normalizedPrefix.Length;
            var candidateScore = prefixIndex * 10000 + Mathf.Min(9999, suffixLength);
            if (candidateScore < score)
                score = candidateScore;
        }

        return score < int.MaxValue;
    }

    private bool TryFindAutoLimbTargetLine(
        AutoLimbJointSpec spec,
        int parentBoneIndex,
        int childBoneIndex,
        out string selectionKey,
        out LimbLine line,
        out string error)
    {
        selectionKey = string.Empty;
        line = null;
        error = "no visible target layer";
        var preferPrimaryLayer = spec.PrimaryLayerNames != null && spec.PrimaryLayerNames.Length > 0;
        if (preferPrimaryLayer &&
            TryFindAutoLimbTargetLine(
                spec,
                parentBoneIndex,
                childBoneIndex,
                true,
                out selectionKey,
                out line,
                out _))
        {
            return true;
        }

        return TryFindAutoLimbTargetLine(
            spec,
            parentBoneIndex,
            childBoneIndex,
            false,
            out selectionKey,
            out line,
            out error);
    }

    private bool TryFindAutoLimbTargetLine(
        AutoLimbJointSpec spec,
        int parentBoneIndex,
        int childBoneIndex,
        bool requirePrimaryLayer,
        out string selectionKey,
        out LimbLine line,
        out string error)
    {
        selectionKey = string.Empty;
        line = null;
        error = "no visible target layer";
        var bestScore = float.PositiveInfinity;
        var joint = bones[childBoneIndex].worldPosition;
        var visibleCandidates = 0;
        var matchingCandidates = 0;
        var missingParentMap = 0;
        var missingChildMap = 0;
        var contourFailures = 0;
        var failedLayerNames = new List<string>();
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.layer == null || !candidate.layer.visible)
                continue;
            visibleCandidates++;
            if (requirePrimaryLayer && !IsPrimaryAutoJointLayer(candidate, spec.PrimaryLayerNames))
                continue;
            matchingCandidates++;
            var hasParentBone = LayerHasMappedRendererBone(candidate, parentBoneIndex);
            var hasChildBone = LayerHasMappedRendererBone(candidate, childBoneIndex);
            if (!hasParentBone || !hasChildBone)
            {
                if (!hasParentBone)
                    missingParentMap++;
                if (!hasChildBone)
                    missingChildMap++;
                continue;
            }

            var score = ScoreAutoLimbTargetLayer(spec, candidate, joint);
            if (score >= bestScore)
                continue;
            if (!TryBuildAutoLimbLine(pair.Key, candidate, parentBoneIndex, childBoneIndex, out var candidateLine))
            {
                contourFailures++;
                if (failedLayerNames.Count < 3)
                    failedLayerNames.Add(candidate.layer.name);
                continue;
            }

            bestScore = score;
            selectionKey = pair.Key;
            line = candidateLine;
        }

        if (line != null)
        {
            error = null;
            return true;
        }

        var parentName = parentBoneIndex >= 0 && parentBoneIndex < bones.Count && bones[parentBoneIndex] != null
            ? bones[parentBoneIndex].name
            : "<invalid>";
        var childName = childBoneIndex >= 0 && childBoneIndex < bones.Count && bones[childBoneIndex] != null
            ? bones[childBoneIndex].name
            : "<invalid>";
        error = "no usable target: visible=" + visibleCandidates +
                ", considered=" + matchingCandidates +
                ", missing-map=" + parentName + ":" + missingParentMap +
                "/" + childName + ":" + missingChildMap +
                ", contour-failed=" + contourFailures;
        if (failedLayerNames.Count > 0)
            error += " [" + string.Join(",", failedLayerNames) + "]";
        return line != null;
    }

    private static bool LayerHasMappedRendererBone(LayerVertexData data, int rendererBoneIndex)
    {
        if (data == null || data.spriteBoneToRendererBone == null)
            return false;

        for (var i = 0; i < data.spriteBoneToRendererBone.Length; i++)
        {
            if (data.spriteBoneToRendererBone[i] == rendererBoneIndex)
                return true;
        }

        return false;
    }

    private static float ScoreAutoLimbTargetLayer(AutoLimbJointSpec spec, LayerVertexData data, Vector2 joint)
    {
        var score = 0f;
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalizedLayerName = NormalizeAutoWeightName(layerName);
        if (ContainsAny(normalizedLayerName, spec.LayerNameTerms))
            score -= 5000f;
        else if (ContainsAny(normalizedLayerName, "body", "torso", "spine", "shirt", "clothes", "clotness", "top"))
            score += 500f;
        else
            score += 100000f;

        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return score + 10000f;

        var bounds = BuildAutoLimbBounds(data.vertices);
        if (!bounds.Contains(joint))
            score += 10000f + DistancePointToRect(joint, bounds);
        else
            score += Mathf.Abs(joint.x - bounds.center.x) + Mathf.Abs(joint.y - bounds.center.y);

        return score;
    }

    private bool TryBuildAutoLimbLine(
        string selectionKey,
        LayerVertexData data,
        int parentBoneIndex,
        int childBoneIndex,
        out LimbLine line)
    {
        line = null;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        var parent = bones[parentBoneIndex];
        var child = bones[childBoneIndex];
        if (parent == null || child == null)
            return false;

        var joint = child.worldPosition;
        var parentDirection = joint - parent.worldPosition;
        if (parentDirection.sqrMagnitude <= 1e-8f)
            parentDirection = GetBoneEnd(parentBoneIndex) - parent.worldPosition;
        var childDirection = GetBoneEnd(childBoneIndex) - joint;
        if (childDirection.sqrMagnitude <= 1e-8f)
            childDirection = joint - parent.worldPosition;
        if (parentDirection.sqrMagnitude <= 1e-8f || childDirection.sqrMagnitude <= 1e-8f)
            return false;

        parentDirection.Normalize();
        childDirection.Normalize();

        var bounds = BuildAutoLimbBounds(data.vertices);
        var halfLength = Mathf.Max(16f, Mathf.Sqrt(bounds.width * bounds.width + bounds.height * bounds.height));
        var stableKey = data.layer != null ? BuildKey(data.layer.name, data.layer.layerId) : string.Empty;
        var lineDirection = ResolveAutoLimbBisectorNormal(parentDirection, childDirection);
        var bestLine = BuildAutoLimbLineCandidate(
            stableKey,
            selectionKey,
            joint,
            lineDirection,
            halfLength,
            parentBoneIndex,
            childBoneIndex);
        if (TryGetElbowContourPoints(bestLine.startDoc, bestLine.endDoc, data, out var lineContourA, out var lineContourB))
        {
            bestLine.contourPointA = lineContourA;
            bestLine.contourPointB = lineContourB;
            bestLine.hasContourPoints = true;
        }
        else
        {
            if (!TryFindOpposedAutoLimbBoundaryPoints(data, joint, lineDirection, out var contourA, out var contourB))
                return false;
            bestLine.contourPointA = contourA;
            bestLine.contourPointB = contourB;
            bestLine.hasContourPoints = true;
        }

        line = bestLine;
        return true;
    }

    private static Vector2 ResolveAutoLimbBisectorNormal(Vector2 parentDirection, Vector2 childDirection)
    {
        if (parentDirection.sqrMagnitude <= 1e-8f && childDirection.sqrMagnitude <= 1e-8f)
            return Vector2.right;
        if (parentDirection.sqrMagnitude <= 1e-8f)
            parentDirection = childDirection;
        if (childDirection.sqrMagnitude <= 1e-8f)
            childDirection = parentDirection;

        parentDirection.Normalize();
        childDirection.Normalize();
        var bisector = parentDirection + childDirection;
        if (bisector.sqrMagnitude <= 1e-8f)
            bisector = parentDirection;
        bisector.Normalize();
        return new Vector2(-bisector.y, bisector.x);
    }

    private static LimbLine BuildAutoLimbLineCandidate(
        string stableKey,
        string selectionKey,
        Vector2 joint,
        Vector2 lineDirection,
        float halfLength,
        int parentBoneIndex,
        int childBoneIndex)
    {
        return new LimbLine
        {
            layerStableKey = stableKey,
            layerSelectionKey = selectionKey,
            startDoc = joint - lineDirection * halfLength,
            endDoc = joint + lineDirection * halfLength,
            hasRequiredJointAnchor = true,
            requiredJointAnchorDoc = joint,
            requiredParentBoneIndex = parentBoneIndex,
            requiredChildBoneIndex = childBoneIndex
        };
    }

    private bool TryFindOpposedAutoLimbBoundaryPoints(
        LayerVertexData data,
        Vector2 joint,
        Vector2 lineDirection,
        out Vector2 negativePoint,
        out Vector2 positivePoint)
    {
        negativePoint = Vector2.zero;
        positivePoint = Vector2.zero;
        if (data == null)
            return false;
        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 2)
            return false;
        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length < 2 || lineDirection.sqrMagnitude <= 1e-8f)
            return false;

        lineDirection.Normalize();
        var hasNegative = false;
        var hasPositive = false;
        var bestNegativeDistance = float.PositiveInfinity;
        var bestPositiveDistance = float.PositiveInfinity;
        var pairCount = boundaryPairs.Length / 2;
        for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            var aIndex = boundaryPairs[pairIndex * 2];
            var bIndex = boundaryPairs[pairIndex * 2 + 1];
            if (aIndex < 0 || bIndex < 0 || aIndex >= vertices.Length || bIndex >= vertices.Length)
                continue;

            var a = vertices[aIndex];
            var b = vertices[bIndex];
            var closest = ClosestPointOnSegment(joint, a, b);
            ScoreOpposedAutoLimbBoundaryPoint(a, joint, lineDirection, ref hasNegative, ref negativePoint, ref bestNegativeDistance, ref hasPositive, ref positivePoint, ref bestPositiveDistance);
            ScoreOpposedAutoLimbBoundaryPoint(b, joint, lineDirection, ref hasNegative, ref negativePoint, ref bestNegativeDistance, ref hasPositive, ref positivePoint, ref bestPositiveDistance);
            ScoreOpposedAutoLimbBoundaryPoint(closest, joint, lineDirection, ref hasNegative, ref negativePoint, ref bestNegativeDistance, ref hasPositive, ref positivePoint, ref bestPositiveDistance);
        }

        return hasNegative && hasPositive && Vector2.Distance(negativePoint, positivePoint) > 0.01f;
    }

    private static void ScoreOpposedAutoLimbBoundaryPoint(
        Vector2 point,
        Vector2 joint,
        Vector2 lineDirection,
        ref bool hasNegative,
        ref Vector2 negativePoint,
        ref float bestNegativeDistance,
        ref bool hasPositive,
        ref Vector2 positivePoint,
        ref float bestPositiveDistance)
    {
        var delta = point - joint;
        var signedDistance = Vector2.Dot(delta, lineDirection);
        var distanceSqr = delta.sqrMagnitude;
        if (signedDistance <= 0f && distanceSqr < bestNegativeDistance)
        {
            hasNegative = true;
            negativePoint = point;
            bestNegativeDistance = distanceSqr;
        }
        if (signedDistance >= 0f && distanceSqr < bestPositiveDistance)
        {
            hasPositive = true;
            positivePoint = point;
            bestPositiveDistance = distanceSqr;
        }
    }

    private bool TryFindAutoJointTargetLayer(
        AutoJointSpec spec,
        int childBoneIndex,
        out string selectionKey,
        out string stableKey,
        out LayerVertexData data)
    {
        selectionKey = string.Empty;
        stableKey = string.Empty;
        data = null;

        var bestScore = float.PositiveInfinity;
        var joint = GetBoneBasePoint(bones[childBoneIndex]);
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.vertices == null || candidate.vertices.Length < 3)
                continue;
            if (!IsPrimaryAutoJointLayer(candidate, spec.PrimaryLayerNames))
                continue;
            if (!LayerHasMappedRendererBone(candidate, childBoneIndex))
                continue;

            var score = ScoreAutoJointTargetLayer(spec, candidate, joint);
            if (score >= bestScore)
                continue;

            bestScore = score;
            selectionKey = pair.Key;
            stableKey = candidate.layer != null ? BuildKey(candidate.layer.name, candidate.layer.layerId) : string.Empty;
            data = candidate;
        }

        return data != null;
    }

    private static bool IsPrimaryAutoJointLayer(LayerVertexData data, string[] primaryLayerNames)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (primaryLayerNames == null || primaryLayerNames.Length == 0)
            return true;

        for (var i = 0; i < primaryLayerNames.Length; i++)
        {
            var primary = NormalizeAutoWeightName(primaryLayerNames[i]);
            if (!string.IsNullOrEmpty(primary) &&
                string.Equals(normalized, primary, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static float ScoreAutoJointTargetLayer(AutoJointSpec spec, LayerVertexData data, Vector2 joint)
    {
        var score = 0f;
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalizedLayerName = NormalizeAutoWeightName(layerName);
        if (!ContainsAny(normalizedLayerName, spec.LayerNameTerms))
            score += 100000f;

        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return score + 10000f;

        var bounds = BuildAutoLimbBounds(data.vertices);
        if (!bounds.Contains(joint))
            score += 1000f + DistancePointToRect(joint, bounds);
        else
            score += Mathf.Abs(joint.x - bounds.center.x) + Mathf.Abs(joint.y - bounds.center.y);

        return score;
    }

    private float CalculateAutoJointRadius(LayerVertexData data, Vector2 center, int childBoneIndex, int parentBoneIndex, float minimumRadius = 0f)
    {
        var radius = 24f;
        if (minimumRadius > 0.0001f)
            radius = Mathf.Max(radius, minimumRadius + 1f);
        if (data != null && data.vertices != null && data.vertices.Length > 0)
        {
            var bounds = BuildAutoLimbBounds(data.vertices);
            radius = Mathf.Clamp(Mathf.Min(bounds.width, bounds.height) * 0.35f, 10f, 128f);
        }

        if (childBoneIndex >= 0 && childBoneIndex < bones.Count &&
            parentBoneIndex >= 0 && parentBoneIndex < bones.Count &&
            bones[childBoneIndex] != null && bones[parentBoneIndex] != null)
        {
            var childBase = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[childBoneIndex]));
            var parentBase = MapAutoJointDrawPointToBase(data, GetBoneBasePoint(bones[parentBoneIndex]));
            var boneDistance = Vector2.Distance(childBase, parentBase);
            if (boneDistance > 0.0001f)
                radius = Mathf.Clamp(Mathf.Max(radius, boneDistance * 0.22f), 10f, 128f);
        }

        radius = ExpandAutoJointRadiusToRayHit(data, center, jointStartAngleRad, radius);
        radius = ExpandAutoJointRadiusToRayHit(data, center, jointEndAngleRad, radius);
        radius = ExpandAutoJointRadiusToRayHit(data, center, GetJointOffsetHandleAngleRad(radius, true), radius);
        radius = ExpandAutoJointRadiusToRayHit(data, center, GetJointOffsetHandleAngleRad(radius, false), radius);

        return Mathf.Clamp(radius, 0.01f, 128f);
    }

    private float ExpandAutoJointRadiusToRayHit(LayerVertexData data, Vector2 center, float angleRad, float radius)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return radius;

        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 6)
            return radius;

        if (!TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, center, angleRad, out var hit))
            return radius;

        var hitDistance = Vector2.Distance(center, hit);
        return hitDistance > 0.0001f
            ? Mathf.Max(radius, hitDistance + 1f)
            : radius;
    }

    private static Rect BuildAutoLimbBounds(IReadOnlyList<Vector2> vertices)
    {
        if (vertices == null || vertices.Count == 0)
            return new Rect();

        var min = vertices[0];
        var max = vertices[0];
        for (var i = 1; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static float DistancePointToRect(Vector2 point, Rect rect)
    {
        var dx = point.x < rect.xMin ? rect.xMin - point.x : point.x > rect.xMax ? point.x - rect.xMax : 0f;
        var dy = point.y < rect.yMin ? rect.yMin - point.y : point.y > rect.yMax ? point.y - rect.yMax : 0f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private int[] BuildDeterministicAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);

        if (TryGetExactHeadAutoWeightBoneName(normalized, out var exactHeadBoneName))
        {
            AddRendererBonesByName(result, exactHeadBoneName);
            return result.ToArray();
        }

        if (IsNeckAutoWeightLayer(normalized))
        {
            AddRendererBonesByName(result, "spine", "neck");
            return result.ToArray();
        }

        if (IsFrontHairAutoWeightLayer(normalized))
        {
            AddRendererBonesByName(result, "head", "front_hair");
            return result.ToArray();
        }

        if (IsBackHairAutoWeightLayer(normalized))
        {
            AddRendererBonesByName(result, "head", "back_hair");
            return result.ToArray();
        }

        AddSemanticLayerBoneMatches(result, normalized);

        return result.ToArray();
    }

    private int[] BuildSingleNamedAutoWeightBoneMap(string boneName)
    {
        var result = new List<int>();
        AddRendererBonesByName(result, boneName);
        return result.ToArray();
    }

    private int[] BuildNeckAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsNeckAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(result, "spine", "neck");
        return result.ToArray();
    }

    private int[] BuildFrontHairAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsFrontHairAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(result, "head", "front_hair");
        return result.ToArray();
    }

    private int[] BuildBackHairAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsBackHairAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(result, "head", "back_hair");
        return result.ToArray();
    }

    private bool TryBuildNeckVerticalBoneWeights(
        LayerVertexData data,
        out BoneWeight[] weights,
        out int weightedVertices)
    {
        weights = null;
        weightedVertices = 0;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length < 2)
            return false;

        var minY = data.vertices[0].y;
        var maxY = data.vertices[0].y;
        for (var i = 1; i < data.vertices.Length; i++)
        {
            var y = data.vertices[i].y;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        var height = maxY - minY;
        if (height <= 0.0001f)
            return false;

        weights = new BoneWeight[data.vertices.Length];
        for (var i = 0; i < data.vertices.Length; i++)
        {
            var neckWeight = Mathf.Clamp01((data.vertices[i].y - minY) / height);
            weights[i] = BuildNeckVerticalBoneWeight(neckWeight);
            weightedVertices++;
        }

        return weightedVertices > 0;
    }

    private static BoneWeight BuildNeckVerticalBoneWeight(float neckWeight)
    {
        var neck = Mathf.Clamp01(neckWeight);
        var spine = 1f - neck;
        return new BoneWeight
        {
            boneIndex0 = 0,
            boneIndex1 = 1,
            boneIndex2 = 0,
            boneIndex3 = 0,
            weight0 = spine,
            weight1 = neck,
            weight2 = 0f,
            weight3 = 0f
        };
    }

    private bool TryBuildFrontHairVerticalBoneWeights(
        LayerVertexData data,
        out BoneWeight[] weights,
        out int weightedVertices)
    {
        weights = null;
        weightedVertices = 0;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length < 2)
            return false;

        var minY = data.vertices[0].y;
        var maxY = data.vertices[0].y;
        for (var i = 1; i < data.vertices.Length; i++)
        {
            var y = data.vertices[i].y;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        var height = maxY - minY;
        if (height <= 0.0001f)
            return false;

        weights = new BoneWeight[data.vertices.Length];
        for (var i = 0; i < data.vertices.Length; i++)
        {
            var topWeight = Mathf.Clamp01((data.vertices[i].y - minY) / height);
            var frontHairWeight = 1f - topWeight;
            weights[i] = BuildFrontHairVerticalBoneWeight(frontHairWeight);
            weightedVertices++;
        }

        return weightedVertices > 0;
    }

    private static BoneWeight BuildFrontHairVerticalBoneWeight(float frontHairWeight)
    {
        var hair = Mathf.Clamp01(frontHairWeight);
        var head = 1f - hair;
        return new BoneWeight
        {
            boneIndex0 = 0,
            boneIndex1 = 1,
            boneIndex2 = 0,
            boneIndex3 = 0,
            weight0 = head,
            weight1 = hair,
            weight2 = 0f,
            weight3 = 0f
        };
    }

    private bool TryBuildBackHairVerticalBoneWeights(
        LayerVertexData data,
        out BoneWeight[] weights,
        out int weightedVertices)
    {
        weights = null;
        weightedVertices = 0;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length < 2)
            return false;

        var minY = data.vertices[0].y;
        var maxY = data.vertices[0].y;
        for (var i = 1; i < data.vertices.Length; i++)
        {
            var y = data.vertices[i].y;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        var height = maxY - minY;
        if (height <= 0.0001f)
            return false;

        weights = new BoneWeight[data.vertices.Length];
        for (var i = 0; i < data.vertices.Length; i++)
        {
            var topWeight = Mathf.Clamp01((data.vertices[i].y - minY) / height);
            var backHairWeight = 1f - topWeight;
            weights[i] = BuildBackHairVerticalBoneWeight(backHairWeight);
            weightedVertices++;
        }

        return weightedVertices > 0;
    }

    private static BoneWeight BuildBackHairVerticalBoneWeight(float backHairWeight)
    {
        var hair = Mathf.Clamp01(backHairWeight);
        var head = 1f - hair;
        return new BoneWeight
        {
            boneIndex0 = 0,
            boneIndex1 = 1,
            boneIndex2 = 0,
            boneIndex3 = 0,
            weight0 = head,
            weight1 = hair,
            weight2 = 0f,
            weight3 = 0f
        };
    }

    private int[] BuildLeftArmAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsLeftArmAutoWeightLayer(data))
            return result.ToArray();

        AddIsolatedArmBoneChain(result, "l", IsSecondaryArmAutoWeightLayer(data, "l"));
        return result.ToArray();
    }

    private int[] BuildRightArmAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsRightArmAutoWeightLayer(data))
            return result.ToArray();

        AddIsolatedArmBoneChain(result, "r", IsSecondaryArmAutoWeightLayer(data, "r"));
        return result.ToArray();
    }

    private void AddIsolatedArmBoneChain(List<int> result, string side, bool secondaryPair)
    {
        if (result == null || bones == null || string.IsNullOrEmpty(side))
            return;

        var spineIndex = FindRendererBoneByNormalizedName("spine");
        if (spineIndex >= 0)
            AddRendererBoneIndex(result, spineIndex);

        var rootName = side + (secondaryPair ? "arm2" : "arm");
        var rootIndex = FindRendererBoneByNormalizedName(rootName);
        if (rootIndex < 0 && !secondaryPair)
            rootIndex = FindLegacyPrimaryArmRoot(side, spineIndex);
        if (rootIndex < 0)
            return;

        AddRendererBoneIndex(result, rootIndex);
        var armChain = new HashSet<int> { rootIndex };
        for (var pass = 0; pass < bones.Count; pass++)
        {
            var changed = false;
            for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                var bone = bones[boneIndex];
                if (bone == null || !armChain.Contains(bone.parentId) || armChain.Contains(boneIndex))
                {
                    continue;
                }

                armChain.Add(boneIndex);
                AddRendererBoneIndex(result, boneIndex);
                changed = true;
            }

            if (!changed)
                break;
        }
    }

    private int FindRendererBoneByNormalizedName(string normalizedName)
    {
        if (bones == null || string.IsNullOrEmpty(normalizedName))
            return -1;
        for (var i = 0; i < bones.Count; i++)
        {
            if (bones[i] != null &&
                string.Equals(NormalizeAutoWeightName(bones[i].name), normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private int FindLegacyPrimaryArmRoot(string side, int spineIndex)
    {
        if (bones == null)
            return -1;
        var prefix = side + "arm";
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var normalized = bone != null ? NormalizeAutoWeightName(bone.name) : string.Empty;
            if (bone == null || bone.parentId != spineIndex ||
                !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, side + "arm2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            return i;
        }
        return -1;
    }

    private int[] BuildLeftLegAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsLeftLegAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(
            result,
            "hips",
            "l_leg",
            "l_leg_1",
            "l_foot");
        return result.ToArray();
    }

    private int[] BuildRightLegAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsRightLegAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(
            result,
            "hips",
            "r_leg",
            "r_leg_1",
            "r_foot");
        return result.ToArray();
    }

    private int[] BuildBodyAutoWeightBoneMap(LayerVertexData data)
    {
        var result = new List<int>();
        if (!IsBodyAutoWeightLayer(data))
            return result.ToArray();

        AddRendererBonesByName(
            result,
            "hips",
            "belly",
            "spine",
            "neck");
        return result.ToArray();
    }

    private static bool IsBodyAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ContainsAny(
                normalized,
                "arm",
                "hand",
                "leg",
                "foot",
                "hair",
                "head",
                "face",
                "eye",
                "mouth",
                "clothes",
                "clotness",
                "shirt",
                "top",
                "boob",
                "breast",
                "chest",
                "nipple"))
            return false;

        return string.Equals(normalized, "body", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "torso", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "belly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNeckAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        return IsNeckAutoWeightLayer(NormalizeAutoWeightName(layerName));
    }

    private static bool IsNeckAutoWeightLayer(string normalizedLayerName)
    {
        return string.Equals(normalizedLayerName, "neck", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFrontHairAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        return IsFrontHairAutoWeightLayer(NormalizeAutoWeightName(layerName));
    }

    private static bool IsFrontHairAutoWeightLayer(string normalizedLayerName)
    {
        return !string.IsNullOrEmpty(normalizedLayerName) &&
               (normalizedLayerName.IndexOf("fronthair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedLayerName.IndexOf("hairfront", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsBackHairAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        return IsBackHairAutoWeightLayer(NormalizeAutoWeightName(layerName));
    }

    private static bool IsBackHairAutoWeightLayer(string normalizedLayerName)
    {
        return !string.IsNullOrEmpty(normalizedLayerName) &&
               (normalizedLayerName.IndexOf("backhair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedLayerName.IndexOf("hairback", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool TryGetExactHeadAutoWeightBoneName(LayerVertexData data, out string boneName)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        return TryGetExactHeadAutoWeightBoneName(NormalizeAutoWeightName(layerName), out boneName);
    }

    private static bool TryGetExactHeadAutoWeightBoneName(string normalizedLayerName, out string boneName)
    {
        boneName = string.Empty;
        if (string.IsNullOrEmpty(normalizedLayerName))
            return false;

        if (ContainsAny(normalizedLayerName, "eyewhites", "eyewhite", "eywhites", "eywhite"))
        {
            boneName = "head";
            return true;
        }

        if (string.Equals(normalizedLayerName, "eyes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedLayerName, "eye", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedLayerName, "irides", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedLayerName, "iris", StringComparison.OrdinalIgnoreCase))
        {
            boneName = "eyes";
            return true;
        }

        if (string.Equals(normalizedLayerName, "head", StringComparison.OrdinalIgnoreCase))
        {
            boneName = "head";
            return true;
        }

        if (normalizedLayerName.IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            boneName = "mouth";
            return true;
        }

        return false;
    }

    private static bool IsLeftArmAutoWeightLayer(LayerVertexData data)
    {
        if (data == null || data.layer == null || !data.layer.visible)
            return false;

        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ContainsAny(normalized, "rarm", "armr", "rightarm", "rhand", "handr", "righthand", "relbow", "elbowr", "rwrist", "wristr", "rightwrist", "rshoulder", "shoulderr", "rightshoulder"))
            return false;

        return ContainsAny(
            normalized,
            "larm",
            "arml",
            "leftarm",
            "upperarml",
            "leftupperarm",
            "forearml",
            "leftforearm",
            "lhand",
            "handl",
            "lefthand",
            "lelbow",
            "elbowl",
            "leftelbow",
            "lwrist",
            "wristl",
            "leftwrist",
            "lshoulder",
            "shoulderl",
            "leftshoulder");
    }

    private static bool IsSecondaryArmAutoWeightLayer(LayerVertexData data, string side)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        return IsSecondaryArmAutoWeightName(normalized, side);
    }

    private static bool IsSecondaryArmAutoWeightName(string normalized, string side)
    {
        if (string.Equals(side, "l", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(normalized, "larm2", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "leftarm2", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "arm2l", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(side, "r", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(normalized, "rarm2", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "rightarm2", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "arm2r", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsRightArmAutoWeightLayer(LayerVertexData data)
    {
        if (data == null || data.layer == null || !data.layer.visible)
            return false;

        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ContainsAny(normalized, "larm", "arml", "leftarm", "lhand", "handl", "lefthand", "lelbow", "elbowl", "lwrist", "wristl", "leftwrist", "lshoulder", "shoulderl", "leftshoulder"))
            return false;

        return ContainsAny(
            normalized,
            "rarm",
            "armr",
            "rightarm",
            "upperarmr",
            "rightupperarm",
            "forearmr",
            "rightforearm",
            "rhand",
            "handr",
            "righthand",
            "relbow",
            "elbowr",
            "rightelbow",
            "rwrist",
            "wristr",
            "rightwrist",
            "rshoulder",
            "shoulderr",
            "rightshoulder");
    }

    private static bool IsLeftLegAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ContainsAny(normalized, "rleg", "legr", "rightleg", "rthigh", "thighr", "rightthigh", "rshin", "shinr", "rightshin", "rknee", "kneer", "rightknee", "rankle", "ankler", "rightankle", "rfoot", "footr", "rightfoot", "rhip", "hipr", "righthip"))
            return false;

        return ContainsAny(
            normalized,
            "lleg",
            "legl",
            "leftleg",
            "lthigh",
            "thighl",
            "leftthigh",
            "lshin",
            "shinl",
            "leftshin",
            "lknee",
            "kneel",
            "leftknee",
            "lankle",
            "anklel",
            "leftankle",
            "lfoot",
            "footl",
            "leftfoot",
            "lhip",
            "hipl",
            "lefthip");
    }

    private static bool IsRightLegAutoWeightLayer(LayerVertexData data)
    {
        var layerName = data != null && data.layer != null ? data.layer.name : string.Empty;
        var normalized = NormalizeAutoWeightName(layerName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (ContainsAny(normalized, "lleg", "legl", "leftleg", "lthigh", "thighl", "leftthigh", "lshin", "shinl", "leftshin", "lknee", "kneel", "leftknee", "lankle", "anklel", "leftankle", "lfoot", "footl", "leftfoot", "lhip", "hipl", "lefthip"))
            return false;

        return ContainsAny(
            normalized,
            "rleg",
            "legr",
            "rightleg",
            "rthigh",
            "thighr",
            "rightthigh",
            "rshin",
            "shinr",
            "rightshin",
            "rknee",
            "kneer",
            "rightknee",
            "rankle",
            "ankler",
            "rightankle",
            "rfoot",
            "footr",
            "rightfoot",
            "rhip",
            "hipr",
            "righthip");
    }


    private void AddExactLayerBoneMatches(List<int> result, string normalizedLayerName)
    {
        if (string.IsNullOrEmpty(normalizedLayerName) || bones == null)
            return;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null)
                continue;
            var normalizedBone = NormalizeAutoWeightName(bone.name);
            if (string.IsNullOrEmpty(normalizedBone))
                continue;
            if (IsForbiddenAutoWeightBoneName(normalizedBone))
                continue;
            if (string.Equals(normalizedLayerName, normalizedBone, StringComparison.OrdinalIgnoreCase))
                AddRendererBoneIndex(result, i);
        }
    }

    private void AddSemanticLayerBoneMatches(List<int> result, string normalizedLayerName)
    {
        if (string.IsNullOrEmpty(normalizedLayerName))
            return;

        if (ContainsAny(normalizedLayerName, "hair", "pony", "lock", "eye", "eyelash", "iride", "iris", "mouth", "nose", "face", "head"))
        {
            AddRendererBonesByName(result, "head");
            return;
        }

        if (ContainsAny(normalizedLayerName, "lnipple", "nipplel"))
        {
            AddRendererBonesByName(result, "l_nipple", "nipple_l");
            return;
        }

        if (ContainsAny(normalizedLayerName, "rnipple", "nippler"))
        {
            AddRendererBonesByName(result, "r_nipple", "nipple_r");
            return;
        }

        if (ContainsAny(normalizedLayerName, "boob", "breast", "chest"))
        {
            AddRendererBonesByName(result, "l_nipple", "r_nipple", "nipple_l", "nipple_r", "spine");
            return;
        }

        if (ContainsAny(normalizedLayerName, "panty", "pantsu", "pelvis", "hips"))
        {
            AddRendererBonesByName(result, "hips", "belly");
            return;
        }

        if (IsSecondaryArmAutoWeightName(normalizedLayerName, "l"))
        {
            AddIsolatedArmBoneChain(result, "l", true);
            return;
        }

        if (IsSecondaryArmAutoWeightName(normalizedLayerName, "r"))
        {
            AddIsolatedArmBoneChain(result, "r", true);
            return;
        }

        if (ContainsAny(normalizedLayerName, "lhand", "lefthand"))
        {
            AddIsolatedArmBoneChain(result, "l", false);
            return;
        }

        if (ContainsAny(normalizedLayerName, "rhand", "righthand"))
        {
            AddIsolatedArmBoneChain(result, "r", false);
            return;
        }

        if (ContainsAny(normalizedLayerName, "larm", "leftarm", "lshoulder", "lelbow"))
        {
            AddIsolatedArmBoneChain(result, "l", false);
            return;
        }

        if (ContainsAny(normalizedLayerName, "rarm", "rightarm", "rshoulder", "relbow"))
        {
            AddIsolatedArmBoneChain(result, "r", false);
            return;
        }

        if (ContainsAny(normalizedLayerName, "lleg", "leftleg", "lthigh", "lknee", "lfoot"))
        {
            AddRendererBonesByName(result, "hips", "l_leg", "l_leg_1", "l_foot");
            return;
        }

        if (ContainsAny(normalizedLayerName, "rleg", "rightleg", "rthigh", "rknee", "rfoot"))
        {
            AddRendererBonesByName(result, "hips", "r_leg", "r_leg_1", "r_foot");
            return;
        }

        if (ContainsAny(normalizedLayerName, "body", "torso", "belly", "spine", "shirt", "top", "clothes"))
        {
            AddRendererBonesByName(result, "hips", "belly", "spine", "l_nipple", "r_nipple", "nipple_l", "nipple_r", "neck");
        }
    }

    private void AddClosestRendererBones(List<int> result, LayerVertexData data, int maxCount)
    {
        if (result == null || data == null || data.vertices == null || data.vertices.Length == 0 || bones == null)
            return;

        var center = Vector2.zero;
        for (var i = 0; i < data.vertices.Length; i++)
            center += data.vertices[i];
        center /= data.vertices.Length;

        for (var pass = 0; pass < maxCount; pass++)
        {
            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                if (!CanUseAutoWeightFallbackBone(boneIndex) || ContainsRendererBoneIndex(result, boneIndex))
                    continue;

                var distance = DistancePointToSegment(center, bones[boneIndex].worldPosition, GetBoneEnd(boneIndex));
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestIndex = boneIndex;
            }

            if (bestIndex < 0)
                break;
            AddRendererBoneIndex(result, bestIndex);
        }
    }

    private bool CanUseAutoWeightFallbackBone(int boneIndex)
    {
        if (!CanUseAutoWeightRendererBone(boneIndex))
            return false;
        var name = NormalizeAutoWeightName(bones[boneIndex].name);
        return !string.Equals(name, "root", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanUseAutoWeightRendererBone(int boneIndex)
    {
        if (bones == null || boneIndex < 0 || boneIndex >= bones.Count || bones[boneIndex] == null)
            return false;
        return !IsForbiddenAutoWeightBoneName(bones[boneIndex].name);
    }

    private static bool IsForbiddenAutoWeightBoneName(string boneName)
    {
        return string.Equals(
            NormalizeAutoWeightName(boneName),
            ForbiddenAutoWeightBoneName,
            StringComparison.OrdinalIgnoreCase);
    }

    private void AddRendererBonesByName(List<int> result, params string[] names)
    {
        if (result == null || names == null || bones == null)
            return;

        for (var nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            var normalizedTarget = NormalizeAutoWeightName(names[nameIndex]);
            if (string.IsNullOrEmpty(normalizedTarget) || IsForbiddenAutoWeightBoneName(normalizedTarget))
                continue;

            for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                var bone = bones[boneIndex];
                if (bone == null)
                    continue;
                if (!CanUseAutoWeightRendererBone(boneIndex))
                    continue;
                if (string.Equals(NormalizeAutoWeightName(bone.name), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    AddRendererBoneIndex(result, boneIndex);
            }
        }
    }

    private void AddRendererBonesByPrefix(List<int> result, params string[] prefixes)
    {
        if (result == null || prefixes == null || bones == null)
            return;

        for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            var bone = bones[boneIndex];
            if (bone == null)
                continue;
            if (!CanUseAutoWeightRendererBone(boneIndex))
                continue;

            var normalizedBone = NormalizeAutoWeightName(bone.name);
            for (var prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            {
                var normalizedPrefix = NormalizeAutoWeightName(prefixes[prefixIndex]);
                if (!string.IsNullOrEmpty(normalizedPrefix) && normalizedBone.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    AddRendererBoneIndex(result, boneIndex);
            }
        }
    }

    private static void AddRendererBoneIndex(List<int> result, int boneIndex)
    {
        if (result == null || boneIndex < 0 || ContainsRendererBoneIndex(result, boneIndex))
            return;
        result.Add(boneIndex);
    }

    private static bool ContainsRendererBoneIndex(List<int> result, int boneIndex)
    {
        if (result == null)
            return false;
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i] == boneIndex)
                return true;
        }
        return false;
    }

    private int CountForbiddenAutoWeightBoneBindings()
    {
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return 0;

        var boundLayers = 0;
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var map = pair.Value != null ? pair.Value.spriteBoneToRendererBone : null;
            if (map == null)
                continue;

            for (var i = 0; i < map.Length; i++)
            {
                var rendererBoneIndex = map[i];
                if (bones == null || rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count ||
                    bones[rendererBoneIndex] == null ||
                    !IsForbiddenAutoWeightBoneName(bones[rendererBoneIndex].name))
                {
                    continue;
                }

                boundLayers++;
                break;
            }
        }
        return boundLayers;
    }

    private int CountCrossArmPairBoneBindings()
    {
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return 0;

        var invalidLayers = 0;
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var data = pair.Value;
            if (data == null || data.layer == null || !data.layer.visible)
                continue;

            int[] allowedMap;
            if (IsLeftArmAutoWeightLayer(data))
                allowedMap = BuildLeftArmAutoWeightBoneMap(data);
            else if (IsRightArmAutoWeightLayer(data))
                allowedMap = BuildRightArmAutoWeightBoneMap(data);
            else
                continue;

            var actualMap = data.spriteBoneToRendererBone;
            if (actualMap == null)
                continue;
            var allowed = new HashSet<int>(allowedMap ?? Array.Empty<int>());
            for (var i = 0; i < actualMap.Length; i++)
            {
                if (allowed.Contains(actualMap[i]))
                    continue;
                invalidLayers++;
                break;
            }
        }
        return invalidLayers;
    }

    private static bool ContainsAny(string source, params string[] terms)
    {
        if (string.IsNullOrEmpty(source) || terms == null)
            return false;
        for (var i = 0; i < terms.Length; i++)
        {
            var term = NormalizeAutoWeightName(terms[i]);
            if (!string.IsNullOrEmpty(term) && source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static string NormalizeAutoWeightName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = char.ToLowerInvariant(value[i]);
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }
        return builder.ToString();
    }

    public ProjectorCandidateInfo[] GetProjectorCandidates(out string targetLayerName, out string targetKey, out string error)
    {
        targetLayerName = string.Empty;
        targetKey = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out _, out var targetSelectionKey, out _))
        {
            error = "Select sprite with geometry.";
            return Array.Empty<ProjectorCandidateInfo>();
        }

        return GetProjectorCandidates(targetSelectionKey, out targetLayerName, out targetKey, out error);
    }

    public bool TryGetProjectorSelectedTarget(out string targetLayerName, out string targetKey, out string error)
    {
        targetLayerName = string.Empty;
        targetKey = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out var targetData, out var targetSelectionKey, out var targetStableKey) || targetData == null)
        {
            error = "Select sprite with geometry.";
            return false;
        }

        targetLayerName = GetLayerDisplayName(targetData.layer);
        targetKey = targetSelectionKey;

        if (!HasProjectorGeometry(targetData))
        {
            error = "Selected sprite has no geometry.";
            targetLayerName = string.Empty;
            targetKey = string.Empty;
            return false;
        }

        return true;
    }

    public ProjectorCandidateInfo[] GetProjectorCandidates(
        string targetSelectionKey,
        out string targetLayerName,
        out string targetKey,
        out string error)
    {
        targetLayerName = string.Empty;
        targetKey = string.Empty;
        error = null;

        if (!TryResolveProjectorLayer(targetSelectionKey, out var targetStableKey, out var targetData) || targetData == null)
        {
            error = "Set a valid target sprite.";
            return Array.Empty<ProjectorCandidateInfo>();
        }

        targetLayerName = GetLayerDisplayName(targetData.layer);
        targetKey = targetSelectionKey;

        if (!HasProjectorGeometry(targetData))
        {
            error = "Target sprite has no geometry.";
            return Array.Empty<ProjectorCandidateInfo>();
        }

        var candidates = new List<ProjectorCandidateInfo>();
        if (layerInfos == null || layerInfos.Count == 0)
            return candidates.ToArray();

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;
            if (!layer.visible)
                continue;

            var selectionKey = BuildSelectionKey(layer);
            var stableKey = BuildKey(layer.name, layer.layerId);
            if (IsLayerMatch(selectionKey, stableKey, targetSelectionKey, targetStableKey))
                continue;

            if (!TryGetLayerVertexDataForKeys(selectionKey, stableKey, out var sourceData) || sourceData == null)
                continue;
            if (!HasProjectorGeometry(sourceData))
                continue;
            if (!GeometriesIntersect(targetData, sourceData))
                continue;

            candidates.Add(new ProjectorCandidateInfo(
                selectionKey,
                stableKey,
                GetLayerDisplayName(sourceData.layer),
                sourceData.vertices != null ? sourceData.vertices.Length : 0,
                sourceData.layer));
        }

        return candidates.ToArray();
    }

    public bool ApplyProjectorWeights(
        IReadOnlyList<string> sourceSelectionKeys,
        bool clearTargetWeights,
        out int weightedCount,
        out string targetLayerName,
        out string error)
    {
        weightedCount = 0;
        targetLayerName = string.Empty;
        error = null;
        if (!TryGetWeightBrushLayerData(out _, out var targetSelectionKey, out _))
        {
            error = "Select sprite with geometry.";
            return false;
        }

        return ApplyProjectorWeights(targetSelectionKey, sourceSelectionKeys, clearTargetWeights, out weightedCount, out targetLayerName, out error);
    }

    public bool ApplyProjectorWeights(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool clearTargetWeights,
        out int weightedCount,
        out string targetLayerName,
        out string error)
    {
        return ApplyProjectorWeights(
            targetSelectionKey,
            sourceSelectionKeys,
            clearTargetWeights,
            false,
            out weightedCount,
            out targetLayerName,
            out error);
    }

    public bool ApplyProjectorWeights(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool clearTargetWeights,
        bool postProcessExpandedCoverage,
        out int weightedCount,
        out string targetLayerName,
        out string error)
    {
        return ApplyProjectorWeights(
            targetSelectionKey,
            sourceSelectionKeys,
            clearTargetWeights,
            postProcessExpandedCoverage,
            false,
            out weightedCount,
            out targetLayerName,
            out error);
    }

    public bool ApplyProjectorWeights(
        string targetSelectionKey,
        IReadOnlyList<string> sourceSelectionKeys,
        bool clearTargetWeights,
        bool postProcessExpandedCoverage,
        bool smoothPostProcessWeights,
        out int weightedCount,
        out string targetLayerName,
        out string error)
    {
        weightedCount = 0;
        targetLayerName = string.Empty;
        error = null;

        if (sourceSelectionKeys == null || sourceSelectionKeys.Count == 0)
        {
            error = "Select source geometry.";
            return false;
        }

        if (!TryResolveProjectorLayer(targetSelectionKey, out var targetStableKey, out var targetData) || targetData == null)
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

        var sources = BuildProjectorSourceList(sourceSelectionKeys, targetSelectionKey, targetStableKey, true, targetData);
        if (sources.Count == 0)
        {
            error = "Selected sources do not intersect target or have no weights.";
            return false;
        }

        var targetDryRun = CloneProjectorLayerData(targetData);
        if (!TryBuildProjectorWeights(
                targetDryRun,
                sources,
                clearTargetWeights,
                postProcessExpandedCoverage,
                smoothPostProcessWeights,
                out weightedCount,
                out _,
                out _,
                out var changed))
        {
            error = "No target vertices covered by selected geometry.";
            return false;
        }

        if (!changed && !HasMissingVertexWeights(targetData))
            return true;

        RegisterUndo("Projector Weights");
        if (!TryBuildProjectorWeights(
                targetData,
                sources,
                clearTargetWeights,
                postProcessExpandedCoverage,
                smoothPostProcessWeights,
                out weightedCount,
                out var nextWeights,
                out var projectedVertexMask,
                out _))
        {
            error = "No target vertices covered by selected geometry.";
            return false;
        }

        targetData.boneWeights = nextWeights;
        FixMissingVertexWeightsForData(targetData, out _, out _, false, projectedVertexMask);
        EnsureLayerBindMatricesForWeights(targetData);
        MarkSkinningWeightsChanged(targetData);
        MarkEditGeometryDataChanged(targetData, targetSelectionKey, targetStableKey);
        return true;
    }

    public bool FixMissingVertexWeights(
        out int fixedCount,
        out int missingCount,
        out string layerName,
        out string error)
    {
        fixedCount = 0;
        missingCount = 0;
        layerName = string.Empty;
        error = null;

        if (!TryGetWeightBrushLayerData(out var data, out var selectionKey, out var stableKey) || data == null)
        {
            error = "Select sprite with geometry.";
            return false;
        }

        layerName = GetLayerDisplayName(data.layer);
        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount <= 0)
        {
            error = "Selected sprite has no geometry.";
            return false;
        }

        var fixedAny = FixMissingVertexWeightsForData(data, out fixedCount, out missingCount);
        if (missingCount <= 0)
        {
            error = "No missing vertex weights.";
            return false;
        }
        if (!fixedAny)
        {
            error = "No missing weights could be fixed.";
            return false;
        }

        RegisterUndo("FIX Vertex Weights");
        EnsureLayerBindMatricesForWeights(data);
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, selectionKey, stableKey);
        return true;
    }

    private bool FixMissingVertexWeightsForData(
        LayerVertexData data,
        out int fixedCount,
        out int missingCount,
        bool allowNearestBoneFallback = true,
        IReadOnlyList<bool> eligibleVertexMask = null)
    {
        fixedCount = 0;
        missingCount = 0;
        var vertexCount = data != null && data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount <= 0)
            return false;

        var currentWeights = new BoneWeight[vertexCount];
        var hasWeight = new bool[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            if (data.boneWeights != null && i < data.boneWeights.Length)
                currentWeights[i] = data.boneWeights[i];
            hasWeight[i] = IsUsableVertexWeight(currentWeights[i]);
            if (!hasWeight[i] && IsVertexWeightFixEligible(eligibleVertexMask, i, vertexCount))
                missingCount++;
        }

        if (missingCount <= 0)
            return false;

        var neighbors = BuildVertexWeightFixNeighbors(data, vertexCount);
        if (neighbors != null && neighbors.Length == vertexCount)
        {
            for (var pass = 0; pass < vertexCount; pass++)
            {
                var changed = false;
                var nextWeights = new Dictionary<int, BoneWeight>();
                for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    if (hasWeight[vertexIndex])
                        continue;
                    if (!IsVertexWeightFixEligible(eligibleVertexMask, vertexIndex, vertexCount))
                        continue;
                    if (!TrySmoothMissingVertexWeight(vertexIndex, data.vertices, neighbors, currentWeights, hasWeight, out var smoothed))
                        continue;
                    nextWeights[vertexIndex] = smoothed;
                }

                foreach (var pair in nextWeights)
                {
                    currentWeights[pair.Key] = pair.Value;
                    hasWeight[pair.Key] = true;
                    fixedCount++;
                    changed = true;
                }

                if (!changed)
                    break;
            }
        }

        if (!allowNearestBoneFallback)
        {
            data.boneWeights = currentWeights;
            return fixedCount > 0;
        }

        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            if (hasWeight[vertexIndex])
                continue;
            if (!IsVertexWeightFixEligible(eligibleVertexMask, vertexIndex, vertexCount))
                continue;
            if (!TryBuildNearestBoneWeight(data.vertices[vertexIndex], data, out var nearestWeight))
                continue;

            currentWeights[vertexIndex] = nearestWeight;
            hasWeight[vertexIndex] = true;
            fixedCount++;
        }

        data.boneWeights = currentWeights;
        return fixedCount > 0;
    }

    private static bool IsVertexWeightFixEligible(IReadOnlyList<bool> eligibleVertexMask, int vertexIndex, int vertexCount)
    {
        if (eligibleVertexMask == null)
            return true;
        return vertexIndex >= 0 &&
               vertexIndex < vertexCount &&
               vertexIndex < eligibleVertexMask.Count &&
               eligibleVertexMask[vertexIndex];
    }

    private static bool HasMissingVertexWeights(LayerVertexData data)
    {
        var vertexCount = data != null && data.vertices != null ? data.vertices.Length : 0;
        if (vertexCount <= 0)
            return false;

        for (var i = 0; i < vertexCount; i++)
        {
            var weight = data.boneWeights != null && i < data.boneWeights.Length
                ? data.boneWeights[i]
                : default;
            if (!IsUsableVertexWeight(weight))
                return true;
        }

        return false;
    }

    private static List<int>[] BuildVertexWeightFixNeighbors(LayerVertexData data, int vertexCount)
    {
        var neighbors = new List<int>[vertexCount];
        for (var i = 0; i < neighbors.Length; i++)
            neighbors[i] = new List<int>();

        AddVertexWeightFixEdgePairs(neighbors, data != null ? data.allEdgePairs : null, vertexCount);
        if (!HasVertexWeightFixNeighbors(neighbors))
        {
            AddVertexWeightFixTriangles(neighbors, data != null ? data.triangles : null, vertexCount);
        }
        if (!HasVertexWeightFixNeighbors(neighbors) && data != null && data.triangles != null && data.triangles.Length >= 3)
        {
            BuildEdgePairs(data.triangles, vertexCount, out var allPairs, out _);
            AddVertexWeightFixEdgePairs(neighbors, allPairs, vertexCount);
        }

        return neighbors;
    }

    private static bool HasVertexWeightFixNeighbors(IReadOnlyList<List<int>> neighbors)
    {
        if (neighbors == null)
            return false;
        for (var i = 0; i < neighbors.Count; i++)
        {
            if (neighbors[i] != null && neighbors[i].Count > 0)
                return true;
        }
        return false;
    }

    private static void AddVertexWeightFixTriangles(IReadOnlyList<List<int>> neighbors, int[] triangles, int vertexCount)
    {
        if (neighbors == null || triangles == null)
            return;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            AddVertexWeightFixNeighborPair(neighbors, triangles[i], triangles[i + 1], vertexCount);
            AddVertexWeightFixNeighborPair(neighbors, triangles[i + 1], triangles[i + 2], vertexCount);
            AddVertexWeightFixNeighborPair(neighbors, triangles[i + 2], triangles[i], vertexCount);
        }
    }

    private static void AddVertexWeightFixEdgePairs(IReadOnlyList<List<int>> neighbors, int[] edgePairs, int vertexCount)
    {
        if (neighbors == null || edgePairs == null)
            return;
        for (var i = 0; i + 1 < edgePairs.Length; i += 2)
            AddVertexWeightFixNeighborPair(neighbors, edgePairs[i], edgePairs[i + 1], vertexCount);
    }

    private static void AddVertexWeightFixNeighborPair(IReadOnlyList<List<int>> neighbors, int a, int b, int vertexCount)
    {
        if (neighbors == null || a < 0 || b < 0 || a >= vertexCount || b >= vertexCount || a == b)
            return;
        if (!neighbors[a].Contains(b))
            neighbors[a].Add(b);
        if (!neighbors[b].Contains(a))
            neighbors[b].Add(a);
    }

    private static bool TrySmoothMissingVertexWeight(
        int vertexIndex,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<List<int>> neighbors,
        IReadOnlyList<BoneWeight> weights,
        IReadOnlyList<bool> hasWeight,
        out BoneWeight smoothed)
    {
        smoothed = default;
        if (neighbors == null || weights == null || hasWeight == null || vertexIndex < 0 || vertexIndex >= neighbors.Count)
            return false;

        var accum = new Dictionary<int, float>();
        var totalFactor = 0f;
        var list = neighbors[vertexIndex];
        if (list == null || list.Count == 0)
            return false;

        for (var i = 0; i < list.Count; i++)
        {
            var neighborIndex = list[i];
            if (neighborIndex < 0 || neighborIndex >= weights.Count || neighborIndex >= hasWeight.Count || !hasWeight[neighborIndex])
                continue;
            var factor = 1f;
            if (vertices != null && vertexIndex < vertices.Count && neighborIndex < vertices.Count)
                factor = 1f / Mathf.Max(0.001f, Vector2.Distance(vertices[vertexIndex], vertices[neighborIndex]));

            AccumulateWeightedVertexFix(accum, weights[neighborIndex], factor);
            totalFactor += factor;
        }

        if (totalFactor <= 0.0001f || accum.Count == 0)
            return false;

        smoothed = BuildVertexFixWeight(accum);
        return IsUsableVertexWeight(smoothed);
    }

    private static void AccumulateWeightedVertexFix(Dictionary<int, float> accum, BoneWeight weight, float factor)
    {
        AccumulateWeightChannel(accum, weight.boneIndex0, weight.weight0 * factor);
        AccumulateWeightChannel(accum, weight.boneIndex1, weight.weight1 * factor);
        AccumulateWeightChannel(accum, weight.boneIndex2, weight.weight2 * factor);
        AccumulateWeightChannel(accum, weight.boneIndex3, weight.weight3 * factor);
    }

    private static BoneWeight BuildVertexFixWeight(Dictionary<int, float> accum)
    {
        var topBones = new int[4] { 0, 0, 0, 0 };
        var topWeights = new float[4];
        if (accum != null)
        {
            foreach (var pair in accum)
                InsertTopWeight(topBones, topWeights, pair.Key, pair.Value);
        }

        var sum = topWeights[0] + topWeights[1] + topWeights[2] + topWeights[3];
        if (sum <= 0.0001f)
            return default;

        var inv = 1f / sum;
        return new BoneWeight
        {
            boneIndex0 = topBones[0],
            boneIndex1 = topBones[1],
            boneIndex2 = topBones[2],
            boneIndex3 = topBones[3],
            weight0 = topWeights[0] * inv,
            weight1 = topWeights[1] * inv,
            weight2 = topWeights[2] * inv,
            weight3 = topWeights[3] * inv
        };
    }

    private static bool IsUsableVertexWeight(BoneWeight weight)
    {
        return weight.weight0 > 0.0001f ||
               weight.weight1 > 0.0001f ||
               weight.weight2 > 0.0001f ||
               weight.weight3 > 0.0001f;
    }

    private bool TryBuildProjectorWeights(
        LayerVertexData targetData,
        IReadOnlyList<LayerVertexData> sources,
        bool clearTargetWeights,
        bool postProcessExpandedCoverage,
        bool smoothPostProcessWeights,
        out int weightedCount,
        out BoneWeight[] nextWeights,
        out bool[] projectedVertexMask,
        out bool changed)
    {
        weightedCount = 0;
        nextWeights = Array.Empty<BoneWeight>();
        projectedVertexMask = Array.Empty<bool>();
        changed = false;
        if (!HasProjectorGeometry(targetData) || sources == null || sources.Count == 0)
            return false;

        var vertexCount = targetData.vertices.Length;
        projectedVertexMask = new bool[vertexCount];
        if (clearTargetWeights)
        {
            changed = HasProjectorTargetSkinningData(targetData);
            ClearProjectorTargetSkinningData(targetData, vertexCount);
            nextWeights = targetData.boneWeights;
        }
        else
        {
            nextWeights = targetData.boneWeights != null && targetData.boneWeights.Length == vertexCount
                ? CloneArray(targetData.boneWeights)
                : new BoneWeight[vertexCount];
            if (nextWeights == null || nextWeights.Length != vertexCount)
                nextWeights = new BoneWeight[vertexCount];
        }

        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            var point = targetData.vertices[vertexIndex];
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var sourceData = sources[sourceIndex];
                if (!IsTargetVertexInsideProjectorSource(targetData, vertexIndex, sourceData))
                    continue;

                if (!TryTransferWeightFromSource(point, sourceData, true, out var sourceWeight))
                    continue;
                if (!TryRemapProjectorWeight(sourceWeight, sourceData, targetData, out sourceWeight, out var expandedTargetBones))
                    continue;

                if (expandedTargetBones || !AreBoneWeightsApproximatelyEqual(nextWeights[vertexIndex], sourceWeight))
                    changed = true;

                nextWeights[vertexIndex] = sourceWeight;
                projectedVertexMask[vertexIndex] = true;
                weightedCount++;
                break;
            }
        }

        if (postProcessExpandedCoverage)
        {
            ApplyExpandedProjectorCoveragePostProcess(
                targetData,
                sources,
                nextWeights,
                projectedVertexMask,
                smoothPostProcessWeights,
                ref weightedCount,
                ref changed);
        }

        return weightedCount > 0;
    }

    private List<LayerVertexData> BuildProjectorSourceList(
        IReadOnlyList<string> sourceSelectionKeys,
        string targetSelectionKey,
        string targetStableKey,
        bool requireWeights = true,
        LayerVertexData targetData = null)
    {
        var result = new List<LayerVertexData>();
        for (var i = 0; i < sourceSelectionKeys.Count; i++)
        {
            var selectionKey = sourceSelectionKeys[i];
            if (string.IsNullOrEmpty(selectionKey))
                continue;
            if (!TryResolveProjectorLayer(selectionKey, out var stableKey, out var data) || data == null)
                continue;
            if (IsLayerMatch(selectionKey, stableKey, targetSelectionKey, targetStableKey))
                continue;
            if (!HasProjectorGeometry(data))
                continue;
            if (targetData != null && !GeometriesIntersect(targetData, data))
                continue;
            if (requireWeights && (data.boneWeights == null || data.boneWeights.Length == 0))
                continue;

            result.Add(data);
        }

        return result;
    }

    private bool TryRemapProjectorWeight(
        BoneWeight sourceWeight,
        LayerVertexData sourceData,
        LayerVertexData targetData,
        out BoneWeight targetWeight,
        out bool expandedTargetBones)
    {
        targetWeight = default;
        expandedTargetBones = false;
        if (sourceData == null || targetData == null)
            return false;
        if (sourceData.spriteBoneToRendererBone == null || sourceData.spriteBoneToRendererBone.Length == 0)
            return false;

        if (targetData.spriteBoneToRendererBone == null)
            targetData.spriteBoneToRendererBone = Array.Empty<int>();
        if (targetData.bindBoneInverseMatrices == null)
            targetData.bindBoneInverseMatrices = Array.Empty<Matrix4x4>();

        var rendererToTargetSpriteBone = BuildRendererToSpriteBoneMap(targetData.spriteBoneToRendererBone);
        if (!EnsureProjectorWeightTargetBones(
                sourceWeight,
                sourceData,
                targetData,
                rendererToTargetSpriteBone,
                out expandedTargetBones))
            return false;

        var accum = new Dictionary<int, float>();
        var activeSourceBones = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(sourceWeight, sourceData.spriteBoneToRendererBone);
        AccumulateRemappedProjectorWeight(accum, sourceData, rendererToTargetSpriteBone, sourceWeight.boneIndex0, sourceWeight.weight0);
        AccumulateRemappedProjectorWeight(accum, sourceData, rendererToTargetSpriteBone, sourceWeight.boneIndex1, sourceWeight.weight1);
        AccumulateRemappedProjectorWeight(accum, sourceData, rendererToTargetSpriteBone, sourceWeight.boneIndex2, sourceWeight.weight2);
        AccumulateRemappedProjectorWeight(accum, sourceData, rendererToTargetSpriteBone, sourceWeight.boneIndex3, sourceWeight.weight3);

        if (accum.Count == 0)
            return false;

        var topBones = new int[4];
        var topWeights = new float[4];
        foreach (var kv in accum)
            InsertTopWeight(topBones, topWeights, kv.Key, kv.Value);

        var sum = topWeights[0] + topWeights[1] + topWeights[2] + topWeights[3];
        if (sum <= 0.0001f)
            return false;

        var inv = 1f / sum;
        targetWeight = new BoneWeight
        {
            boneIndex0 = topBones[0],
            boneIndex1 = topBones[1],
            boneIndex2 = topBones[2],
            boneIndex3 = topBones[3],
            weight0 = topWeights[0] * inv,
            weight1 = topWeights[1] * inv,
            weight2 = topWeights[2] * inv,
            weight3 = topWeights[3] * inv
        };
        PreserveRemappedProjectorIdentitySlots(ref targetWeight, activeSourceBones, sourceData, rendererToTargetSpriteBone);
        return true;
    }

    private bool EnsureProjectorWeightTargetBones(
        BoneWeight sourceWeight,
        LayerVertexData sourceData,
        LayerVertexData targetData,
        Dictionary<int, int> rendererToTargetSpriteBone,
        out bool expandedTargetBones)
    {
        expandedTargetBones = false;
        if (sourceData == null || targetData == null || rendererToTargetSpriteBone == null)
            return false;

        var activeSourceBones = ReanimatorWeightSliderAlgorithms.BuildActiveWeightBoneList(sourceWeight, sourceData.spriteBoneToRendererBone);
        for (var i = 0; i < activeSourceBones.Count; i++)
        {
            if (!EnsureProjectorWeightTargetBone(activeSourceBones[i], sourceData, targetData, rendererToTargetSpriteBone, ref expandedTargetBones))
                return false;
        }

        return ReanimatorProjectorAlgorithms.HasUsableWeight(sourceWeight);
    }

    private bool EnsureProjectorWeightTargetBone(
        int sourceSpriteBoneIndex,
        LayerVertexData sourceData,
        LayerVertexData targetData,
        Dictionary<int, int> rendererToTargetSpriteBone,
        ref bool expandedTargetBones)
    {
        if (sourceSpriteBoneIndex < 0)
            return true;
        if (sourceData == null || sourceData.spriteBoneToRendererBone == null || sourceSpriteBoneIndex >= sourceData.spriteBoneToRendererBone.Length)
            return true;

        var rendererBoneIndex = sourceData.spriteBoneToRendererBone[sourceSpriteBoneIndex];
        if (rendererBoneIndex < 0)
            return true;
        if (!EnsureProjectorTargetBoneAndAncestors(targetData, rendererBoneIndex, rendererToTargetSpriteBone, ref expandedTargetBones))
            return false;

        return true;
    }

    private bool EnsureProjectorTargetBoneAndAncestors(
        LayerVertexData targetData,
        int rendererBoneIndex,
        Dictionary<int, int> rendererToTargetSpriteBone,
        ref bool expandedTargetBones)
    {
        if (targetData == null || rendererToTargetSpriteBone == null || rendererBoneIndex < 0)
            return false;
        if (rendererToTargetSpriteBone.ContainsKey(rendererBoneIndex))
            return true;
        if (bones == null || rendererBoneIndex >= bones.Count || bones[rendererBoneIndex] == null)
            return false;

        var parent = bones[rendererBoneIndex].parentId;
        if (parent >= 0 && parent < bones.Count &&
            !EnsureProjectorTargetBoneAndAncestors(targetData, parent, rendererToTargetSpriteBone, ref expandedTargetBones))
            return false;

        if (!TryAppendSkinningTargetBone(targetData, rendererBoneIndex, out var targetSpriteBoneIndex))
            return false;

        rendererToTargetSpriteBone[rendererBoneIndex] = targetSpriteBoneIndex;
        expandedTargetBones = true;
        return true;
    }

    private bool TryAppendSkinningTargetBone(LayerVertexData targetData, int rendererBoneIndex, out int targetSpriteBoneIndex)
    {
        targetSpriteBoneIndex = -1;
        if (targetData == null || rendererBoneIndex < 0 || bones == null || rendererBoneIndex >= bones.Count)
            return false;
        if (!TryEnsureRendererBindPoseSkinning(targetData))
            return false;

        var sourceMap = targetData.spriteBoneToRendererBone ?? Array.Empty<int>();
        var expandedMap = new int[sourceMap.Length + 1];
        Array.Copy(sourceMap, expandedMap, sourceMap.Length);
        targetSpriteBoneIndex = sourceMap.Length;
        expandedMap[targetSpriteBoneIndex] = rendererBoneIndex;

        var expandedBind = new Matrix4x4[expandedMap.Length];
        var existingBind = targetData.bindBoneInverseMatrices;
        if (existingBind != null && existingBind.Length == sourceMap.Length)
        {
            Array.Copy(existingBind, expandedBind, existingBind.Length);
        }
        else if (sourceMap.Length > 0)
        {
            if (!TryBuildRendererBindMatrices(sourceMap, out var rebuiltExistingBind))
                return false;

            Array.Copy(rebuiltExistingBind, expandedBind, Mathf.Min(rebuiltExistingBind.Length, sourceMap.Length));
        }

        if (!TryBuildBindInverseMatrixForRendererBone(targetData, rendererBoneIndex, out expandedBind[targetSpriteBoneIndex]))
            return false;

        targetData.spriteBoneToRendererBone = expandedMap;
        targetData.bindBoneInverseMatrices = expandedBind;
        targetData.usesSpriteBindPoses = false;
        targetData.skinMatrices = null;
        targetData.deformedVertices = null;
        targetData.lastPoseVersion = -1;
        return true;
    }

    private bool TryEnsureRendererBindPoseSkinning(LayerVertexData data)
    {
        if (data == null)
            return false;
        if (!data.usesSpriteBindPoses)
            return true;

        if (!TryBuildRendererBindMatrices(data.spriteBoneToRendererBone, out var rendererBindMatrices))
            return false;

        data.bindBoneInverseMatrices = rendererBindMatrices;
        data.usesSpriteBindPoses = false;
        data.skinMatrices = null;
        data.deformedVertices = null;
        data.lastPoseVersion = -1;
        return true;
    }

    private bool TryBuildRendererBindMatrices(IReadOnlyList<int> spriteBoneToRendererBone, out Matrix4x4[] bindMatrices)
    {
        bindMatrices = null;
        if (spriteBoneToRendererBone == null)
            return false;

        bindMatrices = new Matrix4x4[spriteBoneToRendererBone.Count];
        for (var i = 0; i < spriteBoneToRendererBone.Count; i++)
        {
            var rendererBoneIndex = spriteBoneToRendererBone[i];
            if (rendererBoneIndex < 0)
            {
                bindMatrices[i] = Matrix4x4.identity;
                continue;
            }

            if (!TryBuildBindInverseMatrixForRendererBone(null, rendererBoneIndex, out bindMatrices[i]))
                return false;
        }

        return true;
    }

    private static void AccumulateRemappedProjectorWeight(
        Dictionary<int, float> accum,
        LayerVertexData sourceData,
        Dictionary<int, int> rendererToTargetSpriteBone,
        int sourceSpriteBoneIndex,
        float weight)
    {
        if (accum == null || sourceData == null || rendererToTargetSpriteBone == null)
            return;
        if (weight <= 0.0001f || sourceSpriteBoneIndex < 0)
            return;
        if (sourceData.spriteBoneToRendererBone == null || sourceSpriteBoneIndex >= sourceData.spriteBoneToRendererBone.Length)
            return;

        var rendererBoneIndex = sourceData.spriteBoneToRendererBone[sourceSpriteBoneIndex];
        if (rendererBoneIndex < 0 || !rendererToTargetSpriteBone.TryGetValue(rendererBoneIndex, out var targetSpriteBoneIndex))
            return;

        AccumulateWeightChannel(accum, targetSpriteBoneIndex, weight);
    }

    private static void PreserveRemappedProjectorIdentitySlots(
        ref BoneWeight targetWeight,
        IReadOnlyList<int> activeSourceBones,
        LayerVertexData sourceData,
        IReadOnlyDictionary<int, int> rendererToTargetSpriteBone)
    {
        if (activeSourceBones == null || activeSourceBones.Count == 0 || sourceData == null || sourceData.spriteBoneToRendererBone == null || rendererToTargetSpriteBone == null)
            return;

        var indices = new[] { targetWeight.boneIndex0, targetWeight.boneIndex1, targetWeight.boneIndex2, targetWeight.boneIndex3 };
        var weights = new[] { targetWeight.weight0, targetWeight.weight1, targetWeight.weight2, targetWeight.weight3 };
        var used = new HashSet<int>();
        for (var i = 0; i < indices.Length; i++)
        {
            if (weights[i] > 0.0001f)
                used.Add(indices[i]);
        }

        for (var i = 0; i < activeSourceBones.Count; i++)
        {
            var sourceSpriteBoneIndex = activeSourceBones[i];
            if (sourceSpriteBoneIndex < 0 || sourceSpriteBoneIndex >= sourceData.spriteBoneToRendererBone.Length)
                continue;

            var rendererBoneIndex = sourceData.spriteBoneToRendererBone[sourceSpriteBoneIndex];
            if (rendererBoneIndex < 0 || !rendererToTargetSpriteBone.TryGetValue(rendererBoneIndex, out var targetSpriteBoneIndex))
                continue;
            if (!used.Add(targetSpriteBoneIndex))
                continue;

            for (var slot = 0; slot < indices.Length; slot++)
            {
                if (weights[slot] > 0.0001f)
                    continue;

                indices[slot] = targetSpriteBoneIndex;
                weights[slot] = 0f;
                break;
            }
        }

        targetWeight.boneIndex0 = indices[0];
        targetWeight.boneIndex1 = indices[1];
        targetWeight.boneIndex2 = indices[2];
        targetWeight.boneIndex3 = indices[3];
        targetWeight.weight0 = weights[0];
        targetWeight.weight1 = weights[1];
        targetWeight.weight2 = weights[2];
        targetWeight.weight3 = weights[3];
    }

    private bool TryResolveProjectorSource(string selectionKey, out string stableKey, out LayerVertexData data)
    {
        return TryResolveProjectorLayer(selectionKey, out stableKey, out data);
    }

    private bool TryResolveProjectorLayer(string selectionKey, out string stableKey, out LayerVertexData data)
    {
        stableKey = string.Empty;
        data = null;
        if (string.IsNullOrEmpty(selectionKey) || layerInfos == null)
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;
            if (!string.Equals(BuildSelectionKey(layer), selectionKey, StringComparison.OrdinalIgnoreCase))
                continue;

            stableKey = BuildKey(layer.name, layer.layerId);
            return TryGetLayerVertexDataForKeys(selectionKey, stableKey, out data) && data != null;
        }

        return false;
    }

}
#endif
