#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    [SerializeField] private bool reanimateEditingAss;
    private void DrawReanimateAssSettings()
    {
        EditorGUILayout.Space();
        bool next=GUILayout.Toggle(reanimateEditingAss,"Ass",EditorStyles.miniButton);
        if(next!=reanimateEditingAss)
        {
            layerPreviewRenderer.CancelAssStroke();layerPreviewRenderer.CancelBoobV2Interaction();
            layerPreviewRenderer.CancelSoftContourBellyStroke();layerPreviewRenderer.IsDrawingSoftContourBelly=false;
            reanimateEditingAss=next;
        }
        if(reanimateEditingAss)DrawAssSettings(false);
    }
    private void DrawAssSettings(bool manual)
    {
        var renderer=layerPreviewRenderer;
        EditorGUILayout.HelpBox("Draw two Ass contours. Centers are placed automatically inside each contour.",MessageType.None);
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Toggle(renderer.AssSelection==0,renderer.HasAssContour(0)?"Redraw Left":"Draw Left",EditorStyles.miniButton))renderer.AssSelection=0;
            if(GUILayout.Toggle(renderer.AssSelection==1,renderer.HasAssContour(1)?"Redraw Right":"Draw Right",EditorStyles.miniButton))renderer.AssSelection=1;
        }
        renderer.AssDensity=EditorGUILayout.IntSlider("Density",renderer.AssDensity,8,128);
        renderer.AssSmoothing=EditorGUILayout.IntSlider("Smoothing",renderer.AssSmoothing,0,6);
        if(GUILayout.Button("Clear Ass")){renderer.ClearAss();Repaint();}
        if(manual)
            using(new EditorGUI.DisabledScope(!renderer.HasAssContour(0)||!renderer.HasAssContour(1)))
                if(GUILayout.Button("Apply Ass"))
                {
                    bool ok=renderer.ApplyAss(out _,out var error);
                    ShowNotification(new GUIContent(ok?"Ass applied":error));Repaint();
                }
    }
}
#endif
