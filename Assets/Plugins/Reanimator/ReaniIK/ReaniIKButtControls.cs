using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Named manual Side Ass helpers. No simulation or automatic posing is attached.</summary>
public static class ReaniIKButtControls
{
    public static string ParallelBoneName(string leg) => leg + "_booty";
    public static string PerpendicularBoneName(string leg) => leg + "_booty_press";

    public sealed class Pair
    {
        public readonly Transform leg, parallel, perpendicular;
        public bool IsValid => leg && parallel && perpendicular && parallel.parent == leg && perpendicular.parent == leg;
        public Pair(Transform leg, Transform parallel, Transform perpendicular)
        {this.leg=leg;this.parallel=parallel;this.perpendicular=perpendicular;}
    }

    public static List<Pair> FindPairs(Transform root)
    {
        var result=new List<Pair>();
        if(!root)return result;
        foreach(var leg in root.GetComponentsInChildren<Transform>(true))
        {
            if(leg.GetComponent<SpriteRenderer>())continue;
            Transform parallel=null,perpendicular=null;
            foreach(Transform child in leg)
            {
                if(child.GetComponent<SpriteRenderer>())continue;
                if(string.Equals(child.name,ParallelBoneName(leg.name),StringComparison.OrdinalIgnoreCase))parallel=child;
                if(string.Equals(child.name,PerpendicularBoneName(leg.name),StringComparison.OrdinalIgnoreCase))perpendicular=child;
            }
            if(parallel && perpendicular)result.Add(new Pair(leg,parallel,perpendicular));
        }
        return result;
    }

    private static Vector3 Axis(Transform bone)
    {
        var axis=bone.TransformVector(Vector3.right);
        return axis.sqrMagnitude>1e-12f?axis.normalized:bone.right;
    }

    public static Vector2 SquareOffset(Pair pair)
    {
        if(pair==null || !pair.IsValid)return Vector2.zero;
        return new Vector2(Vector3.Dot(pair.perpendicular.position-pair.leg.position,Axis(pair.perpendicular)),
            Vector3.Dot(pair.parallel.position-pair.leg.position,Axis(pair.parallel)));
    }

    public static void MoveSquare(Pair pair,Vector2 delta)
    {
        if(pair==null || !pair.IsValid)return;
        pair.parallel.position+=Axis(pair.parallel)*delta.y;
        pair.perpendicular.position+=Axis(pair.perpendicular)*delta.x;
    }

    public static Vector3 SquareWorldOffset(Pair pair)
    {
        if(pair==null || !pair.IsValid)return Vector3.zero;
        // Preserve the thumb position when the circle changes only bone rotations.
        return (pair.parallel.position-pair.leg.position)+(pair.perpendicular.position-pair.leg.position);
    }

    public static void MoveSquareWorld(Pair pair,Vector3 delta)
    {
        if(pair==null || !pair.IsValid)return;
        var a=Axis(pair.parallel);var b=Axis(pair.perpendicular);
        float overlap=Vector3.Dot(a,b),determinant=1-overlap*overlap;
        float alongA=Vector3.Dot(delta,a),alongB=Vector3.Dot(delta,b);
        if(determinant>1e-4f)
        {
            // Resolve the drag in the bones' current axes. The inverse Gram
            // matrix also accounts for axes skewed by a nonuniform parent scale.
            float amountA=(alongA-overlap*alongB)/determinant;
            float amountB=(alongB-overlap*alongA)/determinant;
            pair.parallel.position+=a*amountA;
            pair.perpendicular.position+=b*amountB;
        }
        else
        {
            // Nearly parallel axes cannot represent an arbitrary 2D drag.
            // Share their supported motion without amplifying it or switching roles.
            pair.parallel.position+=a*(alongA*.5f);
            pair.perpendicular.position+=b*(alongB*.5f);
        }
    }

    public static void RotateBoth(Pair pair,Quaternion delta)
    {
        if(pair==null || !pair.IsValid)return;
        pair.parallel.rotation=delta*pair.parallel.rotation;
        pair.perpendicular.rotation=delta*pair.perpendicular.rotation;
    }
}
