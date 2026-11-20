#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

public partial class PsdLayerPreviewRenderer
{
    private Vector2[] SideAssImportedBoundary(LayerInfo layer)
    {
        var sprite=AssetDatabase.LoadAllAssetsAtPath(cachedPath).OfType<Sprite>().FirstOrDefault(s=>s.name==layer.name);
        if(!sprite)return System.Array.Empty<Vector2>();
        var scale=new Vector2(layer.sourceRect.width/sprite.rect.width,layer.sourceRect.height/sprite.rect.height);
        var vertices=sprite.vertices.Select(p=>PsdCoordinateSpace.LayerLocalToDocumentPoint(layer,Vector2.Scale(p*sprite.pixelsPerUnit+sprite.pivot,scale))).ToArray();
        BuildEdgePairs(sprite.triangles.Select(i=>(int)i).ToArray(),vertices.Length,out _,out var edges);
        var geometry=new LayerVertexData{vertices=vertices,triangles=sprite.triangles.Select(i=>(int)i).ToArray(),boundaryEdgePairs=edges};
        if(!TryResolveFullBoobOuterBoundaryLoop(geometry,out var loop))return System.Array.Empty<Vector2>();
        return loop.Select(i=>vertices[i]).ToArray();
    }
    private static Vector2[] SideAssReplaceBoundaryArc(Vector2[] boundary,Vector2[] arc)
    {
        if(arc.Length<2)return boundary;
        int start=Enumerable.Range(0,boundary.Length).OrderBy(i=>(boundary[i]-arc[0]).sqrMagnitude).First();
        int end=Enumerable.Range(0,boundary.Length).OrderBy(i=>(boundary[i]-arc[arc.Length-1]).sqrMagnitude).First();
        if(start==end)return boundary;
        var first=Walk(1);var second=Walk(-1);
        int direction=Score(first)<=Score(second)?1:-1;
        var result=arc.ToList();int index=(end+direction+boundary.Length)%boundary.Length;
        while(index!=start){result.Add(boundary[index]);index=(index+direction+boundary.Length)%boundary.Length;}
        return result.ToArray();
        List<Vector2> Walk(int step)
        {var points=new List<Vector2>();int index=start;for(int i=0;i<=boundary.Length;i++){points.Add(boundary[index]);if(index==end)break;index=(index+step+boundary.Length)%boundary.Length;}return points;}
        float Score(List<Vector2> path)
        {float sum=0;foreach(var p in path){SideAssArcDistance(p,arc,out var distance,out _);sum+=distance;}return sum/path.Count;}
    }
    private static float SideAssArcLength(IReadOnlyList<Vector2> points)
    {float length=0;for(int i=0;i+1<points.Count;i++)length+=Vector2.Distance(points[i],points[i+1]);return length;}
    private static Vector2[] SideAssExtendArc(IReadOnlyList<Vector2> boundary,Vector2[] arc,Vector2 center,out float prefixLength,out float suffixLength,bool extendEnd=true)
    {
        prefixLength=suffixLength=0;
        if(arc.Length<2)return arc;
        int start=Enumerable.Range(0,boundary.Count).OrderBy(i=>(boundary[i]-arc[0]).sqrMagnitude).First();
        int second=Enumerable.Range(0,boundary.Count).OrderBy(i=>(boundary[i]-arc[1]).sqrMagnitude).First();
        int end=Enumerable.Range(0,boundary.Count).OrderBy(i=>(boundary[i]-arc[arc.Length-1]).sqrMagnitude).First();
        int direction=(start+1)%boundary.Count==second?1:-1;
        var before=Walk(start,-direction);var after=extendEnd?Walk(end,direction):new List<Vector2>{arc[arc.Length-1]};
        prefixLength=SideAssArcLength(before);suffixLength=SideAssArcLength(after);
        before.Reverse();before.RemoveAt(before.Count-1);
        before.AddRange(arc);before.AddRange(after.Skip(1));return before.ToArray();
        List<Vector2> Walk(int index,int step)
        {
            var result=new List<Vector2>{boundary[index]};
            float target=Vector2.Distance(boundary[index],center)*SideAssEndDegrees*Mathf.Deg2Rad;
            float traveled=0,nextSample=target/8;
            if(target<.0001f)return result;
            for(int count=0;count<boundary.Count;count++)
            {
                var from=boundary[index];index=(index+step+boundary.Count)%boundary.Count;var to=boundary[index];
                float length=Vector2.Distance(from,to);
                while(nextSample<=Mathf.Min(target,traveled+length)+.00001f)
                {
                    var p=Vector2.Lerp(from,to,Mathf.Clamp01((nextSample-traveled)/Mathf.Max(.0001f,length)));
                    if(Vector2.Distance(result[result.Count-1],p)>.0001f)result.Add(p);
                    nextSample+=target/8;
                }
                if(traveled+length>=target)return result;
                if(Vector2.Distance(result[result.Count-1],to)>.0001f)result.Add(to);
                traveled+=length;
            }
            return result;        }
    }
    private static float SideAssExtendedArcAngle(IReadOnlyList<Vector2> arc,float progress,float prefix,float suffix)
    {
        float total=SideAssArcLength(arc),distance=progress*total,core=Mathf.Max(.0001f,total-prefix-suffix);
        float angle=distance<prefix ? -SideAssEndDegrees*(1-distance/Mathf.Max(.0001f,prefix)) :
            distance>prefix+core ? 90+SideAssEndDegrees*(distance-prefix-core)/Mathf.Max(.0001f,suffix) : 90*(distance-prefix)/core;
        return angle*Mathf.Deg2Rad;
    }
    private static Vector2[] SideAssInsertContourPoints(IReadOnlyList<Vector2> boundary,IReadOnlyList<Vector2> points)
    {
        var result=new List<Vector2>();
        for(int i=0;i<boundary.Count;i++)
        {
            var a=boundary[i];var b=boundary[(i+1)%boundary.Count];result.Add(a);
            foreach(var point in points.Where(p=>DistancePointToSegment(p,a,b)<.001f && Vector2.Distance(p,a)>.001f && Vector2.Distance(p,b)>.001f).OrderBy(p=>(p-a).sqrMagnitude))
                if(Vector2.Distance(result[result.Count-1],point)>.001f)result.Add(point);
        }
        return result.ToArray();
    }
    private static Vector2[] SideAssSourceArc(IReadOnlyList<Vector2> boundary,IReadOnlyList<Vector2> stroke,Vector2 first,Vector2 last)
    {
        int start=Enumerable.Range(0,boundary.Count).OrderBy(i=>(boundary[i]-first).sqrMagnitude).First();
        int end=Enumerable.Range(0,boundary.Count).OrderBy(i=>(boundary[i]-last).sqrMagnitude).First();
        if(start==end)return System.Array.Empty<Vector2>();
        float endpointTolerance=Vector2.Distance(first,last)*.25f;
        if(Vector2.Distance(boundary[start],first)>endpointTolerance || Vector2.Distance(boundary[end],last)>endpointTolerance)return System.Array.Empty<Vector2>();
        var forward=Walk(1);var backward=Walk(-1);
        return Score(forward)<=Score(backward)?forward:backward;
        Vector2[] Walk(int direction)
        {
            var points=new List<Vector2>();int index=start;
            for(int steps=0;steps<=boundary.Count;steps++)
            {points.Add(boundary[index]);if(index==end)break;index=(index+direction+boundary.Count)%boundary.Count;}
            return points.ToArray();
        }
        float Score(Vector2[] path)
        {
            float score=0,length=0;
            for(int i=0;i+1<path.Length;i++)
            {float segment=Vector2.Distance(path[i],path[i+1]);SideAssArcDistance((path[i]+path[i+1])*.5f,stroke,out var distance,out _);score+=distance*segment;length+=segment;}
            return score/Mathf.Max(.0001f,length);
        }
    }
    private static void SideAssArcDistance(Vector2 p,IReadOnlyList<Vector2> arc,out float distance,out float progress)
    {
        distance=float.PositiveInfinity;progress=0;float total=0,nearest=0;
        for(int i=0;i+1<arc.Count;i++)
        {
            var d=arc[i+1]-arc[i];float length=d.magnitude;
            float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(p-arc[i],d)/d.sqrMagnitude):0;
            float candidate=Vector2.Distance(p,arc[i]+d*t);
            if(candidate<distance){distance=candidate;nearest=total+t*length;}
            total+=length;
        }
        progress=total>0?nearest/total:0;
    }
}
#endif
