#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const float BoneVisibilityRowHeight = 18f;
    private const float BoneVisibilityEyeWidth = 18f;
    private const float BoneVisibilityFoldoutWidth = 15f;
    private const float BoneVisibilityToggleWidth = 16f;
    private const float BoneVisibilityRenameWidth = 20f;
    private const float BoneVisibilityDepthWidth = 25f;
    private const float BoneVisibilityColorWidth = 42f;
    private const float BoneVisibilityIndentWidth = 13f;
    private const float BoneVisibilityPinnedGap = 2f;
    private const float BoneVisibilityPinnedRightPadding = 2f;
    private const float BoneVisibilityTreeMinWidth = 180f;

    private static readonly Color BoneVisibilityRowA = new Color(0.205f, 0.205f, 0.205f, 1f);
    private static readonly Color BoneVisibilityRowB = new Color(0.245f, 0.245f, 0.245f, 1f);
    private static readonly Color BoneVisibilityHeader = new Color(0.19f, 0.19f, 0.19f, 1f);
    private static readonly Color BoneVisibilityGrid = new Color(0.13f, 0.13f, 0.13f, 1f);

    private struct BoneVisibilityRow
    {
        public int BoneId;
        public int Level;
        public bool Hierarchical;
    }

    private void DrawBoneVisibilityHeaderRow()
    {
        var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
        if (Event.current != null && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, BoneVisibilityHeader);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BoneVisibilityGrid);
        }

        SplitBoneVisibilityRow(rect, 0, 0f, out var eyeRect, out _, out _, out var nameRect, out var renameRect, out var depthRect, out var colorRect);
        DrawEyeGlyph(eyeRect, true, false);
        GUI.Label(nameRect, "Bone", EditorStyles.centeredGreyMiniLabel);
        GUI.Label(renameRect, "R", EditorStyles.centeredGreyMiniLabel);
        GUI.Label(depthRect, "Depth", EditorStyles.centeredGreyMiniLabel);
        GUI.Label(colorRect, "Color", EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawBoneVisibilityList(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, float listHeight)
    {
        if (bones == null || bones.Count == 0)
            return;
        if (renamingBoneId >= bones.Count)
            CancelBoneRename();
        SyncBoneVisibilityScrollToSelection(bones);

        var rows = BuildBoneVisibilityRows(bones);
        DrawVirtualizedBoneRows(bones, rows, listHeight);
    }

    private List<BoneVisibilityRow> BuildBoneVisibilityRows(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones)
    {
        var rows = new List<BoneVisibilityRow>(bones != null ? bones.Count : 0);
        if (bones == null || bones.Count == 0)
            return rows;

        if (!visibilityHierarchy)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                var boneName = bones[i].name ?? string.Empty;
                if (!string.IsNullOrEmpty(visibilitySearch) &&
                    boneName.IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                rows.Add(new BoneVisibilityRow { BoneId = i, Level = 0, Hierarchical = false });
            }
            return rows;
        }

        var childrenByParent = BuildChildrenByParent(bones);
        var filterCache = new Dictionary<int, bool>();
        var recursionPath = new HashSet<int>();
        var drawnNodes = new HashSet<int>();
        var roots = childrenByParent.TryGetValue(-1, out var rootList) ? rootList : null;
        if (roots == null || roots.Count == 0)
        {
            for (var i = 0; i < bones.Count; i++)
                CollectBoneNodeRows(bones, i, 0, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
            return rows;
        }

        for (var i = 0; i < roots.Count; i++)
            CollectBoneNodeRows(bones, roots[i], 0, childrenByParent, filterCache, recursionPath, drawnNodes, rows);

        for (var i = 0; i < bones.Count; i++)
        {
            if (drawnNodes.Contains(i))
                continue;
            CollectBoneNodeRows(bones, i, 0, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
        }

        return rows;
    }

    private void DrawVirtualizedBoneRows(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        IReadOnlyList<BoneVisibilityRow> rows,
        float listHeight)
    {
        var rowCount = rows != null ? rows.Count : 0;
        var metrics = ReanimatorVisibilityLayoutAlgorithms.CalculateVirtualizedRows(
            rowCount,
            listHeight,
            visibilityScroll.y,
            BoneVisibilityRowHeight);
        if (metrics.TotalRows <= 0)
            return;

        visibilityTreeContentWidth = CalculateBoneVisibilityTreeContentWidth(bones, rows);
        var contentRect = GUILayoutUtility.GetRect(1f, metrics.ContentHeight, GUILayout.ExpandWidth(true));
        for (var i = metrics.FirstRow; i < metrics.LastRow; i++)
        {
            var rowRect = new Rect(contentRect.x, contentRect.y + i * BoneVisibilityRowHeight, contentRect.width, BoneVisibilityRowHeight);
            if (i < rowCount)
            {
                var row = rows[i];
                DrawBoneRowAtRect(bones, row.BoneId, row.Level, row.Hierarchical, i, rowRect);
            }
            else
            {
                DrawBoneVisibilityRowBackground(rowRect, i);
            }
        }
    }

    private void GetVisibleRowRange(int rowCount, float listHeight, out int firstRow, out int lastRow)
    {
        var metrics = ReanimatorVisibilityLayoutAlgorithms.CalculateVirtualizedRows(
            rowCount,
            listHeight,
            visibilityScroll.y,
            BoneVisibilityRowHeight);
        firstRow = metrics.FirstRow;
        lastRow = metrics.LastRow;
    }

    private static float CalculateBoneVisibilityTreeContentWidth(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        IReadOnlyList<BoneVisibilityRow> rows)
    {
        var maxLevel = 0;
        var longestLabelWidth = 0f;
        if (bones != null && rows != null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.BoneId < 0 || row.BoneId >= bones.Count)
                    continue;

                maxLevel = Mathf.Max(maxLevel, row.Level);
                var bone = bones[row.BoneId];
                var label = string.IsNullOrEmpty(bone.name) ? $"bone_{row.BoneId}" : bone.name;
                longestLabelWidth = Mathf.Max(longestLabelWidth, EditorStyles.label.CalcSize(new GUIContent(label)).x);
            }
        }

        return ReanimatorVisibilityLayoutAlgorithms.CalculateTreeContentWidth(
            maxLevel,
            longestLabelWidth,
            includeToggle: true,
            BoneVisibilityEyeWidth,
            BoneVisibilityFoldoutWidth,
            BoneVisibilityToggleWidth,
            BoneVisibilityIndentWidth,
            BoneVisibilityTreeMinWidth);
    }

    private void CollectBoneNodeRows(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int nodeId,
        int level,
        Dictionary<int, List<int>> childrenByParent,
        Dictionary<int, bool> filterCache,
        HashSet<int> recursionPath,
        HashSet<int> drawnNodes,
        List<BoneVisibilityRow> rows)
    {
        if (nodeId < 0 || nodeId >= bones.Count)
            return;
        if (!recursionPath.Add(nodeId))
            return;
        if (drawnNodes.Contains(nodeId))
            return;

        try
        {
            if (!PassesBoneFilter(nodeId, bones, childrenByParent, filterCache, new HashSet<int>()))
                return;

            rows.Add(new BoneVisibilityRow { BoneId = nodeId, Level = level, Hierarchical = true });
            drawnNodes.Add(nodeId);

            if (IsNodeCollapsed(nodeId))
                return;
            if (!childrenByParent.TryGetValue(nodeId, out var children))
                return;

            for (var i = 0; i < children.Count; i++)
                CollectBoneNodeRows(bones, children[i], level + 1, childrenByParent, filterCache, recursionPath, drawnNodes, rows);
        }
        finally
        {
            recursionPath.Remove(nodeId);
        }
    }

    private void SyncBoneVisibilityScrollToSelection(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones)
    {
        if (layerPreviewRenderer == null || bones == null || bones.Count == 0)
            return;

        var selected = layerPreviewRenderer.SelectedBoneIndex;
        if (selected < 0 || selected >= bones.Count)
        {
            visibilityAutoScrolledBoneIndex = -1;
            return;
        }

        var search = visibilitySearch ?? string.Empty;
        if (visibilityAutoScrolledBoneIndex == selected &&
            visibilityAutoScrolledHierarchy == visibilityHierarchy &&
            string.Equals(visibilityAutoScrolledSearch, search, StringComparison.Ordinal))
            return;

        if (visibilityHierarchy)
            ExpandBoneAncestors(bones, selected);

        if (!TryGetVisibleBoneRowIndex(bones, selected, out var rowIndex))
            return;

        visibilityScroll.y = Mathf.Max(0f, rowIndex * BoneVisibilityRowHeight - BoneVisibilityRowHeight * 2f);
        visibilityAutoScrolledBoneIndex = selected;
        visibilityAutoScrolledHierarchy = visibilityHierarchy;
        visibilityAutoScrolledSearch = search;
        Repaint();
    }

    private bool TryGetVisibleBoneRowIndex(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int selectedBoneIndex, out int rowIndex)
    {
        rowIndex = 0;
        if (bones == null || selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count)
            return false;

        if (!visibilityHierarchy)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                var boneName = bones[i].name ?? string.Empty;
                if (!string.IsNullOrEmpty(visibilitySearch) &&
                    boneName.IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (i == selectedBoneIndex)
                    return true;

                rowIndex++;
            }

            return false;
        }

        var childrenByParent = BuildChildrenByParent(bones);
        var filterCache = new Dictionary<int, bool>();
        var visited = new HashSet<int>();
        var roots = childrenByParent.TryGetValue(-1, out var rootList) ? rootList : null;
        if (roots == null || roots.Count == 0)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                if (TryFindVisibleBoneRowRecursive(bones, i, selectedBoneIndex, childrenByParent, filterCache, visited, ref rowIndex))
                    return true;
            }
            return false;
        }

        for (var i = 0; i < roots.Count; i++)
        {
            if (TryFindVisibleBoneRowRecursive(bones, roots[i], selectedBoneIndex, childrenByParent, filterCache, visited, ref rowIndex))
                return true;
        }

        for (var i = 0; i < bones.Count; i++)
        {
            if (visited.Contains(i))
                continue;
            if (TryFindVisibleBoneRowRecursive(bones, i, selectedBoneIndex, childrenByParent, filterCache, visited, ref rowIndex))
                return true;
        }

        return false;
    }

    private bool TryFindVisibleBoneRowRecursive(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int nodeId,
        int selectedBoneIndex,
        Dictionary<int, List<int>> childrenByParent,
        Dictionary<int, bool> filterCache,
        HashSet<int> visited,
        ref int rowIndex)
    {
        if (nodeId < 0 || nodeId >= bones.Count || visited.Contains(nodeId))
            return false;
        visited.Add(nodeId);

        if (!PassesBoneFilter(nodeId, bones, childrenByParent, filterCache, new HashSet<int>()))
            return false;

        if (nodeId == selectedBoneIndex)
            return true;

        rowIndex++;
        if (IsNodeCollapsed(nodeId))
            return false;
        if (!childrenByParent.TryGetValue(nodeId, out var children))
            return false;

        for (var i = 0; i < children.Count; i++)
        {
            if (TryFindVisibleBoneRowRecursive(bones, children[i], selectedBoneIndex, childrenByParent, filterCache, visited, ref rowIndex))
                return true;
        }

        return false;
    }

    private void ExpandBoneAncestors(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int boneId)
    {
        if (bones == null || boneId < 0 || boneId >= bones.Count)
            return;

        var parent = bones[boneId].parentId;
        var guard = 0;
        while (parent >= 0 && parent < bones.Count && guard++ < bones.Count)
        {
            SetNodeCollapsed(parent, false);
            parent = bones[parent].parentId;
        }
    }

    private void DrawBoneNodeRecursive(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int nodeId,
        int level,
        Dictionary<int, List<int>> childrenByParent,
        Dictionary<int, bool> filterCache,
        HashSet<int> recursionPath,
        HashSet<int> drawnNodes,
        ref int rowIndex)
    {
        if (nodeId < 0 || nodeId >= bones.Count)
            return;
        if (!recursionPath.Add(nodeId))
            return;
        if (drawnNodes.Contains(nodeId))
            return;

        try
        {
            if (!PassesBoneFilter(nodeId, bones, childrenByParent, filterCache, new HashSet<int>()))
                return;

            DrawBoneRow(bones, nodeId, level, true, ref rowIndex);
            drawnNodes.Add(nodeId);

            if (IsNodeCollapsed(nodeId))
                return;
            if (!childrenByParent.TryGetValue(nodeId, out var children))
                return;

            for (var i = 0; i < children.Count; i++)
                DrawBoneNodeRecursive(bones, children[i], level + 1, childrenByParent, filterCache, recursionPath, drawnNodes, ref rowIndex);
        }
        finally
        {
            recursionPath.Remove(nodeId);
        }
    }

    private void DrawBoneRow(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int boneId, int level, bool hierarchical, ref int rowIndex)
    {
        var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
        DrawBoneRowAtRect(bones, boneId, level, hierarchical, rowIndex++, rect);
    }

    private void DrawBoneRowAtRect(
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneId,
        int level,
        bool hierarchical,
        int rowIndex,
        Rect rect)
    {
        var bone = bones[boneId];
        var boneName = string.IsNullOrEmpty(bone.name) ? $"bone_{boneId}" : bone.name;
        var generatedBoneName = IsGeneratedBoneName(boneName);
        DrawBoneVisibilityRowBackground(rect, rowIndex);
        if (generatedBoneName && Event.current != null && Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(1f, 0.78f, 0.18f, 0.26f));
        var selected = layerPreviewRenderer != null && layerPreviewRenderer.SelectedBoneIndex == boneId;
        if (selected && Event.current != null && Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.95f, 0.72f, 0.18f, 0.34f));

        SplitBoneVisibilityRow(rect, hierarchical ? level : 0, visibilityScroll.x, out var eyeRect, out var foldoutRect, out var toggleRect, out var nameRect, out var renameRect, out var depthRect, out var colorRect);
        HandleBoneRowSelection(rect, eyeRect, foldoutRect, toggleRect, renameRect, generatedBoneName, depthRect, colorRect, boneId);

        var visible = bone.visible;
        if (DrawEyeGlyph(eyeRect, visible, true))
        {
            layerPreviewRenderer?.SetBoneVisible(boneId, !visible);
            Repaint();
        }

        if (hierarchical)
        {
            var hasChildren = HasChildren(bones, boneId);
            if (hasChildren)
            {
                var expanded = !IsNodeCollapsed(boneId);
                var nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (nextExpanded != expanded)
                    SetNodeCollapsed(boneId, !nextExpanded);
            }
        }

        EditorGUI.BeginChangeCheck();
        visible = EditorGUI.Toggle(toggleRect, visible);
        if (EditorGUI.EndChangeCheck())
        {
            layerPreviewRenderer?.SetBoneVisible(boneId, visible);
        }

        DrawBoneNameCell(nameRect, boneId, boneName, selected);
        DrawBoneRenamePresetButton(renameRect, bones, boneId, boneName);
        var depth = DrawDepthFieldWithDrag(depthRect, bone.depth, true, boneId, null, out var depthChanged);
        if (depthChanged)
            layerPreviewRenderer?.SetBoneDepth(boneId, depth);

        EditorGUI.BeginChangeCheck();
        var color = DrawBoneColorField(colorRect, bone.color);
        if (EditorGUI.EndChangeCheck())
        {
            layerPreviewRenderer?.SetBoneColor(boneId, color);
        }
    }

    private void HandleBoneRowSelection(
        Rect rowRect,
        Rect eyeRect,
        Rect foldoutRect,
        Rect toggleRect,
        Rect renameRect,
        bool hasRenameButton,
        Rect depthRect,
        Rect colorRect,
        int boneId)
    {
        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
            return;
        if (!rowRect.Contains(e.mousePosition))
            return;
        if (eyeRect.Contains(e.mousePosition) ||
            foldoutRect.Contains(e.mousePosition) ||
            toggleRect.Contains(e.mousePosition) ||
            hasRenameButton && renameRect.Contains(e.mousePosition) ||
            depthRect.Contains(e.mousePosition) ||
            colorRect.Contains(e.mousePosition))
            return;

        if (layerPreviewRenderer != null && layerPreviewRenderer.SelectBone(boneId))
            Repaint();
    }

    private static void SplitBoneVisibilityRow(
        Rect rect,
        int level,
        float horizontalScroll,
        out Rect eyeRect,
        out Rect foldoutRect,
        out Rect toggleRect,
        out Rect nameRect,
        out Rect renameRect,
        out Rect depthRect,
        out Rect colorRect)
    {
        var layout = ReanimatorVisibilityLayoutAlgorithms.CalculatePinnedRowRects(
            rect,
            level,
            horizontalScroll,
            includeToggle: true,
            BoneVisibilityEyeWidth,
            BoneVisibilityFoldoutWidth,
            BoneVisibilityToggleWidth,
            BoneVisibilityRenameWidth,
            BoneVisibilityDepthWidth,
            BoneVisibilityColorWidth,
            BoneVisibilityIndentWidth,
            BoneVisibilityTreeMinWidth,
            BoneVisibilityPinnedGap,
            BoneVisibilityPinnedRightPadding);
        eyeRect = layout.EyeRect;
        foldoutRect = layout.FoldoutRect;
        toggleRect = layout.ToggleRect;
        nameRect = layout.NameRect;
        renameRect = layout.RenameRect;
        depthRect = layout.DepthRect;
        colorRect = layout.ColorRect;
    }

    private static void DrawBoneVisibilityRowBackground(Rect rect, int rowIndex)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        EditorGUI.DrawRect(rect, rowIndex % 2 == 0 ? BoneVisibilityRowA : BoneVisibilityRowB);
    }

    private void DrawEmptyBoneVisibilityRows(int drawnRows, float listHeight)
    {
        var targetRows = Mathf.CeilToInt(Mathf.Max(0f, listHeight) / BoneVisibilityRowHeight);
        for (var i = drawnRows; i < targetRows; i++)
        {
            var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
            DrawBoneVisibilityRowBackground(rect, i);
        }
    }

    private static bool DrawEyeGlyph(Rect rect, bool visible, bool interactive)
    {
        var content = GetEyeContent(visible);
        var hit = interactive && GUI.Button(rect, content, GUIStyle.none);
        if (!interactive)
            GUI.Label(rect, content, GUIStyle.none);
        return hit;
    }

    private static GUIContent GetEyeContent(bool visible)
    {
        var content = EditorGUIUtility.IconContent(visible ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff");
        if (content != null && content.image != null)
            return content;

        return new GUIContent(visible ? "◉" : "○");
    }

    private static Color DrawBoneColorField(Rect rect, Color color)
    {
        DrawTinyChecker(rect);
        return EditorGUI.ColorField(rect, GUIContent.none, color, true, true, false);
    }

    private static void DrawTinyChecker(Rect rect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        const float size = 4f;
        var a = new Color(0.56f, 0.56f, 0.56f, 1f);
        var b = new Color(0.34f, 0.34f, 0.34f, 1f);
        for (var y = rect.y; y < rect.yMax; y += size)
        {
            for (var x = rect.x; x < rect.xMax; x += size)
            {
                var xi = Mathf.FloorToInt((x - rect.x) / size);
                var yi = Mathf.FloorToInt((y - rect.y) / size);
                var tile = new Rect(x, y, Mathf.Min(size, rect.xMax - x), Mathf.Min(size, rect.yMax - y));
                EditorGUI.DrawRect(tile, (xi + yi) % 2 == 0 ? a : b);
            }
        }
    }

    private void DrawBoneNameCell(Rect rect, int boneId, string boneName, bool selected)
    {
        if (renamingBoneId != boneId)
        {
            var style = selected ? EditorStyles.whiteLabel : EditorStyles.label;
            GUI.Label(rect, boneName, style);

            var clickEvent = Event.current;
            if (clickEvent != null && clickEvent.type == EventType.MouseDown && clickEvent.button == 0 && rect.Contains(clickEvent.mousePosition))
            {
                if (layerPreviewRenderer != null && layerPreviewRenderer.SelectBone(boneId))
                    Repaint();
                if (clickEvent.clickCount >= 2)
                    BeginBoneRename(boneId, boneName);
                clickEvent.Use();
            }
            return;
        }

        var controlName = $"BoneRenameField_{boneId}";
        GUI.SetNextControlName(controlName);
        var edited = EditorGUI.TextField(rect, renamingBoneValue);
        if (!string.Equals(edited, renamingBoneValue, StringComparison.Ordinal))
            renamingBoneValue = edited;

        if (focusBoneRenameField)
        {
            EditorGUI.FocusTextInControl(controlName);
            focusBoneRenameField = false;
        }

        var evt = Event.current;
        if (evt == null)
            return;

        if (evt.type == EventType.MouseDown && evt.button == 0 && !rect.Contains(evt.mousePosition))
        {
            CommitBoneRename();
            return;
        }

        if (!string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal))
            return;

        if (evt.type == EventType.KeyDown)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                CommitBoneRename();
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CancelBoneRename();
                evt.Use();
            }

            return;
        }

    }

    private void DrawBoneRenamePresetButton(
        Rect rect,
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        int boneId,
        string boneName)
    {
        if (!IsGeneratedBoneName(boneName))
            return;

        if (GUI.Button(rect, "R", EditorStyles.miniButton))
            ShowBoneNamePresetMenu(layerPreviewRenderer, bones, boneId);
    }

    private static bool IsGeneratedBoneName(string boneName)
    {
        return !string.IsNullOrEmpty(boneName) &&
               boneName.StartsWith("bone_", StringComparison.OrdinalIgnoreCase);
    }

    private void BeginBoneRename(int boneId, string currentName)
    {
        if (renamingBoneId == boneId)
            return;

        GUI.FocusControl(null);
        CommitBoneRename();
        renamingBoneId = boneId;
        renamingBoneValue = currentName ?? string.Empty;
        focusBoneRenameField = true;
    }

    private void CommitBoneRename()
    {
        if (renamingBoneId < 0)
            return;

        var boneId = renamingBoneId;
        var newName = (renamingBoneValue ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(newName))
            layerPreviewRenderer?.RenameBone(boneId, newName);

        renamingBoneId = -1;
        renamingBoneValue = string.Empty;
        focusBoneRenameField = false;
    }

    private void CancelBoneRename()
    {
        renamingBoneId = -1;
        renamingBoneValue = string.Empty;
        focusBoneRenameField = false;
    }

    private void HandleDepthDragLifecycle()
    {
        var e = Event.current;
        if (!isDepthDragActive || e == null)
            return;

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            EndDepthDrag();
            e.Use();
        }
    }

    private bool HandleDepthDragForBone(int boneId, Rect fieldRect, ref int value)
    {
        return HandleDepthDragCore(fieldRect, true, boneId, null, ref value);
    }

    private int DrawDepthFieldWithDrag(int currentValue, bool isBone, int boneId, string layerKey, out bool changed)
    {
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(50f));
        return DrawDepthFieldWithDrag(rect, currentValue, isBone, boneId, layerKey, out changed);
    }

    private int DrawDepthFieldWithDrag(Rect rect, int currentValue, bool isBone, int boneId, string layerKey, out bool changed)
    {
        var value = currentValue;
        changed = false;

        var dragChanged = isBone
            ? HandleDepthDragForBone(boneId, rect, ref value)
            : HandleDepthDragForLayerByKey(layerKey, rect, ref value);
        if (dragChanged)
            changed = true;

        EditorGUI.BeginChangeCheck();
        value = EditorGUI.DelayedIntField(rect, value);
        if (EditorGUI.EndChangeCheck())
            changed = true;

        return value;
    }

    private bool HandleDepthDragForLayerByKey(string layerKey, Rect fieldRect, ref int value)
    {
        return HandleDepthDragCore(fieldRect, false, -1, layerKey, ref value);
    }

    private bool HandleDepthDragCore(Rect fieldRect, bool isBone, int boneId, string layerKey, ref int value)
    {
        var e = Event.current;
        if (e == null)
            return false;

        var handleRect = fieldRect;
        handleRect.xMin = handleRect.xMax - 6f;
        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.SlideArrow);

        if (!isDepthDragActive)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && handleRect.Contains(e.mousePosition))
            {
                isDepthDragActive = true;
                depthDragIsBone = isBone;
                depthDragBoneId = boneId;
                depthDragLayerKey = layerKey;
                depthDragStartValue = value;
                depthDragStartMouse = e.mousePosition;
                depthDragControlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = depthDragControlId;
                e.Use();
            }
            return false;
        }

        if (depthDragIsBone != isBone)
            return false;
        if (depthDragIsBone && depthDragBoneId != boneId)
            return false;
        if (!depthDragIsBone && !string.Equals(depthDragLayerKey, layerKey, StringComparison.Ordinal))
            return false;

        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == depthDragControlId)
        {
            var deltaX = e.mousePosition.x - depthDragStartMouse.x;
            var step = Mathf.FloorToInt(Mathf.Abs(deltaX) / 8f) * Math.Sign(deltaX);
            var nextValue = depthDragStartValue + step;
            if (nextValue != value)
            {
                value = nextValue;
                e.Use();
                Repaint();
                return true;
            }

            e.Use();
        }

        return false;
    }

    private void EndDepthDrag()
    {
        isDepthDragActive = false;
        depthDragIsBone = false;
        depthDragBoneId = -1;
        depthDragLayerKey = null;
        depthDragStartValue = 0;
        depthDragStartMouse = Vector2.zero;

        if (GUIUtility.hotControl == depthDragControlId)
            GUIUtility.hotControl = 0;

        depthDragControlId = 0;
    }

    private static string BuildLayerKey(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (layer == null)
            return string.Empty;
        return layer.layerId != 0 ? layer.layerId.ToString() : layer.name ?? string.Empty;
    }

    private Dictionary<int, List<int>> BuildChildrenByParent(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones)
    {
        var result = new Dictionary<int, List<int>>();
        for (var i = 0; i < bones.Count; i++)
        {
            var parentId = bones[i].parentId;
            if (parentId < 0 || parentId >= bones.Count)
                parentId = -1;

            if (!result.TryGetValue(parentId, out var list))
            {
                list = new List<int>();
                result[parentId] = list;
            }

            list.Add(i);
        }

        return result;
    }

    private bool PassesBoneFilter(
        int nodeId,
        IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones,
        Dictionary<int, List<int>> childrenByParent,
        Dictionary<int, bool> cache,
        HashSet<int> recursionPath)
    {
        if (nodeId < 0 || nodeId >= bones.Count)
            return false;
        if (string.IsNullOrEmpty(visibilitySearch))
            return true;
        if (cache.TryGetValue(nodeId, out var cached))
            return cached;
        if (!recursionPath.Add(nodeId))
            return false;

        var passes = (bones[nodeId].name ?? string.Empty).IndexOf(visibilitySearch, StringComparison.OrdinalIgnoreCase) >= 0;
        if (!passes && childrenByParent.TryGetValue(nodeId, out var children))
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (!PassesBoneFilter(children[i], bones, childrenByParent, cache, recursionPath))
                    continue;
                passes = true;
                break;
            }
        }

        recursionPath.Remove(nodeId);
        cache[nodeId] = passes;
        return passes;
    }

    private bool HasChildren(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int nodeId)
    {
        for (var i = 0; i < bones.Count; i++)
        {
            if (bones[i].parentId == nodeId)
                return true;
        }

        return false;
    }

    private int GetBoneHierarchyDepth(IReadOnlyList<PsdLayerPreviewRenderer.BoneInfo> bones, int nodeId)
    {
        var depth = 0;
        var guard = 0;
        var parentId = bones[nodeId].parentId;
        while (parentId >= 0 && parentId < bones.Count && guard < bones.Count)
        {
            depth++;
            parentId = bones[parentId].parentId;
            guard++;
        }

        return depth;
    }

    private bool IsNodeCollapsed(int nodeId)
    {
        return collapsedBoneNodes != null && collapsedBoneNodes.Contains(nodeId);
    }

    private void SetNodeCollapsed(int nodeId, bool collapsed)
    {
        if (collapsedBoneNodes == null)
            collapsedBoneNodes = new List<int>();

        if (collapsed)
        {
            if (!collapsedBoneNodes.Contains(nodeId))
                collapsedBoneNodes.Add(nodeId);
            return;
        }

        collapsedBoneNodes.Remove(nodeId);
    }

    private void CollapseAllBoneNodes()
    {
        collapsedBoneNodes ??= new List<int>();
        collapsedBoneNodes.Clear();
        var bones = layerPreviewRenderer != null ? layerPreviewRenderer.Bones : null;
        if (bones == null)
            return;

        for (var i = 0; i < bones.Count; i++)
        {
            if (HasChildren(bones, i))
                collapsedBoneNodes.Add(i);
        }
    }
}

