#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string VerifySideAssSpringChildren()
    {
        foreach(var recipe in sideAssFile.regions.Where(r=>r.manualFan))
        {
            foreach(var name in new[]{recipe.volume,recipe.press})
            {
                if(!TryFindBoneIndexByName(name,out var parent))throw new Exception("Missing Butt control bone.");
                int child=SideAssSkinBoneIndex(parent);
                if(child==parent || bones[child].parentId!=parent || bones[child].name!=name+"_SC")throw new Exception("Missing named SC child.");
                if(Vector2.Distance(bones[parent].worldPosition,bones[child].worldPosition)>.001f || Quaternion.Angle(bones[parent].worldRotation,bones[child].worldRotation)>.05f || Mathf.Abs(bones[parent].length-bones[child].length)>.001f)
                    throw new Exception("SC child does not duplicate its parent's world pose and length.");
                float childWeight=0;
                foreach(var data in vertexDataByLayerSelectionKey.Values.Distinct())
                {
                    if(data?.boneWeights==null || data.spriteBoneToRendererBone==null)continue;
                    foreach(var weight in data.boneWeights)
                    {
                        if(Influence(data,weight,parent)>.00001f)throw new Exception("A controlled Butt parent still carries skin weights.");
                        childWeight+=Influence(data,weight,child);
                    }
                }
                if(childWeight<=.001f)throw new Exception("SC child has no inherited helper weights.");
            }
        }
        var dataBefore=vertexDataByLayerSelectionKey.Values.Distinct().ToDictionary(d=>d,SideAssStamp);
        int before=bones.Count;
        if(!EnsureSideAssSpringChildren(out var error))throw new Exception(error);
        if(bones.Count!=before || dataBefore.Any(item=>SideAssStamp(item.Key)!=item.Value))throw new Exception("Repeated SC migration changed geometry or duplicated children.");
        VerifySideAssWeightTransferMath();
        return "PASS: identical SC children carry all parent weights; repeated migration is stable; stale duplicate palette slots merge safely.";

        float Influence(LayerVertexData d,BoneWeight w,int bone)
        {
            float Slot(int index,float weight)=>index>=0 && index<d.spriteBoneToRendererBone.Length && d.spriteBoneToRendererBone[index]==bone?weight:0;
            return Slot(w.boneIndex0,w.weight0)+Slot(w.boneIndex1,w.weight1)+Slot(w.boneIndex2,w.weight2)+Slot(w.boneIndex3,w.weight3);
        }
    }

    private void VerifySideAssWeightTransferMath()
    {
        var w=new BoneWeight{boneIndex0=0,weight0=.6f,boneIndex1=1,weight1=.4f};
        var bind=new[]{Matrix4x4.identity,Matrix4x4.identity};
        var data=new LayerVertexData{spriteBoneToRendererBone=new[]{1,0},boneWeights=new[]{w},bindBoneInverseMatrices=bind};
        if(!TransferSideAssSkinWeights(data,1,2) || !data.boneWeights[0].Equals(w) || data.spriteBoneToRendererBone[0]!=2 || !ReferenceEquals(data.bindBoneInverseMatrices,bind))
            throw new Exception("Migration did not preserve numeric weights and existing bind matrices.");
        data=new LayerVertexData{spriteBoneToRendererBone=new[]{1,2,0},boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=.4f,boneIndex1=1,weight1=.3f,boneIndex2=2,weight2=.3f}}};
        if(!TransferSideAssSkinWeights(data,1,2))throw new Exception("Stale parent weights were not migrated.");
        var merged=data.boneWeights[0];
        float sum=(merged.boneIndex0==1?merged.weight0:0)+(merged.boneIndex1==1?merged.weight1:0)+(merged.boneIndex2==1?merged.weight2:0)+(merged.boneIndex3==1?merged.weight3:0);
        if(Mathf.Abs(sum-.7f)>.00001f || TransferSideAssSkinWeights(data,1,2))throw new Exception("Duplicate parent/child slots did not merge deterministically.");
    }
}
#endif
