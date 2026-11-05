#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawVisibilityPanel(float availableHeight)
    {
        EnsureVisibilityMode();
        var listHeight = Mathf.Max(0f, availableHeight - 132f);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);
            spriteVisibilityValue = DrawVisibilitySliderRow(VisibilityElement.Sprite, "Sprite", "Sprite Icon", spriteVisibilityValue);
            boneVisibilityValue = DrawVisibilitySliderRow(VisibilityElement.Bone, "Bone", "AvatarMask Icon", boneVisibilityValue);
            vertexVisibilityValue = DrawVisibilitySliderRow(VisibilityElement.Vertex, "Vertex", "PreMatQuad", vertexVisibilityValue);
            influenceVisibilityValue = DrawVisibilitySliderRow(VisibilityElement.Influence, "Influence", "BlendTree Icon", influenceVisibilityValue);
            if (layerPreviewRenderer != null)
                layerPreviewRenderer.BoneInfluenceOverlay = ShouldDrawInfluenceOverlayForCurrentMode()
                    ? influenceVisibilityValue
                    : 0f;

            using (new EditorGUILayout.HorizontalScope())
            {
                var prevBone = visibilityBone;
                var prevSprite = visibilitySprite;
                var newBone = GUILayout.Toggle(prevBone, "Bone", EditorStyles.toolbarButton);
                var newSprite = GUILayout.Toggle(prevSprite, "Sprite", EditorStyles.toolbarButton);

                if (newBone != prevBone || newSprite != prevSprite)
                {
                    if (newBone && !prevBone)
                    {
                        visibilityBone = true;
                        visibilitySprite = false;
                    }
                    else if (newSprite && !prevSprite)
                    {
                        visibilityBone = false;
                        visibilitySprite = true;
                    }
                    else
                    {
                        visibilityBone = prevBone;
                        visibilitySprite = prevSprite;
                    }

                    Repaint();
                }
            }

            visibilitySearch = EditorGUILayout.TextField(visibilitySearch, EditorStyles.toolbarSearchField);

            GUILayout.Space(6f);
            if (visibilitySprite)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    visibilityHierarchy = GUILayout.Toggle(visibilityHierarchy, "Hierarchy", EditorStyles.toolbarButton, GUILayout.Width(80f));
                    if (GUILayout.Button("Expand", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                        collapsedLayerNodes.Clear();
                    if (GUILayout.Button("Collapse", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                        CollapseAllLayerNodes();
                    GUILayout.FlexibleSpace();
                }

                DrawSpriteVisibilityHeaderRow();

                var spriteScroll = GUILayout.BeginScrollView(
                    new Vector2(0f, visibilityScroll.y),
                    false,
                    true,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar,
                    GUILayout.ExpandHeight(true));
                try
                {
                    visibilityScroll.y = spriteScroll.y;
                    if (layerPreviewRenderer == null || layerPreviewRenderer.Layers == null || layerPreviewRenderer.Layers.Count == 0)
                    {
                        visibilityTreeContentWidth = 0f;
                        GUILayout.Label("No layers");
                    }
                    else
                    {
                        DrawSpriteVisibilityList(listHeight);
                    }
                }
                finally
                {
                    GUILayout.EndScrollView();
                }

                DrawVisibilityTreeHorizontalScrollbar();
            }
            else if (visibilityBone)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    visibilityHierarchy = GUILayout.Toggle(visibilityHierarchy, "Hierarchy", EditorStyles.toolbarButton, GUILayout.Width(80f));
                    if (GUILayout.Button("Expand", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                        collapsedBoneNodes.Clear();
                    if (GUILayout.Button("Collapse", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                        CollapseAllBoneNodes();
                    GUILayout.FlexibleSpace();
                }

                DrawBoneVisibilityHeaderRow();

                var boneScroll = GUILayout.BeginScrollView(
                    new Vector2(0f, visibilityScroll.y),
                    false,
                    true,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar,
                    GUILayout.ExpandHeight(true));
                try
                {
                    visibilityScroll.y = boneScroll.y;
                    var bones = layerPreviewRenderer != null ? layerPreviewRenderer.Bones : null;
                    if (bones == null || bones.Count == 0)
                    {
                        visibilityTreeContentWidth = 0f;
                        GUILayout.Label("No bones");
                    }
                    else
                    {
                        DrawBoneVisibilityList(bones, listHeight);
                    }
                }
                finally
                {
                    GUILayout.EndScrollView();
                }

                DrawVisibilityTreeHorizontalScrollbar();
            }
            else
            {
                GUILayout.Box(GUIContent.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
        }
    }

    private void DrawSpriteVisibilityHeaderRow()
    {
        var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, BoneVisibilityHeader);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BoneVisibilityGrid);
        }

        SplitSpriteVisibilityRow(rect, 0, 0f, out var eyeRect, out _, out var nameRect, out var depthRect, out var colorRect);
        DrawEyeGlyph(eyeRect, true, false);
        GUI.Label(nameRect, "Sprite", EditorStyles.centeredGreyMiniLabel);
        GUI.Label(depthRect, "Depth", EditorStyles.centeredGreyMiniLabel);
        GUI.Label(colorRect, "Color", EditorStyles.centeredGreyMiniLabel);
    }

    private static void SplitSpriteVisibilityRow(
        Rect rect,
        int level,
        float horizontalScroll,
        out Rect eyeRect,
        out Rect foldoutRect,
        out Rect nameRect,
        out Rect depthRect,
        out Rect colorRect)
    {
        var layout = ReanimatorVisibilityLayoutAlgorithms.CalculatePinnedRowRects(
            rect,
            level,
            horizontalScroll,
            includeToggle: false,
            BoneVisibilityEyeWidth,
            BoneVisibilityFoldoutWidth,
            BoneVisibilityToggleWidth,
            0f,
            BoneVisibilityDepthWidth,
            BoneVisibilityColorWidth,
            BoneVisibilityIndentWidth,
            BoneVisibilityTreeMinWidth,
            BoneVisibilityPinnedGap,
            BoneVisibilityPinnedRightPadding);
        eyeRect = layout.EyeRect;
        foldoutRect = layout.FoldoutRect;
        nameRect = layout.NameRect;
        depthRect = layout.DepthRect;
        colorRect = layout.ColorRect;
    }

    private void DrawVisibilityTreeHorizontalScrollbar()
    {
        var rect = GUILayoutUtility.GetRect(1f, 14f, GUILayout.ExpandWidth(true));
        var treeRect = GetVisibilityTreeViewportRect(rect);
        var viewportWidth = Mathf.Max(1f, treeRect.width);
        var contentWidth = Mathf.Max(viewportWidth, visibilityTreeContentWidth);
        var maxScroll = Mathf.Max(0f, contentWidth - viewportWidth);
        visibilityScroll.x = Mathf.Clamp(visibilityScroll.x, 0f, maxScroll);

        if (maxScroll <= 0.0001f)
            return;

        visibilityScroll.x = GUI.HorizontalScrollbar(treeRect, visibilityScroll.x, viewportWidth, 0f, contentWidth);
        visibilityScroll.x = Mathf.Clamp(visibilityScroll.x, 0f, maxScroll);
    }

    private static Rect GetVisibilityTreeViewportRect(Rect rect)
    {
        var layout = ReanimatorVisibilityLayoutAlgorithms.CalculatePinnedRowRects(
            rect,
            0,
            0f,
            includeToggle: true,
            BoneVisibilityEyeWidth,
            BoneVisibilityFoldoutWidth,
            BoneVisibilityToggleWidth,
            0f,
            BoneVisibilityDepthWidth,
            BoneVisibilityColorWidth,
            BoneVisibilityIndentWidth,
            BoneVisibilityTreeMinWidth,
            BoneVisibilityPinnedGap,
            BoneVisibilityPinnedRightPadding);
        var width = Mathf.Max(1f, layout.DepthRect.x - rect.x - BoneVisibilityPinnedGap);
        return new Rect(rect.x, rect.y, width, rect.height);
    }

    private static float CalculateSpriteVisibilityTreeContentWidth(IReadOnlyList<SpriteVisibilityRow> rows)
    {
        var maxLevel = 0;
        var longestLabelWidth = 0f;
        if (rows != null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                maxLevel = Mathf.Max(maxLevel, row.Level);
                var label = row.Hierarchical
                    ? row.Node != null && !string.IsNullOrEmpty(row.Node.name) ? row.Node.name : row.Node != null && row.Node.isGroup ? "Group" : "Layer"
                    : row.Label;
                longestLabelWidth = Mathf.Max(longestLabelWidth, EditorStyles.label.CalcSize(new GUIContent(label ?? string.Empty)).x);
            }
        }

        return ReanimatorVisibilityLayoutAlgorithms.CalculateTreeContentWidth(
            maxLevel,
            longestLabelWidth,
            includeToggle: false,
            BoneVisibilityEyeWidth,
            BoneVisibilityFoldoutWidth,
            BoneVisibilityToggleWidth,
            BoneVisibilityIndentWidth,
            BoneVisibilityTreeMinWidth);
    }

    private void DrawSpriteVisibilityList(float listHeight)
    {
        var hierarchyExpanded = visibilityHierarchy && ExpandSelectedLayerAncestors();
        var rows = BuildSpriteVisibilityRows();
        if (hierarchyExpanded)
            rows = BuildSpriteVisibilityRows();
        var searchBeforeReveal = visibilitySearch ?? string.Empty;
        SyncSpriteVisibilityScrollToSelection(rows, listHeight);
        if (!string.Equals(searchBeforeReveal, visibilitySearch ?? string.Empty, StringComparison.Ordinal))
            rows = BuildSpriteVisibilityRows();
        var metrics = ReanimatorVisibilityLayoutAlgorithms.CalculateVirtualizedRows(
            rows.Count,
            listHeight,
            visibilityScroll.y,
            BoneVisibilityRowHeight);
        if (metrics.TotalRows <= 0)
            return;

        visibilityTreeContentWidth = CalculateSpriteVisibilityTreeContentWidth(rows);
        var contentRect = GUILayoutUtility.GetRect(1f, metrics.ContentHeight, GUILayout.ExpandWidth(true));
        var nodes = layerPreviewRenderer != null ? layerPreviewRenderer.LayerHierarchyNodes : null;
        var hasRows = rows.Count > 0;
        var childrenByParent = hasRows && rows[0].Hierarchical && nodes != null
            ? BuildLayerChildrenByParent(nodes)
            : null;
        var selectedPathKeys = hasRows && rows[0].Hierarchical && nodes != null
            ? BuildSelectedLayerPathKeys(nodes)
            : null;

        for (var i = metrics.FirstRow; i < metrics.LastRow; i++)
        {
            var rowRect = new Rect(contentRect.x, contentRect.y + i * BoneVisibilityRowHeight, contentRect.width, BoneVisibilityRowHeight);
            if (i >= rows.Count)
            {
                DrawBoneVisibilityRowBackground(rowRect, i);
                continue;
            }

            var row = rows[i];
            if (row.Hierarchical)
                DrawSpriteHierarchyRowAtRect(nodes, row.Node, row.Level, childrenByParent, selectedPathKeys, i, rowRect);
            else
                DrawSpriteVisibilityRowAtRect(row.Layer, row.Label, row.Level, false, i, rowRect);
        }
    }

    private void SyncSpriteVisibilityScrollToSelection(IReadOnlyList<SpriteVisibilityRow> rows, float listHeight)
    {
        if (layerPreviewRenderer == null)
            return;

        var selectedKey = layerPreviewRenderer.SelectedLayerSelectionKey;
        if (string.IsNullOrEmpty(selectedKey))
        {
            visibilityAutoScrolledLayerSelectionKey = string.Empty;
            return;
        }

        var search = visibilitySearch ?? string.Empty;
        if (string.Equals(visibilityAutoScrolledLayerSelectionKey, selectedKey, StringComparison.OrdinalIgnoreCase) &&
            visibilityAutoScrolledLayerHierarchy == visibilityHierarchy &&
            string.Equals(visibilityAutoScrolledLayerSearch, search, StringComparison.Ordinal))
            return;

        if (!TryGetSelectedSpriteRowIndex(rows, out var rowIndex))
        {
            if (string.IsNullOrEmpty(visibilitySearch))
                return;

            visibilitySearch = string.Empty;
            rows = BuildSpriteVisibilityRows();
            if (!TryGetSelectedSpriteRowIndex(rows, out rowIndex))
                return;
            search = string.Empty;
        }

        var centeredOffset = Mathf.Max(0f, (listHeight - BoneVisibilityRowHeight) * 0.5f);
        visibilityScroll.y = Mathf.Max(0f, rowIndex * BoneVisibilityRowHeight - centeredOffset);
        visibilityAutoScrolledLayerSelectionKey = selectedKey;
        visibilityAutoScrolledLayerHierarchy = visibilityHierarchy;
        visibilityAutoScrolledLayerSearch = search;
        Repaint();
    }

    private bool TryGetSelectedSpriteRowIndex(IReadOnlyList<SpriteVisibilityRow> rows, out int rowIndex)
    {
        rowIndex = -1;
        if (rows == null || layerPreviewRenderer == null)
            return false;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var layer = row.Layer;
            if (row.Hierarchical && row.Node != null && row.Node.layerIndex >= 0 && row.Node.layerIndex < layerPreviewRenderer.Layers.Count)
                layer = layerPreviewRenderer.Layers[row.Node.layerIndex];
            if (layer == null || !layerPreviewRenderer.IsLayerSelected(layer))
                continue;

            rowIndex = i;
            return true;
        }

        return false;
    }

    private List<SpriteVisibilityRow> BuildSpriteVisibilityRows()
    {
        var rows = new List<SpriteVisibilityRow>();
        if (!visibilityHierarchy)
        {
            var layers = layerPreviewRenderer != null ? layerPreviewRenderer.SortedLayers : null;
            if (layers == null)
                return rows;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var layerName = string.IsNullOrEmpty(layer.name) ? $"Layer {i}" : layer.name;
                if (!string.IsNullOrEmpty(visibilitySearch) &&
                    layerName.IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                rows.Add(new SpriteVisibilityRow
                {
                    Layer = layer,
                    Label = layerName,
                    Level = 0,
                    Hierarchical = false
                });
            }
            return rows;
        }

        var nodes = layerPreviewRenderer != null ? layerPreviewRenderer.LayerHierarchyNodes : null;
        if (nodes == null || nodes.Count == 0)
            return rows;

        var childrenByParent = BuildLayerChildrenByParent(nodes);
        var filterCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var recursionPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drawnNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allNodeKeys = BuildLayerNodeKeySet(nodes);
        var roots = childrenByParent.TryGetValue(string.Empty, out var rootList) ? rootList : null;
        if (roots != null)
        {
            for (var i = 0; i < roots.Count; i++)
                CollectLayerNodeRows(nodes, roots[i], 0, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null || drawnNodes.Contains(node.key))
                continue;
            if (roots != null && !IsLayerOrphanNode(node, allNodeKeys))
                continue;
            CollectLayerNodeRows(nodes, node.key, 0, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
        }

        return rows;
    }

    private void CollectLayerNodeRows(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        string nodeKey,
        int level,
        Dictionary<string, List<string>> childrenByParent,
        Dictionary<string, bool> filterCache,
        HashSet<string> recursionPath,
        HashSet<string> drawnNodes,
        List<SpriteVisibilityRow> rows)
    {
        if (string.IsNullOrEmpty(nodeKey) || !recursionPath.Add(nodeKey) || drawnNodes.Contains(nodeKey))
            return;

        try
        {
            var node = FindLayerNode(nodes, nodeKey);
            if (node == null)
                return;
            if (!PassesLayerFilter(nodeKey, nodes, childrenByParent, filterCache, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                return;

            rows.Add(new SpriteVisibilityRow
            {
                Node = node,
                Level = level,
                Hierarchical = true
            });
            drawnNodes.Add(nodeKey);

            if (IsLayerNodeCollapsed(nodeKey))
                return;
            if (!childrenByParent.TryGetValue(nodeKey, out var children))
                return;

            for (var i = 0; i < children.Count; i++)
                CollectLayerNodeRows(nodes, children[i], level + 1, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
        }
        finally
        {
            recursionPath.Remove(nodeKey);
        }
    }

    private void DrawLayerNodeRecursive(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        string nodeKey,
        int level,
        Dictionary<string, List<string>> childrenByParent,
        Dictionary<string, bool> filterCache,
        HashSet<string> recursionPath,
        HashSet<string> drawnNodes,
        HashSet<string> selectedPathKeys,
        ref int rowIndex)
    {
        if (string.IsNullOrEmpty(nodeKey) || !recursionPath.Add(nodeKey) || drawnNodes.Contains(nodeKey))
            return;

        try
        {
            var node = FindLayerNode(nodes, nodeKey);
            if (node == null)
                return;
            if (!PassesLayerFilter(nodeKey, nodes, childrenByParent, filterCache, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                return;

            DrawSpriteHierarchyRow(nodes, node, level, childrenByParent, selectedPathKeys, ref rowIndex);
            drawnNodes.Add(nodeKey);

            if (IsLayerNodeCollapsed(nodeKey))
                return;
            if (!childrenByParent.TryGetValue(nodeKey, out var children))
                return;

            for (var i = 0; i < children.Count; i++)
                DrawLayerNodeRecursive(nodes, children[i], level + 1, childrenByParent, filterCache, recursionPath, drawnNodes, selectedPathKeys, ref rowIndex);
        }
        finally
        {
            recursionPath.Remove(nodeKey);
        }
    }

    private void DrawSpriteHierarchyRow(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        PsdLayerPreviewRenderer.LayerHierarchyNode node,
        int level,
        Dictionary<string, List<string>> childrenByParent,
        HashSet<string> selectedPathKeys,
        ref int rowIndex)
    {
        var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
        DrawSpriteHierarchyRowAtRect(nodes, node, level, childrenByParent, selectedPathKeys, rowIndex++, rect);
    }

    private void DrawSpriteHierarchyRowAtRect(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        PsdLayerPreviewRenderer.LayerHierarchyNode node,
        int level,
        Dictionary<string, List<string>> childrenByParent,
        HashSet<string> selectedPathKeys,
        int rowIndex,
        Rect rect)
    {
        var layer = node.layerIndex >= 0 && layerPreviewRenderer != null && node.layerIndex < layerPreviewRenderer.Layers.Count
            ? layerPreviewRenderer.Layers[node.layerIndex]
            : null;
        var layerName = string.IsNullOrEmpty(node.name) ? (node.isGroup ? "Group" : "Layer") : node.name;
        DrawBoneVisibilityRowBackground(rect, rowIndex);

        var selected = layer != null && layerPreviewRenderer != null && layerPreviewRenderer.IsLayerSelected(layer);
        var selectedPath = selected || (selectedPathKeys != null && selectedPathKeys.Contains(node.key));
        if (selectedPath && Event.current != null && Event.current.type == EventType.Repaint)
        {
            var highlight = selected
                ? new Color(0.45f, 0.16f, 0.62f, 0.36f)
                : new Color(0.45f, 0.16f, 0.62f, 0.24f);
            EditorGUI.DrawRect(rect, highlight);
        }

        SplitSpriteVisibilityRow(rect, level, visibilityScroll.x, out var eyeRect, out var foldoutRect, out var nameRect, out var depthRect, out var colorRect);
        var visible = node.isGroup ? node.visible : layer != null && layer.visible;
        if (DrawEyeGlyph(eyeRect, visible, true))
        {
            layerPreviewRenderer?.SetLayerHierarchyNodeVisible(node.key, !visible);
            Repaint();
        }

        if (childrenByParent.ContainsKey(node.key))
        {
            var expanded = !IsLayerNodeCollapsed(node.key);
            var nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
            if (nextExpanded != expanded)
                SetLayerNodeCollapsed(node.key, !nextExpanded);
        }

        if (node.isGroup)
        {
            var evt = Event.current;
            if (evt != null &&
                evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                evt.clickCount == 2 &&
                nameRect.Contains(evt.mousePosition))
            {
                TryCenterPreviewOnLayerHierarchyNode(node.key);
                evt.Use();
                return;
            }

            GUI.Label(nameRect, layerName, EditorStyles.boldLabel);
            GUI.Label(depthRect, "-", EditorStyles.centeredGreyMiniLabel);
            GUI.Label(colorRect, GUIContent.none);
            return;
        }

        DrawSpriteLayerCells(layer, layerName, nameRect, depthRect, colorRect, selected);
    }

    private void DrawSpriteVisibilityRow(PsdLayerPreviewRenderer.LayerInfo layer, string layerName)
    {
        var rowIndex = 0;
        DrawSpriteVisibilityRow(layer, layerName, 0, false, ref rowIndex);
    }

    private void DrawSpriteVisibilityRow(PsdLayerPreviewRenderer.LayerInfo layer, string layerName, int level, bool hierarchical, ref int rowIndex)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        DrawSpriteVisibilityRowAtRect(layer, layerName, level, hierarchical, rowIndex++, rect);
    }

    private void DrawSpriteVisibilityRowAtRect(
        PsdLayerPreviewRenderer.LayerInfo layer,
        string layerName,
        int level,
        bool hierarchical,
        int rowIndex,
        Rect rect)
    {
        DrawBoneVisibilityRowBackground(rect, rowIndex);
        var selected = layerPreviewRenderer != null && layerPreviewRenderer.IsLayerSelected(layer);
        if (selected && Event.current != null && Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.45f, 0.16f, 0.62f, 0.36f));

        SplitSpriteVisibilityRow(rect, hierarchical ? level : 0, visibilityScroll.x, out var eyeRect, out _, out var nameRect, out var depthRect, out var colorRect);
        if (DrawEyeGlyph(eyeRect, layer.visible, true))
        {
            if (layerPreviewRenderer != null && layerPreviewRenderer.SetLayerVisible(layer, !layer.visible))
                Repaint();
        }

        DrawSpriteLayerCells(layer, layerName, nameRect, depthRect, colorRect, selected);
    }

    private void DrawSpriteLayerCells(
        PsdLayerPreviewRenderer.LayerInfo layer,
        string layerName,
        Rect nameRect,
        Rect depthRect,
        Rect colorRect,
        bool selected)
    {
        var nameStyle = selected ? EditorStyles.whiteLabel : EditorStyles.label;
        var nameButtonRect = nameRect;
        if (layerPreviewRenderer != null && layerPreviewRenderer.IsDebugModeEnabled && layer != null)
        {
            var status = layerPreviewRenderer.GetLayerDebugStatus(layer);
            var iconCount = GetLayerDebugIconCount(status);
            var iconWidth = Mathf.Min(nameRect.width * 0.58f, Mathf.Max(18f, iconCount * 17f));
            var iconRect = new Rect(nameRect.xMax - iconWidth, nameRect.y, iconWidth, nameRect.height);
            nameButtonRect = new Rect(nameRect.x, nameRect.y, Mathf.Max(20f, nameRect.width - iconWidth - 3f), nameRect.height);
            DrawLayerDebugIssueIcons(iconRect, status);
        }

        var evt = Event.current;
        if (evt != null &&
            evt.type == EventType.MouseDown &&
            evt.button == 0 &&
            evt.clickCount == 2 &&
            nameButtonRect.Contains(evt.mousePosition))
        {
            if (layerPreviewRenderer != null)
                layerPreviewRenderer.SelectLayer(layer);
            TryCenterPreviewOnLayer(layer);
            evt.Use();
            return;
        }

        if (GUI.Button(nameButtonRect, layerName, nameStyle))
        {
            if (layerPreviewRenderer != null && layerPreviewRenderer.SelectLayer(selected ? null : layer))
                Repaint();
        }

        var depth = DrawDepthFieldWithDrag(depthRect, layer.depth, false, -1, BuildLayerKey(layer), out var depthChanged);
        if (depthChanged)
            layerPreviewRenderer?.SetLayerDepth(layer, depth);

        EditorGUI.BeginChangeCheck();
        var color = EditorGUI.ColorField(colorRect, GUIContent.none, layer.color);
        if (EditorGUI.EndChangeCheck())
        {
            if (layerPreviewRenderer != null && layerPreviewRenderer.SetLayerColor(layer, color))
                Repaint();
        }
    }

    private static int GetLayerDebugIconCount(PsdLayerPreviewRenderer.LayerDebugStatus status)
    {
        if (!status.HasIssues)
            return 1;

        return CountLayerIssueIcons(status.issues);
    }

    private static int CountLayerIssueIcons(PsdLayerPreviewRenderer.LayerDebugIssueFlags issues)
    {
        var count = 0;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.BadLayer) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.TextureRectIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.MissingGeometry) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.EmptyGeometry) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.TriangleIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.UvIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.WeightIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.BoneMapIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.BindPoseIssue) != 0) count++;
        if ((issues & PsdLayerPreviewRenderer.LayerDebugIssueFlags.SkinningPoseIssue) != 0) count++;
        return Mathf.Max(1, count);
    }

    private static void DrawLayerDebugIssueIcons(Rect rect, PsdLayerPreviewRenderer.LayerDebugStatus status)
    {
        if (Event.current == null)
            return;

        if (!status.HasIssues)
        {
            DrawLayerDebugIssueIcon(
                new Rect(rect.xMin, rect.y + 2f, 16f, rect.height - 4f),
                "OK",
                $"Geometry OK: vertices={status.vertexCount}, triangles={status.triangleCount}, uv={status.uvSource}",
                new Color(0.05f, 0.45f, 0.12f, 1f));
            return;
        }

        var issues = status.issues;
        DrawLayerIssueIcons(rect, issues);
    }

    private static void DrawLayerIssueIcons(Rect rect, PsdLayerPreviewRenderer.LayerDebugIssueFlags issues)
    {
        var x = rect.xMin;
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.BadLayer, "!", "Layer data is invalid.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.TextureRectIssue, "R", "Texture/source rect is empty or outside the atlas.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.MissingGeometry, "G", "Missing geometry data for this sprite.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.EmptyGeometry, "V", "Geometry has no vertices.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.TriangleIssue, "T", "Triangle indices are missing or malformed.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.UvIssue, "UV", "UV data is missing, mismatched, or outside the texture rect.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.WeightIssue, "W", "Bone weights are missing or do not match the vertex count.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.BoneMapIssue, "B", "Sprite bone map is missing.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.BindPoseIssue, "M", "Bind pose data does not match the sprite bone map.");
        DrawLayerDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.LayerDebugIssueFlags.SkinningPoseIssue, "S", "Skinned pose vertices are invalid or explode outside the source geometry bounds.");
    }

    private static void DrawLayerDebugIssueIconIfSet(
        ref float x,
        Rect rowRect,
        PsdLayerPreviewRenderer.LayerDebugIssueFlags issues,
        PsdLayerPreviewRenderer.LayerDebugIssueFlags flag,
        string label,
        string tooltip)
    {
        if ((issues & flag) == 0)
            return;

        DrawLayerDebugIssueIcon(
            new Rect(x, rowRect.y + 2f, 16f, rowRect.height - 4f),
            label,
            tooltip,
            new Color(0.74f, 0.02f, 0.02f, 1f));
        x += 17f;
    }

    private static void DrawLayerDebugIssueIcon(Rect rect, string label, string tooltip, Color color)
    {
        EditorGUI.DrawRect(rect, color);
        var style = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip
        };
        GUI.Label(rect, new GUIContent(label, tooltip), style);
    }

    private static Dictionary<string, List<string>> BuildLayerChildrenByParent(IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (nodes == null)
            return result;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null || string.IsNullOrEmpty(node.key))
                continue;

            var parentKey = node.parentKey ?? string.Empty;
            if (!result.TryGetValue(parentKey, out var list))
            {
                list = new List<string>();
                result[parentKey] = list;
            }

            list.Add(node.key);
        }

        return result;
    }

    private static HashSet<string> BuildLayerNodeKeySet(IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nodes == null)
            return result;

        for (var i = 0; i < nodes.Count; i++)
        {
            var key = nodes[i]?.key;
            if (!string.IsNullOrEmpty(key))
                result.Add(key);
        }

        return result;
    }

    private HashSet<string> BuildSelectedLayerPathKeys(IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nodes == null || layerPreviewRenderer == null)
            return result;

        var parentByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string selectedNodeKey = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null || string.IsNullOrEmpty(node.key))
                continue;

            parentByKey[node.key] = node.parentKey ?? string.Empty;
            if (selectedNodeKey != null || node.layerIndex < 0 || node.layerIndex >= layerPreviewRenderer.Layers.Count)
                continue;

            var layer = layerPreviewRenderer.Layers[node.layerIndex];
            if (layer != null && layerPreviewRenderer.IsLayerSelected(layer))
                selectedNodeKey = node.key;
        }

        var guard = 0;
        var key = selectedNodeKey;
        while (!string.IsNullOrEmpty(key) && guard++ < nodes.Count)
        {
            result.Add(key);
            if (!parentByKey.TryGetValue(key, out key))
                break;
        }

        return result;
    }

    private bool ExpandSelectedLayerAncestors()
    {
        var nodes = layerPreviewRenderer != null ? layerPreviewRenderer.LayerHierarchyNodes : null;
        if (nodes == null || nodes.Count == 0)
            return false;

        var parentByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string selectedNodeKey = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null || string.IsNullOrEmpty(node.key))
                continue;

            parentByKey[node.key] = node.parentKey ?? string.Empty;
            if (selectedNodeKey != null || node.layerIndex < 0 || node.layerIndex >= layerPreviewRenderer.Layers.Count)
                continue;

            var layer = layerPreviewRenderer.Layers[node.layerIndex];
            if (layer != null && layerPreviewRenderer.IsLayerSelected(layer))
                selectedNodeKey = node.key;
        }

        var changed = false;
        var guard = 0;
        var key = selectedNodeKey;
        while (!string.IsNullOrEmpty(key) && guard++ < nodes.Count)
        {
            if (!parentByKey.TryGetValue(key, out var parentKey) || string.IsNullOrEmpty(parentKey))
                break;
            if (collapsedLayerNodes != null && collapsedLayerNodes.Contains(parentKey))
            {
                SetLayerNodeCollapsed(parentKey, false);
                changed = true;
            }
            key = parentKey;
        }

        return changed;
    }

    private static bool IsLayerOrphanNode(PsdLayerPreviewRenderer.LayerHierarchyNode node, HashSet<string> allNodeKeys)
    {
        if (node == null)
            return false;

        var parentKey = node.parentKey ?? string.Empty;
        return !string.IsNullOrEmpty(parentKey) && (allNodeKeys == null || !allNodeKeys.Contains(parentKey));
    }

    private static PsdLayerPreviewRenderer.LayerHierarchyNode FindLayerNode(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        string nodeKey)
    {
        if (nodes == null || string.IsNullOrEmpty(nodeKey))
            return null;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node != null && string.Equals(node.key, nodeKey, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }

    private bool PassesLayerFilter(
        string nodeKey,
        IReadOnlyList<PsdLayerPreviewRenderer.LayerHierarchyNode> nodes,
        Dictionary<string, List<string>> childrenByParent,
        Dictionary<string, bool> cache,
        HashSet<string> recursionPath)
    {
        if (string.IsNullOrEmpty(nodeKey))
            return false;
        if (string.IsNullOrEmpty(visibilitySearch))
            return true;
        if (cache.TryGetValue(nodeKey, out var cached))
            return cached;
        if (!recursionPath.Add(nodeKey))
            return false;

        var node = FindLayerNode(nodes, nodeKey);
        var passes = node != null && (node.name ?? string.Empty).IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) >= 0;
        if (!passes && childrenByParent.TryGetValue(nodeKey, out var children))
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (!PassesLayerFilter(children[i], nodes, childrenByParent, cache, recursionPath))
                    continue;
                passes = true;
                break;
            }
        }

        recursionPath.Remove(nodeKey);
        cache[nodeKey] = passes;
        return passes;
    }

    private bool IsLayerNodeCollapsed(string nodeKey)
    {
        return collapsedLayerNodes != null && collapsedLayerNodes.Contains(nodeKey);
    }

    private void SetLayerNodeCollapsed(string nodeKey, bool collapsed)
    {
        if (string.IsNullOrEmpty(nodeKey))
            return;
        collapsedLayerNodes ??= new List<string>();
        if (collapsed)
        {
            if (!collapsedLayerNodes.Contains(nodeKey))
                collapsedLayerNodes.Add(nodeKey);
            return;
        }

        collapsedLayerNodes.Remove(nodeKey);
    }

    private void CollapseAllLayerNodes()
    {
        collapsedLayerNodes ??= new List<string>();
        collapsedLayerNodes.Clear();

        var nodes = layerPreviewRenderer != null ? layerPreviewRenderer.LayerHierarchyNodes : null;
        if (nodes == null || nodes.Count == 0)
            return;

        var childrenByParent = BuildLayerChildrenByParent(nodes);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null || string.IsNullOrEmpty(node.key))
                continue;
            if (childrenByParent.ContainsKey(node.key))
                collapsedLayerNodes.Add(node.key);
        }
    }

    private float DrawVisibilitySliderRow(VisibilityElement element, string label, string iconName, float value)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        const float iconSize = 16f;
        const float labelWidth = 76f;
        const float valueWidth = 48f;
        const float gap = 4f;

        var iconRect = new Rect(rect.x, rect.y, iconSize, rect.height);
        var labelRect = new Rect(iconRect.xMax + gap, rect.y, labelWidth, rect.height);
        var fieldRect = new Rect(rect.xMax - valueWidth, rect.y, valueWidth, rect.height);
        var sliderRect = new Rect(labelRect.xMax + gap, rect.y, Mathf.Max(40f, fieldRect.x - labelRect.xMax - gap * 2f), rect.height);
        var labelHitRect = new Rect(iconRect.x, rect.y, labelRect.xMax - iconRect.x, rect.height);

        var icon = EditorGUIUtility.IconContent(iconName);
        if (icon != null && icon.image != null)
            GUI.Label(iconRect, icon);

        if (HandleVisibilityLabelClick(element, labelHitRect, value))
            return GetVisibilityElementValue(element);

        EditorGUIUtility.AddCursorRect(labelHitRect, MouseCursor.Link);

        var disabled = !IsVisibilityValueEnabled(value);
        if (disabled)
            DrawVisibilityElementOutline(new Rect(labelHitRect.x - 1f, labelHitRect.y, labelHitRect.width + 2f, labelHitRect.height), new Color(0.72f, 0.18f, 1f, 1f));

        GUI.Label(labelRect, label);
        var nextValue = GUI.HorizontalSlider(sliderRect, Mathf.Clamp01(value), 0f, 1f);
        nextValue = EditorGUI.FloatField(fieldRect, nextValue);
        nextValue = Mathf.Clamp01(nextValue);
        RememberVisibilityElementValue(element, nextValue);
        return nextValue;
    }

    private static void DrawVisibilityElementOutline(Rect rect, Color color)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        const float thickness = 1f;
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
#endif