internal readonly struct ReanimatorVirtualizedRows
{
    public ReanimatorVirtualizedRows(int totalRows, int firstRow, int lastRow, float contentHeight)
    {
        TotalRows = totalRows;
        FirstRow = firstRow;
        LastRow = lastRow;
        ContentHeight = contentHeight;
    }

    public int TotalRows { get; }
    public int FirstRow { get; }
    public int LastRow { get; }
    public float ContentHeight { get; }
}

internal readonly struct ReanimatorVisibilityPinnedRowRects
{
    public ReanimatorVisibilityPinnedRowRects(Rect eyeRect, Rect foldoutRect, Rect toggleRect, Rect nameRect, Rect renameRect, Rect depthRect, Rect colorRect, float treeViewportWidth)
    {
        EyeRect = eyeRect;
        FoldoutRect = foldoutRect;
        ToggleRect = toggleRect;
        NameRect = nameRect;
        RenameRect = renameRect;
        DepthRect = depthRect;
        ColorRect = colorRect;
        TreeViewportWidth = treeViewportWidth;
    }

    public Rect EyeRect { get; }
    public Rect FoldoutRect { get; }
    public Rect ToggleRect { get; }
    public Rect NameRect { get; }
    public Rect RenameRect { get; }
    public Rect DepthRect { get; }
    public Rect ColorRect { get; }
    public float TreeViewportWidth { get; }
}

