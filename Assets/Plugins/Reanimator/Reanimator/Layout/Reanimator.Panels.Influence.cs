#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawBoneInfluenceToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Bone Influence", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Select a bone in preview. The panel shows which sprite vertices are weighted by that renderer bone.",
                    MessageType.Info);

                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetBoneInfluenceInfo(out var info))
                {
                    EditorGUILayout.LabelField("Select a bone in preview.");
                    DrawInfluenceToolSwitches();
                    return;
                }

                DrawInfluenceSwatch(info.color, info.boneName);
                EditorGUILayout.LabelField("Vertices", $"{info.influencedVertexCount}/{info.vertexCount}");
                EditorGUILayout.LabelField("Total Weight", info.totalWeight.ToString("0.###"));
                EditorGUILayout.LabelField("Max Weight", info.maxWeight.ToString("0.###"));

                using (new EditorGUI.DisabledScope(info.influencedVertexCount <= 0))
                {
                    if (GUILayout.Button("Select On Sprite", GUILayout.Height(22f)))
                    {
                        if (layerPreviewRenderer.TrySelectSelectedBoneInfluenceVertices(out var selectedCount, out var layerName, out var boneName, out var error))
                            ShowNotification(new GUIContent($"{boneName}: {selectedCount} vertices on {layerName}"));
                        else
                            ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "No influenced vertices" : error));
                        Repaint();
                    }
                }

                GUILayout.Space(4f);
                var layers = info.layers ?? Array.Empty<PsdLayerPreviewRenderer.BoneInfluenceLayerInfo>();
                for (var i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    EditorGUILayout.LabelField(
                        layer.layerName,
                        $"{layer.influencedVertexCount}/{layer.vertexCount}  w:{layer.totalWeight:0.##}  max:{layer.maxWeight:0.##}");
                }

                DrawInfluenceToolSwitches();
            }
        }
    }

    private void DrawSpriteInfluenceToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Sprite Influence", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Select a sprite in preview. The panel shows its sprite-bone slots and vertex weights.",
                    MessageType.Info);

                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetSpriteInfluenceInfo(out var info))
                {
                    EditorGUILayout.LabelField("Select a weighted sprite in preview.");
                    DrawInfluenceToolSwitches();
                    return;
                }

                var bones = info.bones ?? Array.Empty<PsdLayerPreviewRenderer.SpriteInfluenceBoneInfo>();
                EditorGUILayout.LabelField("Sprite", $"{info.layerName} ({info.vertexCount})");
                if (bones.Length == 0)
                {
                    EditorGUILayout.LabelField("No sprite bones.");
                    DrawInfluenceToolSwitches();
                    return;
                }

                spriteInfluenceBoneIndex = Mathf.Clamp(spriteInfluenceBoneIndex, 0, bones.Length - 1);
                var labels = BuildSpriteInfluenceBoneLabels(bones);
                spriteInfluenceBoneIndex = EditorGUILayout.Popup("Bone", spriteInfluenceBoneIndex, labels);
                var selectedBone = bones[spriteInfluenceBoneIndex];
                DrawInfluenceSwatch(selectedBone.color, selectedBone.boneName);
                EditorGUILayout.LabelField("Vertices", $"{selectedBone.vertexCount}/{info.vertexCount}");
                EditorGUILayout.LabelField("Total Weight", selectedBone.totalWeight.ToString("0.###"));
                EditorGUILayout.LabelField("Max Weight", selectedBone.maxWeight.ToString("0.###"));

                using (new EditorGUI.DisabledScope(selectedBone.vertexCount <= 0))
                {
                    if (GUILayout.Button("Select Vertices", GUILayout.Height(22f)))
                    {
                        if (layerPreviewRenderer.TrySelectSpriteInfluenceVertices(spriteInfluenceBoneIndex, out var selectedCount, out var layerName, out var boneName, out var error))
                            ShowNotification(new GUIContent($"{boneName}: {selectedCount} vertices on {layerName}"));
                        else
                            ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "No influenced vertices" : error));
                        Repaint();
                    }
                }

                GUILayout.Space(4f);
                for (var i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var rect = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
                        EditorGUI.DrawRect(rect, bone.color);
                        EditorGUILayout.LabelField(
                            $"{bone.spriteBoneIndex}: {bone.boneName}",
                            $"{bone.vertexCount}/{info.vertexCount}  w:{bone.totalWeight:0.##}",
                            EditorStyles.miniLabel);
                    }
                }

                DrawInfluenceToolSwitches();
            }
        }
    }

    private void DrawEditGeometryToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Edit Geometry", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Left click selects, Shift toggles, drag moves, empty drag rectangle-selects. Left double-click adds a vertex: contour/outside snaps to contour, inside creates an inner vertex. Right double-click deletes the hovered vertex. Delete/Backspace deletes selected vertices.",
                    MessageType.Info);

                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetEditGeometrySelectionInfo(out var selection, out var selectedVertexCount))
                {
                    EditorGUILayout.LabelField("Select a sprite vertex in preview.");
                    return;
                }

                EditorGUILayout.LabelField("Sprite", selection.layerName);
                EditorGUILayout.LabelField("Selected", $"{selectedVertexCount}/{selection.vertexCount}");

                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedVertexCount <= 0))
                    {
                        if (GUILayout.Button(new GUIContent("Deselect", "Clear the current vertex selection."), GUILayout.Height(22f)))
                            layerPreviewRenderer.DeselectEditGeometryVertexSelection();

                        if (GUILayout.Button(new GUIContent("Invert Sel", "Invert the selected vertices on this sprite."), GUILayout.Height(22f)))
                            layerPreviewRenderer.InvertEditGeometryVertexSelection();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedVertexCount <= 0))
                    {
                        if (GUILayout.Button(new GUIContent("Shrink", "Deselect selected vertices that touch unselected vertices."), GUILayout.Height(22f)))
                            layerPreviewRenderer.ShrinkEditGeometryVertexSelection();

                        if (GUILayout.Button(new GUIContent("Grow", "Add vertices that touch the current selection."), GUILayout.Height(22f)))
                            layerPreviewRenderer.GrowEditGeometryVertexSelection();
                    }
                }

                using (new EditorGUI.DisabledScope(selectedVertexCount <= 0))
                {
                    if (GUILayout.Button(new GUIContent("Only Contour", "Keep only selected vertices that are on the contour."), GUILayout.Height(22f)))
                        layerPreviewRenderer.KeepOnlyContourEditGeometryVertexSelection();
                }
            }
        }
    }

    private static string[] BuildEditGeometryBoneOptions(IReadOnlyList<string> spriteBoneNames)
    {
        var boneCount = spriteBoneNames != null ? spriteBoneNames.Count : 0;
        var options = new string[boneCount + 1];
        options[0] = "None";
        for (var i = 0; i < boneCount; i++)
        {
            var label = string.IsNullOrWhiteSpace(spriteBoneNames[i]) ? $"Bone {i}" : spriteBoneNames[i];
            options[i + 1] = $"{i}: {label}";
        }

        return options;
    }

    private void DrawInfluenceToolSwitches()
    {
        GUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Edit Geometry", GUILayout.Height(22f)))
                SetBottomToolMode(ReanimatorToolMode.EditGeometry);
            if (GUILayout.Button("Weight Brush", GUILayout.Height(22f)))
                SetBottomToolMode(ReanimatorToolMode.WeightBrush);
            if (GUILayout.Button("Slider", GUILayout.Height(22f)))
                SetBottomToolMode(ReanimatorToolMode.WeightSlider);
        }
    }

    private static string[] BuildSpriteInfluenceBoneLabels(IReadOnlyList<PsdLayerPreviewRenderer.SpriteInfluenceBoneInfo> bones)
    {
        var count = bones != null ? bones.Count : 0;
        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            var bone = bones[i];
            var name = string.IsNullOrWhiteSpace(bone.boneName) ? $"Bone {bone.spriteBoneIndex}" : bone.boneName;
            labels[i] = $"{bone.spriteBoneIndex}: {name}";
        }

        return labels;
    }

    private static void DrawInfluenceSwatch(Color color, string label)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var rect = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
            color.a = 1f;
            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(label) ? "Bone" : label, EditorStyles.boldLabel);
        }
    }

    private void SyncEditGeometryWeightUi(
        PsdLayerPreviewRenderer.EditGeometrySelectionInfo selection,
        bool preserveSlotOrder)
    {
        var selectionChanged = editGeometrySelectionLayerToken != selection.layerToken ||
                               editGeometrySelectionVertexIndex != selection.vertexIndex;
        if (!selectionChanged && IsEditGeometryWeightUiSynced(selection))
            return;

        editGeometrySelectionLayerToken = selection.layerToken;
        editGeometrySelectionVertexIndex = selection.vertexIndex;

        var spriteBoneCount = Mathf.Max(0, selection.spriteBoneCount);
        var bw = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(selection.boneWeight, spriteBoneCount, false, true);
        if (preserveSlotOrder && !selectionChanged)
        {
            ReanimatorWeightSliderAlgorithms.BuildStableSlotState(
                bw,
                spriteBoneCount,
                editGeometryWeightBoneIndices,
                out var stableBoneIndices,
                out var stableWeights);
            Array.Copy(stableBoneIndices, editGeometryWeightBoneIndices, editGeometryWeightBoneIndices.Length);
            Array.Copy(stableWeights, editGeometryWeightValues, editGeometryWeightValues.Length);
        }
        else
        {
            editGeometryWeightBoneIndices[0] = SanitizeEditGeometryBoneIndex(bw.boneIndex0, spriteBoneCount);
            editGeometryWeightBoneIndices[1] = SanitizeEditGeometryBoneIndex(bw.boneIndex1, spriteBoneCount, editGeometryWeightBoneIndices, 1);
            editGeometryWeightBoneIndices[2] = SanitizeEditGeometryBoneIndex(bw.boneIndex2, spriteBoneCount, editGeometryWeightBoneIndices, 2);
            editGeometryWeightBoneIndices[3] = SanitizeEditGeometryBoneIndex(bw.boneIndex3, spriteBoneCount, editGeometryWeightBoneIndices, 3);
            editGeometryWeightValues[0] = Mathf.Clamp01(bw.weight0);
            editGeometryWeightValues[1] = Mathf.Clamp01(bw.weight1);
            editGeometryWeightValues[2] = Mathf.Clamp01(bw.weight2);
            editGeometryWeightValues[3] = Mathf.Clamp01(bw.weight3);
        }

        for (var i = 0; i < vertexWeights.Length; i++)
        {
            vertexWeights[i] = editGeometryWeightValues[i];
            vertexWeightEnabled[i] = editGeometryWeightBoneIndices[i] >= 0;
        }
    }

    private bool IsEditGeometryWeightUiSynced(PsdLayerPreviewRenderer.EditGeometrySelectionInfo selection)
    {
        var spriteBoneCount = Mathf.Max(0, selection.spriteBoneCount);
        var bw = ReanimatorWeightSliderAlgorithms.SanitizeBoneWeight(selection.boneWeight, spriteBoneCount, false, true);
        var sanitized = new int[4];
        sanitized[0] = SanitizeEditGeometryBoneIndex(bw.boneIndex0, spriteBoneCount);
        sanitized[1] = SanitizeEditGeometryBoneIndex(bw.boneIndex1, spriteBoneCount, sanitized, 1);
        sanitized[2] = SanitizeEditGeometryBoneIndex(bw.boneIndex2, spriteBoneCount, sanitized, 2);
        sanitized[3] = SanitizeEditGeometryBoneIndex(bw.boneIndex3, spriteBoneCount, sanitized, 3);
        return IsEditGeometryWeightSlotUiSynced(0, sanitized[0], bw.weight0) &&
               IsEditGeometryWeightSlotUiSynced(1, sanitized[1], bw.weight1) &&
               IsEditGeometryWeightSlotUiSynced(2, sanitized[2], bw.weight2) &&
               IsEditGeometryWeightSlotUiSynced(3, sanitized[3], bw.weight3);
    }

    private bool IsEditGeometryWeightSlotUiSynced(int slot, int sanitizedBone, float weight)
    {
        var sanitizedWeight = Mathf.Clamp01(weight);
        var enabled = sanitizedBone >= 0;
        return editGeometryWeightBoneIndices[slot] == sanitizedBone &&
               vertexWeightEnabled[slot] == enabled &&
               Mathf.Approximately(vertexWeights[slot], sanitizedWeight);
    }

    private static int SanitizeEditGeometryBoneIndex(
        int boneIndex,
        int spriteBoneCount,
        IReadOnlyList<int> previousBoneIndices = null,
        int previousCount = 0)
    {
        if (spriteBoneCount <= 0)
            return -1;
        if (boneIndex < 0 || boneIndex >= spriteBoneCount)
            return -1;
        if (previousBoneIndices != null)
        {
            var count = Mathf.Min(previousCount, previousBoneIndices.Count);
            for (var i = 0; i < count; i++)
            {
                if (previousBoneIndices[i] == boneIndex)
                    return -1;
            }
        }
        return boneIndex;
    }

}
#endif
