#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyCapturedShoulderClothingCoverage()
    {
        var fixture=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/ShoulderClothingCoverage.json"));
        var renderer=new PsdLayerPreviewRenderer();
        foreach(var bone in fixture["bones"])renderer.bones.Add(JsonUtility.FromJson<BoneInfo>((string)bone));
        var target=JsonUtility.FromJson<LayerVertexData>((string)fixture["target"]);
        var sources=fixture["sources"].Select(v=>JsonUtility.FromJson<LayerVertexData>((string)v)).ToArray();
        foreach(var data in sources.Concat(new[]{target}))
        {renderer.layerInfos.Add(data.layer);renderer.vertexDataByLayerSelectionKey[BuildSelectionKey(data.layer)]=data;}
        var original=CloneProjectorLayerData(target);
        var start=new Vector2((float)fixture["start"][0],(float)fixture["start"][1]);
        var axis=new Vector2((float)fixture["axis"][0],(float)fixture["axis"][1]);float length=(float)fixture["length"];
        var uncovered=original.boundaryEdgePairs.Distinct().Where(i=>Vector2.Dot(original.vertices[i]-start,axis)<=length &&
            !sources.Any(s=>IsTargetVertexInsideProjectorSource(original,i,s))).ToArray();
        if(uncovered.Length==0)throw new Exception("Fixture does not exercise uncovered clothing edges.");
        BoneWeight[] first=null;int changed=0;
        for(int repeat=0;repeat<2;repeat++)
        {
            if(!renderer.TryInheritReanimateShoulders(target.layer,out var error))throw new Exception(error);
            if(!SideAssJoinIsClosed(target,original.boundaryEdgePairs))throw new Exception("Clothing exterior changed.");
            for(int i=0;i<original.vertices.Length;i++)
                if(original.vertices[i]!=target.vertices[i]||original.vertexUvs[i]!=target.vertexUvs[i])throw new Exception("Original vertex/UV changed.");
            foreach(int i in uncovered)
            {
                if(!TrySampleNearestShoulderSurface(target.vertices[i],sources,out var source,out var sample) ||
                    !renderer.TryRemapProjectorWeight(sample,source,target,out var expected,out _) ||
                    !AreBoneWeightsApproximatelyEqual(expected,target.boneWeights[i]))throw new Exception("Uncovered clothing vertex did not inherit nearest surface weights.");
                if(!original.boneWeights[i].Equals(target.boneWeights[i]))changed++;
            }
            if(first!=null && !first.SequenceEqual(target.boneWeights))throw new Exception("Repeated shoulder inheritance changed weights.");
            first=target.boneWeights.ToArray();
        }
        if(changed==0)throw new Exception("Fixture repair did not change any stale shoulder weights.");
        SaveShoulderCoveragePoseComparison(renderer,original,target,sources);
        return $"PASS: all {uncovered.Length} captured uncovered shoulder-edge vertices inherit surface weights twice; silhouette, original vertices/UVs and repeat weights preserved.";
    }
    private static void SaveShoulderCoveragePoseComparison(PsdLayerPreviewRenderer renderer,LayerVertexData before,LayerVertexData after,LayerVertexData[] sources)
    {
        var rig=UnityEngine.Object.FindObjectsByType<ReaniIK>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault();
        if(!rig)return;
        var transforms=rig.GetComponentsInChildren<Transform>(true).Where(t=>!t.GetComponent<SpriteRenderer>()).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var helpers=transforms.Values.Where(t=>t.name.EndsWith("_shoulder_helper",StringComparison.Ordinal)).OrderBy(t=>t.position.x).ToArray();
        if(helpers.Length<2)return;
        Vector2 center=helpers[0].position;float radius=Mathf.Max(.1f,Vector2.Distance(helpers[0].position,helpers[1].position)*.55f);
        var image=new Texture2D(1200,600,TextureFormat.RGBA32,false);
        image.SetPixels(Enumerable.Repeat(new Color(.08f,.08f,.08f,1),1200*600).ToArray());
        foreach(var source in sources){Draw(source,0,new Color(.65f,.65f,.65f));Draw(source,600,new Color(.65f,.65f,.65f));}
        Draw(before,0,Color.cyan);Draw(after,600,Color.green);
        image.Apply();System.IO.File.WriteAllBytes("Temp/ShoulderClothingPoseComparison.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        void Draw(LayerVertexData data,int column,Color color)
        {
            var points=new Vector2[data.vertices.Length];
            for(int i=0;i<points.Length;i++)
            {
                var input=data.usesSpriteBindPoses?DocumentToSpriteSkinPosition(data,data.vertices[i]):data.vertices[i];
                var w=data.boneWeights[i];Vector3 p=Vector3.zero;
                Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                points[i]=((Vector2)p-center)*(270/radius)+new Vector2(column+300,300);
                void Add(int slot,float weight)
                {
                    if(weight<=0 || slot<0 || slot>=data.spriteBoneToRendererBone.Length)return;
                    var name=renderer.bones[data.spriteBoneToRendererBone[slot]].name;
                    if(!transforms.TryGetValue(name,out var bone))return;
                    var local=data.bindBoneInverseMatrices[slot].MultiplyPoint3x4(input);
                    if(!data.usesSpriteBindPoses)local/=data.pixelsPerUnit;
                    p+=bone.TransformPoint(local)*weight;
                }
            }
            for(int i=0;i+1<data.boundaryEdgePairs.Length;i+=2)
            {
                var a=points[data.boundaryEdgePairs[i]];var b=points[data.boundaryEdgePairs[i+1]];
                int steps=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a,b)),1,3000);
                for(int j=0;j<=steps;j++)
                {var p=Vector2.Lerp(a,b,j/(float)steps);int x=Mathf.RoundToInt(p.x),y=Mathf.RoundToInt(p.y);if(x>=column && x<column+600 && y>=0 && y<600)image.SetPixel(x,y,color);}
            }
        }
    }
    public string CaptureShoulderClothingCoverage(string name)
    {
        if(vertexDataByLayerSelectionKey.Count==0)
        {
            BuildVertices(cachedPath);
            foreach(var patch in autoGeometryPatches.Where(p=>IsAssetPathMatch(p.assetPath,cachedPath)).ToArray())TryBuildLayerDataFromAutoGeometryPatch(patch,out _);
        }
        var target=layerInfos.First(l=>l.name==name);TryGetLayerVertexData(target,out var data);
        var sources=BuildProjectorSourceList(layerInfos.Where(l=>new[]{"l_arm","r_arm","body"}.Contains(l.name)).Select(BuildSelectionKey).ToArray(),BuildSelectionKey(target),BuildKey(target.name,target.layerId),true,data);
        TryFindBoneIndexByName("shoulders",out var shoulder);var start=GetBoneBasePoint(bones[shoulder]);Vector2 axis=bones[shoulder].worldRotation*Vector3.right;
        var band=data.boundaryEdgePairs.Distinct().Where(i=>Vector2.Dot(data.vertices[i]-start,axis)<=bones[shoulder].length).ToArray();
        int missing=band.Count(i=>!sources.Any(s=>IsTargetVertexInsideProjectorSource(data,i,s)));
        System.IO.File.WriteAllText("Assets/Plugins/Reanimator/Tests/Fixtures/ShoulderClothingCoverage.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{
            target=JsonUtility.ToJson(data),sources=sources.Select(JsonUtility.ToJson).ToArray(),bones=bones.Select(JsonUtility.ToJson).ToArray(),start=new[]{start.x,start.y},axis=new[]{axis.x,axis.y},length=bones[shoulder].length}));
        return $"Shoulder-band clothing boundary: {band.Length} vertices, {missing} outside all source surfaces.";
    }
    public string VerifyShoulderClothingInheritance(string name)
    {
        var copy=new PsdLayerPreviewRenderer();copy.cachedDocumentSize=cachedDocumentSize;
        copy.layerInfos.AddRange(layerInfos);
        foreach(var bone in bones)copy.bones.Add(JsonUtility.FromJson<BoneInfo>(JsonUtility.ToJson(bone)));
        copy.RecomputeBoneWorldTransforms();
        foreach(var entry in vertexDataByLayerSelectionKey)copy.vertexDataByLayerSelectionKey[entry.Key]=CloneProjectorLayerData(entry.Value);
        var target=copy.layerInfos.First(l=>l.name==name);
        copy.TryGetLayerVertexData(target,out var data);var before=CloneProjectorLayerData(data);
        for(int repeat=0;repeat<2;repeat++)
        {
            if(!copy.TryInheritReanimateShoulders(target,out var error))throw new Exception(error);
            if(!SideAssJoinIsClosed(data,before.boundaryEdgePairs))throw new Exception("Shoulder inheritance changed clothing silhouette.");
            for(int i=0;i<before.vertices.Length;i++)
                if(data.vertices[i]!=before.vertices[i] || data.vertexUvs[i]!=before.vertexUvs[i])throw new Exception("Shoulder inheritance changed original vertices/UVs.");
            bool shoulders=false,helper=false;
            for(int slot=0;slot<data.spriteBoneToRendererBone.Length;slot++)
            {
                string bone=copy.bones[data.spriteBoneToRendererBone[slot]].name;
                if(!data.boneWeights.Any(w=>GetSpriteBoneWeight(w,slot)>.0001f))continue;
                if(bone=="shoulders")shoulders=true;
                if(bone.IndexOf("_shoulder_helper",StringComparison.OrdinalIgnoreCase)>=0)helper=true;
            }
            if(!shoulders || !helper)throw new Exception("Clothing did not inherit shoulders and arm weld helpers.");
        }
        return "PASS: actual clothing inherits shoulder and weld-helper weights twice while its silhouette, original vertices and UVs remain exact.";
    }
}
#endif