internal static class ReanimatorVisibilityLayoutAlgorithms
{
    public static ReanimatorVirtualizedRows CalculateVirtualizedRows(
        int rowCount,
        float listHeight,
        float scrollY,
        float rowHeight,
        int bufferRows = 4)
    {
        rowCount = Mathf.Max(0, rowCount);
        rowHeight = Mathf.Max(0.0001f, rowHeight);
        listHeight = Mathf.Max(0f, listHeight);
        scrollY = Mathf.Max(0f, scrollY);
        bufferRows = Mathf.Max(0, bufferRows);

        var viewportRows = Mathf.CeilToInt(listHeight / rowHeight);
        var totalRows = Mathf.Max(rowCount, viewportRows);
        if (totalRows <= 0)
            return new ReanimatorVirtualizedRows(0, 0, 0, 0f);

        var contentHeight = totalRows * rowHeight;
        var maxScrollY = Mathf.Max(0f, contentHeight - listHeight);
        scrollY = Mathf.Min(scrollY, maxScrollY);
        var firstRow = Mathf.Clamp(Mathf.FloorToInt(scrollY / rowHeight) - bufferRows, 0, totalRows);
        var lastRow = Mathf.Clamp(Mathf.CeilToInt((scrollY + listHeight) / rowHeight) + bufferRows, firstRow, totalRows);
        return new ReanimatorVirtualizedRows(totalRows, firstRow, lastRow, contentHeight);
    }

