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
    private void DrawBones(PsdCoordinateSpace coordinates, float alpha, PsdPreviewDrawOptions options)
    {
        Handles.BeginGUI();
        var prev = Handles.color;
        var baseAlpha = Mathf.Clamp01(alpha);

        DrawBoneHierarchyLinks(coordinates, baseAlpha, options);

        var drawOrder = boneDrawOrder.Count == bones.Count ? boneDrawOrder : null;
        var count = drawOrder != null ? drawOrder.Count : bones.Count;
        for (var i = 0; i < count; i++)
        {
            var boneIndex = drawOrder != null ? drawOrder[i] : i;
            var bone = bones[boneIndex];
            if (!bone.visible)
                continue;
            var start = coordinates.DocumentToView(bone.worldPosition);
            var rotated = bone.worldRotation * Vector3.right;
            var dir = new Vector2(rotated.x, rotated.y);
            var end = coordinates.DocumentToView(bone.worldPosition + dir * bone.length);
            if (!IsPreviewSegmentVisible(start, end, coordinates.CanvasRectView))
                continue;

            var boneColor = bone.color;
            boneColor.a = boneColor.a * baseAlpha;
            if (TryGetBoneDisplayOverride(options, boneIndex, out var displayOverride))
            {
                boneColor = displayOverride.Color;
                boneColor.a = Mathf.Clamp01(displayOverride.Color.a * baseAlpha * displayOverride.AlphaMultiplier);
            }

            var isSelected = boneIndex == selectedBoneIndex;
            var isHovered = boneIndex == hoveredBoneIndex || boneIndex == panelHoveredBoneIndex;
            DrawBoneShape(start, end, boneColor, baseAlpha, isSelected, isHovered);
            if (options.AllowBoneLengthEditing && isSelected)
                DrawBoneLengthHandle(end, baseAlpha, isResizingBone);
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private static bool TryGetBoneDisplayOverride(
        PsdPreviewDrawOptions options,
        int rendererBoneIndex,
        out ReanimatorBoneDisplayOverride displayOverride)
    {
        displayOverride = default;
        return options != null &&
               options.ActivePreviewModule is IReanimatorBoneDisplayModule boneDisplayModule &&
               boneDisplayModule.TryGetBoneDisplayOverride(options.Api, rendererBoneIndex, out displayOverride);
    }

    private static void DrawBoneShape(Vector2 start, Vector2 end, Color boneColor, float alpha, bool isSelected, bool isHovered)
    {
        var thickness = isSelected ? 3f : isHovered ? 2.8f : 2f;
        var outlineThickness = thickness + 0.5f;
        var outlineColor = isSelected || isHovered
            ? new Color(1f, 0.9f, 0.2f, Mathf.Clamp01(alpha))
            : new Color(0f, 0f, 0f, Mathf.Clamp01(alpha * 0.95f));

        Handles.color = outlineColor;
        BoneShapeDrawer.Draw(start, end, outlineThickness);

        Handles.color = boneColor;
        BoneShapeDrawer.Draw(start, end, thickness);

        Handles.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
        var baseDotRadius = Mathf.Max(3.2f, BoneShapeDrawer.GetBaseRadius(thickness) * 0.52f);
        Handles.DrawSolidDisc(start, Vector3.forward, baseDotRadius);
    }

    private static void DrawBoneLengthHandle(Vector2 end, float alpha, bool isActive)
    {
        Handles.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
        Handles.DrawSolidDisc(end, Vector3.forward, 6f);
        Handles.color = isActive
            ? new Color(1f, 0.55f, 0.08f, Mathf.Clamp01(alpha))
            : new Color(1f, 0.9f, 0.2f, Mathf.Clamp01(alpha));
        Handles.DrawSolidDisc(end, Vector3.forward, 4.2f);
    }

    private void DrawBoneHierarchyLinks(PsdCoordinateSpace coordinates, float alpha, PsdPreviewDrawOptions options)
    {
        const float minGapPixels = 8f;
        const float dashSize = 4f;

        for (var i = 0; i < bones.Count; i++)
        {
            var child = bones[i];
            if (!child.visible)
                continue;
            if (child.parentId < 0 || child.parentId >= bones.Count)
                continue;

            var parent = bones[child.parentId];
            if (!parent.visible)
                continue;

            var childBase = coordinates.DocumentToView(child.worldPosition);
            var parentTip = coordinates.DocumentToView(GetBoneEnd(child.parentId));
            if (!IsPreviewSegmentVisible(childBase, parentTip, coordinates.CanvasRectView))
                continue;
            if (Vector2.Distance(childBase, parentTip) <= minGapPixels)
                continue;

            var linkColor = child.color;
            linkColor.a = Mathf.Clamp01(0.75f * alpha);
            if (TryGetBoneDisplayOverride(options, i, out var displayOverride))
            {
                linkColor = displayOverride.Color;
                linkColor.a = Mathf.Clamp01(displayOverride.Color.a * alpha * displayOverride.AlphaMultiplier * 0.75f);
            }

            Handles.color = linkColor;
            Handles.DrawDottedLine(childBase, parentTip, dashSize);
        }
    }

    private void DrawVertices(PsdCoordinateSpace coordinateMapper, float alpha)
    {
        if (alpha <= 0f || string.IsNullOrEmpty(selectedLayerSelectionKey))
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;
        if (!vertexDataByLayerSelectionKey.TryGetValue(selectedLayerSelectionKey, out var data))
            return;
        if (data.layer == null || !data.layer.visible || data.vertices == null || data.vertices.Length == 0)
            return;
        var selectedLayerBounds = GetLayerSelectionViewRect(data.layer, coordinateMapper);
        if (!IsPreviewRectVisible(selectedLayerBounds, coordinateMapper.CanvasRectView))
            return;

        Handles.BeginGUI();
        var prev = Handles.color;
        var innerLineColor = new Color(0.55f, 0.92f, 1f, alpha * 0.9f);
        var boundaryLineColor = new Color(1f, 0.56f, 0.08f, alpha * 0.9f);
        var pointColor = new Color(0.55f, 0.92f, 1f, alpha);
        const float markerHalfSize = 2f;
        const float thickOffset = 0.65f;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
        {
            Handles.color = prev;
            Handles.EndGUI();
            return;
        }
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinateMapper, data, drawVertices);
        var layerStableKey = BuildKey(data.layer.name, data.layer.layerId);
        UpdateElbowLinesForLayer(selectedLayerSelectionKey, layerStableKey, data);
        UpdateLimbLinesForLayer(selectedLayerSelectionKey, layerStableKey, data);

        var vertexCount = drawVertices.Length;
        DrawBoneInfluenceOnGeometryTriangles(toCanvasVertex, drawVertices, data, alpha);
        ResolvePreviewEdgePairs(data, vertexCount, out var allPairs, out var boundaryPairs, out var contourEdgeKeys);

        var edgePairCount = allPairs != null ? allPairs.Length / 2 : 0;
        var edgeSegmentCount = edgePairCount * 2;
        EnsureVertexBuffer(ref vertexEdgeSegments, edgeSegmentCount);
        var edgeWrite = 0;
        for (var i = 0; i < edgePairCount; i++)
        {
            var a = allPairs[i * 2];
            var b = allPairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                continue;
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[a]);
            vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[b]);
        }

        // Final fallback: if no inner edges left after filtering, draw the full available set.
        if (edgeWrite <= 1 && edgePairCount > 0)
        {
            edgeWrite = 0;
            for (var i = 0; i < edgePairCount; i++)
            {
                var a = allPairs[i * 2];
                var b = allPairs[i * 2 + 1];
                if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                    continue;
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[a]);
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[b]);
            }
        }

        if (edgeWrite > 1)
        {
            Handles.color = innerLineColor;
            Handles.DrawLines(vertexEdgeSegments);
        }

        var boundaryPairCount = boundaryPairs != null ? boundaryPairs.Length / 2 : 0;
        var boundarySegmentCount = boundaryPairCount * 2;
        EnsureVertexBuffer(ref vertexBoundarySegments, boundarySegmentCount);
        var boundaryWrite = 0;
        for (var i = 0; i < boundaryPairCount; i++)
        {
            var a = boundaryPairs[i * 2];
            var b = boundaryPairs[i * 2 + 1];
            if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                continue;
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(drawVertices[a]);
            vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(drawVertices[b]);
        }

        if (boundaryWrite > 1)
        {
            Handles.color = boundaryLineColor;
            Handles.DrawLines(vertexBoundarySegments);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(thickOffset, 0f), boundaryLineColor);
            DrawShiftedSegments(vertexBoundarySegments, boundaryWrite, ref vertexBoundarySegmentsShifted, new Vector2(0f, thickOffset), boundaryLineColor);
        }

        var pointSegmentCount = vertexCount * 4;
        EnsureVertexBuffer(ref vertexPointSegments, pointSegmentCount);
        var pointWrite = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var p = toCanvasVertex(drawVertices[i]);
            vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
            vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
        }

        if (pointWrite > 1)
        {
            Handles.color = pointColor;
            Handles.DrawLines(vertexPointSegments);
        }

        if (editGeometrySelectedVertexIndices != null &&
            editGeometrySelectedVertexIndices.Count > 0 &&
            string.Equals(editGeometrySelectedLayerStableKey, layerStableKey, StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < editGeometrySelectedVertexIndices.Count; i++)
            {
                var selectedVertexIndex = editGeometrySelectedVertexIndices[i];
                if (selectedVertexIndex < 0 || selectedVertexIndex >= vertexCount)
                    continue;

                var selectedPoint = toCanvasVertex(drawVertices[selectedVertexIndex]);
                var isPrimary = selectedVertexIndex == editGeometrySelectedVertexIndex;
                Handles.color = new Color(1f, 0.85f, 0.1f, Mathf.Clamp01(alpha));
                Handles.DrawSolidDisc(selectedPoint, Vector3.forward, isPrimary ? 4f : 3.3f);
                Handles.color = new Color(0.12f, 0.12f, 0.12f, Mathf.Clamp01(alpha));
                Handles.DrawWireDisc(selectedPoint, Vector3.forward, isPrimary ? 5f : 4.2f);
            }
        }
        else if (editGeometrySelectedVertexIndex >= 0 && editGeometrySelectedVertexIndex < vertexCount)
        {
            var selectedPoint = toCanvasVertex(drawVertices[editGeometrySelectedVertexIndex]);
            Handles.color = new Color(1f, 0.85f, 0.1f, Mathf.Clamp01(alpha));
            Handles.DrawSolidDisc(selectedPoint, Vector3.forward, 4f);
            Handles.color = new Color(0.12f, 0.12f, 0.12f, Mathf.Clamp01(alpha));
            Handles.DrawWireDisc(selectedPoint, Vector3.forward, 5f);
        }

        if (createEdgeStartVertexIndex >= 0 &&
            createEdgeStartVertexIndex < vertexCount &&
            string.Equals(createEdgeLayerStableKey, layerStableKey, StringComparison.OrdinalIgnoreCase))
        {
            var startPoint = toCanvasVertex(drawVertices[createEdgeStartVertexIndex]);
            Handles.color = new Color(0.2f, 0.9f, 1f, Mathf.Clamp01(alpha));
            Handles.DrawSolidDisc(startPoint, Vector3.forward, 3.6f);
            Handles.color = new Color(0.04f, 0.1f, 0.12f, Mathf.Clamp01(alpha));
            Handles.DrawWireDisc(startPoint, Vector3.forward, 4.8f);

            if (editGeometrySelectedVertexIndex >= 0 &&
                editGeometrySelectedVertexIndex < vertexCount &&
                editGeometrySelectedVertexIndex != createEdgeStartVertexIndex)
            {
                var selectedPoint = toCanvasVertex(drawVertices[editGeometrySelectedVertexIndex]);
                Handles.color = new Color(0.2f, 0.9f, 1f, Mathf.Clamp01(alpha * 0.95f));
                Handles.DrawAAPolyLine(2f, startPoint, selectedPoint);
            }
        }

        DrawElbowContourPoints(toCanvasVertex, selectedLayerSelectionKey, layerStableKey, data, pointColor, innerLineColor);
        DrawLimbContourPoints(toCanvasVertex, selectedLayerSelectionKey, layerStableKey, data, pointColor, innerLineColor);
        DrawAppliedElbowVertices(toCanvasVertex, selectedLayerSelectionKey, layerStableKey, data, drawVertices, pointColor, innerLineColor);

        Handles.color = prev;
        Handles.EndGUI();
    }

    private void DrawSpriteAtlasGeometryOverlay(
        PsdCoordinateSpace coordinateMapper,
        IReadOnlyList<LayerInfo> drawList,
        string selectedLayerStableKey,
        float alpha)
    {
        if (alpha <= 0f || coordinateMapper == null || drawList == null)
            return;
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return;

        var innerLineColor = new Color(0.55f, 0.92f, 1f, alpha * 0.85f);
        var boundaryLineColor = new Color(1f, 0.56f, 0.08f, alpha * 0.9f);
        var pointColor = new Color(0.55f, 0.92f, 1f, alpha);
        const float markerHalfSize = 2f;

        Handles.BeginGUI();
        var prev = Handles.color;
        for (var layerIndex = 0; layerIndex < drawList.Count; layerIndex++)
        {
            var layer = drawList[layerIndex];
            if (layer == null || !layer.visible)
                continue;
            if (!ShouldDisplaySpriteAtlasGeometryForLayer(layer, selectedLayerStableKey))
                continue;
            if (!TryGetLayerVertexData(layer, out var data) || data == null)
                continue;

            var drawVertices = GetLayerVerticesForDraw(data);
            if (drawVertices == null || drawVertices.Length == 0)
                continue;
            var layerBounds = GetLayerSelectionViewRect(layer, coordinateMapper);
            if (!IsPreviewRectVisible(layerBounds, coordinateMapper.CanvasRectView))
                continue;

            var toCanvasVertex = CreateLayerVertexToViewMapper(coordinateMapper, data, drawVertices);
            var vertexCount = drawVertices.Length;
            ResolvePreviewEdgePairs(data, vertexCount, out var allPairs, out var boundaryPairs, out var contourEdgeKeys);

            var edgePairCount = allPairs != null ? allPairs.Length / 2 : 0;
            EnsureVertexBuffer(ref vertexEdgeSegments, edgePairCount * 2);
            var edgeWrite = 0;
            for (var i = 0; i < edgePairCount; i++)
            {
                var a = allPairs[i * 2];
                var b = allPairs[i * 2 + 1];
                if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                    continue;
                if (contourEdgeKeys != null && contourEdgeKeys.Contains(MakeEdgeKey(a, b)))
                    continue;
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[a]);
                vertexEdgeSegments[edgeWrite++] = toCanvasVertex(drawVertices[b]);
            }

            if (edgeWrite > 1)
            {
                Handles.color = innerLineColor;
                Handles.DrawLines(vertexEdgeSegments);
            }

            var boundaryPairCount = boundaryPairs != null ? boundaryPairs.Length / 2 : 0;
            EnsureVertexBuffer(ref vertexBoundarySegments, boundaryPairCount * 2);
            var boundaryWrite = 0;
            for (var i = 0; i < boundaryPairCount; i++)
            {
                var a = boundaryPairs[i * 2];
                var b = boundaryPairs[i * 2 + 1];
                if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
                    continue;
                vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(drawVertices[a]);
                vertexBoundarySegments[boundaryWrite++] = toCanvasVertex(drawVertices[b]);
            }

            if (boundaryWrite > 1)
            {
                Handles.color = boundaryLineColor;
                Handles.DrawLines(vertexBoundarySegments);
            }

            EnsureVertexBuffer(ref vertexPointSegments, vertexCount * 4);
            var pointWrite = 0;
            for (var i = 0; i < vertexCount; i++)
            {
                var p = toCanvasVertex(drawVertices[i]);
                vertexPointSegments[pointWrite++] = p + new Vector2(-markerHalfSize, 0f);
                vertexPointSegments[pointWrite++] = p + new Vector2(markerHalfSize, 0f);
                vertexPointSegments[pointWrite++] = p + new Vector2(0f, -markerHalfSize);
                vertexPointSegments[pointWrite++] = p + new Vector2(0f, markerHalfSize);
            }

            if (pointWrite > 1)
            {
                Handles.color = pointColor;
                Handles.DrawLines(vertexPointSegments);
            }
        }

        Handles.color = prev;
        Handles.EndGUI();
    }

    private static bool ShouldDisplaySpriteAtlasGeometryForLayer(LayerInfo layer, string selectedLayerStableKey)
    {
        return layer != null &&
               ReanimatorSpriteSheetGeometryAlgorithms.ShouldDisplayGeometryForSelection(
                   BuildKey(layer.name, layer.layerId),
                   selectedLayerStableKey);
    }

    private void DrawBoneInfluenceOnGeometryTriangles(
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        LayerVertexData data,
        float baseAlpha)
    {
        DrawBoneInfluenceOnGeometryTriangles(toCanvasVertex, vertices, data, baseAlpha, int.MaxValue);
    }

    private void DrawBoneInfluenceOnGeometryTriangles(
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        LayerVertexData data,
        float baseAlpha,
        int maxTriangleCount,
        float overlayAlphaOverride = -1f)
    {
        var overlayAlpha = overlayAlphaOverride >= 0f
            ? Mathf.Clamp01(overlayAlphaOverride)
            : Mathf.Clamp01(BoneInfluenceOverlay);
        if (overlayAlpha <= 0.0001f || toCanvasVertex == null || vertices == null || data == null)
            return;
        if (data.boneWeights == null || data.boneWeights.Length == 0)
            return;
        if (data.triangles == null || data.triangles.Length < 3)
            return;
        if (data.spriteBoneToRendererBone == null || data.spriteBoneToRendererBone.Length == 0)
            return;

        var texture = data.texture != null ? data.texture : GetPreviewAtlasTexture();
        var useAlphaMask = texture != null && data.vertexUvs != null && data.vertexUvs.Length >= vertices.Count;
        var mat = useAlphaMask
            ? EnsureMaskedInfluenceOverlayMaterial()
            : EnsureInfluenceOverlayMaterial();
        if (mat == null)
            return;

        var vertexCount = Mathf.Min(vertices.Count, data.boneWeights.Length);
        var vertexAlpha = Mathf.Clamp01(baseAlpha) * overlayAlpha;
        if (vertexAlpha <= 0.0001f)
            return;
        var triangleCount = data.triangles.Length / 3;
        if (triangleCount <= 0)
            return;
        var drawTriangleCount = Mathf.Clamp(maxTriangleCount, 0, triangleCount);
        if (drawTriangleCount <= 0)
            return;

        if (useAlphaMask)
        {
            mat.mainTexture = texture;
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_Opacity"))
                mat.SetFloat("_Opacity", 1f);
        }

        mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Handles.matrix);
        GL.Begin(GL.TRIANGLES);
        for (var tri = 0; tri < drawTriangleCount; tri++)
        {
            var i = tri * 3;
            var ia = data.triangles[i];
            var ib = data.triangles[i + 1];
            var ic = data.triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0)
                continue;
            if (ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
                continue;

            if (useAlphaMask)
            {
                EmitInfluenceVertex(ia, toCanvasVertex, vertices, data, vertexAlpha, data.vertexUvs);
                EmitInfluenceVertex(ib, toCanvasVertex, vertices, data, vertexAlpha, data.vertexUvs);
                EmitInfluenceVertex(ic, toCanvasVertex, vertices, data, vertexAlpha, data.vertexUvs);
            }
            else
            {
                EmitInfluenceVertex(ia, toCanvasVertex, vertices, data, vertexAlpha);
                EmitInfluenceVertex(ib, toCanvasVertex, vertices, data, vertexAlpha);
                EmitInfluenceVertex(ic, toCanvasVertex, vertices, data, vertexAlpha);
            }
        }
        GL.End();
        GL.PopMatrix();
    }

    private void DrawBoneInfluenceOnGeometryTriangles(
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<int> triangles,
        IReadOnlyList<int> spriteBoneToRendererBone,
        float baseAlpha,
        int maxTriangleCount,
        float overlayAlphaOverride = -1f)
    {
        var overlayAlpha = overlayAlphaOverride >= 0f
            ? Mathf.Clamp01(overlayAlphaOverride)
            : Mathf.Clamp01(BoneInfluenceOverlay);
        if (overlayAlpha <= 0.0001f || toCanvasVertex == null || vertices == null || boneWeights == null || triangles == null)
            return;
        if (boneWeights.Count == 0 || triangles.Count < 3 || spriteBoneToRendererBone == null || spriteBoneToRendererBone.Count == 0)
            return;

        var mat = EnsureInfluenceOverlayMaterial();
        if (mat == null)
            return;

        var vertexCount = Mathf.Min(vertices.Count, boneWeights.Count);
        var vertexAlpha = Mathf.Clamp01(baseAlpha) * overlayAlpha;
        if (vertexAlpha <= 0.0001f)
            return;
        var triangleCount = triangles.Count / 3;
        if (triangleCount <= 0)
            return;
        var drawTriangleCount = Mathf.Clamp(maxTriangleCount, 0, triangleCount);
        if (drawTriangleCount <= 0)
            return;

        mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Handles.matrix);
        GL.Begin(GL.TRIANGLES);
        for (var tri = 0; tri < drawTriangleCount; tri++)
        {
            var i = tri * 3;
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0)
                continue;
            if (ia >= vertexCount || ib >= vertexCount || ic >= vertexCount)
                continue;

            EmitInfluenceVertex(ia, toCanvasVertex, vertices, boneWeights, spriteBoneToRendererBone, vertexAlpha);
            EmitInfluenceVertex(ib, toCanvasVertex, vertices, boneWeights, spriteBoneToRendererBone, vertexAlpha);
            EmitInfluenceVertex(ic, toCanvasVertex, vertices, boneWeights, spriteBoneToRendererBone, vertexAlpha);
        }
        GL.End();
        GL.PopMatrix();
    }

    private void EmitInfluenceVertex(
        int index,
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        LayerVertexData data,
        float baseAlpha)
    {
        var color = EvaluateInfluenceVertexColor(data.boneWeights[index], data.spriteBoneToRendererBone, baseAlpha);
        if (color.a <= 0.0001f)
            return;

        var p = toCanvasVertex(vertices[index]);
        GL.Color(color);
        GL.Vertex3(p.x, p.y, 0f);
    }

    private void EmitInfluenceVertex(
        int index,
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        LayerVertexData data,
        float baseAlpha,
        IReadOnlyList<Vector2> uvs)
    {
        var color = EvaluateInfluenceVertexColor(data.boneWeights[index], data.spriteBoneToRendererBone, baseAlpha);
        if (color.a <= 0.0001f)
            return;

        var p = toCanvasVertex(vertices[index]);
        var uv = index >= 0 && index < uvs.Count ? uvs[index] : Vector2.zero;
        GL.Color(color);
        GL.TexCoord2(uv.x, uv.y);
        GL.Vertex3(p.x, p.y, 0f);
    }

    private void EmitInfluenceVertex(
        int index,
        Func<Vector2, Vector2> toCanvasVertex,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<BoneWeight> boneWeights,
        IReadOnlyList<int> spriteBoneToRendererBone,
        float baseAlpha)
    {
        var color = EvaluateInfluenceVertexColor(boneWeights[index], spriteBoneToRendererBone, baseAlpha);
        if (color.a <= 0.0001f)
            return;

        var p = toCanvasVertex(vertices[index]);
        GL.Color(color);
        GL.Vertex3(p.x, p.y, 0f);
    }

    private Color EvaluateInfluenceVertexColor(
        BoneWeight weight,
        IReadOnlyList<int> spriteBoneToRendererBone,
        float baseAlpha)
    {
        var colorSum = Vector3.zero;
        var weightSum = 0f;

        AccumulateInfluenceColor(spriteBoneToRendererBone, weight.boneIndex0, weight.weight0, ref colorSum, ref weightSum);
        AccumulateInfluenceColor(spriteBoneToRendererBone, weight.boneIndex1, weight.weight1, ref colorSum, ref weightSum);
        AccumulateInfluenceColor(spriteBoneToRendererBone, weight.boneIndex2, weight.weight2, ref colorSum, ref weightSum);
        AccumulateInfluenceColor(spriteBoneToRendererBone, weight.boneIndex3, weight.weight3, ref colorSum, ref weightSum);

        if (weightSum <= 0.0001f)
            return new Color(0f, 0f, 0f, 0f);

        var inv = 1f / weightSum;
        var rgb = colorSum * inv;
        var alpha = Mathf.Clamp01(baseAlpha * Mathf.Clamp01(weightSum));
        return new Color(rgb.x, rgb.y, rgb.z, alpha);
    }

    private void AccumulateInfluenceColor(
        IReadOnlyList<int> spriteBoneToRendererBone,
        int spriteBoneIndex,
        float weight,
        ref Vector3 colorSum,
        ref float weightSum)
    {
        if (spriteBoneToRendererBone == null)
            return;
        if (weight <= 0.0001f || spriteBoneIndex < 0 || spriteBoneIndex >= spriteBoneToRendererBone.Count)
            return;

        var mappedRendererIndex = spriteBoneToRendererBone[spriteBoneIndex];
        if (mappedRendererIndex < 0 || mappedRendererIndex >= bones.Count)
            return;

        var normalized = Mathf.Clamp01(weight);
        if (normalized <= 0.0001f)
            return;

        var boneColor = bones[mappedRendererIndex].color;
        colorSum += new Vector3(boneColor.r, boneColor.g, boneColor.b) * normalized;
        weightSum += normalized;
    }

    private void DrawLayerBoundingBoxes(
        PsdCoordinateSpace coordinateMapper,
        IReadOnlyList<LayerInfo> drawList,
        LayerInfo selectedLayer)
    {
        if (coordinateMapper == null || drawList == null)
            return;

        var selectedKey = selectedLayer != null ? BuildSelectionKey(selectedLayer) : selectedLayerSelectionKey;

        if (!IsDebugModeEnabled)
        {
            if (string.IsNullOrEmpty(selectedKey))
                return;

            if (selectedLayer != null)
            {
                var selectedRect = GetLayerSelectionViewRect(selectedLayer, coordinateMapper);
                if (!IsPreviewRectVisible(selectedRect, coordinateMapper.CanvasRectView))
                    return;
                DrawRectOutline(selectedRect, new Color(0.72f, 0.25f, 1f, 0.98f), 2.4f);
                return;
            }

            for (var i = 0; i < drawList.Count; i++)
            {
                var layer = drawList[i];
                if (layer == null)
                    continue;

                var layerKey = BuildSelectionKey(layer);
                if (!string.Equals(layerKey, selectedKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var selectedRect = GetLayerSelectionViewRect(layer, coordinateMapper);
                if (!IsPreviewRectVisible(selectedRect, coordinateMapper.CanvasRectView))
                    return;
                DrawRectOutline(selectedRect, new Color(0.72f, 0.25f, 1f, 0.98f), 2.4f);
                return;
            }

            return;
        }

        var normalColor = new Color(1f, 0f, 0f, 0.38f);
        var selectedColor = new Color(0.72f, 0.25f, 1f, 0.98f);

        for (var i = 0; i < drawList.Count; i++)
        {
            var layer = drawList[i];
            if (layer == null)
                continue;

            var layerKey = BuildSelectionKey(layer);
            var isSelected = !string.IsNullOrEmpty(selectedKey) &&
                             string.Equals(layerKey, selectedKey, StringComparison.OrdinalIgnoreCase);
            if (!layer.visible && !isSelected)
                continue;
            var rect = GetLayerSelectionViewRect(layer, coordinateMapper);
            if (!IsPreviewRectVisible(rect, coordinateMapper.CanvasRectView))
                continue;
            DrawRectOutline(rect, isSelected ? selectedColor : normalColor, isSelected ? 2f : 1f);
        }
    }

    private void DrawSpriteAtlasBoundingBoxes(
        PsdCoordinateSpace coordinateMapper,
        IReadOnlyList<LayerInfo> drawList,
        LayerInfo selectedLayer,
        bool isolateSelectedSprite = false)
    {
        if (coordinateMapper == null || drawList == null)
            return;

        var selectedKey = selectedLayer != null ? BuildSelectionKey(selectedLayer) : selectedLayerSelectionKey;
        var normalColor = new Color(1f, 1f, 1f, 0.38f);
        var selectedColor = new Color(0.72f, 0.25f, 1f, 0.98f);

        for (var i = 0; i < drawList.Count; i++)
        {
            var layer = drawList[i];
            if (layer == null)
                continue;

            var layerKey = BuildSelectionKey(layer);
            var isSelected = !string.IsNullOrEmpty(selectedKey) &&
                             string.Equals(layerKey, selectedKey, StringComparison.OrdinalIgnoreCase);
            if (isolateSelectedSprite && selectedLayer != null && !isSelected)
                continue;
            if (!layer.visible && !isSelected)
                continue;
            var rect = coordinateMapper.LayerToViewRect(layer);
            if (!IsPreviewRectVisible(rect, coordinateMapper.CanvasRectView))
                continue;
            DrawRectOutline(rect, isSelected ? selectedColor : normalColor, isSelected ? 2.2f : 1f);
            if (isSelected)
                DrawSpriteAtlasFrameHandles(rect);
        }
    }

    private static void DrawSpriteAtlasFrameHandles(Rect rect)
    {
        const float size = 8f;
        var borderColor = new Color(0.05f, 0.42f, 0.95f, 1f);
        var fillColor = new Color(0.1f, 0.65f, 1f, 1f);
        var points = new[]
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.center.x, rect.yMin),
            new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMin, rect.center.y),
            new Vector2(rect.xMax, rect.center.y),
            new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.center.x, rect.yMax),
            new Vector2(rect.xMax, rect.yMax)
        };

        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var outer = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
            var inner = new Rect(outer.x + 1f, outer.y + 1f, outer.width - 2f, outer.height - 2f);
            EditorGUI.DrawRect(outer, borderColor);
            EditorGUI.DrawRect(inner, fillColor);
        }
    }

    private static bool IsPreviewSegmentVisible(Vector2 start, Vector2 end, Rect viewport, float padding = 8f)
    {
        var minX = Mathf.Min(start.x, end.x);
        var minY = Mathf.Min(start.y, end.y);
        var maxX = Mathf.Max(start.x, end.x);
        var maxY = Mathf.Max(start.y, end.y);
        return IsPreviewRectVisible(
            Rect.MinMaxRect(minX, minY, maxX, maxY),
            viewport,
            padding);
    }

    private bool DrawLayerMesh(
        LayerInfo layer,
        float alpha,
        Rect localRect,
        PsdCoordinateSpace coordinates)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
            return false;
        if (layer == null || coordinates == null)
            return false;

        if (!TryGetLayerMeshDataForDraw(layer, out var data))
            return false;
        var texture = data.texture != null ? data.texture : GetPreviewAtlasTexture();
        if (texture == null)
            return false;
        if (data.triangles == null || data.triangles.Length < 3)
            return false;
        if (data.vertices == null)
            return false;
        if (!TryEnsureLayerVertexUvs(data))
            return false;

        var vertices = GetLayerVerticesForDraw(data);
        if (vertices == null || vertices.Length == 0 || data.vertexUvs.Length != vertices.Length)
            return false;

        var mat = EnsureLayerMeshMaterial();
        if (mat == null)
            return false;

        mat.mainTexture = texture;
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", texture);
        if (mat.HasProperty("_Opacity"))
            mat.SetFloat("_Opacity", alpha);
        if (mat.HasProperty("_VertexColorBlend"))
            mat.SetFloat("_VertexColorBlend", 0f);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);
        // Unity's 2D Animation GUI shader declares this as a uniform rather than a
        // material property, so HasProperty returns false even though SetFloat works.
        mat.SetFloat("_AdjustLinearForGamma", PlayerSettings.colorSpace == ColorSpace.Linear ? 1f : 0f);

        Handles.BeginGUI();
        mat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Handles.matrix);
        GL.Begin(GL.TRIANGLES);
        GL.Color(Color.white);

        var drawTriangles = GetLayerTrianglesForDraw(data);
        var toCanvasVertex = CreateLayerVertexToViewMapper(coordinates, data);
        for (var i = 0; i + 2 < drawTriangles.Length; i += 3)
        {
            var a = drawTriangles[i];
            var b = drawTriangles[i + 1];
            var c = drawTriangles[i + 2];
            if (!IsLayerTriangleDrawable(a, b, c, vertices.Length, data.vertexUvs.Length))
                continue;

            EmitLayerVertex(a, vertices, data.vertexUvs, toCanvasVertex);
            EmitLayerVertex(b, vertices, data.vertexUvs, toCanvasVertex);
            EmitLayerVertex(c, vertices, data.vertexUvs, toCanvasVertex);
        }

        GL.End();
        GL.PopMatrix();
        Handles.EndGUI();
        return true;
    }

    private bool TryGetLayerMeshDataForDraw(LayerInfo layer, out LayerVertexData data)
    {
        data = null;
        if (layer == null)
            return false;

        return TryGetLayerVertexData(layer, out data) && data != null;
    }

    private int[] GetLayerTrianglesForDraw(LayerVertexData data)
    {
        if (data == null || data.triangles == null)
            return Array.Empty<int>();

        if (data.boneOrderTriangles != null &&
            ReferenceEquals(data.boneOrderSourceTriangles, data.triangles) &&
            ReferenceEquals(data.boneOrderSourceWeights, data.boneWeights) &&
            data.lastBoneOrderVersion == boneOrderVersion)
        {
            return data.boneOrderTriangles;
        }

        data.boneOrderTriangles = ReanimatorBoneOrderAlgorithms.SortTrianglesByBoneOrder(
            data.triangles,
            data.boneWeights,
            data.spriteBoneToRendererBone,
            BuildRendererBoneOrderArray());
        data.boneOrderSourceTriangles = data.triangles;
        data.boneOrderSourceWeights = data.boneWeights;
        data.lastBoneOrderVersion = boneOrderVersion;
        return data.boneOrderTriangles;
    }

    private int[] BuildRendererBoneOrderArray()
    {
        var count = bones != null ? bones.Count : 0;
        var result = new int[count];
        for (var i = 0; i < count; i++)
            result[i] = bones[i] != null ? bones[i].depth : 0;
        return result;
    }

    private static bool IsLayerTriangleDrawable(int a, int b, int c, int vertexCount, int uvCount)
    {
        return a >= 0 && b >= 0 && c >= 0 &&
               a < vertexCount && b < vertexCount && c < vertexCount &&
               a < uvCount && b < uvCount && c < uvCount;
    }

    private static void EmitLayerVertex(
        int index,
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<Vector2> uvs,
        Func<Vector2, Vector2> toCanvasVertex)
    {
        if (index < 0 || index >= vertices.Count || index >= uvs.Count)
            return;

        var p = toCanvasVertex != null ? toCanvasVertex(vertices[index]) : vertices[index];
        var uv = uvs[index];
        GL.TexCoord2(uv.x, uv.y);
        GL.Vertex3(p.x, p.y, 0f);
    }

    private static Func<Vector2, Vector2> CreateLayerVertexToViewMapper(
        PsdCoordinateSpace coordinates,
        LayerVertexData data)
    {
        return CreateLayerVertexToViewMapper(coordinates, data != null ? data.layer : null, null);
    }

    private static Func<Vector2, Vector2> CreateLayerVertexToViewMapper(
        PsdCoordinateSpace coordinates,
        LayerVertexData data,
        IReadOnlyList<Vector2> vertices)
    {
        return CreateLayerVertexToViewMapper(coordinates, data != null ? data.layer : null, vertices);
    }

    private static Func<Vector2, Vector2> CreateLayerVertexToViewMapper(
        PsdCoordinateSpace coordinates,
        LayerInfo layer,
        IReadOnlyList<Vector2> vertices)
    {
        if (coordinates == null)
            return point => point;
        if (layer == null)
            return coordinates.DocumentToView;

        return point => coordinates.LayerDocumentPointToView(layer, point);
    }

    private Rect GetLayerSelectionViewRect(LayerInfo layer, PsdCoordinateSpace coordinates)
    {
        if (TryGetLayerDrawViewBounds(layer, coordinates, out var bounds))
            return bounds;

        return coordinates != null ? coordinates.LayerToViewRect(layer) : Rect.zero;
    }

    private bool TryGetLayerDrawViewBounds(LayerInfo layer, PsdCoordinateSpace coordinates, out Rect bounds)
    {
        bounds = Rect.zero;
        if (layer == null || coordinates == null)
            return false;
        if (!TryGetLayerVertexData(layer, out var data) || data == null)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        var toView = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var hasPoint = false;

        for (var i = 0; i < drawVertices.Length; i++)
        {
            var point = toView(drawVertices[i]);
            if (!IsFinite(point))
                continue;

            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
            hasPoint = true;
        }

        if (!hasPoint || minX > maxX || minY > maxY)
            return false;

        bounds = ExpandRect(Rect.MinMaxRect(minX, minY, maxX, maxY), 4f);
        return bounds.width > 0f && bounds.height > 0f;
    }

    private bool IsPointInsideLayerDrawMesh(LayerInfo layer, PsdCoordinateSpace coordinates, Vector2 viewPoint)
    {
        if (layer == null || coordinates == null)
            return false;
        if (!TryGetLayerDrawViewBounds(layer, coordinates, out var bounds) || !bounds.Contains(viewPoint))
            return false;
        if (!TryGetLayerVertexData(layer, out var data) || data == null)
            return false;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length == 0)
            return false;

        var triangles = GetLayerTrianglesForDraw(data);
        if (triangles == null || triangles.Length < 3)
            return true;

        var toView = CreateLayerVertexToViewMapper(coordinates, data, drawVertices);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (!IsLayerTriangleDrawable(a, b, c, drawVertices.Length, drawVertices.Length))
                continue;

            var p0 = toView(drawVertices[a]);
            var p1 = toView(drawVertices[b]);
            var p2 = toView(drawVertices[c]);
            if (!TryGetBarycentric(viewPoint, p0, p1, p2, out var barycentric))
                continue;

            if (!TryGetLayerTriangleAlpha(data, a, b, c, barycentric, out var alpha))
                return true;

            return alpha > 0.02f;
        }

        return false;
    }

    private bool TryGetLayerTriangleAlpha(
        LayerVertexData data,
        int a,
        int b,
        int c,
        Vector3 barycentric,
        out float alpha)
    {
        alpha = 1f;
        if (data == null || data.vertexUvs == null)
            return false;
        if (a < 0 || b < 0 || c < 0 || a >= data.vertexUvs.Length || b >= data.vertexUvs.Length || c >= data.vertexUvs.Length)
            return false;

        var texture = data.texture != null ? data.texture : GetPreviewAtlasTexture();
        if (texture == null || !texture.isReadable)
            return false;

        var uv = data.vertexUvs[a] * barycentric.x +
                 data.vertexUvs[b] * barycentric.y +
                 data.vertexUvs[c] * barycentric.z;
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);

        try
        {
            alpha = texture.GetPixelBilinear(uv.x, uv.y).a;
            return true;
        }
        catch
        {
            alpha = 1f;
            return false;
        }
    }

    private static bool TryGetBarycentric(Vector2 point, Vector2 a, Vector2 b, Vector2 c, out Vector3 barycentric)
    {
        barycentric = default;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = point - a;
        var d00 = Vector2.Dot(v0, v0);
        var d01 = Vector2.Dot(v0, v1);
        var d11 = Vector2.Dot(v1, v1);
        var d20 = Vector2.Dot(v2, v0);
        var d21 = Vector2.Dot(v2, v1);
        var denominator = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denominator) <= 1e-6f)
            return false;

        var v = (d11 * d20 - d01 * d21) / denominator;
        var w = (d00 * d21 - d01 * d20) / denominator;
        var u = 1f - v - w;
        const float epsilon = -0.0005f;
        if (u < epsilon || v < epsilon || w < epsilon)
            return false;

        barycentric = new Vector3(u, v, w);
        return true;
    }

    private static Rect ExpandRect(Rect rect, float padding)
    {
        return Rect.MinMaxRect(
            rect.xMin - padding,
            rect.yMin - padding,
            rect.xMax + padding,
            rect.yMax + padding);
    }

    private static bool IsFinite(Vector2 point)
    {
        return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
               !float.IsInfinity(point.x) && !float.IsInfinity(point.y);
    }

    private Material EnsureLayerMeshMaterial()
    {
        if (layerMeshMaterial != null)
            return layerMeshMaterial;

        var shader = Shader.Find("Hidden/SkinningModule-GUITextureClip");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Plugins/Reanimator/Graphics/ReanimatorGuiTextureClip.shader");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            return null;

        layerMeshMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return layerMeshMaterial;
    }

    private Material EnsureInfluenceOverlayMaterial()
    {
        if (influenceOverlayMaterial != null)
            return influenceOverlayMaterial;

        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return null;

        influenceOverlayMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        influenceOverlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        influenceOverlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        influenceOverlayMaterial.SetInt("_Cull", (int)CullMode.Off);
        influenceOverlayMaterial.SetInt("_ZWrite", 0);
        return influenceOverlayMaterial;
    }

    private Material EnsureMaskedInfluenceOverlayMaterial()
    {
        if (maskedInfluenceOverlayMaterial != null)
            return maskedInfluenceOverlayMaterial;

        var shader = Shader.Find("Hidden/Reanimator/InfluenceAlphaMask");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Plugins/Reanimator/Graphics/ReanimatorInfluenceAlphaMask.shader");
        if (shader == null)
            return EnsureInfluenceOverlayMaterial();

        maskedInfluenceOverlayMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return maskedInfluenceOverlayMaterial;
    }

    private Vector2[] GetLayerVerticesForDraw(LayerVertexData data)
    {
        if (data == null || data.vertices == null)
            return null;
        bonePoseAtRest = IsCurrentBonePoseRest();
        if (bonePoseAtRest || !deformSpritesWithBonePoseForCurrentDraw)
            return data.vertices;

        if (!data.hasSkinning || data.boneWeights == null || data.spriteBoneToRendererBone == null || data.bindBoneInverseMatrices == null)
            return data.vertices;
        if (data.bindBoneInverseMatrices.Length == 0 || data.boneWeights.Length != data.vertices.Length)
            return data.vertices;
        if (bones.Count == 0)
            return data.vertices;

        if (data.deformedVertices == null || data.deformedVertices.Length != data.vertices.Length)
            data.deformedVertices = new Vector2[data.vertices.Length];
        if (data.skinMatrices == null || data.skinMatrices.Length != data.bindBoneInverseMatrices.Length)
            data.skinMatrices = new Matrix4x4[data.bindBoneInverseMatrices.Length];

        if (data.lastPoseVersion == bonePoseVersion)
        {
            return data.deformedVertices;
        }

        BuildSkinMatrices(data);
        DeformLayerVertices(data);
        data.lastPoseVersion = bonePoseVersion;
        return data.deformedVertices;
    }

    private bool IsCurrentBonePoseRest()
    {
        if (bones == null || bones.Count == 0)
            return true;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if ((bone.position - bone.initialPosition).sqrMagnitude > 1e-6f)
                return false;
            var initialRotation = bone.initialRotation.Equals(default(Quaternion))
                ? bone.rotation
                : bone.initialRotation;
            if (Quaternion.Angle(bone.rotation, initialRotation) > 1e-3f)
                return false;
        }

        return true;
    }

    private void BuildSkinMatrices(LayerVertexData data)
    {
        if (data == null || data.bindBoneInverseMatrices == null || data.skinMatrices == null)
            return;

        ReanimatorSkinningAlgorithms.BuildSkinMatrices(
            bones,
            data.spriteBoneToRendererBone,
            data.bindBoneInverseMatrices,
            data.usesSpriteBindPoses,
            data.layer,
            data.pixelsPerUnit,
            data.pivotPixels,
            data.skinMatrices);
    }

    private void DeformLayerVertices(LayerVertexData data)
    {
        if (data == null || data.deformedVertices == null || data.vertices == null || data.boneWeights == null || data.skinMatrices == null)
            return;

        ReanimatorSkinningAlgorithms.DeformVertices(
            data.vertices,
            data.boneWeights,
            data.skinMatrices,
            data.usesSpriteBindPoses,
            data.layer,
            data.pixelsPerUnit,
            data.pivotPixels,
            data.deformedVertices);
    }

    private static Vector2 DocumentToSpriteSkinPosition(LayerVertexData data, Vector2 documentPosition)
    {
        if (data == null)
            return documentPosition;

        return ReanimatorSkinningAlgorithms.DocumentToSpriteSkinPosition(
            data.layer,
            documentPosition,
            data.pixelsPerUnit,
            data.pivotPixels);
    }

    private static Vector2 SpriteSkinPositionToDocument(LayerVertexData data, Vector2 spritePosition)
    {
        if (data == null)
            return spritePosition;

        return ReanimatorSkinningAlgorithms.SpriteSkinPositionToDocument(
            data.layer,
            spritePosition,
            data.pixelsPerUnit,
            data.pivotPixels);
    }

    private void DrawShiftedSegments(Vector3[] source, int count, ref Vector3[] shiftedBuffer, Vector2 offset, Color color)
    {
        if (count <= 1)
            return;

        EnsureVertexBuffer(ref shiftedBuffer, count);
        for (var i = 0; i < count; i++)
            shiftedBuffer[i] = source[i] + new Vector3(offset.x, offset.y, 0f);

        Handles.color = color;
        Handles.DrawLines(shiftedBuffer);
    }

}
#endif
