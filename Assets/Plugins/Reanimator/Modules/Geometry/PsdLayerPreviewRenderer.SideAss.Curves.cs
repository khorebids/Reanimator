#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    [Serializable] private sealed class SideAssCurves
    {
        public int attachmentDomainVersion=3;
        public AnimationCurve arcInfluence=DefaultSideAssArcInfluence();
        public float helperSmoothing;
        public int bodyContinuationVersion;
        public float bodyReach=1f;
    }
    private static AnimationCurve DefaultSideAssArcInfluence()=>new AnimationCurve(
        new Keyframe(0,0),new Keyframe(.25f,.5f),new Keyframe(.5f,0),new Keyframe(.75f,.5f),new Keyframe(1,0));
    private SideAssCurves sideAssCurves;
    private DateTime sideAssCurvesFileTime;
    private bool sideAssCurvesDirty;
    internal string SideAssCurveRefreshDiagnostic {get;private set;}
    private const string SideAssCurvesPath="ProjectSettings/ReanimatorSideAssCurves.json";
    private SideAssCurves GetSideAssCurves()
    {
        if(sideAssCurves==null)
        {
            sideAssCurves=File.Exists(SideAssCurvesPath)?JsonUtility.FromJson<SideAssCurves>(File.ReadAllText(SideAssCurvesPath)):new SideAssCurves();
            sideAssCurvesFileTime=File.GetLastWriteTimeUtc(SideAssCurvesPath);
        }
        sideAssCurves??=new SideAssCurves();
        if(sideAssCurves.bodyContinuationVersion==0){sideAssCurves.bodyContinuationVersion=1;sideAssCurves.bodyReach=1f;}
        if(sideAssCurves.arcInfluence==null || sideAssCurves.arcInfluence.length==0)sideAssCurves.arcInfluence=DefaultSideAssArcInfluence();
        sideAssCurves.attachmentDomainVersion=3;return sideAssCurves;
    }
    internal AnimationCurve SideAssArcInfluence {get=>GetSideAssCurves().arcInfluence;set{GetSideAssCurves().arcInfluence=value;sideAssCurvesDirty=true;}}
    internal float SideAssHelperSmoothing {get=>Mathf.Clamp01(GetSideAssCurves().helperSmoothing);set{GetSideAssCurves().helperSmoothing=Mathf.Clamp01(value);sideAssCurvesDirty=true;}}
    internal float SideAssBodyReach {get=>Mathf.Clamp(GetSideAssCurves().bodyReach,0f,2f);set{GetSideAssCurves().bodyReach=Mathf.Clamp(value,0f,2f);sideAssCurvesDirty=true;}}
    private string SideAssCurveStamp=>JsonUtility.ToJson(GetSideAssCurves());
    private Vector3 SideAssArcWeights(float progress,float radial)
    {
        float t=Mathf.Clamp01(progress),r=Mathf.Clamp01(radial);
        float influence=Mathf.Clamp01(SideAssArcInfluence.Evaluate(t))*r*r;
        // At zero retain the exact half split. Smoothing redistributes only the
        // existing helper influence, linearly across the middle half of the arc.
        float hardSplit=t<.5f?0:1;
        float perpendicularShare=Mathf.Lerp(hardSplit,Mathf.InverseLerp(.25f,.75f,t),SideAssHelperSmoothing);
        return new Vector3(influence*(1-perpendicularShare),influence*perpendicularShare,1-influence);
    }
    private Vector3 SideAssCurveWeights(float angle,float radial)
    {
        return SideAssArcWeights(Mathf.InverseLerp(-SideAssEndDegrees,90+SideAssEndDegrees,angle*Mathf.Rad2Deg),radial);
    }
    private Vector3 SideAssSourceWeights(Vector2 p,float radial,Vector2 center,Vector2 a,Vector2 b,Vector2[] arc,float prefix,float perpendicular)
    {
        // Use actual contour length, including both end extensions. No independent
        // cap fade or second curve may override the height of the global curve.
        SideAssArcDistance(p,arc,out _,out var progress);
        return SideAssArcWeights(progress,radial);
    }
    internal void SaveAndApplySideAssCurves()
    {
        File.WriteAllText(SideAssCurvesPath,SideAssCurveStamp);
        sideAssCurvesFileTime=File.GetLastWriteTimeUtc(SideAssCurvesPath);
        sideAssCurvesDirty=false;
        RefreshSideAssCurveWeights();
        if(SideAssTestEnabled)UpdateSideAssTestPose();
    }
    internal void SynchronizeSideAssCurveSettings()
    {
        if(sideAssCurves==null){GetSideAssCurves();return;}
        if(sideAssCurvesDirty)return;
        if(sideAssCurvesFileTime!=default && sideAssCurvesFileTime==File.GetLastWriteTimeUtc(SideAssCurvesPath))return;
        sideAssCurves=null;GetSideAssCurves();
        RefreshSideAssCurveWeights();if(SideAssTestEnabled)UpdateSideAssTestPose();
    }
    private void RefreshSideAssCurveWeights(bool previewOnly=false)
    {
        SideAssCurveRefreshDiagnostic="";
        if(!previewOnly)LoadSideAss();
        var recipes=previewOnly?new[]{sideAssTestRecipe}:sideAssFile.regions.Where(r=>r.manualFan && r.arcSamples!=null);
        foreach(var recipe in recipes)
        {
            if(!TryFindBoneIndexByName(recipe.leg,out var leg)||!TryFindBoneIndexByName(recipe.volume,out var a)||!TryFindBoneIndexByName(recipe.press,out var b))continue;
            a=SideAssSkinBoneIndex(a);b=SideAssSkinBoneIndex(b);
            LayerVertexData data;
            if(previewOnly)data=sideAssTestData;
            else if(!vertexDataByLayerSelectionKey.TryGetValue(recipe.layer,out data))continue;
            int sa=Array.IndexOf(data.spriteBoneToRendererBone,a),sb=Array.IndexOf(data.spriteBoneToRendererBone,b),sl=Array.IndexOf(data.spriteBoneToRendererBone,leg);
            SideAssCurveRefreshDiagnostic+=$"{recipe.leg} slots={sa}/{sb}/{sl}; ";
            if(sa<0||sb<0||sl<0)continue;
            if(!previewOnly)RegisterUndo("Adjust Side Ass curves");
            int affectedCount=0;
            for(int i=0;i<data.vertices.Length;i++)
            {
                var p=data.vertices[i];bool affected=IsPointInsideFullBoobContour(p,recipe.contour)||SideAssDistance(p,recipe.contour)<=.001f;
                if(recipe.sourceWeights)
                {
                    SideAssArcDistance(p,recipe.weightArc,out var distance,out _);
                    var region=new System.Collections.Generic.List<Vector2>{recipe.weightCenter};region.AddRange(recipe.weightArc);
                    affected|=distance<=.001f||IsPointInsideFullBoobContour(p,region);
                }
                if(!affected)continue;
                affectedCount++;
                Vector3 w;
                if(recipe.sourceWeights)
                {SideAssArcDistance(p,recipe.weightArc,out var distance,out _);float radius=Vector2.Distance(p,recipe.weightCenter);w=SideAssSourceWeights(p,radius/Mathf.Max(.0001f,radius+distance),recipe.weightCenter,recipe.weightA,recipe.weightB,recipe.weightArc,recipe.weightPrefix,recipe.weightPerpendicular);}
                else{var d=p-recipe.weightCenter;float x=Vector2.Dot(d,recipe.weightA)/recipe.weightRA,y=Vector2.Dot(d,recipe.weightB)/recipe.weightRB;w=SideAssCurveWeights(Mathf.Atan2(y,x),Mathf.Sqrt(x*x+y*y));}
                data.boneWeights[i]=BlendBoneWeights(BuildSingleBoneWeight(sa),w.x,BuildSingleBoneWeight(sb),w.y,BuildSingleBoneWeight(sl),w.z,null);
                var saved=recipe.weights?.Find(item=>(item.point-p).sqrMagnitude<.0001f);
                if(saved!=null)saved.applied=data.boneWeights[i];
            }
            SideAssCurveRefreshDiagnostic+=$"affected={affectedCount}; smoothing={SideAssHelperSmoothing}; ";
            if(previewOnly)continue;
            MarkSkinningWeightsChanged(data);MarkEditGeometryDataChanged(data,BuildSelectionKey(data.layer),BuildKey(data.layer.name,data.layer.layerId));
            recipe.curveStamp=SideAssCurveStamp;recipe.geometryStamp=SideAssStamp(data);
            InheritSideAssOnBody(data.layer);
        }
        if(!previewOnly && SideAssPath!=null){Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath));File.WriteAllText(SideAssPath,JsonUtility.ToJson(sideAssFile,true));}
        if(!previewOnly)InvalidateBonePose();
    }
    private static Vector2[] SideAssResampleArc(Vector2[] arc,int count)
    {
        float total=SideAssArcLength(arc);var result=new Vector2[count];int edge=0;float traversed=0;
        for(int i=0;i<count;i++)
        {
            float target=total*i/(count-1);
            while(edge+2<arc.Length&&traversed+Vector2.Distance(arc[edge],arc[edge+1])<target){traversed+=Vector2.Distance(arc[edge],arc[edge+1]);edge++;}
            result[i]=Vector2.Lerp(arc[edge],arc[edge+1],(target-traversed)/Mathf.Max(.0001f,Vector2.Distance(arc[edge],arc[edge+1])));
        }
        return result;
    }
}
#endif
