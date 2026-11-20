using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.Rendering;

[Serializable]
public sealed partial class ReaniIKSurfaceEffects
{
    public enum TremorMode { RandomBursts, RisingWaves }
    public TremorMode tremorMode;
    public ReaniIKSurfaceMaps maps;
    [Range(0, 1)] public float tremor;
    [Range(0, 1)] public float redness;
    [Range(0, 1)] public float sweat;
    [Range(0, 1)] public float darkness;
    [Range(-1, 3)] public float contrast = 1f;
    [Range(0, 1)] public float desaturation;
    public Color rednessColor = new Color(0.95f, 0.12f, 0.15f, 1);
    [HideInInspector] public Color tremorColor = Color.white;
    public const int MaximumTremorGroups = 100;
    [Range(1, MaximumTremorGroups), InspectorName("Maximum Tremor Groups")] public int tremorGroups = 20;
    [Range(1, 24), InspectorName("Tremor Frequency (Hz)")] public float tremorSpeed = 5;
    public int TargetTremorGroups => Mathf.RoundToInt(Mathf.Clamp01(tremor) * Mathf.Clamp(tremorGroups, 1, MaximumTremorGroups));
    public int ActiveTremorGroups => outline?.ActiveGroups ?? 0;
    [Min(0), InspectorName("Tremor Peak Size"), Tooltip("Peak size as a fraction of character height.")] public float outlineWidth = 0.01f;
    [Range(0.1f, 10f), Tooltip("Width multiplier for tremor peak bases. Height is unchanged.")] public float tremorWidth = 1f;
    [Min(0), Tooltip("How many source texture pixels inward from the opaque contour to sample for tremor color.")] public float tremorColorDepth = 3f;
    [Serializable] private sealed class MaterialSource { public SpriteRenderer renderer; public Material material; }
    [SerializeField, HideInInspector] private List<MaterialSource> materialSources = new List<MaterialSource>();
    [Min(0)] public float sweatSpeed = 0.13f;
    [Min(0.0001f), Tooltip("Minimum droplet radius relative to sprite height. Applied to newly spawned drops.")] public float sweatSizeMin = .004f;
    [Min(0.0001f), Tooltip("Maximum droplet radius relative to sprite height. Applied to newly spawned drops. Set equal to minimum for a fixed size.")] public float sweatSizeMax = .009f;
    [Min(0), Tooltip("Seconds for a new droplet to fade in from transparent.")] public float sweatFadeIn = .4f;
    [Min(0.1f)] public float trailLifetime = 5f;
    [Min(0.001f), Tooltip("Maximum trail length relative to sprite height.")] public float trailMaxLength = .15f;
    [Min(0), Tooltip("Exponential slowdown per second of droplet age.")] public float sweatSlowdown = .25f;
    public bool showControls = true;
    public const int MaximumDrops = 20;
    public int ActiveDrops => drops.Count;
    public bool NeedsAnimation => tremor > 0 || sweat > 0;
    private sealed class Surface
    {
        public SpriteRenderer renderer;
        public Sprite sprite;
        public Material original, owned;
        public ReaniIKSurfaceMaps.Entry entry;
        public Texture2D trail;
        public Color[] wet;
        public Vector4[] positions = new Vector4[MaximumDrops];
        public Vector4[] opacity = new Vector4[5];
        public Vector4 uvX, uvY;
        public float aspect;
        public SpriteSkin skin;
        public Vector2[] vertices;
        public ushort[] triangles;
        public BoneWeight[] weights;
        public Matrix4x4[] bind;
    }
    private sealed class Drop { public Surface surface; public Vector2 position, velocity; public float age, radius, fadeTime = -1; public float opacity;
        public readonly List<TrailPoint> path = new List<TrailPoint>(); }
    private struct TrailPoint { public Vector2 position; public float age; public TrailPoint(Vector2 p, float a) { position=p; age=a; } }
    [NonSerialized] private Texture2D trailTexture;
    [NonSerialized] private readonly List<Surface> surfaces = new List<Surface>();
    [NonSerialized] private readonly List<Drop> drops = new List<Drop>();
    [NonSerialized] private ReaniIKJaggedOutline outline;
    [NonSerialized] private double lastTime;
    [NonSerialized] private float spawnBudget, trailTime;
    [NonSerialized] private Texture2D dropTexture;
    [NonSerialized] private ReaniIKSurfaceMaps boundMaps;
    private bool Active => tremor > 0 || redness > 0 || sweat > 0 || darkness > 0 || !Mathf.Approximately(contrast,1f) || desaturation > 0;
    public void Prepare(Transform root)
    {
        if (!Active || boundMaps != maps)
        {
            if (surfaces.Count > 0)
            {
                var rig = root.GetComponent<ReaniIK>();
                if (rig) { rig.leftArmMask.Release(); rig.rightArmMask.Release(); }
            }
            Release(); boundMaps = maps;
            if (!Active) return;
        }
        if (surfaces.Count > 0) return;
        var owner = root.GetComponent<ReaniIK>();
        if (owner) { owner.leftArmMask.Release(); owner.rightArmMask.Release(); }
        RemoveLegacyPropertyBlocks(root);
        var shader = Shader.Find("Reanimator/Arm Mask");
        if (!shader) return;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!renderer.sprite) continue;
            var surface = new Surface { renderer = renderer, sprite = renderer.sprite, original = ResolveOriginal(renderer),
                entry = maps ? maps.Find(renderer.sprite) : null };
            ReaniIKSurfaceMaps.GetUVTransform(renderer.sprite, out surface.uvX, out surface.uvY);
            surface.aspect = renderer.sprite.rect.width / renderer.sprite.rect.height;
            surface.skin = renderer.GetComponent<SpriteSkin>();
            if (surface.skin && renderer.sprite.HasVertexAttribute(VertexAttribute.BlendWeight))
            {
                surface.vertices = renderer.sprite.vertices; surface.triangles = renderer.sprite.triangles;
                surface.weights = renderer.sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight).ToArray();
                surface.bind = renderer.sprite.GetBindPoses().ToArray();
            }
            // A private material keeps the renderer eligible for SRP batching.
            // SpriteSkin's GPU bone offset is lost when an MPB disables batching.
            if (!renderer.sharedMaterial || renderer.sharedMaterial.name != "Arm Mask (ReaniIK)")
            {
                surface.owned = new Material(shader) { name = "Surface Effects (ReaniIK)", hideFlags = HideFlags.HideAndDontSave };
                if (surface.original) surface.owned.CopyPropertiesFromMaterial(surface.original);
                surface.owned.SetFloat("_ArmMaskAmount", 0);
                surface.owned.SetVector("_ArmRange", new Vector4(0, 1, 0, 0));
                if (!surface.skin || !surface.skin.enabled) surface.owned.DisableKeyword("SKINNED_SPRITE");
                renderer.sharedMaterial = surface.owned;
            }
            surfaces.Add(surface);
        }
    }
    public void Tick(Transform root)
    {
        Prepare(root);
        if (!Active || surfaces.Count == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
#if UNITY_EDITOR
        if (!Application.isPlaying) now = UnityEditor.EditorApplication.timeSinceStartup;
#endif
        float dt = lastTime == 0 ? 0 : Mathf.Clamp((float)(now - lastTime), 0, .05f); lastTime = now;
        Simulate(dt);
        foreach (var s in surfaces)
        {
            if (!s.renderer) continue;
            s.entry = maps ? maps.Find(s.sprite) : null;
            var material = s.renderer.sharedMaterial;
            if (!material || material.shader.name != "Reanimator/Arm Mask") continue;
            material.SetFloat("_SurfaceRedness", Mathf.Clamp01(redness));
            material.SetColor("_SurfaceRedColor", rednessColor);
            material.SetFloat("_SurfaceDarkness", Mathf.Clamp01(darkness));
            material.SetFloat("_SurfaceContrast", Mathf.Clamp(contrast,-1,3));
            material.SetFloat("_SurfaceDesaturation", Mathf.Clamp01(desaturation));
            material.SetVector("_SurfaceUVX", s.uvX); material.SetVector("_SurfaceUVY", s.uvY);
            material.SetTexture("_SurfaceMap", s.entry != null && s.entry.normalAndBlush ? s.entry.normalAndBlush : Texture2D.blackTexture);
            material.SetFloat("_SurfaceHasMap", s.entry != null && s.entry.effectsEnabled && s.entry.normalAndBlush ? 1 : 0);
            material.SetTexture("_SweatTrail", s.trail ? s.trail : Texture2D.blackTexture);
            if (!dropTexture) dropTexture = Resources.Load<Texture2D>("ReaniIKSweatDrop");
            material.SetTexture("_SweatDropTexture", dropTexture);
            int count = 0;
            Array.Clear(s.opacity, 0, s.opacity.Length);
            foreach (var d in drops) if (d.surface == s)
            {
                s.positions[count] = new Vector4(d.position.x, d.position.y, d.radius,
                    d.velocity.sqrMagnitude > 0.00000001f ? Mathf.Atan2(d.velocity.y, d.velocity.x * s.aspect) : -Mathf.PI * .5f);
                s.opacity[count / 4][count % 4] = d.opacity; count++;
            }
            for (int i = 0; i < count; i++) material.SetVector("_SweatDrop" + i, s.positions[i]);
            for (int i = 0; i < 5; i++) material.SetVector("_SweatOpacity" + i, s.opacity[i]);
            material.SetFloat("_SweatDropCount", count);
            material.SetFloat("_SurfaceSweat", s.entry != null && s.entry.effectsEnabled ? Mathf.Clamp01(sweat) : 0);
            material.SetFloat("_SurfaceAspect", s.aspect);
        }
        if (tremor > 0)
        {
            outline ??= new ReaniIKJaggedOutline();
            outline.SetSurface(maps, Mathf.Clamp01(redness), rednessColor, contrast, desaturation);
            outline.Draw(root, Mathf.Clamp01(tremor), outlineWidth, tremorGroups, tremorSpeed, (float)now, Mathf.Clamp01(darkness), tremorWidth, tremorColorDepth, tremorMode);
        }
        else { outline?.Release(); outline = null; }
    }
    private void Simulate(float dt)
    {
        if (sweat <= 0) { drops.Clear(); spawnBudget = 0; }
        else
        {
            spawnBudget = Mathf.Min(spawnBudget + dt * sweat * 7, MaximumDrops);
            while (spawnBudget >= 1 && drops.Count < MaximumDrops)
            {
                spawnBudget--;
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    var s = surfaces[UnityEngine.Random.Range(0, surfaces.Count)];
                    if (s.entry == null || !s.entry.effectsEnabled || !s.entry.sweat || !s.entry.normalAndBlush || !s.entry.coverage || !s.renderer.enabled || !s.renderer.gameObject.activeInHierarchy) continue;
                    var p = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
                    if (s.entry.coverage.GetPixelBilinear(p.x, p.y).a < .5f) continue;
                    // Front/back arm copies share one painted surface; only
                    // spawn on the visible front copy to avoid doubled drops.
                    if (s.renderer.name.EndsWith("_back", StringComparison.OrdinalIgnoreCase)) continue;
                    EnsureTrail(s);
                    float minSize = Mathf.Max(.0001f, Mathf.Min(sweatSizeMin, sweatSizeMax));
                    float maxSize = Mathf.Max(minSize, Mathf.Max(sweatSizeMin, sweatSizeMax));
                    drops.Add(new Drop { surface = s, position = p, radius = UnityEngine.Random.Range(minSize, maxSize) });
                    break;
                }
            }
        }
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i]; var s = d.surface; d.age += dt;
            d.opacity = DropOpacity(d.age, d.fadeTime, sweatFadeIn);
            if (!s.renderer || !s.entry.effectsEnabled || !s.entry.coverage) { drops.RemoveAt(i); continue; }
            if (d.age >= 12.8f && d.fadeTime < 0) d.fadeTime = 0;
            if (d.fadeTime >= 0)
            {
                d.fadeTime += dt;
                d.opacity = DropOpacity(d.age, d.fadeTime, sweatFadeIn);
                if (d.fadeTime >= 1.2f) drops.RemoveAt(i);
                continue;
            }
            Color c = s.entry.normalAndBlush.GetPixelBilinear(d.position.x, d.position.y);
            Vector3 normal = ReaniIKSurfaceMaps.DecodeNormal(c, maps ? maps.normalStrength : 1);
            Vector3 gravity = GravityAt(s, d.position);
            if (s.renderer.flipX) gravity.x *= -1;
            if (s.renderer.flipY) gravity.y *= -1;
            Vector3 tangent = gravity - Vector3.Dot(gravity, normal) * normal;
            var velocity = new Vector2(tangent.x / Mathf.Max(s.aspect, .01f), tangent.y) * sweatSpeed * Mathf.Exp(-Mathf.Max(0, sweatSlowdown) * d.age);
            d.velocity = Vector2.Lerp(d.velocity, velocity, 1 - Mathf.Exp(-dt * 8));
            var previous = d.position; d.position += d.velocity * dt;
            if (d.position.x < 0 || d.position.y < 0 || d.position.x > 1 || d.position.y > 1 || s.entry.coverage.GetPixelBilinear(d.position.x, d.position.y).a < .5f)
            { d.position = previous; d.fadeTime = 0; continue; }
            if (d.path.Count == 0) d.path.Add(new TrailPoint(previous, d.age - dt));
            if (Vector2.Scale(d.position - d.path[d.path.Count-1].position, new Vector2(s.aspect,1)).magnitude > .001f)
                d.path.Add(new TrailPoint(d.position, d.age));
            TrimTrail(d);
        }
        trailTime += dt;
        if (trailTime < .05f) return;
        trailTime = 0;
        if (!trailTexture) trailTexture = Resources.Load<Texture2D>("ReaniIKSweatTrail");
        foreach (var s in surfaces)
        {
            if (!s.trail) continue;
            Array.Clear(s.wet, 0, s.wet.Length);
            foreach (var d in drops) if (d.surface == s) DrawTexturedTrail(d);
            s.trail.SetPixels(s.wet); s.trail.Apply(false, false);
        }
    }
    private static float DropOpacity(float age, float fadeTime, float fadeIn)
    {
        float appear = fadeIn > 0 ? Mathf.SmoothStep(0, 1, age / fadeIn) : 1;
        float disappear = fadeTime >= 0 ? 1 - Mathf.SmoothStep(0, 1, fadeTime / 1.2f) : 1;
        return appear * disappear;
    }
    private static Vector3 GravityAt(Surface s, Vector2 uv)
    {
        if (!s.skin || s.weights == null) return s.renderer.transform.InverseTransformDirection(Vector3.down);
        Vector2 p = (Vector2.Scale(uv, s.sprite.rect.size) - s.sprite.pivot) / s.sprite.pixelsPerUnit;
        for (int i = 0; i + 2 < s.triangles.Length; i += 3)
        {
            int a = s.triangles[i], b = s.triangles[i + 1], c = s.triangles[i + 2];
            Vector2 u = s.vertices[b] - s.vertices[a], v = s.vertices[c] - s.vertices[a], q = p - s.vertices[a];
            float det = u.x * v.y - u.y * v.x; if (Mathf.Abs(det) < 1e-10f) continue;
            float y = (q.x * v.y - q.y * v.x) / det, z = (u.x * q.y - u.y * q.x) / det, x = 1 - y - z;
            if (x < -.001f || y < -.001f || z < -.001f) continue;
            Vector3 right = Vector3.zero, up = Vector3.zero;
            Vertex(a, x); Vertex(b, y); Vertex(c, z);
            if (right.sqrMagnitude > 1e-10f && up.sqrMagnitude > 1e-10f)
                return new Vector3(Vector3.Dot(Vector3.down, right.normalized), Vector3.Dot(Vector3.down, up.normalized), 0);
            void Vertex(int index, float factor)
            {
                var w = s.weights[index]; Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                void Add(int bone, float weight)
                {
                    if (weight <= 0 || bone < 0 || bone >= s.bind.Length || bone >= s.skin.boneTransforms.Length || !s.skin.boneTransforms[bone]) return;
                    var m = s.skin.boneTransforms[bone].localToWorldMatrix * s.bind[bone];
                    right += m.MultiplyVector(Vector3.right) * (factor * weight); up += m.MultiplyVector(Vector3.up) * (factor * weight);
                }
            }
        }
        return s.renderer.transform.InverseTransformDirection(Vector3.down);
    }
    private static void EnsureTrail(Surface s)
    {
        if (s.trail) return;
        int height = 256; int width = Mathf.Clamp(Mathf.RoundToInt(height * s.aspect), 32, 512);
        s.trail = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = "Sweat tracks", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        s.wet = new Color[width * height]; s.trail.SetPixels(s.wet); s.trail.Apply();
    }
    public void Release()
    {
        foreach (var s in surfaces)
        {
            if (s.renderer)
            {
                var material = s.renderer.sharedMaterial;
                if (material && material.shader.name == "Reanimator/Arm Mask")
                {
                    material.SetFloat("_SurfaceRedness", 0); material.SetFloat("_SurfaceDarkness", 0);
                    material.SetFloat("_SurfaceContrast", 1); material.SetFloat("_SurfaceDesaturation", 0);
                    material.SetFloat("_SurfaceSweat", 0); material.SetFloat("_SweatDropCount", 0);
                }
                if (!s.renderer.sharedMaterial || (s.owned && s.renderer.sharedMaterial == s.owned))
                    s.renderer.sharedMaterial = s.original ? s.original : ResolveOriginal(s.renderer);
            }
            Destroy(s.owned); Destroy(s.trail);
        }
        surfaces.Clear(); drops.Clear(); outline?.Release(); outline = null; lastTime = 0; spawnBudget = 0;
    }
    private Material ResolveOriginal(SpriteRenderer renderer)
    {
        materialSources ??= new List<MaterialSource>();
        var saved = materialSources.Find(item => item.renderer == renderer);
        if (saved != null && Usable(saved.material)) return saved.material;
        Material source = Usable(renderer.sharedMaterial) ? renderer.sharedMaterial : null;
#if UNITY_EDITOR
        if (!source)
        {
            var prefab = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            if (prefab && Usable(prefab.sharedMaterial)) source = prefab.sharedMaterial;
        }
#endif
        if (!source) source = Resources.Load<Material>("ReaniIKSpriteBase");
        if (saved == null) { saved = new MaterialSource { renderer = renderer }; materialSources.Add(saved); }
        saved.material = source;
        return source;
    }
    private static bool Usable(Material material) => material && material.shader &&
        material.shader.name != "Hidden/InternalErrorShader" &&
        material.name != "Surface Effects (ReaniIK)" && material.name != "Arm Mask (ReaniIK)";
    public void RepairMissingMaterials(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer.sprite && !renderer.sharedMaterial) renderer.sharedMaterial = ResolveOriginal(renderer);
    }
    public static void RemoveLegacyPropertyBlocks(Transform root)
    {
        var block = new MaterialPropertyBlock();
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderer.GetPropertyBlock(block);
            if (block.GetFloat("_SurfaceAspect") > 0) renderer.SetPropertyBlock(null);
            block.Clear();
        }
    }
    internal static void Destroy(UnityEngine.Object value)
    {
        if (!value) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
    }
}
