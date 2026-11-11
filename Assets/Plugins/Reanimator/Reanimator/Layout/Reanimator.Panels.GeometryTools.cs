#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawPreviewPoseToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Preview Pose", EditorStyles.boldLabel);
                if (layerPreviewRenderer != null)
                {
                    layerPreviewRenderer.PreviewTestAnimEnabled = EditorGUILayout.Toggle("TestAnim", layerPreviewRenderer.PreviewTestAnimEnabled);
                    layerPreviewRenderer.PreviewTestAnimPlaybackMode = (PsdLayerPreviewRenderer.PreviewTestAnimMode)EditorGUILayout.EnumPopup("Mode", layerPreviewRenderer.PreviewTestAnimPlaybackMode);
                    layerPreviewRenderer.PreviewTestAnimPower = EditorGUILayout.FloatField("Power", layerPreviewRenderer.PreviewTestAnimPower);
                    if (layerPreviewRenderer.PreviewTestAnimPlaybackMode == PsdLayerPreviewRenderer.PreviewTestAnimMode.Cyclic)
                        layerPreviewRenderer.PreviewTestAnimCyclicDepthOffset = EditorGUILayout.FloatField("Depth Offset", layerPreviewRenderer.PreviewTestAnimCyclicDepthOffset);
                    layerPreviewRenderer.PreviewTestAnimHierarchyCurve = EditorGUILayout.CurveField("Hierarchy Curve", layerPreviewRenderer.PreviewTestAnimHierarchyCurve);
                    layerPreviewRenderer.PreviewTestAnimCurve = EditorGUILayout.CurveField("Anim Curve", layerPreviewRenderer.PreviewTestAnimCurve);
                    GUILayout.Space(6f);
                }

                EditorGUILayout.LabelField("Pivot Point", EditorStyles.boldLabel);
                DrawPivotSettings();
                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply Pivot", GUILayout.Height(22f)))
                        ApplyPivotFromUi();
                }

                GUILayout.Space(8f);
                DrawStatsTab();
            }
        }
    }

    private void DrawElbowToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Elbow", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Create an elbow guide line. Apply adds preview vertices to geometry, Cancel removes preview vertices. Both actions clear the elbow guide line.", MessageType.Info);
                if (layerPreviewRenderer != null)
                {
                    var radialPoints = EditorGUILayout.IntSlider("radialPoints", layerPreviewRenderer.ElbowRadialPoints, 1, 20);
                    layerPreviewRenderer.ElbowRadialPoints = radialPoints;
                    var bend = EditorGUILayout.Slider("bend", layerPreviewRenderer.ElbowBend, -2f, 2f);
                    layerPreviewRenderer.ElbowBend = bend;
                }
                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var hasElbowSelection = layerPreviewRenderer != null && layerPreviewRenderer.HasElbowSelection();
                    using (new EditorGUI.DisabledScope(!hasElbowSelection))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                            layerPreviewRenderer?.ApplySelectedElbow();

                        if (GUILayout.Button("Cancel", GUILayout.Height(22f)))
                            layerPreviewRenderer?.CancelSelectedElbow();
                    }
                }

            }
        }
    }

    private void DrawLimbsToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Limbs", EditorStyles.boldLabel);
                var boneMode = GUILayout.Toolbar(limbsBoneMode ? 1 : 0, new[] { "Draw edge", "Bone" }) == 1;
                if (limbsBoneMode != boneMode)
                {
                    limbsBoneMode = boneMode;
                    Repaint();
                }

                if (!limbsBoneMode)
                    EditorGUILayout.HelpBox(
                    "Draw a guide line. Apply pipeline: CircleClean first, then Limb points insertion. Limb points are computed from the original contour.",
                    MessageType.Info);

                EditorGUI.BeginChangeCheck();
                if (layerPreviewRenderer != null)
                {
                    var stepCount = EditorGUILayout.IntSlider("steps", layerPreviewRenderer.LimbContourSteps, 0, 20);
                    layerPreviewRenderer.LimbContourSteps = stepCount;
                    var stepDistance = EditorGUILayout.FloatField("distance", layerPreviewRenderer.LimbContourStepDistance);
                    layerPreviewRenderer.LimbContourStepDistance = Mathf.Max(0.01f, stepDistance);
                    var shift = EditorGUILayout.Slider("shift", layerPreviewRenderer.LimbTowardCenterShift, 0f, 1f);
                    layerPreviewRenderer.LimbTowardCenterShift = shift;
                    var curve = EditorGUILayout.CurveField("curve", layerPreviewRenderer.LimbCurve);
                    layerPreviewRenderer.LimbCurve = curve;
                }

                GUILayout.Space(4f);
                limbsUseRadiusCull = EditorGUILayout.ToggleLeft("Use CircleClean Before Limb", limbsUseRadiusCull);
                if (limbsUseRadiusCull)
                {
                    var miniToggleStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            GUILayout.Toggle(
                                true,
                                new GUIContent("CW", "Limbs always replaces contour vertices inside CircleClean"),
                                miniToggleStyle,
                                GUILayout.Width(34f),
                                GUILayout.Height(18f));
                        }

                        radiusCullRemoveInnerVertices = GUILayout.Toggle(
                            radiusCullRemoveInnerVertices,
                            new GUIContent("IW", "Remove inner vertices"),
                            miniToggleStyle,
                            GUILayout.Width(34f),
                            GUILayout.Height(18f));

                        radiusCullRadius = Mathf.Max(0.01f, GUILayout.HorizontalSlider(radiusCullRadius, 0.01f, 512f));
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(limbsBoneMode ? "Centered on the selected bone" : "Center is locked to limb line midpoint", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    SaveLimbsSettingsPreference();
                    EditorUtility.SetDirty(this);
                }

                GUILayout.Space(6f);
                if (limbsBoneMode)
                {
                    DrawLimbsBonePanel();
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var hasSelection = layerPreviewRenderer != null && layerPreviewRenderer.HasLimbSelection();
                    using (new EditorGUI.DisabledScope(!hasSelection))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                        {
                            var error = string.Empty;
                            if (layerPreviewRenderer != null &&
                                layerPreviewRenderer.ApplySelectedLimb(
                                    limbsUseRadiusCull,
                                    radiusCullRadius,
                                    true,
                                    radiusCullRemoveInnerVertices,
                                    out error))
                            {
                                ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Limb applied" : error));
                                Repaint();
                            }
                            else
                            {
                                ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                            }
                        }

                        if (GUILayout.Button("Cancel", GUILayout.Height(22f)))
                            layerPreviewRenderer?.CancelSelectedLimb();
                    }
                }
            }
        }
    }

    private void DrawLimbsBonePanel()
    {
        var renderer = layerPreviewRenderer;
        if (renderer == null)
            return;

        EditorGUILayout.HelpBox("Select a sprite and the middle bone. The guide crosses the bone midpoint. Vertices on the guide are weighted 100% to that bone.", MessageType.Info);
        if (renderer.TryGetSelectedLayer(out var layer))
            EditorGUILayout.LabelField("Sprite", layer.name);

        var bones = renderer.Bones;
        var boneLabels = new string[bones.Count + 1];
        boneLabels[0] = "Select a bone";
        for (var index = 0; index < bones.Count; index++)
            boneLabels[index + 1] = bones[index]?.name ?? "—";
        var selected = EditorGUILayout.Popup("Bone", renderer.SelectedBoneIndex + 1, boneLabels) - 1;
        if (selected != renderer.SelectedBoneIndex)
        {
            renderer.SelectBoneForExternalPanel(selected);
            Repaint();
        }

        if (selected < 0 || selected >= bones.Count || bones[selected] == null)
            return;

        var parent = bones[selected].parentId;
        if (parent < 0 || parent >= bones.Count || bones[parent] == null)
        {
            EditorGUILayout.HelpBox("Select a bone that has a parent.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Parent", bones[parent].name);
        var children = new List<int>();
        var childLabels = new List<string>();
        for (var index = 0; index < bones.Count; index++)
        {
            if (index == parent || index == selected || bones[index]?.parentId != selected)
                continue;
            children.Add(index);
            childLabels.Add(bones[index].name);
        }

        if (children.Count == 0)
        {
            EditorGUILayout.HelpBox("Select a bone that has a child.", MessageType.Info);
            return;
        }

        var child = Mathf.Max(0, children.IndexOf(limbsChildBoneIndex));
        if (children.Count == 1)
            EditorGUILayout.LabelField("Child", childLabels[0]);
        else
            child = EditorGUILayout.Popup("Child", child, childLabels.ToArray());
        limbsChildBoneIndex = children[child];

        using (new EditorGUI.DisabledScope(layer == null))
        {
            if (GUILayout.Button("Apply", GUILayout.Height(22f)))
            {
                if (renderer.ApplyLimbBoneWeights(selected, limbsChildBoneIndex, limbsUseRadiusCull, radiusCullRadius, radiusCullRemoveInnerVertices, out var message))
                    Repaint();
                ShowNotification(new GUIContent(message));
            }
        }
    }

    private void DrawBoobToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Boob", EditorStyles.boldLabel);
                if (layerPreviewRenderer == null)
                    return;

                var version = GUILayout.Toolbar(
                    layerPreviewRenderer.BoobToolVersion,
                    new[] { "Single", "Full body" });
                layerPreviewRenderer.BoobToolVersion = version;
                GUILayout.Space(4f);

                if (layerPreviewRenderer.BoobToolVersion == 0)
                    DrawBoobV1Panel();
                else
                    DrawBoobV2Panel();
            }
        }
    }

    private void DrawBoobV1Panel()
    {
        EditorGUILayout.HelpBox(
            "Click on sprite to set center point. Apply keeps contour vertices, builds rings to center and reassigns two-bone weights by distance.",
            MessageType.Info);
        var outline = EditorGUILayout.Slider("outline", layerPreviewRenderer.BoobOutline, 0f, 1f);
        layerPreviewRenderer.BoobOutline = outline;
        var stages = EditorGUILayout.IntSlider("stages", layerPreviewRenderer.BoobStages, 0, 16);
        layerPreviewRenderer.BoobStages = stages;

        GUILayout.Space(6f);
        var hasPoint = layerPreviewRenderer.HasBoobPoint();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!hasPoint))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                {
                    var error = string.Empty;
                    if (layerPreviewRenderer.ApplyBoob(out error))
                    {
                        ShowNotification(new GUIContent("Boob V1 applied"));
                        Repaint();
                    }
                    else
                    {
                        ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                    }
                }
            }
            if (GUILayout.Button("Clear Point", GUILayout.Height(22f)))
            {
                layerPreviewRenderer.ClearBoobPoint(recordUndo: true);
                Repaint();
            }
        }
    }

    private void DrawBoobV2Panel()
    {
        EditorGUILayout.HelpBox(
            "Choose exactly what to edit. Nipple targets can be clicked or dragged; contours are drawn while holding LMB. Contours close and smooth automatically.",
            MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawBoobV2Item(0, "Left Nipple");
            DrawBoobV2Item(1, "Right Nipple");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawBoobV2Item(2, "Left Contour");
            DrawBoobV2Item(3, "Right Contour");
        }
        EditorGUILayout.HelpBox(layerPreviewRenderer.BoobV2SelectionDescription, MessageType.None);
        layerPreviewRenderer.BoobV2ContourDensity = EditorGUILayout.IntSlider(
            "Density",
            layerPreviewRenderer.BoobV2ContourDensity,
            8,
            128);
        layerPreviewRenderer.BoobV2SmoothingPasses = EditorGUILayout.IntSlider(
            "Smoothing",
            layerPreviewRenderer.BoobV2SmoothingPasses,
            0,
            6);
        EditorGUILayout.LabelField(
            "Nipple targets",
            (layerPreviewRenderer.BoobV2HasLeftTarget ? "Left set" : "Left missing") + " / " +
            (layerPreviewRenderer.BoobV2HasRightTarget ? "Right set" : "Right missing"));
        EditorGUILayout.LabelField(
            "Contour points",
            layerPreviewRenderer.BoobV2FirstContourPointCount + " / " +
            layerPreviewRenderer.BoobV2SecondContourPointCount);

        GUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear Selected", GUILayout.Height(22f)))
            {
                layerPreviewRenderer.ClearSelectedBoobV2();
                Repaint();
            }
            if (GUILayout.Button("Clear All", GUILayout.Height(22f)))
            {
                layerPreviewRenderer.ClearBoobV2(recordUndo: true);
                Repaint();
            }
        }

        using (new EditorGUI.DisabledScope(!layerPreviewRenderer.CanApplyBoobV2()))
        {
            if (GUILayout.Button("Apply Manual FullBoobs", GUILayout.Height(26f)))
            {
                if (layerPreviewRenderer.ApplyBoobV2(out var result, out var error))
                {
                    ShowNotification(new GUIContent("Boob V2 applied"));
                    Repaint();
                }
                else
                {
                    ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                }
            }
        }
    }

    private void DrawBoobV2Item(int item, string label)
    {
        const float buttonSize = 20f;
        var assigned = layerPreviewRenderer.IsBoobV2ItemAssigned(item);
        var selected = layerPreviewRenderer.BoobV2Selection == item;
        var previousColor = GUI.backgroundColor;
        try
        {
            if (!assigned)
                GUI.backgroundColor = new Color(0.23f, 0.88f, 0.78f);
            if (GUILayout.Toggle(selected, label, EditorStyles.miniButton, GUILayout.Height(buttonSize), GUILayout.ExpandWidth(true)))
                layerPreviewRenderer.BoobV2Selection = item;
        }
        finally
        {
            GUI.backgroundColor = previousColor;
        }

        using (new EditorGUI.DisabledScope(!assigned))
        {
            if (GUILayout.Button(new GUIContent("×", "Clear " + label), EditorStyles.miniButton,
                    GUILayout.Width(buttonSize), GUILayout.Height(buttonSize)))
            {
                layerPreviewRenderer.ClearBoobV2Item(item);
                Repaint();
            }
        }
    }

    private void DrawJointToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Joint", EditorStyles.boldLabel);
                jointAlgorithmMode = GUILayout.Toolbar(
                    Mathf.Clamp(jointAlgorithmMode, 0, 1),
                    new[] { "V1", "V2" },
                    GUILayout.Height(22f));
                if (jointAlgorithmMode == 1)
                {
                    EditorGUILayout.HelpBox(
                        "V2: select a bone in the preview. Its current angle relative to the initial pose is shown to the left of the cursor.",
                        MessageType.Info);
                    if (jointV2FirstIterationWeightCurve == null || jointV2FirstIterationWeightCurve.length == 0)
                    {
                        jointV2FirstIterationWeightCurve = ReanimatorJointV2Defaults.CreateWeightCurve();
                    }
                    var isSpecialJoint = false;
                    var specialJointResolved = layerPreviewRenderer != null &&
                                               layerPreviewRenderer.TryGetJointV2SpecialConnection(
                                                   jointV2SpecialJointDistance,
                                                   out isSpecialJoint);
                    var jointTypeStyle = new GUIStyle(EditorStyles.boldLabel);
                    jointTypeStyle.normal.textColor = new Color(1f, 0.52f, 0.08f, 1f);
                    EditorGUILayout.LabelField(
                        specialJointResolved
                            ? isSpecialJoint ? "Special joint" : "Regular joint"
                            : "Joint unavailable",
                        jointTypeStyle);
                    jointV2FirstIterationWeightCurve = EditorGUILayout.CurveField(
                        "WeightCurve",
                        jointV2FirstIterationWeightCurve,
                        new Color(1f, 0.62f, 0.08f, 1f),
                        new Rect(0f, 0f, 1f, 1f));
                    if (jointV2SpecialWeightCurve == null || jointV2SpecialWeightCurve.length == 0)
                        jointV2SpecialWeightCurve = ReanimatorJointV2Defaults.CreateSpecialWeightCurve();
                    jointV2SpecialWeightCurve = EditorGUILayout.CurveField(
                        "SpecialWeightCurve",
                        jointV2SpecialWeightCurve,
                        new Color(0.18f, 1f, 0.28f, 1f),
                        new Rect(0f, 0f, 1f, 1f));
                    jointV2SpecialJointDistance = EditorGUILayout.Slider(
                        "SpecialJointDistance",
                        jointV2SpecialJointDistance,
                        0f,
                        1f);
                    jointV2BoneBaseOffset = EditorGUILayout.Slider(
                        "BoneBaseOffset",
                        jointV2BoneBaseOffset,
                        0f,
                        1f);
                    var bodyFound = false;
                    if (layerPreviewRenderer != null)
                        layerPreviewRenderer.TryGetJointV2BodySprite(out _, out bodyFound);
                    var currentSpriteName = string.Empty;
                    var currentSpriteFound = layerPreviewRenderer != null &&
                                             layerPreviewRenderer.TryGetJointV2CurrentBoneSprite(out currentSpriteName);
                    var bodyStatusStyle = new GUIStyle(EditorStyles.boldLabel);
                    bodyStatusStyle.normal.textColor = bodyFound
                        ? new Color(0.2f, 0.85f, 0.3f)
                        : new Color(1f, 0.25f, 0.2f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            currentSpriteFound ? currentSpriteName : "sprite not found",
                            EditorStyles.boldLabel,
                            GUILayout.ExpandWidth(false));
                        EditorGUILayout.LabelField(
                            bodyFound ? "body found" : "body not found",
                            bodyStatusStyle,
                            GUILayout.ExpandWidth(false));
                    }
                    using (new EditorGUI.DisabledScope(layerPreviewRenderer == null || !bodyFound || !currentSpriteFound))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                        {
                            var applied = layerPreviewRenderer.ApplyJointV2(
                                jointV2FirstIterationWeightCurve,
                                jointV2SpecialWeightCurve,
                                jointV2SpecialJointDistance,
                                jointV2BoneBaseOffset,
                                out var error);
                            ShowNotification(new GUIContent(applied
                                ? "Joint V2 applied"
                                : string.IsNullOrWhiteSpace(error) ? "Joint V2 apply failed" : error));
                            Repaint();
                        }
                    }
                    return;
                }

                EditorGUILayout.HelpBox(
                    "V1 is the original Joint algorithm. Click in preview to set joint center. Blue handles set sector edges; gray handles control transparent offsets.",
                    MessageType.Info);

                jointUseRadiusCull = EditorGUILayout.ToggleLeft("Use CircleClean", jointUseRadiusCull);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Radius", GUILayout.Width(44f));
                    radiusCullRadius = Mathf.Max(0.01f, GUILayout.HorizontalSlider(radiusCullRadius, 0.01f, 512f));
                    GUILayout.Label(radiusCullRadius.ToString("0.##"), GUILayout.Width(46f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("CPairOffset", GUILayout.Width(80f));
                    jointCPairOffset = Mathf.Max(0f, GUILayout.HorizontalSlider(jointCPairOffset, 0f, 128f));
                    jointCPairOffset = Mathf.Max(0f, EditorGUILayout.FloatField(jointCPairOffset, GUILayout.Width(52f)));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("LongPoints", GUILayout.Width(80f));
                    jointLongPoints = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(jointLongPoints, 0f, 10f)), 0, 10);
                    jointLongPoints = Mathf.Clamp(EditorGUILayout.IntField(jointLongPoints, GUILayout.Width(52f)), 0, 10);
                }

                GUILayout.Space(12f);
                var hasPoint = layerPreviewRenderer != null && layerPreviewRenderer.HasJointPoint();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasPoint))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                        {
                            var error = string.Empty;
                            var applied = layerPreviewRenderer != null &&
                                layerPreviewRenderer.ApplyJointStitch(
                                    radiusCullRadius,
                                    jointUseRadiusCull,
                                    false,
                                    true,
                                    jointCPairOffset,
                                    jointLongPoints,
                                    layerPreviewRenderer.JointStartLongPointsOffset,
                                    layerPreviewRenderer.JointEndLongPointsOffset,
                                    out error);
                            if (applied)
                            {
                                var message = string.IsNullOrEmpty(error) ? "Joint applied" : error;
                                ShowNotification(new GUIContent(message));
                                SetBottomToolMode(ReanimatorToolMode.WeightSlider);
                                Repaint();
                            }
                            else
                            {
                                ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                            }
                        }

                        if (GUILayout.Button("Clear Point", GUILayout.Height(22f)))
                            layerPreviewRenderer?.ClearJointPoint(recordUndo: true);
                    }
                }
            }
        }
    }

    private void DrawCreateEdgeToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Create Edge", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Click first vertex, then second vertex on the same sprite. Edge is created immediately on second click.",
                    MessageType.Info);
                var hasStart = layerPreviewRenderer != null && layerPreviewRenderer.HasCreateEdgeStartVertex();
                EditorGUILayout.LabelField("Start Vertex", hasStart ? "Selected" : "Not selected");

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasStart))
                    {
                        if (GUILayout.Button("Clear Start", GUILayout.Height(22f)))
                            layerPreviewRenderer?.CancelCreateEdgeTool();
                    }
                    if (GUILayout.Button("Edit Geometry", GUILayout.Height(22f)))
                        SetBottomToolMode(ReanimatorToolMode.EditGeometry);
                    if (GUILayout.Button("Weight Slider", GUILayout.Height(22f)))
                        SetBottomToolMode(ReanimatorToolMode.WeightSlider);
                }
            }
        }
    }

    private void DrawSplitEdgeToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Split Edge", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Click an existing mesh edge to insert a vertex on it. UVs use the same sprite transform, and weights are interpolated from the edge endpoints.",
                    MessageType.Info);

                if (GUILayout.Button("Edit Geometry", GUILayout.Height(22f)))
                    SetBottomToolMode(ReanimatorToolMode.EditGeometry);
            }
        }
    }

    private void DrawRadiusCullToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("CircleClean", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Click in preview to set center point. Red circle shows affected area. Apply removes vertices and rebuilds mesh.",
                    MessageType.Info);

                var miniToggleStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                using (new EditorGUILayout.HorizontalScope())
                {
                    radiusCullRemoveContourVertices = GUILayout.Toggle(
                        radiusCullRemoveContourVertices,
                        new GUIContent("CW", "Remove contour vertices"),
                        miniToggleStyle,
                        GUILayout.Width(34f),
                        GUILayout.Height(18f));

                    radiusCullRemoveInnerVertices = GUILayout.Toggle(
                        radiusCullRemoveInnerVertices,
                        new GUIContent("IW", "Remove inner vertices"),
                        miniToggleStyle,
                        GUILayout.Width(34f),
                        GUILayout.Height(18f));

                    radiusCullRadius = Mathf.Max(0.01f, GUILayout.HorizontalSlider(radiusCullRadius, 0.01f, 512f));
                }

                var hasPoint = layerPreviewRenderer != null && layerPreviewRenderer.HasRadiusCullPoint();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasPoint))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(22f)))
                        {
                            if (layerPreviewRenderer.ApplyRadiusCull(radiusCullRadius, radiusCullRemoveContourVertices, radiusCullRemoveInnerVertices, out var error))
                            {
                                ShowNotification(new GUIContent("CircleClean applied"));
                                Repaint();
                            }
                            else
                            {
                                ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Apply failed" : error));
                            }
                        }

                        if (GUILayout.Button("Clear Point", GUILayout.Height(22f)))
                            layerPreviewRenderer?.ClearRadiusCullPoint(recordUndo: true);
                    }
                }
            }
        }
    }

    private void DrawAutoGeometryToolPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Geometry", EditorStyles.boldLabel);
                autoGeometryMode = (ReanimatorAutoGeometryMode)EditorGUILayout.Popup("Mode", (int)autoGeometryMode, AutoGeometryModeLabels);
                GUILayout.Space(4f);

                if (autoGeometryMode == ReanimatorAutoGeometryMode.QuadNet)
                {
                    DrawAutoGeometrySliderFieldDouble("Density", ref autoGeometryQuadDensity, 0.01d, 5d);
                    autoGeometryQuadRotationDegrees = EditorGUILayout.Slider(
                        "Rotation",
                        ReanimatorQuadNetAlgorithms.NormalizeQuadNetRotationDegrees(autoGeometryQuadRotationDegrees),
                        0f,
                        360f);
                }
                else if (autoGeometryMode == ReanimatorAutoGeometryMode.Reanimax)
                {
                    autoGeometryReanimaxMode = (ReanimatorReanimaxGeometryMode)EditorGUILayout.Popup(
                        "Reanimax",
                        (int)autoGeometryReanimaxMode,
                        ReanimaxGeometryModeLabels);
                    EditorGUILayout.HelpBox(
                        autoGeometryReanimaxMode == ReanimatorReanimaxGeometryMode.Hard
                            ? "Reanimax Hard is selected. Algorithm steps are not implemented yet."
                            : "Reanimax Soft is reserved for later.",
                        MessageType.Info);
                    if (autoGeometryReanimaxMode == ReanimatorReanimaxGeometryMode.Hard)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(4f);
                            autoGeometryReanimaxHardMaskPreview = EditorGUILayout.ToggleLeft(
                                "Mask",
                                autoGeometryReanimaxHardMaskPreview,
                                GUILayout.Width(90f));
                            autoGeometryReanimaxHardEdgePointsPreview = EditorGUILayout.ToggleLeft(
                                "Edge Pts",
                                autoGeometryReanimaxHardEdgePointsPreview,
                                GUILayout.Width(90f));
                            GUILayout.FlexibleSpace();
                        }
                        using (new EditorGUI.DisabledScope(!autoGeometryReanimaxHardMaskPreview && !autoGeometryReanimaxHardEdgePointsPreview))
                        {
                            autoGeometryReanimaxHardMaskAlphaThreshold = EditorGUILayout.Slider(
                                "Alpha",
                                Mathf.Clamp01(autoGeometryReanimaxHardMaskAlphaThreshold),
                                0f,
                                1f);
                        }
                        using (new EditorGUI.DisabledScope(!autoGeometryReanimaxHardEdgePointsPreview))
                        {
                            autoGeometryReanimaxHardEdgePositionFilterPixels = EditorGUILayout.IntSlider(
                                "Pos Px",
                                Mathf.Clamp(autoGeometryReanimaxHardEdgePositionFilterPixels, 1, 20),
                                1,
                                20);
                            autoGeometryReanimaxHardEdgeMinTurnDegrees = EditorGUILayout.Slider(
                                "Min Turn",
                                Mathf.Clamp(autoGeometryReanimaxHardEdgeMinTurnDegrees, 0f, 45f),
                                0f,
                                45f);
                            autoGeometryReanimaxHardEdgePostPositionFilterPixels = EditorGUILayout.IntSlider(
                                "Post Pos",
                                Mathf.Clamp(autoGeometryReanimaxHardEdgePostPositionFilterPixels, 1, 20),
                                1,
                                20);
                            autoGeometryReanimaxHardEdgeMinDistancePixels = EditorGUILayout.Slider(
                                "Min Dist",
                                Mathf.Clamp(autoGeometryReanimaxHardEdgeMinDistancePixels, 0f, 20f),
                                0f,
                                20f);
                            autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = EditorGUILayout.IntSlider(
                                "Mid Pos",
                                Mathf.Clamp(autoGeometryReanimaxHardNormalMidpointPositionFilterPixels, 1, 50),
                                1,
                                50);
                            autoGeometryReanimaxHardNormalInsetPixels = EditorGUILayout.Slider(
                                "Inset",
                                Mathf.Clamp(autoGeometryReanimaxHardNormalInsetPixels, 0f, 100f),
                                0f,
                                100f);
                            autoGeometryReanimaxHardNormalInsetPositionFilterPixels = EditorGUILayout.IntSlider(
                                "Inset Pos",
                                Mathf.Clamp(autoGeometryReanimaxHardNormalInsetPositionFilterPixels, 1, 50),
                                1,
                                50);
                            if (GUILayout.Button("Reset Edge Defaults", GUILayout.Height(20f)))
                            {
                                ResetReanimaxHardEdgeDefaults();
                                Repaint();
                            }
                        }
                    }
                }
                else
                {
                    DrawAutoGeometrySliderField("Outline Detail", ref autoGeometryOutlineDetail, 0, 100);
                    DrawAutoGeometrySliderField("Alpha Tolerance", ref autoGeometryAlphaTolerance, 0, 254);
                    DrawAutoGeometrySliderField("Subdivide", ref autoGeometrySubdivide, 0, 100);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(4f);
                        autoGeometryGenerateWeights = EditorGUILayout.ToggleLeft("Weights", autoGeometryGenerateWeights, GUILayout.Width(90f));
                        GUILayout.FlexibleSpace();
                    }
                }

                GUILayout.Space(6f);
                var path = GetSelectedAssetPath();
                var canGenerate = layerPreviewRenderer != null &&
                                  !string.IsNullOrWhiteSpace(path) &&
                                  IsPsdPath(path);
                using (new EditorGUI.DisabledScope(!canGenerate))
                {
                    if (GUILayout.Button("Generate For Selected", GUILayout.Height(22f)))
                        GenerateAutoGeometryForSelectedFromUi();
                }
            }
        }
    }

    private bool GenerateAutoGeometryForSelectedFromUi()
    {
        var path = GetSelectedAssetPath();
        if (layerPreviewRenderer == null || string.IsNullOrWhiteSpace(path) || !IsPsdPath(path))
        {
            ShowNotification(new GUIContent("Select PSD sprite"));
            return false;
        }

        if (layerPreviewRenderer.GenerateAutoGeometryForSelected(
                path,
                autoGeometryOutlineDetail,
                autoGeometryAlphaTolerance,
                autoGeometrySubdivide,
                autoGeometryMode == ReanimatorAutoGeometryMode.QuadNet ? true : autoGeometryGenerateWeights,
                autoGeometryMode,
                autoGeometryReanimaxMode,
                autoGeometryQuadDensity,
                autoGeometryQuadRotationDegrees,
                autoGeometryReanimaxHardMaskAlphaThreshold,
                autoGeometryReanimaxHardEdgePositionFilterPixels,
                autoGeometryReanimaxHardEdgeMinTurnDegrees,
                autoGeometryReanimaxHardEdgePostPositionFilterPixels,
                autoGeometryReanimaxHardEdgeMinDistancePixels,
                autoGeometryReanimaxHardNormalMidpointPositionFilterPixels,
                autoGeometryReanimaxHardNormalInsetPixels,
                autoGeometryReanimaxHardNormalInsetPositionFilterPixels,
                out var error))
        {
            ShowNotification(new GUIContent("Geometry generated"));
            Repaint();
            return true;
        }

        ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? "Generate failed" : error));
        return false;
    }

    private static void DrawAutoGeometrySliderField(string label, ref int value, int minValue, int maxValue)
    {
        value = Mathf.Clamp(value, minValue, maxValue);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(92f));
            var newValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, minValue, maxValue));
            newValue = Mathf.Clamp(newValue, minValue, maxValue);
            newValue = EditorGUILayout.IntField(newValue, GUILayout.Width(40f));
            value = Mathf.Clamp(newValue, minValue, maxValue);
        }
    }

    private static void DrawAutoGeometrySliderFieldDouble(string label, ref double value, double minValue, double maxValue)
    {
        value = Math.Max(minValue, Math.Min(maxValue, value));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(92f));
            var slider = GUILayout.HorizontalSlider((float)value, (float)minValue, (float)maxValue);
            var newValue = EditorGUILayout.DoubleField(Math.Round(slider, 2, MidpointRounding.AwayFromZero), GUILayout.Width(48f));
            value = Math.Max(minValue, Math.Min(maxValue, newValue));
            value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
#endif
