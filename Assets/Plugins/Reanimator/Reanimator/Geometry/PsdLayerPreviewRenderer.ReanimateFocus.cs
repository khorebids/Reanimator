#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private readonly Dictionary<LayerInfo,Rect> reanimateFocusBounds = new();
    private string reanimateFocusRevision;

    internal bool TryGetReanimateFocusLocalBounds(LayerInfo layer,out Rect bounds)
    {
        if(reanimateFocusRevision!=cachedAssetDependencyHash)
        {reanimateFocusBounds.Clear();reanimateFocusRevision=cachedAssetDependencyHash;}
        if(reanimateFocusBounds.TryGetValue(layer,out bounds))return bounds.width>0 && bounds.height>0;
        var texture=layer.texture ? layer.texture : GetPreviewAtlasTexture();
        bounds=default;
        if(!texture || !TryGetTexturePixels(texture,out var pixels))return false;
        var rect=PsdCoordinateSpace.LayerTextureRect(layer,new Vector2(texture.width,texture.height));
        if(TryGetLargestVisibleIslandBounds(pixels,texture.width,texture.height,rect,out var pixelBounds))
            bounds=new Rect(pixelBounds.x/rect.width*layer.sourceRect.width,pixelBounds.y/rect.height*layer.sourceRect.height,
                pixelBounds.width/rect.width*layer.sourceRect.width,pixelBounds.height/rect.height*layer.sourceRect.height);
        reanimateFocusBounds[layer]=bounds;
        return bounds.width>0 && bounds.height>0;
    }

    // Focus on the main artwork, excluding transparent atlas padding and tiny
    // disconnected edge/extrusion remnants. This never changes sprite geometry.
    internal static bool TryGetLargestVisibleIslandBounds(Color32[] pixels,int textureWidth,int textureHeight,Rect rect,out Rect bounds)
    {
        bounds=default;
        int x0=Mathf.Clamp(Mathf.FloorToInt(rect.xMin),0,textureWidth),y0=Mathf.Clamp(Mathf.FloorToInt(rect.yMin),0,textureHeight);
        int width=Mathf.Clamp(Mathf.CeilToInt(rect.xMax),0,textureWidth)-x0,height=Mathf.Clamp(Mathf.CeilToInt(rect.yMax),0,textureHeight)-y0;
        if(width<=0 || height<=0)return false;
        var visited=new bool[width*height];var queue=new int[visited.Length];int largest=0;
        for(int seed=0;seed<visited.Length;seed++)
        {
            if(visited[seed])continue;
            visited[seed]=true;
            if(pixels[(y0+seed/width)*textureWidth+x0+seed%width].a<=10)continue;
            int head=0,tail=1,minX=seed%width,maxX=minX,minY=seed/width,maxY=minY;queue[0]=seed;
            while(head<tail)
            {
                int p=queue[head++],x=p%width,y=p/width;
                minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
                if(x>0)Add(p-1);if(x+1<width)Add(p+1);if(y>0)Add(p-width);if(y+1<height)Add(p+width);
            }
            if(tail>largest){largest=tail;bounds=new Rect(minX+x0-rect.xMin,minY+y0-rect.yMin,maxX-minX+1,maxY-minY+1);}
            void Add(int p)
            {
                if(visited[p])return;visited[p]=true;
                if(pixels[(y0+p/width)*textureWidth+x0+p%width].a>10)queue[tail++]=p;
            }
        }
        return largest>0;
    }

    public static string VerifyPaddedReanimateFocus()
    {
        var pixels=new Color32[200*200];
        for(int y=130;y<180;y++)for(int x=20;x<60;x++)pixels[y*200+x]=new Color32(255,255,255,255);
        pixels[199]=new Color32(255,255,255,255);
        if(!TryGetLargestVisibleIslandBounds(pixels,200,200,new Rect(0,0,200,200),out var bounds) || bounds!=new Rect(20,130,40,50))
            throw new System.Exception("Reanimate focused transparent padding or a disconnected atlas remnant.");
        return "PASS: Reanimate focuses visible artwork inside a padded sprite, ignoring detached atlas remnants.";
    }
}
#endif
