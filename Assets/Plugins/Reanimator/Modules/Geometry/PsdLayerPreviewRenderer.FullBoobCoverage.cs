#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    // A planar repair can split a retained face at a new support intersection.
    // Clip replacement faces to the retained face before measuring coverage:
    // a valid new diagonal can cross the old diagonal. Existing vertex
    // attributes stay intact and exterior/manifold validation is separate.
    private static bool FullBoobRetainedTriangleCovered(LayerVertexData data,int a,int b,int c)
    {
        var x=data.vertices[a];var y=data.vertices[b];var z=data.vertices[c];
        float area=Mathf.Abs(FullBoobCross(y-x,z-x))*.5f;
        // Constraint repair welds points within 0.001 document pixels.
        // A face thinner than that has no resolvable surface to retain.
        float longest=Mathf.Max((y-x).magnitude,Mathf.Max((z-y).magnitude,(x-z).magnitude));
        if(area*2f<=longest*.001f)return true;
        float sign=Mathf.Sign(FullBoobCross(y-x,z-x)),covered=0;
        var clip=new[]{x,y,z};
        for(int i=0;i<data.triangles.Length;i+=3)
        {
            var p=data.vertices[data.triangles[i]];var q=data.vertices[data.triangles[i+1]];var r=data.vertices[data.triangles[i+2]];
            if(Mathf.Max(p.x,Mathf.Max(q.x,r.x))<Mathf.Min(x.x,Mathf.Min(y.x,z.x)) ||
               Mathf.Min(p.x,Mathf.Min(q.x,r.x))>Mathf.Max(x.x,Mathf.Max(y.x,z.x)) ||
               Mathf.Max(p.y,Mathf.Max(q.y,r.y))<Mathf.Min(x.y,Mathf.Min(y.y,z.y)) ||
               Mathf.Min(p.y,Mathf.Min(q.y,r.y))>Mathf.Max(x.y,Mathf.Max(y.y,z.y)))continue;
            var polygon=new List<Vector2>{p,q,r};
            for(int edge=0;edge<3 && polygon.Count>0;edge++)
            {
                var start=clip[edge];var delta=clip[(edge+1)%3]-start;
                var next=new List<Vector2>();var previous=polygon[polygon.Count-1];
                float previousDistance=sign*FullBoobCross(delta,previous-start);
                foreach(var current in polygon)
                {
                    float distance=sign*FullBoobCross(delta,current-start);
                    if((distance>=0)!=(previousDistance>=0))
                        next.Add(Vector2.LerpUnclamped(previous,current,previousDistance/(previousDistance-distance)));
                    if(distance>=0)next.Add(current);
                    previous=current;previousDistance=distance;
                }
                polygon=next;
            }
            float intersection=0;
            for(int k=1;k+1<polygon.Count;k++)intersection+=Mathf.Abs(FullBoobCross(polygon[k]-polygon[0],polygon[k+1]-polygon[0]))*.5f;
            if(intersection<.00001f)continue;
            covered+=intersection;
        }
        return Mathf.Abs(covered-area)<=Mathf.Max(.01f,area*.0001f);
    }
}
#endif
