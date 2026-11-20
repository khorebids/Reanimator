using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.Rendering;

// Independent, short-lived needle clusters. The sprite mesh is never modified.
// Anchors follow the skinned silhouette; each cluster samples its own surface color.
internal sealed class ReaniIKJaggedOutline
{
    private sealed class Shape
    {
        public SpriteRenderer renderer; public SpriteSkin skin; public Sprite sprite;
        public Vector2[] vertices, uv; public BoneWeight[] weights; public Matrix4x4[] bind;
        public readonly List<(int a, int b, int inside)> edges = new List<(int, int, int)>();
        public readonly List<int> indices = new List<int>();
        public Vector3[] world; public Material material;
    }
    private readonly struct Edge
    {
        public readonly Shape shape; public readonly int a, b, inside; public readonly float start, length;
        public Edge(Shape s, int a, int b, int inside, float start, float length)
        { shape = s; this.a = a; this.b = b; this.inside = inside; this.start = start; this.length = length; }
    }
    private readonly List<Shape> shapes = new List<Shape>();
    private readonly List<Edge> edges = new List<Edge>();
    private readonly List<Vector3> vertices = new List<Vector3>(), barycentric = new List<Vector3>();
    private readonly List<Vector2> uv = new List<Vector2>();
    private readonly List<Vector2> innerUV = new List<Vector2>();
    private readonly List<Vector3> inward = new List<Vector3>();
    private GameObject owner; private Mesh mesh;
    private ReaniIKSurfaceMaps surfaceMaps;
    private float redness;
    private Color rednessColor;
    private float contrast=1f,desaturation;
    public void SetSurface(ReaniIKSurfaceMaps maps, float amount, Color color, float contrast=1f, float desaturation=0f)
    { surfaceMaps = maps; redness = amount; rednessColor = color;this.contrast=Mathf.Clamp(contrast,-1,3);this.desaturation=Mathf.Clamp01(desaturation); }
    public int ActiveGroups { get; private set; }
    public void Draw(Transform root, float amount, float width, int groupCount, float speed, float time, float darkness, float peakWidth = 1f, float colorDepth = 3f, ReaniIKSurfaceEffects.TremorMode mode = ReaniIKSurfaceEffects.TremorMode.RandomBursts)
    {
        if (!owner) Build(root);
        if (!owner) return;
        vertices.Clear(); uv.Clear(); innerUV.Clear(); inward.Clear(); barycentric.Clear(); edges.Clear(); ActiveGroups = 0;
        float characterHeight = 0, perimeter = 0;
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (var s in shapes)
        {
            s.indices.Clear();
            var selection = surfaceMaps ? surfaceMaps.Find(s.sprite) : null;
            if (selection != null && !selection.effectsEnabled) continue;
            if (!s.renderer || !s.renderer.enabled || !s.renderer.gameObject.activeInHierarchy) continue;
            characterHeight = Mathf.Max(characterHeight, root.InverseTransformVector(Vector3.up * s.renderer.bounds.size.y).magnitude);
            for (int i = 0; i < s.vertices.Length; i++)
            {
                s.world[i] = Skin(s, i, root);
                float y = root.TransformPoint(s.world[i]).y;
                bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
            }
            foreach (var e in s.edges)
            {
                float length = Vector3.Distance(s.world[e.a], s.world[e.b]);
                if (length < 1e-6f) continue;
                edges.Add(new Edge(s, e.a, e.b, e.inside, perimeter, length)); perimeter += length;
            }
            s.material.SetColor("_Color", s.renderer.color);
            s.material.SetFloat("_Darkness", darkness);
            s.material.SetFloat("_Contrast",contrast);
            s.material.SetFloat("_Desaturation",desaturation);
            s.material.SetFloat("_ColorDepthPixels", Mathf.Max(0, colorDepth));
            // Read authoritative sprite-mask data, not a transient arm material's
            // property sheet (which can be empty after shader replacement/reload).
            var surface = surfaceMaps ? surfaceMaps.Find(s.sprite) : null;
            bool hasMap = surface != null && surface.normalAndBlush;
            s.material.SetFloat("_Redness", redness);
            s.material.SetColor("_RedColor", rednessColor);
            s.material.SetTexture("_SurfaceMap", hasMap ? surface.normalAndBlush : Texture2D.blackTexture);
            s.material.SetFloat("_HasMap", hasMap ? 1 : 0);
        }
        width *= Mathf.Max(characterHeight, .001f);
        groupCount = Mathf.RoundToInt(Mathf.Clamp01(amount) * Mathf.Clamp(groupCount, 1, ReaniIKSurfaceEffects.MaximumTremorGroups));
        if (perimeter > 0 && amount > 0)
            for (int group = 0; group < groupCount; group++)
            {
                // Independent phases avoid a synchronized ring or traveling wave.
                float clock = time * Mathf.Max(1, speed) * Mathf.Lerp(.65f, 1.15f, Random01(group * 71 + 3)) + Random01(group * 31 + 5);
                int burst = Mathf.FloorToInt(clock); float phase = Mathf.Repeat(clock, 1);
                float envelope = Mathf.Pow(Mathf.Sin(phase * Mathf.PI), 1.4f);
                bool waves = mode == ReaniIKSurfaceEffects.TremorMode.RisingWaves;
                if (!waves && envelope < .025f) continue;
                // Wave anchors stay fixed so groups do not jump as a wave passes.
                float seed = group * 193 + (waves ? 0 : burst * 937);
                // One sparse anchor per stratum, relocated on the next burst.
                float distance = (group + .15f + Random01(seed + 1) * .7f) / groupCount * perimeter;
                Edge edge = edges[edges.Count - 1];
                foreach (var candidate in edges) if (distance <= candidate.start + candidate.length) { edge = candidate; break; }
                var s = edge.shape;
                Vector3 a = s.world[edge.a], b = s.world[edge.b], inside = s.world[edge.inside];
                Vector3 tangent = (b - a).normalized;
                Vector3 normal = new Vector3(-tangent.y, tangent.x, 0);
                if (Vector3.Dot(normal, inside - (a + b) * .5f) > 0) normal = -normal;
                float t = Mathf.Clamp01((distance - edge.start) / edge.length);
                Vector3 anchor = Vector3.Lerp(a, b, t);
                if (waves)
                {
                    float level = Mathf.InverseLerp(bottom, top, root.TransformPoint(anchor).y);
                    envelope = RisingWaveEnvelope(time, speed, level);
                    if (envelope < .025f) continue;
                }
                Vector2 baseUV = Vector2.Lerp(s.uv[edge.a], s.uv[edge.b], t);
                Vector2 targetUV = Vector2.Lerp(baseUV, s.uv[edge.inside], .4f);
                Vector3 inwardDelta = (inside - anchor) * .4f;
                float height = width * envelope * Mathf.Lerp(2.5f, 6f, Random01(seed + 11));
                int needles = Random01(seed + 23) < .35f ? 2 : 3;
                for (int needle = 0; needle < needles; needle++)
                {
                    float side = needle == 0 ? 0 : needle == 1 ? -1 : 1;
                    float h = height * (needle == 0 ? 1 : Mathf.Lerp(.24f, .55f, Random01(seed + needle * 13)));
                    Vector3 center = anchor + tangent * (side * height * Mathf.Lerp(.12f, .25f, Random01(seed + 9)));
                    float halfBase = h * Mathf.Lerp(.055f, .095f, Random01(seed + needle * 17 + 6)) * Mathf.Clamp(peakWidth, .1f, 10f);
                    Vector3 tip = center + normal * h + tangent * (h * Mathf.Lerp(-.42f, .42f, Random01(seed + needle * 37 + 8)));
                    // Slight overlap hides the join beneath the original silhouette.
                    center -= normal * width * .15f;
                    int index = vertices.Count;
                    vertices.Add(center - tangent * halfBase); vertices.Add(tip); vertices.Add(center + tangent * halfBase);
                    uv.Add(baseUV); uv.Add(baseUV); uv.Add(baseUV);
                    innerUV.Add(targetUV); innerUV.Add(targetUV); innerUV.Add(targetUV);
                    inward.Add(inwardDelta); inward.Add(inwardDelta); inward.Add(inwardDelta);
                    barycentric.Add(Vector3.right); barycentric.Add(Vector3.up); barycentric.Add(Vector3.forward);
                    s.indices.Add(index); s.indices.Add(index + 1); s.indices.Add(index + 2);
                }
                ActiveGroups++;
            }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1, barycentric);
        mesh.SetUVs(2, innerUV); mesh.SetUVs(3, inward);
        mesh.subMeshCount = shapes.Count;
        for (int i = 0; i < shapes.Count; i++) mesh.SetTriangles(shapes[i].indices, i);
        mesh.RecalculateBounds();
    }
    private static float RisingWaveEnvelope(float time, float frequency, float height)
    {
        // Higher points reach the same phase later: a coherent upward pulse.
        float phase = (time * Mathf.Max(1, frequency) - Mathf.Clamp01(height) * .75f) * Mathf.PI * 2;
        return Mathf.Pow(Mathf.Max(0, Mathf.Cos(phase)), 4);
    }
    private void Build(Transform root)
    {
        var shader = Shader.Find("Reanimator/Tremor Outline"); if (!shader) return;
        owner = new GameObject("Tremor outline") { hideFlags = HideFlags.HideAndDontSave, layer = root.gameObject.layer };
        owner.transform.SetParent(root, false);
        mesh = new Mesh { name = "Sparse surface tremor clusters", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        owner.AddComponent<MeshFilter>().sharedMesh = mesh;
        var draw = owner.AddComponent<MeshRenderer>(); int order = int.MaxValue;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!renderer.sprite || renderer.name.EndsWith("_back", StringComparison.OrdinalIgnoreCase)) continue;
            if (renderer.sortingOrder < order) { order = renderer.sortingOrder; draw.sortingLayerID = renderer.sortingLayerID; }
            string name = renderer.sprite.name.ToLowerInvariant();
            if (!(name == "body" || name == "face" || name == "head" || name == "neck" || name.StartsWith("l_arm") || name.StartsWith("r_arm") || name.StartsWith("l_leg") || name.StartsWith("r_leg"))) continue;
            var s = new Shape { renderer = renderer, sprite = renderer.sprite, skin = renderer.GetComponent<SpriteSkin>(), vertices = renderer.sprite.vertices, uv = renderer.sprite.uv };
            s.world = new Vector3[s.vertices.Length]; s.bind = renderer.sprite.GetBindPoses().ToArray();
            if (renderer.sprite.HasVertexAttribute(VertexAttribute.BlendWeight)) s.weights = renderer.sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight).ToArray();
            s.material = new Material(shader) { name = "Surface-colored tremor", hideFlags = HideFlags.HideAndDontSave };
            s.material.SetTexture("_MainTex", renderer.sprite.texture);
            ReaniIKSurfaceMaps.GetUVTransform(renderer.sprite, out var x, out var y);
            s.material.SetVector("_SurfaceUVX", x); s.material.SetVector("_SurfaceUVY", y);
            var boundary = new Dictionary<(int, int), (int a, int b, int other, int count)>();
            var triangles = renderer.sprite.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3) for (int j = 0; j < 3; j++)
            {
                int a = triangles[i + j], b = triangles[i + (j + 1) % 3], c = triangles[i + (j + 2) % 3];
                var key = (Mathf.Min(a, b), Mathf.Max(a, b));
                boundary[key] = boundary.TryGetValue(key, out var old) ? (a, b, c, old.count + 1) : (a, b, c, 1);
            }
            foreach (var edge in boundary.Values) if (edge.count == 1) s.edges.Add((edge.a, edge.b, edge.other));
            shapes.Add(s);
        }
        draw.sortingOrder = order == int.MaxValue ? 0 : order - 1;
        var materials = new Material[shapes.Count]; for (int i = 0; i < materials.Length; i++) materials[i] = shapes[i].material;
        draw.sharedMaterials = materials;
    }
    private static Vector3 Skin(Shape s, int i, Transform root)
    {
        Vector3 p = s.vertices[i];
        if (s.skin && s.skin.enabled && s.weights != null && i < s.weights.Length && s.bind.Length > 0)
        {
            var w = s.weights[i]; Vector3 sum = Vector3.zero; float total = 0;
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            if (total > 0) p = s.renderer.transform.InverseTransformPoint(sum / total);
            void Add(int bone, float weight)
            {
                if (weight <= 0 || bone < 0 || bone >= s.bind.Length || bone >= s.skin.boneTransforms.Length || !s.skin.boneTransforms[bone]) return;
                sum += s.skin.boneTransforms[bone].TransformPoint(s.bind[bone].MultiplyPoint3x4(s.vertices[i])) * weight; total += weight;
            }
        }
        if (s.renderer.flipX) p.x *= -1; if (s.renderer.flipY) p.y *= -1;
        return root.InverseTransformPoint(s.renderer.transform.TransformPoint(p));
    }
    private static float Random01(float seed) => Mathf.Repeat(Mathf.Sin(seed * 12.9898f + 78.233f) * 43758.5453f, 1);
    public void Release()
    {
        foreach (var s in shapes) ReaniIKSurfaceEffects.Destroy(s.material);
        ReaniIKSurfaceEffects.Destroy(owner); ReaniIKSurfaceEffects.Destroy(mesh);
        owner = null; mesh = null; shapes.Clear(); ActiveGroups = 0;
    }
}
