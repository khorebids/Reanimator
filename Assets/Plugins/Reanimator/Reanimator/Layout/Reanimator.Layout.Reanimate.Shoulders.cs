#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    private int reanimateShoulderSide;
    private int reanimateShoulderControl;
    private bool reanimateShoulderDrawing;
    private PsdLayerPreviewRenderer.LayerInfo reanimateShoulderLayer;
    private readonly List<Vector2> reanimateShoulderStroke = new();
    private string ReanimateShoulderBone => reanimateShoulderSide == 0 ? "l_arm" : "r_arm";

    private void SetReanimateShoulderSide(int side)
    {
        CancelReanimateShoulderStroke(); reanimateShoulderSide = Mathf.Clamp(side, 0, 1);
        reanimateShoulderLayer = null; reanimateShoulderDrawing = false; Repaint();
    }
    private void CancelReanimateShoulderStroke()
    {
        if (reanimateShoulderControl != 0 && GUIUtility.hotControl == reanimateShoulderControl) GUIUtility.hotControl = 0;
        reanimateShoulderControl = 0; reanimateShoulderStroke.Clear();
    }
    private PsdLayerPreviewRenderer.LayerInfo DrawReanimateShouldersSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Shoulder Weld Contours", EditorStyles.boldLabel);
        int side = GUILayout.Toolbar(reanimateShoulderSide, new[] { "Left", "Right" });
        if (side != reanimateShoulderSide) SetReanimateShoulderSide(side);
        var layer = layerPreviewRenderer.FindShoulderSprite(ReanimateShoulderBone);
        if (layer == null)
        {
            EditorGUILayout.HelpBox("This arm sprite is absent.", MessageType.Info); return null;
        }
        var contour = layerPreviewRenderer.GetShoulderContour(ReanimateShoulderBone);
        if (!ReferenceEquals(layer, reanimateShoulderLayer))
        { reanimateShoulderLayer = layer; reanimateShoulderDrawing = contour.Length < 3; }
        if (GUILayout.Button(reanimateShoulderDrawing ? "Cancel Outline" : contour.Length >= 3 ? "Redraw Weld" : "Draw Weld"))
        { CancelReanimateShoulderStroke(); reanimateShoulderDrawing = !reanimateShoulderDrawing; }
        if (GUILayout.Button("Clear Outline"))
        {
            CancelReanimateShoulderStroke(); layerPreviewRenderer.SetShoulderContour(ReanimateShoulderBone, null);
            reanimateShoulderDrawing = true;
        }
        EditorGUILayout.HelpBox("Outline the attachment across both arm contours. A / D switches arms and steps.", MessageType.Info);
        return layer;
    }
    private void DrawOrHandleReanimateShoulderCanvas(Rect viewport, PsdCoordinateSpace coordinates)
    {
        var input = Event.current;
        if (input.type == EventType.Repaint)
        {
            Handles.BeginGUI();
            var placement = ReanimatorSkeletronModule.CapturePlacement(Api);
            if (placement != null)
            {
                var arm = placement.Find(bone => bone.name == ReanimateShoulderBone);
                if (arm.name != null)
                    using (new Handles.DrawingScope(Color.cyan))
                    {
                        Handles.DrawAAPolyLine(3, coordinates.DocumentToView(arm.start), coordinates.DocumentToView(arm.end));
                        Handles.DrawSolidDisc(coordinates.DocumentToView(arm.start), Vector3.forward, 5);
                    }
            }
            PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView,
                layerPreviewRenderer.GetShoulderContour(ReanimateShoulderBone), Color.cyan, true);
            PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView, reanimateShoulderStroke, Color.yellow, false);
            Handles.EndGUI(); return;
        }
        if (!reanimateShoulderDrawing) return;
        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
        { CancelReanimateShoulderStroke(); reanimateShoulderDrawing = false; input.Use(); Repaint(); return; }
        if (input.type == EventType.MouseDown && input.button == 0 && !input.alt && viewport.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            reanimateShoulderControl = GUIUtility.GetControlID(FocusType.Passive); GUIUtility.hotControl = reanimateShoulderControl;
            reanimateShoulderStroke.Add(coordinates.ViewToDocument(input.mousePosition)); input.Use();
        }
        else if (reanimateShoulderControl != 0 && GUIUtility.hotControl == reanimateShoulderControl &&
                 (input.type == EventType.MouseDrag || (input.type == EventType.MouseUp && input.button == 0)))
        {
            bool finished = input.type == EventType.MouseUp;
            var point = coordinates.ViewToDocument(input.mousePosition);
            if (reanimateShoulderStroke.Count == 0 || (point - reanimateShoulderStroke[reanimateShoulderStroke.Count - 1]).sqrMagnitude >= 1)
                reanimateShoulderStroke.Add(point);
            if (finished)
            {
                var contour = PsdLayerPreviewRenderer.BuildSmoothedClosedBoobV2Contour(reanimateShoulderStroke, 1, 32);
                if (contour.Count >= 3)
                {
                    layerPreviewRenderer.SetShoulderContour(ReanimateShoulderBone, contour);
                    reanimateShoulderDrawing = false;
                    CancelReanimateShoulderStroke();
                    if (reanimateShoulderSide == 0 && layerPreviewRenderer.FindShoulderSprite("r_arm") != null) SetReanimateShoulderSide(1);
                }
                else CancelReanimateShoulderStroke();
            }
            input.Use(); Repaint();
        }
    }
}
#endif
