#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

public enum ReanimatorAutoGeometryMode
{
    Default = 0,
    QuadNet = 1,
    Reanimax = 2
}

public enum ReanimatorReanimaxGeometryMode
{
    Hard = 0,
    Soft = 1
}

public partial class Reanimator : EditorWindow, IReanimatorExternalPanelHost
{
    private enum PreviewMode
    {
        Skel = 0,
        Sprite = 1,
        Comfimator = 2
    }

    private enum VisibilityElement
    {
        Sprite = 0,
        Bone = 1,
        Vertex = 2,
        Influence = 3
    }

    private const string MenuPath = "KhorebdisLab/Reanimator/Open";
    private const string WindowTitle = "Reanimator";
    internal const string WindowIconPath = "Assets/Plugins/Reanimator/Graphics/ico.png";
    private const double ToolHotkeyDoublePressSeconds = 0.35d;
    private const double ActivePreviewUpdateGraceSeconds = 1.5d;
    private const double CreateBoneHoverRepaintIntervalSeconds = 1d / 30d;
    private const float VisibilityEnabledEpsilon = 0.0001f;
    private const string FaceEyesWeightCurvePrefsKey =
        "KhorebdisLab.Reanimator.FaceEyesWeightCurve.v1";
    private const string LimbsSettingsPrefsKey =
        "KhorebdisLab.Reanimator.LimbsSettings.v1";

    [Serializable]
    private sealed class FaceEyesWeightCurvePreference
    {
        public AnimationCurve curve;
    }

    [Serializable]
    private sealed class LimbsSettingsPreference
    {
        public int contourSteps;
        public float contourStepDistance;
        public float towardCenterShift;
        public AnimationCurve curve;
        public bool useRadiusCull;
        public float radiusCullRadius;
        public bool radiusCullRemoveInnerVertices;
    }
    internal readonly struct ReanimatorReadinessItem
    {
        public readonly string Label;
        public readonly int Points;
        public readonly string[] EvidenceCaseNames;
        public bool HasEvidence => EvidenceCaseNames != null && EvidenceCaseNames.Length > 0;

        public ReanimatorReadinessItem(string label, int points, params string[] evidenceCaseNames)
        {
            Label = label;
            Points = points;
            EvidenceCaseNames = evidenceCaseNames ?? Array.Empty<string>();
        }
    }

    internal static readonly ReanimatorReadinessItem[] ReanimatorReadinessItems =
    {
        new ReanimatorReadinessItem(
            "core preview/apply/volume/bone-edit basics",
            15,
            "VolumeCurve state is serializable and editable",
            "Bone create waits for top Apply",
            "Renderer bone edit setters update transforms with undo"),
        new ReanimatorReadinessItem(
            "guarded file lifecycle + conflict Reload/Overwrite/Cancel",
            15,
            "Apply pipeline uses single SpriteDataProvider flush",
            "Bone utility meta writes use shared persistence",
            "Meta persistence preserves newline style",
            "Repair PSD Params uses single persist after runtime geometry apply",
            "Importer metadata save and reimport does not leave timestamp mismatch",
            "Asset change algorithms detect external asset change"),
        new ReanimatorReadinessItem(
            "Slider zero-weight identity, smoothing, stable drag/commit",
            10,
            "Weight slider preserves one to four influence identities",
            "Weight slider smooths weights from topology neighbors",
            "Weight slider builds line and triangle weights"),
        new ReanimatorReadinessItem(
            "explicit Projector target contract + intersecting source preview",
            10,
            "Projector explicit target filters intersecting clothing sources",
            "Projector apply does not repair uncovered vertices with target bones",
            "Projector apply resolves target binding by sprite guid"),
        new ReanimatorReadinessItem(
            "Volume preset assets, dirty apply survival, drag undo hardening",
            10,
            "VolumeCurve presets clone and load from assets safely",
            "VolumeCurve drag movement uses one undo group",
            "VolumeCurve apply dirties selected layer only"),
        new ReanimatorReadinessItem(
            "full Undo/Redo audit for tool actions and mode-transition cleanup",
            15,
            "Reanimator window Unity Undo restores renderer state",
            "Renderer cancel clears weight tool state",
            "Renderer cancel clears geometry radius and joint state",
            "Renderer cancel clears curve boob and volume state"),
        new ReanimatorReadinessItem(
            "release PSD fixtures + production tool matrix",
            15,
            "Release PSD fixtures round trip through open edit undo apply reimport",
            "Release tool matrix covers every production tool scenario"),
        new ReanimatorReadinessItem(
            "release UX/perf cleanup",
            10,
            "Release UI hides diagnostics until debug mode",
            "Release UI hides debug module from ordinary toolbar",
            "Release UI uses production button labels",
            "Visibility layout virtualizes rows with stable content metrics")
    };

