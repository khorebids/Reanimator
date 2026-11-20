#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifySoftContourWeightComposition()
    {
        var original = new BoneWeight { boneIndex0 = 0, weight0 = .4f, boneIndex1 = 2, weight1 = .35f, boneIndex2 = 3, weight2 = .25f };
        foreach (float fraction in new[] { 0f, .5f, 1f })
        {
            var result = RedistributeSoftContourWeight(original, 0, 1, fraction);
            Require(GetSpriteBoneWeight(result, 2), .35f, "Elbow helper A was overwritten");
            Require(GetSpriteBoneWeight(result, 3), .25f, "Elbow helper B was overwritten");
            Require(GetSpriteBoneWeight(result, 1), .4f * fraction, "SC must receive only the source share");
            var repeated = RedistributeSoftContourWeight(result, 0, 1, fraction);
            Require(GetSpriteBoneWeight(repeated, 1), .4f * fraction, "Repeated SC accumulated weights");
        }
        var saturated = new BoneWeight { boneIndex0 = 0, weight0 = .4f, boneIndex1 = 2, weight1 = .3f, boneIndex2 = 3, weight2 = .2f, boneIndex3 = 4, weight3 = .1f };
        var preserved = RedistributeSoftContourWeight(saturated, 0, 1, .5f);
        Require(GetSpriteBoneWeight(preserved, 4), .1f, "Endpoint smoothing discarded an existing channel");
        return "PASS: source-only SC transfer, two joint helpers preserved, support and endpoint shares, four-channel saturation and repeat stability.";

        static void Require(float actual, float expected, string message)
        { if (Mathf.Abs(actual - expected) > .00001f) throw new Exception(message); }
    }

    // Run on copies of the loaded character. This exercises the actual SC stage
    // against joint-weighted contour vertices without changing the PSD or recipes.
    public string VerifySoftContourOnCurrentJoints()
    {
        int segments = 0, checkedVertices = 0, checkedPoses = 0;
        float maximumDrift = 0;
        foreach (string name in new[] { "l_arm", "r_arm", "l_leg", "r_leg" })
        {
            var original = vertexDataByLayerSelectionKey.Values.First(d => d.layer?.name == name);
            foreach (string sourceName in new[] { name, name + "_1" })
            {
                var scratch = new PsdLayerPreviewRenderer();
                scratch.bones = CloneBones(bones);
                scratch.bonesTreatAsWorldSpace = bonesTreatAsWorldSpace;
                scratch.rotateLocalBoneOffsetsByParent = rotateLocalBoneOffsetsByParent;
                foreach (var bone in scratch.bones) { bone.position = bone.initialPosition; bone.rotation = bone.initialRotation; }
                scratch.RecomputeBoneWorldTransforms();
                var data = CloneProjectorLayerData(original);
                int parent = scratch.bones.FindIndex(b => b.name == sourceName);
                int source = FindSpriteBoneIndexForRendererBone(data, parent);
                if (source < 0) throw new Exception("Missing source " + sourceName);
                var pairs = new List<(Vector2 contour, Vector2 support, bool soften)>();
                var boundary = new HashSet<int>(data.boundaryEdgePairs);
                foreach (int vertex in boundary)
                {
                    if (GetSpriteBoneWeight(data.boneWeights[vertex], source) <= .001f) continue;
                    bool hasHelper = false;
                    for (int slot = 0; slot < data.spriteBoneToRendererBone.Length; slot++)
                        if (data.spriteBoneToRendererBone[slot] >= 0 && scratch.bones[data.spriteBoneToRendererBone[slot]].name.Contains("_elbow_") &&
                            GetSpriteBoneWeight(data.boneWeights[vertex], slot) > .001f) hasHelper = true;
                    if (!hasHelper) continue;
                    for (int t = 0; t < data.triangles.Length; t += 3)
                    {
                        int a = data.triangles[t], b = data.triangles[t + 1], c = data.triangles[t + 2];
                        if (a != vertex && b != vertex && c != vertex) continue;
                        var support = (data.vertices[a] + data.vertices[b] + data.vertices[c]) / 3f;
                        if ((support - data.vertices[vertex]).sqrMagnitude < .0001f) continue;
                        pairs.Add((data.vertices[vertex], support, pairs.Count % 2 == 0));
                        break;
                    }
                }
                if (pairs.Count == 0) continue;
                var beforeData = CloneProjectorLayerData(data);
                var before = (BoneWeight[])data.boneWeights.Clone();
                if (!scratch.ApplySoftContourPairs(data, parent, pairs, out var error)) throw new Exception(sourceName + ": " + error);
                foreach (int vertex in boundary)
                    for (int slot = 0; slot < original.spriteBoneToRendererBone.Length; slot++)
                    {
                        if (original.spriteBoneToRendererBone[slot] < 0 || !scratch.bones[original.spriteBoneToRendererBone[slot]].name.Contains("_elbow_")) continue;
                        if (Mathf.Abs(GetSpriteBoneWeight(before[vertex], slot) - GetSpriteBoneWeight(data.boneWeights[vertex], slot)) > .0001f)
                            throw new Exception(sourceName + ": SC changed a joint helper at vertex " + vertex);
                        checkedVertices++;
                    }
                int joint = scratch.bones.FindIndex(b => b.name == name + "_1");
                foreach (float angle in new[] { -120f, -90f, 0f, 90f, 120f })
                {
                    scratch.bones[joint].rotation = scratch.bones[joint].initialRotation * Quaternion.Euler(0, 0, angle);
                    foreach (var bone in scratch.bones)
                        if (bone.name.StartsWith(name + "_1_elbow_", StringComparison.Ordinal))
                            bone.rotation = bone.initialRotation * Quaternion.Euler(0, 0, angle * .5f);
                    scratch.RecomputeBoneWorldTransforms();
                    scratch.InvalidateBonePose();
                    var expected = scratch.GetLayerVerticesForDraw(beforeData);
                    var actual = scratch.GetLayerVerticesForDraw(data);
                    for (int vertex = 0; vertex < before.Length; vertex++)
                    {
                        float drift = Vector2.Distance(expected[vertex], actual[vertex]);
                        maximumDrift = Mathf.Max(maximumDrift, drift);
                        if (drift > .05f) throw new Exception(sourceName + ": resting SC changes bent surface at " + angle + " degrees, vertex " + vertex + ": " + drift + "px");
                    }
                    checkedPoses++;
                }
                segments++;
            }
        }
        if (segments < 4) throw new Exception("Insufficient joint coverage: " + segments);
        return "PASS: " + segments + " real arm/leg segments, " + checkedVertices + " boundary helper weights preserved; " + checkedPoses + " bent poses, maximum drift " + maximumDrift + "px on isolated mesh copies.";
    }
}
#endif
