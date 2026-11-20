using UnityEngine;

// Local, pelvis-attached soft volume. Compression is explicit because a bent leg
// does not imply contact with a seat.
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(10)]
public sealed class SideAssRig2D : MonoBehaviour
{
    public Transform upperLeg, compressionBone;
    [Range(0, 1.5f)] public float bendProtrusion = .35f;
    [Range(-1, 1)] public float protrusion;
    [Range(0, 1)] public float compression;
    [Range(.1f, 5)] public float frequency = 2;
    [Range(.1f, 1.5f)] public float damping = .8f;
    [Range(0, 1)] public float inertia = .35f;
    [SerializeField, HideInInspector] private Vector3 restPosition, restScale, pressScale;
    [SerializeField, HideInInspector] private Quaternion restRotation;
    [SerializeField, HideInInspector] private float restLegAngle, radius;
    [SerializeField, HideInInspector] private bool configured;
    private Vector3 offset, velocity, previousTarget;
    private bool initialized;

    public void Configure(Transform leg, Transform press, float size, float amount)
    {
        upperLeg = leg; compressionBone = press; radius = Mathf.Max(.0001f, size);
        restPosition = transform.localPosition; restRotation = transform.localRotation; restScale = transform.localScale;
        pressScale = press.localScale; restLegAngle = leg.localEulerAngles.z;
        bendProtrusion = amount; configured = true; initialized = false;
    }
    public static Vector2 CompressionScale(float contact)
    {
        float height = 1 - .6f * Mathf.Clamp01(contact);
        return new Vector2(1 / height, height);
    }
    public void Evaluate(float dt)
    {
        if (!configured || !upperLeg || !compressionBone || !transform.parent) return;
        float bend = Mathf.SmoothStep(0, 1, Mathf.Abs(Mathf.DeltaAngle(restLegAngle, upperLeg.localEulerAngles.z)) / 90);
        Vector3 localTarget = restPosition + restRotation * Vector3.right * (radius * (protrusion + bendProtrusion * bend));
        Vector3 target = transform.parent.TransformPoint(localTarget);
        if (!initialized || dt > .25f) { offset = velocity = Vector3.zero; previousTarget = target; initialized = true; }
        offset -= (target - previousTarget) * inertia; previousTarget = target;
        dt = Mathf.Clamp(dt, 0, .05f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 240));
        float step = dt / steps, omega = 2 * Mathf.PI * Mathf.Max(.1f, frequency);
        for (int i=0;i<steps;i++) { velocity += (-omega * omega * offset - 2 * damping * omega * velocity) * step; offset += velocity * step; }
        Vector3 local = transform.parent.InverseTransformVector(offset);
        local = Quaternion.Inverse(restRotation) * local; local.z = 0;
        local.y *= 1 - compression;
        local = Vector3.ClampMagnitude(local, radius * .2f);
        transform.localPosition = localTarget + restRotation * local;
        transform.localRotation = restRotation; transform.localScale = restScale;
        var scale = CompressionScale(compression);
        compressionBone.localScale = Vector3.Scale(pressScale, new Vector3(scale.x,scale.y,1));
    }
    private double previousTime;
    private void LateUpdate()
    {
        var owner = GetComponentInParent<ReaniIK>();
        if (owner && (!owner.enabled || (!Application.isPlaying && !owner.previewInEditor))) { RestorePose(); return; }
        double now = Time.realtimeSinceStartupAsDouble;
        Evaluate(previousTime == 0 ? 0 : (float)(now-previousTime)); previousTime = now;
    }
    public void RestorePose()
    {
        if (!configured) return;
        transform.localPosition = restPosition; transform.localRotation = restRotation; transform.localScale = restScale;
        if (compressionBone) compressionBone.localScale = pressScale;
        initialized = false; previousTime = 0;
    }
    private void OnEnable() => RestorePose();
    private void OnDisable() => RestorePose();
}
