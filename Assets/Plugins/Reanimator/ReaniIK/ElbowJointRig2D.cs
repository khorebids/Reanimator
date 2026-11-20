using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(1)]
public sealed class ElbowJointRig2D : MonoBehaviour
{
    [SerializeField] private Transform sourceJoint;
    [SerializeField] private bool flip;
    [SerializeField, Min(0f)] private float maximumShift;
    [SerializeField] private bool shoulder;
    [SerializeField] private float shoulderOpening = 180f;
    [SerializeField] private Vector3 restPosition;
    [SerializeField] private Quaternion rotationOffset = Quaternion.identity;
    [SerializeField] private Quaternion restRotation = Quaternion.identity;
    private Vector3 appliedPosition;
    private Quaternion appliedRotation;
    private bool applied;

    public void Configure(Transform joint, bool oppositeSide, float shift)
    {
        RestorePose();
        shoulder = false;
        sourceJoint = joint;
        flip = oppositeSide;
        maximumShift = Mathf.Max(0f, shift);
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        rotationOffset = Quaternion.Inverse(GetDirectionRotation(out _)) * restRotation;
    }

    public void ConfigureShoulder(Transform joint, bool oppositeSide, float shift, float opening)
    {
        Configure(joint, oppositeSide, shift);
        shoulder = true;
        shoulderOpening = opening;
        rotationOffset = Quaternion.Inverse(GetDirectionRotation(out _)) * restRotation;
    }

    private Quaternion GetDirectionRotation(out Vector2 offset)
    {
        offset = Vector2.zero;
        if (!sourceJoint || !transform.parent)
            return restRotation;
        var outgoing = (Vector2)(sourceJoint.localRotation * Vector3.right);
        if (shoulder)
        {
            var bisector = -(Vector2.right + outgoing.normalized);
            if (bisector.sqrMagnitude < 1e-6f) bisector = new Vector2(-outgoing.y, outgoing.x);
            var shoulderDirection = bisector.normalized * (flip ? -1f : 1f);
            var opening = Vector2.Angle(outgoing, shoulderDirection);
            var shoulderBend = EvaluateShoulderBend(opening, shoulderOpening, flip);
            offset = shoulderDirection * (maximumShift * shoulderBend);
            return Quaternion.AngleAxis(Mathf.Atan2(shoulderDirection.y, shoulderDirection.x) * Mathf.Rad2Deg, Vector3.forward);
        }
        var tangent = Vector2.right + outgoing.normalized;
        if (tangent.sqrMagnitude < 1e-6f)
            tangent = Vector2.right;
        var direction = new Vector2(-tangent.y, tangent.x).normalized * (flip ? -1f : 1f);
        var bend = Mathf.Clamp01(Vector2.SignedAngle(Vector2.right, outgoing) * (flip ? 1f : -1f) / 180f);
        offset = direction * (maximumShift * bend);
        return Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
    }

    public void Evaluate()
    {
        if (!sourceJoint || !transform.parent)
            return;
        var rotation = GetDirectionRotation(out var offset);
        appliedPosition = restPosition + (Vector3)offset;
        appliedRotation = rotation * rotationOffset;
        transform.localPosition = appliedPosition;
        transform.localRotation = appliedRotation;
        applied = true;
    }

    public void RestorePose()
    {
        if (!applied)
            return;
        if ((transform.localPosition - appliedPosition).sqrMagnitude < 1e-10f)
            transform.localPosition = restPosition;
        if (Quaternion.Angle(transform.localRotation, appliedRotation) < 0.001f)
            transform.localRotation = restRotation;
        applied = false;
    }

    private void LateUpdate() => Evaluate();
    private void OnDisable() => RestorePose();

    internal static float EvaluateShoulderBend(float opening, float restOpening, bool flipped) =>
        Mathf.Clamp01((restOpening - opening) / Mathf.Max(1f, restOpening - (flipped ? 0f : 90f)));
}
