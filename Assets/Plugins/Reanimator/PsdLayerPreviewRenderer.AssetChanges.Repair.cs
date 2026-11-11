#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public partial class PsdLayerPreviewRenderer
{
    public bool RepairSpriteSheetImporterParameters(string assetPath, out int repairedGroups, out string error)
    {
        repairedGroups = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "PSD path is empty.";
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        if (importer == null)
        {
            error = "Selected asset is not a PSD importer.";
            return false;
        }

        try
        {
            ReanimatorAssetMetaPersistence.RegisterImporterUndo(importer, "Repair Sprite Sheet Importer Parameters");
            RestorePreviewTestAnimationPose();
            RestorePose();

            var layerData = GetPsdLayerData(assetPath);
            var mosaicPositionBySpriteGuid = BuildPsdLayerMosaicPositionBySpriteGuid(new SerializedObject(importer));
            var repairedDataProvider = RepairDataProviderSpriteRectsAndGeometry(
                importer,
                layerData,
                mosaicPositionBySpriteGuid,
                false,
                Vector2.one);
            if (repairedDataProvider &&
                !ApplyImmediateDataProviderChanges(importer as ISpriteEditorDataProvider, "Repair PSD Rects And Geometry", out error))
                return false;

            var repairedImporterRects = SyncImporterSpriteRectsToPsdLayerSizes(importer, layerData);
            var changed = repairedDataProvider || repairedImporterRects;
            changed |= ApplySpriteAtlasPositionsToImporter(importer);
            if (changed)
            {
                StageOldEditorCompatibleSpriteMetadata(importer, clearResliceRequest: false);
                PersistImporterMetadataAndReimport(assetPath);
                repairedGroups++;
                Rebuild(assetPath, false);
            }

            InvalidatePreviewCacheAfterImport();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool RepairDataProviderSpriteRectsAndGeometry(
        PSDImporter importer,
        IReadOnlyList<PsdLayerDataInfo> layerData,
        IReadOnlyDictionary<string, Vector2> mosaicPositionBySpriteGuid,
        bool useUniformResizeScale,
        Vector2 uniformResizeScale)
    {
        if (importer == null || layerData == null || layerData.Count == 0)
            return false;

        var dataProvider = importer as ISpriteEditorDataProvider;
        if (dataProvider == null)
            return false;

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            return false;
        }

        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null || spriteRects.Length == 0)
            return false;

        var meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        if (meshProvider == null)
            return false;

        var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
        var outlineProvider = dataProvider.GetDataProvider<ISpriteOutlineDataProvider>();
        var ppu = Mathf.Max(1e-4f, dataProvider.pixelsPerUnit);
        var layerLookup = BuildPsdLayerDataLookup(layerData);
        var layerIdByGuid = BuildLayerIdBySpriteGuid(new SerializedObject(importer));
        var changed = false;
        var rectsChanged = false;
        for (var i = 0; i < spriteRects.Length; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect == null)
                continue;

            var rect = spriteRect.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                continue;

            var desiredRect = rect;
            var guid = NormalizeGuidString(spriteRect.spriteID.ToString());
            var layerId = !string.IsNullOrEmpty(guid) && layerIdByGuid.TryGetValue(guid, out var resolvedLayerId)
                ? resolvedLayerId
                : 0;
            if (TryResolveLayerData(layerLookup, spriteRect.name, layerId, out var data))
            {
                var mosaicPosition = Vector2.zero;
                var hasMosaicPosition = !string.IsNullOrEmpty(guid) &&
                                        mosaicPositionBySpriteGuid != null &&
                                        mosaicPositionBySpriteGuid.TryGetValue(guid, out mosaicPosition);
                desiredRect = ResolveSpriteRectForPsdRepair(
                    rect,
                    data.layerSizeOnFile,
                    mosaicPosition,
                    hasMosaicPosition,
                    useUniformResizeScale,
                    uniformResizeScale);
            }

            var rectChanged = !RectsRoughlyEqual(rect, desiredRect);
            var pivotPixels = new Vector2(rect.width * spriteRect.pivot.x, rect.height * spriteRect.pivot.y);
            var vertices = meshProvider.GetVertices(spriteRect.spriteID);
            var runtimeUnitGeometry = ReanimatorImporterGeometryAlgorithms.IsLikelyRuntimeUnitImporterGeometry(
                vertices,
                rect,
                ppu,
                pivotPixels);
            if (runtimeUnitGeometry)
                vertices = ReanimatorImporterGeometryAlgorithms.ConvertRuntimeUnitVerticesToRawLocal(vertices, ppu, pivotPixels);
            if (rectChanged)
                vertices = ScaleImporterVerticesForRectResize(vertices, rect, desiredRect);
            if (runtimeUnitGeometry || rectChanged)
                meshProvider.SetVertices(spriteRect.spriteID, vertices);

            if (boneProvider != null && (runtimeUnitGeometry || rectChanged))
            {
                List<SpriteBone> bones = null;
                try
                {
                    bones = boneProvider.GetBones(spriteRect.spriteID);
                }
                catch
                {
                    bones = null;
                }

                if (bones != null && bones.Count > 0)
                {
                    if (runtimeUnitGeometry)
                        bones = ConvertRuntimeUnitBonesToRawLocal(bones, ppu, pivotPixels);
                    if (rectChanged)
                        bones = ScaleImporterBonesForRectResize(bones, rect, desiredRect);
                    boneProvider.SetBones(spriteRect.spriteID, bones);
                }
            }

            if (outlineProvider != null && rectChanged)
            {
                var outlines = outlineProvider.GetOutlines(spriteRect.spriteID);
                outlineProvider.SetOutlines(
                    spriteRect.spriteID,
                    ScaleImporterOutlinesForRectResize(outlines, rect, desiredRect));
            }

            if (rectChanged)
            {
                spriteRect.rect = desiredRect;
                spriteRects[i] = spriteRect;
                rectsChanged = true;
            }

            changed |= runtimeUnitGeometry || rectChanged;
        }

        if (rectsChanged)
            dataProvider.SetSpriteRects(spriteRects);

        return changed;
    }

    private static List<SpriteBone> ConvertRuntimeUnitBonesToRawLocal(
        IReadOnlyList<SpriteBone> bones,
        float pixelsPerUnit,
        Vector2 pivotPixels)
    {
        if (bones == null || bones.Count == 0)
            return new List<SpriteBone>();

        var ppu = Mathf.Max(1e-4f, pixelsPerUnit);
        var result = new List<SpriteBone>(bones.Count);
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var local = new Vector2(bone.position.x, bone.position.y) * ppu;
            if (bone.parentId < 0)
                local += pivotPixels;

            bone.position = new Vector3(local.x, local.y, bone.position.z);
            bone.length *= ppu;
            result.Add(bone);
        }

        return result;
    }

    private static Rect ResolveSpriteRectForPsdRepair(
        Rect serializedSpriteRect,
        Vector2Int layerSizeOnFile,
        Vector2 mosaicPosition,
        bool hasMosaicPosition,
        bool useUniformResizeScale,
        Vector2 uniformResizeScale)
    {
        var psdSizedRect = PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(
            serializedSpriteRect,
            layerSizeOnFile,
            mosaicPosition,
            hasMosaicPosition);
        if (!useUniformResizeScale)
            return psdSizedRect;
        if (RectsRoughlyEqual(serializedSpriteRect, psdSizedRect))
            return psdSizedRect;

        var scaleX = Mathf.Abs(uniformResizeScale.x);
        var scaleY = Mathf.Abs(uniformResizeScale.y);
        if (serializedSpriteRect.width <= 0f || serializedSpriteRect.height <= 0f ||
            scaleX <= 0f || scaleY <= 0f ||
            float.IsNaN(scaleX) || float.IsNaN(scaleY) ||
            float.IsInfinity(scaleX) || float.IsInfinity(scaleY))
        {
            return psdSizedRect;
        }

        if (ReanimatorAssetChangeAlgorithms.PsdLayerSizeMatchesUniformResize(
                serializedSpriteRect,
                layerSizeOnFile,
                uniformResizeScale))
        {
            return psdSizedRect;
        }

        var resized = new Rect(
            serializedSpriteRect.x,
            serializedSpriteRect.y,
            serializedSpriteRect.width * scaleX,
            serializedSpriteRect.height * scaleY);
        if (hasMosaicPosition)
            resized.position = mosaicPosition;
        return resized;
    }

    private static Vertex2DMetaData[] ScaleImporterVerticesForRectResize(
        IReadOnlyList<Vertex2DMetaData> vertices,
        Rect previousRect,
        Rect nextRect)
    {
        if (vertices == null || vertices.Count == 0)
            return Array.Empty<Vertex2DMetaData>();

        var result = new Vertex2DMetaData[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.position = PsdCoordinateSpace.ScaleSpriteLocalPointForRectResize(
                vertex.position,
                previousRect,
                nextRect);
            result[i] = vertex;
        }

        return result;
    }

    private static List<Vector2[]> ScaleImporterOutlinesForRectResize(
        IReadOnlyList<Vector2[]> outlines,
        Rect previousRect,
        Rect nextRect)
    {
        var result = new List<Vector2[]>(outlines != null ? outlines.Count : 0);
        if (outlines == null)
            return result;

        for (var outlineIndex = 0; outlineIndex < outlines.Count; outlineIndex++)
        {
            var outline = outlines[outlineIndex] ?? Array.Empty<Vector2>();
            var scaled = new Vector2[outline.Length];
            for (var pointIndex = 0; pointIndex < outline.Length; pointIndex++)
            {
                scaled[pointIndex] = PsdCoordinateSpace.ScaleSpriteLocalPointForRectResize(
                    outline[pointIndex],
                    previousRect,
                    nextRect);
            }
            result.Add(scaled);
        }

        return result;
    }

    private static List<SpriteBone> ScaleImporterBonesForRectResize(
        IReadOnlyList<SpriteBone> bones,
        Rect previousRect,
        Rect nextRect)
    {
        var result = new List<SpriteBone>(bones != null ? bones.Count : 0);
        if (bones == null)
            return result;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var position = PsdCoordinateSpace.ScaleSpriteLocalPointForRectResize(
                new Vector2(bone.position.x, bone.position.y),
                previousRect,
                nextRect);
            var direction3 = bone.rotation * Vector3.right * bone.length;
            var direction = new Vector2(direction3.x, direction3.y);
            direction = PsdCoordinateSpace.ScaleSpriteLocalVectorForRectResize(
                direction,
                previousRect,
                nextRect);

            bone.position = new Vector3(position.x, position.y, bone.position.z);
            if (direction.sqrMagnitude > 1e-8f)
            {
                bone.rotation = Quaternion.AngleAxis(
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
                    Vector3.forward);
                bone.length = direction.magnitude;
            }
            result.Add(bone);
        }

        return result;
    }

    private static bool SyncImporterSpriteRectsToPsdLayerSizes(PSDImporter importer, IReadOnlyList<PsdLayerDataInfo> layerData)
    {
        return SyncImporterSpriteRectsToPsdLayerSizes(importer, layerData, false, Vector2.one);
    }

    private static bool SyncImporterSpriteRectsToPsdLayerSizes(
        PSDImporter importer,
        IReadOnlyList<PsdLayerDataInfo> layerData,
        bool useUniformResizeScale,
        Vector2 uniformResizeScale)
    {
        if (importer == null || layerData == null || layerData.Count == 0)
            return false;

        var so = new SerializedObject(importer);
        var mosaicPositionBySpriteGuid = BuildPsdLayerMosaicPositionBySpriteGuid(so);
        var changed = false;
        var desiredPartRectByGuid = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var desiredAtlasPositionByGuid = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        var spriteLists = ResolveSpriteLists(so);
        for (var listIndex = 0; listIndex < spriteLists.Count; listIndex++)
        {
            var spriteList = spriteLists[listIndex];
            if (spriteList == null || !spriteList.isArray)
                continue;

            changed |= SyncSpriteListRectsToPsdLayerSizes(
                spriteList,
                layerData,
                mosaicPositionBySpriteGuid,
                desiredPartRectByGuid,
                desiredAtlasPositionByGuid,
                useUniformResizeScale,
                uniformResizeScale);
        }

        changed |= SyncCharacterPartRectsToSpriteImportData(so, desiredPartRectByGuid);
        changed |= SyncPsdLayerMosaicPositionsToSpriteImportData(so, desiredAtlasPositionByGuid);
        if (changed)
            changed |= RequestPsdLayerReslice(so);
        if (!changed)
            return false;

        ApplyImporterSerializedProperties(so, importer);
        return true;
    }

    private static bool RequestPsdLayerReslice(SerializedObject importer)
    {
        if (importer == null)
            return false;

        var changed = false;
        changed |= WriteBoolOrIntIfChanged(importer.FindProperty("m_ResliceFromLayer") ?? importer.FindProperty("resliceFromLayer"), true);
        changed |= WriteBoolOrIntIfChanged(importer.FindProperty("m_SpriteSizeExpandChanged") ?? importer.FindProperty("spriteSizeExpandChanged"), true);
        return changed;
    }

    private static bool ClearPsdLayerResliceRequest(SerializedObject importer)
    {
        return ReanimatorOldEditorMetadataStager.ClearPsdLayerResliceRequest(importer);
    }

    private static bool StageOldEditorCompatibleSpriteMetadata(
        PSDImporter importer,
        bool clearResliceRequest,
        bool syncPsdLayerMosaicPositions = true)
    {
        return ReanimatorOldEditorMetadataStager.Stage(importer, clearResliceRequest, syncPsdLayerMosaicPositions);
    }

    private static bool SyncCharacterPartRectsFromCurrentSpriteImportData(SerializedObject importer)
    {
        return ReanimatorOldEditorMetadataStager.SyncCharacterPartRectsFromCurrentSpriteImportData(importer);
    }

    private static void CollectCharacterPartRectsFromSpriteList(
        SerializedProperty spriteList,
        Dictionary<string, Rect> desiredPartRectByGuid)
    {
        ReanimatorOldEditorMetadataStager.CollectCharacterPartRectsFromSpriteList(spriteList, desiredPartRectByGuid);
    }

    private static bool SyncPsdLayerMosaicPositionsFromCurrentSpriteImportData(SerializedObject importer)
    {
        return ReanimatorOldEditorMetadataStager.SyncPsdLayerMosaicPositionsFromCurrentSpriteImportData(importer);
    }

    private static bool SyncSpriteListRectsToPsdLayerSizes(
        SerializedProperty spriteList,
        IReadOnlyList<PsdLayerDataInfo> layerData,
        IReadOnlyDictionary<string, Vector2> mosaicPositionBySpriteGuid,
        Dictionary<string, Rect> desiredPartRectByGuid,
        Dictionary<string, Vector2> desiredAtlasPositionByGuid,
        bool useUniformResizeScale,
        Vector2 uniformResizeScale)
    {
        var layerLookup = BuildPsdLayerDataLookup(layerData);
        var changed = false;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var spriteElement = spriteList.GetArrayElementAtIndex(i);
            if (spriteElement == null)
                continue;

            var nameProp = spriteElement.FindPropertyRelative("m_Name") ?? spriteElement.FindPropertyRelative("name");
            var name = nameProp != null ? nameProp.stringValue : string.Empty;
            if (!TryResolveLayerData(layerLookup, name, ReadLayerIdFromSpriteElement(spriteElement), out var data))
                continue;

            var rectProp = spriteElement.FindPropertyRelative("m_Rect") ?? spriteElement.FindPropertyRelative("rect");
            var rect = ReadRectProperty(rectProp, Rect.zero);
            var guid = NormalizeGuidString(GetGuidStringProperty(spriteElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
            var mosaicPosition = Vector2.zero;
            var hasMosaicPosition = !string.IsNullOrEmpty(guid) &&
                                    mosaicPositionBySpriteGuid != null &&
                                    mosaicPositionBySpriteGuid.TryGetValue(guid, out mosaicPosition);
            var desiredRect = ResolveSpriteRectForPsdRepair(
                rect,
                data.layerSizeOnFile,
                mosaicPosition,
                hasMosaicPosition,
                useUniformResizeScale,
                uniformResizeScale);
            if (desiredRect.width <= 0f || desiredRect.height <= 0f)
                continue;

            changed |= WriteRectIfChanged(rectProp, desiredRect);
            changed |= SyncSpriteUvTransformIfChanged(spriteElement, desiredRect.position);

            if (!string.IsNullOrEmpty(guid))
            {
                desiredAtlasPositionByGuid[guid] = desiredRect.position;
                var positionProp = spriteElement.FindPropertyRelative("spritePosition") ?? spriteElement.FindPropertyRelative("m_SpritePosition");
                var spritePosition = positionProp != null ? GetVector2FromProperty(positionProp) : Vector2.zero;
                var partRect = ReanimatorAssetChangeAlgorithms.BuildCharacterPartSpritePosition(spritePosition, desiredRect);
                desiredPartRectByGuid[guid] = new Rect(partRect.x, partRect.y, partRect.width, partRect.height);
            }
        }

        return changed;
    }

    private static Dictionary<string, Vector2> BuildPsdLayerMosaicPositionBySpriteGuid(SerializedObject importer)
    {
        var result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        if (importer == null)
            return result;

        CollectPsdLayerMosaicPositions(importer.FindProperty("m_RigPSDLayers"), result);
        CollectPsdLayerMosaicPositions(importer.FindProperty("rigPSDLayers"), result);
        CollectPsdLayerMosaicPositions(importer.FindProperty("m_SharedRigPSDLayers"), result);
        CollectPsdLayerMosaicPositions(importer.FindProperty("sharedRigPSDLayers"), result);
        CollectPsdLayerMosaicPositions(importer.FindProperty("m_MosaicPSDLayers"), result);
        CollectPsdLayerMosaicPositions(importer.FindProperty("mosaicPSDLayers"), result);
        return result;
    }

    private static void CollectPsdLayerMosaicPositions(
        SerializedProperty psdLayerList,
        Dictionary<string, Vector2> destination)
    {
        if (psdLayerList == null || !psdLayerList.isArray || destination == null)
            return;

        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var layer = psdLayerList.GetArrayElementAtIndex(i);
            if (layer == null)
                continue;

            var guid = NormalizeGuidString(GetGuidStringProperty(layer, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
            if (string.IsNullOrEmpty(guid) || destination.ContainsKey(guid))
                continue;

            var positionProperty = layer.FindPropertyRelative("m_MosaicPosition") ?? layer.FindPropertyRelative("mosaicPosition");
            if (positionProperty != null)
                destination[guid] = GetVector2FromProperty(positionProperty);
        }
    }

    private static bool SyncPsdLayerMosaicPositionsToSpriteImportData(
        SerializedObject importer,
        Dictionary<string, Vector2> desiredAtlasPositionByGuid)
    {
        return ReanimatorOldEditorMetadataStager.SyncPsdLayerMosaicPositionsToSpriteImportData(importer, desiredAtlasPositionByGuid);
    }

    private static bool SyncPsdLayerMosaicPositionListToSpriteImportData(
        SerializedProperty psdLayerList,
        Dictionary<string, Vector2> desiredAtlasPositionByGuid)
    {
        if (psdLayerList == null || !psdLayerList.isArray)
            return false;

        var changed = false;
        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var psdLayerElement = psdLayerList.GetArrayElementAtIndex(i);
            if (psdLayerElement == null)
                continue;

            var guid = NormalizeGuidString(GetGuidStringProperty(psdLayerElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
            if (string.IsNullOrEmpty(guid) || !desiredAtlasPositionByGuid.TryGetValue(guid, out var desiredAtlasPosition))
                continue;

            changed |= SyncSpriteMosaicPositionIfChanged(psdLayerElement, desiredAtlasPosition);
        }

        return changed;
    }

    private static bool MarkPsdLayersAsCustomSpriteImportData(
        SerializedObject importer,
        Dictionary<string, Vector2> changedSpriteImportDataByGuid)
    {
        if (importer == null || changedSpriteImportDataByGuid == null || changedSpriteImportDataByGuid.Count == 0)
            return false;

        var changed = false;
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("m_RigPSDLayers"), changedSpriteImportDataByGuid, true);
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("rigPSDLayers"), changedSpriteImportDataByGuid, true);
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("m_SharedRigPSDLayers"), changedSpriteImportDataByGuid, true);
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("sharedRigPSDLayers"), changedSpriteImportDataByGuid, true);
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("m_MosaicPSDLayers"), changedSpriteImportDataByGuid, true);
        changed |= MarkPsdLayerImportedStateList(importer.FindProperty("mosaicPSDLayers"), changedSpriteImportDataByGuid, true);
        return changed;
    }

    private static bool MarkPsdLayerImportedStateList(
        SerializedProperty psdLayerList,
        Dictionary<string, Vector2> changedSpriteImportDataByGuid,
        bool isImported)
    {
        if (psdLayerList == null || !psdLayerList.isArray)
            return false;

        var changed = false;
        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var psdLayerElement = psdLayerList.GetArrayElementAtIndex(i);
            if (psdLayerElement == null)
                continue;

            var guid = NormalizeGuidString(GetGuidStringProperty(psdLayerElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId"));
            if (string.IsNullOrEmpty(guid) || !changedSpriteImportDataByGuid.ContainsKey(guid))
                continue;

            changed |= WriteBoolOrIntIfChanged(
                psdLayerElement.FindPropertyRelative("isImported") ?? psdLayerElement.FindPropertyRelative("m_IsImported"),
                isImported);
        }

        return changed;
    }

    private static bool SyncPsdLayerImportStateToDirtyLayerRecords(SerializedObject importer, IReadOnlyList<DirtyLayerRecord> records)
    {
        if (importer == null || records == null || records.Count == 0)
            return false;

        var changed = false;
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("m_RigPSDLayers"), records);
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("rigPSDLayers"), records);
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("m_SharedRigPSDLayers"), records);
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("sharedRigPSDLayers"), records);
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("m_MosaicPSDLayers"), records);
        changed |= SyncPsdLayerImportStateListToDirtyLayerRecords(importer.FindProperty("mosaicPSDLayers"), records);
        return changed;
    }

    private static bool SyncPsdLayerImportStateListToDirtyLayerRecords(
        SerializedProperty psdLayerList,
        IReadOnlyList<DirtyLayerRecord> records)
    {
        if (psdLayerList == null || !psdLayerList.isArray || records == null || records.Count == 0)
            return false;

        var changed = false;
        for (var i = 0; i < psdLayerList.arraySize; i++)
        {
            var psdLayerElement = psdLayerList.GetArrayElementAtIndex(i);
            if (psdLayerElement == null)
                continue;

            var guid = GetGuidStringProperty(psdLayerElement, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            var record = ResolveDirtyLayerRecordFromPsdLayer(psdLayerElement, records, guid);
            if (record == null)
                continue;

            changed |= WriteBoolOrIntIfChanged(
                psdLayerElement.FindPropertyRelative("isImported") ?? psdLayerElement.FindPropertyRelative("m_IsImported"),
                true);
        }

        return changed;
    }

    private static DirtyLayerRecord ResolveDirtyLayerRecordFromPsdLayer(
        SerializedProperty psdLayerElement,
        IReadOnlyList<DirtyLayerRecord> records,
        string guid)
    {
        if (psdLayerElement == null || records == null || records.Count == 0)
            return null;

        var normalizedGuid = NormalizeGuidString(guid);
        var layerIdProp = psdLayerElement.FindPropertyRelative("m_LayerID") ??
                          psdLayerElement.FindPropertyRelative("layerID") ??
                          psdLayerElement.FindPropertyRelative("layerId");
        var layerId = layerIdProp != null && layerIdProp.propertyType == SerializedPropertyType.Integer
            ? layerIdProp.intValue
            : 0;
        var stableKey = layerId != 0 ? BuildKey(string.Empty, layerId) : string.Empty;

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var layer = record != null ? record.layer : null;
            if (layer == null)
                continue;

            var layerGuid = NormalizeGuidString(layer.spriteGuid);
            if (!string.IsNullOrEmpty(normalizedGuid) &&
                !string.IsNullOrEmpty(layerGuid) &&
                string.Equals(normalizedGuid, layerGuid, StringComparison.OrdinalIgnoreCase))
                return record;

            var layerStableKey = layer.layerId != 0 ? BuildKey(layer.name, layer.layerId) : string.Empty;
            if (!string.IsNullOrEmpty(stableKey) &&
                !string.IsNullOrEmpty(layerStableKey) &&
                string.Equals(stableKey, layerStableKey, StringComparison.Ordinal))
                return record;
        }

        return null;
    }

    private static Dictionary<string, Queue<PsdLayerDataInfo>> BuildPsdLayerDataQueuesByName(IReadOnlyList<PsdLayerDataInfo> layerData)
    {
        var result = new Dictionary<string, Queue<PsdLayerDataInfo>>(StringComparer.OrdinalIgnoreCase);
        if (layerData == null)
            return result;

        for (var i = 0; i < layerData.Count; i++)
        {
            var data = layerData[i];
            if (data.isGroup || string.IsNullOrEmpty(data.name))
                continue;
            if (!result.TryGetValue(data.name, out var queue))
            {
                queue = new Queue<PsdLayerDataInfo>();
                result[data.name] = queue;
            }
            queue.Enqueue(data);
        }

        return result;
    }

    private static bool SyncCharacterPartRectsToSpriteImportData(
        SerializedObject importer,
        Dictionary<string, Rect> desiredPartRectByGuid)
    {
        return ReanimatorOldEditorMetadataStager.SyncCharacterPartRectsToSpriteImportData(importer, desiredPartRectByGuid);
    }
}
#endif
