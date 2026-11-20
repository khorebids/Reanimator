using UnityEngine;

/// <summary>Shared bounded spring simulation used by character rigs and legacy spring components.</summary>
public sealed class SpringBoneSimulation2D
{
    private sealed class BoneState
    {
        public Transform bone;
        public Vector3 basePosition;
        public Vector3 appliedPosition;
        public Quaternion baseRotation;
        public Quaternion appliedRotation;
        public Vector3 previousTarget;
        public Vector3 offset;
        public Vector3 velocity;
        public bool applied;
    }

    private BoneState[] states;
    private readonly bool rotationOnly;

    public SpringBoneSimulation2D(bool rotationOnly = false) => this.rotationOnly = rotationOnly;

    public void RestorePose()
    {
        if (states == null) return;
        foreach (var state in states)
        {
            if (!state.bone || !state.applied) continue;
            // Keep an externally edited or animated pose as the new base pose.
            if (rotationOnly)
            {
                if (state.bone.localRotation == state.appliedRotation)
                    state.bone.localRotation = state.baseRotation;
            }
            else if ((state.bone.localPosition - state.appliedPosition).sqrMagnitude < 1e-10f)
                state.bone.localPosition = state.basePosition;
            state.applied = false;
        }
    }

    public void ResetSimulation()
    {
        RestorePose();
        states = null;
    }

    public void Simulate(Transform[] bones, float frequency, float damping, float inertia, float maximumOffset, float deltaTime,
        float gravitySag = 0f)
    {
        if (bones == null) return;
        if (states == null || states.Length != bones.Length)
        {
            RestorePose();
            states = new BoneState[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                states[i] = new BoneState { bone = bones[i], previousTarget = bones[i] ? GetTarget(bones[i]) : Vector3.zero };
        }

        float dt = Mathf.Clamp(deltaTime, 0f, 0.05f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 240f)));
        float step = dt / steps;
        for (int i = 0; i < states.Length; i++)
        {
            var state = states[i];
            if (state.bone != bones[i])
            {
                ResetSimulation();
                return;
            }
            if (!state.bone || !state.bone.parent) continue;
            var parent = state.bone.parent;
            state.basePosition = state.bone.localPosition;
            Vector3 target = GetTarget(state.bone);
            Vector3 motion = target - state.previousTarget;
            state.previousTarget = target;
            // Long editor stalls and teleports should establish a fresh equilibrium.
            if (deltaTime > 0.25f || parent.InverseTransformVector(motion).magnitude > maximumOffset * 20f)
            {
                state.offset = Vector3.zero;
                state.velocity = Vector3.zero;
            }
            else state.offset -= motion * inertia;

            var equilibrium = Vector3.zero;
            if (!rotationOnly && gravitySag > 0f)
            {
                var down = parent.InverseTransformVector(Vector3.down);
                down.z = 0f;
                equilibrium = parent.TransformVector(down.normalized * Mathf.Min(gravitySag, maximumOffset));
            }

            float omega = 2f * Mathf.PI * Mathf.Clamp(frequency * (1f + i * 0.035f), 0.1f, 20f);
            for (int j = 0; j < steps; j++)
            {
                state.velocity += (-omega * omega * (state.offset - equilibrium) - 2f * damping * omega * state.velocity) * step;
                state.offset += state.velocity * step;
            }
            Vector3 localOffset = parent.InverseTransformVector(state.offset);
            localOffset.z = 0f;
            Vector3 limited = Vector3.ClampMagnitude(localOffset, Mathf.Max(0f, maximumOffset));
            if ((limited - localOffset).sqrMagnitude > 1e-10f)
                state.velocity = Vector3.zero;
            state.offset = parent.TransformVector(limited);
            if (rotationOnly)
            {
                state.baseRotation = state.bone.localRotation;
                Vector2 restDirection = state.baseRotation * Vector3.down;
                float angle = Vector2.SignedAngle(restDirection, restDirection + (Vector2)limited);
                state.appliedRotation = Quaternion.AngleAxis(angle, Vector3.forward) * state.baseRotation;
                state.bone.localRotation = state.appliedRotation;
            }
            else
            {
                state.appliedPosition = state.basePosition + limited;
                state.bone.localPosition = state.appliedPosition;
            }
            state.applied = true;
        }
    }

    // The virtual tip responds to both pivot translation and parent rotation.
    private Vector3 GetTarget(Transform bone)
    {
        if (!rotationOnly || !bone.parent) return bone.position;
        return bone.parent.TransformPoint(bone.localPosition + bone.localRotation * Vector3.down);
    }
}
