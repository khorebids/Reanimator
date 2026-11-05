#if UNITY_EDITOR
using System;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public bool CancelWeightSliderTool()
    {
        var changed = weightSliderIsRectSelecting ||
                      weightSliderIsWeightDragging ||
                      weightSliderControlId != 0 ||
                      !string.IsNullOrEmpty(weightSliderSelectedLayerStableKey) ||
                      (weightSliderSelectedVertexIndices != null && weightSliderSelectedVertexIndices.Count > 0);

        if (changed)
            RegisterUndo("Cancel Weight Slider Tool");

        if (GUIUtility.hotControl == weightSliderControlId && weightSliderControlId != 0)
            GUIUtility.hotControl = 0;

        weightSliderIsRectSelecting = false;
        weightSliderRectToggleSelection = false;
        weightSliderControlId = 0;
        weightSliderRectStartCanvas = Vector2.zero;
        weightSliderRectCurrentCanvas = Vector2.zero;
        weightSliderIsWeightDragging = false;
        weightSliderWeightDragLayerStableKey = string.Empty;
        weightSliderWeightDragVertexIndex = -1;
        weightSliderWeightDragBones = Array.Empty<int>();
        weightSliderWeightDragStates = Array.Empty<WeightSliderVertexDragState>();
        weightSliderWeightDragStartCanvas = Vector2.zero;
        weightSliderWeightDragMoved = false;
        weightSliderWeightDragUndoRegistered = false;
        weightSliderSelectedLayerStableKey = string.Empty;
        weightSliderSelectedVertexIndices.Clear();
        return changed;
    }

    public bool CancelWeightBrushTool()
    {
        var changed = !string.IsNullOrEmpty(weightBrushLayerStableKey);
        if (changed)
            RegisterUndo("Cancel Weight Brush Tool");
        weightBrushLayerStableKey = string.Empty;
        return changed;
    }
}
#endif
