#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class ComfimatorPanelBootstrap
{
    static ComfimatorPanelBootstrap()
    {
        ReanimatorExternalPanelRegistry.Register(new ComfimatorPanel());
    }
}

internal sealed class ComfimatorPanel : IReanimatorExternalPanel, IReanimatorExternalCommandPanel, IReanimatorExternalBlockingProgressPanel
{
    private const string ServerUrlPrefsKey = "Comfimator.Comfy.ServerUrl";
    private const string AutoSkeletonWorkflowPrefsKey = "Comfimator.Comfy.AutoSkeletonWorkflowPath";
    private const string AutoSkeletonIncludeFingersPrefsKey = "Comfimator.AutoSkeleton.IncludeFingers";
    private const string AutoSkeletonNamingReferenceAssetPath = "Assets/3.Girl_in_bathroom 1.psd";
    // Temporary diagnostic switch: keep every breast-specific stage out of ALLIN
    // while the rest of the Reanimator pipeline is verified independently.
    private static readonly bool AllInBreastProcessingEnabled = true;
    // Temporary diagnostic switch: isolate ALLIN from eye controls and Face Eyes.
    private static readonly bool AllInEyeProcessingEnabled = true;
    // Temporary diagnostic switch: Auto Weights already creates bind matrices.
    // Do not mass-rebind every sprite again until the disappearing-body fault is isolated.
    private static readonly bool AllInPostWeightsRestCommitEnabled = false;
    private const float BaseHitRadius = 8f;
    private const float TipHitRadius = 10f;
    private const float BoneHitDistance = 6f;
    // Temporary diagnostic switch: the current all-sprite default geometry pass
    // makes the character meshes sample transparent atlas regions.
    private static readonly bool AllInAutoGeometryEnabled = false;

    private bool toolsExpanded = true;
    private bool allInExpanded = true;
    private Vector2 scroll;
    private Vector2 settingsScroll;
    private bool settingsLoaded;
    private string serverUrl = ComfimatorComfyClient.DefaultServerUrl;
    private string autoSkeletonWorkflowPath = ComfimatorComfyClient.AutoSkeletonControlNetPoseWorkflowPath;
    private bool autoSkeletonIncludeFingers;
    private string connectionStatus = "Not checked";
    private int selectedBoneIndex = -1;
    private int hoveredBoneIndex = -1;
    private int dragBoneIndex = -1;
    private int dragControlId;
    private DragMode dragMode = DragMode.None;
    private Vector2 dragBaseOffsetDoc;
    private bool autoGeometryRunning;
    private bool connectionCheckRunning;
    private bool autoSkeletonRunning;
    private bool detectBoobsRunning;
    private bool nipplesRunning;
    private bool fullBoobsRunning;
    private bool fashionableRunning;
    private bool allInRunning;
    private bool nippleTargetsVisible;
    private bool boobContoursVisible;
    private Vector2[] leftBoobContourDoc = Array.Empty<Vector2>();
    private Vector2[] rightBoobContourDoc = Array.Empty<Vector2>();
    private float leftBoobConfidence;
    private float rightBoobConfidence;
    private string boobContourAssetPath = string.Empty;
    private Vector2 leftNippleTargetDoc;
    private Vector2 rightNippleTargetDoc;
    private float leftNippleConfidence;
    private float rightNippleConfidence;
    private string nippleTargetAssetPath = string.Empty;
    private string actionStatus = string.Empty;
    private bool actionStatusIsError;
    private string allInBlockingStatus = string.Empty;
    private float allInBlockingProgress;
    private float allInBlockingStageStartProgress;
    private float allInBlockingStageSoftMaxProgress;
    private double allInBlockingStageStartTime;

    public string Id => "Comfimator";
    public string Title => "Comfimator";

