#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    private static void ModuleRegistryDiscoversRequiredModuleAnchors()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        var ids = new HashSet<string>(modules.Select(module => module.Id), StringComparer.Ordinal);
        var sections = new HashSet<string>(modules.Select(module => module.Section), StringComparer.Ordinal);

        AssertTrue(modules.Count >= 20, "Registry should discover the main Reanimator module set.");
        AssertContains(ids, "pose.preview", "Registry module ids.");
        AssertContains(ids, "bones.create", "Registry module ids.");
        AssertContains(ids, "geometry.auto", "Registry module ids.");
        AssertContains(ids, "geometry.volume-curve", "Registry module ids.");
        AssertContains(ids, "weights.brush", "Registry module ids.");
        AssertContains(ids, "weights.projector", "Registry module ids.");
        AssertContains(ids, "face.main", "Registry module ids.");
        AssertContains(ids, "utility.debug-mode", "Registry module ids.");

        AssertContains(sections, "Pose", "Registry sections.");
        AssertContains(sections, "Bones", "Registry sections.");
        AssertContains(sections, "Geometry", "Registry sections.");
        AssertContains(sections, "Weights", "Registry sections.");
        AssertContains(sections, "Utility", "Registry sections.");

        var faceModule = modules.First(module => module.Id == "face.main");
        var elbowModule = modules.First(module => module.Id == "geometry.elbow");
        AssertEqual("Geometry", faceModule.Section, "Face should be in the Geometry section.");
        AssertEqual(elbowModule.RowGroup, faceModule.RowGroup, "Face should share a toolbar row with Elbow.");
        AssertTrue(
            Reanimator.IsLeftToolMarkedIncomplete(faceModule.Label(null), faceModule),
            "Face should use the red incomplete-tool button style.");

        AssertTrue(modules.OfType<IReanimatorPreviewModule>().Any(module => module.Id == "geometry.volume-curve"), "Registry should include preview modules.");
        AssertTrue(modules.OfType<IReanimatorDebugPreviewModule>().Any(module => module.Id == "geometry.volume-curve"), "Registry should include debug preview modules.");
        AssertTrue(modules.OfType<IReanimatorBoneDisplayModule>().Any(module => module.Id == "weights.brush"), "Registry should include bone display modules.");
    }

    private static void FaceEyesPanelRequiresSpriteAndEyelidsPrerequisites()
    {
        var source = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.Face.cs"));

        AssertTrue(source.Contains("Sprite is not selected."), "Eyes should show a missing-sprite error.");
        AssertTrue(source.Contains("Required bone 'eyelids' is missing."), "Eyes should show a missing-eyelids error.");
        AssertTrue(source.Contains("TryFindBoneIndexByName(HeadBoneName"), "Eyes should fall back to the head bone.");
        AssertTrue(source.Contains("Create Eyelids Bone"), "Eyes should expose the eyelids creation button.");
        AssertTrue(source.Contains("headBone.length * EyelidsLengthRatio"), "Eyes should derive eyelids length from head.");
        AssertTrue(source.Contains("private const float EyelidsLengthRatio = 1f / 6f"), "Eyes should make the eyelids bone half its previous length.");
        AssertTrue(source.Contains("alphaAnalysis.LeftHole.Center + alphaAnalysis.RightHole.Center"), "Eyes should place eyelids exactly between both detected eye centers.");
        AssertFalse(source.Contains("EyelidsLeftOffsetRatio"), "Eyes should not apply the old arbitrary left offset.");
        AssertTrue(source.Contains("TryAnalyzeSelectedFaceAlphaMask"), "Eyes should analyze the selected sprite alpha mask.");
        AssertTrue(source.Contains("Point Density"), "Eyes should expose point density after both holes are found.");
        AssertTrue(source.Contains("BuildHeadDirectedEyePoints"), "Eyes should report head-directed expanded contour point counts.");
        AssertTrue(source.Contains("Point Offset"), "Eyes should expose the radial point offset slider.");
        AssertTrue(source.Contains("Outline Offset"), "Eyes should expose an independent red outline offset slider.");
        AssertTrue(source.Contains("Weight Curve"), "Eyes should expose the center-to-edge eyelids weight curve.");
        AssertTrue(source.Contains("GUILayout.Button(\"Apply\""), "Eyes should expose an Apply button after the eyelids bone is available.");
        AssertTrue(source.Contains("ApplyFaceEyesGeometry"), "Eyes Apply should commit preview points to selected sprite geometry.");
        AssertTrue(source.Contains("duplicated"), "Eyes should report only the upper and edge duplicate count.");
        AssertTrue(source.Contains("TryGetFaceBoneDirectionInMask"), "Eyes should derive upper and lower directions from the head bone.");

        var moduleSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Modules/ReanimatorFaceModule.cs"));
        AssertTrue(moduleSource.Contains("DrawAlphaHoleTarget"), "Eyes should draw temporary target markers over alpha holes.");
        AssertTrue(moduleSource.Contains("DrawAlphaHoleHeadDirectedPoints"), "Eyes should draw the head-directed expanded eye contour.");
        AssertTrue(moduleSource.Contains("DuplicatePoints"), "Eyes should draw only generated upper and edge duplicates.");
        AssertTrue(moduleSource.Contains("OutlineContour"), "Eyes should draw a unified closed eye outline.");
        AssertTrue(moduleSource.Contains("OutlineCollisionPoints"), "Eyes should draw the first sprite collision as a green point.");
        AssertTrue(moduleSource.Contains("TryGetFaceGeometryBoundaryInMask"), "Eyes should collide the outline with the selected sprite geometry boundary.");
        AssertTrue(moduleSource.Contains("TryGetFaceVirtualBoneSkinMatrix"), "Eyes should preview green points as fully weighted to the eyelids bone.");
        AssertTrue(moduleSource.Contains("new Color(1f, 0.12f, 0.12f, 1f)"), "Eye outline should be red.");
        AssertTrue(moduleSource.Contains("Handles.DrawAAPolyLine"), "Eyes should connect reverse contour points into closed loops.");
        AssertFalse(moduleSource.Contains("vertex") || moduleSource.Contains("triangle"), "Face hole targets should not depend on mesh geometry.");

        var automationSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Geometry/PsdLayerPreviewRenderer.FaceAlphaMask.cs"));
        AssertTrue(automationSource.Contains("TryApplyFaceEyesAutomation"), "Eyes should expose the full automatic apply stage.");
        AssertTrue(automationSource.Contains("TryFindFaceEyesAutomationLayer"), "Eyes automation should locate a face sprite without relying on selection.");
        AssertTrue(automationSource.Contains("TryCreateNamedChildBone"), "Eyes automation should create the eyelids bone when needed.");

        var allInSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Comfimator/Editor/ComfimatorPanel.cs"));
        var nipplesButtonIndex = allInSource.IndexOf("DrawNipplesButton(host);", StringComparison.Ordinal);
        var fashionableButtonIndex = allInSource.IndexOf("DrawFashionableButton(host);", StringComparison.Ordinal);
        AssertTrue(nipplesButtonIndex >= 0 && fashionableButtonIndex > nipplesButtonIndex, "Comfimator should place Nipples before Fashionable.");
        AssertTrue(allInSource.Contains("DrawNippleTarget(host.DocumentToPreview(leftNippleTargetDoc)"), "Comfimator should draw the left nipple target.");
        AssertTrue(allInSource.Contains("DrawNippleTarget(host.DocumentToPreview(rightNippleTargetDoc)"), "Comfimator should draw the right nipple target.");
        var allInIndex = allInSource.IndexOf("private async void RunAllIn", StringComparison.Ordinal);
        var preflightIndex = allInSource.IndexOf("host.TryValidateAllInPreflight", allInIndex, StringComparison.Ordinal);
        var comfyCheckIndex = allInSource.IndexOf("connectionStatus = await ComfimatorComfyClient.CheckConnectionAsync(serverUrl)", allInIndex, StringComparison.Ordinal);
        var geometryIndex = allInSource.IndexOf("var geometry = RunAutoGeometry(host)", allInIndex, StringComparison.Ordinal);
        var weightsIndex = allInSource.IndexOf("var weights = RunAutoWeights(host)", allInIndex, StringComparison.Ordinal);
        var faceEyesIndex = allInSource.IndexOf("var faceEyes = RunFaceEyes(host)", allInIndex, StringComparison.Ordinal);
        var completeIndex = allInSource.IndexOf("SetAllInProgress(\"ALLIN complete\"", allInIndex, StringComparison.Ordinal);
        var allInEndIndex = allInSource.IndexOf("private void SetAllInProgress", allInIndex, StringComparison.Ordinal);
        var allInBody = allInEndIndex > allInIndex
            ? allInSource.Substring(allInIndex, allInEndIndex - allInIndex)
            : string.Empty;
        AssertTrue(preflightIndex >= 0 && comfyCheckIndex > preflightIndex, "ALLIN must reject red naming indicators before contacting Comfy or changing any state.");
        AssertTrue(comfyCheckIndex >= 0 && geometryIndex > comfyCheckIndex, "ALLIN should verify Comfy before changing geometry.");
        AssertEqual(
            2,
            CountOccurrences(allInBody, "RunAutoGeometry(host)"),
            "ALLIN should regenerate deterministic geometry immediately before Auto Weights so async skeleton work cannot restore stale importer topology.");
        AssertTrue(weightsIndex >= 0 && faceEyesIndex > weightsIndex, "ALLIN should apply Face Eyes after Auto Weights.");
        AssertTrue(completeIndex > faceEyesIndex, "Face Eyes should be the final ALLIN stage before completion.");
        AssertFalse(allInBody.Contains("RunFullBoobsFromButton"), "ALLIN must not invoke Qwen-dependent FullBoobs automation.");
        AssertTrue(
            allInBody.Contains("useQwenNipplePositioning: false"),
            "ALLIN must keep breast controls deterministic and must not invoke Qwen nipple positioning.");

        var hostSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.ExternalPanelHost.cs"));
        AssertTrue(hostSource.Contains("faceEyesPointDensity"), "ALLIN Face Eyes should reuse the current point density.");
        AssertTrue(hostSource.Contains("faceEyesPointOffset"), "ALLIN Face Eyes should reuse the current point offset.");
        AssertTrue(hostSource.Contains("faceEyesOutlineOffset"), "ALLIN Face Eyes should reuse the current outline offset.");
        AssertTrue(hostSource.Contains("faceEyesWeightCurve"), "ALLIN Face Eyes should reuse the current weight curve.");

        var boobSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.ToolApplications.Boob.cs"));
        AssertTrue(boobSource.Contains("TryLocateNippleTargets"), "Nipples should expose automatic paired target detection.");
        AssertTrue(boobSource.Contains("TryFindBestNipplePixel"), "Nipples should refine anatomical positions from sprite pixels.");
        AssertTrue(boobSource.Contains("radialConsistency"), "Nipples should score circular pixel structure.");
        AssertTrue(
            boobSource.Contains("CompactFullBoobGeometryKeepingOutside") &&
            boobSource.Contains("DistanceToFullBoobContour") &&
            boobSource.Contains("ShouldCullFullBoobVertex") &&
            boobSource.Contains("retainedTopologyConstraints") &&
            boobSource.Contains("retainedTopologyTriangles") &&
            boobSource.Contains("IsFullBoobEdgeOutsideLocalArea") &&
            boobSource.Contains("retainedVertexCount"),
            "FullBoobs should cull only its two local regions and retain previous-tool vertices and edges outside them.");
        AssertFalse(
            boobSource.Contains("CompactFullBoobGeometryToOuterBoundary"),
            "FullBoobs must never erase the complete pre-existing body interior.");
        AssertTrue(
            boobSource.Contains("TryMapTriangulatedSubsetToSource"),
            "FullBoobs should tolerate Unity tessellation removing redundant collinear vertices.");
        AssertTrue(
            !boobSource.Contains("Failed to cull old FullBoobs geometry"),
            "FullBoobs must not expose the obsolete recoverable cull failure.");
        AssertTrue(
            !boobSource.Contains("Failed to triangulate constrained FullBoobs geometry"),
            "FullBoobs must repair recoverable tessellation state instead of exposing a failed terminal result.");
        AssertTrue(
            boobSource.Contains("ApplyBoobV2") && boobSource.Contains("TryApplyFullBoobsWeights"),
            "Boob V2 manual contours should feed the existing FullBoobs geometry and weight rebuild.");
        AssertTrue(
            boobSource.Contains("SnapBoobV2NippleBonesToTargets") &&
            boobSource.Contains("CommitCurrentBonePoseAsRest"),
            "Boob V2 Apply should snap existing nipple bones to the manual targets and commit their rest pose.");

        var boobInputSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Bones/PsdLayerPreviewRenderer.ToolInput.CreateVertexBoob.cs"));
        AssertTrue(
            boobInputSource.Contains("BuildSmoothedClosedBoobV2Contour") &&
            boobInputSource.Contains("RebuildBoobV2ContoursFromRaw") &&
            boobInputSource.Contains("BoobV2Selection"),
            "Boob V2 should use an explicit selection and rebuild saved raw strokes when contour controls change.");
        var boobV2InputStart = boobInputSource.IndexOf("private void HandleBoobV2Input", StringComparison.Ordinal);
        var boobV2InputEnd = boobInputSource.IndexOf("private void FinishBoobV2Interaction", StringComparison.Ordinal);
        AssertTrue(boobV2InputStart >= 0 && boobV2InputEnd > boobV2InputStart,
            "Boob V2 input source should be locatable.");
        var boobV2Input = boobInputSource.Substring(boobV2InputStart, boobV2InputEnd - boobV2InputStart);
        AssertFalse(
            boobV2Input.Contains("TryGetToolLockedHitLayer"),
            "Manual Boob V2 placement must not be rejected by an unrelated selected-layer lock.");

        var geometryPanelSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Layout/Reanimator.Panels.GeometryTools.cs"));
        AssertTrue(
            geometryPanelSource.Contains("new[] { \"V1\", \"V2\" }") &&
            geometryPanelSource.Contains("GUILayout.SelectionGrid") &&
            geometryPanelSource.Contains("Left Nipple") &&
            geometryPanelSource.Contains("Right Contour") &&
            geometryPanelSource.Contains("\"Density\"") &&
            geometryPanelSource.Contains("BoobV2ContourDensity") &&
            geometryPanelSource.Contains("Apply Manual FullBoobs"),
            "Boob panel should preserve V1 and expose selection plus live V2 contour density.");

        var boobOverlaySource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Reanimator/Rendering/PsdLayerPreviewRenderer.Rendering.ToolOverlays.cs"));
        AssertTrue(
            boobOverlaySource.Contains("DrawBoobV2NippleTarget") &&
            boobOverlaySource.Contains("Handles.DrawWireDisc(point, Vector3.forward, 12f)") &&
            !boobOverlaySource.Contains("DrawBoobV2Target"),
            "Boob V2 nipple points should use the full Nipples reticle instead of A/B labels.");

        var previewDrawSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.DrawLoop.cs"));
        AssertTrue(
            previewDrawSource.Contains("if (tools.Boob)") &&
            previewDrawSource.Contains("DrawBoobPreview(coordinateSpace.DocumentToView, selectedLayer"),
            "Boob V2 targets must render even when no sprite layer is selected.");

        var comfyClientSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/Comfimator/Editor/ComfimatorComfyClient.cs"));
        AssertTrue(
            comfyClientSource.Contains("BuildVisibleNippleSearchFromBreastBounds"),
            "Visible nipple refinement should search from breast bounds instead of being trapped around a bad vision point.");

        var drawLoopSource = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Plugins/Reanimator/PsdLayerPreviewRenderer.DrawLoop.cs"));
        AssertTrue(
            drawLoopSource.Contains("TryBlendDeformedLayerIntoDocument"),
            "Workspace vision capture should rasterize the currently deformed preview mesh.");
    }

    private static void ModuleRegistryKeepsModuleIdsUniqueAndUsable()
    {
        var rawModules = DiscoverConcreteReanimatorModules();
        var registryModules = ReanimatorModuleRegistry.Modules;
        var emptyIds = rawModules.Where(module => string.IsNullOrWhiteSpace(module.Id)).Select(module => module.GetType().Name).ToList();
        var emptySections = rawModules.Where(module => string.IsNullOrWhiteSpace(module.Section)).Select(module => module.GetType().Name).ToList();
        var emptyLabels = rawModules.Where(module => string.IsNullOrWhiteSpace(module.Label(null))).Select(module => module.GetType().Name).ToList();
        var duplicateIds = rawModules
            .GroupBy(module => module.Id ?? string.Empty, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key + ":" + string.Join(",", group.Select(module => module.GetType().Name)))
            .ToList();

        AssertTrue(rawModules.Count > 0, "Raw module discovery should find modules.");
        AssertTrue(emptyIds.Count == 0, "Modules with empty ids: " + string.Join(", ", emptyIds));
        AssertTrue(emptySections.Count == 0, "Modules with empty sections: " + string.Join(", ", emptySections));
        AssertTrue(emptyLabels.Count == 0, "Modules with empty labels: " + string.Join(", ", emptyLabels));
        AssertTrue(duplicateIds.Count == 0, "Duplicate module ids: " + string.Join(", ", duplicateIds));
        AssertEqual(rawModules.Count, registryModules.Count, "Registry should not hide duplicate module ids by grouping.");
    }

    private static void BoobModuleSecondPressCancelsPreview()
    {
        var window = UnityEditor.EditorWindow.CreateInstance<Reanimator>();
        try
        {
            var renderer = new PsdLayerPreviewRenderer();
            var undoLabels = new List<string>();
            renderer.UndoRequest = undoLabels.Add;
            SetPrivateField(window, "layerPreviewRenderer", renderer);

            var module = new ReanimatorBoobModule();
            SetPrivateField(window, "bottomToolMode", ReanimatorToolMode.PreviewPose);
            module.Invoke(window.Api);
            AssertEqual(ReanimatorToolMode.Boob, window.Api.ActiveToolMode, "First Boob press should activate the tool.");

            SetPrivateField(renderer, "boobHasPoint", true);
            SetPrivateField(renderer, "boobLayerStableKey", "l_boob");
            module.Invoke(window.Api);

            AssertEqual(ReanimatorToolMode.NoSettings, window.Api.ActiveToolMode, "Second Boob press should leave the active tool.");
            AssertFalse(renderer.HasBoobPoint(), "Second Boob press should remove the preview wireframe point.");
            AssertEqual(string.Empty, GetPrivateField<string>(renderer, "boobLayerStableKey"), "Second Boob press should clear the preview layer.");
            AssertArray(new[] { "Cancel Boob Tool" }, undoLabels, "Second Boob press should register one cancel operation.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void ModuleRegistrySortsModulesByStableOrder()
    {
        var modules = ReanimatorModuleRegistry.Modules;
        for (var i = 1; i < modules.Count; i++)
        {
            var previous = modules[i - 1];
            var current = modules[i];
            AssertTrue(
                CompareModuleOrder(previous, current) <= 0,
                $"Module order should be stable between {previous.Id} and {current.Id}.");
        }
    }

    private static void ToolModesHaveDebugModuleCoverage()
    {
        var uncovered = new List<string>();
        var modules = ReanimatorModuleRegistry.Modules;
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (!TryGetToolMode(module, out var mode))
                continue;
            if (mode == ReanimatorToolMode.NoSettings || mode == ReanimatorToolMode.DeprecatedCreateVertex)
                continue;
            if (module is IReanimatorDebugPreviewModule)
                continue;

            uncovered.Add($"{module.GetType().Name}:{mode}");
        }

        AssertTrue(uncovered.Count == 0, "Missing debug coverage: " + string.Join(", ", uncovered));
    }

    private static void DebugPreviewModulesAreIsolatedInDebugFiles()
    {
        var offenders = new List<string>();
        var moduleType = typeof(IReanimatorDebugPreviewModule);
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(type => type != null && !type.IsAbstract && !type.IsInterface && moduleType.IsAssignableFrom(type));

        foreach (var type in types)
        {
            var fileName = type.Name + ".Debug.cs";
            if (type == typeof(ReanimatorAutoGeometryModule) ||
                type == typeof(ReanimatorEditGeometryModule) ||
                type == typeof(ReanimatorCreateEdgeModule) ||
                type == typeof(ReanimatorSplitEdgeModule) ||
                type == typeof(ReanimatorElbowModule) ||
                type == typeof(ReanimatorLimbsModule) ||
                type == typeof(ReanimatorJointModule) ||
                type == typeof(ReanimatorBoobModule))
            {
                fileName = "ReanimatorGeometryModules.Debug.cs";
            }
            else if (type == typeof(ReanimatorCurveModule) ||
                     type == typeof(ReanimatorSliderModule) ||
                     type == typeof(ReanimatorBoneInfluenceModule) ||
                     type == typeof(ReanimatorSpriteInfluenceModule))
            {
                fileName = "ReanimatorWeightModules.Debug.cs";
            }
            else if (type == typeof(ReanimatorPreviewPoseModule))
            {
                fileName = "ReanimatorPoseModules.Debug.cs";
            }
            else if (type == typeof(ReanimatorCircleCleanModule))
            {
                fileName = "ReanimatorUtilityModules.Debug.cs";
            }

            var path = Path.Combine(Application.dataPath, "Plugins", "Reanimator", "Modules");
            var exists = Directory.GetFiles(path, fileName, SearchOption.AllDirectories).Length > 0;
            if (!exists)
                offenders.Add(type.Name);
        }

        AssertTrue(offenders.Count == 0, "Debug modules without matching *.Debug.cs file: " + string.Join(", ", offenders));
    }

    private static bool TryGetToolMode(IReanimatorModule module, out ReanimatorToolMode mode)
    {
        mode = ReanimatorToolMode.NoSettings;
        if (module == null)
            return false;

        var field = typeof(ReanimatorToolModeModule).GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null && module is ReanimatorToolModeModule)
        {
            mode = (ReanimatorToolMode)field.GetValue(module);
            return true;
        }

        if (module is ReanimatorProjectorModule)
        {
            mode = ReanimatorToolMode.Projector;
            return true;
        }
        if (module is ReanimatorVertexCullModule)
        {
            mode = ReanimatorToolMode.VertexCull;
            return true;
        }
        if (module is ReanimatorVolumeCurveModule)
        {
            mode = ReanimatorToolMode.VolumeCurve;
            return true;
        }

        return false;
    }

    private static List<IReanimatorModule> DiscoverConcreteReanimatorModules()
    {
        var moduleType = typeof(IReanimatorModule);
        var modules = new List<IReanimatorModule>();
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(type => type != null &&
                           !type.IsAbstract &&
                           !type.IsInterface &&
                           moduleType.IsAssignableFrom(type) &&
                           type.GetConstructor(Type.EmptyTypes) != null);

        foreach (var type in types)
        {
            try
            {
                if (Activator.CreateInstance(type) is IReanimatorModule module)
                    modules.Add(module);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create module {type.FullName}: {ex.Message}", ex);
            }
        }

        return modules;
    }

    private static int CompareModuleOrder(IReanimatorModule lhs, IReanimatorModule rhs)
    {
        var sectionCompare = lhs.SectionOrder.CompareTo(rhs.SectionOrder);
        if (sectionCompare != 0)
            return sectionCompare;

        var buttonCompare = lhs.ButtonOrder.CompareTo(rhs.ButtonOrder);
        if (buttonCompare != 0)
            return buttonCompare;

        return string.Compare(lhs.Id, rhs.Id, StringComparison.Ordinal);
    }
}
#endif
