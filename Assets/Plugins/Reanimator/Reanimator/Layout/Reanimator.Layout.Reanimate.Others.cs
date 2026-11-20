#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private sealed class OtherSpriteDraft
    {
        public ReanimateOtherMode mode;
        public readonly List<Vector2> contour = new();
        public readonly List<Vector2> points = new();
        public readonly List<string> projectorSources = new();
        public bool projectorStretch;
        public string parentBone;
        public bool smoothRoot = true;
        public string rootSourceBone;
        public bool rootSourceExplicit;
    }

    private sealed class OtherSpriteOperation
    {
        public string key, name;
        public ReanimateOtherMode mode;
        public Vector2[] points;
        public Vector2[] contour;
        public int region;
        public string[] sources;
        public bool stretch;
        public bool inheritBoobs;
        public bool inheritBelly;
        public string parentBone;
        public bool smoothRoot = true;
        public string rootSourceBone;
    }

    private static readonly string[] ReanimateOtherModes = { "Skip", "SC", "SR", "SRN" };
    private static readonly HashSet<string> FacePartNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mouth", "nose", "eyes", "eyelids", "eyewhites", "irides", "iris"
    };
    private readonly Dictionary<string, Dictionary<string, List<OtherSpriteDraft>>> reanimateOtherDrafts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> reanimateOtherBoobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> reanimateOtherBelly = new(StringComparer.OrdinalIgnoreCase);
    private int reanimateOtherIndex;
    private int reanimateOtherStrokeControl;
    private Vector2 reanimateOtherStart, reanimateOtherEnd;
    private OtherSpriteDraft reanimateOtherDraft;
    private List<OtherSpriteDraft> reanimateOtherRegions;
    private readonly List<Vector2> reanimateOtherContourStroke = new();
    private bool reanimateDrawingOtherContour;
    private int reanimateOtherRegionIndex;
    private Vector2 reanimateProjectorScroll;

    private void NavigateReanimateStep(int direction)
    {
        reanimateDiagnosticLayer = null;
        if (reanimateStep == ReanimateShouldersStep)
        {
            int side = reanimateShoulderSide + direction;
            if (side >= 0 && side <= 1) { SetReanimateShoulderSide(side); return; }
        }
        if (reanimateStep == ReanimateOthersStep)
        {
            var next = reanimateOtherIndex + direction;
            if (next >= 0 && next < FindReanimateOtherSprites().Count)
            {
                LeaveReanimateOthers();
                reanimateOtherIndex = next;
                Repaint();
                return;
            }
        }
        var returnToLast = direction < 0 && reanimateStep == ReanimateFinalStep;
        var nextStep = direction < 0 && reanimateStep == ReanimateOthersStep
            ? ReanimateHipsStep
            : reanimateStep + direction;
        SetReanimateStep(nextStep);
        if (returnToLast)
            reanimateOtherIndex = Mathf.Max(0, FindReanimateOtherSprites().Count - 1);
    }

    private List<PsdLayerPreviewRenderer.LayerInfo> FindReanimateOtherSprites()
    {
        var others = new List<PsdLayerPreviewRenderer.LayerInfo>();
        if (layerPreviewRenderer == null)
            return others;
        var layers = layerPreviewRenderer.Layers;
        var assigned = new HashSet<PsdLayerPreviewRenderer.LayerInfo>();
        for (var step = ReanimateFaceStep; step < ReanimateOthersStep; step++)
            assigned.Add(FindReanimateStepSprite(layers, step));
        foreach (var layer in layers)
        {
            if (layer == null || !layer.visible || string.IsNullOrWhiteSpace(layer.name) ||
                layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f ||
                assigned.Contains(layer) || FacePartNames.Contains(NormalizeReanimateSpriteName(layer.name)))
                continue;
            others.Add(layer);
        }
        return others;
    }

    private static string NormalizeReanimateSpriteName(string name) =>
        name.Replace("_", "").Replace("-", "").Replace(" ", "");

    private static string ReanimateOtherSpriteKey(PsdLayerPreviewRenderer.LayerInfo layer) =>
        string.IsNullOrEmpty(layer.spriteGuid) ? $"{layer.layerId}:{layer.name}" : layer.spriteGuid;

    private List<OtherSpriteOperation> CaptureReanimateOthers(string path)
    {
        var result = new List<OtherSpriteOperation>();
        if (!reanimateOtherDrafts.TryGetValue(path, out var drafts))
            return result;
        foreach (var sprite in FindReanimateOtherSprites())
        {
            var key = ReanimateOtherSpriteKey(sprite);
            var inherit = reanimateOtherBoobs.TryGetValue(path, out var inherited) && inherited.Contains(key);
            var inheritBelly = reanimateOtherBelly.TryGetValue(path, out var bellies) && bellies.Contains(key);
            if (!drafts.TryGetValue(key, out var regions))
                regions = new List<OtherSpriteDraft>();
            if (inherit || inheritBelly)
                result.Add(new OtherSpriteOperation { key = key, name = sprite.name, mode = ReanimateOtherMode.Skip,
                    inheritBoobs = inherit, inheritBelly = inheritBelly });
            for (var region = 0; region < regions.Count; region++)
            {
                var draft = regions[region];
                if (draft.mode == ReanimateOtherMode.Skip)
                    continue;
                result.Add(new OtherSpriteOperation
                {
                    key = key,
                    name = sprite.name,
                    mode = draft.mode,
                    points = draft.points.ToArray(),
                    contour = draft.contour.ToArray(),
                    region = region,
                    sources = draft.projectorSources.ToArray(),
                    stretch = draft.projectorStretch,
                    parentBone = draft.parentBone,
                    smoothRoot = draft.smoothRoot,
                    rootSourceBone = ResolveReanimateSrnRootSource(draft)
                });
            }
        }
        return result;
    }

    private void ApplyReanimateOthers(List<OtherSpriteOperation> drafts, List<string> skinnedSprites,
        PsdLayerPreviewRenderer.ReanimateChestDraft chestDraft, Vector2[] bellyContour)
    {
        if (drafts.Count == 0)
            return;
        var sprites = new Dictionary<string, PsdLayerPreviewRenderer.LayerInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var sprite in layerPreviewRenderer.Layers)
            if (sprite != null && sprite.visible)
                sprites[ReanimateOtherSpriteKey(sprite)] = sprite;
        var operations = new Dictionary<string, List<OtherSpriteOperation>>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
        {
            if (!operations.TryGetValue(draft.key, out var regions))
            {
                regions = new List<OtherSpriteOperation>();
                operations.Add(draft.key, regions);
            }
            regions.Add(draft);
        }
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
            ApplySprite(draft.key);

        void ApplySprite(string key)
        {
            if (complete.Contains(key))
                return;
            var regions = operations[key];
            var name = regions[0].name;
            if (!inProgress.Add(key))
                throw new InvalidOperationException($"Others / {name}: Projector sources form a cycle.");
            if (!sprites.TryGetValue(key, out var layer))
            {
                foreach (var sprite in sprites.Values)
                    if (string.Equals(sprite.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        layer = sprite;
                        break;
                    }
            }
            if (layer == null)
                throw new InvalidOperationException($"Others: sprite '{name}' is missing after ALLIN.");
            if (regions.Exists(draft => draft.mode == ReanimateOtherMode.Projector) &&
                !layerPreviewRenderer.TryPrepareReanimateProjectorGeometry(layer, out var geometryError))
                throw new InvalidOperationException($"Others / {layer.name}: {geometryError}");
            foreach (var draft in regions)
            {
                if (draft.mode == ReanimateOtherMode.Skip)
                    continue;
                var context = $"Others / {layer.name} / Region {draft.region + 1}";
                if (draft.mode == ReanimateOtherMode.Projector)
                {
                    var sources = new List<PsdLayerPreviewRenderer.LayerInfo>();
                    foreach (var sourceKey in draft.sources)
                    {
                        if (operations.ContainsKey(sourceKey))
                            ApplySprite(sourceKey);
                        if (!sprites.TryGetValue(sourceKey, out var source))
                            throw new InvalidOperationException($"{context}: a Projector source is missing.");
                        sources.Add(source);
                    }
                    if (!layerPreviewRenderer.TryApplyReanimateProjectorRegion(layer, sources, draft.stretch,
                            draft.contour, out var error))
                        throw new InvalidOperationException($"{context}: {error}");
                }
                else if (!layerPreviewRenderer.TryApplyOtherSprite(layer, draft.mode, draft.points,
                             draft.contour, draft.region, out var error, draft.parentBone, draft.smoothRoot, draft.rootSourceBone))
                    throw new InvalidOperationException($"{context}: {error}");
            }
            var contours = new List<Vector2[]>();
            foreach (var draft in regions)
                if (draft.mode != ReanimateOtherMode.Skip)
                    contours.Add(draft.contour);
            layerPreviewRenderer.SoftenReanimateOtherGaps(layer, contours);
            if (regions.Exists(draft => draft.inheritBelly))
            {
                try
                {
                    layerPreviewRenderer.ApplyReanimateBelly(layer, bellyContour);
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException($"Others / {layer.name} / Inherit Belly: {error.Message}", error);
                }
            }
            if (regions.Exists(draft => draft.inheritBoobs) &&
                !layerPreviewRenderer.TryApplyFullBoobsWeights(chestDraft.LeftTarget, chestDraft.RightTarget,
                    chestDraft.LeftContour, chestDraft.RightContour, out _, out var boobsError, layer))
                throw new InvalidOperationException($"Others / {layer.name} / Inherit Boobs: {boobsError}");
            inProgress.Remove(key);
            complete.Add(key);
            skinnedSprites.Add(layer.name);
        }
    }

    private PsdLayerPreviewRenderer.LayerInfo DrawReanimateOthersSettings(string path)
    {
        var sprites = FindReanimateOtherSprites();
        EditorGUILayout.Space();
        if (sprites.Count == 0)
        {
            EditorGUILayout.HelpBox("No remaining sprites.", MessageType.Info);
            return null;
        }

        reanimateOtherIndex = Mathf.Clamp(reanimateOtherIndex, 0, sprites.Count - 1);
        var sprite = sprites[reanimateOtherIndex];
        if (!reanimateOtherDrafts.TryGetValue(path, out var drafts))
        {
            drafts = new Dictionary<string, List<OtherSpriteDraft>>(StringComparer.OrdinalIgnoreCase);
            reanimateOtherDrafts.Add(path, drafts);
        }
        var key = ReanimateOtherSpriteKey(sprite);
        if (!drafts.TryGetValue(key, out var regions))
        {
            regions = new List<OtherSpriteDraft>();
            drafts.Add(key, regions);
        }
        if (!ReferenceEquals(reanimateOtherRegions, regions))
        {
            reanimateOtherRegions = regions;
            reanimateOtherRegionIndex = 0;
        }

        EditorGUILayout.LabelField($"{reanimateOtherIndex + 1} / {sprites.Count}   {sprite.name}", EditorStyles.boldLabel);
        if (!reanimateOtherBoobs.TryGetValue(path, out var inherited))
        {
            inherited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            reanimateOtherBoobs.Add(path, inherited);
        }
        if (EditorGUILayout.ToggleLeft("Inherit Boobs (after all regions)", inherited.Contains(key)))
            inherited.Add(key);
        else
            inherited.Remove(key);
        if (!reanimateOtherBelly.TryGetValue(path, out var bellies))
        {
            bellies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            reanimateOtherBelly.Add(path, bellies);
        }
        if (EditorGUILayout.ToggleLeft("Inherit Belly (after all regions)", bellies.Contains(key)))
            bellies.Add(key);
        else
            bellies.Remove(key);
        DrawOtherRegionSettings(sprite, regions);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Previous", EditorStyles.miniButton))
                NavigateReanimateStep(-1);
            if (GUILayout.Button("Next", EditorStyles.miniButton))
                NavigateReanimateStep(1);
        }
        return sprite;
    }

    private void DrawOtherRegionSettings(PsdLayerPreviewRenderer.LayerInfo sprite, List<OtherSpriteDraft> regions)
    {
        if (GUILayout.Button("Add Region", EditorStyles.miniButton))
        {
            CancelReanimateOtherStroke();
            regions.Add(new OtherSpriteDraft { mode = ReanimateOtherMode.SC });
            reanimateOtherRegionIndex = regions.Count - 1;
            reanimateDrawingOtherContour = true;
        }
        if (regions.Count == 0)
        {
            reanimateOtherDraft = null;
            return;
        }
        var labels = new string[regions.Count];
        for (var i = 0; i < labels.Length; i++)
            labels[i] = $"Region {i + 1} · {regions[i].mode}";
        var region = EditorGUILayout.Popup(reanimateOtherRegionIndex, labels);
        if (region != reanimateOtherRegionIndex)
        {
            CancelReanimateOtherStroke();
            reanimateDrawingOtherContour = false;
            reanimateOtherRegionIndex = region;
        }
        var draft = regions[region];
        reanimateOtherDraft = draft;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(reanimateDrawingOtherContour ? "Cancel Outline" :
                    draft.contour.Count >= 3 ? "Redraw Outline" : "Draw Outline", EditorStyles.miniButton))
            {
                CancelReanimateOtherStroke();
                reanimateDrawingOtherContour = !reanimateDrawingOtherContour;
            }
            if (GUILayout.Button("Remove Region", EditorStyles.miniButton))
            {
                CancelReanimateOtherStroke();
                regions.RemoveAt(region);
                reanimateOtherRegionIndex = Mathf.Max(0, region - 1);
                reanimateDrawingOtherContour = false;
                reanimateOtherDraft = null;
                return;
            }
        }
        var selected = draft.mode == ReanimateOtherMode.Projector ? -1 : (int)draft.mode;
        var regular = GUILayout.Toolbar(selected, ReanimateOtherModes);
        var mode = regular >= 0 ? (ReanimateOtherMode)regular : draft.mode;
        if (GUILayout.Toggle(draft.mode == ReanimateOtherMode.Projector, "Projector", EditorStyles.miniButton) &&
            (draft.mode != ReanimateOtherMode.Projector || regular < 0))
            mode = ReanimateOtherMode.Projector;
        if (mode != draft.mode)
        {
            CancelReanimateOtherStroke();
            draft.mode = mode;
            Repaint();
        }
        if (mode is ReanimateOtherMode.SR or ReanimateOtherMode.SRN)
        {
            DrawReanimateOtherParent(draft);
            if (mode == ReanimateOtherMode.SRN)
                DrawReanimateSrnRootSettings(draft);
            EditorGUILayout.HelpBox(mode == ReanimateOtherMode.SR
                ? "Drag from the pivot to the tip."
                : "Drag the first bone. Then drag outward from each tip to extend the chain.", MessageType.Info);
            using (new EditorGUI.DisabledScope(draft.points.Count == 0))
                if (GUILayout.Button("Clear Bones", EditorStyles.miniButton))
                {
                    CancelReanimateOtherStroke();
                    draft.points.Clear();
                    Repaint();
                }
        }
        if (mode == ReanimateOtherMode.Projector)
            DrawReanimateProjectorSettings(sprite, draft);
    }

    private void DrawReanimateOtherParent(OtherSpriteDraft draft)
    {
        var options = new List<string> { "Auto (nearest bone)" };
        var skeleton = ReanimatorSkeletronModule.CapturePlacement(Api);
        if (skeleton != null)
            foreach (var bone in skeleton)
                options.Add(bone.name);

        var selected = string.IsNullOrEmpty(draft.parentBone) ? 0 : options.IndexOf(draft.parentBone);
        if (selected < 0)
        {
            selected = options.Count;
            options.Add(draft.parentBone + " (missing)");
        }
        var next = EditorGUILayout.Popup("Parent Bone", selected, options.ToArray());
        if (next != selected)
            draft.parentBone = next == 0 ? null : options[next];
    }

    private void DrawReanimateSrnRootSettings(OtherSpriteDraft draft)
    {
        draft.smoothRoot = EditorGUILayout.ToggleLeft("Smooth first SRN bone", draft.smoothRoot);
        using (new EditorGUI.DisabledScope(!draft.smoothRoot))
        {
            var options = new List<string>{"Parent Bone"};
            var skeleton = ReanimatorSkeletronModule.CapturePlacement(Api);
            if (skeleton != null)
                foreach (var bone in skeleton)
                    if (!options.Contains(bone.name)) options.Add(bone.name);
            var source = ResolveReanimateSrnRootSource(draft);
            var selected = string.IsNullOrEmpty(source)?0:options.IndexOf(source);
            if (selected < 0) { selected = options.Count; options.Add(source); }
            var next = EditorGUILayout.Popup("Blend From Bone", selected, options.ToArray());
            if(next!=selected)
            {draft.rootSourceExplicit=next>0;draft.rootSourceBone=next>0?options[next]:null;}
        }
    }

    private static string ResolveReanimateSrnRootSource(OtherSpriteDraft draft)
    {
        // Old drafts always stored hips even when a different parent was selected.
        // Keep intentional overrides; otherwise the blend follows the chosen parent.
        return draft.rootSourceExplicit || !string.IsNullOrEmpty(draft.rootSourceBone) &&
            !string.Equals(draft.rootSourceBone,"hips",StringComparison.OrdinalIgnoreCase)?draft.rootSourceBone:null;
    }

    private void DrawReanimateProjectorSettings(PsdLayerPreviewRenderer.LayerInfo target, OtherSpriteDraft draft)
    {
        draft.projectorStretch = EditorGUILayout.ToggleLeft("Stretch (APL)", draft.projectorStretch);
        EditorGUILayout.LabelField("Projector Sources", EditorStyles.miniBoldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(reanimateProjectorScroll, GUILayout.Height(130f)))
        {
            reanimateProjectorScroll = scroll.scrollPosition;
            foreach (var layer in layerPreviewRenderer.Layers)
            {
                if (layer == null || layer == target || !layer.visible ||
                    layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
                    continue;
                var key = ReanimateOtherSpriteKey(layer);
                var selected = draft.projectorSources.Contains(key);
                var next = EditorGUILayout.ToggleLeft(layer.name, selected);
                if (next && !selected)
                    draft.projectorSources.Add(key);
                else if (!next && selected)
                    draft.projectorSources.Remove(key);
            }
        }
    }

    private void LeaveReanimateOthers()
    {
        CancelReanimateOtherStroke();
        reanimateOtherDraft = null;
        reanimateOtherRegions = null;
        reanimateDrawingOtherContour = false;
    }

    private void CancelReanimateOtherStroke()
    {
        if (reanimateOtherStrokeControl != 0 && GUIUtility.hotControl == reanimateOtherStrokeControl)
            GUIUtility.hotControl = 0;
        reanimateOtherStrokeControl = 0;
        reanimateOtherContourStroke.Clear();
    }

    private void DrawOrHandleReanimateOtherCanvas(Rect viewport, PsdCoordinateSpace coordinates)
    {
        var draft = reanimateOtherDraft;
        if (draft == null)
            return;
        var canDrawBone = draft.contour.Count >= 3 && draft.mode is (ReanimateOtherMode.SR or ReanimateOtherMode.SRN);
        var input = Event.current;
        if (input.type == EventType.Repaint)
        {
            Handles.BeginGUI();
            foreach (var region in reanimateOtherRegions)
                PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView, region.contour,
                    region == draft ? Color.cyan : new Color(0.75f, 0.85f, 0.9f, 0.55f), true);
            PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView, reanimateOtherContourStroke, Color.cyan, false);
            using (new Handles.DrawingScope(Color.cyan))
            {
                var count = draft.mode == ReanimateOtherMode.SR ? Mathf.Min(2, draft.points.Count) : draft.points.Count;
                if (canDrawBone)
                {
                    for (var i = 1; i < count; i++)
                        DrawBone(draft.points[i - 1], draft.points[i]);
                    if (!reanimateDrawingOtherContour && reanimateOtherStrokeControl != 0)
                        DrawBone(reanimateOtherStart, reanimateOtherEnd);
                }
            }
            Handles.EndGUI();
            return;
        }
        if (!reanimateDrawingOtherContour && !canDrawBone)
            return;
        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape && reanimateOtherStrokeControl != 0)
            CancelReanimateOtherStroke();
        else if (input.type == EventType.MouseDown && input.button == 0 && !input.alt &&
                 viewport.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            reanimateOtherStrokeControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = reanimateOtherStrokeControl;
            reanimateOtherEnd = coordinates.ViewToDocument(input.mousePosition);
            if (reanimateDrawingOtherContour)
                reanimateOtherContourStroke.Add(reanimateOtherEnd);
            reanimateOtherStart = draft.mode == ReanimateOtherMode.SRN && draft.points.Count > 0
                ? draft.points[draft.points.Count - 1] : reanimateOtherEnd;
        }
        else if (reanimateOtherStrokeControl != 0 && GUIUtility.hotControl == reanimateOtherStrokeControl &&
                 (input.type == EventType.MouseDrag || (input.type == EventType.MouseUp && input.button == 0)))
        {
            reanimateOtherEnd = coordinates.ViewToDocument(input.mousePosition);
            if (reanimateDrawingOtherContour && (reanimateOtherContourStroke.Count == 0 ||
                (reanimateOtherEnd - reanimateOtherContourStroke[reanimateOtherContourStroke.Count - 1]).sqrMagnitude >= 0.25f))
                reanimateOtherContourStroke.Add(reanimateOtherEnd);
            if (input.type == EventType.MouseUp)
            {
                if (reanimateDrawingOtherContour)
                {
                    var contour = PsdLayerPreviewRenderer.BuildSmoothedClosedBoobV2Contour(reanimateOtherContourStroke, 2, 32);
                    if (contour.Count >= 3)
                    {
                        draft.contour.Clear();
                        draft.contour.AddRange(contour);
                        reanimateDrawingOtherContour = false;
                    }
                }
                else if ((reanimateOtherEnd - reanimateOtherStart).sqrMagnitude >= 1f)
                {
                    if (draft.mode == ReanimateOtherMode.SR)
                        draft.points.Clear();
                    if (draft.points.Count == 0)
                        draft.points.Add(reanimateOtherStart);
                    draft.points.Add(reanimateOtherEnd);
                }
                CancelReanimateOtherStroke();
            }
        }
        else
            return;
        input.Use();
        Repaint();

        void DrawBone(Vector2 a, Vector2 b)
        {
            var start = coordinates.DocumentToView(a);
            var end = coordinates.DocumentToView(b);
            Handles.DrawAAPolyLine(4f, start, end);
            Handles.DrawSolidDisc(start, Vector3.forward, 4f);
            Handles.DrawSolidDisc(end, Vector3.forward, 4f);
        }
    }
}
#endif
