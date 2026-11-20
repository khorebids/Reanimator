#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool ApplyLocalizedAutoHipJoint(out string error)
    {
        error="Hip Joint preview is unavailable.";
        if(!TryBuildJointV2PreviewGeometry(out var source,out var preview,out _,out _,out _,
                LimbCurve,LimbCurve,ReanimatorJointV2Defaults.SpecialJointDistance))return false;
        int parent=bones[selectedBoneIndex].parentId;
        int hips=FindSpriteBoneIndexForRendererBone(source,parent);
        int leg=FindSpriteBoneIndexForRendererBone(source,selectedBoneIndex);
        if(hips<0 || leg<0)return false;
        var original=CloneProjectorLayerData(source);
        var staged=CloneProjectorLayerData(source);
        // Joint V2's ray/arc geometry may reach the distal cap of a cropped
        // thigh. In automation it supplies local weights, never a new silhouette.
        foreach(var point in preview.vertices)
        {
            bool onBoundary=false;
            for(int e=0;e+1<original.boundaryEdgePairs.Length;e+=2)
                if(DistancePointToSegment(point,original.vertices[original.boundaryEdgePairs[e]],original.vertices[original.boundaryEdgePairs[e+1]])<.01f)
                {onBoundary=true;break;}
            if(onBoundary)continue;
            if(!TryFindContainingTriangle(original.triangles,original.vertices,point,
                out _,out _,out _,out _,out _,out _,out _,out _))continue;
            if(TryTransferWeightFromSource(point,original,true,out var old) && GetSpriteBoneWeight(old,hips)>0f)
                InsertOtherRegionPoint(staged,point,true);
        }
        for(int i=0;i<staged.vertices.Length;i++)
        {
            if(!TryTransferWeightFromSource(staged.vertices[i],original,true,out var old))continue;
            float oldHips=GetSpriteBoneWeight(old,hips);
            if(oldHips<=0f)continue;
            if(!TryTransferWeightFromSource(staged.vertices[i],preview,true,out var projected) ||
                !TryRemapProjectorWeight(projected,preview,staged,out projected,out _))continue;
            float nextHips=Mathf.Min(oldHips,GetSpriteBoneWeight(projected,hips));
            var values=new Dictionary<int,float>();
            Add(old.boneIndex0,old.weight0);Add(old.boneIndex1,old.weight1);
            Add(old.boneIndex2,old.weight2);Add(old.boneIndex3,old.weight3);
            AccumulateWeightChannel(values,hips,nextHips);
            AccumulateWeightChannel(values,leg,GetSpriteBoneWeight(old,leg)+oldHips-nextHips);
            staged.boneWeights[i]=BuildVertexFixWeight(values);
            void Add(int bone,float weight){if(bone!=hips && bone!=leg)AccumulateWeightChannel(values,bone,weight);}
        }
        var contour=new List<Vector2>(16);
        if(!TryGetJointV2AttachedGuidePoints(ReanimatorJointV2Defaults.SpecialJointDistance,
            out var center,out _,out _,out _,contour))return false;
        var target=GetJointV2InterpolatedBaseTarget(center,ReanimatorJointV2Defaults.BoneBaseOffset);
        var matrices=CaptureJointV2SkinMatrices(original);
        RegisterUndo("Apply Local Hip Joint");
        CommitOtherRegion(source.layer,source,staged);
        MoveJointV2DynamicBoneBasePreservingTipAndChildren(target);
        RestoreJointV2SkinningAfterBaseMove(source,matrices);
        error=null;return true;
    }
}
#endif
