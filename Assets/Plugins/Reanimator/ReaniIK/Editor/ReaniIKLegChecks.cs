#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public static class ReaniIKLegChecks
{
    public static string VerifyPrefabAuthoredPose(string path)
    {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=(GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
            root.SetActive(false);var rig=root.GetComponent<ReaniIK>();int count=0;
            rig.SynchronizeAnimationControls();
            foreach(var limb in new[]{rig.leftArm,rig.rightArm,rig.leftArm2,rig.rightArm2,rig.leftLeg,rig.rightLeg})
            {
                if(!limb.IsValid || !limb.lower)continue;
                var knee=limb.lower.position;var tip=limb.tip.position;
                for(int pass=0;pass<3;pass++)
                {
                    limb.Solve(1);
                    Require(Vector3.Distance(limb.lower.position,knee)<.001f && Vector3.Distance(limb.tip.position,tip)<.001f,
                        "First IK evaluation reversed authored bend: "+limb.upper.name);
                }
                count++;
            }
            return "PASS: "+count+" prefab limbs retain authored joints/targets across three initial IK evaluations after save/reload.";
        }
        finally{if(root)UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
    }
    public static string VerifyAuthoredBends()
    {
        int cases=0;
        foreach(float bend in new[]{-150f,-126f,-30f,30f,126f,150f})
        foreach(string side in new[]{"l","r"})
        {
            var root=new GameObject("Bent leg fixture");root.SetActive(false);
            try
            {
                var upper=Bone(root.transform,side+"_leg",Vector3.zero);
                upper.localRotation=Quaternion.Euler(0,0,42);
                var lower=Bone(upper,side+"_leg_1",Vector3.right*2);
                lower.localRotation=Quaternion.Euler(0,0,bend);
                var tip=Bone(lower,side+"_foot",Vector3.right*2.3f);
                var rig=root.AddComponent<ReaniIK>();
                var controls=Bone(root.transform,"rigs",Vector3.zero);
                var bones=new[]{upper,lower,tip};
                Vector3 knee=lower.position,foot=tip.position;
                ReaniIKLegSetup.Configure(rig,bones,controls);
                var limb=side=="l"?rig.leftLeg:rig.rightLeg;
                for(int pass=0;pass<3;pass++)
                {
                    limb.Solve(1);
                    Require(Vector3.Distance(lower.position,knee)<.001f && Vector3.Distance(tip.position,foot)<.001f,
                        "IK changed authored bend on initial/repeated evaluation: "+side+" "+bend);
                }
                bool flip=limb.flip;limb.flip=!flip;
                ReaniIKLegSetup.Configure(rig,bones,controls);
                Require(limb.flip==!flip,"Repeated setup erased manual Flip.");
                cases++;
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        return "PASS: "+cases+" authored bent legs retain knee/foot positions on first and repeated solves; manual Flip survives setup.";
    }
    public static string Run()
    {
        int cases = 0;
        foreach (bool knee in new[] { false, true })
            foreach (bool foot in new[] { false, true })
            {
                var root = new GameObject("Leg IK Fixture");
                root.SetActive(false);
                try
                {
                    var hips = Bone(root.transform, "hips", Vector3.zero);
                    var bones = new List<Transform> { hips };
                    foreach (var side in new[] { "l", "r" })
                    {
                        var upper = Bone(hips, side + "_leg", new Vector3(side == "l" ? -1f : 1f, 0f));
                        bones.Add(upper);
                        var last = upper;
                        if (knee)
                        {
                            last = Bone(last, side + "_leg_1", Vector3.down);
                            bones.Add(last);
                        }
                        if (foot)
                        {
                            last = Bone(last, side + "_foot", Vector3.down);
                            bones.Add(last);
                        }
                    }
                    var controls = Bone(root.transform, "rigs", Vector3.zero);
                    var rig = root.AddComponent<ReaniIK>();
                    Require(ReaniIKLegSetup.Configure(rig, bones, controls) == 2, "Both legs must configure.");
                    foreach (var limb in new[] { rig.leftLeg, rig.rightLeg })
                    {
                        int expected = 1 + (knee ? 1 : 0) + (foot ? 1 : 0);
                        Require(limb.IsValid && limb.BoneCount == expected, "Wrong chain length.");
                        string lastName = foot ? "_foot" : knee ? "_leg_1" : "_leg";
                        Require(limb.tip.name.EndsWith(lastName), "Control is not on the last existing bone.");
                        Require(limb.upper != hips && limb.lower != hips && limb.tip != hips, "Hips entered the leg chain.");
                        var position = limb.upper.localPosition;
                        var rotation = limb.upper.localRotation;
                        var tipRotation = limb.tip.localRotation;
                        var targetPosition = limb.target.position;
                        limb.target.rotation = Quaternion.Euler(0f, 0f, 37f);
                        Vector3 desired = limb.BoneCount == 1 ? limb.tip.position + new Vector3(0.3f, 0.2f) :
                            limb.upper.position + new Vector3(0.6f, knee && foot ? -1.2f : -0.8f);
                        limb.target.position = desired;
                        limb.Solve(1f);
                        if (expected == 1 || expected == 3)
                            Require(Vector3.Distance(limb.tip.position, desired) < 0.001f, "End bone missed a reachable target.");
                        else
                        {
                            var actual = limb.tip.position - limb.upper.position;
                            Require(Vector3.Angle(actual, desired - limb.upper.position) < 0.01f, "Two-bone leg failed to aim.");
                            Require(Mathf.Abs(actual.magnitude - 1f) < 0.001f, "Two-bone leg stretched.");
                        }
                        Require(Quaternion.Angle(limb.tip.rotation, limb.target.rotation) < 0.01f, "Last-bone rotation did not follow the control.");
                        if (expected == 3)
                        {
                            var bend = limb.lower.position;
                            limb.RestorePose();
                            limb.flip = !limb.flip;
                            limb.Solve(1f);
                            Require(Vector3.Distance(bend, limb.lower.position) > 0.1f, "Flip did not reverse the knee.");
                        }
                        limb.RestorePose();
                        Require(Vector3.Distance(limb.upper.localPosition, position) < 0.001f &&
                            Quaternion.Angle(limb.upper.localRotation, rotation) < 0.01f &&
                            Quaternion.Angle(limb.tip.localRotation, tipRotation) < 0.01f, "Pose was not restored.");
                        Require(hips.localPosition == Vector3.zero && hips.localRotation == Quaternion.identity, "Solver moved the hips.");
                        var target = limb.target;
                        Require(ReaniIKLegSetup.Configure(rig, bones, controls) == 2 && limb.target == target &&
                            limb.target.position == desired && controls.childCount == 2, "Repeated setup reset or duplicated a control.");
                        limb.target.SetPositionAndRotation(targetPosition, Quaternion.identity);
                        cases++;
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        return "PASS: " + cases + " leg variants (both sides, with/without knee and foot), last-bone binding, aim, rotation, full-chain flip, pose restoration, hips isolation and repeated setup.";
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
