#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal bool QuickSideAss(out string message)
    {
        if(!AutoDrawSideAssArc(out message))return false;
        if(!ApplySideAss(out message))return false;
        SideAssTestEnabled=false;
        message="Side Ass: automatic arc applied with current curve and settings.";
        return true;
    }

    internal bool AutoDrawSideAssArc(out string message)
    {
        LoadSideAss();
        message="Select a leg sprite and its upper leg bone.";
        if(selectedBoneIndex<0 || selectedBoneIndex>=bones.Count || !TryGetSelectedLayer(out var layer) || !TryGetLayerVertexData(layer,out var data))return false;
        selectedBoneIndex=SideAssControlBoneIndex(selectedBoneIndex);
        // Selecting a generated helper to move it should not turn that helper
        // into the input leg when the user next clicks Auto arc.
        var existing=sideAssFile.regions.Find(r=>r.volume==bones[selectedBoneIndex].name || r.press==bones[selectedBoneIndex].name);
        if(existing!=null && TryFindBoneIndexByName(existing.leg,out var source))selectedBoneIndex=source;
        var bone=bones[selectedBoneIndex];
        if(!TryResolveFullBoobOuterBoundaryLoop(data,out var loop)){message="The selected sprite has no recoverable contour.";return false;}
        var rest=CloneBones(bones);foreach(var item in rest){item.position=item.initialPosition;if(!item.initialRotation.Equals(default(Quaternion)))item.rotation=item.initialRotation;}
        new BonePreviewTransform(bonesTreatAsWorldSpace,rotateLocalBoneOffsetsByParent).ResolveAll(rest);
        var boundary=SideAssImportedBoundary(layer);if(boundary.Length<3)boundary=loop.Select(i=>data.vertices[i]).ToArray();
        var restBone=rest[selectedBoneIndex];
        Vector2 direction=restBone.worldRotation*Vector3.right;
        Vector2 away=bone.parentId>=0?restBone.worldPosition-rest[bone.parentId].worldPosition:direction;
        float length=bone.length;
        if(length<=.0001f)
        {
            var child=bones.FirstOrDefault(b=>b!=null && b.parentId==selectedBoneIndex && !b.name.Contains("_booty"));
            if(child!=null)length=Vector2.Distance(child.worldPosition,bone.worldPosition);
        }
        if(!TryBuildSideAssAutoArc(boundary,restBone.worldPosition,direction,length,away,out var arc,out message))return false;
        RegisterUndo("Auto Side Ass arc");
        ClearSideAssContour();sideAssAutoBaseArc=arc;
        sideAssContour.AddRange(arc.Select(p=>MapNippleTargetToCurrentDraw(layer,p)));
        sideAssAutoDrawingDoc=sideAssContour.ToArray();sideAssAutoLeg=bone.name;
        message=$"Auto arc: {arc.Length} contour points; end at 25% of upper leg length.";
        return true;
    }

    internal static bool TryBuildSideAssAutoArc(IReadOnlyList<Vector2> boundary,Vector2 origin,Vector2 legDirection,float length,Vector2 awayFromHips,out Vector2[] arc,out string error)
    {
        arc=Array.Empty<Vector2>();error=null;
        if(boundary==null || boundary.Count<3 || legDirection.sqrMagnitude<.0000001f || length<=.0001f)
        {error="A closed sprite contour and a nonzero upper leg length are required.";return false;}
        var direction=legDirection.normalized;var back=-direction;
        float nearest=float.PositiveInfinity;int edge=-1;
        Vector2 hit=Vector2.zero;
        // A clipped/occluded hip can leave the bone base outside its sprite.
        // Prefer the original backward ray, then enter the visible leg forwards.
        for(int cast=0;cast<2 && edge<0;cast++)
        {
            var ray=cast==0?back:direction;
            for(int i=0;i<boundary.Count;i++)
            {
                var p=boundary[i];var segment=boundary[(i+1)%boundary.Count]-p;
                float denominator=Cross(ray,segment);
                if(Mathf.Abs(denominator)<.0000001f)continue;
                float t=Cross(p-origin,segment)/denominator,u=Cross(p-origin,ray)/denominator;
                if(t<-.00001f || u<-.00001f || u>1.00001f || t>=nearest)continue;
                nearest=Mathf.Max(0,t);edge=i;hit=origin+ray*nearest;
            }
        }
        if(edge<0)
        {
            // If the axis misses the visible cap sideways, project its base onto
            // the contour instead of inventing points outside the sprite.
            for(int i=0;i<boundary.Count;i++)
            {
                var p=boundary[i];var delta=boundary[(i+1)%boundary.Count]-p;
                float t=Mathf.Clamp01(Vector2.Dot(origin-p,delta)/Mathf.Max(delta.sqrMagnitude,1e-20f));
                var candidate=p+delta*t;float distance=(candidate-origin).sqrMagnitude;
                if(distance>=nearest)continue;
                nearest=distance;edge=i;hit=candidate;
            }
        }
        float cut=length*.25f;
        float startAlong=Vector2.Dot(hit-origin,direction);
        float farthest=boundary.Max(p=>Vector2.Dot(p-origin,direction));
        if(farthest-startAlong<=.00001f)
        {
            edge=Enumerable.Range(0,boundary.Count).OrderBy(i=>Vector2.Dot(boundary[i]-origin,direction)).First();
            hit=boundary[edge];startAlong=Vector2.Dot(hit-origin,direction);
        }
        if(cut<=startAlong+.00001f || cut>=farthest-.00001f)
            cut=startAlong+Mathf.Min(length*.25f,(farthest-startAlong)*.5f);
        var forward=boundary[(edge+1)%boundary.Count]-hit;
        var reverse=boundary[edge]-hit;
        // At a vertex, use the next real edge rather than a zero tangent.
        if(forward.sqrMagnitude<.0000001f)forward=boundary[(edge+2)%boundary.Count]-hit;
        if(reverse.sqrMagnitude<.0000001f)reverse=boundary[(edge-1+boundary.Count)%boundary.Count]-hit;
        var away=awayFromHips.sqrMagnitude>.0000001f?awayFromHips.normalized:direction;
        float alignment=Vector2.Dot(forward.normalized,away)-Vector2.Dot(reverse.normalized,away);
        if(Mathf.Abs(alignment)<.00001f)alignment=Vector2.Dot(forward.normalized,direction)-Vector2.Dot(reverse.normalized,direction);
        int step=alignment>=0?1:-1;
        int next=step>0?(edge+1)%boundary.Count:edge;
        var points=new List<Vector2>{hit};
        var previous=hit;
        for(int walked=0;walked<=boundary.Count;walked++)
        {
            var p=boundary[next];
            float from=Vector2.Dot(previous-origin,direction)-cut;
            float to=Vector2.Dot(p-origin,direction)-cut;
            if(from<=0 && to>=0 && to-from>.0000001f)
            {
                var end=Vector2.Lerp(previous,p,-from/(to-from));
                if(Vector2.Distance(points[points.Count-1],end)>.00001f)points.Add(end);
                arc=points.ToArray();return arc.Length>=2;
            }
            if(Vector2.Distance(points[points.Count-1],p)>.00001f)points.Add(p);
            previous=p;next=(next+step+boundary.Count)%boundary.Count;
        }
        error="The contour does not reach the perpendicular at 25% of upper leg length.";return false;
        float Cross(Vector2 x,Vector2 y)=>x.x*y.y-x.y*y.x;
    }
}
#endif
