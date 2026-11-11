using System;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
// Apply IK and secondary motion before SpriteSkin's deformation pass at order 10.
[DefaultExecutionOrder(0)]
public sealed class ReaniIK : MonoBehaviour
{
    [Serializable]
    public sealed class SpringGroup
    {
        public bool enabled = true;
        public Transform[] bones = Array.Empty<Transform>();
        [Min(0.1f)] public float frequency = 3.2f;
        [Range(0.01f, 1f)] public float damping = 0.24f;
        [Range(0f, 1f)] public float inertia = 0.65f;
        [Min(0f)] public float maximumOffset = 0.16f;
        [NonSerialized] private SpringBoneSimulation2D simulation;

        public void RestorePose() => simulation?.RestorePose();
        public void Reset() => simulation?.ResetSimulation();

        public void Simulate(float dt)
        {
            if (!enabled)
            {
                Reset();
                return;
            }
            if (simulation == null) simulation = new SpringBoneSimulation2D();
            simulation.Simulate(bones, frequency, damping, inertia, maximumOffset, dt);
        }
    }

    [Serializable]
    public sealed class Limb
    {
        public enum FlipMode { Straight, Dino }

        public bool enabled = true;
        public Transform upper;
        public Transform lower;
        public Transform tip;
        public Transform target;
        [Range(0f, 1f)] public float weight = 1f;
        public bool flip;
        public FlipMode flipMode;
        public bool invertDinoFlip;
        [Min(0f), Tooltip("Full bend transitions per second. Zero switches instantly.")]
        public float dinoFlipSpeed = 4f;
        [Range(0f, 1f)] public float targetSoftness;
        public Transform softnessSource;
        [NonSerialized] private bool? dinoFlip;
        [NonSerialized] private float dinoBend;

        [NonSerialized] private Transform sampledSoftnessSource;
        [NonSerialized] private Transform sampledSoftnessParent;
        [NonSerialized] private Vector3 softnessRestPosition;

        public Vector3 EffectiveTarget
        {
            get
            {
                if (!softnessSource) return target.position;
                Transform parent = softnessSource.parent;
                if (sampledSoftnessSource != softnessSource || sampledSoftnessParent != parent)
                {
                    sampledSoftnessSource = softnessSource;
                    sampledSoftnessParent = parent;
                    softnessRestPosition = softnessSource.localPosition;
                }
                Vector3 localOffset = softnessSource.localPosition - softnessRestPosition;
                Vector3 worldOffset = parent ? parent.TransformVector(localOffset) : localOffset;
                return target.position + worldOffset * Mathf.Clamp01(targetSoftness);
            }
        }
        public bool constrainRotation = true;
        public bool solveFromDefaultPose = true;
        [HideInInspector] public Quaternion defaultUpper = Quaternion.identity;
        [HideInInspector] public Quaternion defaultLower = Quaternion.identity;
        [HideInInspector] public Quaternion defaultTip = Quaternion.identity;

        [NonSerialized] private Transform savedUpper;
        [NonSerialized] private Transform savedLower;
        [NonSerialized] private Transform savedTip;
        [NonSerialized] private Quaternion baseUpper;
        [NonSerialized] private Quaternion baseLower;
        [NonSerialized] private Quaternion baseTip;
        [NonSerialized] private Quaternion appliedUpper;
        [NonSerialized] private Quaternion appliedLower;
        [NonSerialized] private Quaternion appliedTip;
        [NonSerialized] private bool applied;

        public bool IsValid => upper && lower && tip && target &&
            lower.parent == upper && tip.parent == lower && !target.IsChildOf(upper);

        public void CaptureDefaultPose()
        {
            if (!upper || !lower || !tip) return;
            defaultUpper = upper.localRotation;
            defaultLower = lower.localRotation;
            defaultTip = tip.localRotation;
        }

        public void RestorePose()
        {
            if (!applied) return;
            RestoreRotation(savedUpper, appliedUpper, baseUpper);
            RestoreRotation(savedLower, appliedLower, baseLower);
            RestoreRotation(savedTip, appliedTip, baseTip);
            applied = false;
        }

        private static void RestoreRotation(Transform bone, Quaternion previous, Quaternion original)
        {
            if (bone && Quaternion.Angle(bone.localRotation, previous) < 0.001f)
                bone.localRotation = original;
        }

