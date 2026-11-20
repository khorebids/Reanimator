using UnityEngine;

public sealed partial class ReaniIKSurfaceEffects
{
    private void TrimTrail(Drop d)
    {
        float length = 0, limit = Mathf.Max(.001f, trailMaxLength);
        var scale = new Vector2(d.surface.aspect, 1);
        for (int i = d.path.Count - 1; i > 0; i--)
        {
            float segment = Vector2.Scale(d.path[i].position - d.path[i-1].position, scale).magnitude;
            if (length + segment > limit)
            {
                float t = (limit - length) / Mathf.Max(segment, .000001f);
                d.path[i-1] = new TrailPoint(Vector2.Lerp(d.path[i].position, d.path[i-1].position, t), Mathf.Lerp(d.path[i].age, d.path[i-1].age, t));
                if (i > 1) d.path.RemoveRange(0, i-1);
                break;
            }
            length += segment;
        }
        while (d.path.Count > 1 && d.age - d.path[1].age > Mathf.Max(.1f, trailLifetime)) d.path.RemoveAt(0);
    }

    private void DrawTexturedTrail(Drop d)
    {
        if (!trailTexture) return;
        var s = d.surface;
        var scale = new Vector2(s.aspect, 1);
        float distance = 0, halfWidth = d.radius * 1.5f;
        for (int i = d.path.Count - 1; i > 0; i--)
        {
            Vector2 a = Vector2.Scale(d.path[i].position, scale), b = Vector2.Scale(d.path[i-1].position, scale);
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < .000001f) continue;
            Vector2 normal = new Vector2(-delta.y, delta.x) / length;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x,b.x)-halfWidth)/s.aspect*s.trail.width));
            int x1 = Mathf.Min(s.trail.width-1, Mathf.CeilToInt((Mathf.Max(a.x,b.x)+halfWidth)/s.aspect*s.trail.width));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y,b.y)-halfWidth)*s.trail.height));
            int y1 = Mathf.Min(s.trail.height-1, Mathf.CeilToInt((Mathf.Max(a.y,b.y)+halfWidth)*s.trail.height));
            for (int y=y0; y<=y1; y++) for (int x=x0; x<=x1; x++)
            {
                Vector2 p = new Vector2((x+.5f)/s.trail.width*s.aspect, (y+.5f)/s.trail.height);
                float t = Vector2.Dot(p-a,delta)/(length*length);
                if (t < 0 || t > 1) continue;
                float v = Vector2.Dot(p-a,normal)/(halfWidth*2)+.5f;
                if (v < 0 || v > 1) continue;
                float u = (distance + t*length)/Mathf.Max(.001f,trailMaxLength);
                float age = d.age - Mathf.Lerp(d.path[i].age,d.path[i-1].age,t);
                float fade = Mathf.Clamp01(1-age/Mathf.Max(.1f,trailLifetime)) * Mathf.Clamp01((1-u)*5);
                Color texel = trailTexture.GetPixelBilinear(u,v);
                float wet = Mathf.Max(texel.r,Mathf.Max(texel.g,texel.b))*texel.a*fade*d.opacity;
                int k=y*s.trail.width+x;
                s.wet[k].r=Mathf.Max(s.wet[k].r,wet);
            }
            distance += length;
        }
    }
}
