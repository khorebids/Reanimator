#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private bool TryAutoRepairPsdImporterState(
        string assetPath,
        PSDImporter importer,
        Vector2Int previousDocumentSize,
        Vector2Int currentDocumentSize,
        out bool repaired,
        out string error)
    {
        repaired = false;
        error = null;
        // Changes to alpha can alter trimmed layer rectangles without resizing the document.
        // Only a measured document resize authorizes an automatic geometry rescale.
        if (previousDocumentSize.x <= 0 || previousDocumentSize.y <= 0 ||
            currentDocumentSize.x <= 0 || currentDocumentSize.y <= 0 ||
            previousDocumentSize == currentDocumentSize)
            return true;

        var layerData = GetPsdLayerData(assetPath);
        var mosaicPositionBySpriteGuid = BuildPsdLayerMosaicPositionBySpriteGuid(new SerializedObject(importer));
        var hasUniformLayerResize = TryDetectUniformPsdLayerResize(
            importer,
            layerData,
            mosaicPositionBySpriteGuid,
            out var detectedScale,
            out var changedLayers,
            out var comparableLayers);
        if (!ReanimatorAssetChangeAlgorithms.ShouldAutoRepairPsdImporterState(hasPendingChanges, hasUniformLayerResize))
            return true;
        var documentScale = ReanimatorAssetChangeAlgorithms.CalculatePsdDocumentResizeScale(
            previousDocumentSize, currentDocumentSize);
        if (Mathf.Abs(detectedScale.x - documentScale.x) > 0.02f ||
            Mathf.Abs(detectedScale.y - documentScale.y) > 0.02f)
            return true;

        if (importer == null)
        {
            error = "Selected asset is not a PSD importer.";
            return false;
        }

        try
        {
            ReanimatorAssetMetaPersistence.RegisterImporterUndo(importer, "Auto Rescale PSD Geometry");
            RestorePreviewTestAnimationPose();
            RestorePose();

            var repairedDataProvider = RepairDataProviderSpriteRectsAndGeometry(
                importer,
                layerData,
                mosaicPositionBySpriteGuid,
                true,
                detectedScale);
            if (repairedDataProvider &&
                !ApplyImmediateDataProviderChanges(importer as ISpriteEditorDataProvider, "Auto Rescale PSD Geometry", out error))
                return false;

            var changed = repairedDataProvider;
            changed |= ApplySpriteAtlasPositionsToImporter(importer);
            if (!changed)
                return true;

            StageOldEditorCompatibleSpriteMetadata(importer, clearResliceRequest: false);
            PersistImporterMetadataAndReimport(assetPath);
            repaired = true;

            if (previousDocumentSize.x > 0 && previousDocumentSize.y > 0 &&
                currentDocumentSize.x > 0 && currentDocumentSize.y > 0 &&
                previousDocumentSize != currentDocumentSize)
            {
                var scale = ReanimatorAssetChangeAlgorithms.CalculatePsdDocumentResizeScale(
                    previousDocumentSize,
                    currentDocumentSize);
                ReanimatorLog.Trace(
                    $"Reanimator auto-repaired PSD importer state for {assetPath}: " +
                    $"{previousDocumentSize.x}x{previousDocumentSize.y} -> {currentDocumentSize.x}x{currentDocumentSize.y} " +
                    $"(scale {scale.x:0.###}, {scale.y:0.###}).",
                    IsDebugModeEnabled);
            }
            else
            {
                ReanimatorLog.Trace(
                    $"Reanimator auto-repaired PSD importer state for {assetPath}.",
                    IsDebugModeEnabled);
            }
            ReanimatorLog.Trace(
                $"Reanimator auto-repair matched {changedLayers}/{comparableLayers} resized PSD layers " +
                $"(detected scale {detectedScale.x:0.###}, {detectedScale.y:0.###}).",
                IsDebugModeEnabled);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryDetectUniformPsdLayerResize(
        PSDImporter importer,
        IReadOnlyList<PsdLayerDataInfo> layerData,
        IReadOnlyDictionary<string, Vector2> mosaicPositionBySpriteGuid,
        out Vector2 scale,
        out int changedLayers,
        out int comparableLayers)
    {
        scale = Vector2.one;
        changedLayers = 0;
        comparableLayers = 0;
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

        var currentRects = new List<Rect>(spriteRects.Length);
        var desiredRects = new List<Rect>(spriteRects.Length);
        var layerLookup = BuildPsdLayerDataLookup(layerData);
        var layerIdByGuid = BuildLayerIdBySpriteGuid(new SerializedObject(importer));
        for (var i = 0; i < spriteRects.Length; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect == null)
                continue;

            var rect = spriteRect.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                continue;

            var guid = NormalizeGuidString(spriteRect.spriteID.ToString());
            var layerId = !string.IsNullOrEmpty(guid) && layerIdByGuid.TryGetValue(guid, out var resolvedLayerId)
                ? resolvedLayerId
                : 0;
            if (!TryResolveLayerData(layerLookup, spriteRect.name, layerId, out var data))
                continue;

            var mosaicPosition = Vector2.zero;
            var hasMosaicPosition = !string.IsNullOrEmpty(guid) &&
                                    mosaicPositionBySpriteGuid != null &&
                                    mosaicPositionBySpriteGuid.TryGetValue(guid, out mosaicPosition);
            var desiredRect = PsdCoordinateSpace.ResolveSpriteRectForPsdLayerSize(
                rect,
                data.layerSizeOnFile,
                mosaicPosition,
                hasMosaicPosition);
            currentRects.Add(rect);
            desiredRects.Add(desiredRect);
        }

        return ReanimatorAssetChangeAlgorithms.TryDetectUniformPsdLayerResize(
            currentRects,
            desiredRects,
            out scale,
            out changedLayers,
            out comparableLayers);
    }

    private void Rebuild(string assetPath, bool preserveUiVisibility = true)
    {
        CancelPreviewCacheRefresh();
        var previousPath = cachedPath;
        var previousDocumentSize = cachedDocumentSize;
        if (!string.Equals(previousPath, assetPath, StringComparison.OrdinalIgnoreCase))
            boneRecoveryAttemptedPath = null;
        cachedPath = assetPath;
        var reuseLayerSelection = !string.IsNullOrEmpty(previousPath)
                                  && string.Equals(previousPath, assetPath, StringComparison.OrdinalIgnoreCase);
        if (!reuseLayerSelection)
        {
            selectedLayerSelectionKey = string.Empty;
            elbowLines.Clear();
            elbowIsCreating = false;
            elbowIsDragging = false;
            elbowDraggedLineIndex = -1;
            elbowSelectedLineIndex = -1;
            elbowCreateLayerStableKey = string.Empty;
            elbowDragLastMouseDoc = Vector2.zero;
            elbowControlId = 0;
            elbowDragMode = ElbowDragMode.None;
            limbLines.Clear();
            limbIsCreating = false;
            limbIsDragging = false;
            limbDraggedLineIndex = -1;
            limbSelectedLineIndex = -1;
            limbCreateLayerStableKey = string.Empty;
            limbDragLastMouseDoc = Vector2.zero;
            limbControlId = 0;
            limbDragMode = ElbowDragMode.None;
            boobHasPoint = false;
            boobCenterDoc = Vector2.zero;
            boobLayerStableKey = string.Empty;
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
            boobIsDragging = false;
            boobControlId = 0;
            editGeometrySelectedVertexIndex = -1;
            editGeometrySelectedVertexIndices.Clear();
            editGeometryIsDragging = false;
            editGeometryControlId = 0;
            editGeometrySelectedLayerStableKey = string.Empty;
            editGeometryIsRectSelecting = false;
            editGeometryRectToggleSelection = false;
            editGeometryRectStartCanvas = Vector2.zero;
            editGeometryRectCurrentCanvas = Vector2.zero;
            editGeometryDragLastBaseDoc = Vector2.zero;
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
            createEdgeStartVertexIndex = -1;
            createEdgeLayerStableKey = string.Empty;
            radiusCullHasPoint = false;
            radiusCullCenterDoc = Vector2.zero;
            radiusCullLayerStableKey = string.Empty;
            radiusCullHandleAngleRad = Mathf.PI * 0.25f;
            jointStartAngleRad = -Mathf.PI * 0.16666667f;
            jointEndAngleRad = Mathf.PI * 0.16666667f;
            jointNearestBoneIndex = -1;
            radiusCullIsDragging = false;
            radiusCullHandleIsDragging = false;
            radiusCullControlId = 0;
            jointIsDragging = false;
            jointStartHandleIsDragging = false;
            jointEndHandleIsDragging = false;
            jointMiddleHandleIsDragging = false;
            jointStartOffsetHandleIsDragging = false;
            jointEndOffsetHandleIsDragging = false;
            jointControlId = 0;
            radiusCullHasRadiusOverride = false;
            radiusCullRadiusOverride = 0f;
            InvalidateRadiusCullPreviewCache(true);
        }

        var previousVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var previousLayerDepth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousLayerColor = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var previousHierarchyVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var previousBoneVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var previousBoneDepth = new Dictionary<int, int>();
        var previousBoneColor = new Dictionary<int, Color>();
        var sameAssetPath = preserveUiVisibility
                            && !string.IsNullOrEmpty(previousPath)
                            && string.Equals(previousPath, assetPath, StringComparison.OrdinalIgnoreCase);
        if (sameAssetPath)
        {
            CaptureCurrentUiVisibilityState(
                previousVisibility,
                previousLayerDepth,
                previousLayerColor,
                previousHierarchyVisibility,
                previousBoneVisibility,
                previousBoneDepth,
                previousBoneColor);
        }
        var reuseVisibility = sameAssetPath || (preserveUiVisibility && TryConsumePendingUiVisibilityState(
            assetPath,
            previousVisibility,
            previousLayerDepth,
            previousLayerColor,
            previousHierarchyVisibility,
            previousBoneVisibility,
            previousBoneDepth,
            previousBoneColor));
        if (preserveUiVisibility && TryCopyCachedUiVisibilityState(
                assetPath,
                previousVisibility,
                previousHierarchyVisibility,
                previousBoneVisibility))
        {
            reuseVisibility = true;
        }

        layerInfos.Clear();
        layerHierarchyNodes.Clear();

        cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        cachedAtlasTexture = null;
        if (cachedTexture == null)
            return;

        var currentDocumentSize = GetDocumentSize(assetPath);
        cachedDocumentSize = currentDocumentSize;

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        if (importer == null)
            return;

        if (!TryAutoRepairPsdImporterState(
                assetPath,
                importer,
                previousDocumentSize,
                currentDocumentSize,
                out var autoRepaired,
                out var autoRepairError))
        {
            ReanimatorLog.Error($"[Reanimator] Auto-repair skipped for {assetPath}: {autoRepairError}");
        }
        else if (autoRepaired)
        {
            cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            currentDocumentSize = GetDocumentSize(assetPath);
            cachedDocumentSize = currentDocumentSize;
            importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
            if (cachedTexture == null || importer == null)
                return;
        }

        var layerData = GetPsdLayerData(assetPath);
        var layerByName = new Dictionary<string, Queue<PsdLayerDataInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in layerData)
        {
            if (data.isGroup) continue;
            if (!layerByName.TryGetValue(data.name, out var queue))
            {
                queue = new Queue<PsdLayerDataInfo>();
                layerByName[data.name] = queue;
            }
            queue.Enqueue(data);
        }

        var so = new SerializedObject(importer);
        var characterPartRectBySpriteGuid = BuildCharacterPartRectBySpriteGuid(so);
        var mosaicPositionBySpriteGuid = BuildPsdLayerMosaicPositionBySpriteGuid(so);
        var metaPositionBySpriteGuid = BuildMetaSpritePositionBySpriteGuid(assetPath);
        var spriteList = ResolveSpriteList(so);
        if (spriteList == null || !spriteList.isArray)
            return;

        var hasSerializedLayerPositions = false;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var element = spriteList.GetArrayElementAtIndex(i);
            if (element == null) continue;

            var nameProp = element.FindPropertyRelative("m_Name") ?? element.FindPropertyRelative("name");
            var rectProp = element.FindPropertyRelative("m_Rect") ?? element.FindPropertyRelative("rect");
            if (rectProp == null) continue;

            var serializedRect = rectProp.rectValue;
            if (serializedRect.width <= 0f || serializedRect.height <= 0f) continue;

            var name = nameProp != null ? nameProp.stringValue : $"Layer {i}";
            var spriteGuid = GetGuidStringProperty(element, "spriteID", "m_SpriteID", "spriteId", "m_SpriteId");
            var hasLayerDataInfo = TryDequeueLayerData(layerByName, name, out var layerDataInfo);
            var mosaicPosition = Vector2.zero;
            var hasMosaicPosition = !string.IsNullOrEmpty(spriteGuid) &&
                                    mosaicPositionBySpriteGuid.TryGetValue(NormalizeGuidString(spriteGuid), out mosaicPosition);
            var rect = hasLayerDataInfo
                ? PsdCoordinateSpace.ResolveSpriteRectForPsdLayerImport(
                    serializedRect,
                    layerDataInfo.layerSizeOnFile,
                    mosaicPosition,
                    hasMosaicPosition)
                : serializedRect;
            var posProp = element.FindPropertyRelative("spritePosition") ?? element.FindPropertyRelative("m_SpritePosition");
            var hasSpritePosition = posProp != null;
            var spritePosition = hasSpritePosition ? GetVector2FromProperty(posProp) : Vector2.zero;
            var characterPartRect = default(RectInt);
            var partPosition = Vector2.zero;
            var metaPosition = Vector2.zero;
            var hasPartPosition = !string.IsNullOrEmpty(spriteGuid) &&
                                  characterPartRectBySpriteGuid.TryGetValue(spriteGuid, out characterPartRect);
            if (hasPartPosition)
                partPosition = new Vector2(characterPartRect.x, characterPartRect.y);
            var preferCharacterPartPosition = hasPartPosition &&
                                              CharacterPartRectSizeMatchesSpriteRect(characterPartRect, rect);
            var hasMetaPosition = !string.IsNullOrEmpty(spriteGuid) &&
                                  metaPositionBySpriteGuid.TryGetValue(spriteGuid, out metaPosition);
            var pos = ReanimatorRebuildMetaAlgorithms.ChooseLayerSpritePosition(
                rect,
                hasSpritePosition,
                spritePosition,
                hasMetaPosition,
                metaPosition,
                hasPartPosition,
                partPosition,
                preferCharacterPartPosition,
                out var hasSerializedPosition);
            hasSerializedLayerPositions |= hasSerializedPosition;

            var borderProp = element.FindPropertyRelative("border") ?? element.FindPropertyRelative("m_Border");
            var pivotProp = element.FindPropertyRelative("pivot") ?? element.FindPropertyRelative("m_Pivot");
            var alignmentProp = element.FindPropertyRelative("alignment") ?? element.FindPropertyRelative("m_Alignment");
            var info = new LayerInfo
            {
                sourceRect = rect,
                textureRect = serializedRect,
                spriteSheetOptimizeReferenceRect = serializedRect,
                spriteSheetSerializedImportRect = serializedRect,
                spriteSheetPsdLayerReferenceRect = rect,
                spriteSheetOptimizeReferencePosition = pos,
                position = pos,
                name = name,
                spriteGuid = spriteGuid,
                depth = i,
                border = GetVector4FromProperty(borderProp),
                pivot = pivotProp != null ? GetVector2FromProperty(pivotProp) : new Vector2(0.5f, 0.5f),
                alignment = alignmentProp != null && alignmentProp.propertyType == SerializedPropertyType.Integer
                    ? alignmentProp.intValue
                    : 0
            };

            if (hasLayerDataInfo)
            {
                info.layerId = layerDataInfo.layerId;
                info.visible = layerDataInfo.isVisible;
                info.depth = layerDataInfo.index;
                info.layerDataIndex = layerDataInfo.index;
                info.parentLayerDataIndex = layerDataInfo.parentIndex;
            }

            if (reuseVisibility)
            {
                var key = BuildKey(info.name, info.layerId);
                if (previousVisibility.TryGetValue(key, out var wasVisible))
                    info.visible = wasVisible;
                if (previousLayerDepth.TryGetValue(key, out var depth))
                    info.depth = depth;
                if (previousLayerColor.TryGetValue(key, out var color))
                    info.color = color;
            }

            layerInfos.Add(info);
        }

        BuildLayerHierarchy(layerData, previousHierarchyVisibility, reuseVisibility);
        BuildBones(importer, reuseVisibility, previousBoneVisibility, previousBoneDepth, previousBoneColor);
        if (!hasSerializedLayerPositions && ShouldFlipLayerPositionsVertically())
        {
            FlipLayerPositionsVertically();
            BuildBones(importer, reuseVisibility, previousBoneVisibility, previousBoneDepth, previousBoneColor);
        }
        BuildDrawOrder();
        BuildVertices(assetPath);
        bonePoseAtRest = true;
        cachedAssetDependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        nextAssetDependencyHashCheckTime = EditorApplication.timeSinceStartup + AssetDependencyHashCheckIntervalSeconds;
        cachedPreviewPipelineVersion = CurrentPreviewPipelineVersion;
        if (!LayerExistsBySelectionKey(selectedLayerSelectionKey))
            selectedLayerSelectionKey = string.Empty;
        if (selectedBoneIndex >= bones.Count)
            selectedBoneIndex = -1;
    }

}

#endif

