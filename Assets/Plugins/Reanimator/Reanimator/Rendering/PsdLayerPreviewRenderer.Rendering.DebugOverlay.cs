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
    private void DrawDebugOverlay(
        PsdCoordinateSpace coordinateMapper,
        IReadOnlyList<LayerInfo> drawList,
        LayerInfo selectedLayer,
        float vertexAlpha,
        float boneAlpha,
        PsdPreviewDrawOptions options)
    {
        if (coordinateMapper == null)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
        {
            // Keep text panel interactive (drag) on mouse/layout events.
            DrawDebugTextBlock(coordinateMapper, selectedLayer, vertexAlpha, boneAlpha, options);
            return;
        }

        var contentColor = new Color(1f, 0.05f, 0.05f, 0.95f);
        var rawColor = new Color(1f, 0.15f, 0.15f, 0.4f);
        var beforeColor = new Color(1f, 0.2f, 0.2f, 0.65f);
        var afterColor = new Color(1f, 0f, 0f, 0.95f);
        var vertexBoundsColor = new Color(1f, 0f, 0f, 0.95f);
        var boneDebugMode = IsDebugBoneOverlayMode(options);

        if (boneDebugMode)
        {
            DrawDebugBoneStatusLabels(coordinateMapper);
        }
        else
        {
            DrawDebugDocRect(coordinateMapper, debugContentBoundsDoc, contentColor, 2.2f);
            DrawDebugDocRect(coordinateMapper, debugRawBoneBoundsDoc, rawColor, 1.2f);
            DrawDebugDocRect(coordinateMapper, debugBoneBoundsDocBefore, beforeColor, 1.6f);
            DrawDebugDocRect(coordinateMapper, debugBoneBoundsDocAfter, afterColor, 2.4f);

            if (drawList != null)
            {
                for (var i = 0; i < drawList.Count; i++)
                {
                    var layer = drawList[i];
                    if (layer == null || !layer.visible)
                        continue;
                    var selectionKey = BuildSelectionKey(layer);
                    var isSelected = selectedLayer != null && string.Equals(selectionKey, BuildSelectionKey(selectedLayer), StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(selectionKey) && debugLayerVertexBoundsDocByLayerKey.TryGetValue(selectionKey, out var vertexBounds))
                        DrawDebugDocRect(coordinateMapper, vertexBounds, vertexBoundsColor, isSelected ? 2.1f : 1.1f);
                }
            }

            DrawDebugLayerStatusLabels(coordinateMapper, drawList, selectedLayer);
        }

        DrawDebugTextBlock(coordinateMapper, selectedLayer, vertexAlpha, boneAlpha, options);
    }

    private void DrawDebugTextBlock(
        PsdCoordinateSpace coordinateMapper,
        LayerInfo selectedLayer,
        float vertexAlpha,
        float boneAlpha,
        PsdPreviewDrawOptions options)
    {
        var canvas = coordinateMapper.CanvasRectView;
        var width = canvas.width - 16f;
        if (width < 180f)
            width = Mathf.Max(120f, canvas.width - 4f);
        width = Mathf.Min(520f, width);
        var textRect = new Rect(canvas.xMin, canvas.yMin, width, 180f);
        var selectedKey = selectedLayer != null ? BuildSelectionKey(selectedLayer) : selectedLayerSelectionKey;
        debugLayerVertexScoreByLayerKey.TryGetValue(selectedKey ?? string.Empty, out var selectedVertexScore);
        var geometryText = BuildSelectedLayerGeometryDebugText(selectedKey);
        var pairsText = BuildSelectedLayerPairDebugText(selectedKey);
        var healthText = BuildDebugHealthText(options, selectedKey, selectedLayer, vertexAlpha, boneAlpha);
        var toolText = BuildToolDebugText(options, selectedKey);

        var text =
            "DEBUG RED PIPELINE\n" +
            $"{healthText}\n" +
            $"{toolText}\n" +
            $"mode: {debugChosenBoneMode}\n" +
            $"scores: LR={debugModeScoreLocalRot:F3} LN={debugModeScoreLocalNoRot:F3} W={debugModeScoreWorld:F3}\n" +
            $"selected_layer: {(string.IsNullOrEmpty(selectedKey) ? "<none>" : selectedKey)}\n" +
            $"selected_layer_vertex_score: {selectedVertexScore:F3}\n" +
            $"{pairsText}\n" +
            $"importer_mesh_preview: {useImporterMeshForPreview}\n" +
            $"importer_mesh_usage: auto_if_binding\n" +
            $"transform_pipeline: unity_local_bones + document_space_geometry\n" +
            $"draw_alpha: bone={boneAlpha:F2} vertex={vertexAlpha:F2}\n" +
            $"content: {RectToDebugString(debugContentBoundsDoc)}\n" +
            $"raw: {RectToDebugString(debugRawBoneBoundsDoc)}\n" +
            $"before: {RectToDebugString(debugBoneBoundsDocBefore)}\n" +
            $"after: {RectToDebugString(debugBoneBoundsDocAfter)}\n" +
            geometryText;

        var style = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            richText = false,
            normal = { textColor = new Color(1f, 0.2f, 0.2f, 1f) }
        };

        var bg = textRect;
        bg.height = style.CalcHeight(new GUIContent(text), textRect.width) + 8f;
        debugOverlayPanelOffset = ClampDebugOverlayOffset(canvas, bg.size, debugOverlayPanelOffset);
        bg.position = canvas.position + debugOverlayPanelOffset;
        HandleDebugOverlayDrag(canvas, ref bg);
        EditorGUI.DrawRect(bg, new Color(0.18f, 0f, 0f, 0.65f));
        var header = new Rect(bg.x, bg.y, bg.width, 18f);
        EditorGUI.DrawRect(header, new Color(0.35f, 0f, 0f, 0.75f));
        GUI.Label(new Rect(bg.x + 4f, bg.y + 4f, bg.width - 8f, bg.height - 8f), text, style);
    }

    private string BuildDebugHealthText(PsdPreviewDrawOptions options, string selectedKey, LayerInfo selectedLayer, float vertexAlpha, float boneAlpha)
    {
        var issues = new List<string>();
        var atlasTexture = GetPreviewAtlasTexture();
        var atlasSize = GetPreviewAtlasSize();
        if (cachedTexture == null)
            issues.Add("PSD texture is missing");
        if (atlasTexture == null)
            issues.Add("preview atlas texture is missing");
        if (cachedDocumentSize.x <= 0 || cachedDocumentSize.y <= 0)
            issues.Add("document size is invalid");
        if (layerInfos == null || layerInfos.Count == 0)
            issues.Add("no PSD layers in preview cache");
        if (bones == null || bones.Count == 0)
            issues.Add("no bones loaded");
        if (!string.IsNullOrEmpty(selectedLayerSelectionKey) && selectedLayer == null && !LayerExistsBySelectionKey(selectedLayerSelectionKey))
            issues.Add("selected layer key does not exist in PSD layer list");
        if (options != null && options.DrawBones && boneAlpha <= 0.0001f)
            issues.Add("bone visibility is zero");
        if (vertexAlpha <= 0.0001f)
            issues.Add("vertex visibility is zero");

        AppendAtlasRectIssues(issues, atlasSize);
        AppendSelectedLayerDataIssues(issues, selectedKey);

        var issueText = issues.Count == 0 ? "OK" : string.Join(" | ", issues.ToArray());
        var textureName = atlasTexture != null ? atlasTexture.name : "<none>";
        var selectedLayerName = selectedLayer != null ? selectedLayer.name : "<none>";
        return
            $"health: {issueText}\n" +
            $"cache: psd={VectorToDebugString(new Vector2(cachedDocumentSize.x, cachedDocumentSize.y))} atlas={atlasSize.x}x{atlasSize.y} tex={textureName} layers={(layerInfos != null ? layerInfos.Count : 0)} vertex_layers={(vertexDataByLayerSelectionKey != null ? vertexDataByLayerSelectionKey.Count : 0)} bones={(bones != null ? bones.Count : 0)}\n" +
            $"selection: layer={selectedLayerName} key={(string.IsNullOrEmpty(selectedKey) ? "<none>" : selectedKey)} bone={DescribeSelectedDebugBone()}";
    }

    private void AppendAtlasRectIssues(List<string> issues, Vector2Int atlasSize)
    {
        if (issues == null || layerInfos == null)
            return;

        var atlasRect = new Rect(0f, 0f, Mathf.Max(1, atlasSize.x), Mathf.Max(1, atlasSize.y));
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;

            var rect = PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(atlasSize.x, atlasSize.y));
            if (rect.width <= 0f || rect.height <= 0f)
            {
                issues.Add($"layer '{layer.name}' has empty texture rect");
                continue;
            }

            if (rect.xMin < -0.001f || rect.yMin < -0.001f || rect.xMax > atlasRect.xMax + 0.001f || rect.yMax > atlasRect.yMax + 0.001f)
                issues.Add($"layer '{layer.name}' texture rect is outside atlas");
        }
    }

    private void AppendSelectedLayerDataIssues(List<string> issues, string selectedKey)
    {
        if (issues == null || string.IsNullOrEmpty(selectedKey))
            return;

        if (!vertexDataByLayerSelectionKey.TryGetValue(selectedKey, out var data) || data == null)
        {
            issues.Add("selected layer has no vertex data");
            return;
        }

        AppendLayerDataIssues(issues, data, "selected");
    }

    private void AppendLayerDataIssues(List<string> issues, LayerVertexData data, string label)
    {
        if (issues == null || data == null)
            return;

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        var triangleIndexCount = data.triangles != null ? data.triangles.Length : 0;
        if (vertexCount <= 0)
            issues.Add($"{label} layer has no vertices");
        if (triangleIndexCount > 0 && triangleIndexCount % 3 != 0)
            issues.Add($"{label} layer triangle index count is not divisible by 3");
        if (data.vertexUvs == null || data.vertexUvs.Length != vertexCount)
            issues.Add($"{label} layer UV count does not match vertices");
        else if (!AreLayerVertexUvsUsable(data))
            issues.Add($"{label} layer UVs are outside texture rect or invalid");

        if (data.hasSkinning)
        {
            if (data.boneWeights == null || data.boneWeights.Length != vertexCount)
                issues.Add($"{label} layer bone weight count does not match vertices");
            if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
                issues.Add($"{label} layer has skinning but no sprite bone map");
            if (data.bindBoneInverseMatrices == null || data.spriteBoneToRendererBone == null || data.bindBoneInverseMatrices.Length != data.spriteBoneToRendererBone.Length)
                issues.Add($"{label} layer bind pose count does not match sprite bone map");
        }
    }

    private void DrawDebugLayerStatusLabels(
        PsdCoordinateSpace coordinateMapper,
        IReadOnlyList<LayerInfo> drawList,
        LayerInfo selectedLayer)
    {
        if (coordinateMapper == null || drawList == null)
            return;

        var selectedKey = selectedLayer != null ? BuildSelectionKey(selectedLayer) : selectedLayerSelectionKey;
        var labelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };

        var atlasSize = GetPreviewAtlasSize();
        var canvas = coordinateMapper.CanvasRectView;
        for (var i = 0; i < drawList.Count; i++)
        {
            var layer = drawList[i];
            if (layer == null || !layer.visible)
                continue;

            var layerRect = coordinateMapper.LayerToViewRect(layer);
            if (!layerRect.Overlaps(canvas))
                continue;

            var key = BuildSelectionKey(layer);
            var isSelected = !string.IsNullOrEmpty(key) && string.Equals(key, selectedKey, StringComparison.OrdinalIgnoreCase);
            var status = BuildDebugLayerStatusText(GetLayerDebugStatus(layer, key, atlasSize));
            var text = $"{ShortenDebugLayerName(layer.name)} {status}";
            var textSize = labelStyle.CalcSize(new GUIContent(text));
            var width = Mathf.Clamp(textSize.x + 8f, 42f, Mathf.Max(42f, layerRect.width));
            var height = 17f;
            var labelRect = new Rect(
                Mathf.Clamp(layerRect.xMin, canvas.xMin, Mathf.Max(canvas.xMin, canvas.xMax - width)),
                Mathf.Clamp(layerRect.yMin - height - 2f, canvas.yMin, Mathf.Max(canvas.yMin, canvas.yMax - height)),
                width,
                height);

            var hasIssue = !status.StartsWith("OK", StringComparison.Ordinal);
            var bgColor = hasIssue
                ? new Color(0.7f, 0f, 0f, isSelected ? 0.95f : 0.78f)
                : new Color(0f, 0.28f, 0.05f, isSelected ? 0.9f : 0.65f);
            EditorGUI.DrawRect(labelRect, bgColor);
            if (isSelected)
                DrawRectOutline(labelRect, new Color(1f, 1f, 1f, 0.9f), 1f);
            GUI.Label(new Rect(labelRect.x + 4f, labelRect.y, labelRect.width - 8f, labelRect.height), text, labelStyle);
        }
    }

    private bool IsDebugBoneOverlayMode(PsdPreviewDrawOptions options)
    {
        return options != null &&
               !options.SpriteAtlasMode &&
               bones != null &&
               bones.Count > 0 &&
               options.DebugOverlayMode == PsdPreviewDebugOverlayMode.Bone;
    }

    private void DrawDebugBoneStatusLabels(PsdCoordinateSpace coordinateMapper)
    {
        if (coordinateMapper == null || bones == null || bones.Count == 0)
            return;
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        Handles.BeginGUI();
        var previousColor = Handles.color;
        var canvas = coordinateMapper.CanvasRectView;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null || !bone.visible)
                continue;

            var start = coordinateMapper.DocumentToView(bone.worldPosition);
            var end = coordinateMapper.DocumentToView(GetBoneEnd(i));
            if (!IsPreviewSegmentVisible(start, end, canvas, 24f))
                continue;

            var selected = i == selectedBoneIndex;
            var color = selected ? new Color(1f, 0.48f, 0f, 1f) : new Color(1f, 0.15f, 0.15f, 0.92f);
            DrawDebugBoneRect(start, end, color, selected ? 2.2f : 1.2f);

            if (!TryGetBoneDebugInfo(i, out var info))
                continue;

            DrawDebugBoneLabel(canvas, start, end, info, selected);
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private static void DrawDebugBoneRect(Vector2 start, Vector2 end, Color color, float thickness)
    {
        var bounds = BuildDebugBoneViewRect(start, end, 8f);
        var p0 = new Vector2(bounds.xMin, bounds.yMin);
        var p1 = new Vector2(bounds.xMax, bounds.yMin);
        var p2 = new Vector2(bounds.xMax, bounds.yMax);
        var p3 = new Vector2(bounds.xMin, bounds.yMax);
        Handles.color = color;
        Handles.DrawAAPolyLine(thickness, p0, p1, p2, p3, p0);
    }

    private void DrawDebugBoneLabel(Rect canvas, Vector2 start, Vector2 end, BoneDebugInfo info, bool selected)
    {
        var text = BuildDebugBoneLabelText(info, selected);
        var style = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };

        var labelWidth = selected ? 260f : 190f;
        var height = Mathf.Clamp(style.CalcHeight(new GUIContent(text), labelWidth) + 6f, 18f, selected ? 110f : 62f);
        var mid = (start + end) * 0.5f;
        var labelRect = new Rect(mid.x + 8f, mid.y - height * 0.5f, labelWidth, height);
        labelRect.x = Mathf.Clamp(labelRect.x, canvas.xMin, Mathf.Max(canvas.xMin, canvas.xMax - labelRect.width));
        labelRect.y = Mathf.Clamp(labelRect.y, canvas.yMin, Mathf.Max(canvas.yMin, canvas.yMax - labelRect.height));

        var bg = info.HasIssues
            ? new Color(0.55f, 0f, 0f, selected ? 0.9f : 0.72f)
            : new Color(0.05f, 0.25f, 0.08f, selected ? 0.86f : 0.62f);
        EditorGUI.DrawRect(labelRect, bg);
        if (selected)
            DrawRectOutline(labelRect, new Color(1f, 0.92f, 0.25f, 0.95f), 1f);
        GUI.Label(new Rect(labelRect.x + 4f, labelRect.y + 3f, labelRect.width - 8f, labelRect.height - 6f), text, style);
    }

    private static Rect BuildDebugBoneViewRect(Vector2 start, Vector2 end, float padding)
    {
        return Rect.MinMaxRect(
            Mathf.Min(start.x, end.x) - padding,
            Mathf.Min(start.y, end.y) - padding,
            Mathf.Max(start.x, end.x) + padding,
            Mathf.Max(start.y, end.y) + padding);
    }

    private static string BuildDebugBoneLabelText(BoneDebugInfo info, bool selected)
    {
        var issues = BuildDebugBoneIssueText(info.issues);
        var sprites = BuildDebugBoneSpriteListText(info.sprites, selected ? 6 : 3);
        var header = $"{info.rendererBoneIndex}: {info.boneName}";
        if (!selected)
            return $"{header}\n{sprites}";

        return
            $"{header}\n" +
            $"issues: {issues}\n" +
            $"rest: {VectorToDebugString(info.restPosition)} world: {VectorToDebugString(info.worldPosition)}\n" +
            $"sprites: {sprites}";
    }

    private static string BuildDebugBoneSpriteListText(IReadOnlyList<BoneDebugSpriteInfo> sprites, int maxCount)
    {
        if (sprites == null || sprites.Count == 0)
            return "sprites: <none>";

        var names = new List<string>(Mathf.Min(maxCount, sprites.Count));
        for (var i = 0; i < sprites.Count && i < maxCount; i++)
        {
            var sprite = sprites[i];
            if (sprite.influencedVertexCount <= 0)
                continue;

            names.Add($"{ShortenDebugLayerName(sprite.layerName)}({sprite.influencedVertexCount})");
        }

        if (names.Count == 0)
            return "sprites: mapped/no weights";
        if (sprites.Count > maxCount)
            names.Add($"...+{sprites.Count - maxCount}");
        return string.Join(", ", names.ToArray());
    }

    private static string BuildDebugBoneIssueText(BoneDebugIssueFlags issues)
    {
        if (issues == BoneDebugIssueFlags.None)
            return "OK";

        var parts = new List<string>();
        if ((issues & BoneDebugIssueFlags.Hidden) != 0) parts.Add("hidden");
        if ((issues & BoneDebugIssueFlags.MissingParent) != 0) parts.Add("parent");
        if ((issues & BoneDebugIssueFlags.ZeroLength) != 0) parts.Add("length");
        if ((issues & BoneDebugIssueFlags.PoseChanged) != 0) parts.Add("pose");
        if ((issues & BoneDebugIssueFlags.NoWeightedSprites) != 0) parts.Add("no weights");
        if ((issues & BoneDebugIssueFlags.MissingGeometry) != 0) parts.Add("geo");
        if ((issues & BoneDebugIssueFlags.WeightIssue) != 0) parts.Add("weights");
        if ((issues & BoneDebugIssueFlags.BoneMapIssue) != 0) parts.Add("bone map");
        if ((issues & BoneDebugIssueFlags.BindPoseIssue) != 0) parts.Add("bind");
        return string.Join(", ", parts.ToArray());
    }

    public LayerDebugStatus GetLayerDebugStatus(LayerInfo layer)
    {
        var selectionKey = layer != null ? BuildSelectionKey(layer) : string.Empty;
        return GetLayerDebugStatus(layer, selectionKey, GetPreviewAtlasSize());
    }

    private LayerDebugStatus GetLayerDebugStatus(LayerInfo layer, string selectionKey, Vector2Int atlasSize)
    {
        if (layer == null)
            return new LayerDebugStatus(LayerDebugIssueFlags.BadLayer, 0, 0, string.Empty);

        var issues = LayerDebugIssueFlags.None;

        var atlasRect = new Rect(0f, 0f, Mathf.Max(1, atlasSize.x), Mathf.Max(1, atlasSize.y));
        var textureRect = PsdCoordinateSpace.LayerTextureRect(layer, new Vector2(atlasSize.x, atlasSize.y));
        if (textureRect.width <= 0f || textureRect.height <= 0f)
            issues |= LayerDebugIssueFlags.TextureRectIssue;
        else if (textureRect.xMin < -0.001f || textureRect.yMin < -0.001f || textureRect.xMax > atlasRect.xMax + 0.001f || textureRect.yMax > atlasRect.yMax + 0.001f)
            issues |= LayerDebugIssueFlags.TextureRectIssue;

        if (string.IsNullOrEmpty(selectionKey) || !vertexDataByLayerSelectionKey.TryGetValue(selectionKey, out var data) || data == null)
        {
            issues |= LayerDebugIssueFlags.MissingGeometry;
            return new LayerDebugStatus(issues, 0, 0, string.Empty);
        }

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        var triangleIndexCount = data.triangles != null ? data.triangles.Length : 0;
        if (vertexCount <= 0)
            issues |= LayerDebugIssueFlags.EmptyGeometry;
        if (triangleIndexCount <= 0)
            issues |= LayerDebugIssueFlags.TriangleIssue;
        else if (triangleIndexCount % 3 != 0)
            issues |= LayerDebugIssueFlags.TriangleIssue;
        if (data.vertexUvs == null || data.vertexUvs.Length != vertexCount)
            issues |= LayerDebugIssueFlags.UvIssue;
        else if (!AreLayerVertexUvsUsable(data))
            issues |= LayerDebugIssueFlags.UvIssue;

        if (data.hasSkinning)
        {
            if (data.boneWeights == null || data.boneWeights.Length != vertexCount)
                issues |= LayerDebugIssueFlags.WeightIssue;
            if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
                issues |= LayerDebugIssueFlags.BoneMapIssue;
            if (data.bindBoneInverseMatrices == null || data.spriteBoneToRendererBone == null || data.bindBoneInverseMatrices.Length != data.spriteBoneToRendererBone.Length)
                issues |= LayerDebugIssueFlags.BindPoseIssue;
            if (!IsCurrentBonePoseRest() && HasSkinningPoseIssue(data))
                issues |= LayerDebugIssueFlags.SkinningPoseIssue;
        }

        var triangleCount = triangleIndexCount / 3;
        var uvSource = debugLayerGeometryByLayerKey != null &&
                       !string.IsNullOrEmpty(selectionKey) &&
                       debugLayerGeometryByLayerKey.TryGetValue(selectionKey, out var debugInfo) &&
                       debugInfo != null
            ? debugInfo.uvSource
            : "uv";
        return new LayerDebugStatus(issues, vertexCount, triangleCount, uvSource);
    }

    private static string BuildDebugLayerStatusText(LayerDebugStatus status)
    {
        if (status.issues != LayerDebugIssueFlags.None)
        {
            var issues = new List<string>();
            if ((status.issues & LayerDebugIssueFlags.BadLayer) != 0)
                issues.Add("BAD_LAYER");
            if ((status.issues & LayerDebugIssueFlags.TextureRectIssue) != 0)
                issues.Add("RECT");
            if ((status.issues & LayerDebugIssueFlags.MissingGeometry) != 0)
                issues.Add("NO_GEO");
            if ((status.issues & LayerDebugIssueFlags.EmptyGeometry) != 0)
                issues.Add("V0");
            if ((status.issues & LayerDebugIssueFlags.TriangleIssue) != 0)
                issues.Add("TRI");
            if ((status.issues & LayerDebugIssueFlags.UvIssue) != 0)
                issues.Add("UV");
            if ((status.issues & LayerDebugIssueFlags.WeightIssue) != 0)
                issues.Add("W");
            if ((status.issues & LayerDebugIssueFlags.BoneMapIssue) != 0)
                issues.Add("BONE");
            if ((status.issues & LayerDebugIssueFlags.BindPoseIssue) != 0)
                issues.Add("BIND");
            if ((status.issues & LayerDebugIssueFlags.SkinningPoseIssue) != 0)
                issues.Add("SKIN");
            return string.Join(" ", issues.ToArray());
        }

        return $"OK v{status.vertexCount} t{status.triangleCount} {status.uvSource}";
    }

    private bool HasSkinningPoseIssue(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return true;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return true;

        for (var i = 0; i < drawVertices.Length; i++)
        {
            var p = drawVertices[i];
            if (!IsFinite(p.x) || !IsFinite(p.y))
                return true;
        }

        if (!TryGetBounds(data.vertices, out var baseBounds) ||
            !TryGetBounds(drawVertices, out var drawBounds))
            return true;

        var baseReference = Mathf.Max(1f, Mathf.Max(baseBounds.width, baseBounds.height));
        var drawReference = Mathf.Max(drawBounds.width, drawBounds.height);
        return drawReference > baseReference * 8f;
    }

    private static string ShortenDebugLayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "<unnamed>";
        const int maxLength = 18;
        return name.Length <= maxLength ? name : name.Substring(0, maxLength - 1) + ".";
    }

    private string BuildToolDebugText(PsdPreviewDrawOptions options, string selectedKey)
    {
        var toolName = GetActiveDebugToolName(options);
        var issues = new List<string>();
        var details = new List<string>();

        if (options == null)
        {
            issues.Add("draw options are missing");
        }
        else if (options.SpriteAtlasMode)
        {
            details.Add("sprite editor mode: atlas rect editing active");
            if (string.IsNullOrEmpty(selectedKey))
                issues.Add("no sprite rect selected");
        }
        else if (options.ActivePreviewModule != null)
        {
            var moduleId = options.ActivePreviewModule.Id ?? string.Empty;
            details.Add($"preview module id={moduleId}");

            if (string.Equals(moduleId, "weights.brush", StringComparison.OrdinalIgnoreCase))
            {
                AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
                if (TryGetWeightBrushTargetInfo(out var target))
                    details.Add($"brush_target={target.layerName} sprite_bones={target.spriteBoneCount} vertices={target.vertexCount}");
            }
            else if (string.Equals(moduleId, "weights.projector", StringComparison.OrdinalIgnoreCase))
            {
                AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
                var candidates = GetProjectorCandidates(out var targetName, out var targetKey, out var error);
                details.Add($"projector_target={(string.IsNullOrEmpty(targetName) ? "<none>" : targetName)} key={(string.IsNullOrEmpty(targetKey) ? "<none>" : targetKey)} candidates={(candidates != null ? candidates.Length : 0)}");
                if (!string.IsNullOrWhiteSpace(error))
                    issues.Add(error);
            }
            else if (string.Equals(moduleId, "geometry.vertex-cull", StringComparison.OrdinalIgnoreCase))
            {
                AppendSelectedGeometryRequirement(issues, selectedKey);
                if (TryGetVertexCullTargetInfo(out var layerName, out var vertexCount, out var error))
                    details.Add($"vertex_cull_target={layerName} vertices={vertexCount}");
                else if (!string.IsNullOrWhiteSpace(error))
                    issues.Add(error);
            }
            else
            {
                AppendSelectedLayerRequirement(issues, selectedKey);
            }
        }
        else
        {
            AppendToolDebugIssues(options.Tools, selectedKey, issues, details);
        }

        var issueText = issues.Count == 0 ? "OK" : string.Join(" | ", issues.ToArray());
        var detailText = details.Count == 0 ? "<none>" : string.Join(" | ", details.ToArray());
        return $"tool: {toolName}\ntool_issues: {issueText}\ntool_state: {detailText}";
    }

    private void AppendToolDebugIssues(PsdPreviewToolOptions tools, string selectedKey, List<string> issues, List<string> details)
    {
        if (tools == null)
        {
            issues.Add("tool flags are missing");
            return;
        }

        if (tools.AutoGeometry)
        {
            AppendSelectedLayerRequirement(issues, selectedKey);
            details.Add("requires selected sprite, readable texture data, valid sprite rect");
        }
        else if (tools.EditGeometry || tools.CreateEdge || tools.SplitEdge)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add($"selection_vertices={(editGeometrySelectedVertexIndices != null ? editGeometrySelectedVertexIndices.Count : 0)} create_edge_start={(HasCreateEdgeStartVertex() ? "set" : "none")}");
        }
        else if (tools.WeightSlider)
        {
            AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
            details.Add($"weight_slider_selection={(editGeometrySelectedVertexIndices != null ? editGeometrySelectedVertexIndices.Count : 0)}");
        }
        else if (tools.BoneInfluence)
        {
            AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
            if (selectedBoneIndex < 0 || selectedBoneIndex >= (bones != null ? bones.Count : 0))
                issues.Add("no selected renderer bone");
            if (TryGetBoneInfluenceInfo(out var info))
                details.Add($"bone={info.boneName} influenced={info.influencedVertexCount}/{info.vertexCount} layers={(info.layers != null ? info.layers.Length : 0)}");
        }
        else if (tools.SpriteInfluence)
        {
            AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
            if (TryGetSpriteInfluenceInfo(out var info))
                details.Add($"sprite={info.layerName} bones={(info.bones != null ? info.bones.Length : 0)} vertices={info.vertexCount}");
        }
        else if (tools.Curve)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add("curve needs selected geometry and mapped bones for weight projection");
        }
        else if (tools.Limb)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add($"limb_lines={(limbLines != null ? limbLines.Count : 0)} selected_bone={DescribeSelectedDebugBone()}");
        }
        else if (tools.Elbow)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add($"elbow_lines={(elbowLines != null ? elbowLines.Count : 0)} selected_bone={DescribeSelectedDebugBone()}");
        }
        else if (tools.Boob)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add($"boob_point={(boobHasPoint ? VectorToDebugString(boobCenterDoc) : "<none>")} stages={boobStages} outline={boobOutline:F2}");
        }
        else if (tools.Joint)
        {
            AppendSelectedWeightedGeometryRequirement(issues, selectedKey);
            details.Add($"joint_angles={jointStartAngleRad:F2}->{jointEndAngleRad:F2} lock={jointCenterLockEnabled} nearest={jointNearestBoneIndex}");
        }
        else if (tools.RadiusCull)
        {
            AppendSelectedGeometryRequirement(issues, selectedKey);
            details.Add($"radius_override={(radiusCullHasRadiusOverride ? radiusCullRadiusOverride.ToString("F2") : "<panel>")}");
        }
        else
        {
            details.Add("default selection/bone interaction");
        }
    }

    private void AppendSelectedLayerRequirement(List<string> issues, string selectedKey)
    {
        if (string.IsNullOrEmpty(selectedKey))
            issues.Add("no selected PSD sprite/layer");
    }

    private void AppendSelectedGeometryRequirement(List<string> issues, string selectedKey)
    {
        AppendSelectedLayerRequirement(issues, selectedKey);
        if (string.IsNullOrEmpty(selectedKey))
            return;

        if (!vertexDataByLayerSelectionKey.TryGetValue(selectedKey, out var data) || data == null || data.vertices == null || data.vertices.Length == 0)
            issues.Add("selected sprite has no geometry");
    }

    private void AppendSelectedWeightedGeometryRequirement(List<string> issues, string selectedKey)
    {
        AppendSelectedGeometryRequirement(issues, selectedKey);
        if (string.IsNullOrEmpty(selectedKey))
            return;

        if (!vertexDataByLayerSelectionKey.TryGetValue(selectedKey, out var data) || data == null)
            return;

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        if (data.boneWeights == null || data.boneWeights.Length != vertexCount)
            issues.Add("selected sprite weights do not match geometry");
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            issues.Add("selected sprite has no sprite-bone mapping");
    }

    private string GetActiveDebugToolName(PsdPreviewDrawOptions options)
    {
        if (options == null)
            return "<no options>";
        if (options.SpriteAtlasMode)
            return "Sprite Editor";
        if (options.ActivePreviewModule != null)
            return options.ActivePreviewModule.Id;

        var tools = options.Tools;
        if (tools == null)
            return "<no tools>";
        if (tools.AutoGeometry) return "Auto Geometry";
        if (tools.Elbow) return "Elbow";
        if (tools.Limb) return "Limbs";
        if (tools.Boob) return "Boob";
        if (tools.Curve) return "Curve";
        if (tools.WeightSlider) return "Weight Slider";
        if (tools.CreateEdge) return "Create Edge";
        if (tools.SplitEdge) return "Split Edge";
        if (tools.EditGeometry) return "Edit Geometry";
        if (tools.RadiusCull) return "Circle Clean";
        if (tools.Joint) return "Joint";
        if (tools.BoneInfluence) return "Bone Influence";
        if (tools.SpriteInfluence) return "Sprite Influence";
        return "Default";
    }

    private string DescribeSelectedDebugBone()
    {
        if (selectedBoneIndex < 0 || selectedBoneIndex >= (bones != null ? bones.Count : 0))
            return "<none>";

        var bone = bones[selectedBoneIndex];
        var name = bone != null && !string.IsNullOrWhiteSpace(bone.name) ? bone.name : $"bone_{selectedBoneIndex}";
        return $"{selectedBoneIndex}:{name}";
    }

    private string BuildSelectedLayerPairDebugText(string selectedKey)
    {
        if (string.IsNullOrEmpty(selectedKey) ||
            !vertexDataByLayerSelectionKey.TryGetValue(selectedKey, out var data) ||
            data == null)
            return "pairs: <no data>";

        var vertexCount = data.vertices != null ? data.vertices.Length : 0;
        var pairSource = ResolvePreviewEdgePairs(data, vertexCount, out var allPairs, out var boundaryPairs, out var contourEdgeKeys);

        var allPairCount = allPairs != null ? allPairs.Length / 2 : 0;
        var boundaryPairCount = boundaryPairs != null ? boundaryPairs.Length / 2 : 0;
        var innerPairCount = CountInnerPairs(allPairs, contourEdgeKeys, vertexCount);
        var triangleCount = data.triangles != null ? data.triangles.Length / 3 : 0;

        return $"pairs: src={pairSource} all={allPairCount} boundary={boundaryPairCount} inner={innerPairCount} tris={triangleCount}";
    }

    private void HandleDebugOverlayDrag(Rect canvas, ref Rect panelRect)
    {
        var e = Event.current;
        if (e == null)
            return;

        const float headerHeight = 18f;
        var headerRect = new Rect(panelRect.x, panelRect.y, panelRect.width, headerHeight);

        if (e.type == EventType.MouseDown && e.button == 0 && headerRect.Contains(e.mousePosition))
        {
            debugOverlayDragging = true;
            debugOverlayDragStartMouse = e.mousePosition;
            debugOverlayDragStartOffset = debugOverlayPanelOffset;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            debugOverlayDragging = false;
            return;
        }

        if (e.type != EventType.MouseDrag || !debugOverlayDragging)
            return;

        var delta = e.mousePosition - debugOverlayDragStartMouse;
        debugOverlayPanelOffset = ClampDebugOverlayOffset(canvas, panelRect.size, debugOverlayDragStartOffset + delta);
        panelRect.position = canvas.position + debugOverlayPanelOffset;
        GUI.changed = true;
        e.Use();
    }

    private static Vector2 ClampDebugOverlayOffset(Rect canvas, Vector2 panelSize, Vector2 offset)
    {
        var maxX = Mathf.Max(0f, canvas.width - panelSize.x);
        var maxY = Mathf.Max(0f, canvas.height - panelSize.y);
        return new Vector2(
            Mathf.Clamp(offset.x, 0f, maxX),
            Mathf.Clamp(offset.y, 0f, maxY));
    }

    private string BuildSelectedLayerGeometryDebugText(string selectedKey)
    {
        if (string.IsNullOrEmpty(selectedKey) ||
            debugLayerGeometryByLayerKey == null ||
            !debugLayerGeometryByLayerKey.TryGetValue(selectedKey, out var info) ||
            info == null)
            return "geo_debug: <no data>";

        var rawToLocalRule = GetRawToLocalRule(info.sourceKind);
        return
            $"geo_source: {info.sourceKind} raw_space:{info.rawSpace} importer:{info.usedImporterMesh}\n" +
            $"geo_uv: {info.uvSource} bounds:{RectToDebugString(info.uvBounds)} sample:{VectorToDebugString(info.uvSample)}\n" +
            $"geo_rule: {rawToLocalRule}; world=layer_pos+local\n" +
            $"geo_layer_pos: {VectorToDebugString(info.layerPosition)} src_rect:{RectToDebugString(info.layerSourceRect)}\n" +
            $"geo_ppu_pivot: ppu={info.pixelsPerUnit:F3} pivot={VectorToDebugString(info.pivotPixels)} tex={VectorToDebugString(info.textureSize)}\n" +
            $"geo_sprite_rect: {RectToDebugString(info.spriteRect)} tex_rect:{RectToDebugString(info.spriteTextureRect)}\n" +
            $"geo_bounds_raw: {RectToDebugString(info.rawBounds)}\n" +
            $"geo_bounds_local: {RectToDebugString(info.localBounds)}\n" +
            $"geo_bounds_world: {RectToDebugString(info.worldBounds)}\n" +
            $"geo_sample raw:{VectorToDebugString(info.rawSample)} local:{VectorToDebugString(info.localSample)} world:{VectorToDebugString(info.worldSample)}";
    }

    private static string GetRawToLocalRule(string sourceKind)
    {
        if (string.Equals(sourceKind, "sprite_uv", StringComparison.Ordinal))
            return "local=(uv*texture_size)-source_rect.xy";
        if (string.Equals(sourceKind, "sprite_vertices", StringComparison.Ordinal))
            return "local=(vertex*ppu)+pivot";
        if (string.Equals(sourceKind, "importer_vertices", StringComparison.Ordinal) ||
            string.Equals(sourceKind, "importer_vertices_auto", StringComparison.Ordinal))
            return "local=importer_vertex";
        return "local=<unknown>";
    }

    private static void DrawDebugDocRect(PsdCoordinateSpace mapper, Rect docRect, Color color, float thickness)
    {
        if (mapper == null || docRect.width <= 0f || docRect.height <= 0f)
            return;

        var p0 = mapper.DocToView(new Vector2(docRect.xMin, docRect.yMin));
        var p1 = mapper.DocToView(new Vector2(docRect.xMax, docRect.yMin));
        var p2 = mapper.DocToView(new Vector2(docRect.xMax, docRect.yMax));
        var p3 = mapper.DocToView(new Vector2(docRect.xMin, docRect.yMax));

        Handles.BeginGUI();
        var prev = Handles.color;
        Handles.color = color;
        Handles.DrawAAPolyLine(thickness, p0, p1, p2, p3, p0);
        Handles.color = prev;
        Handles.EndGUI();
    }

    private static Func<Vector2, Vector2> ResolveLayerVertexToViewMapper(
        PsdCoordinateSpace mapper,
        LayerInfo layer,
        Func<Vector2, Vector2> fallback)
    {
        if (mapper == null || layer == null)
            return fallback;

        return mapper.CreateLayerDocumentToViewMapper(layer, fallback);
    }

    private static string RectToDebugString(Rect rect)
    {
        return $"[{rect.xMin:F1},{rect.yMin:F1}] {rect.width:F1}x{rect.height:F1}";
    }

    private static string VectorToDebugString(Vector2 value)
    {
        return $"[{value.x:F2},{value.y:F2}]";
    }

    private bool LayerExistsBySelectionKey(string key)
    {
        if (string.IsNullOrEmpty(key) || layerInfos == null)
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (string.Equals(BuildSelectionKey(layer), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
#endif
