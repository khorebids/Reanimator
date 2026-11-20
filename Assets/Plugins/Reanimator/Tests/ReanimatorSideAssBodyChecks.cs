#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string VerifySideAssBodyContinuation()
    {
        if(vertexDataByLayerSelectionKey.Count==0)
        {
            BuildVertices(cachedPath);
            foreach(var patch in autoGeometryPatches.Where(p=>IsAssetPathMatch(p.assetPath,cachedPath)).ToArray())TryBuildLayerDataFromAutoGeometryPatch(patch,out _);
        }
        LoadSideAss();
        var copy=new PsdLayerPreviewRenderer();copy.cachedDocumentSize=cachedDocumentSize;
        copy.layerInfos.AddRange(layerInfos);
        foreach(var bone in bones)copy.bones.Add(JsonUtility.FromJson<BoneInfo>(JsonUtility.ToJson(bone)));
        copy.RecomputeBoneWorldTransforms();
        foreach(var entry in vertexDataByLayerSelectionKey)copy.vertexDataByLayerSelectionKey[entry.Key]=CloneProjectorLayerData(entry.Value);
        copy.sideAssFile=JsonUtility.FromJson<SideAssFile>(JsonUtility.ToJson(sideAssFile));
        copy.sideAssCurves=JsonUtility.FromJson<SideAssCurves>(JsonUtility.ToJson(GetSideAssCurves()));
        copy.sideAssCurvesDirty=true;copy.SideAssBodyReach=1;
        var recipe=copy.sideAssFile.regions.First(r=>r.manualFan);
        var leg=copy.layerInfos.First(l=>BuildSelectionKey(l)==recipe.layer);
        var body=copy.layerInfos.First(l=>NormalizeAutoWeightName(l.name) is "body" or "torso");
        copy.TryGetLayerVertexData(body,out var data);var original=CloneProjectorLayerData(data);
        copy.InheritSideAssOnBody(leg);
        var first=CloneProjectorLayerData(data);
        copy.TryGetLayerVertexData(leg,out var legData);
        SaveSideAssBodyComparison(copy,original,first,legData,recipe);
        int outside=recipe.bodyWeights.Count(w=>!IsPointInsideFullBoobContour(w.point,recipe.contour)&&SideAssDistance(w.point,recipe.contour)>.01f);
        if(outside==0)throw new Exception("No continuation beyond original leg contour.");
        for(int i=0;i<original.vertices.Length;i++)
        {
            if(data.vertices[i]!=original.vertices[i]||data.vertexUvs[i]!=original.vertexUvs[i])throw new Exception("Original torso vertices/UVs changed.");
            if(!recipe.bodyWeights.Any(w=>(w.point-data.vertices[i]).sqrMagnitude<.0001f)&&!AreBoneWeightsApproximatelyEqual(data.boneWeights[i],original.boneWeights[i]))throw new Exception("Unrelated torso weights changed.");
        }
        copy.InheritSideAssOnBody(leg);
        if(!first.vertices.SequenceEqual(data.vertices)||!first.triangles.SequenceEqual(data.triangles)||!first.vertexUvs.SequenceEqual(data.vertexUvs)||!first.boneWeights.SequenceEqual(data.boneWeights))throw new Exception($"Body continuation accumulates: vertices {first.vertices.Length}/{data.vertices.Length}, triangles equal {first.triangles.SequenceEqual(data.triangles)}, UV equal {first.vertexUvs.SequenceEqual(data.vertexUvs)}, weights differing {first.boneWeights.Zip(data.boneWeights,(a,b)=>!a.Equals(b)).Count(v=>v)} / approximately {first.boneWeights.Zip(data.boneWeights,(a,b)=>!AreBoneWeightsApproximatelyEqual(a,b)).Count(v=>v)}.");
        var saved=recipe.bodyWeights.ToArray();
        copy.SideAssBodyReach=0;copy.InheritSideAssOnBody(leg);
        foreach(var w in saved)
        {
            int i=Array.FindIndex(data.vertices,p=>(p-w.point).sqrMagnitude<.0001f);
            if(i<0||!AreBoneWeightsApproximatelyEqual(data.boneWeights[i],w.original))throw new Exception("Disabling continuation did not restore torso weights.");
        }
        if(copy.bones.Count!=bones.Count)throw new Exception("Continuation added unexpected helper bones.");
        return $"PASS: {saved.Length} torso vertices, {outside} beyond leg contour; exact original vertices/UVs, local weights, repeat stability, disable restoration and same two helpers.";
    }
    private static void SaveSideAssBodyComparison(PsdLayerPreviewRenderer renderer,LayerVertexData before,LayerVertexData after,LayerVertexData leg,SideAssRecipe recipe)
    {
        var rig=UnityEngine.Object.FindObjectsByType<ReaniIK>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault();
        if(!rig)return;
        var transforms=rig.GetComponentsInChildren<Transform>(true).Where(t=>!t.GetComponent<SpriteRenderer>()).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        if(!transforms.TryGetValue(recipe.volume,out var helper))return;
        Vector2 center=helper.position;float radius=recipe.radius/leg.pixelsPerUnit*1.3f;
        const int size=600;
        var texture=new Texture2D(size*2,size,TextureFormat.RGBA32,false);
        texture.SetPixels(Enumerable.Repeat(new Color(.07f,.07f,.07f,1),size*size*2).ToArray());
        Draw(leg,0,Color.gray);Draw(leg,size,Color.gray);Draw(before,0,Color.cyan);Draw(after,size,Color.green);
        texture.Apply();System.IO.File.WriteAllBytes("Temp/SideAssBodyComparison.png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        void Draw(LayerVertexData data,int offset,Color color)
        {
            var points=new Vector2[data.vertices.Length];
            for(int i=0;i<points.Length;i++)
            {
                var input=data.usesSpriteBindPoses?DocumentToSpriteSkinPosition(data,data.vertices[i]):data.vertices[i];
                var w=data.boneWeights[i];Vector3 p=Vector3.zero;
                Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                points[i]=((Vector2)p-center)*(270/radius)+new Vector2(offset+300,300);
                void Add(int slot,float weight)
                {
                    if(weight<=0||slot<0||slot>=data.spriteBoneToRendererBone.Length)return;
                    if(!transforms.TryGetValue(renderer.bones[data.spriteBoneToRendererBone[slot]].name,out var bone))return;
                    var local=data.bindBoneInverseMatrices[slot].MultiplyPoint3x4(input);
                    if(!data.usesSpriteBindPoses)local/=data.pixelsPerUnit;
                    p+=bone.TransformPoint(local)*weight;
                }
            }
            for(int e=0;e+1<data.boundaryEdgePairs.Length;e+=2)
            {
                var a=points[data.boundaryEdgePairs[e]];var b=points[data.boundaryEdgePairs[e+1]];
                int steps=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a,b)),1,3000);
                for(int k=0;k<=steps;k++)
                {
                    var p=Vector2.Lerp(a,b,k/(float)steps);int x=Mathf.RoundToInt(p.x),y=Mathf.RoundToInt(p.y);
                    if(x>=offset&&x<offset+size&&y>=0&&y<size)texture.SetPixel(x,y,color);
                }
            }
        }
    }
}
#endif
