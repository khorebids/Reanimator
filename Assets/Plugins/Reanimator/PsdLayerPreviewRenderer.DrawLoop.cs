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
    public bool Draw(
        string assetPath,
        Rect localRect,
        PsdPreviewDrawOptions options = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        if (options == null)
            options = PsdPreviewDrawOptions.CreateDefault();
        options.Sanitize();
        deformSpritesWithBonePoseForCurrentDraw = options.DeformSpritesWithBonePose;
        spriteSelectionAllowedForCurrentDraw = options.AllowSpriteSelection;
        spriteSelectionLockedForCurrentDraw = options.LockSpriteSelection;
        isolateSelectedSpriteForCurrentDraw = options.IsolateSelectedSprite;
        isolateBoneModificationForCurrentDraw = options.IsolateBoneModification;

        limbPreviewNeedsRepaint = false;
        if (!IsPreviewCacheReady(assetPath))
            return false;

        if (HasAssetDependencyChanged(assetPath))
        {
            InvalidatePreviewCacheForExternalAssetChange();
            return false;
        }

        if (options.DrawBones && bones.Count == 0 && !string.Equals(boneRecoveryAttemptedPath, assetPath, StringComparison.OrdinalIgnoreCase))
        {
            boneRecoveryAttemptedPath = assetPath;
            return false;
        }

        if (bones.Count > 0)
        {
            boneRecoveryAttemptedPath = null;
        }

        if (cachedTexture == null || layerInfos.Count == 0)
            return false;

        UpdatePreviewTestAnimation(options.PreviewTestAnimationActive);

        var docSize = cachedDocumentSize;
        if (docSize.x <= 0 || docSize.y <= 0)
            docSize = new Vector2Int(cachedTexture.width, cachedTexture.height);
        var spriteAtlasMode = options.SpriteAtlasMode;
        var layoutMode = spriteAtlasMode ? PsdPreviewLayoutMode.SpriteAtlas : PsdPreviewLayoutMode.Skeleton;
        var coordinateDocumentSize = spriteAtlasMode
            ? GetPreviewAtlasSize()
            : docSize;
        var coordinateSpace = new PsdCoordinateSpace(localRect, coordinateDocumentSize, layerInfos, options.Navigator, layoutMode);
        var prevColor = GUI.color;
        var spriteDrawColor = Color.white;

        var drewSomething = false;
        var drawList = sortedLayers.Count > 0 ? sortedLayers : layerInfos;
        var tools = options.Tools;
        var visibility = options.Visibility;
        var radiusCullState = PsdPreviewRadiusCullState.Create(options, radiusCullHasRadiusOverride, radiusCullRadiusOverride);

        if (radiusCullState.SyncToActiveLimbLine)
            SyncRadiusCullPointToActiveLimbLine(true);

        bool selectingContour = options.ActivePreviewModule is ReanimatorSoftContourModule &&
            SelectingSoftContourPoints;
        if (selectingContour)
        {
            options.ActivePreviewModule.HandlePreviewInput(options.Api,
                new ReanimatorPreviewContext(localRect, coordinateSpace, drawList, visibility));
        }
        else if (spriteAtlasMode)
        {
            HandleSpriteAtlasFrameInput(localRect, coordinateSpace, drawList);
        }
        else if (tools.Elbow)
            HandleElbowInput(localRect, coordinateSpace, drawList);
        else if (tools.Limb)
        {
            if (radiusCullState.LimbHandleInputActive)
                HandleRadiusCullInput(localRect, coordinateSpace, drawList, radiusCullState.InputRadius, true);
            if (Event.current == null || Event.current.type != EventType.Used)
                HandleLimbsInput(localRect, coordinateSpace, drawList);
        }
        else if (tools.Boob)
            HandleBoobInput(localRect, coordinateSpace, drawList);
        else if (tools.Curve)
        {
            var curveDragActive = curveIsDragging && curveControlId != 0;
            if (curveDragActive)
            {
                HandleCurveInput(localRect, coordinateSpace, drawList);
                if ((Event.current == null || Event.current.type != EventType.Used) && options.DrawBones && bones.Count > 0)
                    HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
            }
            else
            {
                if (options.DrawBones && bones.Count > 0)
                    HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
                if (Event.current == null || Event.current.type != EventType.Used)
                    HandleCurveInput(localRect, coordinateSpace, drawList);
            }
        }
        else if (tools.WeightSlider)
        {
            if (weightSliderIsRectSelecting)
            {
                HandleWeightSliderInput(localRect, coordinateSpace, drawList);
            }
            else
            {
                HandleWeightSliderInput(localRect, coordinateSpace, drawList);
                if ((Event.current == null || Event.current.type != EventType.Used) && options.DrawBones && bones.Count > 0)
                    HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
            }
        }
        else if (options.ActivePreviewModule != null)
        {
            var context = new ReanimatorPreviewContext(localRect, coordinateSpace, drawList, visibility);
            options.ActivePreviewModule.HandlePreviewInput(options.Api, context);
            if (options.ActivePreviewModule.AllowBoneInputAfterPreviewInput &&
                (Event.current == null || Event.current.type != EventType.Used) &&
                options.DrawBones && bones.Count > 0)
            {
                var boneInputOptions = options.ActivePreviewModule is IReanimatorBoneInputModule boneInputModule
                    ? boneInputModule.BoneInputOptions
                    : default(ReanimatorBoneInputOptions);
                HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing, boneInputOptions);
            }
            if ((string.Equals(options.ActivePreviewModule.Id, "weights.projector", StringComparison.OrdinalIgnoreCase) ||
                 options.ActivePreviewModule is ReanimatorSoftContourModule) &&
                (Event.current == null || Event.current.type != EventType.Used))
                HandleSpriteSelection(localRect, coordinateSpace, drawList);
        }
        else if (tools.CreateEdge)
            HandleCreateEdgeInput(localRect, coordinateSpace, drawList);
        else if (tools.SplitEdge)
            HandleSplitEdgeInput(localRect, coordinateSpace, drawList);
        else if (tools.BoneInfluence)
        {
            if (options.DrawBones && bones.Count > 0)
                HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
            if (Event.current == null || Event.current.type != EventType.Used)
                HandleSpriteSelection(localRect, coordinateSpace, drawList);
        }
        else if (tools.SpriteInfluence)
        {
            HandleSpriteSelection(localRect, coordinateSpace, drawList);
            if ((Event.current == null || Event.current.type != EventType.Used) && options.DrawBones && bones.Count > 0)
                HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
        }
        else if (tools.EditGeometry)
            HandleEditGeometryInput(localRect, coordinateSpace, drawList);
        else if (tools.Joint)
        {
            if (options.Joint.AlgorithmMode == 1)
            {
                if (options.DrawBones && bones.Count > 0)
                    HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
            }
            else
            {
                if (jointCenterLockEnabled && options.DrawBones && bones.Count > 0)
                    HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
                if (Event.current == null || Event.current.type != EventType.Used)
                    HandleJointInput(localRect, coordinateSpace, drawList, radiusCullState.InputRadius);
            }
        }
        else if (radiusCullState.InputActive)
            HandleRadiusCullInput(localRect, coordinateSpace, drawList, radiusCullState.InputRadius, false);
        else
        {
            if (!tools.AutoGeometry && options.DrawBones && bones.Count > 0)
                HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing);
            HandleSpriteSelection(localRect, coordinateSpace, drawList);
        }

        var selectedKey = options.AllowSpriteSelection ? selectedLayerSelectionKey : string.Empty;
        var selectedLayer = FindLayerBySelectionKey(drawList, selectedKey);
        var selectedLayerStableKey = selectedLayer != null ? BuildKey(selectedLayer.name, selectedLayer.layerId) : string.Empty;
        foreach (var layer in drawList)
        {
            if (selectedLayer == null && !string.IsNullOrEmpty(selectedKey))
            {
                var key = BuildSelectionKey(layer);
                if (string.Equals(key, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    selectedLayer = layer;
                    selectedLayerStableKey = BuildKey(selectedLayer.name, selectedLayer.layerId);
                    continue;
                }
            }

            if (!layer.visible)
                continue;
            if (options.AllowSpriteSelection && options.IsolateSelectedSprite && selectedLayer != null && !IsLayerSelected(layer))
                continue;

            drewSomething |= DrawLayerVisual(layer, visibility.SpriteAlpha, localRect, coordinateSpace, spriteAtlasMode, options.SpriteAtlasGeometryMode, selectedLayerStableKey, spriteDrawColor);
        }

        if (selectedLayer != null && selectedLayer.visible)
            drewSomething |= DrawLayerVisual(selectedLayer, visibility.SpriteAlpha, localRect, coordinateSpace, spriteAtlasMode, options.SpriteAtlasGeometryMode, selectedLayerStableKey, spriteDrawColor);

        GUI.color = prevColor;

        if (spriteAtlasMode)
            DrawSpriteAtlasBoundingBoxes(coordinateSpace, drawList, selectedLayer, options.AllowSpriteSelection && options.IsolateSelectedSprite);
        else if (options.AllowSpriteSelection)
            DrawLayerBoundingBoxes(coordinateSpace, drawList, selectedLayer);

        DrawRectOutline(coordinateSpace.CanvasRectView, new Color(1f, 1f, 1f, 0.45f), 1f);

        if (spriteAtlasMode)
        {
            if (options.SpriteAtlasGeometryMode && !(options.ActivePreviewModule is ReanimatorSoftContourModule))
                DrawSpriteAtlasGeometryOverlay(coordinateSpace, drawList, selectedLayerStableKey, visibility.VertexAlpha);
            if (options.ActivePreviewModule is ReanimatorSoftContourModule)
                DrawSoftContour(coordinateSpace);
            if (IsDebugModeEnabled)
            {
                DrawDebugOverlay(coordinateSpace, drawList, selectedLayer, visibility.VertexAlpha, visibility.BoneAlpha, options);
                options.ActiveDebugPreviewModule?.DrawDebugPreviewOverlay(options.Api, new ReanimatorPreviewContext(localRect, coordinateSpace, drawList, visibility));
            }
            DrawTopmostToolOverlays(localRect, coordinateSpace, selectedLayer, options);
            GUI.color = prevColor;
            return drewSomething;
        }

        var shouldDrawCullGeometryPreview = radiusCullState.ShouldDrawGeometryPreview(selectedLayer != null);
        var activeModuleOverridesGeometryPreview =
            options.ActivePreviewModule != null &&
            options.ActivePreviewModule.OverridesSelectedGeometryPreview;
        var drewRadiusCullPreviewGeometry = false;
        if (!options.SuppressGeometryOverlay && visibility.VertexAlpha > 0f && vertexDataByLayerSelectionKey.Count > 0)
        {
            if (tools.LimbBone && DrawLimbBonePreview(coordinateSpace, visibility.VertexAlpha, tools.LimbBoneChild,
                    tools.LimbBoneCull, options.RadiusCull.Radius, options.RadiusCull.RemoveInnerVertices))
            {
                drewSomething = true;
            }
            else if (shouldDrawCullGeometryPreview)
            {
                if (radiusCullState.LimbPreviewActive)
                {
                    drewRadiusCullPreviewGeometry = DrawLimbAfterCullGeometryPreview(
                        coordinateSpace,
                        coordinateSpace.DocumentToView,
                        selectedLayer,
                        visibility.VertexAlpha,
                        radiusCullState.PreviewRadius,
                        options.RadiusCull.RemoveContourVertices,
                        options.RadiusCull.RemoveInnerVertices);
                }
                else
                {
                    drewRadiusCullPreviewGeometry = DrawRadiusCullGeometryPreview(
                        coordinateSpace,
                        coordinateSpace.DocumentToView,
                        selectedLayer,
                        visibility.VertexAlpha,
                        radiusCullState.PreviewRadius,
                        options.RadiusCull.RemoveContourVertices,
                        options.RadiusCull.RemoveInnerVertices,
                        radiusCullState.JointPreviewActive,
                        options.Joint.CPairOffset,
                        options.Joint.LongPoints,
                        options.Joint.StartLongPointsOffset,
                        options.Joint.EndLongPointsOffset);
                }
                if (!drewRadiusCullPreviewGeometry && !activeModuleOverridesGeometryPreview)
                    DrawVertices(coordinateSpace, visibility.VertexAlpha, !tools.LimbBone);
            }
            else if (!activeModuleOverridesGeometryPreview)
            {
                DrawVertices(coordinateSpace, visibility.VertexAlpha, !tools.LimbBone);
            }
            drewSomething = true;
        }

        if (tools.Elbow && selectedLayer != null)
            DrawElbowLines(coordinateSpace.DocumentToView, selectedLayer, visibility.VertexAlpha);
        if (tools.Limb && selectedLayer != null)
            DrawLimbLines(coordinateSpace.DocumentToView, selectedLayer, visibility.VertexAlpha);
        if (tools.Boob)
            DrawBoobPreview(coordinateSpace.DocumentToView, selectedLayer, visibility.VertexAlpha);
        if (tools.Curve && selectedLayer != null)
            DrawCurveOverlay(coordinateSpace, selectedLayer, visibility.VertexAlpha);
        if (tools.WeightSlider)
            DrawWeightSliderOverlay(coordinateSpace, visibility.VertexAlpha);
        var activePreviewContext = options.ActivePreviewModule != null || (IsDebugModeEnabled && options.ActiveDebugPreviewModule != null)
            ? new ReanimatorPreviewContext(localRect, coordinateSpace, drawList, visibility)
            : null;
        if (options.ActivePreviewModule != null)
            options.ActivePreviewModule.DrawPreviewOverlay(options.Api, activePreviewContext);
        if (tools.EditGeometry || tools.BoneInfluence || tools.SpriteInfluence)
            DrawEditGeometrySelectionOverlay(coordinateSpace, visibility.VertexAlpha);
        if (tools.Joint && options.Joint.AlgorithmMode == 0 && selectedLayer != null)
        {
            DrawJointOverlay(coordinateSpace, selectedLayer, visibility.VertexAlpha, radiusCullState.PreviewRadius);
        }
        else if (radiusCullState.OverlayActive && selectedLayer != null)
        {
            DrawRadiusCullOverlay(coordinateSpace, selectedLayer, visibility.VertexAlpha, radiusCullState.PreviewRadius, new Color(1f, 0.2f, 0.2f, 1f));
        }

        if (options.DrawBones && bones.Count > 0)
        {
            DrawBones(coordinateSpace, visibility.BoneAlpha, options);
            drewSomething = true;
        }

        if (IsDebugModeEnabled)
        {
            DrawDebugOverlay(coordinateSpace, drawList, selectedLayer, visibility.VertexAlpha, visibility.BoneAlpha, options);
            options.ActiveDebugPreviewModule?.DrawDebugPreviewOverlay(options.Api, activePreviewContext);
        }

        DrawTopmostToolOverlays(localRect, coordinateSpace, selectedLayer, options);
        GUI.color = prevColor;
        return drewSomething;
    }

    private void DrawTopmostToolOverlays(
        Rect localRect,
        PsdCoordinateSpace coordinateSpace,
        LayerInfo selectedLayer,
        PsdPreviewDrawOptions options)
    {
        if (options != null && options.AutoGeometryQuadNetPreviewActive)
            DrawAutoGeometryQuadNetPreviewSquare(
                localRect,
                coordinateSpace,
                selectedLayer,
                options.AutoGeometryQuadDensity,
                options.AutoGeometryQuadRotationDegrees);
        if (options != null && options.AutoGeometryReanimaxHardMaskPreviewActive)
            DrawReanimaxHardMaskPreview(
                localRect,
                coordinateSpace,
                selectedLayer,
                options.AutoGeometryReanimaxHardMaskAlphaThreshold);
        if (options != null && options.AutoGeometryReanimaxHardEdgePointsPreviewActive)
            DrawReanimaxHardEdgePointsPreview(
                localRect,
                coordinateSpace,
                selectedLayer,
                options.AutoGeometryReanimaxHardMaskAlphaThreshold,
                options.AutoGeometryReanimaxHardEdgePositionFilterPixels,
                options.AutoGeometryReanimaxHardEdgeMinTurnDegrees,
                options.AutoGeometryReanimaxHardEdgePostPositionFilterPixels,
                options.AutoGeometryReanimaxHardEdgeMinDistancePixels,
                options.AutoGeometryReanimaxHardNormalMidpointPositionFilterPixels,
                options.AutoGeometryReanimaxHardNormalInsetPixels,
                options.AutoGeometryReanimaxHardNormalInsetPositionFilterPixels);
        if (options != null && options.Tools.Joint && options.Joint.AlgorithmMode == 1)
        {
            DrawJointV2GeneratedPreview(
                coordinateSpace,
                options.Joint.WeightCurve,
                options.Joint.SpecialWeightCurve,
                options.Joint.SpecialJointDistance,
                options.Joint.BoneBaseOffset);
            DrawJointV2PoseIntersectionOverlay(coordinateSpace, options.Joint.SpecialJointDistance);
            DrawJointV2BoneAngleOverlay(localRect);
        }
    }

    private bool DrawLayerVisual(
        LayerInfo layer,
        float layerAlpha,
        Rect localRect,
        PsdCoordinateSpace coordinateSpace,
        bool spriteAtlasMode,
        bool spriteAtlasGeometryMode,
        string selectedLayerStableKey,
        Color previousGuiColor)
    {
        if (layer == null || coordinateSpace == null)
            return false;

        var dst = coordinateSpace.LayerToViewRect(layer);
        if (!IsPreviewRectVisible(dst, localRect))
            return false;

        var drawTexture = layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        if (drawTexture == null)
            return false;

        var drewSkinned = !spriteAtlasMode && DrawLayerMesh(
            layer,
            layerAlpha,
            localRect,
            coordinateSpace);
        if (!drewSkinned)
        {
            var issueTint = ShouldTintLayerForDebugIssues(layer, spriteAtlasMode, spriteAtlasGeometryMode);
            GUI.color = issueTint
                ? new Color(1f, 0.18f, 0.18f, Mathf.Clamp01(layerAlpha))
                : new Color(previousGuiColor.r, previousGuiColor.g, previousGuiColor.b, layerAlpha);
            var guiUv = PsdCoordinateSpace.LayerTextureUvRect(
                layer,
                new Vector2(drawTexture.width, drawTexture.height),
                false);
            GUI.DrawTextureWithTexCoords(dst, drawTexture, guiUv, true);
        }

        return true;
    }

    public bool IsPreviewCacheReady(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath) &&
               cachedTexture != null &&
               layerInfos != null &&
               layerInfos.Count > 0 &&
               cachedPreviewPipelineVersion == CurrentPreviewPipelineVersion &&
               string.Equals(cachedPath, assetPath, StringComparison.OrdinalIgnoreCase);
    }

    public void SaveWorkspacePreviewPng(string outputPath)
    {
        if (cachedTexture == null || layerInfos == null || layerInfos.Count == 0)
            throw new InvalidOperationException("PSD preview cache is not ready.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var targetSize = cachedDocumentSize;
        if (targetSize.x <= 0 || targetSize.y <= 0)
            targetSize = new Vector2Int(cachedTexture.width, cachedTexture.height);

        var composed = ComposeDocumentPreviewTexture(targetSize);
        File.WriteAllBytes(outputPath, composed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(composed);
    }

    public void RefreshPreviewCache(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        Rebuild(assetPath);
    }

    private Texture2D ComposeDocumentPreviewTexture(Vector2Int targetSize)
    {
        var width = Mathf.Max(1, targetSize.x);
        var height = Mathf.Max(1, targetSize.y);
        var outputPixels = new Color32[width * height];
        var textureCache = new Dictionary<Texture2D, TexturePixelBuffer>();

        try
        {
            var drawList = sortedLayers.Count > 0 ? sortedLayers : layerInfos;
            for (var i = 0; i < drawList.Count; i++)
            {
                var layer = drawList[i];
                if (layer == null || !layer.visible)
                    continue;

                var texture = layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
                if (texture == null)
                    continue;

                if (!textureCache.TryGetValue(texture, out var source))
                {
                    var readable = CopyTextureToReadable(texture);
                    source = new TexturePixelBuffer(readable);
                    textureCache[texture] = source;
                }

                if (!TryBlendDeformedLayerIntoDocument(outputPixels, width, height, layer, source))
                    BlendLayerIntoDocument(outputPixels, width, height, layer, source);
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels32(outputPixels);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            foreach (var entry in textureCache)
                entry.Value.Dispose();
        }
    }

    private bool TryBlendDeformedLayerIntoDocument(
        Color32[] destination,
        int destinationWidth,
        int destinationHeight,
        LayerInfo layer,
        TexturePixelBuffer source)
    {
        if (destination == null || layer == null || source == null ||
            !TryGetLayerVertexData(layer, out var data) || data == null ||
            data.vertices == null || data.vertexUvs == null || data.triangles == null ||
            data.vertices.Length < 3 || data.vertexUvs.Length != data.vertices.Length ||
            data.triangles.Length < 3)
        {
            return false;
        }

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length ||
            ReferenceEquals(drawVertices, data.vertices))
        {
            return false;
        }

        var wrotePixel = false;
        for (var triangle = 0; triangle + 2 < data.triangles.Length; triangle += 3)
        {
            var ia = data.triangles[triangle];
            var ib = data.triangles[triangle + 1];
            var ic = data.triangles[triangle + 2];
            if (ia < 0 || ib < 0 || ic < 0 ||
                ia >= drawVertices.Length || ib >= drawVertices.Length || ic >= drawVertices.Length)
            {
                continue;
            }

            var a = drawVertices[ia];
            var b = drawVertices[ib];
            var c = drawVertices[ic];
            var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, destinationWidth - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, destinationWidth - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, destinationHeight - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, destinationHeight - 1);
            if (minX > maxX || minY > maxY)
                continue;

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (!TryGetNippleBarycentric(
                            new Vector2(x + 0.5f, y + 0.5f),
                            a,
                            b,
                            c,
                            out var wa,
                            out var wb,
                            out var wc))
                    {
                        continue;
                    }

                    var uv = data.vertexUvs[ia] * wa + data.vertexUvs[ib] * wb + data.vertexUvs[ic] * wc;
                    var sourceColor = source.Sample(uv.x * source.Width, uv.y * source.Height);
                    if (sourceColor.a == 0)
                        continue;

                    var index = y * destinationWidth + x;
                    destination[index] = AlphaBlend(sourceColor, destination[index]);
                    wrotePixel = true;
                }
            }
        }

        return wrotePixel;
    }

    private static void BlendLayerIntoDocument(
        Color32[] destination,
        int destinationWidth,
        int destinationHeight,
        LayerInfo layer,
        TexturePixelBuffer source)
    {
        if (destination == null || layer == null || source == null || source.Width <= 0 || source.Height <= 0)
            return;

        var layerWidth = Mathf.Max(1f, layer.sourceRect.width);
        var layerHeight = Mathf.Max(1f, layer.sourceRect.height);
        var textureRect = PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(source.Width, source.Height));
        if (textureRect.width <= 0f || textureRect.height <= 0f)
            return;

        var xMin = Mathf.Max(0, Mathf.FloorToInt(layer.position.x));
        var xMax = Mathf.Min(destinationWidth, Mathf.CeilToInt(layer.position.x + layerWidth));
        var yMin = Mathf.Max(0, Mathf.FloorToInt(layer.position.y));
        var yMax = Mathf.Min(destinationHeight, Mathf.CeilToInt(layer.position.y + layerHeight));
        if (xMin >= xMax || yMin >= yMax)
            return;

        for (var y = yMin; y < yMax; y++)
        {
            var localY = y + 0.5f - layer.position.y;
            var sourceY = textureRect.y + localY * textureRect.height / layerHeight;
            for (var x = xMin; x < xMax; x++)
            {
                var localX = x + 0.5f - layer.position.x;
                var sourceX = textureRect.x + localX * textureRect.width / layerWidth;
                var sourceColor = source.Sample(sourceX, sourceY);
                if (sourceColor.a == 0)
                    continue;

                var index = y * destinationWidth + x;
                destination[index] = AlphaBlend(sourceColor, destination[index]);
            }
        }
    }

    private static Color32 AlphaBlend(Color32 source, Color32 destination)
    {
        var sourceAlpha = source.a / 255f;
        if (sourceAlpha >= 0.999f)
            return source;

        var destinationAlpha = destination.a / 255f;
        var outAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
        if (outAlpha <= 0.0001f)
            return new Color32(0, 0, 0, 0);

        var inv = 1f - sourceAlpha;
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt((source.r * sourceAlpha + destination.r * destinationAlpha * inv) / outAlpha), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((source.g * sourceAlpha + destination.g * destinationAlpha * inv) / outAlpha), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((source.b * sourceAlpha + destination.b * destinationAlpha * inv) / outAlpha), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(outAlpha * 255f), 0, 255));
    }

    private static Texture2D CopyTextureToReadable(Texture2D source)
    {
        var width = Mathf.Max(1, source.width);
        var height = Mathf.Max(1, source.height);
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default);
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readable.Apply(false, false);
            return readable;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private sealed class TexturePixelBuffer : IDisposable
    {
        private readonly Texture2D texture;
        private readonly Color32[] pixels;

        public TexturePixelBuffer(Texture2D texture)
        {
            this.texture = texture;
            Width = texture != null ? texture.width : 0;
            Height = texture != null ? texture.height : 0;
            pixels = texture != null ? texture.GetPixels32() : Array.Empty<Color32>();
        }

        public int Width { get; }
        public int Height { get; }

        public Color32 Sample(float x, float y)
        {
            if (pixels == null || pixels.Length == 0 || Width <= 0 || Height <= 0)
                return new Color32(0, 0, 0, 0);

            var px = Mathf.Clamp(Mathf.FloorToInt(x), 0, Width - 1);
            var py = Mathf.Clamp(Mathf.FloorToInt(y), 0, Height - 1);
            return pixels[py * Width + px];
        }

        public void Dispose()
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static bool IsPreviewRectVisible(Rect rect, Rect viewport, float padding = 8f)
    {
        if (viewport.width <= 0f || viewport.height <= 0f)
            return false;

        if (rect.width < 0f)
        {
            rect.x += rect.width;
            rect.width = -rect.width;
        }
        if (rect.height < 0f)
        {
            rect.y += rect.height;
            rect.height = -rect.height;
        }
        if (rect.width <= 0f)
            rect.width = 1f;
        if (rect.height <= 0f)
            rect.height = 1f;

        var paddedViewport = new Rect(
            viewport.xMin - padding,
            viewport.yMin - padding,
            viewport.width + padding * 2f,
            viewport.height + padding * 2f);
        return rect.Overlaps(paddedViewport, true);
    }

    private bool HasAssetDependencyChanged(string assetPath)
    {
        if (!ShouldCheckAssetDependencyHash(assetPath))
            return false;

        var currentDependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        if (string.Equals(cachedAssetDependencyHash, currentDependencyHash, StringComparison.Ordinal))
            return false;

        if (hasPendingChanges || HasPendingMeshWorkForAsset(assetPath))
            return false;

        cachedAssetDependencyHash = currentDependencyHash;
        return true;
    }

    private bool ShouldTintLayerForDebugIssues(
        LayerInfo layer,
        bool spriteAtlasMode,
        bool spriteAtlasGeometryMode)
    {
        if (!spriteAtlasMode || !spriteAtlasGeometryMode || !IsDebugModeEnabled || layer == null)
            return false;

        return GetLayerDebugStatus(layer).HasIssues;
    }

    private void InvalidatePreviewCacheForExternalAssetChange()
    {
        CancelPreviewCacheRefresh();
        cachedTexture = null;
        cachedAtlasTexture = null;
        cachedDocumentSize = default;
        cachedAssetDependencyHash = string.Empty;
        cachedPreviewPipelineVersion = -1;
        nextAssetDependencyHashCheckTime = 0d;
        vertexDataByLayerSelectionKey.Clear();
        defaultGeometryBySpriteKey.Clear();
        defaultGeometryAssetPath = string.Empty;
        ClearDirtyLayerSpriteState();
        InvalidateRadiusCullPreviewCache(true);
        limbPreviewNeedsRepaint = false;
        debugLayerVertexBoundsDocByLayerKey.Clear();
        debugLayerVertexScoreByLayerKey.Clear();
        debugLayerGeometryByLayerKey.Clear();
    }

    private bool ShouldCheckAssetDependencyHash(string assetPath)
    {
        if (string.IsNullOrEmpty(cachedAssetDependencyHash))
            return true;
        if (!string.Equals(cachedPath, assetPath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (cachedTexture == null)
            return true;
        if (hasPendingChanges)
            return false;
        if (IsPreviewInteractionActive())
            return false;

        var currentEvent = Event.current;
        if (currentEvent != null && currentEvent.type != EventType.Repaint && currentEvent.type != EventType.Layout)
            return false;

        var now = EditorApplication.timeSinceStartup;
        if (now < nextAssetDependencyHashCheckTime)
            return false;

        nextAssetDependencyHashCheckTime = now + AssetDependencyHashCheckIntervalSeconds;
        return true;
    }

    public bool HasActivePreviewInteraction => IsPreviewInteractionActive();

    private bool IsPreviewInteractionActive()
    {
        return isDragging ||
               isRotating ||
               isResizingBone ||
               spriteRectIsDragging ||
               editGeometryIsDragging ||
               elbowIsCreating ||
               elbowIsDragging ||
               limbIsCreating ||
               limbIsDragging ||
               boobIsDragging ||
               curveIsDragging ||
               weightSliderIsRectSelecting ||
               weightSliderIsWeightDragging ||
               radiusCullIsDragging ||
               radiusCullHandleIsDragging ||
               jointIsDragging ||
               jointStartHandleIsDragging ||
               jointEndHandleIsDragging ||
               jointMiddleHandleIsDragging ||
               jointStartOffsetHandleIsDragging ||
               jointEndOffsetHandleIsDragging;
    }
}

#endif

