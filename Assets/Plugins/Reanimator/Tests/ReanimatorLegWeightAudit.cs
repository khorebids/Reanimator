#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string AuditLegWeightStages()
    {
        var copy=new PsdLayerPreviewRenderer();copy.cachedPath=cachedPath;copy.cachedDocumentSize=cachedDocumentSize;
        copy.layerInfos.AddRange(layerInfos);
        foreach(var bone in bones)copy.bones.Add(JsonUtility.FromJson<BoneInfo>(JsonUtility.ToJson(bone)));
        copy.RecomputeBoneWorldTransforms();
        foreach(var entry in vertexDataByLayerSelectionKey)
        {
            if(entry.Value?.layer==null || !new[]{"body","l_leg","r_leg"}.Contains(entry.Value.layer.name))continue;
            var data=JsonUtility.FromJson<LayerVertexData>(JsonUtility.ToJson(entry.Value));
            data.layer=entry.Value.layer;data.texture=entry.Value.texture;
            copy.vertexDataByLayerSelectionKey[entry.Key]=data;
        }
        var stages=new JArray();Snapshot("current");
        copy.ApplyRiggedLimbAutoWeights((1<<7)|(1<<8),false);Snapshot("Auto Weights");
        var baseline=copy.vertexDataByLayerSelectionKey.ToDictionary(p=>p.Key,p=>CloneProjectorLayerData(p.Value));
        var joints=copy.ApplySelectedAutoLimbJoints((1<<8)|(1<<9));Snapshot("Hip Joint V2");
        VerifyLocality();
        copy.ApplySelectedAutoLimbJoints((1<<8)|(1<<9));VerifyLocality();Snapshot("Hip Joint V2 repeated");
        var result=new JObject{["stages"]=stages,["joints"]=new JArray(joints)}.ToString();
        System.IO.File.WriteAllText("Temp/LegWeightStageAudit.json",result);return result;
        void VerifyLocality()
        {
            foreach(var entry in baseline.Where(p=>new[]{"l_leg","r_leg"}.Contains(p.Value.layer.name)))
            {
                var before=entry.Value;var after=copy.vertexDataByLayerSelectionKey[entry.Key];
                if(!SideAssJoinIsClosed(after,before.boundaryEdgePairs))throw new Exception("Hip joint changed the leg silhouette.");
                int slot=Array.FindIndex(before.spriteBoneToRendererBone,b=>copy.bones[b].name=="hips");
                for(int i=0;i<before.vertices.Length;i++)
                {
                    if(before.vertices[i]!=after.vertices[i] || before.vertexUvs[i]!=after.vertexUvs[i])throw new Exception("Hip joint changed an existing vertex/UV.");
                    if(slot>=0 && GetSpriteBoneWeight(before.boneWeights[i],slot)==0f && !before.boneWeights[i].Equals(after.boneWeights[i]))
                        throw new Exception("Hip joint changed weights beyond the attachment region.");
                }
            }
        }
        void Snapshot(string stage)
        {
            var layers=new JArray();
            foreach(var data in copy.vertexDataByLayerSelectionKey.Values.Where(d=>d.layer!=null && new[]{"l_leg","r_leg"}.Contains(d.layer.name)))
            {
                copy.TryFindBoneIndexByName(data.layer.name,out var leg);var bone=copy.bones[leg];
                Vector2 start=GetBoneBasePoint(bone),axis=bone.worldRotation*Vector3.right;
                int slot=Array.FindIndex(data.spriteBoneToRendererBone,b=>copy.bones[b].name=="hips");
                var influenced=new JArray();
                for(int i=0;i<data.vertices.Length;i++)
                {
                    float w=slot<0?0:GetSpriteBoneWeight(data.boneWeights[i],slot);
                    if(w>.001f)influenced.Add(new JObject{["i"]=i,["progress"]=Vector2.Dot(data.vertices[i]-start,axis)/Mathf.Max(.001f,bone.length),["weight"]=w});
                }
                layers.Add(new JObject{["name"]=data.layer.name,["vertices"]=data.vertices.Length,["hips"]=influenced});
            }
            stages.Add(new JObject{["stage"]=stage,["layers"]=layers});
        }
    }
}
#endif