    internal static int ReanimatorReadinessPercent => CalculateReanimatorReadinessPercent(
        ReanimatorReadinessItems,
        ReanimatorReadinessRuntimeState.HasPassed);
    internal static string[] ReanimatorReadinessLines => BuildReanimatorReadinessLines(
        ReanimatorReadinessItems,
        ReanimatorReadinessRuntimeState.HasPassed);
    private static readonly string[] Tabs = { "Editor", "Statistics" };
    private static Texture2D windowIcon;
    [NonSerialized] private bool isApplyingChanges;

    [SerializeField] private int tabIndex;
    [SerializeField] private bool showPerSprite;
    [SerializeField] private Vector2 statsScroll;
    [SerializeField] private Vector2 leftToolsScroll;
    [SerializeField] private bool leftPoseExpanded = true;
    [SerializeField] private bool leftBonesExpanded = true;
    [SerializeField] private bool leftGeometryExpanded = true;
    [SerializeField] private bool leftWeightsExpanded = true;
    [SerializeField] private bool leftUtilityExpanded = true;
    [SerializeField] private bool leftTodoExpanded = true;
    [SerializeField] private bool spriteSheetToolsExpanded = true;
    [SerializeField] private bool spriteSheetRepairExpanded = true;
    [SerializeField] private bool spriteSheetToolkitExpanded = true;
    [SerializeField] private string spriteSheetToolkitStatus;
    [SerializeField] private string activeAssetPath;
    [SerializeField] private PreviewNavigator previewNavigator;
    [SerializeField] private PreviewNavigator skinningPreviewNavigator;
    [SerializeField] private PreviewNavigator spriteSheetPreviewNavigator;
    [SerializeField] private PreviewNavigator comfimatorPreviewNavigator;
    [SerializeField] private PsdLayerPreviewRenderer layerPreviewRenderer;
    [SerializeField] private bool previewSpriteSelectionLocked;
    [SerializeField] private bool previewSelectedSpriteIsolated;
    [SerializeField] private bool previewVertexInfluenceVisibilityMuted;
    [SerializeField] private float previewVertexVisibilityBeforeMute = 1f;
    [SerializeField] private float previewInfluenceVisibilityBeforeMute = 1f;
    [SerializeField] private Vector2 visibilityScroll;
    [NonSerialized] private float visibilityTreeContentWidth;
    [SerializeField] private int deferredPreviewRepaintFrames;
    private Rect cachedToolbarRect;
    private Rect cachedPreviewRect;
    [SerializeField] private float spriteVisibilityValue = 1f;
    [SerializeField] private float boneVisibilityValue = 1f;
    [SerializeField] private float vertexVisibilityValue = 1f;
    [SerializeField] private float influenceVisibilityValue = 1f;
    [SerializeField] private float spriteVisibilityRestoreValue = 1f;
    [SerializeField] private float boneVisibilityRestoreValue = 1f;
    [SerializeField] private float vertexVisibilityRestoreValue = 1f;
    [SerializeField] private float influenceVisibilityRestoreValue = 1f;
    [SerializeField] private bool visibilityBone = true;
    [SerializeField] private bool visibilitySprite = false;
    [SerializeField] private bool visibilityHierarchy = true;
    [SerializeField] private List<int> collapsedBoneNodes = new List<int>();
    [SerializeField] private List<string> collapsedLayerNodes = new List<string>();
    [SerializeField] private string visibilitySearch = string.Empty;
    [SerializeField] private float rightPanelWidth = ReanimatorWindowLayout.DefaultRightPanelWidth;
    [SerializeField] private bool isRightPanelWidthResizing;
    [SerializeField, Range(0.2f, 0.8f)] private float rightPanelSplit = 0.5f;
    [SerializeField] private bool isRightPanelResizing;
    [SerializeField] private Vector2 weightScroll;
    [SerializeField] private Vector2 faceScroll;
    [SerializeField] private int faceSubtoolIndex;
    [SerializeField, Range(0.1f, 1f)] private float faceEyesPointDensity = 0.5f;
    [SerializeField, Range(0f, 32f)] private float faceEyesPointOffset = 4f;
    [SerializeField, Range(0f, 32f)] private float faceEyesOutlineOffset = 4f;
    [SerializeField] private AnimationCurve faceEyesWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField] private bool isolateBoneModification;
    [SerializeField] private int renamingBoneId = -1;
    [SerializeField] private string renamingBoneValue = string.Empty;
    [SerializeField] private bool focusBoneRenameField;
    private bool isDepthDragActive;
    private bool depthDragIsBone;
    private int depthDragBoneId = -1;
    private string depthDragLayerKey;
    private int depthDragStartValue;
    private Vector2 depthDragStartMouse;
    private int depthDragControlId;
    [SerializeField] private int visibilityAutoScrolledBoneIndex = -1;
    [SerializeField] private bool visibilityAutoScrolledHierarchy;
    [SerializeField] private string visibilityAutoScrolledSearch = string.Empty;
    [SerializeField] private string visibilityAutoScrolledLayerSelectionKey = string.Empty;
    [SerializeField] private bool visibilityAutoScrolledLayerHierarchy;
    [SerializeField] private string visibilityAutoScrolledLayerSearch = string.Empty;

    [SerializeField] private int weightModeIndex;
    [SerializeField] private int weightBoneIndex;
    [SerializeField] private bool weightNormalize = true;
    [SerializeField] private float weightAmount;
    [SerializeField] private float weightSmoothAmount = 0.5f;
    [SerializeField] private float[] vertexWeights = new float[4];
    [SerializeField] private bool[] vertexWeightEnabled = new bool[4];
    [SerializeField] private int[] editGeometryWeightBoneIndices = new int[4];
    [SerializeField] private float[] editGeometryWeightValues = new float[4];
    [SerializeField] private int editGeometrySelectionLayerToken = -1;
    [SerializeField] private int editGeometrySelectionVertexIndex = -1;
    [SerializeField] private PsdLayerPreviewRenderer.PivotPreset pivotPreset = PsdLayerPreviewRenderer.PivotPreset.BottomCenter;
    [SerializeField] private Vector2 pivotPosition = new Vector2(0.5f, 0f);
    [SerializeField] private ReanimatorAutoGeometryMode autoGeometryMode = ReanimatorAutoGeometryMode.Default;
    [SerializeField, Range(0, 100)] private int autoGeometryOutlineDetail = 10;
    [SerializeField, Range(0, 254)] private int autoGeometryAlphaTolerance = 10;
    [SerializeField, Range(0, 100)] private int autoGeometrySubdivide = 0;
    [SerializeField] private double autoGeometryQuadDensity = 5d;
    [SerializeField, Range(0f, 360f)] private float autoGeometryQuadRotationDegrees;
    [SerializeField] private ReanimatorReanimaxGeometryMode autoGeometryReanimaxMode = ReanimatorReanimaxGeometryMode.Hard;
    [SerializeField] private bool autoGeometryReanimaxHardMaskPreview;
    [SerializeField] private bool autoGeometryReanimaxHardEdgePointsPreview;
    [SerializeField, Range(0f, 1f)] private float autoGeometryReanimaxHardMaskAlphaThreshold = 0.5f;
    [SerializeField, Range(1, 20)] private int autoGeometryReanimaxHardEdgePositionFilterPixels = 3;
    [SerializeField, Range(0f, 45f)] private float autoGeometryReanimaxHardEdgeMinTurnDegrees = 13f;
    [SerializeField, Range(1, 20)] private int autoGeometryReanimaxHardEdgePostPositionFilterPixels = 4;
    [SerializeField, Range(0f, 20f)] private float autoGeometryReanimaxHardEdgeMinDistancePixels = 2.8f;
    [SerializeField, Range(1, 50)] private int autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30;
    [SerializeField, Range(0f, 100f)] private float autoGeometryReanimaxHardNormalInsetPixels = 5f;
    [SerializeField, Range(1, 50)] private int autoGeometryReanimaxHardNormalInsetPositionFilterPixels = 5;
    [SerializeField] private int autoGeometryReanimaxHardEdgeDefaultsVersion;
    [SerializeField] private bool autoGeometryGenerateWeights = true;
    [SerializeField] private float radiusCullRadius = 24f;
    [SerializeField] private bool radiusCullRemoveContourVertices = false;
    [SerializeField] private bool radiusCullRemoveInnerVertices = true;
    [SerializeField] private int curveSpaceMode;
    [SerializeField] private bool limbsUseRadiusCull = true;
    [SerializeField] private bool jointUseRadiusCull = true;
    [SerializeField, Range(0, 1)] private int jointAlgorithmMode;
    [SerializeField] private AnimationCurve jointV2FirstIterationWeightCurve =
        ReanimatorJointV2Defaults.CreateWeightCurve();
    [SerializeField] private AnimationCurve jointV2SpecialWeightCurve =
        ReanimatorJointV2Defaults.CreateSpecialWeightCurve();
    [SerializeField, Range(0f, 1f)] private float jointV2SpecialJointDistance =
        ReanimatorJointV2Defaults.SpecialJointDistance;
    [SerializeField, Range(0f, 1f)] private float jointV2BoneBaseOffset =
        ReanimatorJointV2Defaults.BoneBaseOffset;
    [SerializeField] private float jointCPairOffset = 8f;
    [SerializeField, Range(0, 10)] private int jointLongPoints = 4;
    [SerializeField] private float analizeCurveGraphHeight = 180f;
    [SerializeField] private ReanimatorToolMode bottomToolMode = ReanimatorToolMode.PreviewPose;
    [SerializeField] private PreviewMode previewMode = PreviewMode.Skel;
    [SerializeField] private bool spriteSheetGeometryMode;
    [SerializeField] private int spritePivotUnitMode;
    private double lastAutoGeometryHotkeyTime;
    private bool autoGeometryHotkeyIsDown;
    private double lastUserActivityTime;
    private double lastCreateBoneHoverRepaintTime;
    private readonly ReanimatorMissingGeometryScanState missingGeometryScanState = new ReanimatorMissingGeometryScanState();

    private static readonly string[] WeightModes = { "AddAndSubtract", "Add", "Subtract", "Replace" };
    internal static readonly string[] FaceSubtoolLabels = { "Eyes", "Mouth", "Earrings" };
    private static readonly string[] PivotPresetLabels =
    {
        "Center",
        "TopLeft",
        "TopCenter",
        "TopRight",
        "LeftCenter",
        "RightCenter",
        "BottomLeft",
        "BottomCenter",
        "BottomRight",
        "Custom",
        "RootBonePosition"
    };
    private static readonly string[] SpritePivotLabels =
    {
        "Center",
        "Top Left",
        "Top",
        "Top Right",
        "Left",
        "Right",
        "Bottom Left",
        "Bottom",
        "Bottom Right",
        "Custom"
    };
    private static readonly string[] SpritePivotUnitModes =
    {
        "Normalized",
        "Pixels"
    };
    private static readonly string[] AutoGeometryModeLabels =
    {
        "Default",
        "QuadNet",
        "Reanimax"
    };
    private static readonly string[] ReanimaxGeometryModeLabels =
    {
        "Hard",
        "Soft"
    };
    private static readonly string[] CurveSpaceModeLabels =
    {
        "Relative",
        "Absolute"
    };

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var window = GetWindow<Reanimator>(WindowTitle);
        window.ApplyWindowTitle();
        window.Repaint();
    }

    public static void OpenAsset(string assetPath)
    {
        var normalizedPath = NormalizeReanimatorAssetPath(assetPath);
        if (!IsPsdImporterAsset(normalizedPath))
            return;

        var asset = AssetDatabase.LoadMainAssetAtPath(normalizedPath);
        if (asset == null)
            return;

        Selection.activeObject = asset;

        var window = GetWindow<Reanimator>(WindowTitle);
        if (!string.Equals(window.activeAssetPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            window.activeAssetPath = normalizedPath;
            window.CancelBoneRename();
            window.EndDepthDrag();
            window.DeferPreviewContent();
            window.layerPreviewRenderer?.MarkDirty();
        }

        window.ApplyWindowTitle();
        window.Focus();
        window.Repaint();
    }

    private void OnEnable()
    {
        lastUserActivityTime = EditorApplication.timeSinceStartup;
        wantsMouseEnterLeaveWindow = true;
        UpdateWindowEventOptions();
        Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
        Undo.undoRedoPerformed += HandleUndoRedoPerformed;
        ApplyWindowTitle();
        EnsureWeightArrays();
        EnsureVisibilityMode();
        EnsureDefaultToolMode();
        EnsureReanimaxHardEdgeDefaults();
        LoadFaceEyesWeightCurvePreference();
        EnsurePreviewNavigators();
        if (layerPreviewRenderer == null)
            layerPreviewRenderer = new PsdLayerPreviewRenderer();
        LoadLimbsSettingsPreference();
        layerPreviewRenderer.BoneInfluenceOverlay = influenceVisibilityValue;
        if (string.IsNullOrWhiteSpace(activeAssetPath))
            TryUpdateActiveAssetPathFromSelection();
        EditorApplication.update -= HandleEditorUpdate;
        EditorApplication.update += HandleEditorUpdate;
        DeferPreviewContent();
    }

    private void EnsureReanimaxHardEdgeDefaults()
    {
        if (autoGeometryReanimaxHardEdgeDefaultsVersion >= 7)
            return;

        if (autoGeometryReanimaxHardEdgeDefaultsVersion < 1 &&
            autoGeometryReanimaxHardEdgePositionFilterPixels == 1 &&
            Mathf.Abs(autoGeometryReanimaxHardEdgeMinTurnDegrees) <= 0.0001f)
        {
            autoGeometryReanimaxHardEdgePositionFilterPixels = 3;
            autoGeometryReanimaxHardEdgeMinTurnDegrees = 13f;
        }

        if (autoGeometryReanimaxHardEdgePostPositionFilterPixels < 1)
            autoGeometryReanimaxHardEdgePostPositionFilterPixels = 4;
        else if (autoGeometryReanimaxHardEdgeDefaultsVersion < 2 &&
                 autoGeometryReanimaxHardEdgePostPositionFilterPixels == 2)
            autoGeometryReanimaxHardEdgePostPositionFilterPixels = 4;

        if (autoGeometryReanimaxHardEdgeDefaultsVersion < 3 &&
            autoGeometryReanimaxHardEdgeMinDistancePixels <= 0.0001f)
            autoGeometryReanimaxHardEdgeMinDistancePixels = 2f;
        if (autoGeometryReanimaxHardEdgeDefaultsVersion < 4 &&
            Mathf.Abs(autoGeometryReanimaxHardEdgeMinDistancePixels - 2f) <= 0.0001f)
            autoGeometryReanimaxHardEdgeMinDistancePixels = 2.8f;

        if (autoGeometryReanimaxHardNormalMidpointPositionFilterPixels < 1)
            autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30;
        if (autoGeometryReanimaxHardEdgeDefaultsVersion < 6 &&
            autoGeometryReanimaxHardNormalMidpointPositionFilterPixels == 3)
            autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30;
        if (autoGeometryReanimaxHardNormalInsetPixels <= 0.0001f)
            autoGeometryReanimaxHardNormalInsetPixels = 5f;
        if (autoGeometryReanimaxHardNormalInsetPositionFilterPixels < 1)
            autoGeometryReanimaxHardNormalInsetPositionFilterPixels = 5;

        autoGeometryReanimaxHardEdgeDefaultsVersion = 7;
    }

    private void ResetReanimaxHardEdgeDefaults()
    {
        autoGeometryReanimaxHardEdgePositionFilterPixels = 3;
        autoGeometryReanimaxHardEdgeMinTurnDegrees = 13f;
        autoGeometryReanimaxHardEdgePostPositionFilterPixels = 4;
        autoGeometryReanimaxHardEdgeMinDistancePixels = 2.8f;
        autoGeometryReanimaxHardNormalMidpointPositionFilterPixels = 30;
        autoGeometryReanimaxHardNormalInsetPixels = 5f;
        autoGeometryReanimaxHardNormalInsetPositionFilterPixels = 5;
        autoGeometryReanimaxHardEdgeDefaultsVersion = 7;
    }

    private void OnDisable()
    {
        SaveFaceEyesWeightCurvePreference();
        SaveLimbsSettingsPreference();
        StoreActivePreviewNavigator();
        EditorApplication.update -= HandleEditorUpdate;
        Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
    }

    private void LoadFaceEyesWeightCurvePreference()
    {
        if (!EditorPrefs.HasKey(FaceEyesWeightCurvePrefsKey))
        {
            SaveFaceEyesWeightCurvePreference();
            return;
        }

        try
        {
            var json = EditorPrefs.GetString(FaceEyesWeightCurvePrefsKey, string.Empty);
            var preference = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<FaceEyesWeightCurvePreference>(json);
            if (preference?.curve != null && preference.curve.length > 0)
                faceEyesWeightCurve = CloneFaceEyesWeightCurve(preference.curve);
        }
        catch (Exception)
        {
            // Keep the serialized window value if an old preference is corrupt.
        }
    }

    private void SaveFaceEyesWeightCurvePreference()
    {
        if (faceEyesWeightCurve == null || faceEyesWeightCurve.length == 0)
            faceEyesWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var preference = new FaceEyesWeightCurvePreference
        {
            curve = CloneFaceEyesWeightCurve(faceEyesWeightCurve)
        };
        EditorPrefs.SetString(FaceEyesWeightCurvePrefsKey, JsonUtility.ToJson(preference));
    }

    private static AnimationCurve CloneFaceEyesWeightCurve(AnimationCurve source)
    {
        if (source == null || source.length == 0)
            return AnimationCurve.Linear(0f, 0f, 1f, 1f);

        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
    }

    private void LoadLimbsSettingsPreference()
    {
        if (layerPreviewRenderer == null)
            return;

        if (!EditorPrefs.HasKey(LimbsSettingsPrefsKey))
        {
            SaveLimbsSettingsPreference();
            return;
        }

        try
        {
            var json = EditorPrefs.GetString(LimbsSettingsPrefsKey, string.Empty);
            var preference = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<LimbsSettingsPreference>(json);
            if (preference == null)
                return;

            layerPreviewRenderer.LimbContourSteps = preference.contourSteps;
            layerPreviewRenderer.LimbContourStepDistance = preference.contourStepDistance;
            layerPreviewRenderer.LimbTowardCenterShift = preference.towardCenterShift;
            layerPreviewRenderer.LimbCurve = CloneLimbsCurve(preference.curve);
            limbsUseRadiusCull = preference.useRadiusCull;
            radiusCullRadius = Mathf.Max(0.01f, preference.radiusCullRadius);
            radiusCullRemoveInnerVertices = preference.radiusCullRemoveInnerVertices;
        }
        catch (Exception)
        {
            // Keep the serialized window values if an old preference is corrupt.
        }
    }

    private void SaveLimbsSettingsPreference()
    {
        if (layerPreviewRenderer == null)
            return;

        var preference = new LimbsSettingsPreference
        {
            contourSteps = layerPreviewRenderer.LimbContourSteps,
            contourStepDistance = layerPreviewRenderer.LimbContourStepDistance,
            towardCenterShift = layerPreviewRenderer.LimbTowardCenterShift,
            curve = CloneLimbsCurve(layerPreviewRenderer.LimbCurve),
            useRadiusCull = limbsUseRadiusCull,
            radiusCullRadius = Mathf.Max(0.01f, radiusCullRadius),
            radiusCullRemoveInnerVertices = radiusCullRemoveInnerVertices
        };
        EditorPrefs.SetString(LimbsSettingsPrefsKey, JsonUtility.ToJson(preference));
    }

    private static AnimationCurve CloneLimbsCurve(AnimationCurve source)
    {
        if (source == null || source.length == 0)
            return AnimationCurve.Linear(0f, 0f, 1f, 1f);

        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
    }

    private void EnsurePreviewNavigators()
    {
        if (previewNavigator == null)
            previewNavigator = new PreviewNavigator();

        if (skinningPreviewNavigator == null && spriteSheetPreviewNavigator == null && comfimatorPreviewNavigator == null)
        {
            if (previewMode == PreviewMode.Comfimator)
            {
                comfimatorPreviewNavigator = previewNavigator;
                skinningPreviewNavigator = new PreviewNavigator();
                spriteSheetPreviewNavigator = new PreviewNavigator();
            }
            else if (previewMode == PreviewMode.Sprite)
            {
                spriteSheetPreviewNavigator = previewNavigator;
                skinningPreviewNavigator = new PreviewNavigator();
                comfimatorPreviewNavigator = new PreviewNavigator();
            }
            else
            {
                skinningPreviewNavigator = previewNavigator;
                spriteSheetPreviewNavigator = new PreviewNavigator();
                comfimatorPreviewNavigator = new PreviewNavigator();
            }
        }
        else
        {
            if (skinningPreviewNavigator == null)
                skinningPreviewNavigator = new PreviewNavigator();
            if (spriteSheetPreviewNavigator == null)
                spriteSheetPreviewNavigator = new PreviewNavigator();
            if (comfimatorPreviewNavigator == null)
                comfimatorPreviewNavigator = new PreviewNavigator();
        }

        previewNavigator = GetPreviewNavigator(previewMode);
    }

    private void StoreActivePreviewNavigator()
    {
        if (previewNavigator == null)
            return;

        if (previewMode == PreviewMode.Comfimator)
            comfimatorPreviewNavigator = previewNavigator;
        else if (previewMode == PreviewMode.Sprite)
            spriteSheetPreviewNavigator = previewNavigator;
        else
            skinningPreviewNavigator = previewNavigator;
    }

    private PreviewNavigator GetPreviewNavigator(PreviewMode mode)
    {
        if (mode == PreviewMode.Comfimator)
            return comfimatorPreviewNavigator;
        return mode == PreviewMode.Sprite ? spriteSheetPreviewNavigator : skinningPreviewNavigator;
    }

    private static bool IsSpritePreviewMode(PreviewMode mode)
    {
        return mode == PreviewMode.Sprite || mode == PreviewMode.Comfimator;
    }

    private void OnFocus()
    {
        MarkReanimatorUserActivity();
        UpdateWindowEventOptions();
        Repaint();
    }

    private void OnLostFocus()
    {
        layerPreviewRenderer?.FlushPendingBoneAutosave();
        layerPreviewRenderer?.StopPreviewTestAnimation();
        UpdateWindowEventOptions();
        Repaint();
    }

    private void HandleUndoRedoPerformed()
    {
        layerPreviewRenderer?.RefreshCachedAssetAfterUndoRedo();
        Repaint();
    }

    private void RegisterReanimatorUndo(string operationName)
    {
        var name = string.IsNullOrWhiteSpace(operationName) ? "Reanimator Change" : operationName;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(name);
        Undo.RegisterCompleteObjectUndo(this, name);
    }

    private void ApplyWindowTitle()
    {
        var icon = LoadWindowIcon();
        titleContent = icon != null
            ? EditorGUIUtility.TrTextContentWithIcon(WindowTitle, icon)
            : new GUIContent(WindowTitle);
    }

    internal static Texture2D LoadWindowIcon()
    {
        if (windowIcon == null)
            windowIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(WindowIconPath);
        return windowIcon;
    }

    private void OnSelectionChange()
    {
        if (isApplyingChanges)
            return;

        if (!TryUpdateActiveAssetPathFromSelection())
            return;

        CancelBoneRename();
        EndDepthDrag();
        DeferPreviewContent();
        layerPreviewRenderer?.MarkDirty();
        Repaint();
    }

    private void OnProjectChange()
    {
        if (isApplyingChanges)
            return;

        InvalidateNamingIndicatorCaches(false);

        if (layerPreviewRenderer != null && layerPreviewRenderer.HasPendingChanges)
        {
            Repaint();
            return;
        }

        CancelBoneRename();
        EndDepthDrag();
        DeferPreviewContent();
        layerPreviewRenderer?.MarkDirty();
        Repaint();
    }

    private void OnGUI()
    {
        TrackReanimatorUserActivity(Event.current);
        UpdateWindowEventOptions();
        HandleDepthDragLifecycle();
        HandlePreviewCameraHotkeys();
        HandleToolHotkeys();
        DrawTestTab();
    }

    private void TrackReanimatorUserActivity(Event e)
    {
        if (e == null)
            return;

        switch (e.type)
        {
            case EventType.MouseDown:
            case EventType.MouseDrag:
            case EventType.MouseUp:
            case EventType.ScrollWheel:
            case EventType.KeyDown:
            case EventType.KeyUp:
            case EventType.DragUpdated:
            case EventType.DragPerform:
            case EventType.ExecuteCommand:
            case EventType.ValidateCommand:
            case EventType.MouseEnterWindow:
                MarkReanimatorUserActivity();
                break;
            case EventType.MouseMove:
                if (EditorWindow.mouseOverWindow == this)
                    MarkReanimatorUserActivity();
                break;
        }
    }

    private void MarkReanimatorUserActivity()
    {
        lastUserActivityTime = EditorApplication.timeSinceStartup;
    }

    private void UpdateWindowEventOptions()
    {
        wantsMouseEnterLeaveWindow = true;
        wantsMouseMove = IsReanimatorInteractionActive() || IsBoneSelectionHoverPreviewActive();
    }

    private bool ShouldRunContinuousPreviewUpdates()
    {
        if (IsReanimatorInteractionActive())
            return true;
        if (!IsReanimatorWindowAttended())
            return false;

        return EditorApplication.timeSinceStartup - lastUserActivityTime <= ActivePreviewUpdateGraceSeconds;
    }

    private bool IsReanimatorWindowAttended()
    {
        return EditorWindow.focusedWindow == this || EditorWindow.mouseOverWindow == this;
    }

    private bool IsReanimatorInteractionActive()
    {
        return isRightPanelWidthResizing ||
               isRightPanelResizing ||
               isDepthDragActive ||
               IsCreateBoneHoverPreviewActive() ||
               (EditorWindow.focusedWindow == this && GUIUtility.hotControl != 0) ||
               (layerPreviewRenderer != null && layerPreviewRenderer.HasActivePreviewInteraction);
    }

    private bool IsCreateBoneHoverPreviewActive()
    {
        return previewMode == PreviewMode.Skel &&
               bottomToolMode == ReanimatorToolMode.CreateBone &&
               layerPreviewRenderer != null;
    }

    private bool IsBoneSelectionHoverPreviewActive()
    {
        return previewMode == PreviewMode.Skel &&
               layerPreviewRenderer != null;
    }

    private void DeferPreviewContent(int repaintFrames = 1)
    {
        deferredPreviewRepaintFrames = Mathf.Max(deferredPreviewRepaintFrames, repaintFrames);
    }

    private bool ShouldDrawPreviewContentThisFrame()
    {
        var e = Event.current;
        if (deferredPreviewRepaintFrames <= 0 || e == null || e.type != EventType.Repaint)
            return true;

        deferredPreviewRepaintFrames--;
        Repaint();
        return false;
    }

    private void HandleEditorUpdate()
    {
        if (layerPreviewRenderer == null)
            return;
        if (!IsReanimatorWindowAttended())
            return;

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path))
        {
            layerPreviewRenderer.CancelPreviewCacheRefresh();
            return;
        }

        if (!layerPreviewRenderer.IsPreviewCacheReady(path))
            layerPreviewRenderer.RequestPreviewCacheRefresh(path);

        var repainted = false;
        if (layerPreviewRenderer.TickPreviewCacheRefresh())
        {
            Repaint();
            repainted = true;
        }

        if (layerPreviewRenderer.IsPreviewCacheReady(path) &&
            !layerPreviewRenderer.IsPreviewCacheRefreshInProgress &&
            TryScheduleMissingGeometryScan(path))
        {
            repainted = true;
        }

        var now = EditorApplication.timeSinceStartup;
        if (ShouldRepaintCreateBoneHoverPreview(now))
        {
            lastCreateBoneHoverRepaintTime = now;
            if (!repainted)
                Repaint();
        }
    }

    private bool ShouldRepaintCreateBoneHoverPreview(double now)
    {
        return IsCreateBoneHoverPreviewActive() &&
               now - lastCreateBoneHoverRepaintTime >= CreateBoneHoverRepaintIntervalSeconds;
    }

    private void HandleToolHotkeys()
    {
        var e = Event.current;
        if (IsAutoGeometryHotkey(e) && e.type == EventType.KeyUp)
        {
            autoGeometryHotkeyIsDown = false;
            return;
        }

        if (!ReanimatorHotkeyAlgorithms.ShouldHandleToolHotkeyEvent(
                e,
                IsSpritePreviewMode(previewMode),
                EditorGUIUtility.editingTextField,
                GUIUtility.hotControl,
                bottomToolMode))
            return;

        if (IsAutoGeometryHotkey(e))
        {
            HandleAutoGeometryHotkey();
            e.Use();
            Repaint();
            return;
        }

        if (IsRestorePoseHotkey(e))
        {
            RestorePreviewPose();
            e.Use();
            Repaint();
            return;
        }

        if (!TryGetToolHotkeyMode(e, out var mode))
            return;

        SetBottomToolMode(mode);
        e.Use();
        Repaint();
    }

    private void HandlePreviewCameraHotkeys()
    {
        var e = Event.current;
        if (!ReanimatorHotkeyAlgorithms.ShouldHandlePreviewCameraPanEvent(
                e,
                EditorGUIUtility.editingTextField,
                GUIUtility.hotControl))
            return;

        if (previewNavigator == null)
            previewNavigator = new PreviewNavigator();
        if (!ReanimatorHotkeyAlgorithms.TryGetPreviewCameraPanDelta(e, out var panDelta))
            return;

        previewNavigator.PanBy(panDelta);
        e.Use();
        Repaint();
    }

    private void HandleAutoGeometryHotkey()
    {
        if (autoGeometryHotkeyIsDown)
            return;

        autoGeometryHotkeyIsDown = true;
        var now = EditorApplication.timeSinceStartup;
        var isDoublePress = ReanimatorHotkeyAlgorithms.IsConfirmedDoublePress(
            bottomToolMode == ReanimatorToolMode.AutoGeometry,
            lastAutoGeometryHotkeyTime,
            now,
            ToolHotkeyDoublePressSeconds);

        SetBottomToolMode(ReanimatorToolMode.AutoGeometry);
        lastAutoGeometryHotkeyTime = now;

        if (isDoublePress)
        {
            GenerateAutoGeometryForSelectedFromUi();
            lastAutoGeometryHotkeyTime = 0d;
        }
    }

    private static bool TryGetToolHotkeyMode(Event e, out ReanimatorToolMode mode)
    {
        return ReanimatorHotkeyAlgorithms.TryGetToolHotkeyMode(e, out mode);
    }

    private static bool IsAutoGeometryHotkey(Event e)
    {
        return ReanimatorHotkeyAlgorithms.IsAutoGeometryHotkey(e);
    }

    private static bool IsRestorePoseHotkey(Event e)
    {
        return ReanimatorHotkeyAlgorithms.IsRestorePoseHotkey(e);
    }
}
#endif









