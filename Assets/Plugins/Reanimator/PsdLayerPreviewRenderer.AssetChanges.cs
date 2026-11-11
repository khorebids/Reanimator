#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    public void Revert(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        selectedBoneIndex = -1;
        isRotating = false;
        isDragging = false;
        isResizingBone = false;
        boneTransformChangedDuringDrag = false;
        EndEditGeometryInteraction(true);
        hasPendingChanges = false;
        ClearDirtyLayerSpriteState();
        elbowLines.Clear();
        limbLines.Clear();
        EndLimbInteraction();
        ClearBoobPoint();
        curveLayerStableKey = string.Empty;
        curveSelectedLoopPositions.Clear();
        curveGraphSelectionSignature = string.Empty;
        curveEditedChildWeights.Clear();
        ClearCurveSecondBoneSelection();
        curveIsDragging = false;
        curveDragStartCanvas = Vector2.zero;
        curveDragCurrentCanvas = Vector2.zero;
        curveControlId = 0;
        curveDragLayerSelectionKey = string.Empty;
        CancelVolumeCurveTool();
        weightSliderSelectedVertexIndices.Clear();
        weightSliderSelectedLayerStableKey = string.Empty;
        weightSliderIsRectSelecting = false;
        weightSliderRectToggleSelection = false;
        weightSliderControlId = 0;
        weightSliderIsWeightDragging = false;
        weightSliderWeightDragLayerStableKey = string.Empty;
        weightSliderWeightDragVertexIndex = -1;
        weightSliderWeightDragBones = Array.Empty<int>();
        weightSliderWeightDragStates = Array.Empty<WeightSliderVertexDragState>();
        weightSliderWeightDragStartCanvas = Vector2.zero;
        weightSliderWeightDragMoved = false;
        RemoveAutoGeometryPatchesForAsset(assetPath);
        RemoveAppliedVerticesForAsset(assetPath);
        Rebuild(assetPath, false);
    }

    public void RefreshCachedAssetAfterUndoRedo()
    {
        if (string.IsNullOrWhiteSpace(cachedPath))
            return;

        var currentHash = AssetDatabase.GetAssetDependencyHash(cachedPath).ToString();
        if (!hasPendingChanges && !string.Equals(cachedAssetDependencyHash, currentHash, StringComparison.Ordinal))
        {
            Rebuild(cachedPath);
            return;
        }

        if (cachedTexture == null)
            cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(cachedPath);
        RestoreRuntimeStateAfterSerializedUndo();
    }

    private void RestoreRuntimeStateAfterSerializedUndo()
    {
        bones ??= new List<BoneInfo>();
        layerInfos ??= new List<LayerInfo>();
        layerHierarchyNodes ??= new List<LayerHierarchyNode>();

        BuildDrawOrder();
        BuildBoneDrawOrder();
        boneOrderVersion++;
        RecomputeBoneWorldTransforms();
        var restoredPoseAtRest = bonePoseAtRest;
        InvalidateBonePose();
        bonePoseAtRest = restoredPoseAtRest;

        // LayerVertexData is a runtime cache and is intentionally not serialized by
        // Unity Undo. Rebuild it from the importer, then reapply the serialized
        // pending patches restored by Undo/Redo. Without this, an operation such as
        // Joint V2 could restore its bones and patch list while leaving the applied
        // topology, weights and bind matrices visible in the live cache.
        BuildVertices(cachedPath);
        InvalidateRadiusCullPreviewCache(true);

        if (selectedBoneIndex >= bones.Count)
            selectedBoneIndex = -1;
        if (!LayerExistsBySelectionKey(selectedLayerSelectionKey))
            selectedLayerSelectionKey = string.Empty;
    }

    private bool HasPendingMeshWorkForAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var normalizedPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalizedPath))
            return false;

        if (autoGeometryPatches != null)
        {
            for (var i = 0; i < autoGeometryPatches.Count; i++)
            {
                var patch = autoGeometryPatches[i];
                if (patch == null)
                    continue;
                if (IsAssetPathMatch(patch.assetPath, normalizedPath))
                    return true;
            }
        }

        if (appliedElbowVertices != null)
        {
            MigrateLegacyAppliedVerticesToAsset(normalizedPath);
            for (var i = 0; i < appliedElbowVertices.Count; i++)
            {
                var point = appliedElbowVertices[i];
                if (point == null)
                    continue;
                if (IsAssetPathMatch(point.assetPath, normalizedPath))
                    return true;
            }
        }

        return false;
    }

    private bool ApplyLayerPositionsToImporter(PSDImporter importer, DirtyLayerLookup lookup = null)
    {
        if (importer == null || layerInfos == null || layerInfos.Count == 0)
            return false;
        if (!HasDirtyLayerPositionState())
            return false;

        lookup ??= BuildDirtyLayerLookup(positionState: true);

        var so = new SerializedObject(importer);
        var changed = false;

        for (var rootIndex = 0; rootIndex < BoneCharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = so.FindProperty(BoneCharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var partsProp = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
            if (partsProp == null || !partsProp.isArray)
                continue;

            var handledGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < partsProp.arraySize; i++)
            {
                var part = partsProp.GetArrayElementAtIndex(i);
                if (part == null)
                    continue;

                var guid = NormalizeGuidString(GetGuidStringProperty(part, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID"));
                if (string.IsNullOrEmpty(guid) || !lookup.byGuid.TryGetValue(guid, out var record))
                    continue;

                handledGuids.Add(guid);
                changed |= WriteRectIntPositionIfChanged(
                    part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition"),
                    record.position);
            }

            foreach (var pair in lookup.byGuid)
            {
                var guid = NormalizeGuidString(pair.Key);
                var record = pair.Value;
                if (string.IsNullOrEmpty(guid) || record == null || handledGuids.Contains(guid))
                    continue;

                var index = partsProp.arraySize;
                partsProp.InsertArrayElementAtIndex(index);
                var part = partsProp.GetArrayElementAtIndex(index);
                if (part == null)
                    continue;

                changed |= WriteStringIfChanged(part.FindPropertyRelative("spriteId") ?? part.FindPropertyRelative("m_SpriteId"), guid);
                changed |= WriteRectIntIfChanged(
                    part.FindPropertyRelative("spritePosition") ?? part.FindPropertyRelative("m_SpritePosition"),
                    record.position,
                    record.sourceRect.size);
                var bonesProp = part.FindPropertyRelative("bones") ?? part.FindPropertyRelative("m_Bones");
                if (bonesProp != null && bonesProp.isArray && bonesProp.arraySize != 0)
                {
                    bonesProp.ClearArray();
                    changed = true;
                }
                changed |= WriteIntIfChanged(part.FindPropertyRelative("parentGroup") ?? part.FindPropertyRelative("m_ParentGroup"), -1);
                changed |= WriteIntIfChanged(part.FindPropertyRelative("order") ?? part.FindPropertyRelative("m_Order"), index);
                handledGuids.Add(guid);
            }
        }

        var spriteLists = ResolveSpriteLists(so);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            var layerIndex = 0;
            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(i);
                if (spriteElement == null)
                    continue;

                var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
                var record = ResolveDirtyLayerFromSerializedSprite(spriteElement, lookup, guid);
                layerIndex++;
                if (record == null)
                    continue;

                var positionProp = spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition");
                changed |= WriteVector2IfChanged(positionProp, record.position);
            }
        }

        changed |= ApplyDirtyLayerPositionsToSpriteLists(so, lookup);
        changed |= ClearPsdLayerResliceRequest(so);
        if (!changed)
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    private static bool ApplyDirtyLayerPositionsToSpriteLists(SerializedObject importer, DirtyLayerLookup lookup)
    {
        if (importer == null || lookup == null)
            return false;

        if ((lookup.byGuid == null || lookup.byGuid.Count == 0) &&
            (lookup.byStableKey == null || lookup.byStableKey.Count == 0))
            return false;

        var changed = false;
        var spriteLists = ResolveSpriteLists(importer);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(i);
                if (spriteElement == null)
                    continue;

                var guid = NormalizeGuidString(GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
                var record = ResolveDirtyLayerFromSerializedSprite(spriteElement, lookup, guid);
                if (record == null)
                    continue;

                changed = true;
                var positionProp = spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition");
                var positionChanged = WriteVector2IfChanged(positionProp, record.position);
                changed |= positionChanged;
            }
        }

        return changed;
    }

    private static bool SyncSerializedSpriteBoneNamesFromCharacterData(PSDImporter importer)
    {
        if (importer == null)
            return false;

        var so = new SerializedObject(importer);
        var syncData = BuildSerializedCharacterBoneNameSyncData(so);
        if (!syncData.HasAnyNames)
            return false;

        var changed = SyncSerializedSpriteBoneNamesFromCharacterData(so, syncData);
        if (!changed)
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    internal static bool SyncSpriteBoneNamesFromCharacterData(
        IReadOnlyList<SpriteBone> characterBones,
        IReadOnlyList<SpriteBone> spriteBones,
        out List<SpriteBone> syncedBones)
    {
        return SyncSpriteBoneNamesFromCharacterData(characterBones, spriteBones, null, out syncedBones);
    }

    internal static bool SyncSpriteBoneNamesFromCharacterData(
        IReadOnlyList<SpriteBone> characterBones,
        IReadOnlyList<SpriteBone> spriteBones,
        IReadOnlyList<int> characterBoneIndices,
        out List<SpriteBone> syncedBones)
    {
        syncedBones = spriteBones != null ? new List<SpriteBone>(spriteBones) : new List<SpriteBone>();
        var boneNameByGuid = BuildUniqueSpriteBoneNameByGuid(characterBones);
        if (((characterBones == null || characterBones.Count == 0) && boneNameByGuid.Count == 0) ||
            syncedBones.Count == 0)
        {
            return false;
        }

        var changed = false;
        for (var i = 0; i < syncedBones.Count; i++)
        {
            var spriteBone = syncedBones[i];
            var desiredName = ResolveCharacterBoneName(characterBones, characterBoneIndices, i);
            var guid = NormalizeGuidString(spriteBone.guid);
            if (string.IsNullOrWhiteSpace(desiredName) &&
                !string.IsNullOrEmpty(guid) &&
                boneNameByGuid.TryGetValue(guid, out var guidName))
            {
                desiredName = guidName;
            }

            if (string.IsNullOrWhiteSpace(desiredName) ||
                string.Equals(spriteBone.name, desiredName, StringComparison.Ordinal))
            {
                continue;
            }

            spriteBone.name = desiredName;
            syncedBones[i] = spriteBone;
            changed = true;
        }

        return changed;
    }

    private static string ResolveCharacterBoneName(
        IReadOnlyList<SpriteBone> characterBones,
        IReadOnlyList<int> characterBoneIndices,
        int spriteBoneIndex)
    {
        if (characterBones == null ||
            characterBoneIndices == null ||
            spriteBoneIndex < 0 ||
            spriteBoneIndex >= characterBoneIndices.Count)
        {
            return string.Empty;
        }

        var characterBoneIndex = characterBoneIndices[spriteBoneIndex];
        if (characterBoneIndex < 0 || characterBoneIndex >= characterBones.Count)
            return string.Empty;

        return characterBones[characterBoneIndex].name;
    }

    private static Dictionary<string, string> BuildUniqueSpriteBoneNameByGuid(IReadOnlyList<SpriteBone> bones)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bones == null || bones.Count == 0)
            return result;

        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var guid = NormalizeGuidString(bone.guid);
            if (string.IsNullOrEmpty(guid) || string.IsNullOrWhiteSpace(bone.name))
                continue;

            if (result.ContainsKey(guid))
            {
                result.Remove(guid);
                duplicates.Add(guid);
                continue;
            }

            if (!duplicates.Contains(guid))
                result[guid] = bone.name;
        }

        return result;
    }

    private static CharacterBoneNameSyncData BuildSerializedCharacterBoneNameSyncData(SerializedObject importer)
    {
        var result = new CharacterBoneNameSyncData();
        if (importer == null)
            return result;

        for (var rootIndex = 0; rootIndex < BoneCharacterDataRootNames.Length; rootIndex++)
        {
            var characterData = importer.FindProperty(BoneCharacterDataRootNames[rootIndex]);
            if (characterData == null)
                continue;

            var bonesProp = characterData.FindPropertyRelative("bones") ?? characterData.FindPropertyRelative("m_Bones");
            if (bonesProp == null || !bonesProp.isArray)
                continue;
            if (bonesProp.arraySize == 0)
                continue;

            result.BoneNames = new string[bonesProp.arraySize];
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < bonesProp.arraySize; i++)
            {
                var boneProp = bonesProp.GetArrayElementAtIndex(i);
                var guid = NormalizeGuidString(GetGuidStringProperty(boneProp, "guid", "m_Guid"));
                var nameProp = boneProp != null ? boneProp.FindPropertyRelative("name") ?? boneProp.FindPropertyRelative("m_Name") : null;
                var name = nameProp != null ? nameProp.stringValue : string.Empty;
                result.BoneNames[i] = name;
                if (string.IsNullOrEmpty(guid) || string.IsNullOrWhiteSpace(name))
                    continue;

                if (result.UniqueNameByGuid.ContainsKey(guid))
                {
                    result.UniqueNameByGuid.Remove(guid);
                    duplicates.Add(guid);
                    continue;
                }

                if (!duplicates.Contains(guid))
                    result.UniqueNameByGuid[guid] = name;
            }

            var partsProp = characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts");
            if (partsProp != null && partsProp.isArray)
            {
                for (var partIndex = 0; partIndex < partsProp.arraySize; partIndex++)
                {
                    var part = partsProp.GetArrayElementAtIndex(partIndex);
                    var spriteGuid = NormalizeGuidString(GetGuidStringProperty(part, "spriteId", "m_SpriteId", "spriteID", "m_SpriteID"));
                    if (string.IsNullOrEmpty(spriteGuid))
                        continue;

                    var partBones = ReadIntArrayProperty(part.FindPropertyRelative("bones") ?? part.FindPropertyRelative("m_Bones"));
                    if (partBones.Count > 0)
                        result.PartBoneIndicesBySpriteGuid[spriteGuid] = partBones;
                }
            }

            return result;
        }

        return result;
    }

    private static bool SyncSerializedSpriteBoneNamesFromCharacterData(
        SerializedObject importer,
        CharacterBoneNameSyncData syncData)
    {
        if (importer == null || syncData == null || !syncData.HasAnyNames)
            return false;

        var changed = false;
        var spriteLists = ResolveSpriteLists(importer);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            for (var spriteIndex = 0; spriteIndex < spriteList.arraySize; spriteIndex++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(spriteIndex);
                if (spriteElement == null)
                    continue;

                var spriteGuid = NormalizeGuidString(GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
                syncData.PartBoneIndicesBySpriteGuid.TryGetValue(spriteGuid, out var partBoneIndices);
                var spriteBones = spriteElement.FindPropertyRelative("spriteBone") ??
                                  spriteElement.FindPropertyRelative("m_SpriteBone");
                if (spriteBones == null || !spriteBones.isArray)
                    continue;

                var canUsePartBoneIndices = partBoneIndices != null && partBoneIndices.Count == spriteBones.arraySize;
                for (var boneIndex = 0; boneIndex < spriteBones.arraySize; boneIndex++)
                {
                    var boneProp = spriteBones.GetArrayElementAtIndex(boneIndex);
                    var desiredName = canUsePartBoneIndices
                        ? ResolveSerializedCharacterBoneName(syncData.BoneNames, partBoneIndices, boneIndex)
                        : string.Empty;
                    var guid = NormalizeGuidString(GetGuidStringProperty(boneProp, "guid", "m_Guid"));
                    if (string.IsNullOrWhiteSpace(desiredName) &&
                        !string.IsNullOrEmpty(guid) &&
                        syncData.UniqueNameByGuid.TryGetValue(guid, out var guidName))
                    {
                        desiredName = guidName;
                    }

                    var nameProp = boneProp != null ? boneProp.FindPropertyRelative("name") ?? boneProp.FindPropertyRelative("m_Name") : null;
                    if (nameProp == null ||
                        string.IsNullOrWhiteSpace(desiredName) ||
                        string.Equals(nameProp.stringValue, desiredName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    nameProp.stringValue = desiredName;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static string ResolveSerializedCharacterBoneName(
        IReadOnlyList<string> boneNames,
        IReadOnlyList<int> characterBoneIndices,
        int spriteBoneIndex)
    {
        if (boneNames == null ||
            characterBoneIndices == null ||
            spriteBoneIndex < 0 ||
            spriteBoneIndex >= characterBoneIndices.Count)
        {
            return string.Empty;
        }

        var characterBoneIndex = characterBoneIndices[spriteBoneIndex];
        if (characterBoneIndex < 0 || characterBoneIndex >= boneNames.Count)
            return string.Empty;

        return boneNames[characterBoneIndex];
    }

    private static List<int> ReadIntArrayProperty(SerializedProperty property)
    {
        var result = new List<int>();
        if (property == null || !property.isArray)
            return result;

        for (var i = 0; i < property.arraySize; i++)
        {
            var element = property.GetArrayElementAtIndex(i);
            if (element != null && element.propertyType == SerializedPropertyType.Integer)
                result.Add(element.intValue);
        }

        return result;
    }

    private sealed class CharacterBoneNameSyncData
    {
        public string[] BoneNames = Array.Empty<string>();
        public readonly Dictionary<string, string> UniqueNameByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<int>> PartBoneIndicesBySpriteGuid = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        public bool HasAnyNames => (BoneNames != null && BoneNames.Length > 0) || UniqueNameByGuid.Count > 0;
    }

    public bool SetLayerSpriteName(LayerInfo layer, string name)
    {
        if (layer == null)
            return false;

        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return false;
        if (string.Equals(layer.name, name, StringComparison.Ordinal))
            return false;

        RegisterUndo("Rename Sprite");
        var oldSelectionKey = BuildSelectionKey(layer);
        MarkLayerSpriteStateDirty(layer);
        layer.name = name;
        if (string.Equals(selectedLayerSelectionKey, oldSelectionKey, StringComparison.OrdinalIgnoreCase))
            selectedLayerSelectionKey = BuildSelectionKey(layer);
        MarkLayerSpriteStateDirty(layer);
        hasPendingChanges = true;
        return true;
    }

    public bool SetLayerSpriteBorder(LayerInfo layer, Vector4 border)
    {
        if (layer == null)
            return false;

        border.x = Mathf.Max(0f, border.x);
        border.y = Mathf.Max(0f, border.y);
        border.z = Mathf.Max(0f, border.z);
        border.w = Mathf.Max(0f, border.w);
        if ((layer.border - border).sqrMagnitude <= 1e-8f)
            return false;

        RegisterUndo("Set Sprite Border");
        layer.border = border;
        MarkLayerSpriteStateDirty(layer);
        hasPendingChanges = true;
        return true;
    }

    public bool SetLayerSpritePivot(LayerInfo layer, int alignment, Vector2 pivot)
    {
        if (layer == null)
            return false;

        alignment = Mathf.Clamp(alignment, 0, 9);
        pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
        if (layer.alignment == alignment && (layer.pivot - pivot).sqrMagnitude <= 1e-8f)
            return false;

        RegisterUndo("Set Sprite Pivot");
        layer.alignment = alignment;
        layer.pivot = pivot;
        MarkLayerSpriteStateDirty(layer);
        hasPendingChanges = true;
        return true;
    }

    private bool ApplySpriteChangesToImporter(PSDImporter importer, DirtyLayerLookup lookup = null)
    {
        if (importer == null || layerInfos == null || layerInfos.Count == 0)
            return false;
        if (!HasDirtyLayerSpriteState() && lookup == null)
            return false;

        var so = new SerializedObject(importer);
        if (!ApplySpriteChangesToSerializedImporter(so, lookup))
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    private bool ApplySpriteChangesToSerializedImporter(SerializedObject so, DirtyLayerLookup lookup = null)
    {
        if (so == null || layerInfos == null || layerInfos.Count == 0)
            return false;
        if (!HasDirtyLayerSpriteState() && lookup == null)
            return false;

        lookup ??= BuildDirtyLayerLookup(positionState: false);
        var spriteLists = ResolveSpriteLists(so);
        if (spriteLists.Count == 0)
            return false;

        var changed = false;
        var desiredPartRectByGuid = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var desiredAtlasPositionByGuid = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        var appliedDirtyRecords = new List<DirtyLayerRecord>();
        var seenAppliedDirtyRecords = new HashSet<DirtyLayerRecord>();
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            var layerIndex = 0;
            for (var i = 0; i < spriteList.arraySize; i++)
            {
                var spriteElement = spriteList.GetArrayElementAtIndex(i);
                if (spriteElement == null)
                    continue;

                var guid = GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
                var record = ResolveDirtyLayerFromSerializedSprite(spriteElement, lookup, guid);
                LayerInfo layer = record != null ? record.layer : null;
                layerIndex++;

                if (record == null || layer == null)
                    continue;

                var normalizedGuid = NormalizeGuidString(guid);
                var sourceRect = record != null ? record.sourceRect : layer.sourceRect;
                var position = record != null ? record.position : layer.position;
                if (record != null && seenAppliedDirtyRecords.Add(record))
                    appliedDirtyRecords.Add(record);
                if (record != null)
                {
                    changed = true;
                    WriteRectValue(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), sourceRect);
                    WriteVector2Value(spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition"), position);
                    WriteVector2Value(spriteElement.FindPropertyRelative("m_UVTransform") ?? spriteElement.FindPropertyRelative("uvTransform"), sourceRect.position);
                }
                changed |= WriteStringIfChanged(spriteElement.FindPropertyRelative("m_Name") ?? spriteElement.FindPropertyRelative("name"), layer.name);
                if (record == null)
                {
                    changed |= WriteRectIfChanged(spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect"), sourceRect);
                    changed |= WriteVector2IfChanged(spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition"), position);
                    changed |= SyncSpriteUvTransformIfChanged(spriteElement, sourceRect.position);
                }
                if (!string.IsNullOrEmpty(normalizedGuid))
                {
                    if (!desiredAtlasPositionByGuid.ContainsKey(normalizedGuid))
                        desiredAtlasPositionByGuid[normalizedGuid] = sourceRect.position;
                    var partRect = ReanimatorAssetChangeAlgorithms.BuildCharacterPartSpritePosition(position, sourceRect);
                    if (!desiredPartRectByGuid.ContainsKey(normalizedGuid))
                        desiredPartRectByGuid[normalizedGuid] = new Rect(partRect.x, partRect.y, partRect.width, partRect.height);
                }
                changed |= WriteVector4IfChanged(spriteElement.FindPropertyRelative("m_Border") ?? spriteElement.FindPropertyRelative("border"), layer.border);
                changed |= WriteVector2IfChanged(spriteElement.FindPropertyRelative("m_Pivot") ?? spriteElement.FindPropertyRelative("pivot"), layer.pivot);
                changed |= WriteIntIfChanged(spriteElement.FindPropertyRelative("m_Alignment") ?? spriteElement.FindPropertyRelative("alignment"), layer.alignment);
            }
        }

        changed |= SyncCharacterPartRectsToSpriteImportData(so, desiredPartRectByGuid);
        changed |= MarkPsdLayersAsCustomSpriteImportData(so, desiredAtlasPositionByGuid);
        changed |= SyncPsdLayerImportStateToDirtyLayerRecords(so, appliedDirtyRecords);
        changed |= ClearPsdLayerResliceRequest(so);
        if (!changed)
            return false;

        return true;
    }

    private bool SyncSpriteRectsToDataProvider(ISpriteEditorDataProvider dataProvider, DirtyLayerLookup lookup = null)
    {
        if (dataProvider == null || layerInfos == null || layerInfos.Count == 0)
            return false;
        if (!HasDirtyLayerSpriteState() && lookup == null)
            return false;

        lookup ??= BuildDirtyLayerLookup(positionState: false);
        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null || spriteRects.Length == 0)
            return false;

        var changed = false;
        for (var i = 0; i < spriteRects.Length; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect == null)
                continue;

            LayerInfo layer = null;
            var guid = NormalizeGuidString(spriteRect.spriteID.ToString());
            var record = !string.IsNullOrEmpty(guid) && lookup.byGuid.TryGetValue(guid, out var byGuid) ? byGuid : null;

            if (record != null)
                layer = record.layer;

            if (record == null || layer == null)
                continue;

            var desiredRect = record.sourceRect;
            if (SyncSpriteRectWithLayerIfChanged(spriteRect, layer, desiredRect))
                changed = true;
        }

        if (changed)
            dataProvider.SetSpriteRects(spriteRects);

        return changed;
    }

    private static bool SyncCharacterPartPositionsToDataProvider(
        ISpriteEditorDataProvider dataProvider,
        DirtyLayerLookup lookup,
        DirtyLayerLookup spriteRectLookup = null)
    {
        if (dataProvider == null || lookup == null)
            return false;
        if (!dataProvider.HasDataProvider(typeof(ICharacterDataProvider)))
            return false;

        var dirtyRecordByGuid = new Dictionary<string, DirtyLayerRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in lookup.byGuid)
        {
            var guid = NormalizeGuidString(pair.Key);
            if (!string.IsNullOrEmpty(guid) && pair.Value != null)
                dirtyRecordByGuid[guid] = pair.Value;
        }

        var spriteRects = dataProvider.GetSpriteRects();
        var currentSpriteSizeByGuid = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        if (spriteRects != null)
        {
            for (var i = 0; i < spriteRects.Length; i++)
            {
                var spriteRect = spriteRects[i];
                if (spriteRect == null)
                    continue;

                var guid = NormalizeGuidString(spriteRect.spriteID.ToString());
                if (string.IsNullOrEmpty(guid))
                    continue;

                currentSpriteSizeByGuid[guid] = spriteRect.rect.size;
                if (lookup.byGuid.TryGetValue(guid, out var record) && record != null)
                    dirtyRecordByGuid[guid] = record;
            }
        }

        if (dirtyRecordByGuid.Count == 0)
            return false;

        var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
        if (characterProvider == null)
            return false;

        CharacterData characterData;
        try
        {
            characterData = characterProvider.GetCharacterData();
        }
        catch
        {
            return false;
        }

        var parts = characterData.parts != null
            ? new List<CharacterPart>(characterData.parts)
            : new List<CharacterPart>();
        var changed = false;
        var handledGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dirtyRecordByGuid)
        {
            var guid = NormalizeGuidString(pair.Key);
            if (string.IsNullOrEmpty(guid) || !handledGuids.Add(guid))
                continue;

            var record = pair.Value;
            if (record == null)
                continue;

            var useSpriteRectSize = IsDirtyRecordInLookup(spriteRectLookup, guid, record);
            var currentSize = useSpriteRectSize
                ? record.sourceRect.size
                : currentSpriteSizeByGuid.TryGetValue(guid, out var spriteSize)
                    ? spriteSize
                    : record.sourceRect.size;
            var spritePosition = new RectInt(
                Mathf.RoundToInt(record.position.x),
                Mathf.RoundToInt(record.position.y),
                Mathf.RoundToInt(currentSize.x),
                Mathf.RoundToInt(currentSize.y));

            var found = false;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (!string.Equals(NormalizeGuidString(part.spriteId), guid, StringComparison.OrdinalIgnoreCase))
                    continue;

                found = true;
                if (!useSpriteRectSize)
                {
                    spritePosition.width = part.spritePosition.width;
                    spritePosition.height = part.spritePosition.height;
                }

                if (RectIntsEqual(part.spritePosition, spritePosition))
                    break;

                part.spritePosition = spritePosition;
                parts[i] = part;
                changed = true;
                break;
            }

            if (found)
                continue;

            parts.Add(new CharacterPart
            {
                spriteId = guid,
                spritePosition = spritePosition,
                bones = Array.Empty<int>(),
                parentGroup = -1,
                order = parts.Count
            });
            changed = true;
        }

        if (!changed)
            return false;

        characterData.parts = parts.ToArray();
        characterProvider.SetCharacterData(characterData);
        return true;
    }

    private static bool RectIntsEqual(RectInt lhs, RectInt rhs)
    {
        return lhs.x == rhs.x &&
               lhs.y == rhs.y &&
               lhs.width == rhs.width &&
               lhs.height == rhs.height;
    }

    private static bool IsDirtyRecordInLookup(DirtyLayerLookup lookup, string guid, DirtyLayerRecord record)
    {
        return ReanimatorDirtyLayerLookupBuilder.ContainsRecord(lookup, guid, record);
    }

}

#endif

