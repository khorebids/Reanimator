#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

public static class ReanimatorArmDeformationChecks
{
    public static string Run(string path)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
            root.SetActive(false);var rig=root.GetComponent<ReaniIK>();
            var transforms=root.GetComponentsInChildren<Transform>(true);
            var positions=transforms.Select(t=>t.localPosition).ToArray();
            var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
            float worst=0;int checkedEdges=0;
            foreach(float bend in new[]{-60f,-30f,0f,30f,60f})
            {
                foreach(var driver in root.GetComponentsInChildren<ElbowJointRig2D>(true))driver.RestorePose();
                foreach(var driver in root.GetComponentsInChildren<ShoulderAttachmentRig2D>(true))driver.RestorePose();
                for(int i=0;i<transforms.Length;i++){transforms[i].SetLocalPositionAndRotation(positions[i],rotations[i]);transforms[i].localScale=scales[i];}
                if(rig.leftArm.lower)rig.leftArm.lower.localRotation*=Quaternion.Euler(0,0,bend);
                if(rig.rightArm.lower)rig.rightArm.lower.localRotation*=Quaternion.Euler(0,0,-bend);
                foreach(var driver in root.GetComponentsInChildren<ElbowJointRig2D>(true))driver.Evaluate();
                foreach(var driver in root.GetComponentsInChildren<ShoulderAttachmentRig2D>(true))driver.Evaluate();
                foreach(var skin in root.GetComponentsInChildren<SpriteSkin>(true).Where(s=>s.name=="l_arm"||s.name=="r_arm"))
                {
                    var sprite=skin.GetComponent<SpriteRenderer>().sprite;
                    var points=sprite.vertices;var weights=sprite.GetVertexAttribute<BoneWeight>(UnityEngine.Rendering.VertexAttribute.BlendWeight).ToArray();
                    var bind=sprite.GetBindPoses().ToArray();var bones=skin.boneTransforms;
                    var posed=new Vector3[points.Length];
                    for(int i=0;i<points.Length;i++)
                    {
                        var w=weights[i];Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                        void Add(int slot,float amount){if(amount>0)posed[i]+=bones[slot].TransformPoint(bind[slot].MultiplyPoint3x4(points[i]))*amount;}
                        if(!float.IsFinite(posed[i].x)||!float.IsFinite(posed[i].y))throw new Exception("Non-finite skinned arm vertex.");
                    }
                    var triangles=sprite.triangles;
                    for(int i=0;i<triangles.Length;i+=3)for(int e=0;e<3;e++)
                    {
                        int a=triangles[i+e],b=triangles[i+(e+1)%3];
                        float rest=skin.transform.TransformVector(points[a]-points[b]).magnitude;
                        if(rest<.01f)continue;
                        float ratio=Vector3.Distance(posed[a],posed[b])/rest;worst=Mathf.Max(worst,ratio);checkedEdges++;
                        if(ratio>8)throw new Exception($"{skin.name} / bend {bend}: edge {a}/{b}, {bones[weights[a].boneIndex0].name}/{bones[weights[b].boneIndex0].name}, stretch {ratio:F2} exceeds spike threshold.");
                    }
                }
            }
            return $"PASS: five elbow poses, {checkedEdges} skinned arm edges; maximum stretch {worst:F2}; no non-finite vertices or extreme spikes.";
        }
        finally{if(root)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
