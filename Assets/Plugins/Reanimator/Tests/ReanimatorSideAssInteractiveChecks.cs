#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string VerifySelectedSideAssAutomation()
    {
        int originalTargets=SideAssAllInTargets;
        try
        {
            foreach(int mask in new[]{1,2,3,0})
            {
                var unchanged=new System.Collections.Generic.Dictionary<LayerVertexData,string>();
                for(int side=0;side<2;side++)
                {
                    if((mask&(1<<side))!=0)continue;
                    var layer=layerInfos.Find(l=>l.name==(side==0?"l_leg":"r_leg"));
                    if(layer!=null && TryGetLayerVertexData(layer,out var data))
                        unchanged[data]=JsonUtility.ToJson(new SideAssGeometryStamp{vertices=data.vertices,uv=data.vertexUvs,triangles=data.triangles,weights=data.boneWeights});
                }
                int selected=selectedBoneIndex;TryGetSelectedLayer(out var previous);
                SideAssAllInTargets=mask;
                var names=ApplySelectedSideAss();
                var expected=new[]{"l_leg","r_leg"}.Where((name,side)=>(mask&(1<<side))!=0).ToArray();
                if(!names.SequenceEqual(expected))throw new Exception("Side Ass did not honor independent left/right enable flags.");
                if(selectedBoneIndex!=selected || !TryGetSelectedLayer(out var after) || !ReferenceEquals(previous,after))throw new Exception("Side Ass changed the caller's selection.");
                foreach(var pair in unchanged)
                    if(pair.Value!=JsonUtility.ToJson(new SideAssGeometryStamp{vertices=pair.Key.vertices,uv=pair.Key.vertexUvs,triangles=pair.Key.triangles,weights=pair.Key.boneWeights}))
                        throw new Exception("Side Ass changed an unselected leg's geometry or numeric weights.");
                if(SideAssAllInTargets!=mask)throw new Exception("Side Ass enable flags were not persisted.");
            }
            return "PASS: Side Ass left-only, right-only, both and neither; unselected legs and caller selection preserved.";
        }
        finally{SideAssAllInTargets=originalTargets;}
    }

    public string VerifyQuickSideAss()
    {
        string settings=SideAssCurveStamp;
        if(!QuickSideAss(out var message))throw new Exception("Quick: "+message);
        TryGetSelectedLayer(out var layer);TryGetLayerVertexData(layer,out var data);
        string geometry=SideAssStamp(data);int count=bones.Count;
        if(!QuickSideAss(out message))throw new Exception("Repeated Quick: "+message);
        if(SideAssCurveStamp!=settings)throw new Exception("Quick changed the current curve or smoothing.");
        if(bones.Count!=count || SideAssStamp(data)!=geometry)throw new Exception("Repeated Quick changed geometry or duplicated helpers.");
        var recipe=sideAssFile.regions.First(r=>r.leg==bones[selectedBoneIndex].name);
        if(recipe.curveStamp!=settings || recipe.fanPointCount!=SideAssFanPoints || recipe.fanFlip!=SideAssFlip || SideAssTestEnabled)
            throw new Exception("Quick did not apply the current settings.");
        return "PASS: Quick auto-draws and applies current settings; repeated Quick is stable.";
    }

    public static string VerifySideAssHelperSmoothing()
    {
        var renderer=new PsdLayerPreviewRenderer{sideAssCurves=new SideAssCurves()};
        if(renderer.SideAssHelperSmoothing!=0)throw new Exception("Smoothing must default to zero.");
        renderer.SideAssArcInfluence=AnimationCurve.Constant(0,1,.8f);
        var hard=renderer.SideAssArcWeights(.375f,1);
        if(Mathf.Abs(hard.x-.8f)>.0001f || hard.y!=0)throw new Exception("Zero smoothing changed the hard split.");
        renderer.SideAssHelperSmoothing=.5f;
        var half=renderer.SideAssArcWeights(.375f,1);
        if(Mathf.Abs(half.x-.7f)>.0001f || Mathf.Abs(half.y-.1f)>.0001f)throw new Exception("Smoothing strength is not linear.");
        renderer.SideAssHelperSmoothing=1;
        var left=renderer.SideAssArcWeights(.375f,1);var center=renderer.SideAssArcWeights(.5f,1);var right=renderer.SideAssArcWeights(.625f,1);
        if(Mathf.Abs(left.x-.6f)>.0001f || Mathf.Abs(left.y-.2f)>.0001f || Mathf.Abs(center.x-.4f)>.0001f || Mathf.Abs(center.y-.4f)>.0001f || Mathf.Abs(right.x-.2f)>.0001f || Mathf.Abs(right.y-.6f)>.0001f)
            throw new Exception("Smoothing does not redistribute influence across curve time 0.25..0.75.");
        for(int i=0;i<=100;i++)
        {
            float t=i/100f;var w=renderer.SideAssArcWeights(t,.5f);
            if(Mathf.Abs(w.x+w.y-.2f)>.0001f || Mathf.Abs(w.z-.8f)>.0001f || w.x<0 || w.y<0)throw new Exception("Smoothing changed curve strength, radial falloff or leg weight.");
            if(t<=.25f && w.y!=0 || t>=.75f && w.x!=0)throw new Exception("Smoothing escaped its curve interval.");
        }
        if(JsonUtility.FromJson<SideAssCurves>(renderer.SideAssCurveStamp).helperSmoothing!=1)throw new Exception("Smoothing was not serialized.");
        return "PASS: smoothing defaults to zero, blends linearly only over curve time 0.25..0.75 and preserves total helper/leg weight.";
    }

    public static string VerifySideAssAttachmentCurveDomain()
    {
        var renderer=new PsdLayerPreviewRenderer{sideAssCurves=new SideAssCurves()};
        // Unevenly spaced, bent contour: halfway means contour distance, not
        // vertex count, angle, or the perpendicular helper's original direction.
        var arc=new[]{Vector2.zero,new Vector2(2,0),new Vector2(10,0),new Vector2(10,30)};
        Vector3 At(Vector2 p,float radial=1)=>renderer.SideAssSourceWeights(p,radial,Vector2.zero,Vector2.right,Vector2.up,arc,2,10);
        var start=At(arc[0]);var middle=At(new Vector2(10,10));var end=At(arc[3]);
        if(start.z<.999f || middle.z<.999f || end.z<.999f)throw new Exception("Default curve must meet the leg at both ends and the split.");
        renderer.SideAssArcInfluence=AnimationCurve.Linear(0,0,1,1);
        var first=At(new Vector2(10,0));var second=At(new Vector2(10,20));var inner=At(new Vector2(10,20),.5f);
        if(Mathf.Abs(first.x-.25f)>.0001f || first.y!=0 || Mathf.Abs(second.y-.75f)>.0001f || second.x!=0)
            throw new Exception("Global curve time/height was not split between the helpers at half contour length.");
        if(Mathf.Abs(inner.y-.1875f)>.0001f)throw new Exception("Radial falloff was lost.");
        if(At(arc[3]).y<.999f)throw new Exception("An old cap fade still overrides the new curve.");
        if(At(new Vector2(10,10)).x!=0 || Mathf.Abs(At(new Vector2(10,10)).y-.5f)>.0001f)throw new Exception("Midpoint ownership is ambiguous.");
        var analytic=renderer.SideAssCurveWeights(-9*Mathf.Deg2Rad,1);
        if(analytic.x<=0 || analytic.y!=0)throw new Exception("Extra attachment degrees are outside the first half.");
        foreach(var w in new[]{first,second,inner,analytic})
            if(Mathf.Abs(w.x+w.y+w.z-1)>.0001f)throw new Exception("Curve weights are not normalized.");
        return "PASS: one full-contour curve; first half back, second half perpendicular, height controls weight, leg gets remainder.";
    }

    public string VerifySideAssInteractivePreview()
    {
        if(!TryGetSelectedLayer(out var layer)||!TryGetLayerVertexData(layer,out var data))throw new Exception("Select the fixture leg.");
        var boneList=bones;var boneReferences=bones.ToArray();string boneJson=string.Join("|",bones.Select(JsonUtility.ToJson));
        string mesh=SideAssStamp(data),recipes=JsonUtility.ToJson(sideAssFile),saved=File.Exists(SideAssPath)?File.ReadAllText(SideAssPath):null;
        var curves=JsonUtility.ToJson(GetSideAssCurves());
        try
        {
            if(!BuildSideAssTest(out var message))throw new Exception(message);
            if(sideAssTestRecipe.arcSamples.Length!=SideAssFanPoints)throw new Exception("Preview ignored arc point count.");
            var neutral=sideAssTestVertices.ToArray();
            if(neutral.Zip(sideAssTestData.vertices,(a,b)=>Vector2.Distance(a,b)).Max()>.01f)throw new Exception("Neutral pseudo geometry inherited a stale bind pose.");
            SideAssTestRotation=55;SideAssTestBack=new Vector2(35,-20);SideAssTestPerpendicular=new Vector2(-10,45);
            UpdateSideAssTestPose();var posed=sideAssTestVertices.ToArray();
            if(neutral.SequenceEqual(posed))throw new Exception("Pseudo skeleton did not deform the preview.");
            SideAssArcInfluence=AnimationCurve.Constant(0,1,1);
            
            UpdateSideAssTestPose();
            if(posed.SequenceEqual(sideAssTestVertices))throw new Exception("Curve edits did not change the posed preview.");
            SideAssHelperSmoothing=0;UpdateSideAssTestPose();var unsmoothed=sideAssTestVertices.ToArray();
            SideAssHelperSmoothing=1;UpdateSideAssTestPose();
            if(unsmoothed.SequenceEqual(sideAssTestVertices))throw new Exception("Smoothing did not update the posed preview.");
            if(SideAssTestRotation!=55 || SideAssTestBack!=new Vector2(35,-20))throw new Exception("Curve editing lost test pose.");
            if(!ReferenceEquals(bones,boneList)||!bones.SequenceEqual(boneReferences)||boneJson!=string.Join("|",bones.Select(JsonUtility.ToJson)))throw new Exception("Preview changed source bones.");
            if(mesh!=SideAssStamp(data)||recipes!=JsonUtility.ToJson(sideAssFile)||saved!=(File.Exists(SideAssPath)?File.ReadAllText(SideAssPath):null))throw new Exception("Preview changed production geometry or recipes.");
            if(sideAssTestVertices.Any(p=>float.IsNaN(p.x)||float.IsNaN(p.y)))throw new Exception("Invalid preview deformation.");
            return "PASS: temporary fan, leg rotation, helper offsets, live curves and smoothing; source bones, mesh and recipe unchanged.";
        }
        finally{sideAssCurves=JsonUtility.FromJson<SideAssCurves>(curves);ResetSideAssTestPose();SideAssTestEnabled=false;}
    }
}
#endif
