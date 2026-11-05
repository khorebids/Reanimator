#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed class ReanimatorProjectorCandidateState
{
    private readonly Dictionary<string, bool> enabledByKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> currentCandidateKeys = new List<string>();

    public string CurrentTargetKey { get; private set; } = string.Empty;
    public string HoveredCandidateKey { get; private set; } = string.Empty;
    public int CandidateCount => currentCandidateKeys.Count;

    public void Reset()
    {
        CurrentTargetKey = string.Empty;
        HoveredCandidateKey = string.Empty;
        enabledByKey.Clear();
        currentCandidateKeys.Clear();
    }

    public bool ResetForTargetIfChanged(string targetKey)
    {
        targetKey ??= string.Empty;
        if (string.Equals(CurrentTargetKey, targetKey, StringComparison.OrdinalIgnoreCase))
            return false;

        CurrentTargetKey = targetKey;
        HoveredCandidateKey = string.Empty;
        enabledByKey.Clear();
        currentCandidateKeys.Clear();
        return true;
    }

    public void BeginCandidateFrame()
    {
        currentCandidateKeys.Clear();
    }

    public bool TrackCandidate(string key, bool defaultEnabled)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        currentCandidateKeys.Add(key);
        if (!enabledByKey.ContainsKey(key))
            enabledByKey[key] = defaultEnabled;
        return true;
    }

    public bool IsEnabled(string key)
    {
        return !string.IsNullOrEmpty(key) &&
               enabledByKey.TryGetValue(key, out var enabled) &&
               enabled;
    }

    public void SetEnabled(string key, bool enabled)
    {
        if (!string.IsNullOrEmpty(key))
            enabledByKey[key] = enabled;
    }

    public void SetAllEnabled(bool enabled)
    {
        for (var i = 0; i < currentCandidateKeys.Count; i++)
        {
            var key = currentCandidateKeys[i];
            if (!string.IsNullOrEmpty(key))
                enabledByKey[key] = enabled;
        }
    }

    public string[] BuildSelectedKeys()
    {
        var result = new List<string>();
        for (var i = 0; i < currentCandidateKeys.Count; i++)
        {
            var key = currentCandidateKeys[i];
            if (IsEnabled(key))
                result.Add(key);
        }

        return result.ToArray();
    }

    public bool SetHoveredKey(string key)
    {
        key ??= string.Empty;
        if (string.Equals(HoveredCandidateKey, key, StringComparison.OrdinalIgnoreCase))
            return false;

        HoveredCandidateKey = key;
        return true;
    }
}

internal enum ReanimatorProjectorMode
{
    Weights,
    Stretch
}

internal enum ReanimatorProjectorStretchMode
{
    Apl,
    In,
    Combo
}

internal sealed class ReanimatorProjectorSettings
{
    public ReanimatorProjectorMode Mode = ReanimatorProjectorMode.Weights;
    public ReanimatorProjectorStretchMode StretchMode = ReanimatorProjectorStretchMode.Combo;
    public string TargetSelectionKey = string.Empty;
    public bool ClearTargetWeights = true;
    public bool PostProcessExpandedCoverage = true;
    public bool SmoothPostProcessWeights = true;
    public bool FilterWeightTargetsToPreview;
    public bool StretchKeepContour;
    public bool StretchUseCull;
    public float StretchCullDistance = 4f;

    public bool SourceInsideTargetOnly => StretchMode == ReanimatorProjectorStretchMode.In;
    public bool UseAplStretch => StretchMode == ReanimatorProjectorStretchMode.Apl;
}

internal static class ReanimatorProjectorUiAlgorithms
{
    public static bool IsCandidateRowHovered(Rect rowRect, Event e)
    {
        if (!CanUpdateCandidateHoverFromEvent(e))
            return false;

        return rowRect.width > 0f &&
               rowRect.height > 0f &&
               rowRect.Contains(e.mousePosition);
    }