    public bool InvokeCommand(string label, IReanimatorExternalPanelHost host)
    {
        EnsureSettingsLoaded();

        switch ((label ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "AUTO GEOMETRY":
                RunAutoGeometryFromButton(host);
                return true;
            case "AUTO SKELETON":
                RunAutoSkeletonConnectionTest(host);
                return true;
            case "AUTO WEIGHTS":
                RunAutoWeightsFromButton(host);
                return true;
            case "DETECT BOOBS":
                RunDetectBoobsFromButton(host);
                return true;
            case "NIPPLES":
                RunNipplesFromButton(host);
                return true;
            case "FULLBOOBS":
            case "FULL BOOBS":
                RunFullBoobsFromButton(host);
                return true;
            case "FASHIONABLE":
                RunFashionableFromButton(host);
                return true;
            case "COOK PREFAB":
                RunCookPrefabFromButton(host);
                return true;
            case "ALLIN":
            case "ALL IN":
                RunAllIn(host);
                return true;
            default:
                return false;
        }
    }

    public bool TryGetBlockingProgress(out string title, out string status, out float progress)
    {
        title = "ALLIN";
        status = allInBlockingStatus;
        progress = Mathf.Clamp01(allInBlockingProgress);
        if (allInRunning && allInBlockingStageSoftMaxProgress > allInBlockingStageStartProgress)
        {
            var elapsed = Mathf.Max(0f, (float)(EditorApplication.timeSinceStartup - allInBlockingStageStartTime));
            var animated = Mathf.Lerp(
                allInBlockingStageStartProgress,
                allInBlockingStageSoftMaxProgress,
                1f - Mathf.Exp(-elapsed * 0.22f));
            progress = Mathf.Clamp01(Mathf.Max(progress, animated));
        }
        return allInRunning;
    }

    private enum DragMode
    {
        None = 0,
        MoveBase = 1,
        RotateTip = 2
    }

    private enum BoneHandle
    {
        None = 0,
        Base = 1,
        Tip = 2,
        Body = 3
    }

    private readonly struct BoneHit
    {
        public readonly int BoneIndex;
        public readonly BoneHandle Handle;

        public BoneHit(int boneIndex, BoneHandle handle)
        {
            BoneIndex = boneIndex;
            Handle = handle;
        }
    }

    public void DrawLeftPanel(IReanimatorExternalPanelHost host)
    {
        EnsureSettingsLoaded();

        EditorGUILayout.LabelField(Title, EditorStyles.boldLabel);
        scroll = GUILayout.BeginScrollView(
            scroll,
            false,
            true,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.ExpandHeight(true));

        DrawSection("Tools", ref toolsExpanded, () =>
        {
            DrawAutoGeometryButton(host);
            DrawAutoSkeletonButton(host);
            DrawAutoWeightsButton(host);
            DrawDetectBoobsButton(host);
            DrawNipplesButton(host);
            DrawFullBoobsButton(host);
            DrawFashionableButton(host);
            DrawCookPrefabButton(host);
        });
        DrawSection("ALLIN", ref allInExpanded, () => DrawAllInButton(host));

        DrawMadnessIndicator(host);

        DrawActionStatus();

        GUILayout.EndScrollView();
    }

    public void DrawRightPanel(IReanimatorExternalPanelHost host, float availableHeight)
    {
        EnsureSettingsLoaded();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(Mathf.Max(80f, availableHeight))))
        {
            settingsScroll = GUILayout.BeginScrollView(
                settingsScroll,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.ExpandHeight(true));

            DrawSettings(host);

            GUILayout.EndScrollView();
        }
    }

    public void DrawPreviewOverlay(IReanimatorExternalPanelHost host)
    {
        var bones = host != null ? host.CaptureBones() : null;
        if (bones != null && bones.Count > 0)
        {
            HandleInput(host, bones);
            DrawBones(host, bones);
        }
        DrawBoobContours(host);
        DrawNippleTargets(host);
    }

    private static void DrawSection(string label, ref bool expanded, Action draw)
    {
        expanded = EditorGUILayout.Foldout(expanded, label, true);
        if (!expanded)
            return;

        using (new EditorGUI.IndentLevelScope())
        {
            draw?.Invoke();
        }
    }

    private void EnsureSettingsLoaded()
    {
        if (settingsLoaded)
            return;

        serverUrl = EditorPrefs.GetString(ServerUrlPrefsKey, ComfimatorComfyClient.DefaultServerUrl);
        autoSkeletonWorkflowPath = EditorPrefs.GetString(AutoSkeletonWorkflowPrefsKey, ComfimatorComfyClient.AutoSkeletonControlNetPoseWorkflowPath);
        autoSkeletonIncludeFingers = EditorPrefs.GetBool(AutoSkeletonIncludeFingersPrefsKey, false);
        if (string.Equals(autoSkeletonWorkflowPath, ComfimatorComfyClient.AutoSkeletonEchoWorkflowPath, StringComparison.OrdinalIgnoreCase))
        {
            autoSkeletonWorkflowPath = ComfimatorComfyClient.AutoSkeletonControlNetPoseWorkflowPath;
            SaveSettings();
        }
        settingsLoaded = true;
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(ServerUrlPrefsKey, string.IsNullOrWhiteSpace(serverUrl) ? ComfimatorComfyClient.DefaultServerUrl : serverUrl.Trim());
        EditorPrefs.SetString(
            AutoSkeletonWorkflowPrefsKey,
            string.IsNullOrWhiteSpace(autoSkeletonWorkflowPath) ? ComfimatorComfyClient.AutoSkeletonControlNetPoseWorkflowPath : autoSkeletonWorkflowPath.Trim());
        EditorPrefs.SetBool(AutoSkeletonIncludeFingersPrefsKey, autoSkeletonIncludeFingers);
    }

    private void DrawSettings(IReanimatorExternalPanelHost host)
    {
        EditorGUILayout.LabelField("Comfy Connection", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        serverUrl = EditorGUILayout.TextField("Server URL", serverUrl);
        autoSkeletonWorkflowPath = EditorGUILayout.TextField("Workflow", autoSkeletonWorkflowPath);
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("Auto Skeleton", EditorStyles.boldLabel);
        autoSkeletonIncludeFingers = EditorGUILayout.ToggleLeft("Fingers", autoSkeletonIncludeFingers);
        if (EditorGUI.EndChangeCheck())
        {
            SaveSettings();
            host?.RepaintHost();
        }

        using (new EditorGUI.DisabledScope(connectionCheckRunning))
        {
            if (GUILayout.Button(connectionCheckRunning ? "Checking..." : "Test Connection", GUILayout.Height(22f)))
                RunConnectionCheck(host);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset", GUILayout.Height(20f), GUILayout.Width(60f)))
            {
                serverUrl = ComfimatorComfyClient.DefaultServerUrl;
                autoSkeletonWorkflowPath = ComfimatorComfyClient.AutoSkeletonControlNetPoseWorkflowPath;
                autoSkeletonIncludeFingers = false;
                connectionStatus = "Not checked";
                actionStatusIsError = false;
                SaveSettings();
                host?.RepaintHost();
            }

            EditorGUILayout.LabelField("Status: " + connectionStatus, EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void DrawMadnessIndicator(IReanimatorExternalPanelHost host)
    {
        var running = autoGeometryRunning || autoSkeletonRunning || detectBoobsRunning ||
                      nipplesRunning || fullBoobsRunning || fashionableRunning || allInRunning;
        var madness = running
            ? Mathf.PingPong((float)EditorApplication.timeSinceStartup * 0.85f, 1f)
            : 0.06f;

        var rect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.ProgressBar(rect, madness, running ? "INSANITY ACTIVE" : "INSANITY IDLE");

        if (running)
            host?.RepaintHost();
    }

    private void DrawActionStatus()
    {
        if (string.IsNullOrWhiteSpace(actionStatus))
            return;

        if (actionStatusIsError)
        {
            EditorGUILayout.HelpBox(actionStatus, MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField(actionStatus, EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawAutoGeometryButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(autoGeometryRunning || allInRunning))
        {
            if (!GUILayout.Button(autoGeometryRunning ? "Auto Geometry..." : "Auto Geometry", GUILayout.Height(22f)))
                return;
        }

        RunAutoGeometryFromButton(host);
    }

    private void DrawAutoSkeletonButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(autoSkeletonRunning || allInRunning))
        {
            if (!GUILayout.Button(autoSkeletonRunning ? "Auto Skeleton..." : "Auto Skeleton", GUILayout.Height(22f)))
                return;
        }

        RunAutoSkeletonConnectionTest(host);
    }

    private void DrawAutoWeightsButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(allInRunning || fashionableRunning))
        {
            if (!GUILayout.Button("Auto Weights", GUILayout.Height(22f)))
                return;
        }

        RunAutoWeightsFromButton(host);
    }

    private void DrawNipplesButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(
                   nipplesRunning || detectBoobsRunning || fullBoobsRunning || allInRunning || autoGeometryRunning ||
                   autoSkeletonRunning || fashionableRunning))
        {
            if (!GUILayout.Button(nipplesRunning ? "Nipples..." : "Nipples", GUILayout.Height(22f)))
                return;
        }

        RunNipplesFromButton(host);
    }

    private void DrawDetectBoobsButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(
                   detectBoobsRunning || nipplesRunning || fullBoobsRunning || allInRunning || autoGeometryRunning ||
                   autoSkeletonRunning || fashionableRunning))
        {
            if (!GUILayout.Button(
                    detectBoobsRunning ? "Detect Boobs..." : "Detect Boobs",
                    GUILayout.Height(22f)))
            {
                return;
            }
        }

        RunDetectBoobsFromButton(host);
    }

    private void DrawFullBoobsButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(
                   fullBoobsRunning || detectBoobsRunning || nipplesRunning || allInRunning ||
                   autoGeometryRunning || autoSkeletonRunning || fashionableRunning))
        {
            if (!GUILayout.Button(fullBoobsRunning ? "FullBoobs..." : "FullBoobs", GUILayout.Height(22f)))
                return;
        }

        RunFullBoobsFromButton(host);
    }

    private async void RunFullBoobsFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null || fullBoobsRunning || nipplesRunning || detectBoobsRunning)
            return;

        if (host.HasSeparatedBreastSprites())
        {
            actionStatus = "Breasts are split into separate sprites. FullBoobs is unavailable.";
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
            host.RepaintHost();
            return;
        }
        if (!HasRequiredFullBoobBones(host))
        {
            actionStatus = "FullBoobs requires bones l_nipple and r_nipple.";
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
            host.RepaintHost();
            return;
        }

        fullBoobsRunning = true;
        actionStatusIsError = false;
        host.RepaintHost();
        try
        {
            actionStatus = "FullBoobs 1/3: detecting nipples";
            host.ShowNotification(actionStatus);
            ClearNippleTargets();
            RunNipplesFromButton(host);
            await WaitForComfimatorOperationAsync(() => nipplesRunning, "Nipples");
            if (!nippleTargetsVisible)
                throw new InvalidOperationException("Nipple detection produced no targets. " + actionStatus);

            actionStatus = "FullBoobs 2/3: detecting breast contours";
            host.ShowNotification(actionStatus);
            boobContoursVisible = false;
            RunDetectBoobsFromButton(host);
            await WaitForComfimatorOperationAsync(() => detectBoobsRunning, "Detect Boobs");
            if (!boobContoursVisible || leftBoobContourDoc.Length < 3 || rightBoobContourDoc.Length < 3)
                throw new InvalidOperationException("Breast detection produced no valid contours. " + actionStatus);

            actionStatus = "FullBoobs 3/3: applying boob weights";
            host.ShowNotification(actionStatus);
            var applyResult = host.ApplyFullBoobsWeights(
                leftNippleTargetDoc,
                rightNippleTargetDoc,
                leftBoobContourDoc,
                rightBoobContourDoc);
            actionStatus = applyResult;
            actionStatusIsError = false;
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            actionStatus = "FullBoobs failed: " + ex.Message;
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
        }
        finally
        {
            fullBoobsRunning = false;
            host.RepaintHost();
        }
    }

    private static bool HasRequiredFullBoobBones(IReanimatorExternalPanelHost host)
    {
        var hasLeft = false;
        var hasRight = false;
        var bones = host != null ? host.CaptureBones() : null;
        if (bones == null)
            return false;
        for (var i = 0; i < bones.Count; i++)
        {
            var name = NormalizeSemanticBoneName(bones[i].Name);
            if (name == "lnipple" || name == "nipplel" || name == "leftnipple")
                hasLeft = true;
            else if (name == "rnipple" || name == "nippler" || name == "rightnipple")
                hasRight = true;
        }
        return hasLeft && hasRight;
    }

    private static async Task WaitForComfimatorOperationAsync(Func<bool> isRunning, string operationName)
    {
        var deadline = DateTime.UtcNow.AddMinutes(20);
        while (isRunning != null && isRunning())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(operationName + " timed out.");
            await Task.Delay(100);
        }
    }

    private async void RunDetectBoobsFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null || detectBoobsRunning)
            return;

        if (host.HasSeparatedBreastSprites())
        {
            boobContoursVisible = false;
            boobContourAssetPath = string.Empty;
            leftBoobContourDoc = Array.Empty<Vector2>();
            rightBoobContourDoc = Array.Empty<Vector2>();
            actionStatus = "Breasts are split into separate sprites. Detect Boobs is unavailable.";
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
            host.RepaintHost();
            return;
        }

        EnsureSettingsLoaded();
        detectBoobsRunning = true;
        actionStatus = "Detect Boobs: capturing visible sprites";
        actionStatusIsError = false;
        host.RepaintHost();
        try
        {
            // CaptureWorkspacePng composites only LayerInfo.visible sprites. Hidden
            // clothing and every editor overlay are excluded before upload.
            var screenshotPath = host.CaptureWorkspacePng();
            // Reuse only the current Nipples result for this exact asset. Bones
            // are output data and must never replace or move those preview
            // targets. Without a current result, locate fresh visual centers.
            actionStatus = "Detect Boobs: locating breast centers with Qwen3-VL";
            host.ShowNotification(actionStatus);
            host.RepaintHost();
            var nippleResult = await ComfimatorComfyClient.RunNippleVisionAsync(serverUrl, screenshotPath);
            var usedExistingNippleTargets = TryGetCurrentNippleTargets(
                host,
                out var viewerLeftNippleDoc,
                out var viewerRightNippleDoc);
            if (!usedExistingNippleTargets)
            {
                viewerLeftNippleDoc = host.WorkspaceNormalizedToDocument(nippleResult.ViewerLeft);
                viewerRightNippleDoc = host.WorkspaceNormalizedToDocument(nippleResult.ViewerRight);
                CorrectBoobCentersFromWorkspaceAlpha(
                    host,
                    screenshotPath,
                    nippleResult.HasBreastBounds ? nippleResult.ViewerLeftBreastBounds : (Rect?)null,
                    nippleResult.HasBreastBounds ? nippleResult.ViewerRightBreastBounds : (Rect?)null,
                    ref viewerLeftNippleDoc,
                    ref viewerRightNippleDoc);
            }
            var hasNippleAnchors = Vector2.Distance(viewerLeftNippleDoc, viewerRightNippleDoc) > 4f;
            var nippleAnchorSource = usedExistingNippleTargets && nippleResult.HasBreastBounds
                ? "existing Nipples targets + Qwen breast bounds"
                : usedExistingNippleTargets
                    ? "existing Nipples targets"
                : nippleResult.HasBreastBounds
                    ? "Qwen centers + breast bounds"
                : "Qwen centers aligned to torso";
            var viewerLeftNipple = hasNippleAnchors
                ? DocumentToWorkspaceNormalized(host, viewerLeftNippleDoc)
                : Vector2.zero;
            var viewerRightNipple = hasNippleAnchors
                ? DocumentToWorkspaceNormalized(host, viewerRightNippleDoc)
                : Vector2.zero;

            actionStatus = "Detect Boobs: analyzing visible body with Qwen3-VL";
            host.ShowNotification(actionStatus);
            host.RepaintHost();

            var result = await ComfimatorComfyClient.RunBoobVisionAsync(
                serverUrl,
                screenshotPath,
                hasNippleAnchors,
                viewerLeftNipple,
                viewerRightNipple);
            if (hasNippleAnchors && TryBuildFusedBodyBoobContours(
                    host,
                    screenshotPath,
                    viewerLeftNippleDoc,
                    viewerRightNippleDoc,
                    nippleResult.HasBreastBounds ? nippleResult.ViewerLeftBreastBounds : (Rect?)null,
                    nippleResult.HasBreastBounds ? nippleResult.ViewerRightBreastBounds : (Rect?)null,
                    out var anatomicalLeftContour,
                    out var anatomicalRightContour))
            {
                leftBoobContourDoc = anatomicalLeftContour;
                rightBoobContourDoc = anatomicalRightContour;
            }
            else
            {
                leftBoobContourDoc = ConvertContourToDocument(host, result.ViewerLeftContour);
                rightBoobContourDoc = ConvertContourToDocument(host, result.ViewerRightContour);
            }

            var constrainedLayers = string.Empty;
            if (hasNippleAnchors)
            {
                if (host.TryConstrainBoobContourToVisibleSprite(
                        viewerLeftNippleDoc,
                        leftBoobContourDoc,
                        out var constrainedLeft,
                        out var leftLayerName))
                {
                    leftBoobContourDoc = constrainedLeft;
                    constrainedLayers = leftLayerName;
                }
                if (host.TryConstrainBoobContourToVisibleSprite(
                        viewerRightNippleDoc,
                        rightBoobContourDoc,
                        out var constrainedRight,
                        out var rightLayerName))
                {
                    rightBoobContourDoc = constrainedRight;
                    if (!string.Equals(constrainedLayers, rightLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        constrainedLayers = string.IsNullOrWhiteSpace(constrainedLayers)
                            ? rightLayerName
                            : constrainedLayers + ", " + rightLayerName;
                    }
                }
            }

            leftBoobContourDoc = SmoothSharpContourPeaks(leftBoobContourDoc, out var leftSmoothedPeaks);
            rightBoobContourDoc = SmoothSharpContourPeaks(rightBoobContourDoc, out var rightSmoothedPeaks);
            var smoothedPeakCount = leftSmoothedPeaks + rightSmoothedPeaks;
            if (smoothedPeakCount > 0 && hasNippleAnchors)
            {
                // Smoothing is an inward neighbour blend in normal cases, but run
                // the layer guard once more for concave sprite silhouettes.
                if (host.TryConstrainBoobContourToVisibleSprite(
                        viewerLeftNippleDoc,
                        leftBoobContourDoc,
                        out var smoothedLeft,
                        out _))
                {
                    leftBoobContourDoc = smoothedLeft;
                }
                if (host.TryConstrainBoobContourToVisibleSprite(
                        viewerRightNippleDoc,
                        rightBoobContourDoc,
                        out var smoothedRight,
                        out _))
                {
                    rightBoobContourDoc = smoothedRight;
                }
            }

            // This must be the final geometry operation. Layer rest-pose/current-
            // draw mapping above can move a point after an earlier alpha clamp.
            // Nothing is allowed to modify the contours after this workspace
            // silhouette pass.
            var finalAlphaClampApplied = ClampFinalContoursToWorkspaceAlpha(
                host,
                screenshotPath,
                viewerLeftNippleDoc,
                viewerRightNippleDoc,
                leftBoobContourDoc,
                rightBoobContourDoc);

            // Absolute last operation: constrain against the actual body sprite,
            // not the composited workspace (which may be fully opaque because of
            // a visible background layer). Never display an unconstrained result.
            if (!host.TryConstrainBoobContourToVisibleSprite(
                    viewerLeftNippleDoc,
                    leftBoobContourDoc,
                    out var finalBodyLeft,
                    out var finalBodyLeftLayer) ||
                !host.TryConstrainBoobContourToVisibleSprite(
                    viewerRightNippleDoc,
                    rightBoobContourDoc,
                    out var finalBodyRight,
                    out var finalBodyRightLayer))
            {
                throw new InvalidOperationException(
                    "Body sprite contour is unavailable. Detect Boobs will not show an unclipped result.");
            }
            leftBoobContourDoc = finalBodyLeft;
            rightBoobContourDoc = finalBodyRight;
            if (string.IsNullOrWhiteSpace(constrainedLayers))
                constrainedLayers = finalBodyLeftLayer;
            if (!string.Equals(finalBodyLeftLayer, finalBodyRightLayer, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(finalBodyRightLayer))
            {
                constrainedLayers += ", " + finalBodyRightLayer;
            }
            leftBoobConfidence = result.LeftConfidence;
            rightBoobConfidence = result.RightConfidence;
            boobContoursVisible = true;
            boobContourAssetPath = host.CurrentAssetPath ?? string.Empty;
            connectionStatus = "Connected";
            actionStatus = string.Format(
                "Detect Boobs: left {0}% ({2} points), right {1}% ({3} points); {6}; visible sprites only{4}{5}{7}",
                Mathf.RoundToInt(leftBoobConfidence * 100f),
                Mathf.RoundToInt(rightBoobConfidence * 100f),
                leftBoobContourDoc.Length,
                rightBoobContourDoc.Length,
                string.IsNullOrWhiteSpace(constrainedLayers)
                    ? string.Empty
                    : "; alpha constrained by " + constrainedLayers,
                smoothedPeakCount > 0
                    ? "; smoothed peaks: " + smoothedPeakCount
                    : string.Empty,
                nippleAnchorSource,
                finalAlphaClampApplied
                    ? "; final body sprite silhouette clamp"
                    : "; final body sprite silhouette clamp (workspace alpha unavailable)");
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            boobContoursVisible = false;
            boobContourAssetPath = string.Empty;
            leftBoobContourDoc = Array.Empty<Vector2>();
            rightBoobContourDoc = Array.Empty<Vector2>();
            if (IsComfyUnavailable(ex))
                ShowComfyUnavailable(host);
            else
            {
                actionStatus = "Detect Boobs failed: " + ex.Message;
                actionStatusIsError = true;
                host.ShowNotification(actionStatus);
            }
        }
        finally
        {
            detectBoobsRunning = false;
            host.RepaintHost();
        }
    }

    private static Vector2[] ConvertContourToDocument(
        IReanimatorExternalPanelHost host,
        IReadOnlyList<Vector2> normalizedContour)
    {
        if (normalizedContour == null || normalizedContour.Count == 0)
            return Array.Empty<Vector2>();

        var result = new Vector2[normalizedContour.Count];
        for (var i = 0; i < normalizedContour.Count; i++)
            result[i] = host.WorkspaceNormalizedToDocument(normalizedContour[i]);
        return result;
    }

    private static Vector2 DocumentToWorkspaceNormalized(
        IReanimatorExternalPanelHost host,
        Vector2 documentPoint)
    {
        var topLeft = host.WorkspaceNormalizedToDocument(Vector2.zero);
        var bottomRight = host.WorkspaceNormalizedToDocument(Vector2.one);
        var width = Mathf.Max(0.0001f, bottomRight.x - topLeft.x);
        var height = Mathf.Max(0.0001f, topLeft.y - bottomRight.y);
        return new Vector2(
            Mathf.Clamp01((documentPoint.x - topLeft.x) / width),
            Mathf.Clamp01((topLeft.y - documentPoint.y) / height));
    }

    private static Vector2[] SmoothSharpContourPeaks(
        IReadOnlyList<Vector2> contour,
        out int smoothedPeakCount)
    {
        smoothedPeakCount = 0;
        if (contour == null || contour.Count < 6)
            return contour == null ? Array.Empty<Vector2>() : CopyContour(contour);

        var result = CopyContour(contour);
        var smoothVertex = new bool[contour.Count];
        for (var i = 0; i < contour.Count; i++)
        {
            var previous = contour[(i + contour.Count - 1) % contour.Count];
            var current = contour[i];
            var next = contour[(i + 1) % contour.Count];
            var incoming = previous - current;
            var outgoing = next - current;
            var incomingLength = incoming.magnitude;
            var outgoingLength = outgoing.magnitude;
            if (incomingLength <= 0.001f || outgoingLength <= 0.001f)
                continue;

            var vertexAngle = Vector2.Angle(incoming, outgoing);
            var chordLength = Mathf.Max(0.001f, Vector2.Distance(previous, next));
            var spikeRatio = Vector2.Distance(current, (previous + next) * 0.5f) / chordLength;
            if (vertexAngle >= 125f || spikeRatio <= 0.18f)
                continue;

            smoothVertex[i] = true;
            smoothedPeakCount++;
        }

        for (var i = 0; i < contour.Count; i++)
        {
            if (!smoothVertex[i])
                continue;
            var previous = contour[(i + contour.Count - 1) % contour.Count];
            var next = contour[(i + 1) % contour.Count];
            var neighbourMidpoint = (previous + next) * 0.5f;
            result[i] = Vector2.Lerp(contour[i], neighbourMidpoint, 0.38f);
        }
        return result;
    }

    private static Vector2[] CopyContour(IReadOnlyList<Vector2> contour)
    {
        var result = new Vector2[contour.Count];
        for (var i = 0; i < contour.Count; i++)
            result[i] = contour[i];
        return result;
    }

    private static bool IsPlausibleBoobAnchorPair(
        IReanimatorExternalPanelHost host,
        Vector2 viewerLeft,
        Vector2 viewerRight)
    {
        var pairDelta = viewerRight - viewerLeft;
        if (pairDelta.magnitude <= 4f)
            return false;

        var bones = host.CaptureBones();
        if (!TryGetTorsoFrame(bones, pairDelta, out var sideAxis, out var torsoUp))
            return true;

        var lateralDistance = Mathf.Abs(Vector2.Dot(pairDelta, sideAxis));
        var axialMismatch = Mathf.Abs(Vector2.Dot(pairDelta, torsoUp));
        if (lateralDistance <= 4f || axialMismatch > lateralDistance * 0.48f)
            return false;

        if (!TryFindExternalBone(bones, "neck", out var neck) ||
            !TryFindExternalBone(bones, "hips", out var hips))
        {
            return true;
        }

        var torsoLength = Vector2.Distance(neck.WorldPosition, hips.WorldPosition);
        if (torsoLength <= 4f)
            return true;

        var midpoint = (viewerLeft + viewerRight) * 0.5f;
        var heightFromHips = Vector2.Dot(midpoint - hips.WorldPosition, torsoUp) / torsoLength;
        return heightFromHips >= 0.35f && heightFromHips <= 1.18f;
    }

    private static void CorrectBoobCentersFromWorkspaceAlpha(
        IReanimatorExternalPanelHost host,
        string screenshotPath,
        Rect? viewerLeftBounds,
        Rect? viewerRightBounds,
        ref Vector2 viewerLeftDocumentPoint,
        ref Vector2 viewerRightDocumentPoint)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            return;

        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(screenshotPath), false))
                return;

            if (TryFindOpaqueBoundsCentroid(texture, viewerLeftBounds, out var leftCenter))
                viewerLeftDocumentPoint = host.WorkspaceNormalizedToDocument(leftCenter);
            if (TryFindOpaqueBoundsCentroid(texture, viewerRightBounds, out var rightCenter))
                viewerRightDocumentPoint = host.WorkspaceNormalizedToDocument(rightCenter);
        }
        catch
        {
            // Keep Qwen centers when the temporary preview cannot be sampled.
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static bool TryFindOpaqueBoundsCentroid(
        Texture2D texture,
        Rect? normalizedBounds,
        out Vector2 normalizedCenter)
    {
        normalizedCenter = default;
        if (texture == null || !normalizedBounds.HasValue)
            return false;

        var bounds = normalizedBounds.Value;
        const int horizontalSamples = 64;
        const int verticalSamples = 48;
        var weightedSum = Vector2.zero;
        var totalWeight = 0f;
        for (var y = 0; y < verticalSamples; y++)
        {
            var normalizedY = Mathf.Lerp(bounds.yMin, bounds.yMax, (y + 0.5f) / verticalSamples);
            for (var x = 0; x < horizontalSamples; x++)
            {
                var normalizedX = Mathf.Lerp(bounds.xMin, bounds.xMax, (x + 0.5f) / horizontalSamples);
                var alpha = texture.GetPixelBilinear(normalizedX, 1f - normalizedY).a;
                if (alpha < 0.08f)
                    continue;

                // Squared alpha prevents antialiased silhouette pixels from
                // pulling the center back toward the transparent boundary.
                var weight = alpha * alpha;
                weightedSum += new Vector2(normalizedX, normalizedY) * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0.001f)
            return false;
        normalizedCenter = weightedSum / totalWeight;
        return bounds.Contains(normalizedCenter);
    }

    private static void CorrectBoobAnchorPairToTorso(
        IReanimatorExternalPanelHost host,
        ref Vector2 viewerLeft,
        ref Vector2 viewerRight)
    {
        var pairDelta = viewerRight - viewerLeft;
        if (!TryGetTorsoFrame(host.CaptureBones(), pairDelta, out var sideAxis, out var torsoUp))
            return;

        var lateralDistance = Mathf.Abs(Vector2.Dot(pairDelta, sideAxis));
        if (lateralDistance <= 4f)
            return;

        // A reclining torso legitimately tilts the breast pair, but a noisy
        // center must never rotate that pair toward the longitudinal torso axis.
        var axialDistance = Mathf.Clamp(
            Vector2.Dot(pairDelta, torsoUp),
            -lateralDistance * 0.35f,
            lateralDistance * 0.35f);
        var correctedDelta = sideAxis * lateralDistance + torsoUp * axialDistance;
        var midpoint = (viewerLeft + viewerRight) * 0.5f;
        viewerLeft = midpoint - correctedDelta * 0.5f;
        viewerRight = midpoint + correctedDelta * 0.5f;
    }

    private static bool TryGetTorsoFrame(
        IReadOnlyList<ReanimatorExternalBone> bones,
        Vector2 pairDelta,
        out Vector2 sideAxis,
        out Vector2 torsoUp)
    {
        sideAxis = default;
        torsoUp = default;
        if (!TryFindExternalBone(bones, "neck", out var neck) ||
            !TryFindExternalBone(bones, "hips", out var hips))
        {
            return false;
        }

        torsoUp = neck.WorldPosition - hips.WorldPosition;
        if (torsoUp.sqrMagnitude <= 16f)
            return false;
        torsoUp.Normalize();
        sideAxis = new Vector2(torsoUp.y, -torsoUp.x);
        if (Vector2.Dot(sideAxis, pairDelta) < 0f)
            sideAxis = -sideAxis;
        return true;
    }

    private static bool TryBuildFusedBodyBoobContours(
        IReanimatorExternalPanelHost host,
        string screenshotPath,
        Vector2 viewerLeftNipple,
        Vector2 viewerRightNipple,
        Rect? viewerLeftBounds,
        Rect? viewerRightBounds,
        out Vector2[] viewerLeftContour,
        out Vector2[] viewerRightContour)
    {
        viewerLeftContour = Array.Empty<Vector2>();
        viewerRightContour = Array.Empty<Vector2>();
        var pairDelta = viewerRightNipple - viewerLeftNipple;
        var nippleSpacing = pairDelta.magnitude;
        if (nippleSpacing <= 4f || string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            return false;

        var bones = host.CaptureBones();
        Vector2 sideAxis;
        Vector2 torsoUp;
        if (TryGetTorsoFrame(bones, pairDelta, out sideAxis, out torsoUp))
        {
            nippleSpacing = Mathf.Abs(Vector2.Dot(pairDelta, sideAxis));
            if (nippleSpacing <= 4f)
                nippleSpacing = pairDelta.magnitude;
        }
        else
        {
            sideAxis = pairDelta / nippleSpacing;
            torsoUp = new Vector2(-sideAxis.y, sideAxis.x);
            if (torsoUp.y < 0f)
                torsoUp = -torsoUp;
        }

        CalculateBoobPerspectiveScales(
            bones,
            viewerLeftNipple,
            viewerRightNipple,
            out var viewerLeftScale,
            out var viewerRightScale);
        TryGetViewerArmBones(host, bones, out var viewerLeftArm, out var viewerRightArm);

        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(screenshotPath), false))
                return false;

            viewerLeftContour = BuildFusedBodyBoobContour(
                host,
                texture,
                viewerLeftNipple,
                sideAxis,
                torsoUp,
                nippleSpacing,
                true,
                viewerLeftScale,
                viewerLeftArm,
                GetBreastBoundsRadii(host, viewerLeftBounds, sideAxis, torsoUp));
            viewerRightContour = BuildFusedBodyBoobContour(
                host,
                texture,
                viewerRightNipple,
                sideAxis,
                torsoUp,
                nippleSpacing,
                false,
                viewerRightScale,
                viewerRightArm,
                GetBreastBoundsRadii(host, viewerRightBounds, sideAxis, torsoUp));
            ClampContourToWorkspaceAlpha(host, texture, viewerLeftNipple, viewerLeftContour);
            ClampContourToWorkspaceAlpha(host, texture, viewerRightNipple, viewerRightContour);
            ClampBoobContoursToPairSeparator(
                viewerLeftContour,
                viewerRightContour,
                viewerLeftNipple,
                viewerRightNipple,
                nippleSpacing);
            return viewerLeftContour.Length >= 12 && viewerRightContour.Length >= 12;
        }
        catch
        {
            viewerLeftContour = Array.Empty<Vector2>();
            viewerRightContour = Array.Empty<Vector2>();
            return false;
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Vector2[] BuildFusedBodyBoobContour(
        IReanimatorExternalPanelHost host,
        Texture2D texture,
        Vector2 nipple,
        Vector2 sideAxis,
        Vector2 torsoUp,
        float nippleSpacing,
        bool viewerLeft,
        float perspectiveScale,
        ReanimatorExternalBone? outerArm,
        Vector2? detectedBoundsRadii)
    {
        const int pointCount = 24;
        var radii = new float[pointCount];
        var expectedRadii = new float[pointCount];
        var center = nipple - torsoUp * (nippleSpacing * 0.015f);
        for (var i = 0; i < pointCount; i++)
        {
            var angle = Mathf.PI * 2f * i / pointCount;
            var side = Mathf.Cos(angle);
            var up = Mathf.Sin(angle);
            var pointsTowardInnerChest = viewerLeft ? side > 0f : side < 0f;
            // The nipple sits closer to the outer wall than to the cleavage and
            // below the upper attachment of the breast.  A symmetric ellipse is
            // therefore systematically too small on the inner/top sides and too
            // wide toward the arm.
            var horizontalRadius = nippleSpacing * (pointsTowardInnerChest
                ? 0.50f
                : 0.27f * perspectiveScale);
            var verticalRadius = nippleSpacing * (up >= 0f
                ? 0.30f * perspectiveScale
                : 0.27f * Mathf.Lerp(1f, perspectiveScale, 0.22f));
            if (detectedBoundsRadii.HasValue)
            {
                var boundsRadii = detectedBoundsRadii.Value;
                var boundsHorizontal = boundsRadii.x * (pointsTowardInnerChest ? 1.04f : 0.92f);
                var boundsVertical = boundsRadii.y * (up >= 0f ? 0.92f : 1f);
                horizontalRadius = Mathf.Max(horizontalRadius, boundsHorizontal);
                verticalRadius = Mathf.Max(verticalRadius, boundsVertical);
            }
            var expectedRadius = 1f / Mathf.Sqrt(
                side * side / (horizontalRadius * horizontalRadius) +
                up * up / (verticalRadius * verticalRadius));
            var direction = (sideAxis * side + torsoUp * up).normalized;
            expectedRadii[i] = expectedRadius;
            radii[i] = RefineBoobRadiusFromPixels(
                host,
                texture,
                center,
                direction,
                expectedRadius,
                nippleSpacing);
        }

        // Two conservative circular smoothing passes remove individual highlight
        // spikes without erasing the lower crease or the inner cleavage shape.
        for (var pass = 0; pass < 2; pass++)
        {
            var smoothed = new float[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                var previous = radii[(i + pointCount - 1) % pointCount];
                var next = radii[(i + 1) % pointCount];
                var value = radii[i] * 0.6f + (previous + next) * 0.2f;
                smoothed[i] = Mathf.Clamp(
                    value,
                    expectedRadii[i] * 0.78f,
                    expectedRadii[i] * 1.18f);
            }
            radii = smoothed;
        }

        var contour = new Vector2[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            var angle = Mathf.PI * 2f * i / pointCount;
            var direction = (sideAxis * Mathf.Cos(angle) + torsoUp * Mathf.Sin(angle)).normalized;
            contour[i] = center + direction * radii[i];
        }


        if (outerArm.HasValue)
            ClampBoobContourInsideArm(contour, center, sideAxis, viewerLeft, outerArm.Value, nippleSpacing);
        return contour;
    }

    private static Vector2? GetBreastBoundsRadii(
        IReanimatorExternalPanelHost host,
        Rect? normalizedBounds,
        Vector2 sideAxis,
        Vector2 torsoUp)
    {
        if (!normalizedBounds.HasValue)
            return null;

        var bounds = normalizedBounds.Value;
        var corners = new[]
        {
            host.WorkspaceNormalizedToDocument(new Vector2(bounds.xMin, bounds.yMin)),
            host.WorkspaceNormalizedToDocument(new Vector2(bounds.xMax, bounds.yMin)),
            host.WorkspaceNormalizedToDocument(new Vector2(bounds.xMax, bounds.yMax)),
            host.WorkspaceNormalizedToDocument(new Vector2(bounds.xMin, bounds.yMax))
        };
        var minSide = float.PositiveInfinity;
        var maxSide = float.NegativeInfinity;
        var minUp = float.PositiveInfinity;
        var maxUp = float.NegativeInfinity;
        for (var i = 0; i < corners.Length; i++)
        {
            var side = Vector2.Dot(corners[i], sideAxis);
            var up = Vector2.Dot(corners[i], torsoUp);
            minSide = Mathf.Min(minSide, side);
            maxSide = Mathf.Max(maxSide, side);
            minUp = Mathf.Min(minUp, up);
            maxUp = Mathf.Max(maxUp, up);
        }

        // Qwen boxes intentionally include the full attachment area and are a
        // little looser than the deforming soft volume. Keep most of their size
        // while leaving pixel gradients room to fit the final boundary.
        var horizontalRadius = (maxSide - minSide) * 0.5f * 0.65f;
        var verticalRadius = (maxUp - minUp) * 0.5f * 0.52f;
        if (horizontalRadius <= 4f || verticalRadius <= 4f)
            return null;
        return new Vector2(horizontalRadius, verticalRadius);
    }

    private static void ClampContourToWorkspaceAlpha(
        IReanimatorExternalPanelHost host,
        Texture2D texture,
        Vector2 centerDocumentPoint,
        Vector2[] contour)
    {
        if (texture == null || contour == null || contour.Length == 0)
            return;

        if (SampleWorkspaceColor(host, texture, centerDocumentPoint).a < 0.08f)
        {
            var normalizedCenter = DocumentToWorkspaceNormalized(host, centerDocumentPoint);
            if (!TryFindNearestOpaqueWorkspacePoint(texture, normalizedCenter, out normalizedCenter))
                return;
            centerDocumentPoint = host.WorkspaceNormalizedToDocument(normalizedCenter);
        }

        const int scanSteps = 64;
        for (var i = 0; i < contour.Length; i++)
        {
            var target = contour[i];
            var previousT = 0f;
            var exitT = -1f;
            for (var step = 1; step <= scanSteps; step++)
            {
                var t = step / (float)scanSteps;
                var samplePoint = Vector2.Lerp(centerDocumentPoint, target, t);
                if (SampleWorkspaceColor(host, texture, samplePoint).a >= 0.08f)
                {
                    previousT = t;
                    continue;
                }

                exitT = t;
                break;
            }

            if (exitT < 0f)
                continue;

            var insideT = previousT;
            var outsideT = exitT;
            for (var step = 0; step < 14; step++)
            {
                var middleT = (insideT + outsideT) * 0.5f;
                var samplePoint = Vector2.Lerp(centerDocumentPoint, target, middleT);
                if (SampleWorkspaceColor(host, texture, samplePoint).a >= 0.08f)
                    insideT = middleT;
                else
                    outsideT = middleT;
            }

            contour[i] = Vector2.Lerp(
                centerDocumentPoint,
                target,
                Mathf.Max(0f, insideT - 0.018f));
        }
    }

    private static bool TryFindNearestOpaqueWorkspacePoint(
        Texture2D texture,
        Vector2 normalizedTopLeft,
        out Vector2 opaqueNormalizedTopLeft)
    {
        opaqueNormalizedTopLeft = normalizedTopLeft;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return false;

        var centerX = Mathf.Clamp(Mathf.RoundToInt(normalizedTopLeft.x * (texture.width - 1)), 0, texture.width - 1);
        var centerY = Mathf.Clamp(Mathf.RoundToInt((1f - normalizedTopLeft.y) * (texture.height - 1)), 0, texture.height - 1);
        var maxRadius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(texture.width, texture.height) * 0.2f), 8, 512);
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            var minX = Mathf.Max(0, centerX - radius);
            var maxX = Mathf.Min(texture.width - 1, centerX + radius);
            var minY = Mathf.Max(0, centerY - radius);
            var maxY = Mathf.Min(texture.height - 1, centerY + radius);

            for (var x = minX; x <= maxX; x++)
            {
                if (texture.GetPixel(x, minY).a >= 0.08f)
                    return SetOpaqueWorkspacePoint(texture, x, minY, out opaqueNormalizedTopLeft);
                if (maxY != minY && texture.GetPixel(x, maxY).a >= 0.08f)
                    return SetOpaqueWorkspacePoint(texture, x, maxY, out opaqueNormalizedTopLeft);
            }
            for (var y = minY + 1; y < maxY; y++)
            {
                if (texture.GetPixel(minX, y).a >= 0.08f)
                    return SetOpaqueWorkspacePoint(texture, minX, y, out opaqueNormalizedTopLeft);
                if (maxX != minX && texture.GetPixel(maxX, y).a >= 0.08f)
                    return SetOpaqueWorkspacePoint(texture, maxX, y, out opaqueNormalizedTopLeft);
            }
        }

        return false;
    }

    private static bool SetOpaqueWorkspacePoint(
        Texture2D texture,
        int x,
        int y,
        out Vector2 normalizedTopLeft)
    {
        normalizedTopLeft = new Vector2(
            (x + 0.5f) / texture.width,
            1f - (y + 0.5f) / texture.height);
        return true;
    }

    private static bool ClampFinalContoursToWorkspaceAlpha(
        IReanimatorExternalPanelHost host,
        string screenshotPath,
        Vector2 viewerLeftCenter,
        Vector2 viewerRightCenter,
        Vector2[] viewerLeftContour,
        Vector2[] viewerRightContour)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            return false;

        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(screenshotPath), false))
                return false;

            ClampContourToWorkspaceAlpha(host, texture, viewerLeftCenter, viewerLeftContour);
            ClampContourToWorkspaceAlpha(host, texture, viewerRightCenter, viewerRightContour);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void ClampBoobContoursToPairSeparator(
        Vector2[] viewerLeftContour,
        Vector2[] viewerRightContour,
        Vector2 viewerLeftCenter,
        Vector2 viewerRightCenter,
        float nippleSpacing)
    {
        var pairAxis = viewerRightCenter - viewerLeftCenter;
        if (pairAxis.sqrMagnitude <= 16f)
            return;
        pairAxis.Normalize();
        var midpoint = (viewerLeftCenter + viewerRightCenter) * 0.5f;
        var halfGap = Mathf.Max(1.5f, nippleSpacing * 0.018f);

        for (var i = 0; i < viewerLeftContour.Length; i++)
        {
            var signedDistance = Vector2.Dot(viewerLeftContour[i] - midpoint, pairAxis);
            if (signedDistance > -halfGap)
                viewerLeftContour[i] -= pairAxis * (signedDistance + halfGap);
        }
        for (var i = 0; i < viewerRightContour.Length; i++)
        {
            var signedDistance = Vector2.Dot(viewerRightContour[i] - midpoint, pairAxis);
            if (signedDistance < halfGap)
                viewerRightContour[i] += pairAxis * (halfGap - signedDistance);
        }
    }

    private static void CalculateBoobPerspectiveScales(
        IReadOnlyList<ReanimatorExternalBone> bones,
        Vector2 viewerLeftNipple,
        Vector2 viewerRightNipple,
        out float viewerLeftScale,
        out float viewerRightScale)
    {
        viewerLeftScale = 1f;
        viewerRightScale = 1f;
        if (!TryFindExternalBone(bones, "neck", out var neck) ||
            !TryFindExternalBone(bones, "hips", out var hips))
        {
            return;
        }

        var torsoAxis = neck.WorldPosition - hips.WorldPosition;
        if (torsoAxis.sqrMagnitude <= 1f)
            return;
        torsoAxis.Normalize();
        var leftDistance = Mathf.Abs(Cross2D(torsoAxis, viewerLeftNipple - hips.WorldPosition));
        var rightDistance = Mathf.Abs(Cross2D(torsoAxis, viewerRightNipple - hips.WorldPosition));
        var farther = Mathf.Max(leftDistance, rightDistance);
        var nearer = Mathf.Min(leftDistance, rightDistance);
        if (farther <= 1f || nearer / farther >= 0.9f)
            return;

        var foreshortenedScale = Mathf.Clamp(nearer / farther, 0.72f, 1f);
        if (leftDistance > rightDistance)
            viewerLeftScale = foreshortenedScale;
        else
            viewerRightScale = foreshortenedScale;
    }

    private static void TryGetViewerArmBones(
        IReanimatorExternalPanelHost host,
        IReadOnlyList<ReanimatorExternalBone> bones,
        out ReanimatorExternalBone? viewerLeftArm,
        out ReanimatorExternalBone? viewerRightArm)
    {
        viewerLeftArm = null;
        viewerRightArm = null;
        if (bones == null)
            return;

        ReanimatorExternalBone? first = null;
        ReanimatorExternalBone? second = null;
        for (var i = 0; i < bones.Count; i++)
        {
            var name = NormalizeSemanticBoneName(bones[i].Name);
            if (name != "larm" && name != "rarm")
                continue;
            if (!first.HasValue)
                first = bones[i];
            else
            {
                second = bones[i];
                break;
            }
        }
        if (!first.HasValue || !second.HasValue)
            return;

        if (host.DocumentToPreview(first.Value.WorldPosition).x <=
            host.DocumentToPreview(second.Value.WorldPosition).x)
        {
            viewerLeftArm = first;
            viewerRightArm = second;
        }
        else
        {
            viewerLeftArm = second;
            viewerRightArm = first;
        }
    }

    private static void ClampBoobContourInsideArm(
        Vector2[] contour,
        Vector2 torsoCenter,
        Vector2 sideAxis,
        bool viewerLeft,
        ReanimatorExternalBone arm,
        float nippleSpacing)
    {
        if (contour == null || contour.Length == 0)
            return;

        var armDirection3 = arm.WorldRotation * Vector3.right;
        var armDirection = new Vector2(armDirection3.x, armDirection3.y).normalized;
        if (armDirection.sqrMagnitude <= 0.0001f)
            return;

        var inwardNormal = new Vector2(-armDirection.y, armDirection.x);
        if (Vector2.Dot(torsoCenter - arm.WorldPosition, inwardNormal) < 0f)
            inwardNormal = -inwardNormal;
        var allowedOutsideDistance = -nippleSpacing * 0.025f;
        var outerSign = viewerLeft ? -1f : 1f;
        for (var i = 0; i < contour.Length; i++)
        {
            if (Vector2.Dot(contour[i] - torsoCenter, sideAxis) * outerSign <= 0f)
                continue;

            var signedDistance = Vector2.Dot(contour[i] - arm.WorldPosition, inwardNormal);
            if (signedDistance >= allowedOutsideDistance)
                continue;
            contour[i] += inwardNormal * (allowedOutsideDistance - signedDistance);
        }
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float RefineBoobRadiusFromPixels(
        IReanimatorExternalPanelHost host,
        Texture2D texture,
        Vector2 center,
        Vector2 direction,
        float expectedRadius,
        float nippleSpacing)
    {
        var bestRadius = expectedRadius;
        var bestGradient = 0f;
        var bestScore = float.MinValue;
        var sampleStep = Mathf.Max(1.5f, nippleSpacing * 0.012f);
        const int samples = 25;
        for (var i = 0; i < samples; i++)
        {
            var t = Mathf.Lerp(0.76f, 1.2f, i / (samples - 1f));
            var radius = expectedRadius * t;
            var point = center + direction * radius;
            var before = SampleWorkspaceColor(host, texture, point - direction * sampleStep);
            var after = SampleWorkspaceColor(host, texture, point + direction * sampleStep);
            var gradient = ColorDistance(before, after);
            var priorPenalty = Mathf.Abs(t - 1f) * 0.16f;
            var score = gradient - priorPenalty;
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestGradient = gradient;
            bestRadius = radius;
        }

        // Skin highlights create many weak internal gradients. Only a meaningful
        // crease/volume transition is allowed to pull a point away from anatomy.
        return bestGradient >= 0.055f ? bestRadius : expectedRadius;
    }

    private static Color SampleWorkspaceColor(
        IReanimatorExternalPanelHost host,
        Texture2D texture,
        Vector2 documentPoint)
    {
        var normalized = DocumentToWorkspaceNormalized(host, documentPoint);
        return texture.GetPixelBilinear(normalized.x, 1f - normalized.y);
    }

    private static float ColorDistance(Color a, Color b)
    {
        var dr = a.r - b.r;
        var dg = a.g - b.g;
        var db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static bool TryFindExternalBone(
        IReadOnlyList<ReanimatorExternalBone> bones,
        string name,
        out ReanimatorExternalBone result)
    {
        result = default;
        if (bones == null)
            return false;

        for (var i = 0; i < bones.Count; i++)
        {
            if (!string.Equals(bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            result = bones[i];
            return true;
        }
        return false;
    }

    private async void RunNipplesFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null || nipplesRunning)
            return;

        EnsureSettingsLoaded();
        // A search is a replacement operation. Remove every trace of the
        // previous result before capturing or awaiting Comfy so stale targets
        // can neither remain visible nor be mistaken for the new result.
        ClearNippleTargets();
        nipplesRunning = true;
        actionStatus = "Nipples: capturing workspace";
        actionStatusIsError = false;
        host.RepaintHost();
        try
        {
            var screenshotPath = host.CaptureWorkspacePng();
            actionStatus = "Nipples: analyzing with Qwen3-VL";
            host.ShowNotification(actionStatus);
            host.RepaintHost();

            var result = await ComfimatorComfyClient.RunNippleVisionAsync(serverUrl, screenshotPath);
            leftNippleTargetDoc = host.WorkspaceNormalizedToDocument(result.ViewerLeft);
            rightNippleTargetDoc = host.WorkspaceNormalizedToDocument(result.ViewerRight);
            leftNippleConfidence = result.LeftConfidence;
            rightNippleConfidence = result.RightConfidence;
            nippleTargetsVisible = true;
            nippleTargetAssetPath = host.CurrentAssetPath ?? string.Empty;
            connectionStatus = "Connected";
            var finalViewerLeft = DocumentToWorkspaceNormalized(host, leftNippleTargetDoc);
            var finalViewerRight = DocumentToWorkspaceNormalized(host, rightNippleTargetDoc);
            actionStatus = string.Format(
                "Nipples v6 workspace pixels: final left ({0:0.000}, {1:0.000}), " +
                "right ({2:0.000}, {3:0.000})",
                finalViewerLeft.x,
                finalViewerLeft.y,
                finalViewerRight.x,
                finalViewerRight.y);
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            ClearNippleTargets();
            if (IsComfyUnavailable(ex))
                ShowComfyUnavailable(host);
            else
            {
                actionStatus = "Nipples failed: " + ex.Message;
                actionStatusIsError = true;
                host.ShowNotification(actionStatus);
            }
        }
        finally
        {
            nipplesRunning = false;
            host.RepaintHost();
        }
    }

    private void ClearNippleTargets()
    {
        nippleTargetsVisible = false;
        nippleTargetAssetPath = string.Empty;
        leftNippleTargetDoc = Vector2.zero;
        rightNippleTargetDoc = Vector2.zero;
        leftNippleConfidence = 0f;
        rightNippleConfidence = 0f;
    }

    private bool TryGetCurrentNippleTargets(
        IReanimatorExternalPanelHost host,
        out Vector2 viewerLeftDocumentPoint,
        out Vector2 viewerRightDocumentPoint)
    {
        viewerLeftDocumentPoint = default;
        viewerRightDocumentPoint = default;
        if (!nippleTargetsVisible || host == null ||
            !string.Equals(
                nippleTargetAssetPath,
                host.CurrentAssetPath ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) ||
            Vector2.Distance(leftNippleTargetDoc, rightNippleTargetDoc) <= 4f)
        {
            return false;
        }

        viewerLeftDocumentPoint = leftNippleTargetDoc;
        viewerRightDocumentPoint = rightNippleTargetDoc;
        return true;
    }

    private static bool TryGetSemanticNippleBoneTargets(
        IReanimatorExternalPanelHost host,
        out Vector2 viewerLeftDocumentPoint,
        out Vector2 viewerRightDocumentPoint)
    {
        viewerLeftDocumentPoint = default;
        viewerRightDocumentPoint = default;
        if (!TryGetSemanticNippleBones(host, out var viewerLeftBone, out var viewerRightBone))
            return false;

        viewerLeftDocumentPoint = viewerLeftBone.WorldPosition;
        viewerRightDocumentPoint = viewerRightBone.WorldPosition;
        return true;
    }

    private static bool TryGetSemanticNippleBones(
        IReanimatorExternalPanelHost host,
        out ReanimatorExternalBone viewerLeftBone,
        out ReanimatorExternalBone viewerRightBone)
    {
        viewerLeftBone = default;
        viewerRightBone = default;
        var bones = host.CaptureBones();
        if (bones == null || bones.Count < 2)
            return false;

        ReanimatorExternalBone? characterLeft = null;
        ReanimatorExternalBone? characterRight = null;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var name = NormalizeSemanticBoneName(bone.Name);
            if (name == "nipplel" || name == "lnipple" || name == "leftnipple")
                characterLeft = bone;
            else if (name == "nippler" || name == "rnipple" || name == "rightnipple")
                characterRight = bone;
        }

        if (!characterLeft.HasValue || !characterRight.HasValue)
            return false;

        var first = characterLeft.Value;
        var second = characterRight.Value;
        if (host.DocumentToPreview(first.WorldPosition).x <= host.DocumentToPreview(second.WorldPosition).x)
        {
            viewerLeftBone = first;
            viewerRightBone = second;
        }
        else
        {
            viewerLeftBone = second;
            viewerRightBone = first;
        }

        return true;
    }

    private static string NormalizeSemanticBoneName(string name)
    {
        return (name ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
    }

    private void DrawFashionableButton(IReanimatorExternalPanelHost host)
    {
        var hasClFolder = host != null && host.HasFashionableFolder();
        using (new EditorGUI.DisabledScope(
                   fashionableRunning || allInRunning || autoGeometryRunning || autoSkeletonRunning || !hasClFolder))
        {
            if (!GUILayout.Button(fashionableRunning ? "Fashionable..." : "Fashionable", GUILayout.Height(22f)))
                return;
        }

        RunFashionableFromButton(host);
    }

    private void RunFashionableFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null || fashionableRunning || !host.HasFashionableFolder())
            return;

        fashionableRunning = true;
        actionStatus = "Fashionable: applying APL";
        actionStatusIsError = false;
        host.RepaintHost();
        try
        {
            actionStatus = "Fashionable: " + host.ApplyFashionable();
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            actionStatus = "Fashionable failed: " + ex.Message;
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
        }
        finally
        {
            fashionableRunning = false;
            host.RepaintHost();
        }
    }

    private void RunAutoWeightsFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null)
            return;

        try
        {
            actionStatus = RunAutoWeights(host);
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            actionStatus = "Auto Weights failed: " + ex.Message;
            host.ShowNotification(actionStatus);
        }
        finally
        {
            host.RepaintHost();
        }
    }

    private void DrawAllInButton(IReanimatorExternalPanelHost host)
    {
        var preflightValid = host != null && host.TryValidateAllInPreflight(out _);
        using (new EditorGUI.DisabledScope(
                   !preflightValid || allInRunning || autoGeometryRunning || autoSkeletonRunning || fashionableRunning))
        {
            if (!GUILayout.Button(allInRunning ? "ALLIN..." : "ALLIN", GUILayout.Height(22f)))
                return;
        }

        RunAllIn(host);
    }

    private void DrawCookPrefabButton(IReanimatorExternalPanelHost host)
    {
        using (new EditorGUI.DisabledScope(allInRunning || autoGeometryRunning || autoSkeletonRunning || fashionableRunning))
        {
            if (!GUILayout.Button("Cook Prefab", GUILayout.Height(22f)))
                return;
        }

        RunCookPrefabFromButton(host);
    }

    private void RunCookPrefabFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null)
            return;

        try
        {
            actionStatus = "Cook Prefab: " + host.CookPrefabNextToPsd();
            actionStatusIsError = false;
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            actionStatus = "Cook Prefab failed: " + ex.Message;
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
        }
        finally
        {
            host.RepaintHost();
        }
    }

    private void RunAutoGeometryFromButton(IReanimatorExternalPanelHost host)
    {
        if (host == null || autoGeometryRunning)
            return;

        autoGeometryRunning = true;
        actionStatus = "Auto Geometry: generating";
        actionStatusIsError = false;
        host.RepaintHost();

        try
        {
            actionStatus = RunAutoGeometry(host);
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            actionStatus = "Auto Geometry failed: " + ex.Message;
            actionStatusIsError = true;
            host.ShowNotification(actionStatus);
        }
        finally
        {
            autoGeometryRunning = false;
            host.RepaintHost();
        }
    }

    private async void RunConnectionCheck(IReanimatorExternalPanelHost host)
    {
        EnsureSettingsLoaded();
        if (connectionCheckRunning)
            return;

        connectionCheckRunning = true;
        connectionStatus = "Checking " + (string.IsNullOrWhiteSpace(serverUrl) ? ComfimatorComfyClient.DefaultServerUrl : serverUrl.Trim());
        actionStatusIsError = false;
        host?.RepaintHost();

        try
        {
            connectionStatus = await ComfimatorComfyClient.CheckConnectionAsync(serverUrl);
            host?.ShowNotification(connectionStatus);
        }
        catch (Exception ex)
        {
            connectionStatus = "Disconnected: " + ex.Message;
            actionStatus = "Comfy is unavailable.";
            actionStatusIsError = true;
            host?.ShowNotification(connectionStatus);
        }
        finally
        {
            connectionCheckRunning = false;
            host?.RepaintHost();
        }
    }

    private async void RunAutoSkeletonConnectionTest(IReanimatorExternalPanelHost host)
    {
        if (host == null || autoSkeletonRunning)
            return;

        autoSkeletonRunning = true;
        actionStatus = "Auto Skeleton: starting";
        actionStatusIsError = false;
        host.RepaintHost();

        try
        {
            var result = await RunAutoSkeletonAsync(host, "Auto Skeleton", true, true);
            actionStatus = "Auto Skeleton: " + result;
            host.ShowNotification(actionStatus);
        }
        catch (Exception ex)
        {
            if (IsComfyUnavailable(ex))
                ShowComfyUnavailable(host);
            else
            {
                actionStatus = "Auto Skeleton failed: " + ex.Message;
                actionStatusIsError = true;
                host.ShowNotification(actionStatus);
            }
        }
        finally
        {
            autoSkeletonRunning = false;
            host.RepaintHost();
        }
    }

    private async void RunAllIn(IReanimatorExternalPanelHost host)
    {
        if (host == null || allInRunning || autoGeometryRunning || autoSkeletonRunning)
            return;

        if (!host.TryValidateAllInPreflight(out var preflightError))
        {
            actionStatus = "ALLIN blocked: " + preflightError;
            actionStatusIsError = true;
            allInBlockingStatus = actionStatus;
            Debug.LogWarning("[Reanimator][ALLIN] blocked before start: " + preflightError);
            host.ShowNotification(actionStatus);
            host.RepaintHost();
            return;
        }

        EnsureSettingsLoaded();
        allInRunning = true;
        SetAllInProgress("ALLIN: checking Comfy", 0.02f, 0.06f);
        actionStatusIsError = false;
        host.ShowNotification(actionStatus);
        host.RepaintHost();

        try
        {
            connectionStatus = await ComfimatorComfyClient.CheckConnectionAsync(serverUrl);

            var geometry = "Auto Geometry disabled; using imported PSD geometry";
            if (AllInAutoGeometryEnabled)
            {
                SetAllInProgress("ALLIN: generating geometry", 0.08f, 0.24f);
                host.RepaintHost();
                geometry = RunAutoGeometry(host);
            }

            SetAllInProgress("ALLIN: geometry complete", 0.28f, 0.28f);
            host.RepaintHost();

            SetAllInProgress("ALLIN: generating skeleton", 0.34f, 0.68f);
            host.RepaintHost();
            var skeleton = await RunAutoSkeletonAsync(
                host,
                "ALLIN Auto Skeleton",
                AllInBreastProcessingEnabled,
                AllInEyeProcessingEnabled,
                useQwenNipplePositioning: false);

            SetAllInProgress("ALLIN: skeleton complete", 0.72f, 0.72f);
            host.RepaintHost();

            if (AllInAutoGeometryEnabled)
            {
                // Auto Skeleton is asynchronous and may span an editor-domain
                // refresh or a stale preview-cache rebuild. Re-run the
                // deterministic geometry stage immediately before weights so
                // ALLIN can never bind or display the importer mesh that was
                // present before this run.
                SetAllInProgress("ALLIN: finalizing geometry", 0.74f, 0.77f);
                host.RepaintHost();
                geometry = RunAutoGeometry(host);
            }

            SetAllInProgress("ALLIN: applying weights", 0.78f, 0.92f);
            host.RepaintHost();
            var weights = RunAutoWeights(host);
            if (AllInPostWeightsRestCommitEnabled)
            {
                // Auto Weights already builds bind matrices for the generated
                // skeleton. This extra global rebind is disabled diagnostically.
                host.CommitCurrentBonePoseAsRest();
            }

            var faceEyes = "eye processing disabled for diagnostic test";
            if (AllInEyeProcessingEnabled)
            {
                SetAllInProgress("ALLIN: applying Face Eyes", 0.94f, 0.999f);
                actionStatusIsError = false;
                host.RepaintHost();
                faceEyes = RunFaceEyes(host);
            }

            SetAllInProgress("ALLIN complete", 1f, 1f);
            actionStatus = "ALLIN complete: " + geometry + "; Auto Skeleton: " + skeleton + "; " +
                           weights + "; " + faceEyes;
            host.ShowNotification("ALLIN complete");
        }
        catch (Exception ex)
        {
            if (IsComfyUnavailable(ex))
            {
                ShowComfyUnavailable(host);
                allInBlockingStatus = actionStatus;
            }
            else
            {
                actionStatus = "ALLIN failed: " + ex.Message;
                actionStatusIsError = true;
                allInBlockingStatus = actionStatus;
                host.ShowNotification(actionStatus);
            }
        }
        finally
        {
            allInRunning = false;
            host.RepaintHost();
        }
    }

    private void SetAllInProgress(string status, float progress, float softMaxProgress)
    {
        actionStatus = status;
        allInBlockingStatus = status;
        allInBlockingProgress = Mathf.Clamp01(progress);
        allInBlockingStageStartProgress = allInBlockingProgress;
        allInBlockingStageSoftMaxProgress = Mathf.Clamp01(Mathf.Max(progress, softMaxProgress));
        allInBlockingStageStartTime = EditorApplication.timeSinceStartup;
    }

    private string RunAutoGeometry(IReanimatorExternalPanelHost host)
    {
        if (host == null)
            throw new InvalidOperationException("Reanimator host is not ready.");

        return "Auto Geometry: " + host.ApplyDefaultAutoGeometry();
    }

    private async Task<string> RunAutoSkeletonAsync(
        IReanimatorExternalPanelHost host,
        string label,
        bool includeBreastProcessing,
        bool includeEyeProcessing,
        bool useQwenNipplePositioning = true)
    {
        if (host == null)
            throw new InvalidOperationException("Reanimator host is not ready.");

        EnsureSettingsLoaded();
        SaveSettings();

        if (allInRunning)
            SetAllInProgress(label + ": capturing workspace", 0.38f, 0.44f);
        else
            actionStatus = label + ": capturing workspace";
        actionStatusIsError = false;
        host.RepaintHost();

        var secondaryArmLayerCount = host.SecondaryArmLayerCount;
        if (secondaryArmLayerCount == 1)
            throw new InvalidOperationException("Auto Skeleton needs both l_arm_2 and r_arm_2; only one secondary arm layer was found.");
        var hasSecondaryArmPair = secondaryArmLayerCount == 2;
        var screenshotPath = hasSecondaryArmPair
            ? host.CaptureAutoSkeletonArmPassWorkspacePng(false)
            : host.CaptureWorkspacePng();
        if (allInRunning)
            SetAllInProgress(label + ": sending primary arm pass to Comfy", 0.44f, 0.56f);
        else
            actionStatus = label + ": sending primary arm pass to Comfy";
        host.ShowNotification(label + ": primary arm pass sent to Comfy");
        host.RepaintHost();

        var output = await ComfimatorComfyClient.RunAutoSkeletonEchoAsync(
            serverUrl,
            screenshotPath,
            autoSkeletonWorkflowPath);

        if (string.IsNullOrWhiteSpace(output.PoseJson))
            throw new InvalidOperationException("Comfy did not return pose keypoint JSON.");

        string applyStatus;
        var outputSummary = output.OutputFileName + ", " + output.PoseJsonFileName;
        if (hasSecondaryArmPair)
        {
            if (allInRunning)
                SetAllInProgress(label + ": capturing secondary arm pass", 0.56f, 0.62f);
            else
                actionStatus = label + ": capturing secondary arm pass";
            host.RepaintHost();

            var secondaryScreenshotPath = host.CaptureAutoSkeletonArmPassWorkspacePng(true);
            if (allInRunning)
                SetAllInProgress(label + ": sending secondary arm pass to Comfy", 0.62f, 0.68f);
            else
                actionStatus = label + ": sending secondary arm pass to Comfy";
            host.ShowNotification(label + ": secondary arm pass sent to Comfy");
            host.RepaintHost();

            var secondaryOutput = await ComfimatorComfyClient.RunAutoSkeletonEchoAsync(
                serverUrl,
                secondaryScreenshotPath,
                autoSkeletonWorkflowPath);
            if (string.IsNullOrWhiteSpace(secondaryOutput.PoseJson))
                throw new InvalidOperationException("Comfy did not return pose keypoint JSON for the secondary arm pass.");

            applyStatus = host.ApplyAutoSkeletonPoseJsonWithSecondaryArms(
                output.PoseJson,
                secondaryOutput.PoseJson,
                AutoSkeletonNamingReferenceAssetPath,
                autoSkeletonIncludeFingers,
                includeBreastProcessing,
                includeEyeProcessing);
            outputSummary += "; second arms: " + secondaryOutput.OutputFileName + ", " + secondaryOutput.PoseJsonFileName;
        }
        else
        {
            applyStatus = host.ApplyAutoSkeletonPoseJson(
                output.PoseJson,
                AutoSkeletonNamingReferenceAssetPath,
                autoSkeletonIncludeFingers,
                includeBreastProcessing,
                includeEyeProcessing);
        }
        var nippleStatus = includeBreastProcessing
            ? "breast controls kept at deterministic skeleton positions"
            : "breast controls disabled for diagnostic test";
        if (includeBreastProcessing && useQwenNipplePositioning)
        {
            nippleStatus = await ApplyAutoSkeletonNipplePositionsAsync(
                host,
                screenshotPath,
                label);
        }
        connectionStatus = "Connected";
        return applyStatus + "; " + nippleStatus + " (" + outputSummary + ")";
    }

    private async Task<string> ApplyAutoSkeletonNipplePositionsAsync(
        IReanimatorExternalPanelHost host,
        string screenshotPath,
        string label)
    {
        if (allInRunning)
            SetAllInProgress(label + ": locating nipples", 0.68f, 0.71f);
        else
            actionStatus = label + ": locating nipples";
        host.RepaintHost();

        Vector2 targetViewerLeft;
        Vector2 targetViewerRight;
        float targetLeftConfidence;
        float targetRightConfidence;
        string source;

        try
        {
            var vision = await ComfimatorComfyClient.RunNippleVisionAsync(serverUrl, screenshotPath);
            targetViewerLeft = host.WorkspaceNormalizedToDocument(vision.ViewerLeft);
            targetViewerRight = host.WorkspaceNormalizedToDocument(vision.ViewerRight);
            targetLeftConfidence = vision.LeftConfidence;
            targetRightConfidence = vision.RightConfidence;
            source = "Comfy + workspace pixel refinement";
        }
        catch (Exception ex)
        {
            return "nipple positions unchanged (" + ex.Message + ")";
        }

        if (!TrySetSemanticNippleBonePositions(host, targetViewerLeft, targetViewerRight))
            return "nipple bones were not found";

        leftNippleTargetDoc = targetViewerLeft;
        rightNippleTargetDoc = targetViewerRight;
        leftNippleConfidence = targetLeftConfidence;
        rightNippleConfidence = targetRightConfidence;
        nippleTargetsVisible = true;
        nippleTargetAssetPath = host.CurrentAssetPath ?? string.Empty;
        host.RepaintHost();
        return "nipple bones positioned from " + source;
    }

    private static bool TrySetSemanticNippleBonePositions(
        IReanimatorExternalPanelHost host,
        Vector2 viewerLeftDocumentPoint,
        Vector2 viewerRightDocumentPoint)
    {
        if (!TryGetSemanticNippleBones(host, out var viewerLeftBone, out var viewerRightBone))
            return false;

        host.BeginUndo("Auto Skeleton Nipple Positions");
        host.SetBoneWorldTransform(
            viewerLeftBone.Index,
            viewerLeftDocumentPoint,
            viewerLeftBone.WorldRotation,
            viewerLeftBone.Length,
            true);
        host.SetBoneWorldTransform(
            viewerRightBone.Index,
            viewerRightDocumentPoint,
            viewerRightBone.WorldRotation,
            viewerRightBone.Length,
            true);
        // Auto Skeleton creates a fresh skeleton and captures its rest pose before
        // Comfy refines the nipple positions. The refined anchors are part of that
        // new skeleton, not a preview deformation. Commit them as the new rest
        // pose before Auto Weights builds bind matrices.
        host.CommitCurrentBonePoseAsRest();
        return true;
    }

    private void ShowComfyUnavailable(IReanimatorExternalPanelHost host)
    {
        connectionStatus = "Disconnected";
        actionStatus = "Comfy is unavailable.";
        actionStatusIsError = true;
        host?.ShowNotification("Comfy is unavailable.");
    }

    private static bool IsComfyUnavailable(Exception ex)
    {
        while (ex != null)
        {
            if (ex is HttpRequestException || ex is TaskCanceledException)
                return true;

            ex = ex.InnerException;
        }

        return false;
    }

    private static string RunAutoWeights(IReanimatorExternalPanelHost host)
    {
        if (host == null)
            throw new InvalidOperationException("Reanimator host is not ready.");

        return "Auto Weights: " + host.ApplyDeterministicAutoWeights();
    }

    private static string RunFaceEyes(IReanimatorExternalPanelHost host)
    {
        if (host == null)
            throw new InvalidOperationException("Reanimator host is not ready.");

        return "Face Eyes: " + host.ApplyFaceEyesAutomation();
    }

    private void HandleInput(IReanimatorExternalPanelHost host, IReadOnlyList<ReanimatorExternalBone> bones)
    {
        var e = Event.current;
        if (host == null || e == null || !IsMouseEvent(e))
            return;

        var previewRect = host.PreviewRect;
        if (!previewRect.Contains(e.mousePosition) && e.type != EventType.MouseUp)
            return;

        var hit = FindBone(e.mousePosition, host, bones);
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            hoveredBoneIndex = hit.BoneIndex;
            if (hit.BoneIndex >= 0)
                host.RepaintHost();
        }

        if (e.type == EventType.MouseDown && e.button == 0 && hit.BoneIndex >= 0)
        {
            selectedBoneIndex = hit.BoneIndex;
            dragBoneIndex = hit.BoneIndex;
            dragMode = hit.Handle == BoneHandle.Base ? DragMode.MoveBase : DragMode.RotateTip;
            dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = dragControlId;
            host.BeginUndo("Comfimator Bone Transform");
            host.SelectBone(selectedBoneIndex);

            var bone = bones[dragBoneIndex];
            var mouseDoc = host.PreviewToDocument(e.mousePosition);
            dragBaseOffsetDoc = bone.WorldPosition - mouseDoc;
            e.Use();
            host.RepaintHost();
            return;
        }

        if (e.type == EventType.MouseDrag &&
            GUIUtility.hotControl == dragControlId &&
            dragBoneIndex >= 0 &&
            dragBoneIndex < bones.Count)
        {
            ApplyDrag(host, bones[dragBoneIndex], e.mousePosition, commit: false);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
        {
            if (dragBoneIndex >= 0 && dragBoneIndex < bones.Count)
                ApplyDrag(host, bones[dragBoneIndex], e.mousePosition, commit: true);

            GUIUtility.hotControl = 0;
            dragBoneIndex = -1;
            dragMode = DragMode.None;
            e.Use();
            host.RepaintHost();
        }
    }

    private void ApplyDrag(IReanimatorExternalPanelHost host, ReanimatorExternalBone bone, Vector2 mousePreview, bool commit)
    {
        var mouseDoc = host.PreviewToDocument(mousePreview);
        var worldPosition = bone.WorldPosition;
        var worldRotation = bone.WorldRotation;
        var length = Mathf.Max(1f, bone.Length);

        if (dragMode == DragMode.MoveBase)
        {
            worldPosition = mouseDoc + dragBaseOffsetDoc;
        }
        else if (dragMode == DragMode.RotateTip)
        {
            var direction = mouseDoc - bone.WorldPosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                worldRotation = Quaternion.AngleAxis(angle, Vector3.forward);
                length = direction.magnitude;
            }
        }

        host.SetBoneWorldTransform(bone.Index, worldPosition, worldRotation, length, commit);
    }

    private void DrawBones(IReanimatorExternalPanelHost host, IReadOnlyList<ReanimatorExternalBone> bones)
    {
        Handles.BeginGUI();
        var previousColor = Handles.color;

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (!bone.Visible || bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count)
                continue;

            var parent = bones[bone.ParentIndex];
            if (!parent.Visible)
                continue;

            Handles.color = WithAlpha(bone.Color, 0.55f);
            Handles.DrawDottedLine(host.DocumentToPreview(bone.WorldPosition), BoneTip(host, parent), 4f);
        }

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (!bone.Visible)
                continue;

            var selected = bone.Index == selectedBoneIndex || bone.Selected;
            var hovered = bone.Index == hoveredBoneIndex;
            DrawBone(host, bone, selected, hovered);
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawNippleTargets(IReanimatorExternalPanelHost host)
    {
        if (!nippleTargetsVisible || host == null ||
            !string.Equals(nippleTargetAssetPath, host.CurrentAssetPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Handles.BeginGUI();
        DrawNippleTarget(host.DocumentToPreview(leftNippleTargetDoc), leftNippleConfidence);
        DrawNippleTarget(host.DocumentToPreview(rightNippleTargetDoc), rightNippleConfidence);
        Handles.EndGUI();
    }

    private void DrawBoobContours(IReanimatorExternalPanelHost host)
    {
        if (!boobContoursVisible || host == null ||
            !string.Equals(
                boobContourAssetPath,
                host.CurrentAssetPath ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Handles.BeginGUI();
        DrawBoobContour(
            host,
            leftBoobContourDoc,
            new Color(0.15f, 1f, 0.85f, 1f));
        DrawBoobContour(
            host,
            rightBoobContourDoc,
            new Color(1f, 0.25f, 0.72f, 1f));
        Handles.EndGUI();
    }

    private static void DrawBoobContour(
        IReanimatorExternalPanelHost host,
        IReadOnlyList<Vector2> contour,
        Color color)
    {
        if (contour == null || contour.Count < 3)
            return;

        var points = new Vector3[contour.Count + 1];
        for (var i = 0; i < contour.Count; i++)
            points[i] = host.DocumentToPreview(contour[i]);
        points[points.Length - 1] = points[0];

        Handles.color = new Color(0f, 0f, 0f, 0.9f);
        Handles.DrawAAPolyLine(5f, points);
        Handles.color = color;
        Handles.DrawAAPolyLine(2.5f, points);
        for (var i = 0; i < contour.Count; i++)
        {
            Handles.color = new Color(0f, 0f, 0f, 0.9f);
            Handles.DrawSolidDisc(points[i], Vector3.forward, 3.8f);
            Handles.color = color;
            Handles.DrawSolidDisc(points[i], Vector3.forward, 2.4f);
        }
    }

    private static void DrawNippleTarget(Vector2 point, float confidence)
    {
        var color = confidence >= 0.65f
            ? new Color(0.2f, 1f, 0.25f, 1f)
            : confidence >= 0.3f
                ? new Color(1f, 0.82f, 0.1f, 1f)
                : new Color(1f, 0.18f, 0.14f, 1f);

        Handles.color = new Color(0f, 0f, 0f, 0.9f);
        Handles.DrawWireDisc(point, Vector3.forward, 12f);
        Handles.DrawWireDisc(point, Vector3.forward, 6.5f);
        Handles.DrawAAPolyLine(4f, point + Vector2.left * 15f, point + Vector2.right * 15f);
        Handles.DrawAAPolyLine(4f, point + Vector2.down * 15f, point + Vector2.up * 15f);

        Handles.color = color;
        Handles.DrawWireDisc(point, Vector3.forward, 10.5f);
        Handles.DrawWireDisc(point, Vector3.forward, 5f);
        Handles.DrawAAPolyLine(2f, point + Vector2.left * 14f, point + Vector2.right * 14f);
        Handles.DrawAAPolyLine(2f, point + Vector2.down * 14f, point + Vector2.up * 14f);
        Handles.DrawSolidDisc(point, Vector3.forward, 2f);
    }

    private static void DrawBone(IReanimatorExternalPanelHost host, ReanimatorExternalBone bone, bool selected, bool hovered)
    {
        var start = host.DocumentToPreview(bone.WorldPosition);
        var end = BoneTip(host, bone);
        var thickness = selected ? 3.4f : hovered ? 2.8f : 2f;
        var outline = selected || hovered
            ? new Color(1f, 0.86f, 0.14f, 1f)
            : new Color(0f, 0f, 0f, 0.95f);

        Handles.color = outline;
        Handles.DrawAAPolyLine(thickness + 2f, start, end);
        Handles.color = WithAlpha(bone.Color, 0.95f);
        Handles.DrawAAPolyLine(thickness, start, end);
        Handles.DrawSolidDisc(start, Vector3.forward, selected ? 4.4f : 3.4f);
        Handles.color = selected ? new Color(1f, 0.86f, 0.14f, 1f) : WithAlpha(bone.Color, 0.9f);
        Handles.DrawWireDisc(end, Vector3.forward, selected ? 5.4f : 4.2f);
    }

    private static Vector2 BoneTip(IReanimatorExternalPanelHost host, ReanimatorExternalBone bone)
    {
        var direction3 = bone.WorldRotation * Vector3.right;
        var direction = new Vector2(direction3.x, direction3.y);
        return host.DocumentToPreview(bone.WorldPosition + direction * Mathf.Max(1f, bone.Length));
    }

    private static BoneHit FindBone(Vector2 previewPoint, IReanimatorExternalPanelHost host, IReadOnlyList<ReanimatorExternalBone> bones)
    {
        var best = new BoneHit(-1, BoneHandle.None);
        var bestDistance = float.MaxValue;

        for (var i = bones.Count - 1; i >= 0; i--)
        {
            var bone = bones[i];
            if (!bone.Visible)
                continue;

            var start = host.DocumentToPreview(bone.WorldPosition);
            var end = BoneTip(host, bone);
            var baseDistance = Vector2.Distance(previewPoint, start);
            if (baseDistance <= BaseHitRadius && baseDistance < bestDistance)
            {
                best = new BoneHit(bone.Index, BoneHandle.Base);
                bestDistance = baseDistance;
            }

            var tipDistance = Vector2.Distance(previewPoint, end);
            if (tipDistance <= TipHitRadius && tipDistance < bestDistance)
            {
                best = new BoneHit(bone.Index, BoneHandle.Tip);
                bestDistance = tipDistance;
            }

            var bodyDistance = DistanceToSegment(previewPoint, start, end);
            if (bodyDistance <= BoneHitDistance && bodyDistance < bestDistance)
            {
                best = new BoneHit(bone.Index, BoneHandle.Body);
                bestDistance = bodyDistance;
            }
        }

        return best;
    }

    private static bool IsMouseEvent(Event e)
    {
        return e.type == EventType.MouseDown ||
               e.type == EventType.MouseDrag ||
               e.type == EventType.MouseUp ||
               e.type == EventType.MouseMove;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var magnitude = ab.sqrMagnitude;
        if (magnitude <= 0.0001f)
            return Vector2.Distance(point, a);

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / magnitude);
        return Vector2.Distance(point, a + ab * t);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}
#endif
