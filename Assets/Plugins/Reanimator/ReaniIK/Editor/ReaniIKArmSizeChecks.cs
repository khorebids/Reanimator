#if UNITY_EDITOR
using System;
using UnityEngine;

public static class ReaniIKArmSizeChecks
{
    public static string Run()
    {
        var root=new GameObject("Arm size regression");root.SetActive(false);
        try
        {
            var rig=root.AddComponent<ReaniIK>();rig.enabled=false;
            root.transform.localScale=new Vector3(-1.4f,.8f,1);
            root.transform.localRotation=Quaternion.Euler(0,0,31);
            var belly=Bone(root.transform,"belly",Vector3.zero);
            var spine=Bone(belly,"spine",Vector3.up);
            Bone(Bone(spine,"neck",Vector3.up),"head",Vector3.up);
            var left=Bone(spine,"l_arm",Vector3.left*.4f);var right=Bone(spine,"r_arm",Vector3.right*.4f);
            var leftLower=Bone(left,"l_forearm",Vector3.right*.3f);var leftHand=Bone(leftLower,"l_hand",Vector3.right*.2f);
            var rightLower=Bone(right,"r_forearm",Vector3.right*.3f);var rightHand=Bone(rightLower,"r_hand",Vector3.right*.2f);
            rig.leftArm.upper=rig.leftArm2.upper=left;rig.rightArm.upper=rig.rightArm2.upper=right;
            rig.leftArm.lower=rig.leftArm2.lower=leftLower;rig.leftArm.tip=rig.leftArm2.tip=leftHand;
            rig.rightArm.lower=rig.rightArm2.lower=rightLower;rig.rightArm.tip=rig.rightArm2.tip=rightHand;
            rig.bodyScale.distribution=AnimationCurve.Linear(0,1,1,1);
            float leftBase=Vector3.Distance(left.position,leftHand.position),rightBase=Vector3.Distance(right.position,rightHand.position);
            int cases=0;
            foreach(float body in new[]{.8f,1f,1.2f})
            foreach(float compensation in new[]{0f,.5f,1f})
            foreach(float arm in new[]{.8f,1.2f})
            {
                rig.bodyScale.size=body;rig.bodyScale.back_arms=compensation;
                rig.bodyScale.leftArmSize=arm;rig.bodyScale.rightArmSize=2-arm;
                for(int repeat=0;repeat<20;repeat++)rig.bodyScale.Apply(rig);
                float inherited=body*body;
                float factor=inherited*Mathf.Lerp(1,1/inherited,compensation);
                float delta=arm-1;
                float first=1+delta,second=1+delta*1.4f,last=1+delta*1.96f;
                Check(Mathf.Abs(Vector3.Distance(left.position,leftHand.position)/leftBase-factor*(.6f*first+.4f*second))<.0001f,"Left arm / body compensation mismatch.");
                Check(Mathf.Abs(Vector3.Distance(right.position,rightHand.position)/rightBase-factor*(1-delta*1.16f))<.0001f,"Right arm independence / compensation mismatch.");
                Check(Vector3.Distance(leftLower.localScale,Vector3.one*(second/first))<.00001f,"Forearm did not receive 40% stronger scale change.");
                Check(Vector3.Distance(leftHand.localScale,Vector3.one*(last/second))<.00001f,"Hand did not receive another 40% stronger scale change.");
                rig.bodyScale.Restore();
                Check(left.localScale==Vector3.one&&right.localScale==Vector3.one,"Arm size did not restore authored scale.");
                foreach(var child in new[]{leftLower,leftHand,rightLower,rightHand})Check(child.localScale==Vector3.one,"Segment scale did not restore.");
                cases++;
            }
            return $"PASS: {cases} arm/body combinations, compensation 0/0.5/1, independent sides, full descendant chains, duplicate bindings, mirrored nonuniform rotated root and 20 repeats without drift.";
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    private static Transform Bone(Transform parent,string name,Vector3 position)
    {var bone=new GameObject(name).transform;bone.SetParent(parent,false);bone.localPosition=position;return bone;}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
