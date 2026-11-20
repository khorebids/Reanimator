#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static bool TryNormalizeSideAssTriangles(LayerVertexData data)
    {
        try {data.triangles=ReanimatorMeshWinding.Normalize(data.triangles,out _);return true;}
        catch(System.InvalidOperationException){return false;}
    }
    internal static System.Action<string> SideAssJoinFailureProbe;
    [System.Serializable] private sealed class SideAssJoinDiagnostic
    { public string dataJson; public int[] exterior; public Vector2Int[] retained; public int[] local; }

    private int[] NormalizeSideAssExterior(LayerVertexData data, int[] exterior)
    {
        var degree=new Dictionary<int,int>();
        for(int i=0;i<exterior.Length;i++)degree[exterior[i]]=degree.TryGetValue(exterior[i],out var count)?count+1:1;
        if(degree.Values.All(count=>count==2))return exterior;
        var input=new List<Vector2Int>();
        for(int i=0;i<exterior.Length;i+=2)input.Add(new Vector2Int(exterior[i],exterior[i+1]));
        var graph=PlanarSideAssConstraints(data,input);
        var adjacent=new Dictionary<int,List<int>>();
        foreach(var edge in graph){Add(edge.x,edge.y);Add(edge.y,edge.x);}
        foreach(var pair in adjacent)pair.Value.Sort((a,b)=>Mathf.Atan2(data.vertices[a].y-data.vertices[pair.Key].y,data.vertices[a].x-data.vertices[pair.Key].x).CompareTo(Mathf.Atan2(data.vertices[b].y-data.vertices[pair.Key].y,data.vertices[b].x-data.vertices[pair.Key].x)));
        var visited=new HashSet<(int,int)>();List<int> outer=null;float largest=0;
        foreach(var edge in graph)foreach(bool reverse in new[]{false,true})
        {
            int start=reverse?edge.y:edge.x,next=reverse?edge.x:edge.y,from=start,to=next;
            if(visited.Contains((from,to)))continue;
            var face=new List<int>();bool closed=false;
            for(int guard=0;guard<=graph.Count*2;guard++)
            {
                if(!visited.Add((from,to)))break;
                face.Add(from);var neighbors=adjacent[to];int index=neighbors.IndexOf(from);
                int after=neighbors[(index-1+neighbors.Count)%neighbors.Count];from=to;to=after;
                if(from==start && to==next){closed=true;break;}
            }
            if(!closed || face.Count<3)continue;
            float area=0;
            for(int i=0;i<face.Count;i++)area+=FullBoobCross(data.vertices[face[i]],data.vertices[face[(i+1)%face.Count]]);
            // Clockwise faces are the outside of the planar arrangement. The
            // overlapping cap's internal edges must not be treated as silhouette.
            if(area<largest){largest=area;outer=face;}
        }
        return outer!=null?BuildBoundaryPairsFromLoop(outer):exterior;
        void Add(int a,int b){if(!adjacent.TryGetValue(a,out var neighbors))adjacent[a]=neighbors=new List<int>();if(!neighbors.Contains(b))neighbors.Add(b);}
    }
    private bool RepairSideAssJoin(LayerVertexData data,int[] exterior,List<Vector2Int> retained,List<int> local)
    {
        var required=new List<Vector2Int>();
        for(int i=0;i<local.Count;i+=3)for(int j=0;j<3;j++)required.Add(new Vector2Int(local[i+j],local[i+(j+1)%3]));
        foreach(bool keepHints in new[]{true,false})
        {
            var graph=new List<Vector2Int>(required);
            if(keepHints)graph.AddRange(retained);
            for(int i=0;i<exterior.Length;i+=2)graph.Add(new Vector2Int(exterior[i],exterior[i+1]));
            graph=PlanarSideAssConstraints(data,graph);
            if(ConnectSideAssGraph(data,graph,exterior))graph=PlanarSideAssConstraints(data,graph);
            var boundary=new List<int>();
            for(int i=0;i<exterior.Length;i+=2)
            {
                foreach(var edge in Split(exterior[i],exterior[i+1])){boundary.Add(edge.x);boundary.Add(edge.y);}
            }
            data.boundaryEdgePairs=boundary.ToArray();
            if(TriangulateSideAssFaces(data,graph,exterior))return true;
            bool triangulated=TryRetriangulateFullBoobsConstraintSet(data,graph,data.vertices.Length,null,true);
            if(!triangulated)continue;
            if(!TryNormalizeSideAssTriangles(data))continue;
            BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
            if(SideAssJoinIsClosed(data,exterior))return true;
        }
        // Intersecting old support edges can leave an inconsistent constraint
        // graph. Recover from the silhouette without changing vertex attributes.
        data.boundaryEdgePairs=exterior;
        if(TryRetriangulateKeepingVertexSet(data) || TryRetriangulateWithDirectFallback(data))
        {
            if(!TryNormalizeSideAssTriangles(data))return false;
            BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
            if(SideAssJoinIsClosed(data,exterior))return true;
        }
        SideAssJoinFailureProbe?.Invoke(JsonUtility.ToJson(new SideAssJoinDiagnostic{dataJson=JsonUtility.ToJson(data),exterior=exterior,retained=retained.ToArray(),local=local.ToArray()}));
        return false;
        IEnumerable<Vector2Int> Split(int a,int b)
        {
            var points=Enumerable.Range(0,data.vertices.Length).Where(v=>DistancePointToSegment(data.vertices[v],data.vertices[a],data.vertices[b])<.001f)
                .OrderBy(v=>(data.vertices[v]-data.vertices[a]).sqrMagnitude).ToArray();
            for(int i=0;i+1<points.Length;i++)if((data.vertices[points[i]]-data.vertices[points[i+1]]).sqrMagnitude>.000001f)yield return new Vector2Int(points[i],points[i+1]);
        }
    }
    private static bool ConnectSideAssGraph(LayerVertexData data,List<Vector2Int> graph,int[] exterior)
    {
        bool changed=false;
        for(int pass=0;pass<data.vertices.Length;pass++)
        {
            var parent=Enumerable.Range(0,data.vertices.Length).ToArray();var used=new HashSet<int>();
            foreach(var edge in graph){used.Add(edge.x);used.Add(edge.y);parent[Root(edge.x)]=Root(edge.y);}
            if(used.Select(Root).Distinct().Count()<=1)return changed;
            float best=float.PositiveInfinity;Vector2Int bridge=default;bool found=false;
            foreach(int a in used)foreach(int b in used)
            {
                if(a>=b || Root(a)==Root(b))continue;
                var x=data.vertices[a];var y=data.vertices[b];float distance=(x-y).sqrMagnitude;
                if(distance>=best || !Inside(Vector2.Lerp(x,y,.25f)) || !Inside(Vector2.Lerp(x,y,.5f)) || !Inside(Vector2.Lerp(x,y,.75f)))continue;
                bool blocked=false;
                foreach(var edge in graph)
                {
                    if(edge.x==a||edge.x==b||edge.y==a||edge.y==b)continue;
                    if(FullBoobSegmentsProperlyIntersect(x,y,data.vertices[edge.x],data.vertices[edge.y])){blocked=true;break;}
                }
                if(!blocked){best=distance;bridge=new Vector2Int(a,b);found=true;}
            }
            if(!found)return changed;
            graph.Add(bridge);changed=true;
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        }
        return changed;
        bool Inside(Vector2 p)
        {
            bool inside=false;
            for(int e=0;e<exterior.Length;e+=2)
            {var a=data.vertices[exterior[e]];var b=data.vertices[exterior[e+1]];if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}
            return inside;
        }
    }
    private List<Vector2Int> PlanarSideAssConstraints(LayerVertexData data,List<Vector2Int> input)
    {
        var vertices=data.vertices.ToList();var uv=data.vertexUvs.ToList();var weights=data.boneWeights.ToList();
        var canonical=new List<int>();
        for(int i=0;i<vertices.Count;i++)
        {int found=-1;for(int j=0;j<i;j++)if((vertices[j]-vertices[i]).sqrMagnitude<.000001f){found=canonical[j];break;}canonical.Add(found<0?i:found);}
        var edges=input.Select(e=>new Vector2Int(canonical[e.x],canonical[e.y])).Where(e=>e.x!=e.y)
            .Select(e=>new Vector2Int(Mathf.Min(e.x,e.y),Mathf.Max(e.x,e.y))).Distinct().ToArray();
        for(int i=0;i<edges.Length;i++)for(int j=i+1;j<edges.Length;j++)
        {
            Vector2 a=vertices[edges[i].x],ab=vertices[edges[i].y]-a,c=vertices[edges[j].x],cd=vertices[edges[j].y]-c;
            float divisor=FullBoobCross(ab,cd);if(Mathf.Abs(divisor)<.0000001f)continue;
            float t=FullBoobCross(c-a,cd)/divisor,u=FullBoobCross(c-a,ab)/divisor;
            if(t<=0||t>=1||u<=0||u>=1)continue;
            var point=a+ab*t;
            if(vertices.Any(p=>(p-point).sqrMagnitude<.000001f))continue;
            canonical.Add(vertices.Count);vertices.Add(point);uv.Add(Vector2.Lerp(uv[edges[i].x],uv[edges[i].y],t));
            weights.Add(InterpolateBoneWeight(weights[edges[i].x],weights[edges[i].y],t,data.spriteBoneToRendererBone.Length));
        }
        var result=new HashSet<Vector2Int>();
        foreach(var edge in edges)
        {
            var points=Enumerable.Range(0,vertices.Count).Where(v=>canonical[v]==v && DistancePointToSegment(vertices[v],vertices[edge.x],vertices[edge.y])<.001f)
                .OrderBy(v=>(vertices[v]-vertices[edge.x]).sqrMagnitude).ToArray();
            for(int i=0;i+1<points.Length;i++)if((vertices[points[i]]-vertices[points[i+1]]).sqrMagnitude>.000001f)
                result.Add(new Vector2Int(Mathf.Min(points[i],points[i+1]),Mathf.Max(points[i],points[i+1])));
        }
        data.vertices=vertices.ToArray();data.vertexUvs=uv.ToArray();data.boneWeights=weights.ToArray();return result.ToList();
    }
    private static bool TriangulateSideAssFaces(LayerVertexData data,List<Vector2Int> graph,int[] exterior)
    {
        // Treat internal closed cycles as mesh cells, not holes. Unity's generic
        // tessellator can omit alternating cells when old and new ring graphs meet.
        var adjacent=new Dictionary<int,List<int>>();
        foreach(var e in graph){Add(e.x,e.y);Add(e.y,e.x);}
        foreach(var pair in adjacent)pair.Value.Sort((a,b)=>Mathf.Atan2(data.vertices[a].y-data.vertices[pair.Key].y,data.vertices[a].x-data.vertices[pair.Key].x).CompareTo(Mathf.Atan2(data.vertices[b].y-data.vertices[pair.Key].y,data.vertices[b].x-data.vertices[pair.Key].x)));
        var visited=new HashSet<(int,int)>();var triangles=new List<int>();
        foreach(var edge in graph)foreach(bool reverse in new[]{false,true})
        {
            int start=reverse?edge.y:edge.x,next=reverse?edge.x:edge.y;
            if(visited.Contains((start,next)))continue;
            int from=start,to=next;var face=new List<int>();bool closed=false;
            for(int steps=0;steps<=graph.Count*2;steps++)
            {
                if(!visited.Add((from,to)))break;
                face.Add(from);var neighbors=adjacent[to];int index=neighbors.IndexOf(from);
                int after=neighbors[(index-1+neighbors.Count)%neighbors.Count];from=to;to=after;
                if(from==start && to==next){closed=true;break;}
            }
            if(!closed || face.Count<3)continue;
            float area=0;for(int i=0;i<face.Count;i++)area+=FullBoobCross(data.vertices[face[i]],data.vertices[face[(i+1)%face.Count]]);
            if(area<=.000001f)continue;
            var pending=new List<int>(face);int guard=0;
            while(pending.Count>3 && guard++<face.Count*face.Count)
            {
                bool clipped=false;
                for(int i=0;i<pending.Count;i++)
                {
                    int a=pending[(i-1+pending.Count)%pending.Count],b=pending[i],c=pending[(i+1)%pending.Count];
                    if(a==c || a==b || b==c){pending.RemoveAt(i);clipped=true;break;}
                    var x=data.vertices[a];var y=data.vertices[b];var z=data.vertices[c];
                    if(FullBoobCross(y-x,z-y)<=.000001f)continue;
                    bool blocked=false;
                    foreach(int v in pending)
                    {
                        if(v==a||v==b||v==c)continue;var p=data.vertices[v];
                        if(FullBoobCross(y-x,p-x)>=-.000001f && FullBoobCross(z-y,p-y)>=-.000001f && FullBoobCross(x-z,p-z)>=-.000001f){blocked=true;break;}
                    }
                    if(blocked)continue;
                    Emit(a,b,c);pending.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)return false;
            }
            if(pending.Count==3)Emit(pending[0],pending[1],pending[2]);
        }
        data.triangles=triangles.ToArray();
        if(!TryNormalizeSideAssTriangles(data))return false;
        BuildEdgePairs(data.triangles,data.vertices.Length,out data.allEdgePairs,out data.boundaryEdgePairs);
        return SideAssJoinIsClosed(data,exterior);
        void Add(int a,int b){if(!adjacent.TryGetValue(a,out var list))adjacent[a]=list=new List<int>();if(!list.Contains(b))list.Add(b);}
        void Emit(int a,int b,int c)
        {
            Vector2 p=(data.vertices[a]+data.vertices[b]+data.vertices[c])/3;
            bool inside=false;
            for(int e=0;e<exterior.Length;e+=2)
            {
                var x=data.vertices[exterior[e]];var y=data.vertices[exterior[e+1]];
                if((x.y>p.y)!=(y.y>p.y) && p.x<(y.x-x.x)*(p.y-x.y)/(y.y-x.y)+x.x)inside=!inside;
            }
            if(inside){triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        }
    }
}
#endif
