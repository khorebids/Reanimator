#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    internal bool DeleteSelectedBone()
    {
        int removed = selectedBoneIndex;
        if (removed < 0 || removed >= bones.Count || bones[removed] == null) return false;

        RegisterUndo("Delete Bone");
        var deleted = bones[removed];
        var changedLayers = new List<KeyValuePair<string, LayerVertexData>>();
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var data = pair.Value;
            var map = data?.spriteBoneToRendererBone;
            if (map == null) continue;

            int slot = Array.IndexOf(map, removed);
            if (slot >= 0)
            {
                int fallback = deleted.parentId >= 0 ? deleted.parentId : removed == 0 ? 1 : 0;
                int fallbackSlot = Array.IndexOf(map, fallback);
                if (fallback < bones.Count && fallbackSlot < 0)
                {
                    TryEnsureRendererBindPoseSkinning(data);
                    map = (int[])data.spriteBoneToRendererBone.Clone();
                    map[slot] = fallback;
                    var bind = BuildBindBoneInverseMatrices(map);
                    data.spriteBoneToRendererBone = map;
                    data.bindBoneInverseMatrices = bind;
                    data.usesSpriteBindPoses = false;
                }
                else
                {
                    var nextMap = new List<int>(map);
                    nextMap.RemoveAt(slot);
                    var previousBind = data.bindBoneInverseMatrices;
                    if (previousBind != null && previousBind.Length == map.Length)
                    {
                        var nextBind = new List<Matrix4x4>(previousBind);
                        nextBind.RemoveAt(slot);
                        data.bindBoneInverseMatrices = nextBind.ToArray();
                    }
                    else
                    {
                        data.bindBoneInverseMatrices = BuildBindBoneInverseMatrices(nextMap.ToArray());
                        data.usesSpriteBindPoses = false;
                    }
                    data.spriteBoneToRendererBone = map = nextMap.ToArray();
                    if (data.boneWeights != null)
                    {
                        var weights = new BoneWeight[data.boneWeights.Length];
                        for (int i = 0; i < weights.Length; i++)
                        {
                            var original = data.boneWeights[i];
                            var weight = ReanimatorWeightSliderAlgorithms.RemoveBoneInfluenceAndRemap(original, slot, map.Length + 1);
                            if (fallbackSlot >= 0 && weight.weight0 <= 0f &&
                                GetSpriteBoneWeight(original, slot) > 0f)
                                weight = new BoneWeight { boneIndex0 = fallbackSlot > slot ? fallbackSlot - 1 : fallbackSlot, weight0 = 1f };
                            weights[i] = weight;
                        }
                        data.boneWeights = weights;
                    }
                }
            }

            bool shifted = false;
            var remapped = (int[])map.Clone();
            for (int i = 0; i < remapped.Length; i++)
            {
                if (remapped[i] <= removed) continue;
                remapped[i]--;
                shifted = true;
            }
            if (slot < 0 && !shifted) continue;
            data.spriteBoneToRendererBone = remapped;
            changedLayers.Add(pair);
        }

        bones.RemoveAt(removed);
        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.parentId == removed)
            {
                bone.parentId = deleted.parentId;
                if (!bonesTreatAsWorldSpace)
                {
                    bone.position = deleted.position + (Vector2)(deleted.rotation * (Vector3)bone.position);
                    bone.rotation = deleted.rotation * bone.rotation;
                    bone.initialPosition = deleted.initialPosition + (Vector2)(deleted.initialRotation * (Vector3)bone.initialPosition);
                    bone.initialRotation = deleted.initialRotation * bone.initialRotation;
                }
            }
            if (bone.parentId > removed) bone.parentId--;
            bone.index = i;
        }
        selectedBoneIndex = -1;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        foreach (var pair in changedLayers)
        {
            var data = pair.Value;
            MarkSkinningWeightsChanged(data);
            MarkEditGeometryDataChanged(data, pair.Key, BuildKey(data.layer.name, data.layer.layerId));
        }
        MarkBoneStateChanged();
        return true;
    }
    internal enum CreateBoneAnchorKind
    {
        None = 0,
        Tip = 1,
        Base = 2
    }

    internal bool TryPrepareCreateBoneStart(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out Vector2 startCanvas,
        out bool shouldCreate)
    {
        startCanvas = mouseCanvas;
        shouldCreate = true;
        if (context == null || context.Coordinates == null || bones == null || bones.Count == 0)
            return false;

        if (TryFindCreateBoneAnchorAt(mouseCanvas, context.Coordinates, 12f, out var parentBoneIndex, out var anchorCanvas, out _, out _))
        {
            selectedBoneIndex = parentBoneIndex;
            startCanvas = anchorCanvas;
            shouldCreate = true;
            return true;
        }

        var hit = FindBoneHandleAt(mouseCanvas, context.Coordinates);
        if (hit.index < 0 || hit.index >= bones.Count)
            return false;

        selectedBoneIndex = hit.index;
        shouldCreate = false;
        return true;
    }

    internal bool TryGetCreateBoneParentTip(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int parentBoneIndex,
        out Vector2 tipCanvas)
    {
        parentBoneIndex = -1;
        tipCanvas = default;
        return context != null &&
               context.Coordinates != null &&
               bones != null &&
               bones.Count > 0 &&
               TryFindBoneTipAt(mouseCanvas, context.Coordinates, 12f, out parentBoneIndex, out tipCanvas);
    }

    internal bool TryGetCreateBoneParentAnchor(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int parentBoneIndex,
        out Vector2 anchorCanvas)
    {
        return TryGetCreateBoneParentAnchor(context, mouseCanvas, out parentBoneIndex, out anchorCanvas, out _);
    }

    internal bool TryGetCreateBoneParentAnchor(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int parentBoneIndex,
        out Vector2 anchorCanvas,
        out CreateBoneAnchorKind anchorKind)
    {
        parentBoneIndex = -1;
        anchorCanvas = default;
        anchorKind = CreateBoneAnchorKind.None;
        return context != null &&
               context.Coordinates != null &&
               bones != null &&
               bones.Count > 0 &&
               TryFindCreateBoneAnchorAt(mouseCanvas, context.Coordinates, 12f, out parentBoneIndex, out anchorCanvas, out anchorKind, out _);
    }

    internal bool TryGetCreateBoneParentAnchor(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int parentBoneIndex,
        out Vector2 anchorCanvas,
        out CreateBoneAnchorKind anchorKind,
        out int anchorBoneIndex)
    {
        parentBoneIndex = -1;
        anchorCanvas = default;
        anchorKind = CreateBoneAnchorKind.None;
        anchorBoneIndex = -1;
        return context != null &&
               context.Coordinates != null &&
               bones != null &&
               bones.Count > 0 &&
               TryFindCreateBoneAnchorAt(mouseCanvas, context.Coordinates, 12f, out parentBoneIndex, out anchorCanvas, out anchorKind, out anchorBoneIndex);
    }

    internal bool TryGetCreateBoneAnchorMarkerRadii(
        ReanimatorPreviewContext context,
        int anchorBoneIndex,
        CreateBoneAnchorKind anchorKind,
        out float outerRadius,
        out float innerRadius)
    {
        outerRadius = 0f;
        innerRadius = 0f;
        if (context == null || context.Coordinates == null || !IsValidBoneIndex(anchorBoneIndex))
            return false;

        var bone = bones[anchorBoneIndex];
        var startCanvas = context.Coordinates.DocumentToView(bone.worldPosition);
        var endCanvas = context.Coordinates.DocumentToView(GetBoneEnd(anchorBoneIndex));
        var lengthCanvas = Vector2.Distance(startCanvas, endCanvas);
        outerRadius = CalculateCreateBoneAnchorOuterRadius(anchorKind, lengthCanvas);
        innerRadius = CalculateCreateBoneAnchorInnerRadius(outerRadius);
        return true;
    }

    internal static float CalculateCreateBoneAnchorOuterRadius(CreateBoneAnchorKind anchorKind, float boneLengthCanvas)
    {
        boneLengthCanvas = Mathf.Max(0f, boneLengthCanvas);
        if (anchorKind == CreateBoneAnchorKind.Base)
            return Mathf.Clamp(boneLengthCanvas * 0.08f, 10f, 28f);

        return Mathf.Clamp(boneLengthCanvas * 0.045f, 5f, 14f);
    }

    internal static float CalculateCreateBoneAnchorInnerRadius(float outerRadius)
    {
        return Mathf.Clamp(Mathf.Max(0f, outerRadius) * 0.42f, 2.4f, 12f);
    }

    internal bool TryCreateBone(
        ReanimatorPreviewContext context,
        Vector2 startCanvas,
        Vector2 endCanvas,
        out int createdBoneIndex)
    {
        createdBoneIndex = -1;
        if (context == null || context.Coordinates == null)
            return false;

        var startDoc = context.Coordinates.ViewToDocument(startCanvas);
        var endDoc = context.Coordinates.ViewToDocument(endCanvas);
        var direction = endDoc - startDoc;
        var length = direction.magnitude;
        if (length <= 0.5f)
            return false;

        RegisterUndo("Create Bone");

        var parentIndex = selectedBoneIndex >= 0 && selectedBoneIndex < bones.Count ? selectedBoneIndex : -1;
        var worldRotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
        var localPosition = startDoc;
        var localRotation = worldRotation;
        if (!bonesTreatAsWorldSpace && parentIndex >= 0)
        {
            var parent = bones[parentIndex];
            var local = Quaternion.Inverse(parent.worldRotation) *
                        new Vector3(startDoc.x - parent.worldPosition.x, startDoc.y - parent.worldPosition.y, 0f);
            localPosition = new Vector2(local.x, local.y);
            localRotation = Quaternion.Inverse(parent.worldRotation) * worldRotation;
        }

        createdBoneIndex = bones.Count;
        var bone = new BoneInfo
        {
            index = createdBoneIndex,
            name = BuildUniqueBoneName(createdBoneIndex),
            parentId = parentIndex,
            position = localPosition,
            rotation = localRotation,
            initialPosition = localPosition,
            initialRotation = localRotation,
            length = length,
            depth = createdBoneIndex,
            color = CreateDefaultBoneColor(createdBoneIndex),
            visible = true
        };

        bones.Add(bone);
        selectedBoneIndex = createdBoneIndex;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        bonePoseAtRest = true;
        MarkBoneStateChanged();
        return true;
    }

    internal bool TrySplitBoneAt(
        ReanimatorPreviewContext context,
        Vector2 mouseCanvas,
        out int createdBoneIndex)
    {
        createdBoneIndex = -1;
        if (context == null || context.Coordinates == null)
            return false;
        if (!TryFindBoneSplitAt(mouseCanvas, context.Coordinates, out var boneIndex, out var splitT))
            return false;
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return false;

        var original = bones[boneIndex];
        var originalLength = Mathf.Max(0f, original.length);
        var firstLength = originalLength * splitT;
        var secondLength = originalLength - firstLength;
        if (firstLength <= 0.5f || secondLength <= 0.5f)
            return false;

        RegisterUndo("Split Bone");

        createdBoneIndex = bones.Count;
        var splitDirection3 = original.worldRotation * Vector3.right;
        var splitWorld = original.worldPosition + new Vector2(splitDirection3.x, splitDirection3.y) * firstLength;
        var newBone = new BoneInfo
        {
            index = createdBoneIndex,
            name = BuildUniqueBoneName(createdBoneIndex),
            parentId = boneIndex,
            position = bonesTreatAsWorldSpace ? splitWorld : new Vector2(firstLength, 0f),
            rotation = bonesTreatAsWorldSpace ? original.worldRotation : Quaternion.identity,
            initialPosition = bonesTreatAsWorldSpace ? splitWorld : new Vector2(firstLength, 0f),
            initialRotation = bonesTreatAsWorldSpace ? original.worldRotation : Quaternion.identity,
            length = secondLength,
            depth = createdBoneIndex,
            color = CreateDefaultBoneColor(createdBoneIndex),
            visible = true
        };

        original.length = firstLength;
        bones[boneIndex] = original;

        for (var i = 0; i < bones.Count; i++)
        {
            if (i == boneIndex)
                continue;

            var child = bones[i];
            if (child.parentId != boneIndex)
                continue;

            child.parentId = createdBoneIndex;
            if (!bonesTreatAsWorldSpace)
            {
                var childWorldPosition = child.worldPosition;
                var childWorldRotation = child.worldRotation;
                var local = Quaternion.Inverse(original.worldRotation) *
                            new Vector3(childWorldPosition.x - splitWorld.x, childWorldPosition.y - splitWorld.y, 0f);
                child.position = new Vector2(local.x, local.y);
                child.rotation = Quaternion.Inverse(original.worldRotation) * childWorldRotation;
                child.initialPosition = child.position;
                child.initialRotation = child.rotation;
            }

            bones[i] = child;
        }

        bones.Add(newBone);
        selectedBoneIndex = createdBoneIndex;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        CaptureInitialBonePoseFromCurrent();
        bonePoseAtRest = true;
        MarkBoneStateChanged();
        return true;
    }

    private bool TryFindBoneSplitAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        out int boneIndex,
        out float splitT)
    {
        boneIndex = -1;
        splitT = 0f;
        if (coordinates == null)
            return false;

        const float maxDistance = 8f;
        var maxDistanceSqr = maxDistance * maxDistance;
        var bestDistanceSqr = float.MaxValue;
        var order = boneDrawOrder.Count == bones.Count ? boneDrawOrder : null;
        var count = order != null ? order.Count : bones.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var candidateIndex = order != null ? order[i] : i;
            if (candidateIndex < 0 || candidateIndex >= bones.Count)
                continue;

            var bone = bones[candidateIndex];
            if (!bone.visible || bone.length <= 1f)
                continue;

            var start = coordinates.DocumentToView(bone.worldPosition);
            var end = coordinates.DocumentToView(GetBoneEnd(candidateIndex));
            var segment = end - start;
            var lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= 0.0001f)
                continue;

            var t = Mathf.Clamp01(Vector2.Dot(mouseCanvas - start, segment) / lengthSqr);
            if (t <= 0.08f || t >= 0.92f)
                continue;

            var projected = start + segment * t;
            var distanceSqr = (mouseCanvas - projected).sqrMagnitude;
            if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                continue;

            boneIndex = candidateIndex;
            splitT = t;
            bestDistanceSqr = distanceSqr;
        }

        return boneIndex >= 0;
    }

    private bool TryFindBoneTipAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        float maxDistance,
        out int boneIndex,
        out Vector2 tipCanvas)
    {
        boneIndex = -1;
        tipCanvas = default;
        if (coordinates == null)
            return false;

        var maxDistanceSqr = maxDistance * maxDistance;
        var bestDistanceSqr = float.MaxValue;
        var order = boneDrawOrder.Count == bones.Count ? boneDrawOrder : null;
        var count = order != null ? order.Count : bones.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var candidateIndex = order != null ? order[i] : i;
            if (candidateIndex < 0 || candidateIndex >= bones.Count)
                continue;

            var bone = bones[candidateIndex];
            if (!bone.visible)
                continue;

            var candidateTip = coordinates.DocumentToView(GetBoneEnd(candidateIndex));
            var distanceSqr = (mouseCanvas - candidateTip).sqrMagnitude;
            if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                continue;

            boneIndex = candidateIndex;
            tipCanvas = candidateTip;
            bestDistanceSqr = distanceSqr;
        }

        return boneIndex >= 0;
    }

    private bool TryFindCreateBoneAnchorAt(
        Vector2 mouseCanvas,
        PsdCoordinateSpace coordinates,
        float maxDistance,
        out int parentBoneIndex,
        out Vector2 anchorCanvas,
        out CreateBoneAnchorKind anchorKind,
        out int anchorBoneIndex)
    {
        parentBoneIndex = -1;
        anchorCanvas = default;
        anchorKind = CreateBoneAnchorKind.None;
        anchorBoneIndex = -1;
        if (coordinates == null)
            return false;

        var maxDistanceSqr = maxDistance * maxDistance;
        var bestDistanceSqr = float.MaxValue;
        var order = boneDrawOrder.Count == bones.Count ? boneDrawOrder : null;
        var count = order != null ? order.Count : bones.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var candidateIndex = order != null ? order[i] : i;
            if (candidateIndex < 0 || candidateIndex >= bones.Count)
                continue;

            var bone = bones[candidateIndex];
            if (!bone.visible)
                continue;

            TryAcceptCreateBoneAnchor(
                mouseCanvas,
                coordinates.DocumentToView(GetBoneEnd(candidateIndex)),
                candidateIndex,
                candidateIndex,
                CreateBoneAnchorKind.Tip,
                maxDistanceSqr,
                ref bestDistanceSqr,
                ref parentBoneIndex,
                ref anchorCanvas,
                ref anchorKind,
                ref anchorBoneIndex);

            TryAcceptCreateBoneAnchor(
                mouseCanvas,
                coordinates.DocumentToView(bone.worldPosition),
                IsValidBoneIndex(bone.parentId) ? bone.parentId : -1,
                candidateIndex,
                CreateBoneAnchorKind.Base,
                maxDistanceSqr,
                ref bestDistanceSqr,
                ref parentBoneIndex,
                ref anchorCanvas,
                ref anchorKind,
                ref anchorBoneIndex);
        }

        return bestDistanceSqr < float.MaxValue;
    }

    private static void TryAcceptCreateBoneAnchor(
        Vector2 mouseCanvas,
        Vector2 candidateCanvas,
        int candidateParentBoneIndex,
        int candidateAnchorBoneIndex,
        CreateBoneAnchorKind candidateAnchorKind,
        float maxDistanceSqr,
        ref float bestDistanceSqr,
        ref int parentBoneIndex,
        ref Vector2 anchorCanvas,
        ref CreateBoneAnchorKind anchorKind,
        ref int anchorBoneIndex)
    {
        var distanceSqr = (mouseCanvas - candidateCanvas).sqrMagnitude;
        if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
            return;

        parentBoneIndex = candidateParentBoneIndex;
        anchorCanvas = candidateCanvas;
        anchorKind = candidateAnchorKind;
        anchorBoneIndex = candidateAnchorBoneIndex;
        bestDistanceSqr = distanceSqr;
    }

    private bool IsValidBoneIndex(int index)
    {
        return bones != null && index >= 0 && index < bones.Count;
    }

    private string BuildUniqueBoneName(int seedIndex)
    {
        var index = Mathf.Max(0, seedIndex);
        while (true)
        {
            var candidate = $"bone_{index}";
            var exists = false;
            for (var i = 0; i < bones.Count; i++)
            {
                if (!string.Equals(bones[i].name, candidate, StringComparison.OrdinalIgnoreCase))
                    continue;

                exists = true;
                break;
            }

            if (!exists)
                return candidate;

            index++;
        }
    }

    private static Color CreateDefaultBoneColor(int index)
    {
        var hue = Mathf.Repeat(index * 0.6180339f, 1f);
        var color = Color.HSVToRGB(hue, 0.75f, 1f);
        color.a = 1f;
        return color;
    }

    public bool TryGetRendererBoneCenterCanvas(int rendererBoneIndex, Rect localRect, out Vector2 centerCanvas)
    {
        centerCanvas = default;
        if (rendererBoneIndex < 0 || rendererBoneIndex >= bones.Count)
            return false;
        if (localRect.width <= 0f || localRect.height <= 0f)
            return false;

        var docSize = cachedDocumentSize;
        if (docSize.x <= 0 || docSize.y <= 0)
            docSize = cachedTexture != null
                ? new Vector2Int(cachedTexture.width, cachedTexture.height)
                : new Vector2Int(1, 1);

        var bone = bones[rendererBoneIndex];
        var centerDoc = (bone.worldPosition + GetBoneEnd(rendererBoneIndex)) * 0.5f;
        var coordinates = new PsdCoordinateSpace(localRect, docSize, layerInfos, null, PsdPreviewLayoutMode.Skeleton);
        centerCanvas = coordinates.DocumentToView(centerDoc);
        return true;
    }

    private void BuildBoneDrawOrder()
    {
        boneDrawOrder.Clear();
        for (var i = 0; i < bones.Count; i++)
            boneDrawOrder.Add(i);

        boneDrawOrder.Sort((a, b) =>
        {
            var cmp = bones[a].depth.CompareTo(bones[b].depth);
            if (cmp != 0)
                return cmp;
            return bones[a].index.CompareTo(bones[b].index);
        });
    }

    private static string BuildBoneKey(string name, int index)
    {
        return $"{index}:{name}";
    }

    private static string GetStringProperty(SerializedProperty parent, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return string.Empty;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var rel = relativeNames[i];
            if (string.IsNullOrEmpty(rel))
                continue;
            var prop = parent.FindPropertyRelative(rel);
            if (prop == null || prop.propertyType != SerializedPropertyType.String)
                continue;
            return prop.stringValue;
        }

        return string.Empty;
    }

    private static string GetGuidStringProperty(SerializedProperty parent, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return string.Empty;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var rel = relativeNames[i];
            if (string.IsNullOrEmpty(rel))
                continue;

            var prop = parent.FindPropertyRelative(rel);
            if (prop == null)
                continue;

            if (prop.propertyType == SerializedPropertyType.String)
                return NormalizeGuidString(prop.stringValue);

            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                var guidProp = prop.FindPropertyRelative("m_GuidString")
                               ?? prop.FindPropertyRelative("guid")
                               ?? prop.FindPropertyRelative("m_Guid");
                if (guidProp != null && guidProp.propertyType == SerializedPropertyType.String)
                    return NormalizeGuidString(guidProp.stringValue);
            }
        }

        return string.Empty;
    }

    private static string NormalizeGuidString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace("-", string.Empty).ToLowerInvariant();
    }

    private static int GetIntProperty(SerializedProperty parent, int defaultValue, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return defaultValue;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var rel = relativeNames[i];
            if (string.IsNullOrEmpty(rel))
                continue;
            var prop = parent.FindPropertyRelative(rel);
            if (prop == null || prop.propertyType != SerializedPropertyType.Integer)
                continue;
            return prop.intValue;
        }

        return defaultValue;
    }

    private static float GetFloatProperty(SerializedProperty parent, float defaultValue, params string[] relativeNames)
    {
        if (parent == null || relativeNames == null)
            return defaultValue;

        for (var i = 0; i < relativeNames.Length; i++)
        {
            var rel = relativeNames[i];
            if (string.IsNullOrEmpty(rel))
                continue;
            var prop = parent.FindPropertyRelative(rel);
            if (prop == null || prop.propertyType != SerializedPropertyType.Float)
                continue;
            return prop.floatValue;
        }

        return defaultValue;
    }

    private static bool TryGetNumericPropertyValue(SerializedProperty property, out float value)
    {
        value = 0f;
        if (property == null)
            return false;

        switch (property.propertyType)
        {
            case SerializedPropertyType.Float:
                value = property.floatValue;
                return true;
            case SerializedPropertyType.Integer:
                value = property.intValue;
                return true;
            default:
                return false;
        }
    }

    private static Vector2 GetVector2FromProperty(SerializedProperty property)
    {
        if (property == null)
            return Vector2.zero;

        if (property.propertyType == SerializedPropertyType.Vector2)
            return property.vector2Value;
        if (property.propertyType == SerializedPropertyType.Vector2Int)
            return property.vector2IntValue;

        if (property.propertyType == SerializedPropertyType.Vector3)
        {
            var v3 = property.vector3Value;
            return new Vector2(v3.x, v3.y);
        }
        if (property.propertyType == SerializedPropertyType.Vector3Int)
        {
            var v3 = property.vector3IntValue;
            return new Vector2(v3.x, v3.y);
        }

        if (property.propertyType == SerializedPropertyType.Rect)
            return property.rectValue.position;
        if (property.propertyType == SerializedPropertyType.RectInt)
            return property.rectIntValue.position;

        var xProp = property.FindPropertyRelative("x");
        var yProp = property.FindPropertyRelative("y");
        if (TryGetNumericPropertyValue(xProp, out var x) && TryGetNumericPropertyValue(yProp, out var y))
            return new Vector2(x, y);

        return Vector2.zero;
    }

    private static Vector4 GetVector4FromProperty(SerializedProperty property)
    {
        if (property == null)
            return Vector4.zero;

        if (property.propertyType == SerializedPropertyType.Vector4)
            return property.vector4Value;

        var xProp = property.FindPropertyRelative("x");
        var yProp = property.FindPropertyRelative("y");
        var zProp = property.FindPropertyRelative("z");
        var wProp = property.FindPropertyRelative("w");
        if (TryGetNumericPropertyValue(xProp, out var x) &&
            TryGetNumericPropertyValue(yProp, out var y) &&
            TryGetNumericPropertyValue(zProp, out var z) &&
            TryGetNumericPropertyValue(wProp, out var w))
            return new Vector4(x, y, z, w);

        return Vector4.zero;
    }

    private static Quaternion GetQuaternionFromProperty(SerializedProperty property)
    {
        if (property == null)
            return Quaternion.identity;

        if (property.propertyType == SerializedPropertyType.Quaternion)
            return property.quaternionValue;

        var xProp = property.FindPropertyRelative("x");
        var yProp = property.FindPropertyRelative("y");
        var zProp = property.FindPropertyRelative("z");
        var wProp = property.FindPropertyRelative("w");
        if (TryGetNumericPropertyValue(xProp, out var x) &&
            TryGetNumericPropertyValue(yProp, out var y) &&
            TryGetNumericPropertyValue(zProp, out var z) &&
            TryGetNumericPropertyValue(wProp, out var w))
            return new Quaternion(x, y, z, w);

        return Quaternion.identity;
    }

    private static List<BoneInfo> CloneBones(IReadOnlyList<BoneInfo> source)
    {
        var result = new List<BoneInfo>(source != null ? source.Count : 0);
        if (source == null)
            return result;

        for (var i = 0; i < source.Count; i++)
        {
            var src = source[i];
            result.Add(new BoneInfo
            {
                index = src.index,
                name = src.name,
                parentId = src.parentId,
                position = src.position,
                rotation = src.rotation,
                initialPosition = src.initialPosition,
                initialRotation = src.initialRotation,
                length = src.length,
                depth = src.depth,
                color = src.color,
                visible = src.visible,
                worldPosition = src.worldPosition,
                worldRotation = src.worldRotation
            });
        }

        return result;
    }

    private void RecomputeBoneWorldTransforms()
    {
        var transform = new BonePreviewTransform(bonesTreatAsWorldSpace, rotateLocalBoneOffsetsByParent);
        transform.ResolveAll(bones);
    }

    private void CaptureInitialBonePoseFromCurrent()
    {
        if (bones == null || bones.Count == 0)
            return;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            bone.initialPosition = bone.position;
            bone.initialRotation = bone.rotation;
            bones[i] = bone;
        }
    }

    private float EvaluateBoneLayoutScore(Rect contentBounds, bool worldMode, bool rotateParentOffsets)
    {
        if (bones == null || bones.Count == 0)
            return float.MaxValue;

        var prevWorld = bonesTreatAsWorldSpace;
        var prevRotate = rotateLocalBoneOffsetsByParent;
        try
        {
            bonesTreatAsWorldSpace = worldMode;
            rotateLocalBoneOffsetsByParent = rotateParentOffsets;

            RecomputeBoneWorldTransforms();

            var boundsScore = ScoreBoundsFit(GetBoneWorldBounds(), contentBounds);
            if (float.IsNaN(boundsScore) || float.IsInfinity(boundsScore))
                boundsScore = float.MaxValue * 0.5f;

            // Penalize layouts where child bases are far from parent tips.
            // This helps choose the correct local/world interpretation.
            var tipError = ComputeAverageBoneTipError(GetBoneWorldPositionLocal, GetBoneWorldRotationLocal);
            var contentDiag = Mathf.Max(contentBounds.width, contentBounds.height, 1f);
            var tipPenalty = Mathf.Clamp01(tipError / contentDiag) * 2.5f;
            return boundsScore + tipPenalty;
        }
        finally
        {
            bonesTreatAsWorldSpace = prevWorld;
            rotateLocalBoneOffsetsByParent = prevRotate;
        }
    }

    private Rect GetLayerContentBoundsDoc()
    {
        if (layerInfos == null || layerInfos.Count == 0)
            return GetContentBoundsDoc();

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var usedVisibleLayers = false;
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null || !layer.visible)
                continue;
            usedVisibleLayers = true;
            min = Vector2.Min(min, layer.position);
            max = Vector2.Max(max, layer.position + new Vector2(layer.sourceRect.width, layer.sourceRect.height));
        }

        // If everything is hidden, fall back to all layers.
        if (!usedVisibleLayers)
        {
            for (var i = 0; i < layerInfos.Count; i++)
            {
                var layer = layerInfos[i];
                if (layer == null)
                    continue;
                min = Vector2.Min(min, layer.position);
                max = Vector2.Max(max, layer.position + new Vector2(layer.sourceRect.width, layer.sourceRect.height));
            }
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return GetContentBoundsDoc();

        var size = max - min;
        if (size.x <= 0f || size.y <= 0f)
            return GetContentBoundsDoc();

        return new Rect(min, size);
    }

    private bool ShouldTreatRawBonesAsWorldSpace()
    {
        if (bones == null || bones.Count == 0)
            return false;

        RecomputeBoneWorldTransforms();

        var localBounds = GetBoneWorldBounds();
        var localTipError = ComputeAverageBoneTipError(GetBoneWorldPositionLocal, GetBoneWorldRotationLocal);

        var worldBounds = GetRawWorldBoneBounds();
        var worldTipError = ComputeAverageBoneTipError(GetBoneRawPosition, GetBoneRawRotation);

        if (worldTipError + 0.01f < localTipError * 0.75f)
            return true;

        var contentBounds = GetContentBoundsDoc();
        var localBoundsScore = ScoreBoundsFit(localBounds, contentBounds);
        var worldBoundsScore = ScoreBoundsFit(worldBounds, contentBounds);
        return worldBoundsScore + 0.15f < localBoundsScore;
    }

    private void ConvertRawWorldBonesToLocal()
    {
        var transform = new BonePreviewTransform(true, rotateLocalBoneOffsetsByParent);
        transform.ConvertWorldToLocal(bones);
    }

    private Vector2 GetBoneWorldPositionLocal(int index)
    {
        if (index < 0 || index >= bones.Count)
            return Vector2.zero;
        return bones[index].worldPosition;
    }

    private Quaternion GetBoneWorldRotationLocal(int index)
    {
        if (index < 0 || index >= bones.Count)
            return Quaternion.identity;
        return bones[index].worldRotation;
    }

    private Vector2 GetBoneRawPosition(int index)
    {
        if (index < 0 || index >= bones.Count)
            return Vector2.zero;
        return bones[index].position;
    }

    private Quaternion GetBoneRawRotation(int index)
    {
        if (index < 0 || index >= bones.Count)
            return Quaternion.identity;
        return bones[index].rotation;
    }

    private float ComputeAverageBoneTipError(Func<int, Vector2> getWorldPosition, Func<int, Quaternion> getWorldRotation)
    {
        if (bones == null || bones.Count == 0 || getWorldPosition == null || getWorldRotation == null)
            return 0f;

        var total = 0f;
        var count = 0;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.parentId < 0 || bone.parentId >= bones.Count)
                continue;

            var parentPos = getWorldPosition(bone.parentId);
            var parentRot = getWorldRotation(bone.parentId);
            var parentDir3 = parentRot * Vector3.right;
            var parentDir = new Vector2(parentDir3.x, parentDir3.y);
            var expectedChildPos = parentPos + parentDir * bones[bone.parentId].length;
            var actualChildPos = getWorldPosition(i);
            total += Vector2.Distance(expectedChildPos, actualChildPos);
            count++;
        }

        return count > 0 ? total / count : 0f;
    }

    private Rect GetRawWorldBoneBounds()
    {
        if (bones == null || bones.Count == 0)
            return new Rect(0f, 0f, 0f, 0f);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < bones.Count; i++)
        {
            var basePos = bones[i].position;
            var dir3 = bones[i].rotation * Vector3.right;
            var tipPos = basePos + new Vector2(dir3.x, dir3.y) * bones[i].length;
            min = Vector2.Min(min, basePos);
            min = Vector2.Min(min, tipPos);
            max = Vector2.Max(max, basePos);
            max = Vector2.Max(max, tipPos);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return new Rect(0f, 0f, 0f, 0f);

        return new Rect(min, max - min);
    }

    private static float ScoreBoundsFit(Rect candidate, Rect content)
    {
        if (candidate.width <= 0f || candidate.height <= 0f)
            return float.MaxValue;
        if (content.width <= 0f || content.height <= 0f)
            return 0f;

        var contentDiag = Mathf.Max(content.width, content.height, 1f);
        var candidateDiag = Mathf.Max(candidate.width, candidate.height, 1e-4f);
        var centerDistance = Vector2.Distance(candidate.center, content.center) / contentDiag;
        var scalePenalty = Mathf.Abs(Mathf.Log(candidateDiag / contentDiag));
        return centerDistance + scalePenalty;
    }

    private Rect GetBoneWorldBounds()
    {
        if (bones == null || bones.Count == 0)
            return new Rect(0f, 0f, 0f, 0f);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < bones.Count; i++)
        {
            var basePos = bones[i].worldPosition;
            var tipPos = GetBoneEnd(i);
            min = Vector2.Min(min, basePos);
            min = Vector2.Min(min, tipPos);
            max = Vector2.Max(max, basePos);
            max = Vector2.Max(max, tipPos);
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return new Rect(0f, 0f, 0f, 0f);

        var size = max - min;
        return new Rect(min, size);
    }
}
#endif
