#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static bool TryInsertElbowVertexExact(LayerVertexData data,Vector2 point,out int index)
    {
        index=-1;
        if(!TryFindContainingTriangle(data.triangles,data.vertices,point,out var triangle,out var a,out var b,out var c,
            out var ab,out var bc,out var ca,out _))return false;
        float nearest=Mathf.Min(ab,Mathf.Min(bc,ca));int edgeA=-1,edgeB=-1;
        if(nearest<=.01f)
        {
            edgeA=a;edgeB=b;if(bc<=ab && bc<=ca){edgeA=b;edgeB=c;}else if(ca<=ab && ca<=bc){edgeA=c;edgeB=a;}
            var start=data.vertices[edgeA];var delta=data.vertices[edgeB]-start;
            point=start+delta*Mathf.Clamp01(Vector2.Dot(point-start,delta)/Mathf.Max(delta.sqrMagnitude,.000001f));
        }
        for(int i=0;i<data.vertices.Length;i++)if((data.vertices[i]-point).sqrMagnitude<=.0001f){index=i;return true;}
        index=data.vertices.Length;var next=new List<int>(data.triangles.Length+6);bool split=false;
        for(int i=0;i<data.triangles.Length;i+=3)
        {
            if(edgeA<0 && i==triangle)
            {next.Add(a);next.Add(b);next.Add(index);next.Add(b);next.Add(c);next.Add(index);next.Add(c);next.Add(a);next.Add(index);split=true;continue;}
            bool edgeSplit=false;
            for(int e=0;edgeA>=0 && e<3;e++)
            {
                int x=data.triangles[i+e],y=data.triangles[i+(e+1)%3],z=data.triangles[i+(e+2)%3];
                if(!((x==edgeA && y==edgeB)||(x==edgeB && y==edgeA)))continue;
                next.Add(x);next.Add(index);next.Add(z);next.Add(index);next.Add(y);next.Add(z);edgeSplit=split=true;break;
            }
            if(!edgeSplit){next.Add(data.triangles[i]);next.Add(data.triangles[i+1]);next.Add(data.triangles[i+2]);}
        }
        if(!split){index=-1;return false;}
        var pa=data.vertices[a];var pb=data.vertices[b];var pc=data.vertices[c];float area=FullBoobCross(pb-pa,pc-pa);
        if(Mathf.Abs(area)<.000001f){index=-1;return false;}
        float wb=FullBoobCross(point-pa,pc-pa)/area,wc=FullBoobCross(pb-pa,point-pa)/area,wa=1f-wb-wc;
        var uv=data.vertexUvs[a]*wa+data.vertexUvs[b]*wb+data.vertexUvs[c]*wc;
        var weight=BlendBoneWeights(data.boneWeights[a],wa,data.boneWeights[b],wb,data.boneWeights[c],wc,null);
        Array.Resize(ref data.vertices,index+1);data.vertices[index]=point;
        Array.Resize(ref data.vertexUvs,index+1);data.vertexUvs[index]=uv;
        Array.Resize(ref data.boneWeights,index+1);data.boneWeights[index]=weight;
        data.triangles=next.ToArray();BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
        data.lastPoseVersion=-1;data.deformedVertices=null;data.skinMatrices=null;return true;
    }
    private static void RemoveDuplicateElbowTriangles(LayerVertexData data)
    {
        // Repeated geometry passes can leave the same face with opposite winding.
        // Keep one face; never remove or renumber source vertices, UVs or weights.
        var unique=new HashSet<ulong>();var triangles=new List<int>(data.triangles.Length);
        for(int i=0;i+2<data.triangles.Length;i+=3)
        {
            int a=data.triangles[i],b=data.triangles[i+1],c=data.triangles[i+2];
            if(a==b || b==c || a==c || !unique.Add(MakeFullBoobTriangleKey(a,b,c)))continue;
            triangles.Add(a);triangles.Add(b);triangles.Add(c);
        }
        data.triangles=triangles.ToArray();
        BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
    }
}
#endif