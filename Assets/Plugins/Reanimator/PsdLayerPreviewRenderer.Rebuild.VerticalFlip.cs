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
    private bool ShouldFlipLayerPositionsVertically()
    {
        if (cachedDocumentSize.y <= 0 || layerInfos == null || layerInfos.Count == 0 || bones == null || bones.Count == 0)
            return false;

        var boneBounds = GetBoneWorldBounds();
        if (boneBounds.width <= 0f || boneBounds.height <= 0f)
            return false;

        var rawBounds = GetLayerBoundsForVerticalMode(false);
        var flippedBounds = GetLayerBoundsForVerticalMode(true);
        var rawScore = ScoreBoundsFit(rawBounds, boneBounds);
        var flippedScore = ScoreBoundsFit(flippedBounds, boneBounds);
        if (float.IsNaN(rawScore) || float.IsInfinity(rawScore))
            rawScore = float.MaxValue * 0.5f;
        if (float.IsNaN(flippedScore) || float.IsInfinity(flippedScore))
            flippedScore = float.MaxValue * 0.5f;

        ReanimatorLog.Trace(
            $"[PSD DEBUG RED] LayerYFlip raw={rawScore:F3} flipped={flippedScore:F3} docH={cachedDocumentSize.y}",
            IsDebugModeEnabled);

        return flippedScore + 0.05f < rawScore;
    }

    private Rect GetLayerBoundsForVerticalMode(bool flipped)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var docHeight = Mathf.Max(1f, cachedDocumentSize.y);

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;

            var position = layer.position;
            if (flipped)
                position.y = docHeight - position.y - layer.sourceRect.height;

            min = Vector2.Min(min, position);
            max = Vector2.Max(max, position + new Vector2(layer.sourceRect.width, layer.sourceRect.height));
        }

        if (min.x == float.MaxValue || min.y == float.MaxValue)
            return new Rect(0f, 0f, 0f, 0f);

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private void FlipLayerPositionsVertically()
    {
        var docHeight = Mathf.Max(1f, cachedDocumentSize.y);
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;
            layer.position.y = docHeight - layer.position.y - layer.sourceRect.height;
        }
    }
}

#endif
