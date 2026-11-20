#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void InheritSideAssOnBody(LayerInfo leg)
    {
        var body=layerInfos.Find(l=>l!=null && NormalizeAutoWeightName(l.name) is "body" or "torso");
        if(body==null || body==leg || !TryGetLayerVertexData(body,out var target))return;
        var recipe=sideAssFile.regions.Find(r=>r.layer==BuildSelectionKey(leg) && r.manualFan);
        if(recipe?.weightArc==null || recipe.weightArc.Length<2)return;
        if(!TryFindBoneIndexByName(recipe.volume,out var back) || !TryFindBoneIndexByName(recipe.press,out var press))return;
        back=SideAssSkinBoneIndex(back);press=SideAssSkinBoneIndex(press);
        var staged=CloneProjectorLayerData(target);var exterior=target.boundaryEdgePairs.ToArray();
        var saved=recipe.bodyWeights??new List<SideAssSavedWeight>();
        foreach(var item in saved)
        {
            int index=Array.FindIndex(staged.vertices,p=>(p-item.point).sqrMagnitude<.0001f);
            if(index>=0 && AreBoneWeightsApproximatelyEqual(staged.boneWeights[index],Remap(item.applied)))
                staged.boneWeights[index]=Remap(item.original);
        }
        if(!TryEnsureFullBoobSpriteBone(staged,back,out var backSlot) || !TryEnsureFullBoobSpriteBone(staged,press,out var pressSlot))
            throw new InvalidOperationException("Side Ass body continuation could not bind the two existing helpers.");
        float reach=recipe.radius*SideAssBodyReach;
        if(reach>.001f)
        {
            RefineSideAssBoundaryWhere(staged,(a,b)=>Affected(a)||Affected(b)||Affected((a+b)*.5f),Mathf.Max(.001f,recipe.radius*.08f));
            foreach(var point in recipe.weightArc)InsertShoulderInteriorSupport(staged,point);
            BuildEdgePairs(staged.triangles,staged.vertices.Length,out staged.allEdgePairs,out staged.boundaryEdgePairs);
        }
        var next=new List<SideAssSavedWeight>();
        for(int i=0;i<staged.vertices.Length;i++)
        {
            var p=staged.vertices[i];var influence=Influence(p);
            if(influence.x+influence.y<=.000001f)continue;
            var original=WithoutOwnHelpers(staged.boneWeights[i]);
            var values=new Dictionary<int,float>();
            Add(original.boneIndex0,original.weight0);Add(original.boneIndex1,original.weight1);
            Add(original.boneIndex2,original.weight2);Add(original.boneIndex3,original.weight3);
            values[backSlot]=influence.x;values[pressSlot]=influence.y;
            var applied=BuildVertexFixWeight(values);staged.boneWeights[i]=applied;
            next.Add(new SideAssSavedWeight{point=p,original=original,applied=applied});
            void Add(int slot,float value){AccumulateWeightChannel(values,slot,value*(1-influence.x-influence.y));}
        }
        if(!TryNormalizeSideAssTriangles(staged) || !SideAssJoinIsClosed(staged,exterior))
            throw new InvalidOperationException("Side Ass body continuation did not preserve the torso boundary.");
        RegisterUndo("Side Ass body continuation");CommitOtherRegion(body,target,staged);
        recipe.bodyWeights=next;
        recipe.bodyWeightBones=staged.spriteBoneToRendererBone.Select(index=>bones[index].name).ToArray();
        if(SideAssPath!=null){Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath));File.WriteAllText(SideAssPath,JsonUtility.ToJson(sideAssFile,true));}

        bool Affected(Vector2 p){var w=Influence(p);return w.x+w.y>.000001f;}
        Vector3 Influence(Vector2 p)
        {
            if(reach<=.001f || Vector2.Dot(p-recipe.weightCenter,recipe.weightB)<-recipe.radius*.15f)return Vector3.zero;
            SideAssArcDistance(p,recipe.weightArc,out var distance,out var progress);
            if(distance>=reach)return Vector3.zero;
            float radius=Vector2.Distance(p,recipe.weightCenter);
            var w=SideAssArcWeights(progress,radius/Mathf.Max(.0001f,radius+distance));
            bool inside=IsPointInsideFullBoobContour(p,recipe.contour)||SideAssDistance(p,recipe.contour)<=.001f;
            float fade=inside?1f:1f-Mathf.SmoothStep(0,1,distance/reach);
            return new Vector3(w.x*fade,w.y*fade,0);
        }
        BoneWeight WithoutOwnHelpers(BoneWeight weight)
        {
            if(GetSpriteBoneWeight(weight,backSlot)<=0 && GetSpriteBoneWeight(weight,pressSlot)<=0)return weight;
            var values=new Dictionary<int,float>();float removed=0;
            Add(weight.boneIndex0,weight.weight0);Add(weight.boneIndex1,weight.weight1);Add(weight.boneIndex2,weight.weight2);Add(weight.boneIndex3,weight.weight3);
            if(values.Count==0)
            {
                int root=bones.FindIndex(b=>b.name=="hips");
                if(root<0)root=bones.FindIndex(b=>b.name=="spine");
                if(root<0 || !TryEnsureFullBoobSpriteBone(staged,root,out var slot))throw new InvalidOperationException("Body continuation needs torso skinning.");
                values[slot]=Mathf.Max(removed,1f);
            }
            return BuildVertexFixWeight(values);
            void Add(int slot,float value){if(slot==backSlot||slot==pressSlot){removed+=value;return;}AccumulateWeightChannel(values,slot,value);}
        }
        BoneWeight Remap(BoneWeight weight)
        {
            if(recipe.bodyWeightBones==null)return weight;
            if(recipe.bodyWeightBones.Length==staged.spriteBoneToRendererBone.Length &&
                recipe.bodyWeightBones.SequenceEqual(staged.spriteBoneToRendererBone.Select(index=>bones[index].name)))return weight;
            var values=new Dictionary<int,float>();
            Add(weight.boneIndex0,weight.weight0);Add(weight.boneIndex1,weight.weight1);Add(weight.boneIndex2,weight.weight2);Add(weight.boneIndex3,weight.weight3);
            return BuildVertexFixWeight(values);
            void Add(int old,float value)
            {
                if(value<=0 || old<0 || old>=recipe.bodyWeightBones.Length)return;
                int bone=bones.FindIndex(b=>b.name==recipe.bodyWeightBones[old]);
                int slot=Array.IndexOf(staged.spriteBoneToRendererBone,bone);
                if(slot>=0)AccumulateWeightChannel(values,slot,value);
            }
        }
    }
}
#endif
