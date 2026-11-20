#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyDenseAssClothingProjection()
    {
        var raw=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/DenseAssClothing.json"));
        var r=new PsdLayerPreviewRenderer();
        foreach(var b in raw["bones"])r.bones.Add(JsonUtility.FromJson<BoneInfo>((string)b));
        r.RecomputeBoneWorldTransforms();
        var target=JsonUtility.FromJson<LayerVertexData>((string)raw["target"]);
        var source=JsonUtility.FromJson<LayerVertexData>((string)raw["source"]);
        foreach(var d in new[]{target,source}){r.layerInfos.Add(d.layer);r.vertexDataByLayerSelectionKey[BuildSelectionKey(d.layer)]=d;}
        var original=CloneProjectorLayerData(target);
        var contour=raw["contour"].Select(p=>new Vector2((float)p[0],(float)p[1])).ToArray();
        double Area(LayerVertexData d)
        {
            double total=0;
            for(int i=0;i<d.triangles.Length;i+=3)
                total+=Math.Abs(FullBoobCross(d.vertices[d.triangles[i+1]]-d.vertices[d.triangles[i]],d.vertices[d.triangles[i+2]]-d.vertices[d.triangles[i]]))*.5;
            return total;
        }
        double before=Area(target);
        for(int pass=0;pass<2;pass++)
        {
            if(!r.TryApplyReanimateProjectorRegion(target.layer,new[]{source.layer},true,contour,out var error))throw new Exception(error);
            if(target.triangles.Length<original.triangles.Length || Math.Abs(Area(target)-before)>before*.00001)
                throw new Exception("Dense Ass projection erased clothing triangles or changed surface area.");
            for(int i=0;i<original.vertices.Length;i++)
                if(target.vertices[i]!=original.vertices[i] || target.vertexUvs[i]!=original.vertexUvs[i])
                    throw new Exception("Dense Ass projection moved existing clothing vertices or UVs.");
            if(!SideAssJoinIsClosed(target,original.boundaryEdgePairs))throw new Exception("Dense Ass projection changed clothing exterior.");
        }
        return "PASS: captured second-side Ass projection twice preserves clothing surface, existing vertices/UVs and closed exterior.";
    }
}
#endif
