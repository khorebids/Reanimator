#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

internal static partial class ReanimatorSelfTests
{
    [Serializable] private sealed class OccludedShoulderFixture
    {
        public System.Collections.Generic.List<PsdLayerPreviewRenderer.BoneInfo> bones;
        public Vector2Int size;
        public string mesh;
        public Vector2[] contour;
    }

    private static void FailedShoulderResamplingDoesNotAbortAutomation()
    {
        var fixture = JsonUtility.FromJson<OccludedShoulderFixture>(System.IO.File.ReadAllText(
            "Assets/Plugins/Reanimator/Tests/Fixtures/FailedShoulderResampling.json"));
        var renderer = new PsdLayerPreviewRenderer();
        string asset = UnityEditor.AssetDatabase.GenerateUniqueAssetPath("Assets/__ShoulderResamplingTest.asset");
        UnityEditor.AssetDatabase.CreateAsset(new Texture2D(1, 1), asset);
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(asset);
        try
        {
            SetPrivateField(renderer, "cachedPath", asset);
            SetPrivateField(renderer, "cachedDocumentSize", fixture.size);
            SetPrivateField(renderer, "bones", fixture.bones);
            var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
            JsonUtility.FromJsonOverwrite(fixture.mesh, data);
            var layer = GetPrivateField<PsdLayerPreviewRenderer.LayerInfo>(data, "layer");
            GetPrivateField<System.Collections.Generic.List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos").Add(layer);
            string key = (string)typeof(PsdLayerPreviewRenderer).GetMethod("BuildSelectionKey",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { layer });
            var cache = GetPrivateField<System.Collections.IDictionary>(renderer, "vertexDataByLayerSelectionKey");
            cache[key] = data;
            renderer.SetShoulderContour("r_arm", fixture.contour);
            string mesh = JsonUtility.ToJson(data);
            string bones = string.Join("|", fixture.bones.Select(bone => JsonUtility.ToJson(bone)));
            for (int repeat = 0; repeat < 2; repeat++)
            {
                string result = renderer.ApplyAllShoulderContours();
                AssertTrue(result.Contains("r_arm: shoulder omitted") && result.Contains("resampled shoulder contour"),
                    "The recorded resampling failure must be omitted instead of aborting automation: " + result);
                AssertEqual(mesh, JsonUtility.ToJson(cache[key]), "Failed resampling must preserve the complete arm mesh.");
                AssertEqual(bones, string.Join("|", GetPrivateField<System.Collections.Generic.List<PsdLayerPreviewRenderer.BoneInfo>>(renderer, "bones").Select(bone => JsonUtility.ToJson(bone))),
                    "Failed resampling must preserve the existing skeleton and preceding shoulder helper.");
            }
        }
        finally
        {
            UnityEditor.AssetDatabase.DeleteAsset(asset);
            foreach (string directory in new[] { "ShoulderContours", "PreviewBoneRigs" })
            {
                string path = System.IO.Path.Combine("Library", "Reanimator", directory, guid + ".json");
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
    }

    private static void OccludedShouldersAreOmittedWithoutChangingArms()
    {
        var fixture = JsonUtility.FromJson<OccludedShoulderFixture>(System.IO.File.ReadAllText(
            "Assets/Plugins/Reanimator/Tests/Fixtures/OccludedShoulder.json"));
        var renderer = new PsdLayerPreviewRenderer();
        var texture = new Texture2D(1, 1);
        string asset = UnityEditor.AssetDatabase.GenerateUniqueAssetPath("Assets/__ShoulderOcclusionTest.asset");
        UnityEditor.AssetDatabase.CreateAsset(texture, asset);
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(asset);
        try
        {
            SetPrivateField(renderer, "cachedPath", asset);
            SetPrivateField(renderer, "cachedDocumentSize", fixture.size);
            SetPrivateField(renderer, "bones", fixture.bones);
            var layers = GetPrivateField<System.Collections.Generic.List<PsdLayerPreviewRenderer.LayerInfo>>(renderer, "layerInfos");
            var cache = GetPrivateField<System.Collections.IDictionary>(renderer, "vertexDataByLayerSelectionKey");
            var snapshots = new System.Collections.Generic.List<(object data, string json)>();
            foreach (string name in new[] { "l_arm", "r_arm" })
            {
                var data = CreatePrivateNestedInstance(renderer, "LayerVertexData");
                JsonUtility.FromJsonOverwrite(fixture.mesh, data);
                var layer = GetPrivateField<PsdLayerPreviewRenderer.LayerInfo>(data, "layer");
                layer.name = name; layer.visible = true;
                layers.Add(layer);
                string key = (string)typeof(PsdLayerPreviewRenderer).GetMethod("BuildSelectionKey",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { layer });
                cache[key] = data;
                // Use the recorded occluded right-arm base for both sides, so
                // continuing past the first omission is exercised as well.
                var bone = fixture.bones.First(item => item.name == name);
                var right = fixture.bones.First(item => item.name == "r_arm");
                bone.worldPosition = right.worldPosition; bone.worldRotation = right.worldRotation;
                renderer.SetShoulderContour(name, new[] { Vector2.zero, Vector2.right, Vector2.up });
                snapshots.Add((data, JsonUtility.ToJson(data)));
            }
            for (int repeat = 0; repeat < 2; repeat++)
            {
                string result = renderer.ApplyAllShoulderContours();
                AssertTrue(result.Contains("l_arm: shoulder omitted") && result.Contains("r_arm: shoulder omitted"),
                    "Both occluded shoulders must be omitted independently without aborting automation.");
                foreach (var snapshot in snapshots)
                    AssertEqual(snapshot.json, JsonUtility.ToJson(snapshot.data), "An omitted shoulder must preserve mesh and weights exactly.");
            }
        }
        finally
        {
            UnityEditor.AssetDatabase.DeleteAsset(asset);
            string path = System.IO.Path.Combine("Library", "Reanimator", "ShoulderContours", guid + ".json");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private static void ShoulderDefaultsAndReanimateNavigationPersist()
    {
        var renderer = new PsdLayerPreviewRenderer();
        string profile = System.IO.File.ReadAllText("ProjectSettings/ReanimatorShoulderSettings.json");
        var type = typeof(PsdLayerPreviewRenderer).GetNestedType("ShoulderDefaults", System.Reflection.BindingFlags.NonPublic);
        var saved = JsonUtility.FromJson(profile, type);
        AssertEqual(JsonUtility.ToJson(type.GetField("transition").GetValue(saved)), JsonUtility.ToJson(renderer.ShoulderTransitionCurve), "Fresh renderers must load the saved transition curve including tangents.");
        AssertEqual(JsonUtility.ToJson(type.GetField("helper").GetValue(saved)), JsonUtility.ToJson(renderer.ShoulderContourHelperCurve), "Fresh renderers must load the saved helper curve including tangents.");
        var window = ScriptableObject.CreateInstance<Reanimator>();
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var step = typeof(Reanimator).GetField("reanimateStep", flags);
            var side = typeof(Reanimator).GetField("reanimateShoulderSide", flags);
            var navigate = typeof(Reanimator).GetMethod("NavigateReanimateStep", flags);
            step.SetValue(window, 0);
            navigate.Invoke(window, new object[] { 1 });
            AssertEqual(1, (int)step.GetValue(window), "Shoulder outline step must immediately follow Full PSD.");
            AssertEqual(0, (int)side.GetValue(window), "Shoulder step must focus the left arm first.");
            navigate.Invoke(window, new object[] { 1 });
            AssertEqual(1, (int)step.GetValue(window), "Second arm must remain in the shoulder step.");
            AssertEqual(1, (int)side.GetValue(window), "Next must focus the right arm.");
            navigate.Invoke(window, new object[] { 1 });
            AssertEqual(2, (int)step.GetValue(window), "Face must follow both shoulder contours.");
            navigate.Invoke(window, new object[] { -1 });
            AssertEqual(1, (int)side.GetValue(window), "Back from Face must return to the right arm.");
            navigate.Invoke(window, new object[] { -1 });
            AssertEqual(0, (int)side.GetValue(window), "Back again must focus the left arm.");
            navigate.Invoke(window, new object[] { -1 });
            AssertEqual(0, (int)step.GetValue(window), "Back from the left arm must return to Full PSD.");
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    }

    private static void RemovingExperimentalShoulderHelperKeepsTheOriginal()
    {
        var renderer = new PsdLayerPreviewRenderer();
        SetPrivateField(renderer, "bones", new System.Collections.Generic.List<PsdLayerPreviewRenderer.BoneInfo>
        {
            BuildVisibleWorldBone(0, "root", -1, Vector2.zero),
            BuildVisibleWorldBone(1, "shoulders", 0, Vector2.up),
            BuildVisibleWorldBone(2, "l_arm", 1, Vector2.right),
            BuildVisibleWorldBone(3, "l_arm_shoulder_helper", 1, Vector2.right),
            BuildVisibleWorldBone(4, "l_arm_shoulder_helper_opposite", 1, Vector2.right)
        });
        SetPrivateField(renderer, "selectedBoneIndex", 2);
        renderer.GetPreviewBoneRig(3).enabled = true;
        renderer.GetPreviewBoneRig(4).enabled = true;
        AssertEqual(1, renderer.RemoveOppositeShoulderHelpers(), "The experimental helper must be deleted.");
        AssertEqual(4, renderer.Bones.Count, "Only the experimental helper may be removed.");
        AssertEqual("l_arm_shoulder_helper", renderer.Bones[3].name, "Original helper must remain.");
        AssertTrue(renderer.IsBoneAutomated(3), "Original helper automation must remain.");
        AssertEqual(2, renderer.SelectedBoneIndex, "Selected arm must remain selected.");
        AssertEqual(0, renderer.RemoveOppositeShoulderHelpers(), "Repeated cleanup must be harmless.");
    }







    private static void ShoulderSmoothingIncludesSecondNeighborOnly()
    {
        var points = new[] { Vector2.zero, new Vector2(2, 0), new Vector2(6, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        var loop = new[] { 0, 1, 2, 3, 4, 5 };
        var track = new PsdLayerPreviewRenderer.ShoulderBoundaryTrack(points, loop);
        var weights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 4, weight0 = 1 }, points.Length).ToArray();
        weights[0] = new BoneWeight { boneIndex0 = 3, weight0 = 1 };
        var changed = new bool[weights.Length];
        PsdLayerPreviewRenderer.SmoothShoulderContourNeighbors(points, loop, track,
            new[] { 0f }, new[] { 1 }, weights, changed, neighborCount: 2);
        AssertFloat(.8f, GetWeight(weights[1], 3), .0001f, "First neighbor must interpolate toward the third outside point.");
        AssertFloat(.4f, GetWeight(weights[2], 3), .0001f, "Second neighbor must also be smoothed by arc length.");
        AssertEqual(2, changed.Count(value => value), "Exactly two outside points should change.");
        AssertFloat(1f, GetWeight(weights[0], 3), .0001f, "Endpoint must stay fixed.");
        AssertFloat(1f, GetWeight(weights[3], 4), .0001f, "Third outside point must stay unchanged.");
    }

    private static void ShoulderNeighborSmoothingRunsBeforeAndAfterHelperOverlay()
    {
        var points = new[] { Vector2.zero, new Vector2(2, 0), new Vector2(6, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        var loop = new[] { 0, 1, 2, 3, 4, 5 };
        var track = new PsdLayerPreviewRenderer.ShoulderBoundaryTrack(points, loop);
        var weights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 4, weight0 = 1 }, points.Length).ToArray();
        weights[0] = new BoneWeight { boneIndex0 = 3, weight0 = 1 };
        weights[1] = new BoneWeight { boneIndex0 = 5, weight0 = 1 };
        var original = (BoneWeight[])weights.Clone();
        var changed = new bool[weights.Length];
        PsdLayerPreviewRenderer.SmoothShoulderContourNeighbors(points, loop, track, new[] { 0f }, new[] { 1 }, weights, changed);
        AssertFloat(2f / 3f, GetWeight(weights[1], 3), .0001f, "First neighbor must be interpolated by contour distance before the helper overlay.");
        AssertFloat(1f / 3f, GetWeight(weights[1], 4), .0001f, "Unequal edge lengths must be respected.");
        AssertFloat(0f, GetWeight(weights[1], 5), .0001f, "First smoothing must remove the neighbor's stale helper influence.");
        AssertEqual(1, changed.Count(value => value), "Only the immediate contour neighbor should be smoothed.");
        var reversed = (BoneWeight[])original.Clone();
        PsdLayerPreviewRenderer.SmoothShoulderContourNeighbors(points, loop.Reverse().ToArray(), track,
            new[] { 0f }, new[] { 1 }, reversed, new bool[weights.Length]);
        AssertFloat(GetWeight(weights[1], 3), GetWeight(reversed[1], 3), .0001f, "Reversed contour winding must choose the same physical neighbor.");
        weights[1] = new BoneWeight { boneIndex0 = 3, weight0 = 1f / 3f, boneIndex1 = 4, weight1 = 1f / 6f, boneIndex2 = 5, weight2 = .5f };
        PsdLayerPreviewRenderer.SmoothShoulderContourNeighbors(points, loop, track, new[] { 2f }, new[] { 1 }, weights, changed);
        AssertFloat(.25f, GetWeight(weights[2], 5), .0001f, "Second smoothing must propagate the completed helper overlay beyond its end.");
        AssertFloat(1f / 6f, GetWeight(weights[2], 3), .0001f, "Second smoothing must retain the shoulder contribution.");
        AssertFloat(7f / 12f, GetWeight(weights[2], 4), .0001f, "Second smoothing must retain normalized arm contribution.");
        AssertFloat(1f, GetWeight(weights[0], 3), .0001f, "The locked endpoint must remain unchanged.");
        AssertFloat(1f, GetWeight(weights[3], 4), .0001f, "The farther neighbor must remain unchanged.");
    }

    private static void ShoulderTransitionCurveKeepsLockedEndpointsAndHelperOverlay()
    {
        var curve = AnimationCurve.Linear(0, .2f, 1, .2f);
        AssertFloat(0, PsdLayerPreviewRenderer.EvaluateShoulderTransition(0, curve), .0001f, "Attachment endpoint must remain fully locked to shoulders.");
        AssertFloat(1, PsdLayerPreviewRenderer.EvaluateShoulderTransition(1, curve), .0001f, "Released endpoint must remain fully assigned to the arm.");
        float arm = PsdLayerPreviewRenderer.EvaluateShoulderTransition(.5f, curve);
        AssertFloat(.2f, arm, .0001f, "Interior transition must follow the editable curve.");
        var weight = PsdLayerPreviewRenderer.EvaluateShoulderContourWeights(arm, .5f, AnimationCurve.Linear(0, 1, 1, 0), 3, 4, 5);
        AssertFloat(.4f, GetWeight(weight, 3), .0001f, "Helper must preserve the curved shoulder share.");
        AssertFloat(.1f, GetWeight(weight, 4), .0001f, "Helper must preserve the curved arm share.");
        AssertFloat(.5f, GetWeight(weight, 5), .0001f, "Helper curve must remain independent of the transition curve.");
    }

    private static void ShoulderRetreatKeepsShouldersOrientationAndDoublesContourRange()
    {
        AssertFloat(2f, PsdLayerPreviewRenderer.ShoulderSeamLengthInWidths, .0001f, "Weld contour traversal must be doubled to 2a.");
        AssertFloat(1f, PsdLayerPreviewRenderer.ShoulderHelperReachInWidths, .0001f, "Helper falloff must be doubled to a in both directions.");
        var shoulders = new GameObject("ShouldersRetreatFixture"); shoulders.SetActive(false);
        try
        {
            var arm = new GameObject("arm").transform; arm.SetParent(shoulders.transform, false);
            var helper = new GameObject("helper").transform; helper.SetParent(shoulders.transform, false);
            arm.localRotation = Quaternion.Euler(0, 0, 60);
            helper.localRotation = Quaternion.Euler(0, 0, 210);
            var direction = helper.rotation;
            var rig = helper.gameObject.AddComponent<ShoulderAttachmentRig2D>();
            rig.Configure(arm, shoulders.transform, 1, 300, false);
            arm.localRotation = Quaternion.Euler(0, 0, 120); rig.Evaluate();
            AssertFloat(.5f, helper.localPosition.magnitude, .001f, "Major-angle closure must still drive retreat.");
            AssertQuaternion(direction, helper.rotation, .001f, "Turning the arm must not rotate the helper.");
            arm.localRotation = Quaternion.Euler(0, 0, 30); rig.Evaluate();
            AssertVector(Vector2.zero, helper.localPosition, .001f, "Opening the angle must return the helper.");
            AssertQuaternion(direction, helper.rotation, .001f, "Return must not rotate the helper.");
        }
        finally { UnityEngine.Object.DestroyImmediate(shoulders); }
    }

    private static void ShoulderHelperOverlaysLinearTwoBoneBase()
    {
        var curve = AnimationCurve.Linear(0f, .8f, 1f, .2f);
        foreach (float progress in new[] { 0f, .25f, .5f, 1f })
            foreach (float time in new[] { 0f, .5f, 1f })
            {
                var weight = PsdLayerPreviewRenderer.EvaluateShoulderContourWeights(progress, time, curve, 3, 4, 5);
                float h = curve.Evaluate(time);
                AssertFloat(h, GetWeight(weight, 5), .0001f, "Helper curve must control the overlay, including both curve endpoints.");
                AssertFloat((1f - progress) * (1f - h), GetWeight(weight, 3), .0001f, "Shoulder share must preserve the linear base under the helper.");
                AssertFloat(progress * (1f - h), GetWeight(weight, 4), .0001f, "Arm share must preserve the linear base under the helper.");
                AssertFloat(1f, weight.weight0 + weight.weight1 + weight.weight2, .0001f, "Three weights must remain normalized.");
            }
        var baseOnly = PsdLayerPreviewRenderer.EvaluateShoulderContourWeights(.25f, 0f, null, 3, 4, 5);
        AssertFloat(.75f, GetWeight(baseOnly, 3), .0001f, "First stage is a linear shoulder-to-arm interpolation.");
        AssertFloat(.25f, GetWeight(baseOnly, 4), .0001f, "First stage must not inherit any previous helper weight.");
        AssertFloat(0f, GetWeight(baseOnly, 5), .0001f, "First stage contains only the two source bones.");
    }

    private static void ShoulderAttachmentInheritsArmAndRetractsOnMajorAngleClosure()
    {
        var points = new[] { Vector2.zero, new Vector2(100, 0), new Vector2(100, 200), new Vector2(0, 200) };
        var track = new PsdLayerPreviewRenderer.ShoulderBoundaryTrack(points, new[] { 0, 1, 2, 3 });
        for (int i = 0; i < 5; i++)
            AssertVector(new Vector2(100, 25f * i), track.At(100 + 100f * i / 4f), .001f,
                "Five seam points must be equally spaced along the contour, including both endpoints.");
        AssertFloat(0, ShoulderAttachmentRig2D.EvaluateRetreat(60, 300), .001f, "Rest major angle must not move the helper.");
        AssertFloat(.5f, ShoulderAttachmentRig2D.EvaluateRetreat(120, 300), .001f, "Half closure must retreat halfway.");
        AssertFloat(1, ShoulderAttachmentRig2D.EvaluateRetreat(180, 300), .001f, "Complete closure must use the full retreat.");
        AssertFloat(0, ShoulderAttachmentRig2D.EvaluateRetreat(30, 300), .001f, "Opening must return the helper to its attachment.");
        var root = new GameObject("AttachedShoulderFixture"); root.SetActive(false);
        try
        {
            var arm = new GameObject("arm").transform; arm.SetParent(root.transform, false);
            var helper = new GameObject("helper").transform; helper.SetParent(arm, false);
            arm.localRotation = Quaternion.Euler(0, 0, 60);
            helper.localRotation = Quaternion.Euler(0, 0, 150);
            var restRotation = helper.localRotation;
            var rig = helper.gameObject.AddComponent<ShoulderAttachmentRig2D>();
            rig.Configure(arm, root.transform, 1, 300, false);
            arm.localRotation = Quaternion.Euler(0, 0, 120); rig.Evaluate();
            AssertFloat(.5f, helper.localPosition.magnitude, .001f, "Helper retreat must follow major-angle closure.");
            AssertQuaternion(restRotation, helper.localRotation, .001f, "Helper must inherit the arm's rotation without bisector autorotation.");
            arm.localRotation = Quaternion.Euler(0, 0, 30); rig.Evaluate();
            AssertVector(Vector2.zero, helper.localPosition, .001f, "Opening must return to the base.");
            rig.RestorePose();
            AssertVector(Vector2.zero, helper.localPosition, .001f, "Disabling the helper must restore its attachment.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ShoulderReferenceIsPerpendicularAndScaleIndependent()
    {
        var source = new[] { new Vector2(-20f, -10f), new Vector2(20f, -10f),
            new Vector2(20f, 120f), new Vector2(-20f, 120f) };
        var edges = new[] { 0, 1, 1, 2, 2, 3, 3, 0 };
        foreach (float scale in new[] { .25f, 1f, 3f, 10f })
            foreach (float angle in new[] { 0f, 37f, 135f })
            {
                var rotation = Quaternion.Euler(0f, 0f, angle);
                var origin = new Vector2(150f, -70f);
                var direction = (Vector2)(rotation * Vector3.up);
                var vertices = Array.ConvertAll(source, point => origin + (Vector2)(rotation * (Vector3)(point * scale)));
                AssertTrue(PsdLayerPreviewRenderer.TryGetShoulderReferenceWidth(vertices, edges, origin, direction,
                    out var a, out var b), "Both contours must be intersected at every scale and rotation.");
                float width = Vector2.Distance(a, b);
                AssertFloat(40f * scale, width, .005f, "Reference must be the full arm cross-section.");
                AssertFloat(0f, Vector2.Dot((a - b).normalized, direction), .001f, "Reference must be perpendicular to the first arm bone.");
                AssertFloat(0f, Vector2.Dot(a - origin, direction), .005f, "Reference must pass through the arm bone base.");
                AssertFloat(20f * scale, width * .5f, .005f, "Relative shift must scale with the arm, without a pixel constant.");
            }
    }

    private static void ShoulderContourUsesThreeLinearStepsAndFullPixelRetreat()
    {
        var vertices = new[] { Vector2.zero, new Vector2(0f, 100f), new Vector2(30f, 100f), new Vector2(30f, 0f) };
        AssertTrue(PsdLayerPreviewRenderer.TrySampleElbowContour(vertices, new[] { 0, 1, 2, 3 },
            new Vector2(0f, 10f), Vector2.up, 60f, out var samples, sampleCount: 4, linear: true),
            "Shoulder contour sampling must succeed.");
        AssertEqual(4, samples.Length, "Attachment plus exactly three support samples are required.");
        for (int i = 0; i < samples.Length; i++)
            AssertVector(new Vector2(0f, 10f + 20f * i), samples[i], .001f, "Contour support spacing must be linear.");
        AssertFloat(0f, ElbowJointRig2D.EvaluateShoulderBend(150f, 150f, false), .001f, "Rest pose must not retreat.");
        AssertFloat(.5f, ElbowJointRig2D.EvaluateShoulderBend(120f, 150f, false), .001f, "Half bend must use half the retreat.");
        AssertFloat(60f, 60f * ElbowJointRig2D.EvaluateShoulderBend(90f, 150f, false), .001f, "Full bend must use all 60 pixels.");
        var root = new GameObject("ShoulderRetreatFixture");
        root.SetActive(false);
        try
        {
            var arm = new GameObject("arm").transform;
            var helper = new GameObject("helper").transform;
            arm.SetParent(root.transform, false);
            helper.SetParent(root.transform, false);
            arm.localRotation = Quaternion.Euler(0f, 0f, 60f);
            helper.localRotation = Quaternion.Euler(0f, 0f, 210f);
            var rig = helper.gameObject.AddComponent<ElbowJointRig2D>();
            rig.ConfigureShoulder(arm, false, 60f, 150f);
            rig.Evaluate();
            AssertFloat(0f, helper.localPosition.magnitude, .001f, "Runtime shoulder must stay attached at rest.");
            arm.localRotation = Quaternion.Euler(0f, 0f, 180f);
            rig.Evaluate();
            AssertFloat(60f, helper.localPosition.magnitude, .01f, "Runtime shoulder must retreat the full configured distance.");
            rig.RestorePose();
            AssertFloat(0f, helper.localPosition.magnitude, .001f, "Runtime shoulder must restore its attachment.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    public static string VerifyRepeatedShoulderFan(PsdLayerPreviewRenderer renderer)
    {
        AssertTrue(renderer.ApplyShoulderFan(out var error), "Initial shoulder apply failed: " + error);
        AssertTrue(renderer.TryGetSelectedLayer(out var layer), "Shoulder sprite is not selected.");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var args = new object[] { layer, null };
        AssertTrue((bool)typeof(PsdLayerPreviewRenderer).GetMethod("TryGetLayerVertexData", flags).Invoke(renderer, args), "Shoulder mesh is missing.");
        var signature = typeof(PsdLayerPreviewRenderer).GetMethod("ShoulderFanSignature", flags);
        var before = (string)signature.Invoke(renderer, new[] { args[1] });
        for (int repeat = 0; repeat < 3; repeat++)
        {
            AssertTrue(renderer.ApplyShoulderFan(out error), "Repeated shoulder apply failed: " + error);
            AssertEqual(before, (string)signature.Invoke(renderer, new[] { args[1] }),
                "Repeated apply must preserve vertices, UVs, topology, weights and attachment.");
        }
        return "Shoulder applied; three repeated runs preserved the complete mesh and weights.";
    }

    public static string VerifyShoulderContourRuntime(PsdLayerPreviewRenderer renderer)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var args = new object[] { null, null };
        AssertTrue((bool)typeof(PsdLayerPreviewRenderer).GetMethod("TryBuildShoulderContour", flags).Invoke(renderer, args), "Shoulder contour plan must remain valid.");
        var plan = args[0];
        int armIndex = GetPrivateField<int>(plan, "arm");
        var helper = renderer.Bones.First(bone => bone.name == renderer.Bones[armIndex].name + "_shoulder_helper");
        AssertEqual(GetPrivateField<int>(plan, "shoulders"), helper.parentId, "Helper must be a child of shoulders.");
        AssertTrue(helper.depth < renderer.Bones[armIndex].depth, "Helper order must be lower than arm order.");
        var data = GetPrivateField<object>(plan, "data");
        var draw = (Vector2[])typeof(PsdLayerPreviewRenderer).GetMethod("GetLayerVerticesForDraw", flags).Invoke(renderer, new[] { data });
        var point = GetPrivateField<Vector2>(plan, "helperPoint");
        int closest = Enumerable.Range(0, draw.Length).OrderBy(i => (draw[i] - point).sqrMagnitude).First();
        AssertFloat(0f, Vector2.Distance(draw[closest], point), .02f, "Helper intersection must be an explicit contour vertex.");
        int slot = Array.IndexOf(GetPrivateField<int[]>(data, "spriteBoneToRendererBone"), helper.index);
        AssertFloat(Mathf.Clamp01(renderer.ShoulderContourHelperCurve.Evaluate(0f)),
            GetWeight(GetPrivateField<BoneWeight[]>(data, "boneWeights")[closest], slot), .001f,
            "Helper intersection must honor the user's curve value at time zero.");
        AssertTrue(renderer.IsBoneAutomated(helper.index), "Attached helper must receive the automated-bone outline.");
        var root = new GameObject("ShoulderCookRuntimeFixture"); root.SetActive(false);
        try
        {
            var transforms = renderer.Bones.Select(bone => new GameObject(bone.name).transform).ToArray();
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].SetParent(root.transform, false);
                transforms[i].SetPositionAndRotation((Vector3)renderer.Bones[i].worldPosition * .01f, renderer.Bones[i].worldRotation);
            }
            for (int i = 0; i < transforms.Length; i++)
                if (renderer.Bones[i].parentId >= 0) transforms[i].SetParent(transforms[renderer.Bones[i].parentId], true);
            renderer.ConfigureAutomaticElbowRuntime(transforms);
            var runtime = transforms[helper.index].GetComponent<ShoulderAttachmentRig2D>();
            AssertTrue(runtime, "Cooked helper needs the attached shoulder runtime component.");
            var rest = runtime.transform.localPosition;
            var rotation = runtime.transform.localRotation;
            runtime.Evaluate();
            AssertVector(rest, runtime.transform.localPosition, .001f, "Cooked helper must start at the attachment.");
            var arm = transforms[armIndex]; var shoulders = arm.parent;
            var original = arm.localRotation;
            arm.localRotation = original * Quaternion.Euler(0, 0, 20);
            float plus = Vector2.Angle(shoulders.right, arm.right);
            arm.localRotation = original * Quaternion.Euler(0, 0, -20);
            float minus = Vector2.Angle(shoulders.right, arm.right);
            arm.localRotation = original * Quaternion.Euler(0, 0, plus >= minus ? 20 : -20);
            runtime.Evaluate();
            AssertTrue(Vector3.Distance(rest, runtime.transform.localPosition) > .001f, "Closing the major angle must move the cooked helper.");
            AssertQuaternion(rotation, runtime.transform.localRotation, .001f, "Cooked helper must retain its arm-local rotation.");
            arm.localRotation = original; runtime.Evaluate();
            AssertVector(rest, runtime.transform.localPosition, .001f, "Cooked helper must return when the angle opens.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return "Helper is shoulders-parented, its contour intersection honors the helper curve, turquoise marker is enabled, cooked runtime translates and returns correctly.";
    }
}
#endif
