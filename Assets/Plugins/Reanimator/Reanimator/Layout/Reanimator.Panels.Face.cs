#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const string EyelidsBoneName = "eyelids";
    private const string HeadBoneName = "head";
    private const float EyelidsLengthRatio = 1f / 6f;

    private void DrawFaceToolPanel(float availableHeight)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Face", EditorStyles.boldLabel);

            var clampedIndex = Mathf.Clamp(faceSubtoolIndex, 0, FaceSubtoolLabels.Length - 1);
            var nextIndex = GUILayout.Toolbar(clampedIndex, FaceSubtoolLabels, GUILayout.Height(22f));
            if (nextIndex != faceSubtoolIndex)
            {
                faceSubtoolIndex = nextIndex;
                faceScroll = Vector2.zero;
                Repaint();
            }

            GUILayout.Space(4f);
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(faceScroll, GUILayout.ExpandHeight(true)))
            {
                faceScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField(FaceSubtoolLabels[faceSubtoolIndex], EditorStyles.boldLabel);
                if (faceSubtoolIndex == 0)
                    DrawFaceEyesPanel();
                else
                    DrawFaceSubtoolPlaceholder();
            }
        }
    }

    private void DrawFaceEyesPanel()
    {
        var renderer = layerPreviewRenderer;
        if (renderer == null || !renderer.TryGetSelectedLayer(out _))
        {
            EditorGUILayout.HelpBox("Sprite is not selected.", MessageType.Error);
            return;
        }

        if (DrawFaceEyesAlphaMaskStatus(renderer, out var alphaAnalysis))
            DrawFaceEyesPointSettings(renderer, alphaAnalysis);

        if (renderer.TryFindBoneIndexByName(EyelidsBoneName, out _))
        {
            DrawFaceReadyStatus("Bone 'eyelids' found.");
            return;
        }

        EditorGUILayout.HelpBox("Required bone 'eyelids' is missing.", MessageType.Error);
        if (!renderer.TryFindBoneIndexByName(HeadBoneName, out var headBoneIndex))
        {
            EditorGUILayout.HelpBox(
                "Bone 'head' is missing, so 'eyelids' cannot be created automatically.",
                MessageType.Error);
            return;
        }

        var bones = renderer.Bones;
        var headBone = headBoneIndex >= 0 && headBoneIndex < bones.Count ? bones[headBoneIndex] : null;
        if (headBone == null)
            return;
        if (alphaAnalysis == null ||
            !alphaAnalysis.HasLeftHole ||
            !alphaAnalysis.HasRightHole ||
            !renderer.TryGetSelectedLayer(out var selectedLayer))
        {
            EditorGUILayout.HelpBox(
                "Both eye centers are required to create the 'eyelids' bone.",
                MessageType.Error);
            return;
        }

        GUILayout.Space(4f);
        if (!GUILayout.Button("Create Eyelids Bone", GUILayout.Height(28f)))
            return;

        var eyelidsLength = Mathf.Max(0.01f, headBone.length * EyelidsLengthRatio);
        var eyeMidpointMask = (alphaAnalysis.LeftHole.Center + alphaAnalysis.RightHole.Center) * 0.5f;
        var maskToLocalScale = new Vector2(
            selectedLayer.sourceRect.width / Mathf.Max(1f, alphaAnalysis.Width),
            selectedLayer.sourceRect.height / Mathf.Max(1f, alphaAnalysis.Height));
        var eyeMidpointDocument = PsdCoordinateSpace.LayerLocalToDocumentPoint(
            selectedLayer,
            Vector2.Scale(eyeMidpointMask, maskToLocalScale));
        var eyeMidpointOffset = eyeMidpointDocument - headBone.worldPosition;
        if (renderer.TryCreateNamedChildBone(
                EyelidsBoneName,
                headBoneIndex,
                eyelidsLength,
                eyeMidpointOffset,
                out _))
        {
            ShowNotification(new GUIContent("Eyelids bone created"));
            Repaint();
        }
    }

    private static bool DrawFaceEyesAlphaMaskStatus(
        PsdLayerPreviewRenderer renderer,
        out ReanimatorFaceAlphaMaskAnalysis analysis)
    {
        analysis = null;
        if (renderer == null || !renderer.TryAnalyzeSelectedFaceAlphaMask(0.5f, out analysis) || analysis == null)
        {
            EditorGUILayout.HelpBox("Selected sprite alpha mask could not be read.", MessageType.Error);
            return false;
        }

        if (!analysis.HasContour)
        {
            EditorGUILayout.HelpBox("Selected sprite has no opaque contour.", MessageType.Error);
            return false;
        }

        EditorGUILayout.LabelField(
            $"Alpha holes: {analysis.Holes.Length}   Center: {analysis.Center.x:0.#}, {analysis.Center.y:0.#}",
            EditorStyles.miniLabel);
        if (analysis.HasLeftHole && analysis.HasRightHole)
        {
            DrawFaceReadyStatus(
                $"Eye holes found: left {analysis.LeftHole.Area}px, right {analysis.RightHole.Area}px.");
            return true;
        }

        var missingSide = !analysis.HasLeftHole && !analysis.HasRightHole
            ? "left and right"
            : !analysis.HasLeftHole ? "left" : "right";
        EditorGUILayout.HelpBox(
            $"Could not find the largest alpha hole on the {missingSide} side of the contour center.",
            MessageType.Error);
        return false;
    }

    private void DrawFaceEyesPointSettings(
        PsdLayerPreviewRenderer renderer,
        ReanimatorFaceAlphaMaskAnalysis analysis)
    {
        if (!renderer.TryGetSelectedLayer(out var layer) ||
            !renderer.TryGetFaceBoneDirectionInMask(HeadBoneName, layer, analysis, out var headDirection))
        {
            EditorGUILayout.HelpBox(
                "Bone 'head' is required to determine upper and lower eye points.",
                MessageType.Error);
            return;
        }
        renderer.TryGetFaceGeometryBoundaryInMask(
            layer,
            analysis,
            out var geometryBoundaryVertices,
            out var geometryBoundaryEdgePairs);

        GUILayout.Space(4f);
        var nextDensity = EditorGUILayout.Slider(
            "Point Density",
            Mathf.Clamp(faceEyesPointDensity, 0.1f, 1f),
            0.1f,
            1f);
        if (!Mathf.Approximately(nextDensity, faceEyesPointDensity))
        {
            faceEyesPointDensity = nextDensity;
            Repaint();
        }

        var nextOffset = EditorGUILayout.Slider(
            "Point Offset",
            Mathf.Clamp(faceEyesPointOffset, 0f, 32f),
            0f,
            32f);
        if (!Mathf.Approximately(nextOffset, faceEyesPointOffset))
        {
            faceEyesPointOffset = nextOffset;
            Repaint();
        }

        var nextOutlineOffset = EditorGUILayout.Slider(
            "Outline Offset",
            Mathf.Clamp(faceEyesOutlineOffset, 0f, 32f),
            0f,
            32f);
        if (!Mathf.Approximately(nextOutlineOffset, faceEyesOutlineOffset))
        {
            faceEyesOutlineOffset = nextOutlineOffset;
            Repaint();
        }

        if (faceEyesWeightCurve == null || faceEyesWeightCurve.length == 0)
            faceEyesWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        EditorGUI.BeginChangeCheck();
        var nextWeightCurve = EditorGUILayout.CurveField(
            "Weight Curve",
            faceEyesWeightCurve,
            new Color(0.4f, 1f, 0.25f, 1f),
            new Rect(0f, 0f, 1f, 1f));
        if (EditorGUI.EndChangeCheck())
        {
            faceEyesWeightCurve = CloneFaceEyesWeightCurve(nextWeightCurve);
            SaveFaceEyesWeightCurvePreference();
            EditorUtility.SetDirty(this);
            Repaint();
        }
        EditorGUILayout.LabelField("Curve input: center 0  →  edges 1", EditorStyles.miniLabel);

        var leftPoints = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            analysis.LeftHole,
            faceEyesPointDensity,
            headDirection,
            faceEyesPointOffset,
            faceEyesOutlineOffset,
            geometryBoundaryVertices,
            geometryBoundaryEdgePairs,
            faceEyesWeightCurve);
        var rightPoints = ReanimatorFaceAlphaMaskAlgorithms.BuildHeadDirectedEyePoints(
            analysis,
            analysis.RightHole,
            faceEyesPointDensity,
            headDirection,
            faceEyesPointOffset,
            faceEyesOutlineOffset,
            geometryBoundaryVertices,
            geometryBoundaryEdgePairs,
            faceEyesWeightCurve);
        ReanimatorFaceAlphaMaskAlgorithms.ApplySharedEyeOutlineCollisionStop(
            faceEyesOutlineOffset,
            leftPoints,
            rightPoints);
        EditorGUILayout.LabelField(
            $"Eye points: left {leftPoints.TotalPointCount} ({leftPoints.DuplicatePoints.Length} duplicated), " +
            $"right {rightPoints.TotalPointCount} ({rightPoints.DuplicatePoints.Length} duplicated)",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            $"Red outline: left {leftPoints.OutlineContour.Length}, right {rightPoints.OutlineContour.Length}",
            EditorStyles.miniLabel);

        if (!renderer.TryFindBoneIndexByName(EyelidsBoneName, out _))
            return;
        GUILayout.Space(8f);
        if (!GUILayout.Button("Apply", GUILayout.Height(30f)))
            return;

        if (renderer.ApplyFaceEyesGeometry(
                analysis,
                leftPoints,
                rightPoints,
                out var addedVertexCount,
                out var removedVertexCount,
                out var weightedVertexCount,
                out var applyError))
        {
            ShowNotification(new GUIContent(
                $"Eyes applied: {removedVertexCount} removed, {addedVertexCount} added, {weightedVertexCount} weighted"));
        }
        else
        {
            ShowNotification(new GUIContent(
                string.IsNullOrWhiteSpace(applyError) ? "Eyes apply failed" : applyError));
        }
        Repaint();
    }

    private static void DrawFaceReadyStatus(string message)
    {
        var style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = new Color(0.2f, 0.85f, 0.3f);
        EditorGUILayout.LabelField(message, style);
    }

    private static void DrawFaceSubtoolPlaceholder()
    {
        EditorGUILayout.HelpBox(
            "Settings for this Face subtool will be added here.",
            MessageType.Info);
        DrawFaceAutomationPlaceholder();
    }

    private static void DrawFaceAutomationPlaceholder()
    {
        GUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(true))
            GUILayout.Button("Automation", GUILayout.Height(28f));
    }
}
#endif
