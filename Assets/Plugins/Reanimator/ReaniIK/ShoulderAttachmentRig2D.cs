using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1)]
public sealed class ShoulderAttachmentRig2D : MonoBehaviour
{
    [SerializeField] private Transform arm, shoulders;
    [SerializeField] private float maximumShift, restMajorAngle;
    [SerializeField] private bool flip;
    [SerializeField] private Vector3 restPosition;
    [SerializeField] private Quaternion restRotation;
    private Vector3 appliedPosition;
    private bool applied;

    public static float EvaluateRetreat(float minorAngle, float referenceMajorAngle) =>
        Mathf.Clamp01((referenceMajorAngle - (360f - minorAngle)) / Mathf.Max(1f, referenceMajorAngle - 180f));

    public void Configure(Transform armBone, Transform shouldersBone, float shift, float referenceMajorAngle, bool reverse)
    {
        RestorePose(); arm = armBone; shoulders = shouldersBone;
        maximumShift = Mathf.Max(0, shift); restMajorAngle = referenceMajorAngle; flip = reverse;
        restPosition = transform.localPosition; restRotation = transform.localRotation;
    }
    public void Evaluate()
    {
        if (!arm || !shoulders || (transform.parent != arm && transform.parent != shoulders)) return;
        float bend = EvaluateRetreat(Vector2.Angle(shoulders.right, arm.right), restMajorAngle);
        appliedPosition = restPosition + restRotation * Vector3.right * ((flip ? -1 : 1) * maximumShift * bend);
        transform.localPosition = appliedPosition;
        applied = true;
    }
    public void RestorePose()
    {
        if (applied && (transform.localPosition - appliedPosition).sqrMagnitude < 1e-10f) transform.localPosition = restPosition;
        applied = false;
    }
    private void LateUpdate() => Evaluate();
    private void OnDisable() => RestorePose();
}