        public void Solve(float globalWeight, float deltaTime = 0f)
        {
            float blend = Mathf.Clamp01(weight * globalWeight);
            if (!enabled || !IsValid || blend <= 0f) return;

            savedUpper = upper;
            savedLower = lower;
            savedTip = tip;
            baseUpper = upper.localRotation;
            baseLower = lower.localRotation;
            baseTip = tip.localRotation;

            if (solveFromDefaultPose)
            {
                upper.localRotation = defaultUpper;
                lower.localRotation = defaultLower;
                if (constrainRotation) tip.localRotation = defaultTip;
            }

            // Project onto the bone's XY plane, matching a 2D Limb constraint.
            Vector3 localTarget = upper.InverseTransformPoint(EffectiveTarget);
            localTarget.z = 0f;
            Vector3 worldTarget = upper.TransformPoint(localTarget);
            float firstLength = Vector3.Distance(upper.position, lower.position);
            float secondLength = Vector3.Distance(lower.position, tip.position);
            float distance = Vector3.Distance(upper.position, worldTarget);

            if (firstLength > 0.00001f && secondLength > 0.00001f)
            {
                float reachableDistance = Mathf.Clamp(distance,
                    Mathf.Max(Mathf.Abs(firstLength - secondLength), 0.00001f),
                    firstLength + secondLength);
                float cosine = (reachableDistance * reachableDistance + firstLength * firstLength -
                    secondLength * secondLength) / (2f * firstLength * reachableDistance);
                float bendAngle = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
                Vector2 direction = localTarget;
                if (direction.sqrMagnitude < 1e-10f) direction = lower.localPosition;
                float aimAngle = Vector2.SignedAngle(Vector2.right, direction) +
                    Vector2.SignedAngle(lower.localPosition, Vector2.right);
                if (flipMode == FlipMode.Dino)
                {
                    Quaternion upperStart = upper.localRotation;
                    Quaternion lowerStart = lower.localRotation;
                    bool currentFlip = dinoFlip ?? flip;
                    if (!dinoFlip.HasValue) dinoBend = currentFlip ? -1f : 1f;
                    AimAt(worldTarget, aimAngle + (currentFlip ? -bendAngle : bendAngle));
                    float currentError = Quaternion.Angle(tip.rotation, target.rotation);
                    upper.localRotation = upperStart;
                    lower.localRotation = lowerStart;
                    AimAt(worldTarget, aimAngle + (currentFlip ? bendAngle : -bendAngle));
                    float otherError = Quaternion.Angle(tip.rotation, target.rotation);
                    // A small dead band prevents flicker around equally good bends.
                    float advantage = currentError - otherError;
                    if ((invertDinoFlip ? -advantage : advantage) > 5f)
                        currentFlip = !currentFlip;
                    dinoFlip = currentFlip;
                    float desiredBend = currentFlip ? -1f : 1f;
                    dinoBend = dinoFlipSpeed > 0f
                        ? Mathf.MoveTowards(dinoBend, desiredBend, 2f * dinoFlipSpeed * Mathf.Max(0f, deltaTime))
                        : desiredBend;
                    upper.localRotation = upperStart;
                    lower.localRotation = lowerStart;
                    AimAt(worldTarget, aimAngle + dinoBend * bendAngle);
                }
                else
                {
                    dinoFlip = null;
                    AimAt(worldTarget, aimAngle + (flip ? -bendAngle : bendAngle));
                }
            }

            if (constrainRotation) tip.rotation = target.rotation;
            upper.localRotation = Quaternion.Slerp(baseUpper, upper.localRotation, blend);
            lower.localRotation = Quaternion.Slerp(baseLower, lower.localRotation, blend);
            if (constrainRotation) tip.localRotation = Quaternion.Slerp(baseTip, tip.localRotation, blend);
            appliedUpper = upper.localRotation;
            appliedLower = lower.localRotation;
            appliedTip = tip.localRotation;
            applied = true;
        }

        private void AimAt(Vector3 worldTarget, float upperAngle)
        {
            upper.localRotation *= Quaternion.AngleAxis(upperAngle, Vector3.forward);
            Vector2 lowerTarget = lower.InverseTransformPoint(worldTarget);
            if (lowerTarget.sqrMagnitude <= 1e-10f) return;
            float lowerAngle = Vector2.SignedAngle(Vector2.right, lowerTarget) +
                Vector2.SignedAngle(tip.localPosition, Vector2.right);
            lower.localRotation *= Quaternion.AngleAxis(lowerAngle, Vector3.forward);
        }
    }