    public static bool CanUpdateCandidateHoverFromEvent(Event e)
    {
        if (e == null)
            return false;
        if (e.type != EventType.Repaint &&
            e.type != EventType.MouseMove &&
            e.type != EventType.MouseDrag &&
            e.type != EventType.MouseDown &&
            e.type != EventType.MouseUp)
        {
            return false;
        }

        return true;
    }

    public static PsdLayerPreviewRenderer.ProjectorCandidateInfo[] FilterCandidatesToPreview(
        PsdLayerPreviewRenderer.ProjectorCandidateInfo[] candidates,
        ReanimatorPreviewContext context)
    {
        if (candidates == null || candidates.Length == 0 || context == null || context.Coordinates == null)
            return candidates;

        var visible = new List<PsdLayerPreviewRenderer.ProjectorCandidateInfo>(candidates.Length);
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate.Layer == null)
            {
                visible.Add(candidate);
                continue;
            }

            if (IsCandidateViewRectVisible(context.LocalRect, context.Coordinates.LayerToViewRect(candidate.Layer)))
                visible.Add(candidate);
        }

        return visible.Count == candidates.Length ? candidates : visible.ToArray();
    }

    public static bool IsCandidateViewRectVisible(Rect previewRect, Rect candidateRect, float padding = 0f)
    {
        if (previewRect.width <= 0f || previewRect.height <= 0f)
            return true;

        if (candidateRect.width < 0f)
        {
            candidateRect.x += candidateRect.width;
            candidateRect.width = -candidateRect.width;
        }
        if (candidateRect.height < 0f)
        {
            candidateRect.y += candidateRect.height;
            candidateRect.height = -candidateRect.height;
        }
        if (candidateRect.width <= 0f || candidateRect.height <= 0f)
            return false;

        var paddedPreview = new Rect(
            previewRect.xMin - padding,
            previewRect.yMin - padding,
            previewRect.width + padding * 2f,
            previewRect.height + padding * 2f);
        return candidateRect.Overlaps(paddedPreview, true);
    }
}

internal static class ReanimatorProjectorOperationRunner
{
    public static void Apply(Reanimator.SystemApi api, ReanimatorProjectorSettings settings, string[] sourceKeys)
    {
        if (api == null || settings == null)
            return;

        sourceKeys ??= Array.Empty<string>();
        var applyError = string.Empty;
        if (string.IsNullOrWhiteSpace(settings.TargetSelectionKey))
        {
            if (settings.Mode != ReanimatorProjectorMode.Stretch || !settings.UseAplStretch)
                api.ShowNotification("Set Projector target first");
            api.Repaint();
            return;
        }

        if (settings.Mode == ReanimatorProjectorMode.Weights &&
            api.ApplyProjectorWeights(
                settings.TargetSelectionKey,
                sourceKeys,
                settings.ClearTargetWeights,
                settings.PostProcessExpandedCoverage,
                settings.SmoothPostProcessWeights,
                out var weightedCount,
                out _,
                out applyError))
        {
            api.ShowNotification($"Projector Weights: {weightedCount} vertices");
        }
        else if (settings.Mode == ReanimatorProjectorMode.Stretch)
        {
            var applied = api.ApplyProjectorStretch(
                settings.TargetSelectionKey,
                sourceKeys,
                settings.UseAplStretch,
                settings.SourceInsideTargetOnly,
                settings.StretchKeepContour && !settings.UseAplStretch,
                settings.StretchUseCull,
                settings.StretchCullDistance,
                out var insertedCount,
                out var removedCount,
                out _,
                out applyError);

            // APL is an action, not a diagnostic surface. It applies topology
            // and weights silently; an idempotent repeat is silent as well.
            if (!settings.UseAplStretch)
            {
                api.ShowNotification(applied
                    ? $"Projector Stretch: +{insertedCount} / -{removedCount} vertices"
                    : string.IsNullOrWhiteSpace(applyError)
                        ? "Projector: no applicable changes."
                        : applyError);
            }
        }
        else
        {
            api.ShowNotification(string.IsNullOrWhiteSpace(applyError) ? "Projector: no applicable changes." : applyError);
        }

        api.Repaint();
    }
}
#endif
