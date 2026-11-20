#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public static class ReaniIKButtChecks
{
    public static string Run()
    {
        CheckAvailability();
        int cases=0;
        foreach(float mirror in new[]{1f,-1f})
        {
            var root=new GameObject("Butt controls fixture");root.SetActive(false);
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(2,-1,0),Quaternion.Euler(0,0,23));
                root.transform.localScale=new Vector3(mirror*1.4f,.7f,1);
                var hips=Bone(root.transform,"hips",Vector3.zero);
                foreach(var name in new[]{"r_leg","l_leg"})
                {
                    var leg=Bone(hips,name,new Vector3(name=="l_leg"?-1:1,-.5f,0));
                    leg.localRotation=Quaternion.Euler(0,0,-38);
                    var a=Bone(leg,ReaniIKButtControls.ParallelBoneName(name),Vector3.zero);
                    var b=Bone(leg,ReaniIKButtControls.PerpendicularBoneName(name),Vector3.zero);
                    a.localRotation=Quaternion.Euler(0,0,180);b.localRotation=Quaternion.Euler(0,0,90);
                    Bone(a,a.name+"_SC",Vector3.zero);Bone(b,b.name+"_SC",Vector3.zero);
                    // Duplicate renderer names must not replace bone bindings.
                    Bone(leg,a.name,Vector3.zero).gameObject.AddComponent<SpriteRenderer>();
                }
                Bone(hips,"unpaired_booty",Vector3.zero);
                var pairs=ReaniIKButtControls.FindPairs(root.transform);
                Require(pairs.Count==2,"Both manual helper pairs must auto-detect, with no renderer or orphan pair.");
                Require(pairs[0].leg.name=="l_leg" && pairs[1].leg.name=="r_leg","Left control must precede right regardless of hierarchy order.");
                foreach(var pair in pairs)
                {
                    var legPosition=pair.leg.localPosition;var legRotation=pair.leg.localRotation;
                    var a=pair.parallel;var b=pair.perpendicular;var pa=a.position;var pb=b.position;
                    var axisA=a.TransformVector(Vector3.right).normalized;var axisB=b.TransformVector(Vector3.right).normalized;
                    ReaniIKButtControls.MoveSquare(pair,new Vector2(0,.35f));
                    Require(Vector3.Distance(a.position,pa+axisA*.35f)<.0001f && Vector3.Distance(b.position,pb)<.0001f,"Vertical movement must affect only the parallel helper along its axis.");
                    pa=a.position;
                    ReaniIKButtControls.MoveSquare(pair,new Vector2(-.22f,0));
                    Require(Vector3.Distance(b.position,pb-axisB*.22f)<.0001f && Vector3.Distance(a.position,pa)<.0001f,"Horizontal movement must affect only the perpendicular helper along its axis.");
                    Require(Vector2.Distance(ReaniIKButtControls.SquareOffset(pair),new Vector2(-.22f,.35f))<.0001f,"Square does not reflect the helper displacements.");
                    pa=a.position;pb=b.position;var ra=a.rotation;var rb=b.rotation;
                    var delta=Quaternion.AngleAxis(37,root.transform.forward);
                    ReaniIKButtControls.RotateBoth(pair,delta);
                    Require(Quaternion.Angle(a.rotation,delta*ra)<.001f && Quaternion.Angle(b.rotation,delta*rb)<.001f,"Circle must rotate both helpers equally.");
                    Require(Vector3.Distance(a.position,pa)<.0001f && Vector3.Distance(b.position,pb)<.0001f,"Rotating helpers must not move their origins.");
                    Require(pair.leg.localPosition==legPosition && Quaternion.Angle(pair.leg.localRotation,legRotation)<.001f && hips.localPosition==Vector3.zero,"Butt control modified leg or hips.");
                    ReaniIKButtControls.MoveSquare(pair,Vector2.zero);ReaniIKButtControls.RotateBoth(pair,Quaternion.identity);
                    Require(Vector3.Distance(a.position,pa)<.0001f && Vector3.Distance(b.position,pb)<.0001f,"Idle control drifted.");
                    var ca=a.Find(a.name+"_SC");var cb=b.Find(b.name+"_SC");
                    var springs=new ReaniIK.SpringGroup{bones=new[]{ca,cb},frequency=3.2f,damping=.24f,inertia=.65f,maximumOffset=.3f};
                    springs.Simulate(1f/60);springs.RestorePose();
                    ReaniIKButtControls.MoveSquare(pair,new Vector2(.1f,.15f));
                    pa=a.position;pb=b.position;
                    springs.Simulate(1f/60);
                    Require(ca.localPosition.sqrMagnitude>1e-8f && cb.localPosition.sqrMagnitude>1e-8f,"SC children did not spring behind their controlled parents.");
                    Require(Vector3.Distance(a.position,pa)<.0001f && Vector3.Distance(b.position,pb)<.0001f,"Spring simulation modified the controlled parents.");
                    springs.Reset();
                    Require(ca.localPosition.sqrMagnitude<1e-8f && cb.localPosition.sqrMagnitude<1e-8f,"Spring reset did not restore identical child origins.");
                    VerifyWorldDrag(pair,root.transform);
                    cases++;
                }
                // Pairing is by direct parent as well as by name, not a global name lookup.
                var orphan=Bone(root.transform,"other_leg",Vector3.zero);
                Bone(orphan,ReaniIKButtControls.ParallelBoneName("other_leg"),Vector3.zero);
                Bone(hips,ReaniIKButtControls.PerpendicularBoneName("other_leg"),Vector3.zero);
                Require(ReaniIKButtControls.FindPairs(root.transform).Count==2,"Unrelated helper parents were paired.");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        return "PASS: "+cases+" Butt pairs, current-axis world dragging at eight rotations, horizontal/vertical role swaps, mirrored nonuniform scales, paired rotation, SC springs and no thumb jump.";
    }

    private static void VerifyWorldDrag(ReaniIKButtControls.Pair pair,Transform root)
    {
        var a=pair.parallel;var b=pair.perpendicular;
        pair.leg.localRotation=Quaternion.identity;
        foreach(float angle in new[]{0f,30f,45f,89f,90f,135f,180f,270f})
        {
            foreach(var drag in new[]{root.right*.2f,root.up*.3f,root.right*.2f-root.up*.15f})
            {
                a.localPosition=b.localPosition=Vector3.zero;
                a.localRotation=Quaternion.Euler(0,0,angle);b.localRotation=Quaternion.Euler(0,0,angle+90);
                var pa=a.position;var pb=b.position;
                var axisA=a.TransformVector(Vector3.right).normalized;var axisB=b.TransformVector(Vector3.right).normalized;
                ReaniIKButtControls.MoveSquareWorld(pair,drag);
                var da=a.position-pa;var db=b.position-pb;
                Require(Vector3.Distance(da+db,drag)<.0001f,"Current-axis movement did not follow the drag direction and distance.");
                Require(Vector3.Cross(da,axisA).magnitude<.0001f && Vector3.Cross(db,axisB).magnitude<.0001f,"A helper moved sideways across its current axis.");
                Require(Vector3.Distance(ReaniIKButtControls.SquareWorldOffset(pair),drag)<.0001f,"Square thumb did not follow the drag.");
                if(Vector3.Distance(drag,root.right*.2f)<.0001f)
                {
                    if(angle==0 || angle==180)Require(db.magnitude<.0001f,"Horizontal drag did not choose the currently horizontal parallel bone.");
                    if(angle==90 || angle==270)Require(da.magnitude<.0001f,"Horizontal drag did not switch to the currently horizontal perpendicular bone.");
                }
                var thumb=ReaniIKButtControls.SquareWorldOffset(pair);
                ReaniIKButtControls.RotateBoth(pair,Quaternion.AngleAxis(17,root.forward));
                Require(Vector3.Distance(thumb,ReaniIKButtControls.SquareWorldOffset(pair))<.0001f,"Circle rotation made the square jump.");
            }
        }
        a.localPosition=b.localPosition=Vector3.zero;a.localRotation=b.localRotation=Quaternion.identity;
        var pa0=a.position;var pb0=b.position;var along=a.TransformVector(Vector3.right).normalized*.2f;
        ReaniIKButtControls.MoveSquareWorld(pair,along);
        Require(Vector3.Distance((a.position-pa0)+(b.position-pb0),along)<.0001f,"Coincident helper axes amplified the drag.");
    }

    private static void CheckAvailability()
    {
        for(int mask=0;mask<4;mask++)
        {
            var root=new GameObject("Butt availability fixture");root.SetActive(false);
            try
            {
                // Right first in the hierarchy deliberately tests stable ordering.
                foreach(var side in new[]{1,0})
                {
                    string name=side==0?"l_leg":"r_leg";
                    var leg=Bone(root.transform,name,Vector3.zero);
                    if((mask&(1<<side))==0)continue;
                    Bone(leg,ReaniIKButtControls.ParallelBoneName(name),Vector3.zero);
                    Bone(leg,ReaniIKButtControls.PerpendicularBoneName(name),Vector3.zero);
                }
                var pairs=ReaniIKButtControls.FindPairs(root.transform);
                Require(pairs.Count==((mask&1)!=0?1:0)+((mask&2)!=0?1:0),"Unavailable Side Ass reserved an empty control slot.");
                if(mask==1)Require(pairs[0].leg.name=="l_leg","Left-only control must occupy the first slot.");
                if(mask==2)Require(pairs[0].leg.name=="r_leg","Right-only control must occupy the first slot.");
                if(mask==3)Require(pairs[0].leg.name=="l_leg" && pairs[1].leg.name=="r_leg","Both controls must be ordered left, right.");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }

    private static Transform Bone(Transform parent,string name,Vector3 position)
    {
        var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
