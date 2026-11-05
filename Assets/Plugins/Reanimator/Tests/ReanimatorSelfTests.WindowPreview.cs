#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void ReadinessOverlayReportsCurrentScore()
    {
        AssertEqual(100, Reanimator.ReanimatorReadinessPercent, "Readiness progress badge should show the current overall score.");
        AssertTrue(Reanimator.ReanimatorReadinessLines != null, "Readiness lines should exist.");
        AssertEqual(9, Reanimator.ReanimatorReadinessLines.Length, "Readiness overlay should list the full breakdown.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("Slider zero-weight identity")),
            "Readiness overlay should report the completed slider production work.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("explicit Projector target contract")),
            "Readiness overlay should report the completed projector target contract.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("Volume preset assets")),
            "Readiness overlay should report the completed volume hardening work.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("Undo/Redo audit")),
            "Readiness overlay should report the completed undo/redo audit.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("release PSD fixtures")),
            "Readiness overlay should report the completed release fixture matrix.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("guarded file lifecycle")),
            "Readiness overlay should report the completed file lifecycle work.");
        AssertTrue(
            Reanimator.ReanimatorReadinessLines.Any(line => line.Contains("release UX/perf cleanup")),
            "Readiness overlay should report the completed UX/perf cleanup.");
        AssertFalse(
            Reanimator.ReanimatorReadinessLines.Any(line => line.StartsWith("Missing:", System.StringComparison.Ordinal)),
            "Release readiness should not leave visible missing items.");
    }

    private static void ReadinessProgressIsCalculatedFromChecklist()
    {
        var items = new[]
        {
            new Reanimator.ReanimatorReadinessItem("done-a", 40, "evidence-a"),
            new Reanimator.ReanimatorReadinessItem("missing-b", 60)
        };

        AssertEqual(
            40,
            Reanimator.CalculateReanimatorReadinessPercent(items, caseName => string.Equals(caseName, "evidence-a", System.StringComparison.Ordinal)),
            "Readiness percent should be calculated from passed evidence points.");
        var lines = Reanimator.BuildReanimatorReadinessLines(items, caseName => string.Equals(caseName, "evidence-a", System.StringComparison.Ordinal));
        AssertEqual("Current production readiness: 40/100", lines[0], "Readiness header should reflect calculated score.");
        AssertTrue(lines.Any(line => line == "Done: done-a"), "Readiness lines should mark completed items.");
        AssertTrue(lines.Any(line => line == "Missing: missing-b"), "Readiness lines should expose missing items.");
    }

    private static void ReadinessChecklistIsBackedBySelfTests()
    {
        var caseNames = new HashSet<string>(BuildCases().Select(test => test.Name));
        var offenders = Reanimator.ReanimatorReadinessItems
            .Where(item => item.EvidenceCaseNames == null ||
                           item.EvidenceCaseNames.Length == 0 ||
                           item.EvidenceCaseNames.Any(caseName => !caseNames.Contains(caseName)))
            .Select(item => item.Label)
            .ToArray();

        AssertEqual(
            0,
            offenders.Length,
            "Release readiness items should be backed by existing regression self-tests. Offenders: " + string.Join(", ", offenders));
    }

    private static void ReanimatorPluginFolderIsSelfContained()
    {
        var root = Path.Combine(Application.dataPath, "Plugins/Reanimator");
        AssertTrue(Directory.Exists(root), "Reanimator plugin root should exist under Assets/Plugins.");
        AssertTrue(
            File.Exists(Path.Combine(root, "SpriteSkinPrefabBoneAssignerWindow.cs")),
            "SpriteSkin prefab bone assigner should live inside the Reanimator plugin folder.");

        var oldRootParts = new[]
        {
            "Assets",
            "0.Internal",
            "Game",
            "Scripts",
            "Editor",
            "Reanimator"
        };
        var oldRoot = string.Join("/", oldRootParts);
        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(oldRoot))
            .Select(path => path.Replace(Application.dataPath, "Assets").Replace('\\', '/'))
            .ToArray();

        AssertEqual(
            0,
            offenders.Length,
            "Reanimator plugin code should not reference the old project-local source root. Offenders: " + string.Join(", ", offenders));
    }

    private static void ReleaseUiHidesDiagnosticsUntilDebugMode()
    {
        var renderer = new PsdLayerPreviewRenderer();
        AssertFalse(Reanimator.ShouldShowReleaseDiagnostics(null), "Release UI should hide diagnostics without a renderer.");
        AssertFalse(Reanimator.ShouldShowReleaseDiagnostics(renderer), "Release UI should hide diagnostics while debug is off.");

        renderer.ToggleDebugModeByDoublePress();
        AssertTrue(Reanimator.ShouldShowReleaseDiagnostics(renderer), "Armed debug mode should reveal diagnostics.");
        renderer.ToggleDebugModeByDoublePress();
        AssertTrue(Reanimator.ShouldShowReleaseDiagnostics(renderer), "Enabled debug mode should reveal diagnostics.");
    }

    private static void ReleaseUiHidesDebugModuleFromOrdinaryToolbar()
    {
        var module = new ReanimatorDebugModeModule();
        AssertFalse(Reanimator.ShouldDrawModuleInProductionUi(module, null), "Debug module should not be drawn without active diagnostics.");
        AssertTrue(Reanimator.ShouldDrawModuleInProductionUi(new ReanimatorGenerateMissingGeometryModule(), null), "Normal utility modules should stay visible.");

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(window, "layerPreviewRenderer", renderer);
            AssertFalse(Reanimator.ShouldDrawModuleInProductionUi(module, window.Api), "Debug module should stay hidden while debug is off.");

            renderer.ToggleDebugModeByDoublePress();
            AssertTrue(Reanimator.ShouldDrawModuleInProductionUi(module, window.Api), "Debug module should appear while debug is armed.");
            renderer.ToggleDebugModeByDoublePress();
            AssertTrue(Reanimator.ShouldDrawModuleInProductionUi(module, window.Api), "Debug module should stay visible while debug is enabled.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void ReleaseUiUsesProductionButtonLabels()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        AssertTrue(modules.Count > 0, "Module labels should be discoverable.");
        for (var i = 0; i < modules.Count; i++)
        {
            var label = modules[i].Label(null) ?? string.Empty;
            AssertFalse(label.Contains("DellBones"), $"Module {modules[i].Id} should not expose the old delete-bones label.");
            AssertFalse(label.Contains("CopyBones"), $"Module {modules[i].Id} should not expose the old copy-bones label.");
            AssertFalse(label.Contains("Delete Bones"), $"Module {modules[i].Id} should use compact delete-bones label.");
            AssertFalse(label.Contains("Copy Bones"), $"Module {modules[i].Id} should use compact copy-bones label.");
            AssertFalse(label.Contains("DebugMode"), $"Module {modules[i].Id} should not expose the old debug label.");
            AssertFalse(label.Contains("Force Clean Cache"), $"Module {modules[i].Id} should not expose dev-only cache wording.");
            AssertFalse(string.Equals(label, "Preview Pose (Q)", System.StringComparison.Ordinal), "Preview Pose toolbar label should be shortened.");
            AssertFalse(string.Equals(label, "Preview (Q)", System.StringComparison.Ordinal), "Preview toolbar label should fit in the compact row.");
            AssertFalse(string.Equals(label, "Restore Pose", System.StringComparison.Ordinal), "Restore Pose toolbar label should be shortened.");
            AssertFalse(string.Equals(modules[i].Id, "pose.set-pivot", System.StringComparison.Ordinal), "Set Pivot should not be a separate left toolbar tool.");
            AssertFalse(string.Equals(label, "Set Pivot", System.StringComparison.Ordinal), "Set Pivot should not be exposed as a separate button.");
        }

        var previewPoseModule = modules.First(module => string.Equals(module.Id, "pose.preview", System.StringComparison.Ordinal));
        var restorePoseModule = modules.First(module => string.Equals(module.Id, "pose.restore", System.StringComparison.Ordinal));
        AssertEqual("Prew (Q)", previewPoseModule.Label(null), "Preview pose toolbar label.");
        AssertEqual("Restore (E)", restorePoseModule.Label(null), "Restore pose toolbar label.");
        AssertEqual(previewPoseModule.RowGroup, restorePoseModule.RowGroup, "Preview and Restore should share one toolbar row.");
        AssertTrue(previewPoseModule.RowGroup > 0, "Preview and Restore should use a horizontal toolbar row group.");

        AssertEqual("DelBon x5", new ReanimatorDeleteBonesModule().Label(null), "Delete bones fallback label.");
        AssertEqual("CopyBon", new ReanimatorCopyBonesModule().Label(null), "Copy bones label.");
        AssertEqual("Debug [OFF]", new ReanimatorDebugModeModule().Label(null), "Debug fallback label.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorCreateBoneModule().Label(null), new ReanimatorCreateBoneModule()), "Bones/Add-Split button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorEditBoneModule().Label(null), new ReanimatorEditBoneModule()), "Bones/Edit button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorAutoGeometryModule().Label(null), new ReanimatorAutoGeometryModule()), "Geometry/Auto button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorEditGeometryModule().Label(null), new ReanimatorEditGeometryModule()), "Geometry/Edit button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorSliderModule().Label(null), new ReanimatorSliderModule()), "Weights/Slider button should use the normal completed color.");
        AssertTrue(Reanimator.IsLeftToolMarkedIncomplete(previewPoseModule.Label(null), previewPoseModule), "Preview button should start as unchecked/incomplete.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete(restorePoseModule.Label(null), restorePoseModule), "Restore button should use the normal completed color.");
        AssertTrue(Reanimator.IsLeftToolMarkedIncomplete(new ReanimatorVolumeCurveModule().Label(null), new ReanimatorVolumeCurveModule()), "Volume button should start as unchecked/incomplete.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete("Geometry"), "Sprite Sheet Geometry button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete("Optimize"), "Sprite Sheet Optimize button should use the normal completed color.");
        AssertFalse(Reanimator.IsLeftToolMarkedIncomplete("Default Geometry"), "Sprite Sheet Default Geometry button should use the normal completed color.");

        var spriteSheetToolsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.SpriteSheetTools.cs"));
        AssertFalse(spriteSheetToolsSource.Contains("DrawLeftToolButton(\"TODO\""), "Release UI should not expose a TODO button label.");

        var panelsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.cs"));
        var spriteSheetPanelsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.SpriteSheetTools.cs"));
        var layoutSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Layout.cs"));
        AssertFalse(panelsSource.Contains("DrawLeftToolsProgress"), "Skinning UI should not draw a readiness percent block above the tools.");
        AssertFalse(spriteSheetPanelsSource.Contains("DrawLeftToolsProgress"), "Sprite Sheet UI should not draw a readiness percent block above the tools.");
        AssertFalse(panelsSource.Contains("DrawTodoFooterButton"), "Skinning UI should not expose the stale readiness footer button.");
        AssertFalse(spriteSheetPanelsSource.Contains("DrawTodoFooterButton"), "Sprite Sheet UI should not expose the stale readiness footer button.");
        AssertFalse(spriteSheetPanelsSource.Contains("DrawLeftToolButton(\"Readiness\""), "Sprite Sheet UI should not expose a readiness overlay toggle.");
        AssertFalse(layoutSource.Contains("DrawReadinessOverlay"), "Preview UI should not draw the stale readiness overlay.");
        AssertFalse(layoutSource.Contains("HandleReadinessOverlayInput"), "Preview UI should not reserve input for the stale readiness overlay.");
        AssertFalse(panelsSource.Contains("GUILayout.Label($\"{ReanimatorReadinessPercent}%\""), "Release UI should not draw the readiness percent as a left-panel badge.");
        AssertFalse(spriteSheetPanelsSource.Contains("GUILayout.Label($\"{ReanimatorReadinessPercent}%\""), "Sprite Sheet UI should not draw the readiness percent as a left-panel badge.");
        AssertFalse(layoutSource.Contains("GUI.Label(percentRect, $\"{ReanimatorReadinessPercent}%\""), "Preview UI should not draw the readiness percent overlay.");
        AssertTrue(panelsSource.Contains("EditorGUI.DrawRect(rect, Color.black);"), "Empty right settings panel should use a black logo background.");

        var drawLoopSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.DrawLoop.cs"));
        var spriteDebugIndex = drawLoopSource.IndexOf(
            "options.ActiveDebugPreviewModule?.DrawDebugPreviewOverlay(options.Api, new ReanimatorPreviewContext",
            System.StringComparison.Ordinal);
        var spriteTopmostIndex = drawLoopSource.IndexOf(
            "DrawTopmostToolOverlays(localRect, coordinateSpace, selectedLayer, options);",
            System.StringComparison.Ordinal);
        AssertTrue(spriteDebugIndex >= 0 && spriteTopmostIndex > spriteDebugIndex, "QuadNet preview marker should draw above sprite-atlas debug overlays.");
        var normalDebugIndex = drawLoopSource.LastIndexOf("DrawDebugOverlay(coordinateSpace, drawList, selectedLayer", System.StringComparison.Ordinal);
        var normalTopmostIndex = drawLoopSource.LastIndexOf("DrawTopmostToolOverlays(localRect, coordinateSpace, selectedLayer, options);", System.StringComparison.Ordinal);
        AssertTrue(normalDebugIndex >= 0 && normalTopmostIndex > normalDebugIndex, "QuadNet preview marker should draw above normal preview overlays.");

        var geometryToolsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.GeometryTools.cs"));
        AssertTrue(geometryToolsSource.Contains("Pivot Point"), "Preview Pose settings should include the Pivot Point area.");
        AssertFalse(geometryToolsSource.Contains("DrawPivotToolPanel"), "Pivot should not have a separate tool settings panel.");
        AssertTrue(geometryToolsSource.Contains("\"Rotation\""), "QuadNet settings should expose a rotation slider.");
        AssertTrue(geometryToolsSource.Contains("autoGeometryQuadRotationDegrees"), "QuadNet generation should pass the rotation slider value.");
        var influencePanelsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.Influence.cs"));
        var editGeometrySelectionSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Geometry/PsdLayerPreviewRenderer.Geometry.EditGeometrySelection.cs"));
        AssertTrue(influencePanelsSource.Contains("\"Deselect\""), "Edit Geometry panel should expose a Deselect button.");
        AssertTrue(influencePanelsSource.Contains("\"Invert Sel\""), "Edit Geometry panel should expose an Invert Sel button.");
        AssertTrue(editGeometrySelectionSource.Contains("DeselectEditGeometryVertexSelection"), "Edit Geometry should provide a deselect selection action.");
        AssertTrue(editGeometrySelectionSource.Contains("InvertEditGeometryVertexSelection"), "Edit Geometry should provide an invert selection action.");
    }

    private static void PsdInspectorHeaderOffersReanimatorOpen()
    {
        AssertTrue(ReanimatorPsdInspectorHeader.SupportsAssetPath("Assets/character.psd"), "PSD importer should offer the Reanimator button.");
        AssertTrue(ReanimatorPsdInspectorHeader.SupportsAssetPath("Assets/CHARACTER.PSB"), "PSB importer should offer the Reanimator button case-insensitively.");
        AssertFalse(ReanimatorPsdInspectorHeader.SupportsAssetPath("Assets/character.png"), "Non-PSD importers should not offer the Reanimator button.");

        var source = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Reanimator.PsdInspectorHeader.cs"));
        AssertTrue(source.Contains("Editor.finishedDefaultHeaderGUI += DrawHeader;"), "PSD inspector extension should attach to the importer header.");
        AssertTrue(source.Contains("Reanimator.OpenAsset(assetPath);"), "PSD inspector button should open the inspected asset explicitly.");
    }

    private static void AutoGeometryExposesReanimaxMode()
    {
        var reanimatorSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator.cs"));
        var geometryToolsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.GeometryTools.cs"));
        var autoGeometrySource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Geometry/PsdLayerPreviewRenderer.Geometry.AutoGeometry.cs"));

        AssertTrue(reanimatorSource.Contains("\"Reanimax\""), "Auto geometry mode popup should expose Reanimax.");
        AssertTrue(reanimatorSource.Contains("\"Hard\""), "Reanimax mode popup should expose Hard.");
        AssertTrue(reanimatorSource.Contains("\"Soft\""), "Reanimax mode popup should expose Soft.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxMode = ReanimatorReanimaxGeometryMode.Hard"), "Reanimax should default to Hard.");
        AssertTrue(geometryToolsSource.Contains("ReanimatorAutoGeometryMode.Reanimax"), "Auto geometry panel should branch Reanimax settings separately.");
        AssertTrue(geometryToolsSource.Contains("ReanimatorReanimaxGeometryMode.Hard"), "Reanimax panel should branch Hard mode explicitly.");
        AssertTrue(geometryToolsSource.Contains("\"Mask\""), "Reanimax Hard should expose the mask preview checkbox.");
        AssertTrue(geometryToolsSource.Contains("\"Edge Pts\""), "Reanimax Hard should expose the edge-points preview checkbox.");
        AssertTrue(geometryToolsSource.Contains("\"Alpha\""), "Reanimax Hard should expose the mask alpha threshold.");
        AssertTrue(geometryToolsSource.Contains("\"Pos Px\""), "Reanimax Hard edge points should expose the position filtering radius.");
        AssertTrue(geometryToolsSource.Contains("\"Min Turn\""), "Reanimax Hard edge points should expose the minimum turn filter.");
        AssertTrue(geometryToolsSource.Contains("\"Post Pos\""), "Reanimax Hard edge points should expose the post-turn position filtering radius.");
        AssertTrue(geometryToolsSource.Contains("\"Min Dist\""), "Reanimax Hard edge points should expose the peak restore distance.");
        AssertTrue(geometryToolsSource.Contains("\"Mid Pos\""), "Reanimax Hard normal midpoints should expose their own position filtering radius.");
        AssertTrue(geometryToolsSource.Contains("\"Inset\""), "Reanimax Hard normal point copies should expose their inward shift distance.");
        AssertTrue(geometryToolsSource.Contains("\"Inset Pos\""), "Reanimax Hard inset point copies should expose their own position filtering radius.");
        AssertTrue(geometryToolsSource.Contains("Mathf.Clamp(autoGeometryReanimaxHardNormalMidpointPositionFilterPixels, 1, 50)"), "Reanimax normal midpoint position filter should allow values up to 50 px.");
        AssertTrue(geometryToolsSource.Contains("Mathf.Clamp(autoGeometryReanimaxHardNormalInsetPositionFilterPixels, 1, 50)"), "Reanimax inset point position filter should allow values up to 50 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardEdgePositionFilterPixels = 3"), "Reanimax first position filter should default to 3 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardEdgeMinTurnDegrees = 13f"), "Reanimax minimum turn should default to 13 degrees.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardEdgePostPositionFilterPixels = 4"), "Reanimax post-turn position filter should default to 4 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardEdgeMinDistancePixels = 2.8f"), "Reanimax peak restore distance should default to 2.8 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30"), "Reanimax normal midpoint position filter should default to 30 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardNormalInsetPixels = 5f"), "Reanimax normal point copy inset should default to 5 px.");
        AssertTrue(reanimatorSource.Contains("autoGeometryReanimaxHardNormalInsetPositionFilterPixels = 5"), "Reanimax inset point position filter should default to 5 px.");
        AssertTrue(reanimatorSource.Contains("ResetReanimaxHardEdgeDefaults"), "Reanimax should expose a reset path for hard edge defaults.");
        AssertTrue(geometryToolsSource.Contains("\"Reset Edge Defaults\""), "Reanimax Hard edge points should expose a reset defaults button.");
        AssertTrue(autoGeometrySource.Contains("TryGenerateReanimaxGeometryLocal"), "Auto geometry generation should route Reanimax separately.");
        AssertTrue(autoGeometrySource.Contains("BuildReanimaxHardInternalPointsLocal"), "Reanimax Hard generation should include the same internal points as the preview.");
        AssertTrue(autoGeometrySource.Contains("TryTriangulateWithUnityTessellation(vertices, boundaryEdges, 0"), "Reanimax Hard generation should triangulate contour and internal points.");
        AssertTrue(autoGeometrySource.Contains("Reanimax Soft: reserved for later"), "Reanimax Soft should stay visible but reserved.");
        var previewOptionsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.PreviewDrawOptions.cs"));
        var toolOverlaySource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Rendering/PsdLayerPreviewRenderer.Rendering.ToolOverlays.cs"));
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardMaskPreview"), "Reanimax mask repaint should require the checkbox.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardEdgePointsPreview"), "Reanimax edge points repaint should require the checkbox.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardEdgePositionFilterPixels"), "Reanimax edge point position filtering should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardEdgeMinTurnDegrees"), "Reanimax edge point turn filtering should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardEdgePostPositionFilterPixels"), "Reanimax post-turn position filtering should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardEdgeMinDistancePixels"), "Reanimax peak restore distance should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardNormalMidpointPositionFilterPixels"), "Reanimax normal midpoint filtering should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardNormalInsetPixels"), "Reanimax normal point copy inset should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxHardNormalInsetPositionFilterPixels"), "Reanimax inset point copy filtering should flow into preview options.");
        AssertTrue(previewOptionsSource.Contains("options.SuppressGeometryOverlay = options.AutoGeometryReanimaxHardEdgePointsPreviewActive"), "Reanimax edge setup should hide the old geometry overlay while tuning.");
        AssertTrue(previewOptionsSource.Contains("autoGeometryReanimaxMode == ReanimatorReanimaxGeometryMode.Hard"), "Reanimax mask should be Hard-only.");
        AssertTrue(toolOverlaySource.Contains("pixels[i].a / 255f < threshold ? (byte)0 : (byte)255"), "Reanimax mask should map low alpha to black and high alpha to white.");
        AssertTrue(toolOverlaySource.Contains("TryReadReanimaxHardMaskSource"), "Reanimax mask should read from GPU texture data.");
        AssertFalse(toolOverlaySource.Contains("GetPixelBilinear"), "Reanimax mask should not require readable imported textures.");
        AssertTrue(toolOverlaySource.Contains("DrawReanimaxHardEdgePointsPreview"), "Reanimax should draw visible edge point previews.");
        AssertTrue(toolOverlaySource.Contains("new Color(1f, 0.46f, 0f, 0.95f)"), "Reanimax should draw the tuning outline in orange.");
        AssertTrue(toolOverlaySource.Contains("DrawShiftedReanimaxHardEdgePath"), "Reanimax tuning outline should be thick like the normal geometry boundary.");
        AssertTrue(toolOverlaySource.Contains("Handles.DrawAAPolyLine(2.5f, linePoints)"), "Reanimax should connect final edge points with preview lines.");
        AssertTrue(toolOverlaySource.Contains("new Color(0.55f, 0.92f, 1f, 1f)"), "Reanimax tuning should highlight final points in cyan.");
        AssertTrue(toolOverlaySource.Contains("Handles.DrawSolidDisc(view, Vector3.forward, 3.1f)"), "Reanimax tuning should draw visible point markers over the orange outline.");
        AssertTrue(toolOverlaySource.Contains("DrawTemporaryReanimaxHardNormalProbe"), "Reanimax should expose the temporary normal probe.");
        AssertTrue(toolOverlaySource.Contains("TryGetTemporaryReanimaxHardNormalSegment"), "Reanimax temporary normal probe should trace each normal from the contour point to the contour exit.");
        AssertTrue(toolOverlaySource.Contains("midpoint = point + direction * (endT * 0.5f)"), "Reanimax temporary normal probe should put the red marker halfway along the normal.");
        AssertTrue(toolOverlaySource.Contains("FilterNearbyTemporaryReanimaxHardPoints"), "Reanimax temporary normal midpoint markers should have their own position filter.");
        AssertTrue(toolOverlaySource.Contains("DrawTemporaryReanimaxHardNormalProbe(localRect, coordinates, selectedLayer, paths, normalMidpointPositionFilterPixels, normalInsetPixels, normalInsetPositionFilterPixels)"), "Reanimax normal midpoint filtering should not mutate contour point paths.");
        AssertTrue(toolOverlaySource.Contains("new Color(0.36f, 0.78f, 1f, 0.95f)"), "Reanimax temporary normal probe should draw midpoint and inset markers in the same blue color.");
        AssertFalse(toolOverlaySource.Contains("Handles.DrawAAPolyLine(1.1f, start, end)"), "Reanimax temporary normal probe should not draw normal guide lines.");
        AssertTrue(toolOverlaySource.Contains("Handles.DrawSolidDisc(view, Vector3.forward, 2.25f)"), "Reanimax temporary normal probe should draw midpoint markers.");
        AssertTrue(toolOverlaySource.Contains("DrawTemporaryReanimaxHardInsetPointCopies"), "Reanimax temporary normal probe should draw inward shifted copies of contour points.");
        AssertTrue(toolOverlaySource.Contains("copy = lines[i].Point + lines[i].InwardDirection * inset"), "Reanimax inset point copies should move from contour points along inward normals.");
        AssertTrue(toolOverlaySource.Contains("FilterNearbyTemporaryReanimaxHardPoints(copies, insetPositionFilterPixels)"), "Reanimax inset point copies should have their own position filter.");
        AssertTrue(toolOverlaySource.Contains("linePoints[linePoints.Length - 1] = linePoints[0]"), "Reanimax edge point lines should close each contour.");
        AssertTrue(toolOverlaySource.Contains("HasAdjacentReanimaxHardMaskBlackOrOutOfBounds"), "Reanimax edge points should be white mask pixels adjacent to transparent pixels or the mask border.");
        AssertTrue(toolOverlaySource.Contains("points[i] = new Vector2(simplifiedPixels[i].x + 0.5f, simplifiedPixels[i].y + 0.5f)"), "Reanimax edge points should sit on white pixel centers.");
        AssertTrue(toolOverlaySource.Contains("SimplifyReanimaxHardEdgePixels"), "Reanimax edge points should be simplified immediately after creation.");
        AssertTrue(toolOverlaySource.Contains("TraceReanimaxHardEdgeComponent"), "Reanimax edge point ordering should trace the contour instead of using nearest-neighbor jumps.");
        AssertFalse(toolOverlaySource.Contains("FindNextReanimaxHardEdgePixel"), "Reanimax edge point ordering should not use global nearest-neighbor jumps.");
        AssertTrue(toolOverlaySource.Contains("RemoveNearbyReanimaxHardEdgePixels"), "Reanimax edge points should run position filtering before direction filtering.");
        AssertTrue(toolOverlaySource.Contains("RemoveLowTurnReanimaxHardEdgePixels"), "Reanimax edge simplification should remove low-turn middle points.");
        AssertTrue(toolOverlaySource.Contains("RestoreDistantReanimaxHardEdgePixels"), "Reanimax edge simplification should restore original peaks far from the final line.");
        AssertTrue(toolOverlaySource.Contains("GetDistanceFromReanimaxHardEdgeLine"), "Reanimax peak restore should compare original points to the final line distance.");
        AssertTrue(toolOverlaySource.Contains("GetReanimaxHardEdgeTurnDegrees"), "Reanimax edge simplification should compare turn angles against the minimum turn.");
        AssertTrue(toolOverlaySource.Contains("previousDirection == nextDirection"), "Reanimax edge simplification should compare consecutive directions.");
        var positionFilterIndex = toolOverlaySource.IndexOf("RemoveNearbyReanimaxHardEdgePixels(ordered", System.StringComparison.Ordinal);
        var directionFilterIndex = toolOverlaySource.IndexOf("RemoveLowTurnReanimaxHardEdgePixels(positionFiltered", System.StringComparison.Ordinal);
        var postPositionFilterIndex = toolOverlaySource.IndexOf("RemoveNearbyReanimaxHardEdgePixels(turnFiltered", System.StringComparison.Ordinal);
        var distanceRestoreIndex = toolOverlaySource.IndexOf("RestoreDistantReanimaxHardEdgePixels(ordered, postPositionFiltered", System.StringComparison.Ordinal);
        AssertTrue(positionFilterIndex >= 0 && directionFilterIndex > positionFilterIndex, "Reanimax should filter edge points by position before filtering by degree.");
        AssertTrue(postPositionFilterIndex > directionFilterIndex, "Reanimax should run a second position filter after filtering by degree.");
        AssertTrue(distanceRestoreIndex > postPositionFilterIndex, "Reanimax should restore distant original points after post-position filtering.");
    }

    private static void ReleaseUiMapsProductionModulesToCustomIcons()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        AssertTrue(modules.Count > 0, "Module icons should be discoverable.");

        var offenders = new List<string>();
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (!Reanimator.ShouldDrawModuleInProductionUi(module, null))
                continue;

            var spriteName = Reanimator.GetLeftToolButtonIconSpriteName(module.Label(null), module);
            if (string.IsNullOrEmpty(spriteName) || !Reanimator.HasLeftToolIconSprite(spriteName))
                offenders.Add($"{module.Id}:{spriteName}");
        }

        AssertEqual(
            0,
            offenders.Count,
            "Production Reanimator modules should use sprites from Graphics/icons.psd. Offenders: " + string.Join(", ", offenders));

        AssertEqual(
            "Edit bones",
            Reanimator.GetLeftToolButtonIconSpriteName(new ReanimatorEditBoneModule().Label(null), new ReanimatorEditBoneModule()),
            "Bones/Edit should not reuse the geometry edit icon.");
        AssertEqual(
            "Edit geometry",
            Reanimator.GetLeftToolButtonIconSpriteName(new ReanimatorEditGeometryModule().Label(null), new ReanimatorEditGeometryModule()),
            "Geometry/Edit should keep its own icon.");
    }

    private static void ReleaseUiIconSheetUsesCurrentPsdImporterMetadata()
    {
        var metaPath = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Graphics/icons.psd.meta");
        AssertTrue(File.Exists(metaPath), "Reanimator icon sheet meta should exist.");

        var meta = File.ReadAllText(metaPath);
        AssertTrue(
            meta.Contains("  layeredSpriteImportData:"),
            "Reanimator icon sheet should use layeredSpriteImportData so PSD layer mapping survives reimport.");
        AssertTrue(
            meta.Contains("  psdLayers:"),
            "Reanimator icon sheet should persist PSD layer IDs in psdLayers to avoid duplicate LayerId fallback warnings.");
        AssertTrue(
            meta.Contains("  rigPSDLayers:"),
            "Reanimator icon sheet should keep rigPSDLayers for Unity importer compatibility.");
    }

    private static void NamingIndicatorLoadsCurrentPsdImporterMetadata()
    {
        const string indicatorPath = "Assets/Plugins/Reanimator/Graphics/indication.psd";
        AssertTrue(AssetDatabase.LoadAssetAtPath<Texture2D>(indicatorPath) != null, "Naming indicator PSD should be loadable.");

        var partsField = typeof(Reanimator).GetField(
            "namingIndicatorParts",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var canvasField = typeof(Reanimator).GetField(
            "namingIndicatorCanvasRect",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        AssertTrue(partsField != null, "Naming indicator parts cache field should exist.");
        AssertTrue(canvasField != null, "Naming indicator canvas cache field should exist.");

        partsField.SetValue(null, null);
        canvasField.SetValue(null, Rect.zero);

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var parts = (System.Collections.ICollection)InvokePrivateMethod(window, "GetNamingIndicatorParts");
            var canvasRect = (Rect)canvasField.GetValue(null);

            AssertTrue(parts != null && parts.Count >= 18, "Naming indicator should include the two generated second-arm parts.");
            AssertTrue(canvasRect.width > 0f && canvasRect.height > 0f, "Naming indicator canvas bounds should be built from loaded parts.");
            var generatedParts = parts.Cast<object>().ToList();
            var leftArm2 = generatedParts.FirstOrDefault(part => GetPrivateField<string>(part, "Name") == "l_arm_2");
            var rightArm2 = generatedParts.FirstOrDefault(part => GetPrivateField<string>(part, "Name") == "r_arm_2");
            AssertTrue(leftArm2 != null && rightArm2 != null, "Naming indicator should generate l_arm_2 and r_arm_2 from the base arm sprites.");
            AssertEqual(45f, GetPrivateField<float>(leftArm2, "RotationDegrees"), "Second left arm should rotate upward by 45 degrees.");
            AssertEqual(-45f, GetPrivateField<float>(rightArm2, "RotationDegrees"), "Second right arm should rotate upward by 45 degrees.");

            var onePairNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "l_arm", "r_arm" };
            AssertFalse(
                (bool)InvokePrivateMethod(window, "ShouldShowNamingIndicatorPart", "l_arm_2", onePairNames),
                "Second-arm graphics should stay hidden for ordinary one-pair characters.");
            onePairNames.Add("l_arm_2");
            AssertTrue(
                (bool)InvokePrivateMethod(window, "ShouldShowNamingIndicatorPart", "r_arm_2", onePairNames),
                "Both second-arm graphics should appear when either indexed arm exists, exposing a missing mate in red.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void NamingIndicatorReportsSwappedLimbsAndDuplicateNames()
    {
        var layers = new List<PsdLayerPreviewRenderer.LayerInfo>
        {
            new PsdLayerPreviewRenderer.LayerInfo
            {
                name = "l_arm",
                position = new Vector2(120f, 0f),
                sourceRect = new Rect(0f, 0f, 20f, 30f)
            },
            new PsdLayerPreviewRenderer.LayerInfo
            {
                name = "r_arm",
                position = new Vector2(20f, 0f),
                sourceRect = new Rect(0f, 0f, 20f, 30f)
            },
            new PsdLayerPreviewRenderer.LayerInfo
            {
                name = "l_leg",
                position = new Vector2(140f, 0f),
                sourceRect = new Rect(0f, 0f, 20f, 40f)
            },
            new PsdLayerPreviewRenderer.LayerInfo
            {
                name = "r_leg",
                position = new Vector2(40f, 0f),
                sourceRect = new Rect(0f, 0f, 20f, 40f)
            },
            new PsdLayerPreviewRenderer.LayerInfo { name = "head" },
            new PsdLayerPreviewRenderer.LayerInfo { name = "Head" }
        };

        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var issues = InvokePrivateMethod(window, "BuildNamingIndicatorIssueState", layers);
            AssertTrue(GetPrivateField<PsdLayerPreviewRenderer.LayerInfo>(issues, "ArmOrderLayer") != null, "WA should report a left arm placed to the right of the right arm.");
            AssertTrue(GetPrivateField<PsdLayerPreviewRenderer.LayerInfo>(issues, "LegOrderLayer") != null, "WL should report a left leg placed to the right of the right leg.");
            AssertEqual(1, GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(issues, "DuplicateLayers").Count, "DD should contain only subsequent duplicate-name sprites.");
            var allInBlockReason = (string)InvokePrivateMethod(window, "BuildNamingIndicatorAllInBlockReason", layers);
            AssertTrue(allInBlockReason.Contains("WA:"), "ALLIN preflight should be blocked by the red WA indicator.");
            AssertTrue(allInBlockReason.Contains("WL:"), "ALLIN preflight should be blocked by the red WL indicator.");
            AssertTrue(allInBlockReason.Contains("DD:"), "ALLIN preflight should be blocked by the red DD indicator.");
            var sourceNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "l_arm", "r_arm", "l_leg", "r_leg", "head", "body"
            };
            AssertTrue(
                (bool)InvokePrivateMethod(window, "IsNamingIndicatorPartProblem", "l_arm", sourceNames, issues),
                "WA should render both arm parts red instead of leaving holes in the indicator.");
            AssertTrue(
                (bool)InvokePrivateMethod(window, "IsNamingIndicatorPartProblem", "top", sourceNames, issues),
                "A missing expected sprite should render red instead of disappearing from the indicator.");
            AssertFalse(
                (bool)InvokePrivateMethod(window, "IsNamingIndicatorPartProblem", "body", sourceNames, issues),
                "A present valid sprite should keep its normal indicator color.");
            var missingTooltip = (string)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorPartHoverTooltip",
                "neck",
                sourceNames,
                issues);
            AssertTrue(missingTooltip.Contains("НЕ НАЙДЕН"), "A missing mannequin part should explain its expected layer name on hover.");
            var armTooltip = (string)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorPartHoverTooltip",
                "l_arm",
                sourceNames,
                issues);
            AssertTrue(armTooltip.Contains("WA:"), "A swapped arm should explain the WA problem on hover.");
            var validTooltip = (string)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorPartHoverTooltip",
                "body",
                sourceNames,
                issues);
            AssertTrue(validTooltip.Contains("— OK"), "A valid mannequin part should report that it is OK on hover.");
            AssertTrue(
                (bool)InvokePrivateMethod(window, "ShouldDrawNamingIndicatorPart", "topwear", sourceNames),
                "The body layer should satisfy the topwear mannequin region.");
            var bodyAliasNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "topwear" };
            AssertTrue(
                (bool)InvokePrivateMethod(window, "ShouldDrawNamingIndicatorPart", "body", bodyAliasNames),
                "The topwear layer should satisfy the body mannequin region.");
            var bodyAliasTooltip = (string)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorPartHoverTooltip",
                "topwear",
                sourceNames,
                issues);
            AssertTrue(bodyAliasTooltip.StartsWith("body — OK"), "The hover title should use the actual matched layer name, not the mannequin template part name.");
            AssertTrue(bodyAliasTooltip.Contains("допустимый слой \"body\""), "The hover tooltip should identify the body/topwear alias that was found.");

            var loadedLayers = new List<PsdLayerPreviewRenderer.LayerInfo>
            {
                new PsdLayerPreviewRenderer.LayerInfo { name = "body" },
                new PsdLayerPreviewRenderer.LayerInfo { name = "neck" }
            };
            var hierarchyNodes = new List<PsdLayerPreviewRenderer.LayerHierarchyNode>
            {
                new PsdLayerPreviewRenderer.LayerHierarchyNode { name = "l_leg" },
                new PsdLayerPreviewRenderer.LayerHierarchyNode { name = "r_leg" }
            };
            var effectiveNames = (HashSet<string>)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorSourceNames",
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "head" },
                loadedLayers,
                hierarchyNodes);
            AssertFalse(effectiveNames.Contains("head"), "Importer subassets must not override the authoritative loaded-layer list.");
            AssertTrue(effectiveNames.Contains("body"), "Loaded body layer should be authoritative even when importer subassets omit it.");
            AssertTrue(effectiveNames.Contains("neck"), "Loaded neck layer should be authoritative even when importer subassets omit it.");
            AssertTrue(effectiveNames.Contains("l_leg") && effectiveNames.Contains("r_leg"), "Loaded leg layers should be authoritative even when importer subassets omit them.");

            var fallbackNames = (HashSet<string>)InvokePrivateMethod(
                window,
                "BuildNamingIndicatorSourceNames",
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "head" },
                null,
                null);
            AssertTrue(fallbackNames.Contains("head"), "Importer subassets should remain available when no loaded-layer list exists.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void PreviewAlwaysSetsLinearGammaShaderUniform()
    {
        var source = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Rendering/PsdLayerPreviewRenderer.Rendering.cs"));
        AssertTrue(
            source.Contains("mat.SetFloat(\"_AdjustLinearForGamma\", PlayerSettings.colorSpace == ColorSpace.Linear ? 1f : 0f);"),
            "Preview should always write the linear-to-gamma uniform used by Unity's 2D Animation GUI shader.");
        AssertFalse(
            source.Contains("mat.HasProperty(\"_AdjustLinearForGamma\")"),
            "The Unity package shader exposes _AdjustLinearForGamma as a uniform, so HasProperty must not gate it.");
    }

    private static void AutoSkeletonMergesSecondArmPair()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "cachedDocumentSize", new Vector2Int(1000, 1000));

        var result = renderer.ApplyAutoSkeletonPoseJsonWithSecondaryArms(
            BuildAutoSkeletonPoseJsonForTest(false),
            BuildAutoSkeletonPoseJsonForTest(true),
            string.Empty,
            false,
            false,
            false);
        var bones = renderer.Bones;

        AssertTrue(result.Contains("second arm pass"), "Dual-pass Auto Skeleton should report the merged arm chain.");
        AssertEqual(1, bones.Count(bone => bone != null && bone.name == "spine"), "Dual-pass Auto Skeleton must keep only one torso skeleton.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "l_arm"), "Primary left arm should remain in the skeleton.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "r_arm"), "Primary right arm should remain in the skeleton.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "l_arm_2"), "Secondary left upper arm should be merged.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "l_arm_3"), "Secondary left forearm should be merged.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "r_arm_2"), "Secondary right upper arm should be merged.");
        AssertTrue(bones.Any(bone => bone != null && bone.name == "r_arm_3"), "Secondary right forearm should be merged.");

        var spineIndex = bones.ToList().FindIndex(bone => bone != null && bone.name == "spine");
        var leftArm2 = bones.First(bone => bone != null && bone.name == "l_arm_2");
        var rightArm2 = bones.First(bone => bone != null && bone.name == "r_arm_2");
        AssertEqual(spineIndex, leftArm2.parentId, "Secondary left arm should attach to the primary spine.");
        AssertEqual(spineIndex, rightArm2.parentId, "Secondary right arm should attach to the primary spine.");
    }

    private static string BuildAutoSkeletonPoseJsonForTest(bool secondaryArms)
    {
        var points = new float[25 * 3];
        void SetPoint(int index, float x, float y)
        {
            points[index * 3] = x;
            points[index * 3 + 1] = y;
            points[index * 3 + 2] = 1f;
        }

        SetPoint(0, 500f, 180f);
        SetPoint(1, 500f, 280f);
        SetPoint(8, 500f, 600f);
        SetPoint(9, 560f, 610f);
        SetPoint(10, 570f, 760f);
        SetPoint(11, 580f, 920f);
        SetPoint(12, 440f, 610f);
        SetPoint(13, 430f, 760f);
        SetPoint(14, 420f, 920f);
        SetPoint(2, 600f, 310f);
        SetPoint(5, 400f, 310f);
        if (secondaryArms)
        {
            SetPoint(3, 700f, 220f);
            SetPoint(4, 800f, 130f);
            SetPoint(6, 300f, 220f);
            SetPoint(7, 200f, 130f);
        }
        else
        {
            SetPoint(3, 700f, 430f);
            SetPoint(4, 790f, 560f);
            SetPoint(6, 300f, 430f);
            SetPoint(7, 210f, 560f);
        }

        return "{\"canvas_width\":1000,\"canvas_height\":1000,\"people\":[{\"pose_keypoints_2d\":[" +
               string.Join(",", points.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) +
               "],\"hand_left_keypoints_2d\":[],\"hand_right_keypoints_2d\":[],\"face_keypoints_2d\":[]}]}";
    }

    private static void ReanimatorUndoIsolatesConsecutiveOperations()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var before = Undo.GetCurrentGroup();
            InvokePrivateMethod(window, "RegisterReanimatorUndo", "Create Bone");
            var first = Undo.GetCurrentGroup();
            InvokePrivateMethod(window, "RegisterReanimatorUndo", "Create Bone");
            var second = Undo.GetCurrentGroup();
            InvokePrivateMethod(window, "RegisterReanimatorUndo", "Create Bone");
            var third = Undo.GetCurrentGroup();

            AssertTrue(first > before, "First Create Bone undo should start a new Unity group.");
            AssertTrue(second > first, "Second Create Bone undo should not share the first bone creation group.");
            AssertTrue(third > second, "Third Create Bone undo should not share the second bone creation group.");
        }
        finally
        {
            Undo.ClearUndo(window);
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorBoneRenameUndoStepsOneRenameAtATime()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        Texture2D texture = null;
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            texture = new Texture2D(1, 1);
            SetPrivateField(renderer, "cachedPath", "Assets/__ReanimatorBoneUndoSelfTest.psd");
            SetPrivateField(renderer, "cachedTexture", texture);
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "bone_0",
                    parentId = -1,
                    position = Vector2.zero,
                    initialPosition = Vector2.zero,
                    rotation = Quaternion.identity,
                    initialRotation = Quaternion.identity,
                    worldPosition = Vector2.zero,
                    worldRotation = Quaternion.identity,
                    length = 10f,
                    visible = true
                }
            });
            SetPrivateField(window, "layerPreviewRenderer", renderer);
            renderer.UndoRequest = operationName => InvokePrivateMethod(window, "RegisterReanimatorUndo", operationName);

            AssertTrue(renderer.RenameBone(0, "hip"), "First rename should change the bone name.");
            AssertTrue(renderer.RenameBone(0, "spine"), "Second rename should change the bone name.");

            if (!Application.isBatchMode)
                return;

            Undo.PerformUndo();
            InvokePrivateMethod(window, "HandleUndoRedoPerformed");
            renderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertEqual("hip", bones[0].name, "One Undo should revert only the most recent bone rename.");

            Undo.PerformUndo();
            InvokePrivateMethod(window, "HandleUndoRedoPerformed");
            renderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertEqual("bone_0", bones[0].name, "Second Undo should revert the earlier bone rename.");
        }
        finally
        {
            Undo.ClearUndo(window);
            if (texture != null)
                Object.DestroyImmediate(texture);
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorWindowUnityUndoRestoresRendererState()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        var group = Undo.GetCurrentGroup();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var layer = BuildLayer("undo-window-sprite", 909, Vector2.zero);
            layer.spriteGuid = "90990990990990990990990990990990";
            layer.sourceRect = new Rect(10f, 20f, 30f, 40f);
            layer.textureRect = layer.sourceRect;
            SetPrivateField(renderer, "layerInfos", new List<PsdLayerPreviewRenderer.LayerInfo> { layer });
            SetPrivateField(window, "layerPreviewRenderer", renderer);

            var originalRect = layer.sourceRect;
            var editedRect = new Rect(11f, 22f, 33f, 44f);
            InvokePrivateMethod(window, "RegisterReanimatorUndo", "Edit Sprite Rect");
            group = Undo.GetCurrentGroup();
            AssertTrue(renderer.SetLayerSpriteRect(layer, editedRect), "Fixture edit should change the renderer sprite rect.");
            Undo.CollapseUndoOperations(group);

            if (!Application.isBatchMode)
                return;

            Undo.PerformUndo();
            var undoRenderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            var undoLayer = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(undoRenderer, "layerInfos")[0];
            AssertRect(originalRect, undoLayer.sourceRect, 0.0001f, "Unity Undo should restore renderer source rect.");
            AssertRect(originalRect, undoLayer.textureRect, 0.0001f, "Unity Undo should restore renderer texture rect.");

            Undo.PerformRedo();
            var redoRenderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            var redoLayer = GetPrivateField<List<PsdLayerPreviewRenderer.LayerInfo>>(redoRenderer, "layerInfos")[0];
            AssertRect(editedRect, redoLayer.sourceRect, 0.0001f, "Unity Redo should restore renderer source rect.");
            AssertRect(editedRect, redoLayer.textureRect, 0.0001f, "Unity Redo should restore renderer texture rect.");
        }
        finally
        {
            Undo.ClearUndo(window);
            Object.DestroyImmediate(window);
        }
    }

    private static void JointV2ApplyUndoRestoresBoneAndGeometryPatchTogether()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        var group = Undo.GetCurrentGroup();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var rendererBones = new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "joint_v2_dynamic",
                    parentId = -1,
                    position = Vector2.zero,
                    initialPosition = Vector2.zero,
                    rotation = Quaternion.identity,
                    initialRotation = Quaternion.identity,
                    worldPosition = Vector2.zero,
                    worldRotation = Quaternion.identity,
                    length = 10f,
                    visible = true
                }
            };
            SetPrivateField(renderer, "bones", rendererBones);
            SetPrivateField(window, "layerPreviewRenderer", renderer);

            InvokePrivateMethod(window, "RegisterReanimatorUndo", "Apply Joint V2");
            group = Undo.GetCurrentGroup();
            rendererBones[0].position = new Vector2(3f, 4f);
            rendererBones[0].initialPosition = new Vector2(3f, 4f);
            rendererBones[0].worldPosition = new Vector2(3f, 4f);
            var patch = CreatePrivateNestedInstance(renderer, "AutoGeometryPatch");
            SetPrivateField(patch, "assetPath", "Assets/__JointV2UndoSelfTest.psd");
            SetPrivateField(patch, "layerSelectionKey", "leg");
            SetPrivateField(patch, "layerStableKey", "leg#1");
            SetPrivateField(patch, "verticesDoc", new[]
            {
                Vector2.zero,
                Vector2.right,
                Vector2.up
            });
            GetPrivateList(renderer, "autoGeometryPatches").Add(patch);
            SetPrivateField(renderer, "hasPendingChanges", true);
            Undo.CollapseUndoOperations(group);

            if (!Application.isBatchMode)
                return;

            Undo.PerformUndo();
            var undoRenderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            var undoBones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(undoRenderer, "bones");
            AssertVector(Vector2.zero, undoBones[0].position, 0.0001f,
                "One Joint V2 Undo should restore the original dynamic bone base.");
            AssertEqual(0, GetPrivateListCount(undoRenderer, "autoGeometryPatches"),
                "The same Joint V2 Undo should remove its generated geometry patch.");

            Undo.PerformRedo();
            var redoRenderer = GetPrivateField<PsdLayerPreviewRenderer>(window, "layerPreviewRenderer");
            var redoBones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(redoRenderer, "bones");
            AssertVector(new Vector2(3f, 4f), redoBones[0].position, 0.0001f,
                "One Joint V2 Redo should restore the moved dynamic bone base.");
            AssertEqual(1, GetPrivateListCount(redoRenderer, "autoGeometryPatches"),
                "The same Joint V2 Redo should restore its generated geometry patch.");
        }
        finally
        {
            Undo.ClearUndo(window);
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorWindowCommitsBoneEditPoseOnToolExit()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "root",
                    parentId = -1,
                    position = new Vector2(15f, 6f),
                    initialPosition = Vector2.zero,
                    rotation = Quaternion.AngleAxis(35f, Vector3.forward),
                    initialRotation = Quaternion.identity,
                    worldPosition = new Vector2(15f, 6f),
                    worldRotation = Quaternion.AngleAxis(35f, Vector3.forward),
                    length = 12f,
                    visible = true
                }
            });

            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.EditBone);

            InvokePrivateMethod(window, "SetBottomToolMode", ReanimatorToolMode.PreviewPose);

            var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertEqual(ReanimatorToolMode.PreviewPose, GetPrivateField<ReanimatorToolMode>(window, "bottomToolMode"), "Tool transition fixture should enter PreviewPose.");
            AssertVector(new Vector2(15f, 6f), bones[0].initialPosition, 0.0001f, "Leaving Bones/Edit should commit edited position as rest.");
            AssertFloat(35f, bones[0].initialRotation.eulerAngles.z, 0.001f, "Leaving Bones/Edit should commit edited rotation as rest.");
            AssertTrue((bool)InvokePrivateMethod(renderer, "IsCurrentBonePoseRest"), "Leaving Bones/Edit should not leave a preview deformation delta.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorWindowRestoresPreviewPoseBeforeBoneEdit()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(renderer, "hasPendingChanges", false);
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "root",
                    parentId = -1,
                    position = new Vector2(18f, 11f),
                    initialPosition = new Vector2(3f, 4f),
                    rotation = Quaternion.AngleAxis(70f, Vector3.forward),
                    initialRotation = Quaternion.AngleAxis(10f, Vector3.forward),
                    worldPosition = new Vector2(18f, 11f),
                    worldRotation = Quaternion.AngleAxis(70f, Vector3.forward),
                    length = 12f,
                    visible = true
                }
            });

            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.PreviewPose);

            InvokePrivateMethod(window, "SetBottomToolMode", ReanimatorToolMode.EditBone);

            var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertEqual(ReanimatorToolMode.EditBone, GetPrivateField<ReanimatorToolMode>(window, "bottomToolMode"), "Tool transition fixture should enter Bones/Edit.");
            AssertVector(new Vector2(3f, 4f), bones[0].position, 0.0001f, "Entering Bones/Edit should discard Preview Pose position delta.");
            AssertVector(new Vector2(3f, 4f), bones[0].initialPosition, 0.0001f, "Entering Bones/Edit must not commit Preview Pose position as rest.");
            AssertFloat(10f, bones[0].rotation.eulerAngles.z, 0.001f, "Entering Bones/Edit should discard Preview Pose rotation delta.");
            AssertFloat(10f, bones[0].initialRotation.eulerAngles.z, 0.001f, "Entering Bones/Edit must not commit Preview Pose rotation as rest.");
            AssertFalse(GetPrivateField<bool>(renderer, "hasPendingChanges"), "Discarding Preview Pose before Bones/Edit must not dirty the renderer.");
            AssertTrue((bool)InvokePrivateMethod(renderer, "IsCurrentBonePoseRest"), "Bones/Edit should start from rest pose.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorWindowCommitsBoneEditPoseBeforeApply()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "root",
                    parentId = -1,
                    position = new Vector2(21f, 7f),
                    initialPosition = Vector2.zero,
                    rotation = Quaternion.AngleAxis(15f, Vector3.forward),
                    initialRotation = Quaternion.identity,
                    worldPosition = new Vector2(21f, 7f),
                    worldRotation = Quaternion.AngleAxis(15f, Vector3.forward),
                    length = 12f,
                    visible = true
                }
            });

            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.EditBone);

            AssertTrue((bool)InvokePrivateMethod(window, "CommitBoneEditRestPoseIfActive"), "Apply path should commit active Bones/Edit rest pose before renderer Apply restores preview pose.");

            var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertVector(new Vector2(21f, 7f), bones[0].initialPosition, 0.0001f, "Apply helper should commit edited bone position as rest.");
            AssertFloat(15f, bones[0].initialRotation.eulerAngles.z, 0.001f, "Apply helper should commit edited bone rotation as rest.");
            AssertTrue((bool)InvokePrivateMethod(renderer, "IsCurrentBonePoseRest"), "Apply helper should not leave a preview deformation delta.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void ReanimatorRestoreDoesNotRevertBoneEditRestPose()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            SetPrivateField(renderer, "bones", new List<PsdLayerPreviewRenderer.BoneInfo>
            {
                new PsdLayerPreviewRenderer.BoneInfo
                {
                    index = 0,
                    name = "root",
                    parentId = -1,
                    position = new Vector2(31f, 9f),
                    initialPosition = Vector2.zero,
                    rotation = Quaternion.AngleAxis(55f, Vector3.forward),
                    initialRotation = Quaternion.identity,
                    worldPosition = new Vector2(31f, 9f),
                    worldRotation = Quaternion.AngleAxis(55f, Vector3.forward),
                    length = 12f,
                    visible = true
                }
            });

            SetPrivateField(window, "layerPreviewRenderer", renderer);
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.EditBone);

            InvokePrivateMethod(window, "RestorePreviewPose");

            var bones = GetPrivateField<List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones");
            AssertVector(new Vector2(31f, 9f), bones[0].position, 0.0001f, "Restore in Bones/Edit must not move the edited bone back to the old preview rest.");
            AssertVector(new Vector2(31f, 9f), bones[0].initialPosition, 0.0001f, "Restore in Bones/Edit should commit the edited bone position as rest.");
            AssertFloat(55f, bones[0].rotation.eulerAngles.z, 0.001f, "Restore in Bones/Edit must not rotate the edited bone back to the old preview rest.");
            AssertFloat(55f, bones[0].initialRotation.eulerAngles.z, 0.001f, "Restore in Bones/Edit should commit the edited bone rotation as rest.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void ProductionReanimatorCodeAvoidsUnityWarningSpam()
    {
        var root = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator");
        AssertTrue(Directory.Exists(root), "Reanimator source root should exist.");

        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("Debug.LogWarning("))
            .Select(path => path.Replace(Application.dataPath, "Assets").Replace('\\', '/'))
            .ToArray();

        AssertEqual(
            0,
            offenders.Length,
            "Production Reanimator code should keep warning-spam out of the Unity console. Offenders: " + string.Join(", ", offenders));
    }

    private static void ProductionReanimatorTraceLogsUseWrapper()
    {
        var root = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator");
        AssertTrue(Directory.Exists(root), "Reanimator source root should exist.");

        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
            .Where(path => !path.EndsWith("ReanimatorLog.cs", System.StringComparison.Ordinal))
            .SelectMany(path => FindDirectDebugLogLines(path, root))
            .ToArray();

        AssertEqual(
            0,
            offenders.Length,
            "Production Reanimator logs should go through ReanimatorLog. Offenders: " + string.Join(", ", offenders));
    }

    private static void ProductionReanimatorAvoidsGlobalAssetSave()
    {
        var root = Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator");
        AssertTrue(Directory.Exists(root), "Reanimator source root should exist.");

        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("AssetDatabase.SaveAssets("))
            .Select(path => path.Replace(Application.dataPath, "Assets").Replace('\\', '/'))
            .ToArray();

        AssertEqual(
            0,
            offenders.Length,
            "Production Reanimator code should save explicit assets instead of all dirty assets. Offenders: " + string.Join(", ", offenders));
    }

    private static string[] FindDirectDebugLogLines(string path, string root)
    {
        var lines = File.ReadAllLines(path);
        return lines
            .Select((line, index) => new { line, index })
            .Where(item => item.line.Contains("Debug.Log(") ||
                           item.line.Contains("Debug.LogError(") ||
                           item.line.Contains("Debug.LogException("))
            .Select(item => $"{path.Substring(root.Length + 1).Replace('\\', '/')}:{item.index + 1}")
            .ToArray();
    }

    private static void WindowLayoutClampsRightPanelWidthSafely()
    {
        AssertFloat(300f, ReanimatorWindowLayout.ClampRightPanelWidth(0f, 1000f), 0.0001f, "Default right panel width.");
        AssertFloat(240f, ReanimatorWindowLayout.ClampRightPanelWidth(100f, 1000f), 0.0001f, "Minimum right panel width.");
        AssertFloat(560f, ReanimatorWindowLayout.ClampRightPanelWidth(900f, 1000f), 0.0001f, "Maximum right panel width.");
        AssertFloat(240f, ReanimatorWindowLayout.ClampRightPanelWidth(900f, 500f), 0.0001f, "Narrow window should clamp to minimum.");
    }

    private static void WindowLayoutCalculatesNonOverlappingPanels()
    {
        var layout = ReanimatorWindowLayout.Calculate(
            new Rect(0f, 0f, 1000f, 700f),
            new Rect(0f, 0f, 1000f, 30f),
            new Rect(0f, 30f, 1000f, 20f),
            320f);

        AssertRect(new Rect(0f, 58f, 1000f, 642f), layout.ContentRect, 0.0001f, "Content rect.");
        AssertRect(new Rect(0f, 0f, 220f, 642f), layout.LeftPanelRect, 0.0001f, "Left panel rect.");
        AssertRect(new Rect(220f, 0f, 460f, 642f), layout.PreviewRect, 0.0001f, "Preview rect.");
        AssertRect(new Rect(680f, 0f, 320f, 642f), layout.RightPanelRect, 0.0001f, "Right panel rect.");
        AssertRect(new Rect(677.5f, 0f, 5f, 642f), layout.RightPanelSplitterRect, 0.0001f, "Right splitter rect.");
    }

    private static void WindowLayoutPreservesPreviewWidthWhenPossible()
    {
        var layout = ReanimatorWindowLayout.Calculate(
            new Rect(0f, 0f, 700f, 500f),
            new Rect(0f, 0f, 700f, 20f),
            new Rect(0f, 20f, 700f, 20f),
            560f);

        AssertFloat(300f, layout.RightPanelRect.width, 0.0001f, "Right panel should shrink for preview.");
        AssertFloat(180f, layout.PreviewRect.width, 0.0001f, "Preview should keep its minimum width.");
        AssertFloat(layout.PreviewRect.xMax, layout.RightPanelRect.x, 0.0001f, "Preview should end where right panel begins.");
        AssertFloat(layout.RightPanelRect.x - 2.5f, layout.RightPanelSplitterRect.x, 0.0001f, "Splitter should straddle right panel edge.");
    }

    private static void PreviewToolbarButtonsStayInsidePreview()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var previewRect = new Rect(0f, 0f, 180f, 120f);
            var pinRect = (Rect)InvokePrivateMethod(window, "GetPreviewToolbarButtonRect", previewRect, 0);
            var isolateRect = (Rect)InvokePrivateMethod(window, "GetPreviewToolbarButtonRect", previewRect, 1);
            var deselectRect = (Rect)InvokePrivateMethod(window, "GetPreviewToolbarButtonRect", previewRect, 2);

            AssertRect(new Rect(6f, 6f, 34f, 20f), pinRect, 0.0001f, "Pin button rect.");
            AssertRect(new Rect(44f, 6f, 34f, 20f), isolateRect, 0.0001f, "Isolate button rect.");
            AssertRect(new Rect(82f, 6f, 34f, 20f), deselectRect, 0.0001f, "Deselect button rect.");
            AssertTrue(previewRect.Contains(pinRect.min), "Pin button should start inside preview.");
            AssertTrue(previewRect.Contains(pinRect.max), "Pin button should end inside preview.");
            AssertTrue(previewRect.Contains(isolateRect.min), "Isolate button should start inside preview.");
            AssertTrue(previewRect.Contains(isolateRect.max), "Isolate button should end inside preview.");
            AssertTrue(previewRect.Contains(deselectRect.min), "Deselect button should start inside preview.");
            AssertTrue(previewRect.Contains(deselectRect.max), "Deselect button should end inside preview.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void PreviewDrawOptionsActivateOneToolInSkinningMode()
    {
        var cases = new[]
        {
            new ToolFlagCase(ReanimatorToolMode.AutoGeometry, "AutoGeometry", tools => tools.AutoGeometry),
            new ToolFlagCase(ReanimatorToolMode.Elbow, "Elbow", tools => tools.Elbow),
            new ToolFlagCase(ReanimatorToolMode.Limbs, "Limb", tools => tools.Limb),
            new ToolFlagCase(ReanimatorToolMode.Boob, "Boob", tools => tools.Boob),
            new ToolFlagCase(ReanimatorToolMode.Curve, "Curve", tools => tools.Curve),
            new ToolFlagCase(ReanimatorToolMode.WeightSlider, "WeightSlider", tools => tools.WeightSlider),
            new ToolFlagCase(ReanimatorToolMode.CreateEdge, "CreateEdge", tools => tools.CreateEdge),
            new ToolFlagCase(ReanimatorToolMode.SplitEdge, "SplitEdge", tools => tools.SplitEdge),
            new ToolFlagCase(ReanimatorToolMode.EditGeometry, "EditGeometry", tools => tools.EditGeometry),
            new ToolFlagCase(ReanimatorToolMode.DeprecatedCreateVertex, "DeprecatedCreateVertex", tools => tools.EditGeometry),
            new ToolFlagCase(ReanimatorToolMode.RadiusCull, "RadiusCull", tools => tools.RadiusCull),
            new ToolFlagCase(ReanimatorToolMode.Joint, "Joint", tools => tools.Joint),
            new ToolFlagCase(ReanimatorToolMode.BoneInfluence, "BoneInfluence", tools => tools.BoneInfluence),
            new ToolFlagCase(ReanimatorToolMode.SpriteInfluence, "SpriteInfluence", tools => tools.SpriteInfluence)
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var options = PsdPreviewDrawOptions.CreateDefault();
            ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
                options,
                cases[i].Mode,
                spriteMode: false,
                radiusCullRadius: 0.2f,
                radiusCullRemoveContourVertices: true,
                radiusCullRemoveInnerVertices: true,
                limbsUseRadiusCull: true,
                jointUseRadiusCull: true,
                jointCPairOffset: 8f,
                jointLongPoints: 4,
                jointStartLongPointsOffset: 12f,
                jointEndLongPointsOffset: 12f);

            AssertFalse(options.SpriteAtlasMode, cases[i].Name + " should not set sprite atlas mode.");
            AssertTrue(options.DrawBones, cases[i].Name + " should draw bones.");
            AssertTrue(cases[i].GetFlag(options.Tools), cases[i].Name + " expected tool flag.");
            AssertEqual(1, CountActiveToolFlags(options.Tools), cases[i].Name + " active tool count.");
            AssertTrue(options.AllowBonePoseEditing, cases[i].Name + " should allow preview bone pose editing.");
            AssertTrue(options.AllowBoneTransformEditing, cases[i].Name + " should allow bone transform input.");
            AssertFalse(options.AllowBoneLengthEditing, cases[i].Name + " should not resize rest bones outside Bones/Edit.");
            AssertTrue(options.DeformSpritesWithBonePose, cases[i].Name + " should deform sprites with preview bone pose.");
        }

        var poseOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            poseOptions,
            ReanimatorToolMode.PreviewPose,
            spriteMode: false,
            radiusCullRadius: 0.2f,
            radiusCullRemoveContourVertices: true,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: true,
            jointCPairOffset: 8f,
            jointLongPoints: 4,
            jointStartLongPointsOffset: 12f,
            jointEndLongPointsOffset: 12f);
        AssertTrue(poseOptions.AllowBonePoseEditing, "PreviewPose should allow preview bone pose editing.");
        AssertTrue(poseOptions.AllowBoneTransformEditing, "PreviewPose should allow bone transform input.");
        AssertFalse(poseOptions.AllowBoneLengthEditing, "PreviewPose should not resize rest bones.");
        AssertTrue(poseOptions.DeformSpritesWithBonePose, "PreviewPose should deform sprites with preview bone pose.");

        var weightBrushOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            weightBrushOptions,
            ReanimatorToolMode.WeightBrush,
            spriteMode: false,
            radiusCullRadius: 0.2f,
            radiusCullRemoveContourVertices: true,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: true,
            jointCPairOffset: 8f,
            jointLongPoints: 4,
            jointStartLongPointsOffset: 12f,
            jointEndLongPointsOffset: 12f);
        AssertTrue(weightBrushOptions.AllowBonePoseEditing, "WeightBrush should allow rotating bones while painting weights.");
        AssertTrue(weightBrushOptions.AllowBoneTransformEditing, "WeightBrush should allow bone transform input while painting weights.");
        AssertFalse(weightBrushOptions.AllowBoneLengthEditing, "WeightBrush should not resize rest bones while painting weights.");
        AssertTrue(weightBrushOptions.DeformSpritesWithBonePose, "WeightBrush should show weight changes under preview bone pose.");

        var editBoneOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            editBoneOptions,
            ReanimatorToolMode.EditBone,
            spriteMode: false,
            radiusCullRadius: 0.2f,
            radiusCullRemoveContourVertices: true,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: true,
            jointCPairOffset: 8f,
            jointLongPoints: 4,
            jointStartLongPointsOffset: 12f,
            jointEndLongPointsOffset: 12f);
        AssertFalse(editBoneOptions.AllowBonePoseEditing, "EditBone should lock preview pose while editing bones.");
        AssertTrue(editBoneOptions.AllowBoneTransformEditing, "EditBone should allow moving and rotating rest bones in the preview.");
        AssertTrue(editBoneOptions.AllowBoneLengthEditing, "EditBone should allow resizing a rest bone from its tip.");
        AssertFalse(editBoneOptions.DeformSpritesWithBonePose, "EditBone should keep sprites in restored pose while editing bones.");

        var volumeOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            volumeOptions,
            ReanimatorToolMode.VolumeCurve,
            spriteMode: false,
            radiusCullRadius: 0.2f,
            radiusCullRemoveContourVertices: true,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: true,
            jointCPairOffset: 8f,
            jointLongPoints: 4,
            jointStartLongPointsOffset: 12f,
            jointEndLongPointsOffset: 12f);
        AssertTrue(volumeOptions.AllowBonePoseEditing, "Volume should not be globally pose-locked by tool mode.");
        AssertTrue(volumeOptions.AllowBoneTransformEditing, "Volume should not be globally transform-locked by tool mode.");
        AssertFalse(volumeOptions.AllowBoneLengthEditing, "Volume should not resize rest bones.");
        AssertTrue(volumeOptions.DeformSpritesWithBonePose, "Volume should preserve preview bone pose deformation when available.");
    }

    private static void PreviewDrawLoopRoutesBoneInputThroughTransformFlag()
    {
        var drawLoopSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.DrawLoop.cs"));

        AssertTrue(
            drawLoopSource.Contains("HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing)"),
            "Bone input should use the transform and length-editing flags so Bones/Edit can edit rest bones.");
        AssertFalse(
            drawLoopSource.Contains("HandleBoneInput(localRect, coordinateSpace, options.AllowBonePoseEditing)"),
            "Bone input must not use the preview-pose flag; Bones/Edit intentionally locks pose deformation but still edits bone transforms.");
    }

    private static void BoneNamePresetsResolveParentSuffixes()
    {
        var bones = new List<PsdLayerPreviewRenderer.BoneInfo>
        {
            new PsdLayerPreviewRenderer.BoneInfo { index = 0, name = "l_arm", parentId = -1 },
            new PsdLayerPreviewRenderer.BoneInfo { index = 1, name = "bone_1", parentId = 0 },
            new PsdLayerPreviewRenderer.BoneInfo { index = 2, name = "bone_2", parentId = 1 }
        };

        AssertEqual("l_arm_1", Reanimator.BuildBoneNameFromParent("l_arm", bones, 1), "Child of same-named parent should receive _1.");
        bones[0].name = "l_arm_7";
        AssertEqual("l_arm_8", Reanimator.BuildBoneNameFromParent("l_arm", bones, 1), "Child of suffixed same-base parent should increment suffix.");
        bones[0].name = "chest";
        AssertEqual("l_arm", Reanimator.BuildBoneNameFromParent("l_arm", bones, 1), "Different parent base should keep selected preset.");
        AssertEqual("head", Reanimator.BuildBoneNameFromParent(" head ", bones, 1), "Preset names should be trimmed.");
    }

    private static void LimbApplyStaysInLimbsMode()
    {
        var geometryToolsSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.GeometryTools.cs"));
        var start = geometryToolsSource.IndexOf("private void DrawLimbsToolPanel", System.StringComparison.Ordinal);
        var end = geometryToolsSource.IndexOf("private void DrawBoobToolPanel", System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Limbs tool panel source should be locatable.");
        var limbsPanelSource = geometryToolsSource.Substring(start, end - start);

        AssertTrue(
            limbsPanelSource.Contains("ShowNotification(new GUIContent(string.IsNullOrEmpty(error) ? \"Limb applied\" : error))"),
            "Limb Apply should show success or the successful CircleClean fallback warning.");
        AssertTrue(limbsPanelSource.Contains("Repaint();"), "Limb Apply should repaint the current tool after committing geometry.");
        AssertFalse(
            limbsPanelSource.Contains("SetBottomToolMode(ReanimatorToolMode.PreviewPose)"),
            "Limb Apply must not immediately leave Limbs mode and hide the committed geometry overlay.");
    }

    private static void WeightBrushMouseDownPaintsInsteadOfSelectingPreviewBone()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var classStart = moduleSource.IndexOf("internal sealed partial class ReanimatorWeightBrushModule", System.StringComparison.Ordinal);
        AssertTrue(classStart >= 0, "Weight brush module source should be locatable.");
        var start = moduleSource.IndexOf("public void HandlePreviewInput", classStart, System.StringComparison.Ordinal);
        var end = moduleSource.IndexOf("public void DrawPreviewOverlay", classStart, System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Weight brush preview input source should be locatable.");
        var inputSource = moduleSource.Substring(start, end - start);

        AssertTrue(
            inputSource.Contains("e.type == EventType.MouseDown && e.button == 1") &&
            inputSource.IndexOf("TrySelectWeightBrushBoneAt", System.StringComparison.Ordinal) <
            inputSource.IndexOf("var isMouseEvent", System.StringComparison.Ordinal),
            "Weight Brush bone selection should be isolated to the right-click branch before left-button painting.");
        AssertTrue(
            inputSource.Contains("ApplyBrushAt("),
            "Weight Brush mouse down should still apply the brush stroke.");
    }

    private static void WeightBrushRightClickSelectsMappedPreviewBone()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var classStart = moduleSource.IndexOf("internal sealed partial class ReanimatorWeightBrushModule", System.StringComparison.Ordinal);
        AssertTrue(classStart >= 0, "Weight brush module source should be locatable.");
        var start = moduleSource.IndexOf("public void HandlePreviewInput", classStart, System.StringComparison.Ordinal);
        var end = moduleSource.IndexOf("public void DrawPreviewOverlay", classStart, System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Weight brush preview input source should be locatable.");
        var inputSource = moduleSource.Substring(start, end - start);

        AssertTrue(inputSource.Contains("api.TrySelectWeightBrushBoneAt(context, e.mousePosition, out var selectedSpriteBone)"), "Right-click should resolve the mapped sprite bone under the cursor.");
        AssertTrue(inputSource.Contains("boneIndex = selectedSpriteBone;"), "Right-click should make the resolved bone active in Weight Brush.");
        var failedSelectionStart = inputSource.IndexOf("if (!api.TrySelectWeightBrushBoneAt", System.StringComparison.Ordinal);
        var selectedBoneStart = inputSource.IndexOf("boneIndex = selectedSpriteBone;", System.StringComparison.Ordinal);
        AssertTrue(failedSelectionStart >= 0 && selectedBoneStart > failedSelectionStart, "Right-click selection should expose separate failed and successful paths.");
        var failedSelectionSource = inputSource.Substring(failedSelectionStart, selectedBoneStart - failedSelectionStart);
        AssertTrue(failedSelectionSource.Contains("e.Use();"), "Right-click on an unmapped bone should consume the event.");
        var successfulSelectionEnd = inputSource.IndexOf("var isMouseEvent", selectedBoneStart, System.StringComparison.Ordinal);
        AssertTrue(successfulSelectionEnd > selectedBoneStart, "Right-click success path should be bounded before brush painting input.");
        var successfulSelectionSource = inputSource.Substring(selectedBoneStart, successfulSelectionEnd - selectedBoneStart);
        AssertFalse(successfulSelectionSource.Contains("e.Use();"), "Right-click on a mapped bone should continue into shared bone input.");
    }

    private static void WeightBrushRightDragRotatesMappedPreviewBone()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var contractSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/System/IReanimatorPreviewModule.cs"));
        var drawLoopSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.DrawLoop.cs"));
        var boneInputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.BoneSpriteSelection.cs"));

        AssertTrue(contractSource.Contains("internal interface IReanimatorBoneInputModule"), "Preview modules should configure shared bone input through an explicit contract.");
        AssertTrue(moduleSource.Contains("IReanimatorBoneInputModule") && moduleSource.Contains("new ReanimatorBoneInputOptions(1, true)"), "Weight Brush should bind shared bone rotation to the right mouse button.");
        AssertTrue(drawLoopSource.Contains("boneInputModule.BoneInputOptions") && drawLoopSource.Contains("HandleBoneInput(localRect, coordinateSpace, options.AllowBoneTransformEditing, options.AllowBoneLengthEditing, boneInputOptions)"), "The preview draw loop should pass module bone input options to the shared handler.");
        AssertTrue(boneInputSource.Contains("e.button != inputOptions.MouseButton"), "Shared bone input should honor the module mouse button.");
        AssertTrue(boneInputSource.Contains("if (inputOptions.ForceRotation)"), "Shared bone input should force rotation instead of moving a bone by its base.");
    }

    private static void WeightBrushAltShiftClickAddsBoneInsteadOfReplacing()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var classStart = moduleSource.IndexOf("internal sealed partial class ReanimatorWeightBrushModule", System.StringComparison.Ordinal);
        AssertTrue(classStart >= 0, "Weight brush module source should be locatable.");
        var start = moduleSource.IndexOf("public void HandlePreviewInput", classStart, System.StringComparison.Ordinal);
        var end = moduleSource.IndexOf("public void DrawPreviewOverlay", classStart, System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Weight brush preview input source should be locatable.");
        var inputSource = moduleSource.Substring(start, end - start);

        AssertTrue(
            inputSource.Contains("if (IsShiftModifier(e))") && inputSource.Contains("api.TryAddWeightBrushBoneAt("),
            "Alt+Shift click should route to additive sprite-bone assignment.");
        AssertTrue(
            inputSource.Contains("else if (api.TryReplaceWeightBrushBoneAt("),
            "Alt click without Shift should keep replacing the active sprite bone.");
    }

    private static void WeightBrushNoneKeepsSelectedPreviewBoneForPainting()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var classStart = moduleSource.IndexOf("internal sealed partial class ReanimatorWeightBrushModule", System.StringComparison.Ordinal);
        AssertTrue(classStart >= 0, "Weight brush module source should be locatable.");
        var start = moduleSource.IndexOf("private void SyncSelectedBone(Reanimator.SystemApi api)", classStart, System.StringComparison.Ordinal);
        var end = moduleSource.IndexOf("private void SyncSelectedBone(Reanimator.SystemApi api, PsdLayerPreviewRenderer.WeightBrushTargetInfo target)", classStart, System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Weight brush paint sync source should be locatable.");
        var syncSource = moduleSource.Substring(start, end - start);

        AssertTrue(
            syncSource.Contains("api == null || boneIndex < 0"),
            "Weight Brush painting with Bone=None must keep the preview-selected bone instead of clearing it.");
        AssertFalse(
            syncSource.Contains("TrySelectWeightBrushBone(-1)"),
            "Weight Brush painting with Bone=None must not clear the selected preview bone before applying.");
    }

    private static void WeightBrushColorRightClickRemovesBoneSlot()
    {
        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/Weights/ReanimatorWeightModules.cs"));
        var classStart = moduleSource.IndexOf("internal sealed partial class ReanimatorWeightBrushModule", System.StringComparison.Ordinal);
        AssertTrue(classStart >= 0, "Weight brush module source should be locatable.");
        var start = moduleSource.IndexOf("private void HandleBoneColorButtonInput", classStart, System.StringComparison.Ordinal);
        var end = moduleSource.IndexOf("private static Color GetBoneButtonColor", classStart, System.StringComparison.Ordinal);
        AssertTrue(start >= 0 && end > start, "Weight brush color button input source should be locatable.");
        var inputSource = moduleSource.Substring(start, end - start);

        AssertTrue(inputSource.Contains("e.button == 1"), "Weight Brush color squares should handle right mouse down.");
        AssertTrue(inputSource.Contains("api.RemoveWeightBrushBoneSlot(index"), "Right-clicking a bone color should remove its sprite bone slot.");
        AssertTrue(inputSource.Contains("if (!valid)"), "Invalid color slots should not be removable.");
    }

    private static void WeightSliderDisablingSlotClearsBoneIdentity()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "editGeometryWeightBoneIndices", new[] { 2, 1, -1, -1 });
            SetPrivateField(window, "editGeometryWeightValues", new[] { 1f, 0f, 0f, 0f });
            SetPrivateField(window, "vertexWeights", new[] { 1f, 0f, 0f, 0f });
            SetPrivateField(window, "vertexWeightEnabled", new[] { true, true, false, false });

            var selection = new PsdLayerPreviewRenderer.EditGeometrySelectionInfo(
                1,
                "weight-layer",
                0,
                1,
                Vector2.zero,
                default,
                3,
                new[] { "root", "left", "right" });

            InvokePrivateMethod(window, "ApplyWeightSliderSlotChange", selection, 1, true, false, 2, 2, 0f);

            var boneIndices = GetPrivateField<int[]>(window, "editGeometryWeightBoneIndices");
            var weights = GetPrivateField<float[]>(window, "vertexWeights");
            var enabled = GetPrivateField<bool[]>(window, "vertexWeightEnabled");

            AssertEqual(-1, boneIndices[1], "Disabling a Weight Slider slot should remove its bone identity.");
            AssertFalse(enabled[1], "Disabling a Weight Slider slot should not re-enable itself while applying zero weight.");
            AssertFloat(0f, weights[1], 0.0001f, "Disabled Weight Slider slot should keep zero weight.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void WeightSliderPreviewPointsAreAlwaysEnabled()
    {
        var panelSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.Weight.cs"));
        AssertFalse(panelSource.Contains("prewPoints"), "Weight Slider should not expose a preview-points checkbox.");
        AssertFalse(panelSource.Contains("weightPreviewPoints"), "Weight Slider panel should not keep a disabled preview-points state.");

        var inputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.WeightSlider.cs"));
        var drawStart = inputSource.IndexOf("private void DrawWeightSliderSelectedVertices", System.StringComparison.Ordinal);
        var drawEnd = inputSource.IndexOf("private void DrawWeightSliderBonePreviewPoints", drawStart, System.StringComparison.Ordinal);
        AssertTrue(drawStart >= 0 && drawEnd > drawStart, "Weight Slider selected-vertex draw source should be locatable.");
        var drawSource = inputSource.Substring(drawStart, drawEnd - drawStart);
        AssertTrue(
            drawSource.Contains("DrawWeightSliderBonePreviewPoints(data, stableKey, vertexIndex, p, toCanvasVertex, alpha);"),
            "Weight Slider should always draw preview points for selected vertices.");
        AssertFalse(drawSource.Contains("WeightSliderPreviewPoints"), "Preview point drawing should not depend on a toggleable renderer flag.");
    }

    private static void WeightSliderRowsReorderOnlyAfterDragRelease()
    {
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            SetPrivateField(window, "editGeometrySelectionLayerToken", 17);
            SetPrivateField(window, "editGeometrySelectionVertexIndex", 3);
            SetPrivateField(window, "editGeometryWeightBoneIndices", new[] { 0, 1, -1, -1 });
            SetPrivateField(window, "editGeometryWeightValues", new[] { 0.6f, 0.4f, 0f, 0f });
            SetPrivateField(window, "vertexWeights", new[] { 0.6f, 0.4f, 0f, 0f });
            SetPrivateField(window, "vertexWeightEnabled", new[] { true, true, false, false });

            var selection = new PsdLayerPreviewRenderer.EditGeometrySelectionInfo(
                17,
                "weight-layer",
                3,
                4,
                Vector2.zero,
                new BoneWeight
                {
                    boneIndex0 = 1,
                    weight0 = 0.61225f,
                    boneIndex1 = 0,
                    weight1 = 0.38775f
                },
                2,
                new[] { "first", "second" });

            InvokePrivateMethod(window, "SyncEditGeometryWeightUi", selection, true);
            AssertArray(
                new[] { 0, 1, -1, -1 },
                GetPrivateField<int[]>(window, "editGeometryWeightBoneIndices"),
                "Rows should keep their bone identity while a UI or preview drag is active.");
            AssertFloat(0.38775f, GetPrivateField<float[]>(window, "vertexWeights")[0], 0.0001f, "Stable first row live weight.");
            AssertFloat(0.61225f, GetPrivateField<float[]>(window, "vertexWeights")[1], 0.0001f, "Stable second row live weight.");

            InvokePrivateMethod(window, "SyncEditGeometryWeightUi", selection, false);
            AssertArray(
                new[] { 1, 0, -1, -1 },
                GetPrivateField<int[]>(window, "editGeometryWeightBoneIndices"),
                "Rows may accept the final weight order after drag release.");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void WeightSliderPreviewDragUsesPointerGrabOffset()
    {
        var inputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.WeightSlider.cs"));
        AssertTrue(
            inputSource.Contains("pointerOffsetCanvas = mouseCanvas - toCanvasVertex(drawVertices[selectedVertexIndex])"),
            "Preview drag should capture a cursor offset for every selected vertex through the layer mapper.");
        AssertTrue(
            inputSource.Contains("state.pointerOffsetCanvas,\n                    state.sourceWeights,\n                    weights"),
            "Preview drag should use each selected vertex's captured cursor offset when calculating weights.");
    }

    private static void WeightSliderMultiPreviewDragUsesIndependentVertexStates()
    {
        var inputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.WeightSlider.cs"));

        AssertTrue(
            inputSource.Contains("IsWeightSliderVertexSelected(selectedHit.stableKey, selectedHit.vertexIndex)"),
            "Clicking a selected Weight Slider vertex should preserve the existing group for dragging.");
        AssertTrue(
            inputSource.Contains("var dragStates = new List<WeightSliderVertexDragState>();"),
            "Multi-vertex preview drag should snapshot independent state for each selected vertex.");
        AssertTrue(
            inputSource.Contains("for (var stateIndex = 0; stateIndex < weightSliderWeightDragStates.Length; stateIndex++)"),
            "Multi-vertex preview drag should calculate every selected vertex independently.");
        AssertTrue(
            inputSource.Contains("updates.Add(new KeyValuePair<int, BoneWeight>(state.vertexIndex, boneWeight));"),
            "Multi-vertex preview drag should write each vertex's own calculated BoneWeight.");
    }

    private static void WeightSliderPreviewDragKeepsAvailableRegionAtZero()
    {
        var inputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.WeightSlider.cs"));
        var beginStart = inputSource.IndexOf("private bool BeginWeightSliderWeightDrag", System.StringComparison.Ordinal);
        var beginEnd = inputSource.IndexOf("private bool ApplyWeightSliderWeightDrag", beginStart, System.StringComparison.Ordinal);
        var drawStart = inputSource.IndexOf("private void DrawWeightSliderBonePreviewPoints", System.StringComparison.Ordinal);
        var drawEnd = inputSource.IndexOf("private void DrawWeightSliderPreviewPointPolygon", drawStart, System.StringComparison.Ordinal);

        AssertTrue(beginStart >= 0 && beginEnd > beginStart, "Preview drag source should be locatable.");
        AssertTrue(drawStart >= 0 && drawEnd > drawStart, "Preview point drawing source should be locatable.");

        var beginSource = inputSource.Substring(beginStart, beginEnd - beginStart);
        var drawSource = inputSource.Substring(drawStart, drawEnd - drawStart);
        AssertFalse(
            beginSource.Contains("data.spriteBoneToRendererBone, false"),
            "Preview drag should keep zero-weight bone identities in its available region.");
        AssertFalse(
            drawSource.Contains("data.spriteBoneToRendererBone, false"),
            "Preview drawing should keep zero-weight points and the available region visible.");
    }

    private static void WeightSliderVertexRightClickSmoothsSelection()
    {
        var inputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.WeightSlider.cs"));
        var layoutSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Layout.cs"));

        AssertTrue(
            inputSource.Contains("var smoothVertex = e.type == EventType.MouseDown && e.button == 1;"),
            "Weight Slider vertices should recognize right mouse down as smoothing input.");
        AssertTrue(
            inputSource.Contains("TrySmoothWeightSliderSelection(weightSliderSmoothStrength);"),
            "Right-clicking a Weight Slider vertex should use the shared Smooth Selection operation.");
        AssertTrue(
            inputSource.Contains("smoothVertex ? false : e.shift"),
            "Right-click smoothing should make the clicked vertex the smoothing selection.");
        AssertTrue(
            layoutSource.Contains("layerPreviewRenderer.WeightSliderSmoothStrength = weightSmoothAmount;"),
            "Preview right-click smoothing should use the panel's current Smooth amount.");
    }

    private static void BoneNamePresetsSortByName()
    {
        var sorted = Reanimator.SortBoneNamePresetsByName(new[]
        {
            "z_arm",
            " head_2 ",
            "head",
            "l_arm",
            "L_arm",
            "root_2",
            "root_10",
            " arm "
        });

        AssertArray(
            new[] { " arm ", "head", " head_2 ", "L_arm", "l_arm", "root_10", "root_2", "z_arm" },
            sorted,
            "Bone name preset sorting should use trimmed names with deterministic ordinal ties.");
    }

    private static void BoneNamePresetsAssetContainsNudeBodyBones()
    {
        var preset = AssetDatabase.LoadAssetAtPath<ReanimatorBoneNamePresetAsset>(
            "Assets/Plugins/Reanimator/Presets/Reanimator Bone Name Presets.asset");
        AssertTrue(preset != null, "Bone name preset asset should exist.");
        AssertTrue(preset.names != null, "Bone name preset asset should expose an editable names list.");

        var expected = new[]
        {
            "root", "bot", "belly", "chest", "neck", "l_arm", "l_elbow", "l_hand",
            "r_arm", "r_elbow", "r_hand", "l_leg", "l_knee", "l_foot", "r_leg",
            "r_knee", "r_foot", "head", "l_lock", "pony", "r_lock", "face_form",
            "r_boob", "r_nipple", "l_boob", "l_nipple", "pony_2", "boobs_root",
            "r_foot_3", "l_foot_4", "l_lock_5", "r_lock_6"
        };

        for (var i = 0; i < expected.Length; i++)
            AssertTrue(preset.names.Contains(expected[i]), $"Bone name preset asset should include {expected[i]} from 1.45_body_nude.psd.");
    }

    private static void PreviewDrawOptionsDisableToolsInSpriteAtlasMode()
    {
        var modes = new[]
        {
            ReanimatorToolMode.AutoGeometry,
            ReanimatorToolMode.Curve,
            ReanimatorToolMode.DeprecatedCreateVertex,
            ReanimatorToolMode.Joint,
            ReanimatorToolMode.RadiusCull,
            ReanimatorToolMode.WeightSlider
        };

        for (var i = 0; i < modes.Length; i++)
        {
            var options = PsdPreviewDrawOptions.CreateDefault();
            ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
                options,
                modes[i],
                spriteMode: true,
                radiusCullRadius: 0.2f,
                radiusCullRemoveContourVertices: true,
                radiusCullRemoveInnerVertices: true,
                limbsUseRadiusCull: true,
                jointUseRadiusCull: true,
                jointCPairOffset: 8f,
                jointLongPoints: 4,
                jointStartLongPointsOffset: 12f,
                jointEndLongPointsOffset: 12f);

            AssertTrue(options.SpriteAtlasMode, modes[i] + " should set sprite atlas mode.");
            AssertFalse(options.DrawBones, modes[i] + " should hide bones in sprite atlas mode.");
            AssertFalse(options.AllowBonePoseEditing, modes[i] + " should not edit preview pose in sprite atlas mode.");
            AssertFalse(options.AllowBoneTransformEditing, modes[i] + " should not edit bone transforms in sprite atlas mode.");
            AssertFalse(options.AllowBoneLengthEditing, modes[i] + " should not resize bones in sprite atlas mode.");
            AssertFalse(options.DeformSpritesWithBonePose, modes[i] + " should not deform sprites with preview pose in sprite atlas mode.");
            AssertEqual(0, CountActiveToolFlags(options.Tools), modes[i] + " active tool count in sprite atlas mode.");
        }
    }

    private static void PreviewDrawOptionsConfigureRadiusAndJointSettings()
    {
        var jointOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            jointOptions,
            ReanimatorToolMode.Joint,
            spriteMode: false,
            radiusCullRadius: 0.45f,
            radiusCullRemoveContourVertices: true,
            radiusCullRemoveInnerVertices: false,
            limbsUseRadiusCull: false,
            jointUseRadiusCull: true,
            jointCPairOffset: 9.5f,
            jointLongPoints: 6,
            jointStartLongPointsOffset: 15f,
            jointEndLongPointsOffset: 19f);

        AssertTrue(jointOptions.Tools.Joint, "Joint mode should activate joint tool.");
        AssertFloat(0.45f, jointOptions.RadiusCull.Radius, 0.0001f, "Joint radius.");
        AssertFalse(jointOptions.RadiusCull.RemoveContourVertices, "Joint radius cull should preserve contour vertices.");
        AssertTrue(jointOptions.RadiusCull.RemoveInnerVertices, "Joint radius cull should use joint inner setting.");
        AssertTrue(jointOptions.RadiusCull.JointOverlayActive, "Joint overlay should be active.");
        AssertFalse(jointOptions.RadiusCull.LimbOverlayActive, "Joint should not activate limb overlay.");
        AssertFloat(9.5f, jointOptions.Joint.CPairOffset, 0.0001f, "Joint C-pair offset.");
        AssertEqual(6, jointOptions.Joint.LongPoints, "Joint long points.");
        AssertFloat(15f, jointOptions.Joint.StartLongPointsOffset, 0.0001f, "Joint start offset.");
        AssertFloat(19f, jointOptions.Joint.EndLongPointsOffset, 0.0001f, "Joint end offset.");

        var jointState = PsdPreviewRadiusCullState.Create(jointOptions, hasRadiusOverride: false, radiusOverride: 0f);
        AssertFalse(jointState.InputActive, "Joint radius state should not accept radius input.");
        AssertTrue(jointState.JointPreviewActive, "Joint radius state should preview joint cull.");
        AssertTrue(jointState.ShouldDrawGeometryPreview(hasSelectedLayer: true), "Joint state should draw geometry preview.");

        var limbOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            limbOptions,
            ReanimatorToolMode.Limbs,
            spriteMode: false,
            radiusCullRadius: 0.25f,
            radiusCullRemoveContourVertices: false,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: false,
            jointCPairOffset: 7f,
            jointLongPoints: 3,
            jointStartLongPointsOffset: 11f,
            jointEndLongPointsOffset: 13f);

        AssertTrue(limbOptions.Tools.Limb, "Limb mode should activate limb tool.");
        AssertTrue(limbOptions.RadiusCull.LimbOverlayActive, "Limb overlay should follow limb radius setting.");
        AssertTrue(limbOptions.RadiusCull.LimbHandleInputActive, "Limb handle input should follow limb radius setting.");
        var limbState = PsdPreviewRadiusCullState.Create(limbOptions, hasRadiusOverride: true, radiusOverride: 0.5f);
        AssertFloat(0.5f, limbState.InputRadius, 0.0001f, "Limb state override radius.");
        AssertTrue(limbState.OverlayActive, "Limb state overlay should be active.");
        AssertTrue(limbState.LimbHandleInputActive, "Limb state handle input should be active.");
        AssertTrue(limbState.LimbPreviewActive, "Limb state preview should be active.");
        AssertTrue(limbState.SyncToActiveLimbLine, "Limb state should sync to active limb line.");

        var radiusOptions = PsdPreviewDrawOptions.CreateDefault();
        ReanimatorPreviewDrawOptionsAlgorithms.ApplyToolMode(
            radiusOptions,
            ReanimatorToolMode.RadiusCull,
            spriteMode: false,
            radiusCullRadius: 0.35f,
            radiusCullRemoveContourVertices: false,
            radiusCullRemoveInnerVertices: true,
            limbsUseRadiusCull: true,
            jointUseRadiusCull: false,
            jointCPairOffset: 7f,
            jointLongPoints: 3,
            jointStartLongPointsOffset: 11f,
            jointEndLongPointsOffset: 13f);

        AssertTrue(radiusOptions.Tools.RadiusCull, "Radius mode should activate radius cull tool.");
        AssertFloat(0.35f, radiusOptions.RadiusCull.Radius, 0.0001f, "Radius cull radius.");
        AssertFalse(radiusOptions.RadiusCull.RemoveContourVertices, "Radius cull contour setting.");
        AssertTrue(radiusOptions.RadiusCull.RemoveInnerVertices, "Radius cull inner setting.");
        var radiusState = PsdPreviewRadiusCullState.Create(radiusOptions, hasRadiusOverride: false, radiusOverride: 0f);
        AssertTrue(radiusState.InputActive, "Radius state should accept radius input.");
        AssertTrue(radiusState.OverlayActive, "Radius state overlay should be active.");
    }
}
#endif
