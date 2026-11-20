using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
// Apply IK and secondary motion before SpriteSkin's deformation pass at order 10.
[DefaultExecutionOrder(0)]
public sealed partial class ReaniIK : MonoBehaviour
{
    [Serializable]
    public sealed class VModeEntry
    {
        public Transform bone;
        public float direction;
        public float strength = 1f;
        public bool invert;

        [NonSerialized] private Transform capturedBone, parent, grandparent;
        [NonSerialized] private Vector3 restPosition;

        internal bool TryGetOffset(out Vector3 offset)
        {
            offset = Vector3.zero;
            var nextParent = bone ? bone.parent : null;
            var nextGrandparent = nextParent ? nextParent.parent : null;
            if (capturedBone != bone || parent != nextParent || grandparent != nextGrandparent)
            {
                capturedBone = bone;
                parent = nextParent;
                grandparent = nextGrandparent;
                if (grandparent) restPosition = parent.localPosition;
            }
            if (!grandparent) return false;

            var displacement = parent.localPosition - restPosition;
            var rotated = Quaternion.AngleAxis(direction, Vector3.forward) * displacement;
            var multiplier = invert ? -strength : strength;
            offset = parent.InverseTransformVector(grandparent.TransformVector(rotated * multiplier));
            return true;
        }
    }

    [Serializable]
    public class SpringGroup
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

