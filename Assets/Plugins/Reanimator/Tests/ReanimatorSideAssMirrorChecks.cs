#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifySideAssClosedBoundaryRefinement()
    {
        var data=new LayerVertexData{
            vertices=new[]{Vector2.zero,new Vector2(2,0),new Vector2(2,2),new Vector2(0,2)},
            vertexUvs=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},
            boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},4).ToArray(),
            spriteBoneToRendererBone=new[]{0},triangles=new[]{0,1,2,0,2,3},
            boundaryEdgePairs=new[]{0,1,1,2,2,3,3,0}};
        var original=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var weights=data.boneWeights.ToArray();
        var exterior=data.boundaryEdgePairs.ToArray();var arc=new[]{data.vertices[0],data.vertices[1],data.vertices[2]};
        var renderer=new PsdLayerPreviewRenderer();renderer.RefineSideAssClosedBoundary(data,arc,.2f);
        if(data.vertices.Length<=4 || !SideAssJoinIsClosed(data,exterior))throw new Exception("Refining boundary opened the mesh.");
        for(int i=0;i<4;i++)if(data.vertices[i]!=original[i] || data.vertexUvs[i]!=uv[i] || !data.boneWeights[i].Equals(weights[i]))
            throw new Exception("Refining boundary changed retained attributes.");
        var refined=data.vertices.ToArray();var triangles=data.triangles.ToArray();
        renderer.RefineSideAssClosedBoundary(data,arc,.2f);
        if(!refined.SequenceEqual(data.vertices)||!triangles.SequenceEqual(data.triangles)||!SideAssJoinIsClosed(data,exterior))
            throw new Exception("Repeated boundary refinement changed validated geometry.");
        return "PASS: local boundary splits remain closed, preserve existing vertices/UVs/weights and repeat without changes.";
    }
    public string RepairAndCaptureCurrentSideAssCurveCache()
    {
        LoadSideAss();var recipe=sideAssFile.regions.First(r=>r.manualFan && r.leg=="r_leg");
        if(vertexDataByLayerSelectionKey.Count==0)
        {
            BuildVertices(cachedPath);
            foreach(var patch in autoGeometryPatches.Where(p=>IsAssetPathMatch(p.assetPath,cachedPath)).ToArray())
                TryBuildLayerDataFromAutoGeometryPatch(patch,out _);
        }
        var data=vertexDataByLayerSelectionKey[recipe.layer];
        var previousDrawMode=deformSpritesWithBonePoseForCurrentDraw;
        deformSpritesWithBonePoseForCurrentDraw=true;
        var originalBones=bones;bones=CloneBones(bones);
        try
        {
        RecomputeBoneWorldTransforms();
        foreach(var name in new[]{recipe.volume,recipe.press})
        {
            if(!TryFindBoneIndexByName(name,out var helper))continue;
            var axis=name==recipe.volume?recipe.weightA:recipe.weightB;
            bones[helper].position=ConvertBoneWorldPositionToLocal(bones[helper].parentId,bones[helper].worldPosition+axis*recipe.radius*.8f);
            RecomputeBoneWorldTransforms();
        }
        InvalidateBonePose();
        const string fixture="Assets/Plugins/Reanimator/Tests/Fixtures/SideAssRightStaleCurves.json";
        if(File.Exists(fixture))
        {
            var captured=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(fixture));
            sideAssCurves=JsonUtility.FromJson<SideAssCurves>((string)captured["oldSettings"]);
            RefreshSideAssCurveWeights();
        }
        var before=GetLayerVerticesForDraw(data).ToArray();var edges=data.boundaryEdgePairs.ToArray();
        if(!File.Exists(fixture))File.WriteAllText(fixture,
            Newtonsoft.Json.JsonConvert.SerializeObject(new{data=JsonUtility.ToJson(data),bones=bones.Select(JsonUtility.ToJson).ToArray(),worldSpace=bonesTreatAsWorldSpace,rotateOffsets=rotateLocalBoneOffsetsByParent,oldSettings=SideAssCurveStamp,settings=File.ReadAllText(SideAssCurvesPath)}));
        sideAssCurves=null;sideAssCurvesDirty=false;GetSideAssCurves();RefreshSideAssCurveWeights();
        InvalidateBonePose();
        var after=GetLayerVerticesForDraw(data).ToArray();SaveSideAssProfileComparison(before,edges,after,data.boundaryEdgePairs);
        return "Current smoothing="+SideAssHelperSmoothing+"; maximum posed correction="+before.Zip(after,Vector2.Distance).Max()+" pixels. Temp/SideAssSmoothingProfiles.png";
        }
        finally {deformSpritesWithBonePoseForCurrentDraw=previousDrawMode;bones=originalBones;RecomputeBoneWorldTransforms();InvalidateBonePose();}
    }

    public string VerifySideAssCurveReload()
    {
        SynchronizeSideAssCurveSettings();
        var expected=SideAssCurveStamp;
        sideAssCurves=new SideAssCurves();sideAssCurvesFileTime=default;sideAssCurvesDirty=false;
        SynchronizeSideAssCurveSettings();
        if(SideAssCurveStamp!=expected)throw new Exception("Hot reload retained stale curve settings.");
        return "PASS: stale default curves after script reload recover saved settings and refresh applied weights.";
    }
    public static string VerifySideAssMirrorWeights()
    {
        var renderer=new PsdLayerPreviewRenderer{sideAssCurves=new SideAssCurves()};
        renderer.SideAssArcInfluence=AnimationCurve.Constant(0,1,.8f);
        var arc=new[]{new Vector2(-3,2),new Vector2(-2,4),new Vector2(1,5),new Vector2(4,1)};
        Vector2 Mirror(Vector2 p)=>new Vector2(-p.x,p.y);
        var refined=SideAssRefineBoundaryArc(arc,.3f);
        var reflected=SideAssRefineBoundaryArc(arc.Select(Mirror).ToArray(),.3f);
        if(refined.Length<=arc.Length || refined.Length!=reflected.Length ||
            refined.Where((p,i)=>(Mirror(p)-reflected[i]).sqrMagnitude>1e-10f).Any() ||
            Mathf.Abs(SideAssArcLength(refined)-SideAssArcLength(arc))>.0001f)
            throw new Exception("Boundary refinement changed the rest silhouette or reflection.");
        foreach(float smooth in new[]{0f,.5f,1f})
        {
            renderer.SideAssHelperSmoothing=smooth;
            for(int i=0;i+1<arc.Length;i++)for(int j=0;j<=20;j++)
            {
                var p=Vector2.Lerp(arc[i],arc[i+1],j/20f)*.8f;
                var a=renderer.SideAssSourceWeights(p,.8f,Vector2.zero,Vector2.right,Vector2.up,arc,0,0);
                var b=renderer.SideAssSourceWeights(Mirror(p),.8f,Vector2.zero,Vector2.left,Vector2.up,arc.Select(Mirror).ToArray(),0,0);
                if((a-b).sqrMagnitude>1e-10f)throw new Exception("Reflection changed Side Ass helper weights.");
                var delta=new Vector2(.4f,-.2f)*a.x+new Vector2(.1f,.3f)*a.y;
                var reflectedDelta=Mirror(new Vector2(.4f,-.2f))*b.x+Mirror(new Vector2(.1f,.3f))*b.y;
                if((Mirror(delta)-reflectedDelta).sqrMagnitude>1e-10f)throw new Exception("Reflection changed deformation.");
            }
        }
        return "PASS: reflected contour weights and deformation match for zero, half and full smoothing.";
    }

    public string VerifyAppliedSideAssSmoothing()
    {
        SynchronizeSideAssCurveSettings();
        LoadSideAss();
        var recipe=sideAssFile.regions.First(r=>r.manualFan && r.leg=="r_leg");
        var data=vertexDataByLayerSelectionKey[recipe.layer];
        var vertices=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var triangles=data.triangles.ToArray();
        var positions=bones.Select(b=>b.position).ToArray();
        var settings=SideAssCurveStamp;
        var disk=File.Exists(SideAssCurvesPath)?File.ReadAllText(SideAssCurvesPath):null;
        try
        {
            SideAssHelperSmoothing=0;SaveAndApplySideAssCurves();
            var hard=data.boneWeights.ToArray();
            SideAssHelperSmoothing=1;SaveAndApplySideAssCurves();
            int changed=hard.Where((w,i)=>!w.Equals(data.boneWeights[i])).Count();
            if(changed==0)throw new Exception("Saving smoothing did not update applied right-leg weights.");
            if(!vertices.SequenceEqual(data.vertices)||!uv.SequenceEqual(data.vertexUvs)||!triangles.SequenceEqual(data.triangles)||!positions.SequenceEqual(bones.Select(b=>b.position)))
                throw new Exception("Curve refresh changed geometry or pose.");
            if(recipe.curveStamp!=SideAssCurveStamp)throw new Exception("Applied curve stamp is stale.");
            var once=data.boneWeights.ToArray();SaveAndApplySideAssCurves();
            if(!once.SequenceEqual(data.boneWeights))throw new Exception("Repeated save changed weights.");
            return "PASS: right-leg smoothing updates "+changed+" applied vertices, preserves geometry/UV/pose, and repeats deterministically.";
        }
        finally
        {
            sideAssCurves=JsonUtility.FromJson<SideAssCurves>(settings);
            if(disk!=null)File.WriteAllText(SideAssCurvesPath,disk);
            sideAssCurvesFileTime=File.GetLastWriteTimeUtc(SideAssCurvesPath);
            sideAssCurvesDirty=false;
            RefreshSideAssCurveWeights();UpdateSideAssTestPose();
        }
    }
}
#endif
