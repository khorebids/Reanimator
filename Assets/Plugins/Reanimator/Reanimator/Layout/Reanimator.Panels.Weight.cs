#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawWeightPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;

                EditorGUILayout.LabelField("Weight Slider", EditorStyles.boldLabel);

                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetWeightSliderSelection(out var selection, out var selectedVertexCount))
                {
                    EditorGUILayout.LabelField("Select a sprite vertex in preview.");
                    return;
                }

                var preserveSlotOrder = GUIUtility.hotControl != 0 || layerPreviewRenderer.IsWeightSliderWeightDragging;
                SyncEditGeometryWeightUi(selection, preserveSlotOrder);
                var boneOptions = BuildEditGeometryBoneOptions(selection.spriteBoneNames);

                EditorGUILayout.LabelField("Sprite", selection.layerName);
                EditorGUILayout.LabelField("Vertex", $"{selection.vertexIndex + 1}/{selection.vertexCount}");
                EditorGUILayout.LabelField("Selected", selectedVertexCount.ToString());

                weightModeIndex = EditorGUILayout.Popup("Mode", weightModeIndex, WeightModes);
                weightBoneIndex = EditorGUILayout.Popup("Bone", Mathf.Clamp(weightBoneIndex, 0, boneOptions.Length - 1), boneOptions);
                weightNormalize = EditorGUILayout.Toggle("Normalize", weightNormalize);
                weightAmount = EditorGUILayout.Slider("Amount", weightAmount, 0f, 1f);
                weightSmoothAmount = EditorGUILayout.Slider("Smooth", weightSmoothAmount, 0f, 1f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(weightBoneIndex <= 0 || selection.spriteBoneCount <= 0))
                    {
                        if (GUILayout.Button("Apply Slider", GUILayout.Height(22f)))
                        {
                            if (ApplyWeightSliderAmountToSelection(selection))
                            {
                                ShowNotification(new GUIContent("Weights updated"));
                                Repaint();
                            }
                        }
                    }

                    using (new EditorGUI.DisabledScope(selection.spriteBoneCount <= 0))
                    {
                        if (GUILayout.Button("Sort", GUILayout.Width(44f), GUILayout.Height(22f)))
                        {
                            if (SortWeightSliderSlotsByWeight())
                            {
                                ShowNotification(new GUIContent("Weights sorted"));
                                Repaint();
                            }
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(selection.spriteBoneCount <= 0))
                {
                    if (GUILayout.Button("Smooth Selection", GUILayout.Height(22f)))
                    {
                        if (layerPreviewRenderer != null &&
                            layerPreviewRenderer.TrySmoothWeightSliderSelection(weightSmoothAmount))
                        {
                            ShowNotification(new GUIContent("Weights smoothed"));
                            Repaint();
                        }
                    }
                }

                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Vertex Weight", EditorStyles.miniBoldLabel);

                for (var i = 0; i < vertexWeights.Length; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var previousEnabled = vertexWeightEnabled[i];
                        var previousBonePopup = Mathf.Clamp(editGeometryWeightBoneIndices[i] + 1, 0, boneOptions.Length - 1);
                        EditorGUI.BeginChangeCheck();
                        var enabled = GUILayout.Toggle(previousEnabled, GUIContent.none, GUILayout.Width(16f));
                        var bonePopup = EditorGUILayout.Popup(previousBonePopup, boneOptions, GUILayout.Width(126f));
                        var newWeight = EditorGUILayout.Slider(vertexWeights[i], 0f, 1f);
                        if (!EditorGUI.EndChangeCheck())
                            continue;

                        if (ApplyWeightSliderSlotChange(selection, i, previousEnabled, enabled, previousBonePopup, bonePopup, newWeight))
                            Repaint();
                    }
                }
            }
        }
    }

    private bool ApplyWeightSliderAmountToSelection(PsdLayerPreviewRenderer.EditGeometrySelectionInfo selection)
    {
        if (selection.spriteBoneCount <= 0)
            return false;

        var spriteBone = weightBoneIndex - 1;
        if (spriteBone < 0 || spriteBone >= selection.spriteBoneCount)
            return false;

        var currentWeight = GetWeightSliderUiBoneWeight(spriteBone);
        var nextWeight = currentWeight;
        switch (Mathf.Clamp(weightModeIndex, 0, WeightModes.Length - 1))
        {
            case 0:
                nextWeight = weightAmount;
                break;
            case 1:
                nextWeight = currentWeight + weightAmount;
                break;
            case 2:
                nextWeight = currentWeight - weightAmount;
                break;
            case 3:
                nextWeight = weightAmount;
                break;
        }

        SetWeightSliderUiBoneWeight(spriteBone, Mathf.Clamp01(nextWeight));
        return ApplyWeightSliderDirectWeights();
    }

    private bool SortWeightSliderSlotsByWeight()
    {
        var order = new int[vertexWeights.Length];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(order, CompareWeightSliderSlotsForSort);

        var sortedBones = new int[editGeometryWeightBoneIndices.Length];
        var sortedWeights = new float[vertexWeights.Length];
        var sortedEnabled = new bool[vertexWeightEnabled.Length];
        var changed = false;
        for (var i = 0; i < order.Length; i++)
        {
            var source = order[i];
            sortedBones[i] = editGeometryWeightBoneIndices[source];
            sortedWeights[i] = vertexWeights[source];
            sortedEnabled[i] = vertexWeightEnabled[source];
            if (source != i)
                changed = true;
        }

        for (var i = 0; i < order.Length; i++)
        {
            editGeometryWeightBoneIndices[i] = sortedBones[i];
            vertexWeights[i] = sortedWeights[i];
            vertexWeightEnabled[i] = sortedEnabled[i];
            if (i < editGeometryWeightValues.Length)
                editGeometryWeightValues[i] = sortedWeights[i];
        }

        if (!changed)
            return false;

        return ApplyWeightSliderDirectWeights(false);
    }

    private int CompareWeightSliderSlotsForSort(int left, int right)
    {
        var leftHasBone = left >= 0 &&
                          left < editGeometryWeightBoneIndices.Length &&
                          editGeometryWeightBoneIndices[left] >= 0;
        var rightHasBone = right >= 0 &&
                           right < editGeometryWeightBoneIndices.Length &&
                           editGeometryWeightBoneIndices[right] >= 0;
        if (leftHasBone != rightHasBone)
            return leftHasBone ? -1 : 1;

        var leftWeight = left >= 0 && left < vertexWeights.Length ? Mathf.Clamp01(vertexWeights[left]) : 0f;
        var rightWeight = right >= 0 && right < vertexWeights.Length ? Mathf.Clamp01(vertexWeights[right]) : 0f;
        var weightCompare = rightWeight.CompareTo(leftWeight);
        if (weightCompare != 0)
            return weightCompare;

        return left.CompareTo(right);
    }

    private bool ApplyWeightSliderSlotChange(
        PsdLayerPreviewRenderer.EditGeometrySelectionInfo selection,
        int slot,
        bool previousEnabled,
        bool enabled,
        int previousBonePopup,
        int bonePopup,
        float newWeight)
    {
        if (slot < 0 || slot >= vertexWeights.Length || selection.spriteBoneCount <= 0)
            return false;

        var boneChanged = bonePopup != previousBonePopup;
        var nextBone = bonePopup - 1;
        if (enabled && nextBone < 0)
        {
            if (layerPreviewRenderer != null &&
                layerPreviewRenderer.TrySuggestWeightSliderBone(BuildEnabledWeightSlotBoneIndices(slot), out var suggestedBone))
            {
                nextBone = suggestedBone;
                boneChanged = true;
            }
            else
            {
                enabled = false;
            }
        }

        if (boneChanged && nextBone >= 0)
            enabled = true;

        if (!enabled)
        {
            nextBone = -1;
            newWeight = 0f;
        }

        if (nextBone < 0)
        {
            enabled = false;
            newWeight = 0f;
        }
        else if ((enabled && !previousEnabled) || (boneChanged && newWeight <= 0.0001f))
        {
            newWeight = GetDefaultEnabledWeightSliderSlotWeight(slot);
        }

        editGeometryWeightBoneIndices[slot] = nextBone;
        ClearDuplicateWeightSliderBones(slot, nextBone);
        vertexWeightEnabled[slot] = enabled && nextBone >= 0;
        SetWeightSliderSlotWeight(slot, vertexWeightEnabled[slot] ? newWeight : 0f);
        return ApplyWeightSliderDirectWeights();
    }

    private int[] BuildEnabledWeightSlotBoneIndices(int excludedSlot)
    {
        var result = new List<int>(4);
        for (var i = 0; i < editGeometryWeightBoneIndices.Length; i++)
        {
            if (i == excludedSlot || !vertexWeightEnabled[i] || editGeometryWeightBoneIndices[i] < 0)
                continue;

            result.Add(editGeometryWeightBoneIndices[i]);
        }

        return result.ToArray();
    }

    private void ClearDuplicateWeightSliderBones(int preservedSlot, int spriteBone)
    {
        if (spriteBone < 0)
            return;

        for (var i = 0; i < editGeometryWeightBoneIndices.Length; i++)
        {
            if (i == preservedSlot || editGeometryWeightBoneIndices[i] != spriteBone)
                continue;

            editGeometryWeightBoneIndices[i] = -1;
            vertexWeightEnabled[i] = false;
            vertexWeights[i] = 0f;
        }
    }

    private float GetDefaultEnabledWeightSliderSlotWeight(int slot)
    {
        var activeCount = 0;
        for (var i = 0; i < vertexWeights.Length; i++)
        {
            if (i != slot && vertexWeightEnabled[i] && editGeometryWeightBoneIndices[i] >= 0)
                activeCount++;
        }

        if (activeCount <= 0)
            return 1f;

        return Mathf.Clamp01(1f / (activeCount + 1f));
    }

    private float GetWeightSliderUiBoneWeight(int spriteBone)
    {
        var result = 0f;
        for (var i = 0; i < editGeometryWeightBoneIndices.Length; i++)
        {
            if (!vertexWeightEnabled[i] || editGeometryWeightBoneIndices[i] != spriteBone)
                continue;

            result += Mathf.Clamp01(vertexWeights[i]);
        }

        return Mathf.Clamp01(result);
    }

    private void SetWeightSliderUiBoneWeight(int spriteBone, float weight)
    {
        var slot = -1;
        var weakestSlot = 0;
        var weakestWeight = float.MaxValue;
        for (var i = 0; i < editGeometryWeightBoneIndices.Length; i++)
        {
            var slotWeight = vertexWeightEnabled[i] ? Mathf.Clamp01(vertexWeights[i]) : 0f;
            if (editGeometryWeightBoneIndices[i] == spriteBone)
            {
                if (slot < 0)
                    slot = i;
                else
                {
                    vertexWeightEnabled[i] = false;
                    vertexWeights[i] = 0f;
                    editGeometryWeightBoneIndices[i] = -1;
                }
            }

            if (slotWeight < weakestWeight)
            {
                weakestWeight = slotWeight;
                weakestSlot = i;
            }
        }

        if (slot < 0)
            slot = weakestSlot;

        editGeometryWeightBoneIndices[slot] = spriteBone;
        vertexWeightEnabled[slot] = spriteBone >= 0;
        SetWeightSliderSlotWeight(slot, weight);
    }

    private void SetWeightSliderSlotWeight(int slot, float weight)
    {
        if (slot < 0 || slot >= vertexWeights.Length)
            return;

        vertexWeights[slot] = Mathf.Clamp01(weight);
        if (editGeometryWeightBoneIndices[slot] >= 0)
            vertexWeightEnabled[slot] = true;

        RedistributeWeightSliderRemainder(slot, vertexWeights[slot]);
    }

    private void RedistributeWeightSliderRemainder(int preservedSlot, float preservedWeight)
    {
        var otherSum = 0f;
        var fallbackSlot = -1;
        for (var i = 0; i < vertexWeights.Length; i++)
        {
            if (i == preservedSlot)
                continue;
            if (fallbackSlot < 0 && editGeometryWeightBoneIndices[i] >= 0)
                fallbackSlot = i;
            if (!vertexWeightEnabled[i])
                continue;

            otherSum += Mathf.Clamp01(vertexWeights[i]);
        }

        if (otherSum <= 0.0001f)
        {
            if (fallbackSlot >= 0 && preservedWeight < 0.9999f)
            {
                vertexWeightEnabled[fallbackSlot] = true;
                vertexWeights[fallbackSlot] = Mathf.Clamp01(1f - preservedWeight);
            }
            return;
        }

        var scale = Mathf.Clamp01(1f - preservedWeight) / otherSum;
        for (var i = 0; i < vertexWeights.Length; i++)
        {
            if (i == preservedSlot || !vertexWeightEnabled[i])
                continue;

            vertexWeights[i] = Mathf.Clamp01(vertexWeights[i] * scale);
        }
    }

    private bool ApplyWeightSliderDirectWeights(bool? normalizeOverride = null)
    {
        if (layerPreviewRenderer == null)
            return false;

        var boneIndices = new int[editGeometryWeightBoneIndices.Length];
        var weights = new float[editGeometryWeightValues.Length];
        for (var i = 0; i < boneIndices.Length; i++)
            boneIndices[i] = -1;

        var write = 0;
        for (var i = 0; i < weights.Length && write < weights.Length; i++)
        {
            if (!vertexWeightEnabled[i] || editGeometryWeightBoneIndices[i] < 0)
                continue;

            boneIndices[write] = editGeometryWeightBoneIndices[i];
            weights[write] = Mathf.Clamp01(vertexWeights[i]);
            write++;
        }

        return layerPreviewRenderer.TrySetWeightSliderSelectedVertexWeights(
            boneIndices,
            weights,
            normalizeOverride ?? weightNormalize);
    }
}
#endif
