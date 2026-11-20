#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter
{
    [SerializeField] private bool wholeBody = true;
    [SerializeField] private PreviewNavigator bodyNavigator = new PreviewNavigator();
    private sealed class BodySurface
    {
        public Sprite sprite;
        public ReaniIKSurfaceMaps.Entry entry;
        public Texture2D image, overlay;
        public Color[] mask, opacity;
        public Rect rect;
        public int order, width, height, alphaWidth, alphaHeight;
        public bool dirty;
    }
    private readonly List<BodySurface> bodySurfaces = new List<BodySurface>();
    private readonly HashSet<BodySurface> strokeSurfaces = new HashSet<BodySurface>();
    private PsdLayerPreviewRenderer bodyLayout;
    private Vector2 bodyDocumentSize;
    private Vector2 lastBrushPoint;
    private int bodyControl, undoGroup;
    private bool bodyPainting;
    private int bodyPaintButton;
    private BodySurface hiddenBodySurface;
    public int BodySpriteCount => bodySurfaces.Count;

    private void PrepareBodyLayout(PsdLayerPreviewRenderer preview)
    {
        string asset = rig ? AssetDatabase.GetAssetPath(sprites[0]) : sourcePath;
        if (preview != null && preview.IsPreviewCacheReady(asset)) bodyLayout = preview;
        else { bodyLayout = new PsdLayerPreviewRenderer(); bodyLayout.RequestPreviewCacheRefresh(asset); }
        bodyNavigator.Reset();
    }
    private bool EnsureBodyCanvas()
    {
        if (bodySurfaces.Count > 0) return true;
        if (bodyLayout == null) PrepareBodyLayout(null);
        if (bodyLayout.IsPreviewCacheRefreshInProgress)
        {
            bodyLayout.TickPreviewCacheRefresh(.02);
            Repaint(); return false;
        }
        bodyDocumentSize = bodyLayout.DocumentSize;
        if (bodyDocumentSize.x <= 0 || bodyDocumentSize.y <= 0) return false;
        foreach (var sprite in sprites)
        {
            var layer = bodyLayout.Layers.FirstOrDefault(l => l.visible && l.name == sprite.name && l.sourceRect.width > 0 && l.sourceRect.height > 0);
            if (layer == null) continue;
            var data = ReaniIKSurfaceAuthoring.EnsureEntry(maps, sprite);
            var image = ReaniIKSurfaceAuthoring.Extract(sprite);
            var s = new BodySurface { sprite = sprite, entry = data, image = image,
                rect = new Rect(layer.position, layer.sourceRect.size), order = -layer.depth,
                mask = data.normalAndBlush.GetPixels(), width = data.normalAndBlush.width, height = data.normalAndBlush.height,
                opacity = image.GetPixels(), alphaWidth = image.width, alphaHeight = image.height };
            s.overlay = new Texture2D(s.width, s.height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            UpdateBodyOverlay(s); bodySurfaces.Add(s);
        }
        bodySurfaces.Sort((a, b) => a.order.CompareTo(b.order));
        return bodySurfaces.Count > 0;
    }
    private static float Opacity(BodySurface s, Vector2 doc)
    {
        if (!s.rect.Contains(doc)) return 0;
        int x = Mathf.Clamp((int)((doc.x - s.rect.x) / s.rect.width * s.alphaWidth), 0, s.alphaWidth - 1);
        int y = Mathf.Clamp((int)((doc.y - s.rect.y) / s.rect.height * s.alphaHeight), 0, s.alphaHeight - 1);
        return s.opacity[y * s.alphaWidth + x].a;
    }
    private BodySurface PickBodySurface(Vector2 doc)
    {
        for (int i = bodySurfaces.Count - 1; i >= 0; i--)
            if (bodySurfaces[i].entry.effectsEnabled && bodySurfaces[i] != hiddenBodySurface && Opacity(bodySurfaces[i], doc) > .1f) return bodySurfaces[i];
        return null;
    }
    private void ToggleBodyOccluder(Vector2 doc)
    {
        hiddenBodySurface = hiddenBodySurface != null ? null : PickBodySurface(doc);
        brushResizing = false;
        Repaint();
    }
    public Sprite BodySpriteAt(Vector2 documentPoint) => PickBodySurface(documentPoint)?.sprite;
    private Vector2 BodyToView(Rect area, Vector2 doc)
    {
        float fit = Mathf.Min(area.width / bodyDocumentSize.x, area.height / bodyDocumentSize.y) * .94f;
        var p = area.center + new Vector2(doc.x - bodyDocumentSize.x * .5f, bodyDocumentSize.y * .5f - doc.y) * fit;
        return bodyNavigator.ToView(area, p);
    }
    private Vector2 ViewToBody(Rect area, Vector2 mouse)
    {
        float fit = Mathf.Min(area.width / bodyDocumentSize.x, area.height / bodyDocumentSize.y) * .94f;
        Vector2 p = (bodyNavigator.FromView(area, mouse) - area.center) / fit;
        return new Vector2(p.x + bodyDocumentSize.x * .5f, bodyDocumentSize.y * .5f - p.y);
    }
    private void DrawBodyCanvas(float width)
    {
        EditorGUILayout.LabelField("Wheel: zoom · MMB / Alt+LMB: pan · F: fit · visible sprites picked automatically", EditorStyles.miniLabel);
        if (!EnsureBodyCanvas())
        {
            EditorGUILayout.HelpBox("Loading whole-body canvas… " + bodyLayout?.PreviewCacheRefreshStatus, MessageType.Info); return;
        }
        Rect rect = GUILayoutUtility.GetRect(100, Mathf.Max(100, width), 120, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.BeginClip(rect);
        try
        {
            var area = new Rect(0, 0, rect.width, rect.height);
            EditorGUI.DrawRect(area, new Color(.16f, .17f, .18f));
            var e = Event.current;
            bool visibilityClick = e.type == EventType.MouseDown && e.button == 1 && e.shift && !e.alt && !bodyPainting && area.Contains(e.mousePosition);
            if (visibilityClick) { ToggleBodyOccluder(ViewToBody(area, e.mousePosition)); e.Use(); }
            float radiusScale = Vector2.Distance(BodyToView(area, Vector2.zero), BodyToView(area, Vector2.up * bodyDocumentSize.y));
            bool resized = visibilityClick || HandleBrushRadius(e, area, radiusScale);
            if (resized && bodyPainting) lastBrushPoint = ViewToBody(area, e.mousePosition);
            if (!resized && !bodyPainting && bodyNavigator.HandleInput(area)) Repaint();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F && area.Contains(e.mousePosition)) { bodyNavigator.Reset(); e.Use(); Repaint(); }
            foreach (var s in bodySurfaces)
            {
                if (!s.entry.effectsEnabled || s == hiddenBodySurface) continue;
                Vector2 min = BodyToView(area, new Vector2(s.rect.xMin, s.rect.yMax)), max = BodyToView(area, new Vector2(s.rect.xMax, s.rect.yMin));
                var r = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                GUI.DrawTexture(r, s.overlay, ScaleMode.StretchToFill, true);
            }
            Vector2 doc = ViewToBody(area, e.mousePosition);
            var hovered = area.Contains(e.mousePosition) ? PickBodySurface(doc) : null;
            float brush = radius * bodyDocumentSize.y;
            if (area.Contains(e.mousePosition))
            {
                float viewRadius = Vector2.Distance(BodyToView(area, doc), BodyToView(area, doc + Vector2.right * brush));
                Handles.BeginGUI(); Handles.color = new Color(.65f, 1, .45f);
                Handles.DrawWireDisc(e.mousePosition, Vector3.forward, viewRadius); Handles.EndGUI();
                string label = hovered == null ? "No visible sprite" : "Brush → " + hovered.sprite.name;
                if (hiddenBodySurface != null) label += " · Hidden: " + hiddenBodySurface.sprite.name + " (Shift+RMB restores)";
                GUI.Label(new Rect(10, 8, area.width - 20, 22), label, EditorStyles.whiteLabel);
            }
            int control = GUIUtility.GetControlID("ReanimatorWholeBodyMasks".GetHashCode(), FocusType.Passive);
            if (StartsBrush(e) && area.Contains(e.mousePosition) && hovered != null)
            {
                bodyPainting = true; bodyPaintButton = e.button; bodyControl = control; GUIUtility.hotControl = control;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Paint whole-body surface masks"); strokeSurfaces.Clear();
                lastBrushPoint = doc; StrokeBody(doc, brush, bodyPaintButton == 1); e.Use();
            }
            else if (bodyPainting && e.type == EventType.MouseDrag && !e.shift)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(lastBrushPoint, doc) / Mathf.Max(brush * .25f, 1)));
                for (int i = 1; i <= steps; i++) StrokeBody(Vector2.Lerp(lastBrushPoint, doc, (float)i / steps), brush, bodyPaintButton == 1);
                lastBrushPoint = doc; e.Use();
            }
            else if (bodyPainting && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp || e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
            {
                bodyPainting = false; if (GUIUtility.hotControl == bodyControl) GUIUtility.hotControl = 0;
                SaveBodyCanvas(); Undo.CollapseUndoOperations(undoGroup); e.Use();
            }
            if (e.type == EventType.ContextClick && area.Contains(e.mousePosition)) e.Use();
            if (e.type == EventType.MouseMove || bodyPainting) Repaint();
        }
        finally { GUI.EndClip(); }
    }
    private void StrokeBody(Vector2 center, float brush, bool clear)
    {
        var brushRect = new Rect(center - Vector2.one * brush, Vector2.one * brush * 2);
        foreach (var s in bodySurfaces)
        {
            if (!s.entry.effectsEnabled) continue;
            if (s == hiddenBodySurface) continue;
            if (!s.rect.Overlaps(brushRect)) continue;
            int left = Mathf.Clamp(Mathf.FloorToInt((brushRect.xMin - s.rect.x) / s.rect.width * s.width), 0, s.width - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt((brushRect.xMax - s.rect.x) / s.rect.width * s.width), 0, s.width - 1);
            int bottom = Mathf.Clamp(Mathf.FloorToInt((brushRect.yMin - s.rect.y) / s.rect.height * s.height), 0, s.height - 1);
            int top = Mathf.Clamp(Mathf.CeilToInt((brushRect.yMax - s.rect.y) / s.rect.height * s.height), 0, s.height - 1);
            bool changed = false;
            for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
            {
                Vector2 doc = s.rect.position + new Vector2((x + .5f) / s.width * s.rect.width, (y + .5f) / s.height * s.rect.height);
                float distance = Vector2.Distance(doc, center) / brush;
                if (distance >= 1 || PickBodySurface(doc) != s) continue;
                int k = y * s.width + x;
                float alpha = Mathf.Lerp(s.mask[k].a, clear ? 0 : 1, strength * (1 - distance) * Opacity(s, doc));
                if (Mathf.Abs(alpha - s.mask[k].a) < .00001f) continue;
                if (!strokeSurfaces.Contains(s)) { Undo.RegisterCompleteObjectUndo(s.entry.normalAndBlush, "Paint whole-body surface mask"); strokeSurfaces.Add(s); }
                s.mask[k].a = alpha; changed = true;
            }
            if (!changed) continue;
            s.dirty = true; s.entry.normalAndBlush.SetPixels(s.mask); s.entry.normalAndBlush.Apply(false);
            if (s.entry == entry) { pixels = s.mask; UpdateOverlay(); }
            UpdateBodyOverlay(s);
        }
        EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); Repaint();
    }
    private void UpdateBodyOverlay(BodySurface s)
    {
        var colors = new Color[s.mask.Length];
        Color tint = rig ? rig.surfaceEffects.rednessColor : new Color(.95f, .12f, .15f, 1);
        for (int y = 0; y < s.height; y++) for (int x = 0; x < s.width; x++)
        {
            int k = y * s.width + x;
            int sourceX = Mathf.Clamp((int)((x + .5f) / s.width * s.alphaWidth), 0, s.alphaWidth - 1);
            int sourceY = Mathf.Clamp((int)((y + .5f) / s.height * s.alphaHeight), 0, s.alphaHeight - 1);
            var source = s.opacity[sourceY * s.alphaWidth + sourceX];
            colors[k] = s.entry.effectsEnabled ? SurfacePreviewColor(source, s.mask[k], tint) : source;
        }
        s.overlay.SetPixels(colors); s.overlay.Apply(false);
    }
    private void SaveBodyCanvas()
    {
        foreach (var s in bodySurfaces)
        {
            if (!s.dirty || !s.entry.normalAndBlush) continue;
            System.IO.File.WriteAllBytes(AssetDatabase.GetAssetPath(s.entry.normalAndBlush), s.entry.normalAndBlush.EncodeToPNG()); s.dirty = false;
        }
        if (maps) { EditorUtility.SetDirty(maps); AssetDatabase.SaveAssetIfDirty(maps); }
    }
    private void ReloadBodyMasksAfterUndo()
    {
        foreach (var s in bodySurfaces)
        {
            var restored = s.entry.normalAndBlush.GetPixels();
            if (s.mask.SequenceEqual(restored)) continue;
            s.mask = restored; s.dirty = true; UpdateBodyOverlay(s);
        }
        SaveBodyCanvas();
    }
    private void ClearBodyCanvas()
    {
        foreach (var s in bodySurfaces) { if (s.image) DestroyImmediate(s.image); if (s.overlay) DestroyImmediate(s.overlay); }
        bodySurfaces.Clear(); strokeSurfaces.Clear(); hiddenBodySurface = null; bodyPainting = false; brushResizing = false;
        if (bodyControl != 0 && GUIUtility.hotControl == bodyControl) GUIUtility.hotControl = 0;
        bodyControl = 0;
    }
}
#endif
