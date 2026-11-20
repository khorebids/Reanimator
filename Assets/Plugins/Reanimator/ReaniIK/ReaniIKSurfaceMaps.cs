using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Reanimator/Surface Maps")]
public sealed class ReaniIKSurfaceMaps : ScriptableObject
{
    [Serializable] public sealed class Entry
    {
        public Sprite sprite;
        [Tooltip("Linear RGBA: RGB = tangent-space surface normal; A = painted redness.")]
        public Texture2D normalAndBlush;
        public Texture2D coverage;
        public bool sweat = true;
        public bool effectsEnabled = true;
    }
    public List<Entry> sprites = new List<Entry>();
    [Range(0, 3)] public float normalStrength = 1f;
    public static Vector3 DecodeNormal(Color encoded, float strength)
    {
        var n = new Vector3((encoded.r * 2 - 1) * Mathf.Max(0, strength), (encoded.g * 2 - 1) * Mathf.Max(0, strength), Mathf.Max(.0001f, encoded.b * 2 - 1));
        return n.normalized;
    }
    [HideInInspector] public int selectionVersion;
    public Entry Find(Sprite sprite) => sprites.Find(entry => entry.sprite == sprite);

    public static bool GetUVTransform(Sprite sprite, out Vector4 x, out Vector4 y)
    {
        x = new Vector4(1, 0, 0, 0); y = new Vector4(0, 1, 0, 0);
        if (!sprite) return false;
        var uv = sprite.uv; var vertices = sprite.vertices; var triangles = sprite.triangles;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            var u = uv[b] - uv[a]; var v = uv[c] - uv[a]; float det = u.x * v.y - u.y * v.x;
            if (Mathf.Abs(det) < 1e-10f) continue;
            Vector2 p = (vertices[b] - vertices[a]) * sprite.pixelsPerUnit;
            Vector2 q = (vertices[c] - vertices[a]) * sprite.pixelsPerUnit;
            var dx = (p * v.y - q * u.y) / det; var dy = (q * u.x - p * v.x) / det;
            var offset = vertices[a] * sprite.pixelsPerUnit + sprite.pivot - dx * uv[a].x - dy * uv[a].y;
            x = new Vector4(dx.x, dy.x, offset.x, 0) / sprite.rect.width;
            y = new Vector4(dx.y, dy.y, offset.y, 0) / sprite.rect.height;
            return true;
        }
        return false;
    }
}
