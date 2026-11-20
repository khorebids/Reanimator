using System;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

// The mask is evaluated in bind-pose sprite coordinates recovered from UVs.
// It therefore follows both CPU and GPU SpriteSkin deformation without moving a world-space mask.
[Serializable]
public sealed class ReaniIKArmMask
{
    [Range(0f, 1f), Tooltip("0: visible. 1: hidden. Starts at the fingertips.")]
    public float amount = 1f;
    [Tooltip("Hide from the shoulder towards the fingertips instead.")]
    public bool flip;
    [Range(0f, 0.1f), Tooltip("Softness of the moving edge, as a fraction of arm length.")]
    public float softness = 0.01f;

    [HideInInspector] public SpriteRenderer frontRenderer;
    [HideInInspector] public SpriteRenderer backRenderer;
    [HideInInspector] public int sourceSortingOrder;
    [HideInInspector] public bool sourceOrderCaptured;
    [SerializeField, HideInInspector] private Material unmaskedMaterial;

    [NonSerialized] private SpriteRenderer renderer;
    [NonSerialized] private Sprite sprite;
    [NonSerialized] private Material originalMaterial, material;
    [NonSerialized] private int materialOwnerId;
    [NonSerialized] private Transform upper, lower, tip;
    [NonSerialized] private bool searched;
    [NonSerialized] private string status;
    public string Status => status;
    public SpriteRenderer Renderer => renderer;