        public void Simulate(float dt, bool rotationOnly = false, float gravitySag = 0f)
        {
            if (!enabled)
            {
                Reset();
                return;
            }
            if (simulation == null) simulation = new SpringBoneSimulation2D(rotationOnly);
            simulation.Simulate(bones, frequency, damping, inertia, maximumOffset, dt, gravitySag);
        }
    }

    [Serializable]
    public sealed class BreastSpringGroup : SpringGroup
    {
        [Min(0f), Tooltip("Resting downward offset in parent space units, along world down.")]
        public float gravitySag = 0.025f;

        public BreastSpringGroup()
        {
            frequency = 2f;
            damping = 0.2f;
            inertia = 0.7f;
            maximumOffset = 1f;
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
        [NonSerialized] private bool appliedTranslation;
        [NonSerialized] private Vector3 baseUpperPosition, appliedUpperPosition;

        public int BoneCount => !upper || !tip ? 0 : lower ? 3 : tip == upper ? 1 : 2;
        public bool IsValid => upper && tip && target && !target.IsChildOf(upper) &&
            (lower ? lower != upper && lower.parent == upper && tip.parent == lower :
                tip == upper || tip.parent == upper);

        public void CaptureDefaultPose()
        {
            if (!upper || !tip) return;
            defaultUpper = upper.localRotation;
            defaultLower = lower ? lower.localRotation : Quaternion.identity;
            defaultTip = tip.localRotation;
        }

        public void RestorePose()
        {
            if (!applied) return;
            if (appliedTranslation && savedUpper &&
                (savedUpper.localPosition - appliedUpperPosition).sqrMagnitude < 1e-10f)
                savedUpper.localPosition = baseUpperPosition;
            appliedTranslation = false;
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
            if (!lower)
            {
                SolveShortChain(blend);
                return;
            }

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

        private void SolveShortChain(float blend)
        {
            savedUpper = upper;
            savedLower = null;
            savedTip = tip != upper ? tip : null;
            baseUpper = upper.localRotation;
            baseTip = tip.localRotation;
            baseUpperPosition = upper.localPosition;
            if (tip == upper)
            {
                // A single existing bone is a pose control, never an IK chain through the hips.
                upper.position = EffectiveTarget;
                upper.localPosition = Vector3.Lerp(baseUpperPosition, upper.localPosition, blend);
                appliedUpperPosition = upper.localPosition;
                appliedTranslation = true;
                if (constrainRotation)
                    upper.rotation = Quaternion.Slerp(upper.rotation, target.rotation, blend);
            }
            else
            {
                if (solveFromDefaultPose) upper.localRotation = defaultUpper;
                Vector2 direction = upper.InverseTransformPoint(EffectiveTarget);
                Vector2 segment = tip.localPosition;
                if (direction.sqrMagnitude > 1e-10f && segment.sqrMagnitude > 1e-10f)
                    upper.localRotation *= Quaternion.AngleAxis(Vector2.SignedAngle(segment, direction), Vector3.forward);
                if (constrainRotation) tip.rotation = target.rotation;
                upper.localRotation = Quaternion.Slerp(baseUpper, upper.localRotation, blend);
                if (constrainRotation) tip.localRotation = Quaternion.Slerp(baseTip, tip.localRotation, blend);
            }
            appliedUpper = upper.localRotation;
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
    public ReaniIKBodyScale bodyScale = new ReaniIKBodyScale();
    [Range(0f, 1f)] public float ikWeight = 1f;
    public BreastSpringGroup breasts = new BreastSpringGroup();
    public SpringGroup softContour = new SpringGroup();
    public SpringGroup springRotation = new SpringGroup { maximumOffset = 0.5f };
    public SpringGroup frontHair = new SpringGroup { frequency = 2.8f, damping = 0.3f, maximumOffset = 0.1f };
    public SpringGroup backHair = new SpringGroup { frequency = 2.2f, damping = 0.28f, maximumOffset = 0.18f };
    public Limb leftArm = new Limb { flip = true };
    public ReaniIKArmMask leftArmMask = new ReaniIKArmMask();
    public Limb rightArm = new Limb();
    public ReaniIKArmMask rightArmMask = new ReaniIKArmMask();
    public ReaniIKSurfaceEffects surfaceEffects = new ReaniIKSurfaceEffects();
    public Limb leftLeg = new Limb { flip = true };
    public Limb rightLeg = new Limb();
    public Limb leftArm2 = new Limb();
    public Limb rightArm2 = new Limb();
    [UnityEngine.Serialization.FormerlySerializedAs("breathingAmplitude")]
    [Range(0f, 3f), Tooltip("Breathing speed relative to the configured period. Zero pauses the cycle.")]
    public float breathingFrequency = 1f;
    [Range(0f, 3f), Tooltip("Multiplier for breathing position, scale and rotation changes.")]
    public float breathingMultiplier = 1f;
    public RigBreathing2D breathing = new RigBreathing2D();
    public List<VModeEntry> v_mode = new List<VModeEntry>();
    private readonly List<(Transform bone, Vector3 before, Vector3 after)> vModeOffsets =
        new List<(Transform, Vector3, Vector3)>();
    [Header("Scene Controls")]
    public bool showHipsControl = true;
    public bool showRootControl = true;
    public bool showSpineControl = true;
    public bool showBellyControl = true;
    public bool showShouldersControl = true;
    public bool showNeckControl = true;
    public bool showFaceControl = true;
    public bool showButtControl = true;
    [InspectorName("Face Control Multipliers")]
    public ReaniIKFacePose facePose = new ReaniIKFacePose();

#if UNITY_EDITOR
    private double previousTime;
#endif

    private void OnEnable()
    {
        RefreshBoneRigDrivers();
        breathing.Restore();
        ReaniIKSurfaceEffects.RemoveLegacyPropertyBlocks(transform);
        surfaceEffects.RepairMissingMaterials(transform);
        ReaniIKLegSetup.Configure(this);
        ReaniIKArmSetup.Configure(this);
        breathing.DiscoverFaceBones(transform);
        facePose.DiscoverBones(transform);
        if (softContour.bones == null || softContour.bones.Length == 0)
            softContour.bones = Array.FindAll(GetComponentsInChildren<Transform>(true), IsSoftContourBone);
        if (springRotation.bones == null || springRotation.bones.Length == 0)
            springRotation.bones = Array.FindAll(GetComponentsInChildren<Transform>(true), IsSpringRotationBone);
        ResetSpringMotion();
#if UNITY_EDITOR
        ReaniIKSurfaceAuthoring.BindExisting(this);
        previousTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update += EditorTick;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += BeforeSceneSave;
        UnityEditor.SceneManagement.PrefabStage.prefabSaving += BeforePrefabSave;
#endif
    }

    private void OnDisable()
    {
        foreach (var booty in GetComponentsInChildren<SideAssRig2D>(true)) booty.RestorePose();
        leftArmMask.Release();
        rightArmMask.Release();
        surfaceEffects.Release();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= BeforeSceneSave;
        UnityEditor.SceneManagement.PrefabStage.prefabSaving -= BeforePrefabSave;
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
        surfaceEffects.Prepare(transform);
        // Also preview the mask when IK motion preview is paused or Animation Mode is active.
        leftArmMask.Apply(transform, leftArm);
        rightArmMask.Apply(transform, rightArm);
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
                EvaluateHiddenBoneRigs(dt);
                surfaceEffects.Tick(transform);
                return;
            }
        }
#endif
        Evaluate(dt);
        surfaceEffects.Tick(transform);
    }

    public void Evaluate(float deltaTime)
    {
        RestorePose();
        bodyScale.Apply(this);
        // Capture newly assigned chains before the other rig modules move them.
        foreach (var entry in v_mode)
            entry?.TryGetOffset(out _);
        breathing.Evaluate(transform, deltaTime, breathingFrequency, breathingMultiplier);
        facePose.Apply(transform);
        leftArm.Solve(ikWeight, deltaTime);
        rightArm.Solve(ikWeight, deltaTime);
        leftArm2.Solve(ikWeight, deltaTime);
        rightArm2.Solve(ikWeight, deltaTime);
        leftLeg.Solve(ikWeight, deltaTime);
        rightLeg.Solve(ikWeight, deltaTime);
        EvaluateHiddenBoneRigs(deltaTime);
        breasts.Simulate(deltaTime, gravitySag: breasts.gravitySag);
        softContour.Simulate(deltaTime);
        frontHair.Simulate(deltaTime);
        backHair.Simulate(deltaTime);
        ApplyVMode();
        springRotation.Simulate(deltaTime, rotationOnly: true);
    }

    private void ApplyVMode()
    {
        foreach (var entry in v_mode)
        {
            if (entry == null || !entry.TryGetOffset(out var offset) || offset == Vector3.zero)
                continue;
            var bone = entry.bone;
            var before = bone.localPosition;
            vModeOffsets.Add((bone, before, before + offset));
        }

        for (var i = 0; i < vModeOffsets.Count; i++)
        {
            var entry = vModeOffsets[i];
            var before = entry.bone.localPosition;
            var after = before + (entry.after - entry.before);
            entry.bone.localPosition = after;
            vModeOffsets[i] = (entry.bone, before, after);
        }
    }

    private void RestorePose()
    {
        RestoreHiddenBoneRigPoses();
        for (var i = vModeOffsets.Count - 1; i >= 0; i--)
        {
            var entry = vModeOffsets[i];
            if (entry.bone && entry.bone.localPosition == entry.after)
                entry.bone.localPosition = entry.before;
        }
        vModeOffsets.Clear();
        springRotation.RestorePose();
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
        facePose.Restore();
        breathing.Restore();
        bodyScale.Restore();
    }

    public void ResetSpringMotion()
    {
        springRotation.Reset();
        breasts.Reset();
        softContour.Reset();
        frontHair.Reset();
        backHair.Reset();
    }

#if UNITY_EDITOR
    // Editor pose edits must start from the authored pose, rather than baking
    // the current breathing, IK, spring or body-size offsets into new defaults.
    public void RestoreProceduralPoseForEditing()
    {
        RestorePose();
        ResetSpringMotion();
    }

    public void RefreshFacePoseForEditing() => facePose.DiscoverBones(transform);
#endif

    internal static bool IsSoftContourBone(Transform bone)
    {
        return bone && bone.name.EndsWith("_SC", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSpringRotationBone(Transform bone)
    {
        return bone && IsSpringRotationBoneName(bone.name);
    }

    internal static bool IsSpringRotationBoneName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.EndsWith("_SR", StringComparison.OrdinalIgnoreCase))
            return true;
        var marker = name.LastIndexOf("_SRN_", StringComparison.OrdinalIgnoreCase);
        return marker > 0 && int.TryParse(name.Substring(marker + 5), out var index) && index > 0;
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
            if (surfaceEffects.NeedsAnimation)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
            return;
        }
        if (UnityEditor.EditorApplication.timeSinceStartup - previousTime < 1.0 / 60.0) return;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }

    private void BeforeSceneSave(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (scene != gameObject.scene) return;
        foreach (var booty in GetComponentsInChildren<SideAssRig2D>(true)) booty.RestorePose();
        leftArmMask.Release();
        rightArmMask.Release();
        surfaceEffects.Release();
        RestorePose();
        ResetSpringMotion();
    }

    private void BeforePrefabSave(GameObject prefabRoot)
    {
        if (gameObject == prefabRoot || transform.IsChildOf(prefabRoot.transform))
        {
            foreach (var booty in GetComponentsInChildren<SideAssRig2D>(true)) booty.RestorePose();
            leftArmMask.Release();
            rightArmMask.Release();
            surfaceEffects.Release();
        }
    }
#endif
}
