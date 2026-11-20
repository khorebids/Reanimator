#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const int FirstReanimateLimbStep = 4;
    private static readonly string[] ReanimateLimbContourNames = { "Contour 1", "Contour 2" };
    private static int ReanimateContourCount(int step)=>step==ReanimateHipsStep?1:2;
    private static readonly Color[] ReanimateLimbContourColors =
    {
        Color.cyan, new Color(1f, 0.35f, 0.7f)
    };

    private readonly Dictionary<string, List<Vector2>[][]> reanimateLimbDrafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Vector2> reanimateLimbStroke = new();
    private List<Vector2>[] reanimateActiveLimbDraft;
    private int reanimateLimbContour = -1;
    private int reanimateLimbStrokeControl;

    private void DrawReanimateLimbSteps()
    {
        if (!reanimateLimbDrafts.TryGetValue(reanimateCanvasPath, out var drafts))
        {
            drafts = new List<Vector2>[ReanimateOthersStep - FirstReanimateLimbStep][];
            reanimateLimbDrafts.Add(reanimateCanvasPath, drafts);
        }
        var limb = reanimateStep - FirstReanimateLimbStep;
        var contours = drafts[limb];
        if (contours == null)
        {
            contours = new List<Vector2>[ReanimateContourCount(reanimateStep)];
            for (var i = 0; i < contours.Length; i++)
                contours[i] = new List<Vector2>();
            drafts[limb] = contours;
        }
        else if(contours.Length!=ReanimateContourCount(reanimateStep))
        {
            Array.Resize(ref contours,ReanimateContourCount(reanimateStep));
            for(int i=0;i<contours.Length;i++)contours[i]??=new List<Vector2>();
            drafts[limb]=contours;
        }
        if (!ReferenceEquals(reanimateActiveLimbDraft, contours))
        {
            reanimateActiveLimbDraft = contours;
            ArmNextReanimateLimbContour();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{ReanimateStepNames[reanimateStep]}  ({limb + 1}/{ReanimateOthersStep - FirstReanimateLimbStep})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("A / D to switch", EditorStyles.miniLabel);
        for (var i = 0; i < contours.Length; i++)
        {
            var drawing = reanimateLimbContour == i;
            var prefix = drawing ? "Drawing " : contours[i].Count >= 3 ? "Redraw " : "Draw ";
            if (GUILayout.Toggle(drawing, prefix + GetReanimateSoftContourName(i), EditorStyles.miniButton) && !drawing)
            {
                CancelReanimateLimbStroke();
                reanimateLimbContour = i;
                Repaint();
            }
        }
    }

    private void DrawReanimateHipsGuide(PsdCoordinateSpace coordinates)
    {
        var skeleton = ReanimatorSkeletronModule.CapturePlacement(Api);
        if (skeleton == null)
            return;
        var hips = skeleton.Find(segment => segment.name == "hips");
        if (hips.name == null)
            return;

        Handles.BeginGUI();
        using (new Handles.DrawingScope(new Color(0.35f, 0.95f, 0.95f)))
        {
            Vector3 start = coordinates.DocumentToView(hips.start);
            Vector3 end = coordinates.DocumentToView(hips.end);
            Handles.DrawAAPolyLine(3f, start, end);
            Handles.DrawSolidDisc(start, Vector3.forward, 4f);
            Handles.DrawSolidDisc(end, Vector3.forward, 4f);
        }
        Handles.EndGUI();
    }

    private string GetReanimateSoftContourName(int index) =>
        reanimateStep == ReanimateHipsStep ? "Belly Sides" : ReanimateLimbContourNames[index];

    private void ArmNextReanimateLimbContour()
    {
        reanimateLimbContour = Array.FindIndex(reanimateActiveLimbDraft, contour => contour.Count < 3);
    }

    private void LeaveReanimateLimbStep()
    {
        CancelReanimateLimbStroke();
        reanimateActiveLimbDraft = null;
        reanimateLimbContour = -1;
    }

    private void CancelReanimateLimbStroke()
    {
        if (reanimateLimbStrokeControl != 0 && GUIUtility.hotControl == reanimateLimbStrokeControl)
            GUIUtility.hotControl = 0;
        reanimateLimbStrokeControl = 0;
        reanimateLimbStroke.Clear();
    }

    private void DrawOrHandleReanimateLimbCanvas(Rect viewport, PsdCoordinateSpace coordinates)
    {
        var input = Event.current;
        if (input.type == EventType.Repaint)
        {
            Handles.BeginGUI();
            var previousColor = Handles.color;
            for (var i = 0; i < reanimateActiveLimbDraft.Length; i++)
                PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView, reanimateActiveLimbDraft[i],
                    ReanimateLimbContourColors[i], true);
            if (reanimateLimbContour >= 0)
                PsdLayerPreviewRenderer.DrawBoobV2Contour(coordinates.DocumentToView, reanimateLimbStroke,
                    ReanimateLimbContourColors[reanimateLimbContour], false);
            Handles.color = previousColor;
            Handles.EndGUI();

            if (reanimateLimbContour >= 0 && viewport.Contains(input.mousePosition))
            {
                const float width = 66f;
                const float height = 21f;
                var x = Mathf.Clamp(input.mousePosition.x - width * 0.5f, 0f, Mathf.Max(0f, viewport.width - width));
                var y = Mathf.Max(0f, input.mousePosition.y - height - 12f);
                GUI.Label(new Rect(x, y, width, height), GetReanimateSoftContourName(reanimateLimbContour),
                    EditorStyles.helpBox);
            }
            return;
        }
        if (input.type == EventType.MouseMove || input.type == EventType.MouseLeaveWindow)
        {
            Repaint();
            return;
        }
        if (reanimateLimbContour < 0)
            return;

        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape && reanimateLimbStrokeControl != 0)
            CancelReanimateLimbStroke();
        else if (input.type == EventType.MouseDown && input.button == 0 && !input.alt &&
                 viewport.Contains(input.mousePosition) && GUIUtility.hotControl == 0)
        {
            reanimateLimbStrokeControl = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = reanimateLimbStrokeControl;
            AddPoint();
        }
        else if (reanimateLimbStrokeControl != 0 && GUIUtility.hotControl == reanimateLimbStrokeControl)
        {
            if (input.type == EventType.MouseDrag)
                AddPoint();
            else if (input.type == EventType.MouseUp && input.button == 0)
            {
                AddPoint();
                var contour = PsdLayerPreviewRenderer.BuildSmoothedClosedBoobV2Contour(reanimateLimbStroke, 2, 32);
                if (contour.Count >= 3)
                {
                    reanimateActiveLimbDraft[reanimateLimbContour] = contour;
                    ArmNextReanimateLimbContour();
                }
                CancelReanimateLimbStroke();
            }
            else
                return;
        }
        else
            return;

        input.Use();
        Repaint();

        void AddPoint()
        {
            var point = coordinates.ViewToDocument(input.mousePosition);
            if (reanimateLimbStroke.Count == 0 || (point - reanimateLimbStroke[reanimateLimbStroke.Count - 1]).sqrMagnitude >= 0.25f)
                reanimateLimbStroke.Add(point);
        }
    }
}
#endif