    public void Apply(Transform root, ReaniIK.Limb limb)
    {
        if (upper != limb.upper || lower != limb.lower || tip != limb.tip)
        {
            Release();
            upper = limb.upper;
            lower = limb.lower;
            tip = limb.tip;
        }
        if (!searched)
        {
            searched = true;
            if (frontRenderer && frontRenderer.transform.IsChildOf(root))
                renderer = frontRenderer;
            else if (upper && lower && tip)
                foreach (var candidate in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // Never mask the rear copy or fall back to the body.
                    if (candidate == backRenderer || !candidate.sprite || !string.Equals(candidate.sprite.name, upper.name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var skin = candidate.GetComponent<SpriteSkin>();
                    if (skin && Array.IndexOf(skin.boneTransforms, upper) >= 0 &&
                        Array.IndexOf(skin.boneTransforms, lower) >= 0 && Array.IndexOf(skin.boneTransforms, tip) >= 0)
                    {
                        if (!renderer || candidate.sortingOrder > renderer.sortingOrder)
                            renderer = candidate;
                    }
                }
        }
        if (!renderer)
        {
            status = "Dedicated arm sprite with the IK chain was not found.";
            return;
        }
        frontRenderer = renderer;
        if (sprite != renderer.sprite || (material && renderer.sharedMaterial != material))
        {
            var current = renderer;
            Release();
            renderer = current;
            searched = true;
        }
        if (!material && !Prepare(limb)) return;
        material.SetFloat("_ArmMaskAmount", Mathf.Clamp01(amount));
        material.SetFloat("_ArmMaskFlip", flip ? 1f : 0f);
        material.SetFloat("_ArmMaskSoftness", Mathf.Clamp(softness, 0f, 0.1f));
        status = null;
    }

    private bool Prepare(ReaniIK.Limb limb)
    {
        var shader = Shader.Find("Reanimator/Arm Mask");
        if (!shader || !shader.isSupported)
        {
            status = "Arm Mask shader is unavailable.";
            return false;
        }
        sprite = renderer.sprite;
        var skin = renderer.GetComponent<SpriteSkin>();
        var bindPoses = sprite.GetBindPoses();
        var spriteBones = sprite.GetBones();
        var indices = new[] { Array.IndexOf(skin.boneTransforms, limb.upper),
            Array.IndexOf(skin.boneTransforms, limb.lower), Array.IndexOf(skin.boneTransforms, limb.tip) };
        foreach (var index in indices)
            if (index < 0 || index >= bindPoses.Length || index >= spriteBones.Length)
            {
                status = "Arm sprite bind poses do not match its bones.";
                return false;
            }
        Vector2 a = bindPoses[indices[0]].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector2 b = bindPoses[indices[1]].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector2 c = bindPoses[indices[2]].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector2 d = bindPoses[indices[2]].inverse.MultiplyPoint3x4(Vector3.right * spriteBones[indices[2]].length);
        if ((d - c).sqrMagnitude < 1e-8f) d = c + (c - b) * 0.3f;
        var vertices = sprite.vertices;
        var uv = sprite.uv;
        var triangles = sprite.triangles;
        Vector4 uvX = default, uvY = default;
        bool found = false;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];
            Vector2 u = uv[i1] - uv[i0], v = uv[i2] - uv[i0];
            float determinant = u.x * v.y - u.y * v.x;
            if (Mathf.Abs(determinant) < 1e-10f) continue;
            Vector2 p = vertices[i1] - vertices[i0], q = vertices[i2] - vertices[i0];
            Vector2 x = (p * v.y - q * u.y) / determinant;
            Vector2 y = (q * u.x - p * v.x) / determinant;
            Vector2 offset = vertices[i0] - x * uv[i0].x - y * uv[i0].y;
            uvX = new Vector4(x.x, y.x, offset.x, 0f);
            uvY = new Vector4(x.y, y.y, offset.y, 0f);
            found = true;
            break;
        }
        if (!found || (b - a).sqrMagnitude < 1e-8f || (c - b).sqrMagnitude < 1e-8f)
        {
            status = "Arm sprite has degenerate geometry or bind poses.";
            return false;
        }
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var vertex in vertices)
        {
            float distance = DistanceAlongArm(vertex, a, b, c, d);
            min = Mathf.Min(min, distance);
            max = Mathf.Max(max, distance);
        }
        originalMaterial = renderer.sharedMaterial;
        if (originalMaterial && originalMaterial.shader == shader && originalMaterial.name != "Surface Effects (ReaniIK)")
            originalMaterial = unmaskedMaterial;
#if UNITY_EDITOR
        if (!originalMaterial)
        {
            var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            if (source && source.sharedMaterial && source.sharedMaterial.shader != shader)
                originalMaterial = source.sharedMaterial;
        }
#endif
        unmaskedMaterial = originalMaterial;
        material = new Material(shader) { name = "Arm Mask (ReaniIK)", hideFlags = HideFlags.HideAndDontSave };
        materialOwnerId = renderer.GetInstanceID();
        if (originalMaterial) material.CopyPropertiesFromMaterial(originalMaterial);
        // CopyPropertiesFromMaterial also copies the source's saved property table.
        // Standard sprite materials have no contrast property, which otherwise
        // reads back as zero and turns the whole masked arm flat grey.
        material.SetFloat("_SurfaceContrast", originalMaterial && originalMaterial.HasProperty("_SurfaceContrast")
            ? originalMaterial.GetFloat("_SurfaceContrast") : 1f);
        material.mainTexture = sprite.texture;
        if (!originalMaterial || originalMaterial.shader != shader)
        {
            material.SetFloat("_SurfaceRedness", 0);
            material.SetFloat("_SurfaceDarkness", 0);
            material.SetFloat("_SurfaceDesaturation", 0);
            material.SetFloat("_SurfaceSweat", 0);
            material.SetFloat("_SurfaceHasMap", 0);
            material.SetFloat("_SurfaceAspect", 1);
            material.SetFloat("_SweatDropCount", 0);
            material.SetTexture("_SurfaceMap", Texture2D.blackTexture);
            material.SetTexture("_SweatTrail", Texture2D.blackTexture);
            material.SetTexture("_SweatDropTexture", Texture2D.blackTexture);
        }
        if (originalMaterial && originalMaterial.IsKeywordEnabled("SKINNED_SPRITE"))
            material.EnableKeyword("SKINNED_SPRITE");
        material.SetVector("_ArmUVX", uvX);
        material.SetVector("_ArmUVY", uvY);
        material.SetVector("_ArmA", a);
        material.SetVector("_ArmB", b);
        material.SetVector("_ArmC", c);
        material.SetVector("_ArmD", d);
        material.SetVector("_ArmRange", new Vector4(min, Mathf.Max(max - min, 1e-5f), 0f, 0f));
        renderer.sharedMaterial = material;
        return true;
    }

    internal static float DistanceAlongArm(Vector2 point, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float best = float.PositiveInfinity, result = 0f, offset = 0f;
        Segment(a, b, true, false);
        Segment(b, c, false, false);
        Segment(c, d, false, true);
        return result;

        void Segment(Vector2 start, Vector2 end, bool first, bool last)
        {
            var delta = end - start;
            float length = delta.magnitude;
            float t = Vector2.Dot(point - start, delta) / Mathf.Max(delta.sqrMagnitude, 1e-10f);
            float distance = (point - (start + delta * Mathf.Clamp01(t))).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                result = offset + length * (first && t < 0f || last && t > 1f ? t : Mathf.Clamp01(t));
            }
            offset += length;
        }
    }

    public void Release()
    {
        var target = renderer ? renderer : frontRenderer;
        if (target && material && target.sharedMaterial == material)
            target.sharedMaterial = originalMaterial ? originalMaterial : unmaskedMaterial;
        else if (target && unmaskedMaterial && target.sharedMaterial &&
                 target.sharedMaterial.name == "Arm Mask (ReaniIK)")
            target.sharedMaterial = unmaskedMaterial;
        // Instantiating a rig can copy a reference to the original rig's transient material.
        // Only the renderer that created that material is allowed to destroy it.
        if (material && target && materialOwnerId == target.GetInstanceID())
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
        material = null;
        materialOwnerId = 0;
        originalMaterial = null;
        renderer = null;
        sprite = null;
        searched = false;
        status = null;
    }
}
