#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public static class ReaniIKArmChecks
{
    public static string Run()
    {
        int cases = 0;
        foreach (bool elbow in new[] { false, true })
            foreach (bool hand in new[] { false, true })
            {
                var root = new GameObject("Arm IK Fixture");
                root.SetActive(false);
                try
                {
                    var hips = Bone(root.transform, "hips", Vector3.zero);
                    var bones = new List<Transform> { hips };
                    foreach (var side in new[] { "l", "r" })
                    {
                        var upper = Bone(hips, side + "_arm", new Vector3(side == "l" ? -1f : 1f, 0f));
                        bones.Add(upper);
                        var last = upper;
                        if (elbow)
                        {
                            last = Bone(last, side + "_arm_1", Vector3.down);
                            bones.Add(last);
                        }
                        if (hand)
                        {
                            last = Bone(last, side + "_hand", Vector3.down);
                            bones.Add(last);
                        }
                    }
                    var controls = Bone(root.transform, "rigs", Vector3.zero);
                    var rig = root.AddComponent<ReaniIK>();
                    Require(ReaniIKArmSetup.Configure(rig, bones, controls) == 2, "Both arms must configure.");
                    foreach (var limb in new[] { rig.leftArm, rig.rightArm })
                    {
                        int expected = 1 + (elbow ? 1 : 0) + (hand ? 1 : 0);
                        Require(limb.IsValid && limb.BoneCount == expected, "Wrong chain length.");
                        string lastName = hand ? "_hand" : elbow ? "_arm_1" : "_arm";
                        Require(limb.tip.name.EndsWith(lastName), "Control is not on the last existing bone.");
                        Require(limb.upper != hips && limb.lower != hips && limb.tip != hips, "Hips entered the arm chain.");
                        var position = limb.upper.localPosition;
                        var rotation = limb.upper.localRotation;
                        var tipRotation = limb.tip.localRotation;
                        var targetPosition = limb.target.position;
                        limb.target.rotation = Quaternion.Euler(0f, 0f, 37f);
                        Vector3 desired = limb.BoneCount == 1 ? limb.tip.position + new Vector3(0.3f, 0.2f) :
                            limb.upper.position + new Vector3(0.6f, elbow && hand ? -1.2f : -0.8f);
                        limb.target.position = desired;
                        limb.Solve(1f);
                        if (expected == 1 || expected == 3)
                            Require(Vector3.Distance(limb.tip.position, desired) < 0.001f, "End bone missed a reachable target.");
                        else
                        {
                            var actual = limb.tip.position - limb.upper.position;
                            Require(Vector3.Angle(actual, desired - limb.upper.position) < 0.01f, "Two-bone arm failed to aim.");
                            Require(Mathf.Abs(actual.magnitude - 1f) < 0.001f, "Two-bone arm stretched.");
                        }
                        Require(Quaternion.Angle(limb.tip.rotation, limb.target.rotation) < 0.01f, "Last-bone rotation did not follow the control.");
                        if (expected == 3)
                        {
                            var bend = limb.lower.position;
                            limb.RestorePose();
                            limb.flip = !limb.flip;
                            limb.Solve(1f);
                            Require(Vector3.Distance(bend, limb.lower.position) > 0.1f, "Flip did not reverse the elbow.");
                        }
                        limb.RestorePose();
                        Require(Vector3.Distance(limb.upper.localPosition, position) < 0.001f &&
                            Quaternion.Angle(limb.upper.localRotation, rotation) < 0.01f &&
                            Quaternion.Angle(limb.tip.localRotation, tipRotation) < 0.01f, "Pose was not restored.");
                        Require(hips.localPosition == Vector3.zero && hips.localRotation == Quaternion.identity, "Solver moved the hips.");
                        var target = limb.target;
                        Require(ReaniIKArmSetup.Configure(rig, bones, controls) == 2 && limb.target == target &&
                            limb.target.position == desired && controls.childCount == 2, "Repeated setup reset or duplicated a control.");
                        limb.target.SetPositionAndRotation(targetPosition, Quaternion.identity);
                        cases++;
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        return "PASS: " + cases + " arm variants (both sides, with/without elbow and hand), last-bone binding, aim, rotation, full-chain flip, pose restoration, hips isolation and repeated setup.";
    }

    private static Transform Bone(Transform parent, string name, Vector3 position)
    {
        var bone = new GameObject(name).transform;
        bone.SetParent(parent, false);
        bone.localPosition = position;
        return bone;
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
#endif

