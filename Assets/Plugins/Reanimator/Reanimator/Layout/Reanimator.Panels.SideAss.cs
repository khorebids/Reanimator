#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private void DrawSideAssPanel()
    {
        var renderer=layerPreviewRenderer;
        renderer.SynchronizeSideAssCurveSettings();
        EditorGUILayout.HelpBox("Select the leg sprite and upper leg bone, then click Auto arc. It follows the contour from the backward bone ray to the perpendicular at 1/4 leg length. Manual drawing remains optional.",MessageType.Info);
        if(renderer.TryGetSelectedLayer(out var layer)) EditorGUILayout.LabelField("Sprite",layer.name);
        else EditorGUILayout.HelpBox("Select a leg sprite in the layer list.",MessageType.Warning);
        DrawLimbsBoneSelector(renderer);
        using(new EditorGUI.DisabledScope(layer==null || renderer.SelectedBoneIndex<0))
            if(GUILayout.Button("Quick — auto arc + apply",GUILayout.Height(32)))
            {renderer.QuickSideAss(out var message);ShowNotification(new GUIContent(message));Repaint();}
        using(new EditorGUI.DisabledScope(layer==null || renderer.SelectedBoneIndex<0))
            if(GUILayout.Button("Auto arc — ¼ leg",GUILayout.Height(28)))
            {renderer.AutoDrawSideAssArc(out var message);ShowNotification(new GUIContent(message));Repaint();}
        renderer.SideAssFanPoints=EditorGUILayout.IntSlider("Arc points",renderer.SideAssFanPoints,8,24);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Full arc — helper influence");
        var arcInfluence=EditorGUILayout.CurveField(renderer.SideAssArcInfluence,Color.cyan,new Rect(0,0,1,1),GUILayout.Height(75));
        EditorGUILayout.LabelField("0–0.5: Back · 0.5–1: Perpendicular",EditorStyles.miniLabel);
        var helperSmoothing=EditorGUILayout.Slider("Helper smoothing",renderer.SideAssHelperSmoothing,0,1);
        var bodyReach=EditorGUILayout.Slider(new GUIContent("Body continuation","Extend the same two helpers onto the hip/torso. Reach is relative to the Side Ass radius; zero disables the extension."),renderer.SideAssBodyReach,0,2);
        if(EditorGUI.EndChangeCheck())
        {
            renderer.SideAssArcInfluence=arcInfluence;renderer.SideAssHelperSmoothing=helperSmoothing;
            renderer.SideAssBodyReach=bodyReach;
            renderer.SaveAndApplySideAssCurves();Repaint();
        }
        renderer.SideAssFlip=EditorGUILayout.ToggleLeft("Flip back axis",renderer.SideAssFlip);
        if(GUILayout.Button("Build / refresh interactive preview",GUILayout.Height(28)))
        {renderer.BuildSideAssTest(out var previewMessage);ShowNotification(new GUIContent(previewMessage));Repaint();}
        renderer.SideAssTestEnabled=EditorGUILayout.ToggleLeft("Interactive preview",renderer.SideAssTestEnabled);
        if(renderer.SideAssTestEnabled)
        {
            EditorGUI.BeginChangeCheck();
            renderer.SideAssTestRotation=EditorGUILayout.Slider("Test leg rotation",renderer.SideAssTestRotation,-180,180);
            renderer.SideAssTestBack=EditorGUILayout.Vector2Field("Test back offset",renderer.SideAssTestBack);
            renderer.SideAssTestPerpendicular=EditorGUILayout.Vector2Field("Test perpendicular offset",renderer.SideAssTestPerpendicular);
            renderer.SideAssTestWire=EditorGUILayout.ToggleLeft("Preview wireframe",renderer.SideAssTestWire);
            if(EditorGUI.EndChangeCheck())renderer.UpdateSideAssTestPose();
            if(GUILayout.Button("Reset test pose"))renderer.ResetSideAssTestPose();
            EditorGUILayout.HelpBox("Drag yellow to rotate the leg; magenta / cyan to move helpers. Curves update the temporary mesh live. Test poses are never baked by Apply.",MessageType.None);
            Repaint();
        }
        if(GUILayout.Button(renderer.SideAssDrawing?"Cancel drawing":"Draw outer arc"))
        {
            if(renderer.SideAssDrawing) renderer.ClearSideAssContour();
            else renderer.SideAssDrawing=true;
            Repaint();
        }
        using(new EditorGUI.DisabledScope(!renderer.HasSideAssContour || layer==null || renderer.SelectedBoneIndex<0))
            if(GUILayout.Button("Apply Side Ass",GUILayout.Height(25)))
            {
                if(renderer.ApplySideAss(out var message))renderer.SideAssTestEnabled=false;
                ShowNotification(new GUIContent(message)); Repaint();
            }
        if(GUILayout.Button("Clear arc")){renderer.ClearSideAssContour();Repaint();}
        if(GUILayout.Button("Reset helper origins")){renderer.ResetSideAssHelperOrigins();Repaint();}
        EditorGUILayout.HelpBox("One curve spans the full contour, including the attachment extension. Its first half weights Back, its second half weights Perpendicular. Height is helper weight; the remaining weight belongs to the selected leg.",MessageType.None);
    }
}
#endif
