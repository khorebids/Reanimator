#if UNITY_EDITOR
using System;
using UnityEngine;

public static class ReaniIKHiddenBoneChecks
{
    public static string Run()
    {
        var root=new GameObject("Hidden bone rig fixture");root.SetActive(false);
        try
        {
            var upper=Bone(root.transform,"upper");var joint=Bone(upper,"joint");
            var helper=Bone(upper,"joint_helper");
            var elbow=helper.gameObject.AddComponent<ElbowJointRig2D>();elbow.Configure(joint,false,.4f);
            var rig=root.AddComponent<ReaniIK>();rig.breathing.enabled=false;
            joint.localRotation=Quaternion.Euler(0,0,-90);
            rig.Evaluate(0);
            Require(helper.localPosition.magnitude>.01f && Quaternion.Angle(helper.localRotation,Quaternion.identity)>1,"Hidden elbow autopose stopped.");
            elbow.enabled=false;rig.Evaluate(0);
            Require(helper.localPosition.sqrMagnitude<1e-8f && Quaternion.Angle(helper.localRotation,Quaternion.identity)<.01f,"Disabling hidden elbow did not restore its pose.");
            var shoulders=Bone(root.transform,"shoulders");var arm=Bone(shoulders,"arm");
            arm.localPosition=Vector3.right;
            var attachment=Bone(shoulders,"shoulder_helper");
            var driver=attachment.gameObject.AddComponent<ShoulderAttachmentRig2D>();driver.Configure(arm,shoulders,.4f,270,false);
            rig.RefreshBoneRigDrivers();arm.localRotation=Quaternion.Euler(0,0,150);rig.Evaluate(0);
            Require(attachment.localPosition.magnitude>.01f,"Hidden shoulder autopose stopped.");
            var pose=attachment.localPosition;rig.Evaluate(0);
            Require(Vector3.Distance(pose,attachment.localPosition)<.0001f,"Repeated hidden autopose accumulated offsets.");
            driver.enabled=false;rig.Evaluate(0);
            Require(attachment.localPosition.sqrMagnitude<1e-8f,"Disabling hidden shoulder did not restore its pose.");
            return "PASS: inactive elbow and shoulder rigs remain driven by ReaniIK, restore when disabled and do not accumulate offsets.";
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    private static Transform Bone(Transform parent,string name)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.gameObject.SetActive(false);return t;}
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
