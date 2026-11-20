#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    internal bool TryInheritReanimateShoulders(LayerInfo target,out string error)
    {
        error=null;
        if(!TryFindBoneIndexByName("shoulders",out var shoulder) || !TryGetLayerVertexData(target,out var data))
        {error="Shoulders rig and clothing geometry are required.";return false;}
        var body=layerInfos.Find(l=>l!=null && NormalizeAutoWeightName(l.name) is "body" or "torso");
        if(body==null){error="Body sprite is missing.";return false;}
        var sources=new List<LayerInfo>();
        foreach(string name in new[]{"l_arm","r_arm"})
        {
            var arm=FindShoulderSprite(name);
            if(arm!=null && arm!=target)sources.Add(arm);
        }
        if(body!=target)sources.Add(body);
        Vector2 start=GetBoneBasePoint(bones[shoulder]),axis=bones[shoulder].worldRotation*Vector3.right;
        float length=bones[shoulder].length;
        // Include the actual weld/helper reach on both arms, including sleeves
        // extending below the torso's shoulder band.
        foreach(var layer in sources)
        {
            if(layer==body || !TryGetLayerVertexData(layer,out var arm))continue;
            for(int i=0;i<arm.vertices.Length;i++)
            {
                var weight=arm.boneWeights[i];bool affected=false;
                foreach(int slot in new[]{weight.boneIndex0,weight.boneIndex1,weight.boneIndex2,weight.boneIndex3})
                    if(slot>=0 && slot<arm.spriteBoneToRendererBone.Length && GetSpriteBoneWeight(weight,slot)>0f)
                    {
                        string name=bones[arm.spriteBoneToRendererBone[slot]].name;
                        if(name=="shoulders" || name.IndexOf("_shoulder_helper",StringComparison.OrdinalIgnoreCase)>=0){affected=true;break;}
                    }
                if(affected)length=Mathf.Max(length,Vector2.Dot(arm.vertices[i]-start,axis));
            }
        }
        var min=data.vertices.Aggregate(Vector2.Min);var max=data.vertices.Aggregate(Vector2.Max);
        var rectangle=new[]{min,new Vector2(max.x,min.y),max,new Vector2(min.x,max.y)};
        var polygon=new List<Vector2>();
        for(int i=0;i<rectangle.Length;i++)
        {
            var a=rectangle[i];var b=rectangle[(i+1)%rectangle.Length];
            float da=Vector2.Dot(a-start,axis)-length,db=Vector2.Dot(b-start,axis)-length;
            if(da<=0)polygon.Add(a);
            if((da<0 && db>0)||(da>0 && db<0))polygon.Add(Vector2.Lerp(a,b,da/(da-db)));
        }
        // Clothing below the shoulder band has nothing to inherit.
        if(polygon.Count<3)return true;
        return TryInheritSurfaceRegion(target,sources,point=>Vector2.Dot(point-start,axis)<=length,out error,completeCoverage:true);
    }

    private bool TryInheritSurfaceRegion(LayerInfo target,IReadOnlyList<LayerInfo> sources,Func<Vector2,bool> contains,out string error,bool completeCoverage=false)
    {
        error=null;
        if(!TryGetLayerVertexData(target,out var data)){error="Target surface geometry is missing.";return false;}
        var staged=CloneProjectorLayerData(data);
        var sourceData=BuildProjectorSourceList(sources.Select(BuildSelectionKey).ToArray(),BuildSelectionKey(target),BuildKey(target.name,target.layerId),true,staged);
        if(sourceData.Count==0)return true;
        foreach(var source in sourceData)
            foreach(var point in source.vertices)
                if(contains(point))InsertShoulderInteriorSupport(staged,point);
        BuildEdgePairs(staged.triangles,staged.vertices.Length,out staged.allEdgePairs,out staged.boundaryEdgePairs);
        var weighted=CloneProjectorLayerData(staged);
        if(!TryBuildProjectorWeights(weighted,sourceData,false,true,true,out _,out var weights,out var projected,out _))
        {error="No source weights could be transferred to the surface.";return false;}
        weighted.boneWeights=weights;
        for(int i=0;i<weights.Length;i++)
            if(projected[i] && contains(staged.vertices[i]) &&
               TryRemapProjectorWeight(weights[i],weighted,staged,out var weight,out _))staged.boneWeights[i]=weight;
        if(completeCoverage)
            for(int i=0;i<staged.vertices.Length;i++)
            {
                if(projected[i] || !contains(staged.vertices[i]))continue;
                if(!TrySampleNearestShoulderSurface(staged.vertices[i],sourceData,out var source,out var sample) ||
                    !TryRemapProjectorWeight(sample,source,staged,out var inherited,out _))
                {error="Could not cover the clothing shoulder edge with source surface weights.";return false;}
                staged.boneWeights[i]=inherited;
            }
        CommitOtherRegion(target,data,staged);return true;
    }

    private static bool TrySampleNearestShoulderSurface(Vector2 point,IReadOnlyList<LayerVertexData> sources,
        out LayerVertexData source,out BoneWeight weight)
    {
        source=null;weight=default;float best=float.PositiveInfinity;Vector2 closest=point;
        foreach(var candidate in sources)
            for(int t=0;t+2<candidate.triangles.Length;t+=3)
            {
                var p=ClosestPointOnTriangle(point,candidate.vertices[candidate.triangles[t]],
                    candidate.vertices[candidate.triangles[t+1]],candidate.vertices[candidate.triangles[t+2]]);
                float distance=(p-point).sqrMagnitude;
                if(distance>=best)continue;
                best=distance;source=candidate;closest=p;
            }
        // Sample the nearest triangle surface barycentrically. Nearest-vertex
        // fallback would introduce new steps as the edge crosses a Voronoi cell.
        return source!=null && TryTransferWeightFromSource(closest,source,true,out weight,useClosestTriangleFallback:true);
    }

    private static void InsertShoulderInteriorSupport(LayerVertexData data,Vector2 point)
    {
        if(data.vertices.Any(p=>(p-point).sqrMagnitude<=.0001f) ||
            !TryFindContainingTriangle(data.triangles,data.vertices,point,out var triangle,out var a,out var b,out var c,
                out var ab,out var bc,out var ca,out _) || Mathf.Min(ab,Mathf.Min(bc,ca))<=.01f)return;
        var pa=data.vertices[a];var pb=data.vertices[b];var pc=data.vertices[c];
        float area=FullBoobCross(pb-pa,pc-pa);if(Mathf.Abs(area)<.000001f)return;
        float wb=FullBoobCross(point-pa,pc-pa)/area,wc=FullBoobCross(pb-pa,point-pa)/area,wa=1f-wb-wc;
        int index=data.vertices.Length;
        var uv=data.vertexUvs[a]*wa+data.vertexUvs[b]*wb+data.vertexUvs[c]*wc;
        var weight=BlendBoneWeights(data.boneWeights[a],wa,data.boneWeights[b],wb,data.boneWeights[c],wc,null);
        Array.Resize(ref data.vertices,index+1);data.vertices[index]=point;
        Array.Resize(ref data.vertexUvs,index+1);data.vertexUvs[index]=uv;
        Array.Resize(ref data.boneWeights,index+1);data.boneWeights[index]=weight;
        int count=data.triangles.Length;Array.Resize(ref data.triangles,count+6);
        data.triangles[triangle]=a;data.triangles[triangle+1]=b;data.triangles[triangle+2]=index;
        data.triangles[count]=b;data.triangles[count+1]=c;data.triangles[count+2]=index;
        data.triangles[count+3]=c;data.triangles[count+4]=a;data.triangles[count+5]=index;
    }
}
#endif
