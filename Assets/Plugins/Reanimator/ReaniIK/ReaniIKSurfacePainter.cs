#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter : ScriptableObject
{
    [SerializeField] private ReaniIK rig;
    [SerializeField] private ReaniIKSurfaceMaps maps;
    [SerializeField] private string sourcePath;
    [System.NonSerialized] private System.Action repaint;
    private Sprite[] sprites;
    private int selected;
    private Texture2D image, overlay;
    [SerializeField] private bool showNormals;
    private Color SurfacePreviewColor(Color source, Color mask, Color tint)
    {
        if (!showNormals) return MultiplyRedness(source, tint, mask.a);
        var n = ReaniIKSurfaceMaps.DecodeNormal(mask, maps ? maps.normalStrength : 1);
        return new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, source.a);
    }
    private Color[] pixels;
    private ReaniIKSurfaceMaps.Entry entry;
    [SerializeField] private float radius = .055f, strength = .45f;
    private bool paint, brushResizing;
    private int paintButton, paintControl;
    private Vector2 scroll;
    private Rect singleCanvasViewport;
    public static void Open(ReaniIK rig)
    {
        Reanimator.OpenSurfaceMasks(rig);
    }
    public void Bind(ReaniIK target, string path, System.Action repaintWindow, PsdLayerPreviewRenderer preview = null)
    {
        repaint = repaintWindow;
        if (rig == target && sourcePath == path && sprites != null) return;
        normalsCancellation?.Cancel();
        SaveBodyCanvas(); Save(); ClearBodyCanvas(); Cleanup(); pixels = null; entry = null;
        rig = target; sourcePath = path;
        sprites = rig ? rig.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.sprite).Select(r => r.sprite).Distinct().ToArray() :
            string.IsNullOrEmpty(path) ? new Sprite[0] : AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        if (sprites.Length == 0) { maps = null; return; }
        if (rig) maps = ReaniIKSurfaceAuthoring.Ensure(rig);
        else
        {
            string mapsPath = System.IO.Path.ChangeExtension(path, ".surfaces.asset");
            maps = AssetDatabase.LoadAssetAtPath<ReaniIKSurfaceMaps>(mapsPath);
            if (!maps) { maps = CreateInstance<ReaniIKSurfaceMaps>(); AssetDatabase.CreateAsset(maps, mapsPath); }
        }
        selected = System.Array.FindIndex(sprites, sprite => sprite.name.Equals("body", System.StringComparison.OrdinalIgnoreCase));
        ReaniIKSurfaceAuthoring.InitializeSelection(maps);
        if (selected < 0) selected = 0;
        Load();
        PrepareBodyLayout(preview);
    }
    private void Repaint() => repaint?.Invoke();
    private void Load()
    {
        Cleanup();
        if (sprites == null || sprites.Length == 0) return;
        entry = ReaniIKSurfaceAuthoring.EnsureEntry(maps, sprites[selected]);
        image = ReaniIKSurfaceAuthoring.Extract(entry.sprite);
        pixels = entry.normalAndBlush.GetPixels();
        overlay = new Texture2D(entry.normalAndBlush.width, entry.normalAndBlush.height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
        UpdateOverlay();
    }
    private void OnEnable() { Undo.undoRedoPerformed += OnUndo; }
    private void OnDisable() { normalsCancellation?.Cancel(); Undo.undoRedoPerformed -= OnUndo; SaveBodyCanvas(); Save(); ClearBodyCanvas(); Cleanup(); }
    private void OnUndo()
    {
        if (entry == null || !entry.normalAndBlush) return;
        pixels = entry.normalAndBlush.GetPixels(); Save(); UpdateOverlay(); ReloadBodyMasksAfterUndo(); Repaint();
    }
    private void Cleanup()
    {
        if (image) DestroyImmediate(image); if (overlay) DestroyImmediate(overlay);
        image = null; overlay = null;
    }
    private void Save()
    {
        if (entry == null || pixels == null || !entry.normalAndBlush) return;
        entry.normalAndBlush.SetPixels(pixels); entry.normalAndBlush.Apply(false);
        System.IO.File.WriteAllBytes(AssetDatabase.GetAssetPath(entry.normalAndBlush), entry.normalAndBlush.EncodeToPNG());
        EditorUtility.SetDirty(maps); AssetDatabase.SaveAssetIfDirty(maps);
    }
    private void UpdateOverlay()
    {
        if (!overlay || pixels == null) return;
        var colors = new Color[pixels.Length];
        Color tint = rig ? rig.surfaceEffects.rednessColor : new Color(.95f, .12f, .15f, 1);
        for (int y = 0; y < overlay.height; y++) for (int x = 0; x < overlay.width; x++)
        {
            int i = y * overlay.width + x;
            var source = image.GetPixelBilinear((x + .5f) / overlay.width, (y + .5f) / overlay.height);
            colors[i] = entry.effectsEnabled ? SurfacePreviewColor(source, pixels[i], tint) : source;
        }
        overlay.SetPixels(colors); overlay.Apply(false);
        foreach (var surface in bodySurfaces)
            if (surface.entry == entry) { surface.mask = pixels; UpdateBodyOverlay(surface); }
    }
    public void DrawGUI(float availableWidth)
    {
        DrawSettings();
        DrawCanvas(availableWidth);
    }
    public void DrawSettings()
    {
        if (sprites == null || sprites.Length == 0) { EditorGUILayout.HelpBox("Select a layered PSD or choose a scene rig to paint surface masks.", MessageType.Info); return; }
        bool nextWhole = GUILayout.Toolbar(wholeBody ? 0 : 1, new[] { "Whole Body", "Single Sprite" }) == 0;
        if (nextWhole != wholeBody) { SaveBodyCanvas(); Save(); ClearBodyCanvas(); wholeBody = nextWhole; Load(); }
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Selected sprite");
        int next = EditorGUILayout.Popup(selected, sprites.Select(s => s.name).ToArray());
        if (EditorGUI.EndChangeCheck()) { SaveBodyCanvas(); Save(); selected = next; Load(); }

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("1. Redness", EditorStyles.boldLabel);
            strength = EditorGUILayout.Slider("Brush opacity", strength, .01f, 1);
            if (GUILayout.Button(new GUIContent("Blur redness", "Blur redness only on checked, visible sprites. Normals are preserved."))) BlurRednessMasks();
            EditorGUILayout.LabelField("LMB: paint · RMB: erase\nShift + wheel: brush radius\nShift + RMB: hide / restore sprite", EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("2. Sweat normals", EditorStyles.boldLabel);
            bool nextNormals = GUILayout.Toggle(showNormals, "Show normals", "Button");
            if (nextNormals != showNormals)
            {
                showNormals = nextNormals;
                UpdateOverlay();
                foreach (var surface in bodySurfaces) UpdateBodyOverlay(surface);
                Repaint();
            }
            EditorGUI.BeginChangeCheck();
            float normalStrength = EditorGUILayout.Slider(new GUIContent("Normal strength", "0: flat surface, 1: original Comfy normals. Changes preview and sweat flow without modifying the generated texture."), maps.normalStrength, 0, 3);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(maps, "Adjust normal strength"); maps.normalStrength = normalStrength; EditorUtility.SetDirty(maps);
                UpdateOverlay(); foreach (var surface in bodySurfaces) UpdateBodyOverlay(surface);
                EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); Repaint();
            }
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft("Sweat on selected sprite", entry.sweat);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(maps, "Toggle sprite sweat"); entry.sweat = enabled; EditorUtility.SetDirty(maps); }
            DrawComfyNormalsControls();
            using (new EditorGUI.DisabledScope(!entry.effectsEnabled || NormalsBusy))
                if (GUILayout.Button("Import normal map…")) ImportNormals();
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(NormalsBusy))
            if (GUILayout.Button("Save", GUILayout.Height(28))) { SaveBodyCanvas(); Save(); }
    }
    public void DrawCanvas(float availableWidth)
    {
        if (sprites == null || sprites.Length == 0) return;
        if (wholeBody) { DrawBodyCanvas(availableWidth); return; }
        // Scroll views consume wheel events themselves; claim modified wheel
        // input before entering the single-sprite scroll view.
        if (Event.current.type == EventType.ScrollWheel && Event.current.shift)
            HandleBrushRadius(Event.current, singleCanvasViewport, 1);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        float width = Mathf.Max(100, availableWidth - 30), height = width * image.height / image.width;
        var area = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        EditorGUI.DrawRect(area, new Color(.17f, .17f, .17f));
        if (entry.effectsEnabled) GUI.DrawTexture(area, overlay, ScaleMode.StretchToFill, true);
        else GUI.Label(area, "Sprite is unchecked in Affected sprites.", EditorStyles.centeredGreyMiniLabel);
        var e = Event.current;
        HandleBrushRadius(e, area, area.height);
        if (area.Contains(e.mousePosition))
        {
            Handles.BeginGUI(); Handles.color = new Color(.65f, 1, .45f);
            Handles.DrawWireDisc(e.mousePosition, Vector3.forward, radius * area.height); Handles.EndGUI();
            if (StartsBrush(e))
            { Undo.RegisterCompleteObjectUndo(entry.normalAndBlush, e.button == 1 ? "Erase redness alpha" : "Paint redness alpha"); paint = true; paintButton = e.button; paintControl = GUIUtility.GetControlID(FocusType.Passive); GUIUtility.hotControl = paintControl; Stroke(area, e.mousePosition, paintButton == 1); e.Use(); }
        }
        if (paint && e.type == EventType.MouseDrag && !e.shift) { Stroke(area, e.mousePosition, paintButton == 1); e.Use(); }
        if (paint && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)) { paint = false; if (GUIUtility.hotControl == paintControl) GUIUtility.hotControl = 0; Save(); e.Use(); }
        if (e.type == EventType.ContextClick && area.Contains(e.mousePosition)) e.Use();
        EditorGUILayout.EndScrollView();
        if (e.type == EventType.Repaint) singleCanvasViewport = GUILayoutUtility.GetLastRect();
        if (e.type == EventType.MouseMove || paint) Repaint();
    }
    private static bool StartsBrush(Event e) => e.type == EventType.MouseDown && (e.button == 0 || e.button == 1) && !e.shift && !e.alt;
    private bool HandleBrushRadius(Event e, Rect area, float radiusScale)
    {
        if (!e.shift || e.alt) { brushResizing = false; return false; }
        if (!area.Contains(e.mousePosition)) { brushResizing = false; return false; }
        brushResizing = true;
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            e.Use(); Repaint(); return true;
        }
        if (e.type == EventType.ScrollWheel)
        {
            // Shift+wheel is delivered as horizontal scrolling on some editor/
            // OS combinations. Do not assume its delta is still in Y.
            float delta = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y;
            radius = Mathf.Clamp(radius * Mathf.Exp(-delta * .05f), .005f, .3f);
            e.Use(); Repaint(); return true;
        }
        if (e.type == EventType.MouseDown || e.type == EventType.ContextClick) { e.Use(); return true; }
        Repaint(); return false;
    }
    private void Stroke(Rect rect, Vector2 mouse, bool clear)
    {
        if (!entry.effectsEnabled) return;
        int w = overlay.width, h = overlay.height;
        Vector2 p = new Vector2((mouse.x - rect.x) / rect.width * w, (1 - (mouse.y - rect.y) / rect.height) * h);
        float r = radius * h;
        for (int y = Mathf.Max(0, Mathf.FloorToInt(p.y - r)); y <= Mathf.Min(h - 1, Mathf.CeilToInt(p.y + r)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(p.x - r)); x <= Mathf.Min(w - 1, Mathf.CeilToInt(p.x + r)); x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), p) / r;
                if (d > 1) continue;
                int k = y * w + x;
                float blend = strength * (1 - d) * entry.coverage.GetPixel(x, y).a;
                pixels[k].a = Mathf.Lerp(pixels[k].a, clear ? 0 : 1, blend);
            }
        entry.normalAndBlush.SetPixels(pixels); entry.normalAndBlush.Apply(false); UpdateOverlay();
        EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); Repaint();
    }
    private void ImportNormals()
    {
        string path = EditorUtility.OpenFilePanel("Import RGB surface normal map", "", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(path)) return;
        var normal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!normal.LoadImage(System.IO.File.ReadAllBytes(path))) return;
            Undo.RegisterCompleteObjectUndo(entry.normalAndBlush, "Import surface normals");
            for (int y = 0; y < overlay.height; y++) for (int x = 0; x < overlay.width; x++)
            {
                int k = y * overlay.width + x; var c = normal.GetPixelBilinear((x + .5f) / overlay.width, (y + .5f) / overlay.height);
                c.a = pixels[k].a; pixels[k] = c;
            }
            Save(); UpdateOverlay();
        }
        finally { DestroyImmediate(normal); }
    }
}
#endif
