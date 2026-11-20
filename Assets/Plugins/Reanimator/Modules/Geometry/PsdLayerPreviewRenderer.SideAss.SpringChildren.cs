#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private int SideAssControlBoneIndex(int index)
    {
        if(index<0 || index>=bones.Count)return index;
        var bone=bones[index];int parent=bone.parentId;
        return parent>=0 && parent<bones.Count && bone.name==bones[parent].name+"_SC"?parent:index;
    }

    private int SideAssSkinBoneIndex(int helper)
    {
        if(helper<0 || helper>=bones.Count)return helper;
        int child=bones.FindIndex(b=>b.parentId==helper && b.name==bones[helper].name+"_SC");
        return child>=0?child:helper;
    }

    private int EnsureSideAssSkinChild(int helper,bool resetExisting)
    {
        var parent=bones[helper];string name=parent.name+"_SC";
        bool exists=TryFindBoneIndexByName(name,out var index);
        if(!exists)index=FindOrCreateVGenBone(helper,name,parent.worldPosition,true);
        var child=bones[index];
        if(!exists || resetExisting || child.parentId!=helper)
        {
            child.parentId=helper;
            child.position=ConvertBoneWorldPositionToLocal(helper,parent.worldPosition);
            child.rotation=ConvertBoneWorldRotationToLocal(helper,parent.worldRotation);
            child.initialPosition=bonesTreatAsWorldSpace?parent.initialPosition:Vector2.zero;
            child.initialRotation=bonesTreatAsWorldSpace?parent.initialRotation:Quaternion.identity;
            child.length=parent.length;child.depth=parent.depth;child.color=parent.color;child.visible=parent.visible;
            RecomputeBoneWorldTransforms();
        }
        return index;
    }

    // Reuse the original palette slot and bind matrix whenever possible: the new
    // child has exactly its parent's world pose, so this preserves deformation.
    // If a stale palette already contains both, merge influences into the child slot.
    private bool TransferSideAssSkinWeights(LayerVertexData data,int parent,int child)
    {
        if(data?.spriteBoneToRendererBone==null || parent==child || Array.IndexOf(data.spriteBoneToRendererBone,parent)<0)return false;
        var map=(int[])data.spriteBoneToRendererBone.Clone();
        int target=Array.IndexOf(map,child);BoneWeight[] weights=null;
        bool changed=false;
        for(int slot=0;slot<map.Length;slot++)
        {
            if(map[slot]!=parent)continue;
            if(target<0){map[slot]=child;target=slot;changed=true;continue;}
            if(data.boneWeights==null)continue;
            weights??=(BoneWeight[])data.boneWeights.Clone();
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];
                if(!Uses(w,slot))continue;
                var values=new Dictionary<int,float>();
                Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                weights[i]=BuildVertexFixWeight(values);changed=true;
                void Add(int bone,float weight){if(weight<=0)return;if(bone==slot)bone=target;values[bone]=values.TryGetValue(bone,out var old)?old+weight:weight;}
            }
        }
        if(!changed)return false;
        data.spriteBoneToRendererBone=map;if(weights!=null)data.boneWeights=weights;
        data.lastPoseVersion=-1;data.skinMatrices=null;data.deformedVertices=null;
        return true;
        bool Uses(BoneWeight w,int slot)=>w.boneIndex0==slot&&w.weight0>0 || w.boneIndex1==slot&&w.weight1>0 || w.boneIndex2==slot&&w.weight2>0 || w.boneIndex3==slot&&w.weight3>0;
    }

    internal bool EnsureSideAssSpringChildren(out string message)
    {
        LoadSideAss();
        var helpers=bones.Where(b=>b.parentId>=0 && b.parentId<bones.Count &&
            (b.name==ReaniIKButtControls.ParallelBoneName(bones[b.parentId].name) || b.name==ReaniIKButtControls.PerpendicularBoneName(bones[b.parentId].name)))
            .Select(b=>b.index).ToArray();
        if(helpers.Length==0){message="No manual Side Ass helpers found.";return false;}
        RegisterUndo("Add Butt spring children");int created=0,changedLayers=0;
        foreach(int helper in helpers)
        {
            int before=bones.Count;int child=EnsureSideAssSkinChild(helper,false);created+=bones.Count-before;
            foreach(var data in vertexDataByLayerSelectionKey.Values.Distinct())
                if(TransferSideAssSkinWeights(data,helper,child))
                {MarkSkinningWeightsChanged(data);MarkEditGeometryDataChanged(data,BuildSelectionKey(data.layer),BuildKey(data.layer.name,data.layer.layerId));changedLayers++;}
        }
        foreach(var recipe in sideAssFile.regions.Where(r=>r.manualFan))
            if(vertexDataByLayerSelectionKey.TryGetValue(recipe.layer,out var data))recipe.geometryStamp=SideAssStamp(data);
        if(created>0){BuildBoneDrawOrder();MarkBoneStateChanged();}
        if(created>0 || changedLayers>0)
        {
            if(SideAssPath!=null){Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath));File.WriteAllText(SideAssPath,JsonUtility.ToJson(sideAssFile,true));}
            InvalidateBonePose();
        }
        message="Butt: "+created+" spring children created; parent weights transferred on "+changedLayers+" layer bindings.";return true;
    }
}
#endif
