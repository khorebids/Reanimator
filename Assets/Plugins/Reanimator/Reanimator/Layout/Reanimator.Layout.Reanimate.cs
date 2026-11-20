#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private const int ReanimateShouldersStep = 1;
    private const int ReanimateFaceStep = 2;
    private const int ReanimateChestStep = 3;
    private const int ReanimateHipsStep = 8;
    private const int ReanimateOthersStep = 9;
    private const int ReanimateFinalStep = 10;
    [SerializeField] private int reanimateStepLayoutVersion;
    private static readonly int[] ReanimateChestItems = { 2, 3, 0, 1 };
    private static readonly string[] ReanimateChestLabels = { "Left Nipple", "Right Nipple", "Left Contour", "Right Contour" };
    private static readonly string[] ReanimateStepNames =
    {
        "Full PSD", "Shoulders", "Face", "Chest", "Left Arm", "Right Arm", "Left Leg", "Right Leg", "Belly Sides", "Others", "Reanimate"
    };
    private static readonly string[] ReanimateStepInstructions =
    {
        "PLACE THE SKELETON",
        "OUTLINE THE SHOULDER ATTACHMENT: LEFT ARM, THEN RIGHT ARM",
        "OUTLINE THE MOUTH, LIPS AND NOSE",
        "OUTLINE THE CHEST AND BELLY",
        "SET SOFT BODY LIMITS FOR THE LEFT ARM",
        "SET SOFT BODY LIMITS FOR THE RIGHT ARM",
        "SET SOFT BODY LIMITS FOR THE LEFT LEG",
        "SET SOFT BODY LIMITS FOR THE RIGHT LEG",
        "SET SOFT BODY LIMITS FOR THE BELLY SIDES",
        "SET UP THE REMAINING SPRITES",
        "REANIMATE"
    };
    private static GUIStyle reanimateStepInstructionStyle;
    private const int ReanimateChallengeSeconds = 300;
    [SerializeField] private string reanimateChallengePath;
    [SerializeField] private double reanimateChallengeStart = -1;
    [SerializeField] private double reanimateChallengeFinish = -1;
    [SerializeField] private bool reanimateChallengeSucceeded;
    private int reanimateLastTimerSecond = -1;

    private static readonly string[][] ReanimateStepSpriteNames =
    {
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { "face", "head" },
        new[] { "body", "chest", "breasts", "breast", "bust", "torso" },
        new[] { "larm", "leftarm", "arml", "armleft" },
        new[] { "rarm", "rightarm", "armr", "armright" },
        new[] { "lleg", "leftleg", "legl", "legleft" },
        new[] { "rleg", "rightleg", "legr", "legright" },
        new[] { "body", "torso" },
        Array.Empty<string>(),
        Array.Empty<string>()
    };

    private readonly PreviewNavigator reanimateNavigator = new PreviewNavigator();
    private static readonly ReanimatorSkeletronModule reanimateSkeletron = new();
    private int reanimateStep;
    private string reanimateCanvasPath;
    private PsdLayerPreviewRenderer.LayerInfo reanimateDiagnosticLayer;
    [NonSerialized] private string reanimateRestartPath;
    private PsdLayerPreviewRenderer.LayerInfo reanimateFaceLayer;
    private PsdLayerPreviewRenderer.LayerInfo reanimateChestLayer;
    private int reanimateChestItem = -1;
    private bool IsReanimateRestartPending => !string.IsNullOrEmpty(reanimateRestartPath);

    private void DrawReanimateWorkspace()
    {
        if (Event.current.type is EventType.MouseMove or EventType.MouseLeaveWindow)
            Repaint();
        layerPreviewRenderer ??= new PsdLayerPreviewRenderer();
        var path = GetSelectedAssetPath();
        if (!string.Equals(path, reanimateCanvasPath, StringComparison.OrdinalIgnoreCase))
        {
            LeaveReanimateFaceStep();
            LeaveReanimateChestStep();
            LeaveReanimateLimbStep();
            LeaveReanimateOthers();
            SetReanimateShoulderSide(0);
            layerPreviewRenderer.ClearSoftContourBellyContour();
            reanimateCanvasPath = path;
            reanimateDiagnosticLayer = null;
            reanimateNavigator.Reset();
        }
        var hasPsd = IsPsdImporterAsset(path);
        var ready = hasPsd && layerPreviewRenderer.IsPreviewCacheReady(path);
        if (hasPsd && !ready)
            layerPreviewRenderer.RequestPreviewCacheRefresh(path);
        if (!ready)
            reanimateDiagnosticLayer = null;

        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            Rect canvas;
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                DrawReanimateChallengeTimer(path);
                reanimateStepInstructionStyle ??= new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    wordWrap = true
                };
                EditorGUILayout.LabelField(ReanimateStepInstructions[reanimateStep],
                    reanimateStepInstructionStyle, GUILayout.MinHeight(38f));
                canvas = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            if (Event.current.type == EventType.MouseDown && canvas.Contains(Event.current.mousePosition))
                Focus();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(180f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("Steps", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("A  Previous     D  Next", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(!hasPsd || IsReanimateRestartPending || reanimateRunning))
                {
                    if (DrawReanimateIconButton("0. Restart", "Restore Pose") && ConfirmPsdReset(path, true))
                    {
                        StartReanimateChallenge(path, EditorApplication.timeSinceStartup);
                        reanimateShowIdleMascot = true;
                        reanimateIdleMascotError = false;
                        reanimateIdleMascotSuccess = false;
                        reanimateRestartPath = path;
                        EditorApplication.delayCall += RunReanimateRestart;
                    }
                }
                for (var i = 0; i < ReanimateStepNames.Length; i++)
                {
                    if (i > FirstReanimateLimbStep && i < ReanimateOthersStep)
                        continue;
                    var grouped = i == FirstReanimateLimbStep;
                    var active = grouped ? reanimateStep >= FirstReanimateLimbStep && reanimateStep < ReanimateOthersStep : reanimateStep == i;
                    var number = i < ReanimateOthersStep ? i + 1 : i - ReanimateOthersStep + 6;
                    var label = grouped ? "Soft Contour" : ReanimateStepNames[i];
                    if (DrawReanimateIconButton($"{number}. {label}", grouped ? "SC" : label, active, true))
                    {
                        ResetSuccessfulMascotOnNavigation();
                        if(!active)SetReanimateStep(i);
                    }
                }

                var drawOthers = ready && reanimateStep == ReanimateOthersStep;
                var drawShoulders = ready && reanimateStep == ReanimateShouldersStep;
                var focus = drawShoulders ? DrawReanimateShouldersSettings() : drawOthers ? DrawReanimateOthersSettings(path) :
                    ready ? FindReanimateStepSprite(layerPreviewRenderer.Layers, reanimateStep) : null;
                if(ready && reanimateStep==ReanimateChestStep && focus==null && layerPreviewRenderer.ReanimateChestMode==2)
                    focus=layerPreviewRenderer.FindSeparatedChestSprite(0);
                if (ready && reanimateStep > 0 && reanimateStep < ReanimateOthersStep && focus == null)
                    EditorGUILayout.HelpBox($"No sprite found for {ReanimateStepNames[reanimateStep]}.", MessageType.Info);
                var drawFace = reanimateStep == ReanimateFaceStep && focus != null;
                var drawSkeleton = ready && reanimateStep == 0;
                var drawChest = ready && reanimateStep == ReanimateChestStep;
                var drawLimb = reanimateStep >= FirstReanimateLimbStep && reanimateStep < ReanimateOthersStep && focus != null;
                if (drawFace)
                {
                    DrawReanimateFaceSteps(focus);
                }
                var bellyLayer = drawChest ? FindReanimateStepSprite(layerPreviewRenderer.Layers,ReanimateChestStep) : null;
                if (drawSkeleton)
                    reanimateSkeletron.DrawPlacementSettings(Api, false);
                if (drawChest)
                {
                    DrawReanimateChestSteps(focus);
                    DrawReanimateBellySettings(bellyLayer);
                    DrawReanimateAssSettings();
                }
                var drawBelly = bellyLayer != null && layerPreviewRenderer.IsDrawingSoftContourBelly;
                if (drawLimb)
                    DrawReanimateLimbSteps();
                if (reanimateStep == ReanimateFinalStep)
                    DrawReanimateFinalStep(path, ready);
                if (reanimateDiagnosticLayer != null &&
                    GUILayout.Button("Back to Step", EditorStyles.miniButton))
                {
                    reanimateDiagnosticLayer = null;
                    Repaint();
                }
                GUILayout.FlexibleSpace();
                DrawNamingIndicatorFooter();

                var eventType = Event.current.type;
                if ((eventType != EventType.Repaint && !drawFace && !drawChest && !drawLimb && !drawSkeleton && !drawOthers && !drawShoulders) ||
                    eventType == EventType.Layout || eventType == EventType.Used ||
                    canvas.width <= 0f || canvas.height <= 0f)
                    return;
                using (new GUI.GroupScope(canvas))
                {
                    var viewport = new Rect(Vector2.zero, canvas.size);
                    if (eventType == EventType.Repaint)
                        DrawChecker(viewport, 16f, new Color(0.32f, 0.32f, 0.32f), new Color(0.22f, 0.22f, 0.22f));
                    if (ready)
                    {
                        var pauseCamera = (drawFace && layerPreviewRenderer.IsFaceMouthStrokeActive) ||
                                          (drawChest && layerPreviewRenderer.IsReanimateChestStrokeActive) ||
                                          (drawLimb && reanimateLimbStrokeControl != 0) ||
                                          (drawSkeleton && ReanimatorSkeletronModule.IsPlacementDragging(Api)) ||
                                          (drawBelly && layerPreviewRenderer.IsSoftContourBellyStrokeActive) ||
                                          (drawOthers && reanimateOtherStrokeControl != 0);
                        pauseCamera |= drawShoulders && reanimateShoulderControl != 0;
                        pauseCamera |= drawChest && layerPreviewRenderer.IsAssStrokeActive;
                        var diagnosing = reanimateDiagnosticLayer != null;
                        if (layerPreviewRenderer.DrawReanimateCanvas(path, viewport, reanimateNavigator,
                                reanimateDiagnosticLayer ?? focus, !diagnosing && pauseCamera,
                                !diagnosing && (drawChest || drawLimb), out var coordinates,
                                drawSkeleton && !diagnosing ? ReanimatorSkeletronModule.GetHiddenLayers(Api) : null,
                                drawBelly && !diagnosing ? bellyLayer : null, diagnosing || drawOthers || drawShoulders,
                                drawChest && !diagnosing?layerPreviewRenderer.GetSeparatedChestPreviewLayers():null))
                            Repaint();
                        if (diagnosing)
                            return;
                        if (drawShoulders && focus != null && coordinates != null)
                            DrawOrHandleReanimateShoulderCanvas(viewport, coordinates);
                        if ((drawSkeleton || drawChest) && coordinates != null)
                        {
                            var context = new ReanimatorPreviewContext(viewport, coordinates, layerPreviewRenderer.SortedLayers, null);
                            if (eventType == EventType.Repaint)
                            {
                                if (drawSkeleton)
                                    reanimateSkeletron.DrawPreviewOverlay(Api, context);
                                if (drawChest)
                                    layerPreviewRenderer.DrawSoftContourBelly(coordinates);
                            }
                            else if (drawBelly)
                            {
                                if (layerPreviewRenderer.HandleSoftContourBellyInput(context))
                                    Repaint();
                            }
                            else if (drawSkeleton)
                                reanimateSkeletron.HandlePreviewInput(Api, context);
                        }
                        if (drawFace && coordinates != null)
                            DrawOrHandleReanimateFaceCanvas(viewport, coordinates, focus, eventType);
                        if (drawChest && coordinates != null && eventType == EventType.Repaint)
                            DrawOrHandleReanimateChestCanvas(viewport,coordinates,focus,eventType);
                        if (drawChest && !drawBelly && coordinates != null && eventType != EventType.Repaint)
                            DrawOrHandleReanimateChestCanvas(viewport, coordinates, focus, eventType);
                        if (drawLimb && coordinates != null)
                        {
                            if (reanimateStep == ReanimateHipsStep && eventType == EventType.Repaint)
                                DrawReanimateHipsGuide(coordinates);
                            DrawOrHandleReanimateLimbCanvas(viewport, coordinates);
                        }
                        if (drawOthers && focus != null && coordinates != null)
                            DrawOrHandleReanimateOtherCanvas(viewport, coordinates);
                    }
                    else
                        GUI.Label(viewport, hasPsd ? "Loading PSD…" : "Select a PSD in the Project window.", EditorStyles.centeredGreyMiniLabel);
                }
            }
        }
    }

    private void DrawReanimateBellySettings(PsdLayerPreviewRenderer.LayerInfo body)
    {
        var renderer = layerPreviewRenderer;
        if(GUILayout.Button("No Belly"))
        {
            renderer.DisableReanimateBelly();Repaint();
        }
        using (new EditorGUI.DisabledScope(body == null || !ReanimatorSkeletronModule.HasPlacement(Api)))
        {
            var drawing = renderer.IsDrawingSoftContourBelly;
            var label = drawing ? "Cancel Belly" : renderer.HasSoftContourBellyContour ? "Redraw Belly" : "Draw Belly";
            if (!GUILayout.Button(label))
                return;
            renderer.CancelBoobV2Interaction();
            reanimateEditingAss=false;renderer.CancelAssStroke();
            renderer.CancelSoftContourBellyStroke();
            renderer.IsDrawingSoftContourBelly = !drawing;
            Repaint();
        }
    }

    private void StartReanimateChallenge(string path, double now)
    {
        reanimateChallengePath = path;
        reanimateChallengeStart = now;
        reanimateChallengeFinish = -1;
        reanimateChallengeSucceeded = false;
        reanimateLastTimerSecond = -1;
    }

    private void FinishReanimateChallenge(string path, double now, bool succeeded)
    {
        if (reanimateChallengeStart >= 0 && reanimateChallengeFinish < 0 &&
            string.Equals(path, reanimateChallengePath, StringComparison.OrdinalIgnoreCase))
        {
            reanimateChallengeFinish = now;
            reanimateChallengeSucceeded = succeeded;
        }
    }

    private void DrawReanimateChallengeTimer(string path)
    {
        var now = EditorApplication.timeSinceStartup;
        var started = reanimateChallengeStart >= 0 && reanimateChallengeStart <= now &&
                      !string.IsNullOrEmpty(path) &&
                      string.Equals(path, reanimateChallengePath, StringComparison.OrdinalIgnoreCase);
        var finish = reanimateChallengeFinish >= 0 ? reanimateChallengeFinish : now;
        var elapsed = started ? Mathf.Max(0, (int)(finish - reanimateChallengeStart)) : 0;
        var previousColor = GUI.contentColor;
        if (elapsed >= ReanimateChallengeSeconds ||
            (started && reanimateChallengeFinish >= 0 && !reanimateChallengeSucceeded))
            GUI.contentColor = new Color(1f, 0.42f, 0.35f);
        else if (started && reanimateChallengeFinish >= 0)
            GUI.contentColor = new Color(0.55f, 1f, 0.60f);
        GUILayout.Label($"{FormatReanimateElapsed(elapsed)} / 05:00", EditorStyles.miniBoldLabel,
            GUILayout.Width(92f));
        GUI.contentColor = previousColor;
    }

    private static string FormatReanimateElapsed(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

    private void TickReanimateChallengeTimer()
    {
        if (previewMode != PreviewMode.Reanimate || reanimateChallengeStart < 0 ||
            reanimateChallengeFinish >= 0 ||
            !string.Equals(activeAssetPath, reanimateChallengePath, StringComparison.OrdinalIgnoreCase))
            return;
        var second = (int)(EditorApplication.timeSinceStartup - reanimateChallengeStart);
        if (second == reanimateLastTimerSecond)
            return;
        reanimateLastTimerSecond = second;
        Repaint();
    }

    private void HandleReanimateStepKeys(Event input)
    {
        // Localized input can omit the physical key code on KeyDown; KeyUp retains it.
        if (input == null || input.rawType != EventType.KeyUp || input.alt || input.control || input.command ||
            IsReanimateRestartPending)
            return;
        var direction = input.keyCode switch
        {
            KeyCode.A => -1,
            KeyCode.D => 1,
            _ => 0
        };
        if (direction == 0)
            return;
        NavigateReanimateStep(direction);
        if (input.type != EventType.Used)
            input.Use();
    }

    private void SetReanimateStep(int step)
    {
        step = Mathf.Clamp(step, 0, ReanimateStepNames.Length - 1);
        if (step == reanimateStep)
            return;
        if(step==ReanimateShouldersStep && !ReanimatorSkeletronModule.HasPlacement(Api))
            ShowNotification(new GUIContent("Place the skeleton on the Full PSD step first."));
        reanimateDiagnosticLayer = null;
        if (reanimateStep == ReanimateShouldersStep) CancelReanimateShoulderStroke();
        if (step == ReanimateShouldersStep) SetReanimateShoulderSide(reanimateStep == ReanimateFaceStep ? 1 : 0);
        if (reanimateStep == 0)
        {
            ReanimatorSkeletronModule.CancelPlacement(layerPreviewRenderer);
            layerPreviewRenderer?.CancelSoftContourBellyStroke();
        }
        if (reanimateStep == ReanimateFaceStep)
            LeaveReanimateFaceStep();
        if (reanimateStep == ReanimateChestStep)
        {
            layerPreviewRenderer?.CancelAssStroke();reanimateEditingAss=false;
            LeaveReanimateChestStep();
        }
        if (reanimateStep >= FirstReanimateLimbStep && reanimateStep < ReanimateOthersStep)
            LeaveReanimateLimbStep();
        if (reanimateStep == ReanimateOthersStep)
            LeaveReanimateOthers();
        if (step == ReanimateOthersStep)
            reanimateOtherIndex = 0;
        reanimateStep = step;
        Repaint();
    }

    private void LeaveReanimateChestStep()
    {
        layerPreviewRenderer?.CancelSoftContourBellyStroke();
        if (reanimateChestLayer == null)
            return;
        layerPreviewRenderer?.CancelBoobV2Interaction();
        reanimateChestLayer = null;
        reanimateChestItem = -1;
    }

    private void DrawReanimateChestSteps(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (!ReferenceEquals(reanimateChestLayer, layer))
        {
            reanimateChestLayer = layer;
            BeginNextReanimateChestItem();
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chest Contours & Nipples", EditorStyles.boldLabel);
        var mode=GUILayout.Toolbar(layerPreviewRenderer.ReanimateChestMode,new[]{"Нет груди","Тело","Отделена"});
        if(mode!=layerPreviewRenderer.ReanimateChestMode)
        {
            layerPreviewRenderer.SetReanimateChestMode(mode);
            reanimateChestItem=-1;if(mode!=0)BeginNextReanimateChestItem();Repaint();
        }
        var modeError=layerPreviewRenderer.GetReanimateChestModeError();
        if(modeError!=null){EditorGUILayout.HelpBox(modeError,MessageType.Error);return;}
        if(layerPreviewRenderer.ReanimateChestMode==0)return;
        if(layerPreviewRenderer.ReanimateChestMode==2)
            EditorGUILayout.LabelField("l_boob  /  r_boob",EditorStyles.miniBoldLabel);
        foreach (var item in ReanimateChestItems)
        {
            var active = reanimateChestItem == item;
            var assigned = layerPreviewRenderer.IsBoobV2ItemAssigned(item);
            var label = ReanimateChestLabels[item];
            if(layerPreviewRenderer.ReanimateChestMode==2)label+=" ("+(item==0||item==2?"l_boob":"r_boob")+")";
            if (active)
                label = (item < 2 ? "Placing " : "Drawing ") + label;
            else if (assigned)
                label = (item < 2 ? "Move " : "Redraw ") + label;
            if (GUILayout.Toggle(active, label, EditorStyles.miniButton) && !active)
            {
                layerPreviewRenderer.CancelSoftContourBellyStroke();
                reanimateEditingAss=false;layerPreviewRenderer.CancelAssStroke();
                layerPreviewRenderer.CancelBoobV2Interaction();
                reanimateChestItem = item;
                Repaint();
            }
        }
    }

    private void BeginNextReanimateChestItem()
    {
        reanimateChestItem = -1;
        if(layerPreviewRenderer.ReanimateChestMode==0)return;
        foreach (var item in ReanimateChestItems)
        {
            if (layerPreviewRenderer.IsBoobV2ItemAssigned(item))
                continue;
            reanimateChestItem = item;
            break;
        }
    }

    private void DrawOrHandleReanimateChestCanvas(Rect viewport, PsdCoordinateSpace coordinates,
        PsdLayerPreviewRenderer.LayerInfo layer, EventType eventType)
    {
        if(reanimateEditingAss)
        {
            if(eventType==EventType.Repaint)layerPreviewRenderer.DrawAssPreview(coordinates.DocumentToView);
            else if(layerPreviewRenderer.HandleAssInput(viewport,coordinates,layer))Repaint();
            return;
        }
        if(layerPreviewRenderer.ReanimateChestMode==0 || layerPreviewRenderer.GetReanimateChestModeError()!=null)
        {
            if(eventType==EventType.Repaint)layerPreviewRenderer.DrawAssPreview(coordinates.DocumentToView);
            return;
        }
        if (eventType == EventType.Repaint)
        {
            layerPreviewRenderer.DrawReanimateChestContours(coordinates);
            layerPreviewRenderer.DrawAssPreview(coordinates.DocumentToView);
            return;
        }
        if (reanimateChestItem < 0)
            return;
        layerPreviewRenderer.BoobV2Selection = reanimateChestItem;
        var target=layerPreviewRenderer.ResolveReanimateChestInputLayer(layer,reanimateChestItem);
        if (target==null || !layerPreviewRenderer.HandleReanimateChestInput(viewport, coordinates, target))
            return;
        if (eventType == EventType.MouseUp)
        {
            var placingNipple = reanimateChestItem < 2;
            BeginNextReanimateChestItem();
            if (placingNipple && reanimateChestItem < 0 && !layerPreviewRenderer.HasSoftContourBellyContour)
                layerPreviewRenderer.IsDrawingSoftContourBelly = true;
        }
        Repaint();
    }

    private void LeaveReanimateFaceStep()
    {
        if (reanimateFaceLayer == null)
            return;
        layerPreviewRenderer?.CancelFaceMouthStroke();
        reanimateFaceLayer = null;
    }

    private void DrawReanimateFaceSteps(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        if (!ReferenceEquals(reanimateFaceLayer, layer))
        {
            reanimateFaceLayer = layer;
            BeginNextReanimateFaceContour(layer);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Face Contours", EditorStyles.boldLabel);
        if(GUILayout.Button("No Mouth")){layerPreviewRenderer.DisableReanimateMouth();Repaint();}
        var labels = PsdLayerPreviewRenderer.FaceMouthContourLabels;
        for (var i = 0; i < labels.Length; i++)
        {
            var drawing = layerPreviewRenderer.FaceMouthDrawingContour == i;
            var verb = drawing ? "Drawing " : layerPreviewRenderer.HasFaceMouthContour(layer, i) ? "Redraw " : "Draw ";
            if (GUILayout.Toggle(drawing, verb + labels[i], EditorStyles.miniButton) && !drawing)
            {
                layerPreviewRenderer.CancelFaceMouthStroke();
                layerPreviewRenderer.BeginFaceMouthContour(layer, i);
                Repaint();
            }
        }
    }

    private void BeginNextReanimateFaceContour(PsdLayerPreviewRenderer.LayerInfo layer)
    {
        for (var i = 0; i < PsdLayerPreviewRenderer.FaceMouthContourLabels.Length; i++)
        {
            if (layerPreviewRenderer.HasFaceMouthContour(layer, i))
                continue;
            layerPreviewRenderer.BeginFaceMouthContour(layer, i);
            break;
        }
    }

    private void DrawOrHandleReanimateFaceCanvas(Rect viewport, PsdCoordinateSpace coordinates,
        PsdLayerPreviewRenderer.LayerInfo layer, EventType eventType)
    {
        if (eventType == EventType.Repaint)
        {
            layerPreviewRenderer.DrawFaceMouthContours(coordinates, layer, showNoseOuter: false);
            return;
        }

        var context = new ReanimatorPreviewContext(viewport, coordinates, layerPreviewRenderer.Layers, null);
        if (!layerPreviewRenderer.HandleFaceMouthInput(context, out var error))
            return;
        if (eventType == EventType.MouseUp)
            BeginNextReanimateFaceContour(layer);
        if (error != null)
            ShowNotification(new GUIContent(error));
        Repaint();
    }

    private void RunReanimateRestart()
    {
        var path = reanimateRestartPath;
        try
        {
            if (this == null || string.IsNullOrWhiteSpace(path))
                return;
            if (!TryRestartReanimateAsset(path, out var count, out var error))
            {
                FinishReanimateChallenge(path, EditorApplication.timeSinceStartup, false);
                ReanimatorLog.Error($"[Reanimator] Restart failed: {error}");
                ShowNotification(new GUIContent(error));
                return;
            }
            SetReanimateStep(0);
            reanimateShowIdleMascot = true;
            reanimateIdleMascotError = false;
            reanimateIdleMascotSuccess = false;
            ShowNotification(new GUIContent(count > 0 ? $"Generated geometry: {count}" : "No missing geometry"));
        }
        finally
        {
            reanimateRestartPath = null;
            if (this != null)
                Repaint();
        }
    }

    private bool TryRestartReanimateAsset(string path, out int count, out string error)
    {
        count = 0;
        SaveReanimateDraftCache(beforeRun: true);
        if (!TryKillPsdData(path, out error))
            return false;

        layerPreviewRenderer ??= new PsdLayerPreviewRenderer();
        layerPreviewRenderer.ClearFaceMouthContours();
        layerPreviewRenderer.ClearBoobV2();
        layerPreviewRenderer.SetReanimateChestMode(1);
        layerPreviewRenderer.ClearAss(false);reanimateEditingAss=false;
        layerPreviewRenderer.ClearSoftContourBellyContour();
        ReanimatorSkeletronModule.ClearPlacement(layerPreviewRenderer);
        reanimateLimbDrafts.Remove(path);
        reanimateOtherDrafts.Remove(path);
        reanimateOtherBoobs.Remove(path);
        reanimateOtherAss.Remove(path);
        reanimateOtherBelly.Remove(path);
        reanimateOtherShoulders.Remove(path);
        layerPreviewRenderer.RefreshPreviewCache(path);
        return layerPreviewRenderer.GenerateMissingGeometry(path, out count, out error);
    }

    private static PsdLayerPreviewRenderer.LayerInfo FindReanimateStepSprite(
        IReadOnlyList<PsdLayerPreviewRenderer.LayerInfo> layers, int step)
    {
        foreach (var name in ReanimateStepSpriteNames[step])
        {
            foreach (var layer in layers)
            {
                if (layer == null || !layer.visible || string.IsNullOrEmpty(layer.name) ||
                    layer.sourceRect.width <= 0f || layer.sourceRect.height <= 0f)
                    continue;
                var normalized = layer.name.Replace("_", "").Replace("-", "").Replace(" ", "");
                if (string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase))
                    return layer;
            }
        }
        return null;
    }
}
#endif