    public bool previewInEditor = true;
    [Range(0f, 1f)] public float ikWeight = 1f;
    public SpringGroup breasts = new SpringGroup();
    public SpringGroup softContour = new SpringGroup();
    public SpringGroup frontHair = new SpringGroup { frequency = 2.8f, damping = 0.3f, maximumOffset = 0.1f };
    public SpringGroup backHair = new SpringGroup { frequency = 2.2f, damping = 0.28f, maximumOffset = 0.18f };
    public Limb leftArm = new Limb();
    public Limb rightArm = new Limb();
    public Limb leftLeg = new Limb();
    public Limb rightLeg = new Limb();
    public Limb leftArm2 = new Limb();
    public Limb rightArm2 = new Limb();
    public RigBreathing2D breathing = new RigBreathing2D();
    [Header("Scene Controls")]
    public bool showHipsControl = true;
    public bool showSpineControl = true;
    public bool showNeckControl = true;
    public bool showEyesControl = true;
    public bool showEyelidsControl = true;

#if UNITY_EDITOR
    private double previousTime;
#endif

    private void OnEnable()
    {
        if (softContour.bones == null || softContour.bones.Length == 0)
            softContour.bones = Array.FindAll(GetComponentsInChildren<Transform>(true), IsSoftContourBone);
        ResetSpringMotion();
#if UNITY_EDITOR
        previousTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update += EditorTick;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += BeforeSceneSave;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= BeforeSceneSave;
#endif
        RestorePose();
        ResetSpringMotion();
    }

    private void Update()
    {
        if (Application.isPlaying) RestorePose();
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            dt = (float)(now - previousTime);
            previousTime = now;
            if (!previewInEditor || UnityEditor.AnimationMode.InAnimationMode())
            {
                RestorePose();
                ResetSpringMotion();
                return;
            }
        }
#endif
        Evaluate(dt);
    }

    public void Evaluate(float deltaTime)
    {
        RestorePose();
        breathing.Evaluate(transform, deltaTime);
        leftArm.Solve(ikWeight, deltaTime);
        rightArm.Solve(ikWeight, deltaTime);
        leftArm2.Solve(ikWeight, deltaTime);
        rightArm2.Solve(ikWeight, deltaTime);
        leftLeg.Solve(ikWeight, deltaTime);
        rightLeg.Solve(ikWeight, deltaTime);
        breasts.Simulate(deltaTime);
        softContour.Simulate(deltaTime);
        frontHair.Simulate(deltaTime);
        backHair.Simulate(deltaTime);
    }

    private void RestorePose()
    {
        breasts.RestorePose();
        softContour.RestorePose();
        frontHair.RestorePose();
        backHair.RestorePose();
        leftArm.RestorePose();
        rightArm.RestorePose();
        leftArm2.RestorePose();
        rightArm2.RestorePose();
        leftLeg.RestorePose();
        rightLeg.RestorePose();
        breathing.Restore();
    }

    public void ResetSpringMotion()
    {
        breasts.Reset();
        softContour.Reset();
        frontHair.Reset();
        backHair.Reset();
    }

    internal static bool IsSoftContourBone(Transform bone)
    {
        return bone && bone.name.EndsWith("_SC", StringComparison.OrdinalIgnoreCase);
    }

    public void CaptureDefaultLimbPoses()
    {
        RestorePose();
        leftArm.CaptureDefaultPose();
        rightArm.CaptureDefaultPose();
        leftArm2.CaptureDefaultPose();
        rightArm2.CaptureDefaultPose();
        leftLeg.CaptureDefaultPose();
        rightLeg.CaptureDefaultPose();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!previewInEditor || UnityEditor.AnimationMode.InAnimationMode())
        {
            RestorePose();
            ResetSpringMotion();
            return;
        }
        if (UnityEditor.EditorApplication.timeSinceStartup - previousTime < 1.0 / 60.0) return;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }

    private void BeforeSceneSave(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (scene != gameObject.scene) return;
        RestorePose();
        ResetSpringMotion();
    }
#endif
}
