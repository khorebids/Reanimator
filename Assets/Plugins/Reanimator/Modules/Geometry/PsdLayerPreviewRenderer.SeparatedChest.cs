#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    // 0: none, 1: contours on body, 2: dedicated breast sprites.
    [SerializeField] private int reanimateChestMode=1;
    internal int ReanimateChestMode=>reanimateChestMode;
    internal void SetReanimateChestMode(int mode)
    {
        mode=Mathf.Clamp(mode,0,2);if(mode==reanimateChestMode)return;
        RegisterUndo("Chest mode");CancelBoobV2Interaction();reanimateChestMode=mode;
        if(mode==0)ClearBoobV2();
    }
    internal LayerInfo FindSeparatedChestSprite(int side)=>layerInfos.Find(l=>l!=null &&
        NormalizeAutoWeightName(l.name)==(side==0?"lboob":"rboob"));
    internal string GetReanimateChestModeError()
    {
        if(reanimateChestMode!=2)return null;
        var missing=new List<string>();
        if(FindSeparatedChestSprite(0)==null)missing.Add("l_boob");
        if(FindSeparatedChestSprite(1)==null)missing.Add("r_boob");
        return missing.Count==0?null:"Separated chest requires sprites: "+string.Join(", ",missing)+".";
    }
    internal HashSet<LayerInfo> GetSeparatedChestPreviewLayers()
    {
        if(reanimateChestMode!=2)return null;
        var result=new HashSet<LayerInfo>();
        for(int side=0;side<2;side++){var layer=FindSeparatedChestSprite(side);if(layer!=null)result.Add(layer);}
        return result;
    }
    internal LayerInfo ResolveReanimateChestInputLayer(LayerInfo body,int item)=>
        reanimateChestMode==2?FindSeparatedChestSprite(item==0||item==2?0:1):body;

    private bool TryApplySeparatedReanimateChest(out string error)
    {
        error=GetReanimateChestModeError();if(error!=null)return false;
        var left=FindSeparatedChestSprite(0);var right=FindSeparatedChestSprite(1);
        if(!TryPlaceFullBoobBones(left,boobV2TargetADoc,boobV2TargetBDoc,out error,right))return false;
        if(!TryFindFullBoobSpineRendererBone(out var spine)||!TryFindFullBoobRendererBones(out var first,out var second))
        {error="Separated chest bones are missing.";return false;}
        var a=MapNippleGuessToBase(left,boobV2TargetADoc);var b=MapNippleGuessToBase(right,boobV2TargetBDoc);
        bool direct=(GetBoneBasePoint(bones[first])-a).sqrMagnitude+(GetBoneBasePoint(bones[second])-b).sqrMagnitude<=
            (GetBoneBasePoint(bones[second])-a).sqrMagnitude+(GetBoneBasePoint(bones[first])-b).sqrMagnitude;
        var layers=new[]{left,right};var centers=new[]{a,b};var targets=new[]{direct?first:second,direct?second:first};
        var contours=new[]{boobV2ContourADoc,boobV2ContourBDoc};
        for(int side=0;side<2;side++)
        {
            if(!TryGetLayerVertexData(layers[side],out var data)){error=layers[side].name+": geometry missing.";return false;}
            var contour=MapFullBoobContourToBase(layers[side],contours[side]);
            if(!TryRebuildPairedContours(data,centers[side],centers[side],contour,Array.Empty<Vector2>(),
                    spine,targets[side],targets[side],false,out _,out error))return false;
        }
        RestorePoseAfterBoobApply();return true;
    }
    internal bool TryInheritReanimateChestDraft(LayerInfo target,LayerInfo body,ReanimateChestDraft draft,int side,out string error)
    {
        var source=draft.Mode==2?FindSeparatedChestSprite(side):body;
        return TryInheritReanimateChest(target,source,draft.LeftContour,draft.RightContour,out error,side);
    }
}
#endif
