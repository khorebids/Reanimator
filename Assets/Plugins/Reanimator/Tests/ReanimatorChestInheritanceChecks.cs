#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyBatchedTriangleOptimization()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/FullBoobsCrossedDiagonal.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        float Area(){float total=0;for(int i=0;i<data.triangles.Length;i+=3)total+=Mathf.Abs(FullBoobCross(data.vertices[data.triangles[i+1]]-data.vertices[data.triangles[i]],data.vertices[data.triangles[i+2]]-data.vertices[data.triangles[i]]))*.5f;return total;}
        float before=Area();
        var vertices=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var weights=data.boneWeights.ToArray();
        OptimizeLayerTriangleQuality(data);
        BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
        if(!SideAssJoinIsClosed(data,exterior) || Mathf.Abs(Area()-before)>Mathf.Max(.01f,before*.0001f))
            throw new Exception("Batched flips changed mesh surface or silhouette.");
        if(!vertices.SequenceEqual(data.vertices) || !uv.SequenceEqual(data.vertexUvs) || !weights.SequenceEqual(data.boneWeights))
            throw new Exception("Batched flips changed vertex attributes.");
        var first=data.triangles.ToArray();OptimizeLayerTriangleQuality(data);
        if(!first.SequenceEqual(data.triangles))throw new Exception("Batched optimization did not converge.");
        return "PASS: batched optimization conserves surface, exterior and attributes and converges on captured production mesh.";
    }
    public static string VerifyCrossedDiagonalChestCoverage()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/FullBoobsCrossedDiagonal.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        var retained=sample["retained"].Select(v=>(int)v).ToArray();
        if(!SideAssJoinIsClosed(data,exterior))throw new Exception("Captured chest mesh is open.");
        for(int i=0;i<retained.Length;i+=3)
            if(!FullBoobRetainedTriangleCovered(data,retained[i],retained[i+1],retained[i+2]))
                throw new Exception("Captured outside surface is missing at face "+i/3);
        var quad=new LayerVertexData{vertices=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,3,1,2,3}};
        if(!FullBoobRetainedTriangleCovered(quad,0,1,2))throw new Exception("Changed diagonal rejected complete coverage.");
        quad.triangles=new[]{0,1,3};
        if(FullBoobRetainedTriangleCovered(quad,0,1,2))throw new Exception("Changed diagonal accepted a missing surface.");
        return "PASS: captured chest and changed diagonal retain full surface; missing coverage rejected.";
    }
    public static string VerifyRotatedShoulderChestJoin()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/FullBoobsRotatedShoulderJoin.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        var retained=sample["retained"].Select(v=>(int)v).ToArray();
        if(!SideAssJoinIsClosed(data,exterior))throw new Exception("Rotated shoulder chest mesh is open.");
        var keys=new HashSet<ulong>();
        for(int i=0;i<data.triangles.Length;i+=3)keys.Add(MakeFullBoobTriangleKey(data.triangles[i],data.triangles[i+1],data.triangles[i+2]));
        for(int i=0;i<retained.Length;i+=3)
            if(!keys.Contains(MakeFullBoobTriangleKey(retained[i],retained[i+1],retained[i+2])) &&
               !FullBoobRetainedTriangleCovered(data,retained[i],retained[i+1],retained[i+2]))
                throw new Exception("Retained chest surface was lost.");
        var simple=new LayerVertexData{vertices=new[]{Vector2.zero,Vector2.right,Vector2.up},triangles=new[]{0,1,2}};
        if(!FullBoobRetainedTriangleCovered(simple,0,1,2))throw new Exception("Coverage rejected a complete face.");
        simple.vertices=new[]{Vector2.zero,Vector2.right,Vector2.up,new Vector2(.5f,0),new Vector2(0,.5f)};
        simple.triangles=new[]{0,3,4};
        if(FullBoobRetainedTriangleCovered(simple,0,1,2))throw new Exception("Coverage accepted a missing surface.");
        return "PASS: rotated shoulder chest remains closed and retains outside surface; missing surface is rejected.";
    }
    public static string VerifyClippedChestJoin()
    {
        var sample=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/FullBoobsClippedJoin.json"));
        var data=JsonUtility.FromJson<LayerVertexData>((string)sample["dataJson"]);
        var vertices=data.vertices.ToArray();var uv=data.vertexUvs.ToArray();var weights=data.boneWeights.ToArray();
        var exterior=sample["exterior"].Select(v=>(int)v).ToArray();
        var retained=sample["retained"].Select(v=>(int)v).ToArray();
        var local=sample["local"].Select(v=>(int)v).ToList();
        var edges=new List<Vector2Int>();
        for(int i=0;i<retained.Length;i+=3)for(int k=0;k<3;k++)edges.Add(new Vector2Int(retained[i+k],retained[i+(k+1)%3]));
        if(!new PsdLayerPreviewRenderer().RepairSideAssJoin(data,exterior,edges,local) || !SideAssJoinIsClosed(data,exterior))
            throw new Exception("Clipped chest join did not close.");
        var keys=new HashSet<string>();
        string Key(int a,int b,int c)=>string.Join(",",new[]{a,b,c}.OrderBy(v=>v));
        for(int i=0;i<data.triangles.Length;i+=3)keys.Add(Key(data.triangles[i],data.triangles[i+1],data.triangles[i+2]));
        for(int i=0;i<retained.Length;i+=3)
            if(!keys.Contains(Key(retained[i],retained[i+1],retained[i+2])))throw new Exception("Outside triangle was lost.");
        for(int i=0;i<vertices.Length;i++)
            if(data.vertices[i]!=vertices[i] || data.vertexUvs[i]!=uv[i] || !data.boneWeights[i].Equals(weights[i]))
                throw new Exception("Join recovery changed existing vertex attributes.");
        return "PASS: captured chest join closes; all outside triangles, vertices, UVs and weights preserved.";
    }

    public string VerifyChestInheritanceOnClothing(string clothingName)
    {
        var clothing=layerInfos.First(l=>l.name==clothingName);
        var body=layerInfos.First(l=>l.name=="body");
        if(!TryGetLayerVertexData(clothing,out var data))throw new Exception("Clothing mesh missing.");
        var original=CloneProjectorLayerData(data);
        var draft=CaptureReanimateChestDraft();
        if(draft==null)throw new Exception("Chest draft missing.");
        var left=MapFullBoobContourToBase(clothing,draft.LeftContour);
        var right=MapFullBoobContourToBase(clothing,draft.RightContour);
        try
        {
            for(int repeat=0;repeat<2;repeat++)
            {
                if(!TryInheritReanimateChest(clothing,body,draft.LeftContour,draft.RightContour,out var error))throw new Exception(error);
                if(!SideAssJoinIsClosed(data,original.boundaryEdgePairs))throw new Exception("Clothing silhouette changed.");
                for(int i=0;i<original.vertices.Length;i++)
                {
                    if(data.vertices[i]!=original.vertices[i] || data.vertexUvs[i]!=original.vertexUvs[i])throw new Exception("Existing clothing vertex/UV changed.");
                    if(!ContainsFaceMouthPoint(original.vertices[i],left) && !ContainsFaceMouthPoint(original.vertices[i],right) &&
                        !data.boneWeights[i].Equals(original.boneWeights[i]))throw new Exception("Weights outside chest changed.");
                }
            }
            return "PASS: clothing inheritance twice; silhouette, existing vertices/UVs and outside weights preserved.";
        }
        finally { CommitOtherRegion(clothing,data,original); }
    }
}
#endif
