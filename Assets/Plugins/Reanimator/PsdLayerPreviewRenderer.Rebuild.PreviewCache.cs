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
    private IEnumerator previewCacheRefresh;
    private string previewCacheRefreshPath;
    public bool IsPreviewCacheRefreshInProgress => previewCacheRefresh != null;
    public float PreviewCacheRefreshProgress { get; private set; }
    public string PreviewCacheRefreshStatus { get; private set; } = string.Empty;

    public void RequestPreviewCacheRefresh(string assetPath, bool preserveUiVisibility = true)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        if (IsPreviewCacheReady(assetPath))
            return;

        if (previewCacheRefresh != null &&
            string.Equals(previewCacheRefreshPath, assetPath, StringComparison.OrdinalIgnoreCase))
            return;

        previewCacheRefreshPath = assetPath;
        PreviewCacheRefreshProgress = 0f;
        PreviewCacheRefreshStatus = "Loading preview...";
        previewCacheRefresh = RebuildIncremental(assetPath, preserveUiVisibility);
    }

    public bool TickPreviewCacheRefresh(double maxSeconds = 0.006d)
    {
        if (previewCacheRefresh == null)
            return false;

        var advanced = false;
        var startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var budgetTicks = Math.Max(1L, (long)(Math.Max(0.001d, maxSeconds) * System.Diagnostics.Stopwatch.Frequency));
        const int MaxStepsPerEditorUpdate = 1;
        var steps = 0;
        do
        {
            bool hasNext;
            try
            {
                hasNext = previewCacheRefresh.MoveNext();
            }
            catch (Exception ex)
            {
                ReanimatorLog.Exception(ex);
                previewCacheRefresh = null;
                previewCacheRefreshPath = string.Empty;
                PreviewCacheRefreshProgress = 0f;
                PreviewCacheRefreshStatus = "Preview load failed.";
                return true;
            }

            advanced = true;
            steps++;
            if (!hasNext)
            {
                previewCacheRefresh = null;
                previewCacheRefreshPath = string.Empty;
                PreviewCacheRefreshProgress = 1f;
                PreviewCacheRefreshStatus = "Preview ready.";
                return true;
            }
        }
        while (steps < MaxStepsPerEditorUpdate &&
               System.Diagnostics.Stopwatch.GetTimestamp() - startTicks < budgetTicks);

        return advanced;
    }

    public void CancelPreviewCacheRefresh()
    {
        previewCacheRefresh = null;
        previewCacheRefreshPath = string.Empty;
    }

    private void SetPreviewCacheRefreshProgress(float progress, string status)
    {
        PreviewCacheRefreshProgress = Mathf.Clamp01(progress);
        PreviewCacheRefreshStatus = status ?? string.Empty;
    }

    private IEnumerator RebuildIncremental(string assetPath, bool preserveUiVisibility)
    {
        SetPreviewCacheRefreshProgress(0.02f, "Preparing preview...");
        cachedPreviewPipelineVersion = -1;
        cachedAssetDependencyHash = string.Empty;
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
            boobV2Selection = 0;
            boobV2HasTargetA = false;
            boobV2HasTargetB = false;
            boobV2TargetADoc = Vector2.zero;
            boobV2TargetBDoc = Vector2.zero;
            boobV2RawContourADoc.Clear();
            boobV2RawContourBDoc.Clear();
            boobV2ContourADoc.Clear();
            boobV2ContourBDoc.Clear();
            boobV2LayerStableKey = string.Empty;
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
            boobV2ActiveTarget = -1;
            boobV2DrawingContour = -1;
            boobV2WorkingContourDoc.Clear();
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

        yield return null;

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
        sortedLayers.Clear();
        bones.Clear();
        boneDrawOrder.Clear();
        vertexDataByLayerSelectionKey.Clear();
        defaultGeometryBySpriteKey.Clear();
        defaultGeometryAssetPath = string.Empty;

        SetPreviewCacheRefreshProgress(0.08f, "Loading texture...");
        cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        cachedAtlasTexture = null;
        if (cachedTexture == null)
            yield break;

        yield return null;

        SetPreviewCacheRefreshProgress(0.14f, "Reading document...");
        var currentDocumentSize = GetDocumentSize(assetPath);
        cachedDocumentSize = currentDocumentSize;

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        if (importer == null)
            yield break;

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
                yield break;
        }

        yield return null;

        SetPreviewCacheRefreshProgress(0.22f, "Reading layers...");
        var layerData = GetPsdLayerData(assetPath);
        var layerByName = new Dictionary<string, Queue<PsdLayerDataInfo>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layerData.Count; i++)
        {
            var data = layerData[i];
            if (!data.isGroup)
            {
                if (!layerByName.TryGetValue(data.name, out var queue))
                {
                    queue = new Queue<PsdLayerDataInfo>();
                    layerByName[data.name] = queue;
                }
                queue.Enqueue(data);
            }

            if ((i & 31) == 31)
                yield return null;
        }

        var so = new SerializedObject(importer);
        var characterPartRectBySpriteGuid = BuildCharacterPartRectBySpriteGuid(so);
        var mosaicPositionBySpriteGuid = BuildPsdLayerMosaicPositionBySpriteGuid(so);
        var metaPositionBySpriteGuid = BuildMetaSpritePositionBySpriteGuid(assetPath);
        var spriteList = ResolveSpriteList(so);
        if (spriteList == null || !spriteList.isArray)
            yield break;

        yield return null;

        SetPreviewCacheRefreshProgress(0.32f, "Building layer list...");
        var hasSerializedLayerPositions = false;
        for (var i = 0; i < spriteList.arraySize; i++)
        {
            var element = spriteList.GetArrayElementAtIndex(i);
            if (element == null)
                continue;

            var nameProp = element.FindPropertyRelative("m_Name") ?? element.FindPropertyRelative("name");
            var rectProp = element.FindPropertyRelative("m_Rect") ?? element.FindPropertyRelative("rect");
            if (rectProp == null)
                continue;

            var serializedRect = rectProp.rectValue;
            if (serializedRect.width <= 0f || serializedRect.height <= 0f)
                continue;

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
            if ((i & 7) == 7)
            {
                SetPreviewCacheRefreshProgress(Mathf.Lerp(0.32f, 0.52f, (i + 1f) / Mathf.Max(1, spriteList.arraySize)), $"Building layers {i + 1}/{spriteList.arraySize}...");
                yield return null;
            }
        }

        SetPreviewCacheRefreshProgress(0.56f, "Building hierarchy...");
        BuildLayerHierarchy(layerData, previousHierarchyVisibility, reuseVisibility);
        yield return null;

        SetPreviewCacheRefreshProgress(0.64f, "Building bones...");
        foreach (var _ in BuildBonesFromMetaFileIncremental(assetPath, reuseVisibility, previousBoneVisibility, previousBoneDepth, previousBoneColor))
            yield return null;

        if (!hasSerializedLayerPositions && ShouldFlipLayerPositionsVertically())
        {
            SetPreviewCacheRefreshProgress(0.70f, "Aligning layers...");
            FlipLayerPositionsVertically();
            foreach (var _ in BuildBonesFromMetaFileIncremental(assetPath, reuseVisibility, previousBoneVisibility, previousBoneDepth, previousBoneColor))
                yield return null;
        }

        SetPreviewCacheRefreshProgress(0.74f, "Sorting draw order...");
        BuildDrawOrder();
        yield return null;

        SetPreviewCacheRefreshProgress(0.78f, "Preparing preview layers...");
        BuildPreviewLayersWithoutSpriteMeshes();
        yield return null;

        SetPreviewCacheRefreshProgress(0.80f, "Preview ready. Loading sprite geometry...");
        bonePoseAtRest = true;
        cachedAssetDependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        nextAssetDependencyHashCheckTime = EditorApplication.timeSinceStartup + AssetDependencyHashCheckIntervalSeconds;
        cachedPreviewPipelineVersion = CurrentPreviewPipelineVersion;
        if (!LayerExistsBySelectionKey(selectedLayerSelectionKey))
            selectedLayerSelectionKey = string.Empty;
        if (selectedBoneIndex >= bones.Count)
            selectedBoneIndex = -1;
        yield return null;

        foreach (var _ in BuildVerticesIncremental(assetPath, true))
            yield return null;

        SetPreviewCacheRefreshProgress(0.98f, "Finalizing preview geometry...");
        yield return null;
    }

}

#endif
