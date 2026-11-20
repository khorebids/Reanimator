#if UNITY_EDITOR
using System;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyMouthTriangleQuality()
    {
        var json=System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/MouthTriangleQuality.json");
        var data=JsonUtility.FromJson<LayerVertexData>(json);
        var original=JsonUtility.FromJson<LayerVertexData>(json);
        double Area(LayerVertexData d)
        {
            double total=0;
            for(int i=0;i<d.triangles.Length;i+=3)
                total+=Math.Abs(FullBoobCross(d.vertices[d.triangles[i+1]]-d.vertices[d.triangles[i]],d.vertices[d.triangles[i+2]]-d.vertices[d.triangles[i]]))*.5;
            return total;
        }
        double Quality(LayerVertexData d)
        {
            double total=0;
            for(int i=0;i<d.triangles.Length;i+=3)
            {
                var a=d.vertices[d.triangles[i]];var b=d.vertices[d.triangles[i+1]];var c=d.vertices[d.triangles[i+2]];
                total+=TriangleMinAngle(a,b,c);
            }
            return total;
        }
        var area=Area(data);var quality=Quality(data);
        for(int pass=0;pass<2;pass++)
        {
            OptimizeLayerTriangleQuality(data);
            if(Math.Abs(Area(data)-area)>area*.00001 || data.triangles.Length!=original.triangles.Length)
                throw new Exception("Mouth optimization changed the surface.");
            if(!SideAssJoinIsClosed(data,original.boundaryEdgePairs))throw new Exception("Mouth optimization changed the exterior.");
            var triangles=data.triangles;data.triangles=original.triangles;
            if(JsonUtility.ToJson(data)!=JsonUtility.ToJson(original))throw new Exception("Mouth optimization changed vertex attributes.");
            data.triangles=triangles;
        }
        if(Quality(data)<=quality)throw new Exception("Captured mouth fans were not improved.");
        return "PASS: captured face topology improved; repeated optimization preserves surface, exterior and every vertex attribute.";
    }
}
#endif
