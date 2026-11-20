#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public partial class PsdLayerPreviewRenderer
{
    private const string ShoulderDefaultsPath = "ProjectSettings/ReanimatorShoulderSettings.json";
    [Serializable] private sealed class ShoulderDefaults { public AnimationCurve transition, helper; public float helperLength, shift; }
    [Serializable] private sealed class ShoulderDraft { public string bone; public Vector2[] normalizedContour; }
    [Serializable] private sealed class ShoulderDraftFile { public List<ShoulderDraft> arms = new(); }
    internal void SaveShoulderDefaults() => File.WriteAllText(ShoulderDefaultsPath, JsonUtility.ToJson(new ShoulderDefaults {
        transition = ShoulderTransitionCurve, helper = ShoulderContourHelperCurve, helperLength = ShoulderFanDepth, shift = ShoulderFanShiftWidth }, true));
    private void LoadShoulderDefaults(bool force = false)
    {
        if (!File.Exists(ShoulderDefaultsPath)) return;
        ShoulderDefaults settings;
        try { settings = JsonUtility.FromJson<ShoulderDefaults>(File.ReadAllText(ShoulderDefaultsPath)); }
        catch (Exception error) when (error is ArgumentException || error is IOException) { return; }
        if (settings == null) return;
        bool fresh = shoulderTransitionCurve == null && shoulderContourHelperCurve == null;
        if (force || shoulderTransitionCurve == null) shoulderTransitionCurve = settings.transition;
        if (force || shoulderContourHelperCurve == null) shoulderContourHelperCurve = settings.helper;
        if (force || fresh) { ShoulderFanDepth = Mathf.Clamp(settings.helperLength, .02f, .4f); ShoulderFanShiftWidth = Mathf.Max(0, settings.shift); }
    }
    private string ShoulderDraftPath()
    {
        var guid = AssetDatabase.AssetPathToGUID(cachedPath);
        return string.IsNullOrEmpty(guid) ? null : Path.Combine("Library", "Reanimator", "ShoulderContours", guid + ".json");
    }
    private ShoulderDraftFile ReadShoulderDrafts()
    {
        var path = ShoulderDraftPath();
        ShoulderDraftFile file = null;
        try { if (path != null && File.Exists(path)) file = JsonUtility.FromJson<ShoulderDraftFile>(File.ReadAllText(path)); }
        catch (Exception error) when (error is ArgumentException || error is IOException) { }
        file ??= new ShoulderDraftFile(); file.arms ??= new List<ShoulderDraft>(); return file;
    }
    internal Vector2[] GetShoulderContour(string bone)
    {
        var record = ReadShoulderDrafts().arms.Find(item => item.bone == bone);
        return record?.normalizedContour == null ? Array.Empty<Vector2>() : Array.ConvertAll(record.normalizedContour,
            point => new Vector2(point.x * cachedDocumentSize.x, point.y * cachedDocumentSize.y));
    }
    internal void SetShoulderContour(string bone, IReadOnlyList<Vector2> contour)
    {
        var path = ShoulderDraftPath(); if (path == null || cachedDocumentSize.x <= 0 || cachedDocumentSize.y <= 0) return;
        var file = ReadShoulderDrafts();
        var normalized = contour != null && contour.Count >= 3 ? contour.Select(point =>
            new Vector2(point.x / cachedDocumentSize.x, point.y / cachedDocumentSize.y)).ToArray() : null;
        var previous = file.arms.Find(item => item.bone == bone);
        if (normalized != null && previous?.normalizedContour != null && previous.normalizedContour.Length == normalized.Length &&
            previous.normalizedContour.Zip(normalized, (a, b) => (a - b).sqrMagnitude <= 1e-12f).All(equal => equal)) return;
        file.arms.RemoveAll(item => item.bone == bone);
        if (normalized != null) file.arms.Add(new ShoulderDraft { bone = bone, normalizedContour = normalized });
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(file, true));
    }
    internal void SaveCurrentShoulderContour()
    {
        var layer = FindShoulderSprite(shoulderWeldBone);
        if (layer != null && shoulderWeldContour.Count >= 3 && BuildSelectionKey(layer) == shoulderWeldLayer)
            SetShoulderContour(shoulderWeldBone, shoulderWeldContour);
    }
    internal LayerInfo FindShoulderSprite(string bone)
    {
        if (string.IsNullOrEmpty(bone)) return null;
        var key = NormalizeAutoWeightName(bone);
        var names = key == "larm" ? new[] { "larm", "leftarm", "arml", "armleft" } :
            key == "rarm" ? new[] { "rarm", "rightarm", "armr", "armright" } : new[] { key };
        return layerInfos.Find(layer => layer != null && layer.visible && names.Contains(NormalizeAutoWeightName(layer.name)) &&
            layer.sourceRect.width > 0 && layer.sourceRect.height > 0);
    }
    internal string GetShoulderDraftError()
    {
        foreach (var bone in new[] { "l_arm", "r_arm" })
            if (FindShoulderSprite(bone) != null && GetShoulderContour(bone).Length < 3)
                return $"Draw the {bone} shoulder weld contour on Reanimate → Shoulders (after Full PSD).";
        return null;
    }
    internal string ApplyAllShoulderContours()
    {
        var error = GetShoulderDraftError(); if (error != null) throw new InvalidOperationException(error);
        LoadShoulderDefaults(true);
        int selected = selectedBoneIndex; string selection = selectedLayerSelectionKey;
        var outline = shoulderWeldContour; string outlineBone = shoulderWeldBone, outlineLayer = shoulderWeldLayer;
        var results = new List<string>();
        try
        {
            foreach (var bone in new[] { "l_arm", "r_arm" })
            {
                var layer = FindShoulderSprite(bone); if (layer == null) continue;
                if (!TryFindBoneIndexByName(bone, out selectedBoneIndex))
                { results.Add(bone + ": shoulder omitted (arm bone is missing)."); continue; }
                SelectLayer(layer); shoulderWeldContour = new List<Vector2>(GetShoulderContour(bone));
                shoulderWeldBone = bone; shoulderWeldLayer = selectedLayerSelectionKey;
                bool applied = TryApplyAutomatedShoulder(out var message);
                results.Add(bone + (applied ? ": " : ": shoulder omitted: ") + message);
            }
            return results.Count == 0 ? "Complete: no arm sprites in this skeleton." : string.Join("; ", results);
        }
        finally { selectedBoneIndex = selected; selectedLayerSelectionKey = selection;
            shoulderWeldContour = outline; shoulderWeldBone = outlineBone; shoulderWeldLayer = outlineLayer; }
    }

    private bool TryApplyAutomatedShoulder(out string message)
    {
        // Shoulder construction is optional in automation. Restore the complete
        // pre-shoulder working state on every unsuccessful exit, including an
        // exception after mesh commit, without undoing the preceding shoulder.
        var state = CaptureApplyFailureWorkingState();
        LoadPreviewBoneRigs();
        string rigs = JsonUtility.ToJson(previewBoneRigs);
        var stamps = shoulderFanStamps?.Select(item => new ShoulderFanStamp { key = item.key, signature = item.signature }).ToList();
        string path = PreviewBoneRigFilePath();
        string savedRigs = path != null && File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            if (ApplyShoulderFan(out message)) return true;
        }
        catch (Exception error) { message = error.Message; }
        RestoreApplyFailureWorkingState(state);
        previewBoneRigs = JsonUtility.FromJson<PreviewBoneRigFile>(rigs);
        shoulderFanStamps = stamps;
        if (path != null)
        {
            if (savedRigs == null) { if (File.Exists(path)) File.Delete(path); }
            else if (!File.Exists(path) || File.ReadAllText(path) != savedRigs) File.WriteAllText(path, savedRigs);
        }
        return false;
    }
}
#endif