    public static ReanimatorVisibilityPinnedRowRects CalculatePinnedRowRects(
        Rect rect,
        int level,
        float horizontalScroll,
        bool includeToggle,
        float eyeWidth,
        float foldoutWidth,
        float toggleWidth,
        float renameWidth,
        float depthWidth,
        float colorWidth,
        float indentWidth,
        float treeMinWidth,
        float gap,
        float rightPadding)
    {
        level = Mathf.Max(0, level);
        horizontalScroll = Mathf.Max(0f, horizontalScroll);
        eyeWidth = Mathf.Max(0f, eyeWidth);
        foldoutWidth = Mathf.Max(0f, foldoutWidth);
        toggleWidth = includeToggle ? Mathf.Max(0f, toggleWidth) : 0f;
        renameWidth = Mathf.Max(0f, renameWidth);
        depthWidth = Mathf.Max(0f, depthWidth);
        colorWidth = Mathf.Max(0f, colorWidth);
        indentWidth = Mathf.Max(0f, indentWidth);
        treeMinWidth = Mathf.Max(0f, treeMinWidth);
        gap = Mathf.Max(0f, gap);
        rightPadding = Mathf.Max(0f, rightPadding);

        var rowY = rect.y + 1f;
        var rowHeight = Mathf.Max(0f, rect.height - 2f);
        var colorRect = new Rect(rect.xMax - rightPadding - colorWidth, rowY, colorWidth, rowHeight);
        var depthRect = new Rect(colorRect.x - gap - depthWidth, rowY, depthWidth, rowHeight);
        var renameRect = renameWidth > 0f
            ? new Rect(depthRect.x - gap - renameWidth, rowY, renameWidth, rowHeight)
            : new Rect(depthRect.x, rowY, 0f, rowHeight);
        var treeViewportEnd = renameWidth > 0f ? renameRect.x - gap : depthRect.x - gap;
        var treeViewportWidth = Mathf.Max(0f, treeViewportEnd - rect.x);

        var eyeRect = new Rect(rect.x + 3f - horizontalScroll, rowY, eyeWidth, rowHeight);
        var treeX = eyeRect.xMax + 4f + level * indentWidth;
        var foldoutRect = new Rect(treeX, rowY, foldoutWidth, rowHeight);
        var toggleRect = includeToggle
            ? new Rect(foldoutRect.xMax, rowY, toggleWidth, rowHeight)
            : new Rect(foldoutRect.xMax, rowY, 0f, rowHeight);
        var nameX = (includeToggle ? toggleRect.xMax : foldoutRect.xMax) + 3f;
        var nameWidth = Mathf.Max(20f, treeViewportEnd - nameX);
        if (treeViewportWidth <= 0f)
            nameWidth = 20f;
        var nameRect = new Rect(nameX, rowY, nameWidth, rowHeight);

        return new ReanimatorVisibilityPinnedRowRects(eyeRect, foldoutRect, toggleRect, nameRect, renameRect, depthRect, colorRect, Mathf.Max(treeMinWidth, treeViewportWidth));
    }

    public static float CalculateTreeContentWidth(
        int maxLevel,
        float longestLabelWidth,
        bool includeToggle,
        float eyeWidth,
        float foldoutWidth,
        float toggleWidth,
        float indentWidth,
        float treeMinWidth)
    {
        maxLevel = Mathf.Max(0, maxLevel);
        longestLabelWidth = Mathf.Max(0f, longestLabelWidth);
        eyeWidth = Mathf.Max(0f, eyeWidth);
        foldoutWidth = Mathf.Max(0f, foldoutWidth);
        toggleWidth = includeToggle ? Mathf.Max(0f, toggleWidth) : 0f;
        indentWidth = Mathf.Max(0f, indentWidth);
        treeMinWidth = Mathf.Max(0f, treeMinWidth);

        var contentWidth = 3f + eyeWidth + 4f + maxLevel * indentWidth + foldoutWidth + toggleWidth + 3f + longestLabelWidth + 12f;
        return Mathf.Max(treeMinWidth, contentWidth);
    }
}
#endif


