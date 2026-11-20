#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public partial class PsdLayerPreviewRenderer
{
    private LayerInfo FindSplitArmLowerLayer(string upperName)
    {
        string normalized=NormalizeAutoWeightName(upperName);
        string side=normalized.StartsWith("l",StringComparison.OrdinalIgnoreCase)?"l":normalized.StartsWith("r",StringComparison.OrdinalIgnoreCase)?"r":null;
        if(side==null || normalized!=side+"arm")return null;
        return layerInfos.Find(layer=>layer!=null && layer.visible &&
            Array.Exists(new[]{side+"hand",side+"forearm",side+"arm1"},name=>NormalizeAutoWeightName(layer.name)==name));
    }
    private int[] RestrictSplitUpperArmBones(LayerVertexData data,int[] map)
    {
        if(FindSplitArmLowerLayer(data.layer.name)==null)return map;
        if(!TryFindBoneIndexByName(data.layer.name,out var upper))return map;
        var result=new List<int>();
        foreach(var bone in map)
            if(bone==upper || !IsBoneDescendant(bone,upper))result.Add(bone);
        return result.ToArray();
    }
}
#endif
