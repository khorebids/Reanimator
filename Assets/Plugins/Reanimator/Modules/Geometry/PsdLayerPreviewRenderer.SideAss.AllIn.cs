#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

public partial class PsdLayerPreviewRenderer
{
    internal int SideAssAllInTargets
    {
        get{LoadSideAss();return sideAssFile.enabledTargets&3;}
        set
        {
            LoadSideAss();sideAssFile.enabledTargets=value&3;
            if(SideAssPath==null)return;
            Directory.CreateDirectory(Path.GetDirectoryName(SideAssPath));
            File.WriteAllText(SideAssPath,UnityEngine.JsonUtility.ToJson(sideAssFile,true));
        }
    }

    internal string[] ApplySelectedSideAss()
    {
        int targets=SideAssAllInTargets;var result=new List<string>();
        if(targets==0)return result.ToArray();
        TryGetSelectedLayer(out var previous);int previousBone=selectedBoneIndex;
        try
        {
            for(int side=0;side<2;side++)
            {
                if((targets&(1<<side))==0)continue;
                string name=side==0?"l_leg":"r_leg";
                var layer=layerInfos.Find(l=>string.Equals(l.name,name,StringComparison.OrdinalIgnoreCase));
                if(layer==null || !TryFindBoneIndexByName(name,out var bone))throw new InvalidOperationException("Side Ass: selected leg sprite or bone is missing: "+name);
                SelectLayer(layer);selectedBoneIndex=bone;
                if(!QuickSideAss(out var error))throw new InvalidOperationException(name+": "+error);
                result.Add(name);
            }
        }
        finally{SelectLayer(previous);selectedBoneIndex=previousBone;}
        return result.ToArray();
    }
}
#endif
