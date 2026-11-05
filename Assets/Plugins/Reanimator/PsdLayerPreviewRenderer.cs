#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

[Serializable]
public partial class PsdLayerPreviewRenderer
{
    private const int CurrentPreviewPipelineVersion = 31;
    private const double AssetDependencyHashCheckIntervalSeconds = 0.75;

    // When false, geometry preview follows bone transforms via skinning data.
    [SerializeField] private bool useImporterMeshForPreview = false;
    [NonSerialized] private bool deformSpritesWithBonePoseForCurrentDraw = true;
    [SerializeField] private PsdPreviewDebugMode debugMode = new PsdPreviewDebugMode();

    public enum PivotPreset
    {
        Center = 0,
        TopLeft = 1,
        TopCenter = 2,
        TopRight = 3,
        LeftCenter = 4,
        RightCenter = 5,
        BottomLeft = 6,
        BottomCenter = 7,
        BottomRight = 8,
        Custom = 9,
        RootBonePosition = 10
    }

    public enum PreviewTestAnimMode
    {
        Simple = 0,
        Cyclic = 1
    }

    [Flags]
    public enum LayerDebugIssueFlags
    {
        None = 0,
        MissingGeometry = 1 << 0,
        EmptyGeometry = 1 << 1,
        TriangleIssue = 1 << 2,
        UvIssue = 1 << 3,
        WeightIssue = 1 << 4,
        BoneMapIssue = 1 << 5,
        BindPoseIssue = 1 << 6,
        TextureRectIssue = 1 << 7,
        BadLayer = 1 << 8,
        SkinningPoseIssue = 1 << 9
    }

    public readonly struct LayerDebugStatus
    {
        public readonly LayerDebugIssueFlags issues;
        public readonly int vertexCount;
        public readonly int triangleCount;
        public readonly string uvSource;

        public LayerDebugStatus(
            LayerDebugIssueFlags issues,
            int vertexCount,
            int triangleCount,
            string uvSource)
        {
            this.issues = issues;
            this.vertexCount = vertexCount;
            this.triangleCount = triangleCount;
            this.uvSource = uvSource;
        }

        public bool HasIssues => issues != LayerDebugIssueFlags.None;
    }

    [Flags]
    public enum BoneDebugIssueFlags
    {
        None = 0,
        Hidden = 1 << 0,
        MissingParent = 1 << 1,
        ZeroLength = 1 << 2,
        PoseChanged = 1 << 3,
        NoWeightedSprites = 1 << 4,
        WeightIssue = 1 << 5,
        MissingGeometry = 1 << 6,
        BoneMapIssue = 1 << 7,
        BindPoseIssue = 1 << 8
    }

    public readonly struct BoneDebugSpriteInfo
    {
        public readonly string layerName;
        public readonly int vertexCount;
        public readonly int influencedVertexCount;
        public readonly int mappedSpriteBoneIndex;
        public readonly float totalWeight;
        public readonly float maxWeight;
        public readonly LayerDebugIssueFlags layerIssues;

        public BoneDebugSpriteInfo(
            string layerName,
            int vertexCount,
            int influencedVertexCount,
            int mappedSpriteBoneIndex,
            float totalWeight,
            float maxWeight,
            LayerDebugIssueFlags layerIssues)
        {
            this.layerName = layerName;
            this.vertexCount = vertexCount;
            this.influencedVertexCount = influencedVertexCount;
            this.mappedSpriteBoneIndex = mappedSpriteBoneIndex;
            this.totalWeight = totalWeight;
            this.maxWeight = maxWeight;
            this.layerIssues = layerIssues;
        }
    }

    public readonly struct BoneDebugInfo
    {
        public readonly int rendererBoneIndex;
        public readonly string boneName;
        public readonly Color color;
        public readonly int parentIndex;
        public readonly string parentName;
        public readonly int childCount;
        public readonly Vector2 restPosition;
        public readonly Vector2 localPosition;
        public readonly Vector2 worldPosition;
        public readonly float length;
        public readonly float posePositionDelta;
        public readonly float poseRotationDeltaDeg;
        public readonly int mappedSpriteCount;
        public readonly int weightedSpriteCount;
        public readonly int influencedVertexCount;
        public readonly int mappedVertexCount;
        public readonly float totalWeight;
        public readonly float maxWeight;
        public readonly BoneDebugIssueFlags issues;
        public readonly BoneDebugSpriteInfo[] sprites;

        public BoneDebugInfo(
            int rendererBoneIndex,
            string boneName,
            Color color,
            int parentIndex,
            string parentName,
            int childCount,
            Vector2 restPosition,
            Vector2 localPosition,
            Vector2 worldPosition,
            float length,
            float posePositionDelta,
            float poseRotationDeltaDeg,
            int mappedSpriteCount,
            int weightedSpriteCount,
            int influencedVertexCount,
            int mappedVertexCount,
            float totalWeight,
            float maxWeight,
            BoneDebugIssueFlags issues,
            BoneDebugSpriteInfo[] sprites)
        {
            this.rendererBoneIndex = rendererBoneIndex;
            this.boneName = boneName;
            this.color = color;
            this.parentIndex = parentIndex;
            this.parentName = parentName;
            this.childCount = childCount;
            this.restPosition = restPosition;
            this.localPosition = localPosition;
            this.worldPosition = worldPosition;
            this.length = length;
            this.posePositionDelta = posePositionDelta;
            this.poseRotationDeltaDeg = poseRotationDeltaDeg;
            this.mappedSpriteCount = mappedSpriteCount;
            this.weightedSpriteCount = weightedSpriteCount;
            this.influencedVertexCount = influencedVertexCount;
            this.mappedVertexCount = mappedVertexCount;
            this.totalWeight = totalWeight;
            this.maxWeight = maxWeight;
            this.issues = issues;
            this.sprites = sprites;
        }

        public bool HasIssues => issues != BoneDebugIssueFlags.None;
    }

    [Serializable]
    public class LayerInfo
    {
        public string name;
        public int layerId;
        public string spriteGuid;
        public int depth;
        public bool visible = true;
        public int layerDataIndex = -1;
        public int parentLayerDataIndex = -1;
        public Color color = Color.red;
        public Rect sourceRect;
        public Rect textureRect;
        public Rect textureSamplingRect;
        [NonSerialized] public Rect spriteSheetOptimizeReferenceRect;
        [NonSerialized] public Rect spriteSheetSerializedImportRect;
        [NonSerialized] public Rect spriteSheetPsdLayerReferenceRect;
        [NonSerialized] public Vector2 spriteSheetOptimizeReferencePosition;
        public Vector2 position;
        [NonSerialized] public Texture2D texture;
        public Vector4 border;
        public Vector2 pivot = new Vector2(0.5f, 0.5f);
        public int alignment;
    }

    [Serializable]
    public class LayerHierarchyNode
    {
        public string key;
        public string parentKey;
        public string name;
        public bool isGroup;
        public bool visible = true;
        public int layerIndex = -1;
    }

    [Serializable]
    private sealed class UiVisibilityBool
    {
        public string key;
        public bool value;
    }

    [Serializable]
    private sealed class UiVisibilityCacheEntry
    {
        public string assetPath;
        public List<UiVisibilityBool> layerVisibility = new List<UiVisibilityBool>();
        public List<UiVisibilityBool> hierarchyVisibility = new List<UiVisibilityBool>();
        public List<UiVisibilityBool> boneVisibility = new List<UiVisibilityBool>();
    }

    [SerializeField] private string cachedPath;
    [SerializeField] private string cachedAssetDependencyHash;
    [SerializeField] private int cachedPreviewPipelineVersion = -1;
    [SerializeField] private Vector2Int cachedDocumentSize;
    [SerializeField] private List<UiVisibilityCacheEntry> uiVisibilityCache = new List<UiVisibilityCacheEntry>();
    [NonSerialized] private string pendingUiVisibilityPath;
    [NonSerialized] private Dictionary<string, bool> pendingLayerVisibility;
    [NonSerialized] private Dictionary<string, int> pendingLayerDepth;
    [NonSerialized] private Dictionary<string, Color> pendingLayerColor;
    [NonSerialized] private Dictionary<string, bool> pendingHierarchyVisibility;
    [NonSerialized] private Dictionary<string, bool> pendingBoneVisibility;
    [NonSerialized] private Dictionary<int, int> pendingBoneDepth;
    [NonSerialized] private Dictionary<int, Color> pendingBoneColor;
    [NonSerialized] private HashSet<string> dirtyLayerSpriteGuids;
    [NonSerialized] private HashSet<string> dirtyLayerStableKeys;
    [NonSerialized] private HashSet<string> dirtyLayerPositionGuids;
    [NonSerialized] private HashSet<string> dirtyLayerPositionStableKeys;
    [SerializeField] private List<LayerInfo> layerInfos = new List<LayerInfo>();
    [SerializeField] private List<LayerHierarchyNode> layerHierarchyNodes = new List<LayerHierarchyNode>();
    [SerializeField] private int selectedBoneIndex = -1;
    [NonSerialized] private int hoveredBoneIndex = -1;
    [NonSerialized] private int panelHoveredBoneIndex = -1;
    [NonSerialized] private Texture2D reanimaxHardMaskPreviewTexture;
    [NonSerialized] private string reanimaxHardMaskPreviewLayerKey;
    [NonSerialized] private Texture2D reanimaxHardMaskPreviewSourceTexture;
    [NonSerialized] private Vector2Int reanimaxHardMaskPreviewSize;
    [NonSerialized] private Rect reanimaxHardMaskPreviewUvRect;
    [NonSerialized] private float reanimaxHardMaskPreviewThreshold = -1f;
    [NonSerialized] private Vector2[][] reanimaxHardMaskEdgePointPathsLocal = Array.Empty<Vector2[]>();
    [NonSerialized] private string reanimaxHardMaskEdgePointsLayerKey;
    [NonSerialized] private Texture2D reanimaxHardMaskEdgePointsSourceTexture;
    [NonSerialized] private Vector2Int reanimaxHardMaskEdgePointsSize;
    [NonSerialized] private Rect reanimaxHardMaskEdgePointsUvRect;
    [NonSerialized] private float reanimaxHardMaskEdgePointsThreshold = -1f;
    [NonSerialized] private int reanimaxHardMaskEdgePointsPositionFilterPixels = -1;
    [NonSerialized] private float reanimaxHardMaskEdgePointsMinTurnDegrees = -1f;
    [NonSerialized] private int reanimaxHardMaskEdgePointsPostPositionFilterPixels = -1;
    [NonSerialized] private float reanimaxHardMaskEdgePointsMinDistancePixels = -1f;
    [SerializeField] private bool isRotating;
    [SerializeField] private bool isDragging;
    [SerializeField] private bool isResizingBone;
    [SerializeField] private Vector2 dragOffsetDoc;
    [SerializeField] private Vector2 dragPreservedTipDoc;
    [NonSerialized] private bool boneBaseDragPreservingTip;
    [SerializeField] private int dragControlId;
    [NonSerialized] private bool boneTransformChangedDuringDrag;
    [SerializeField] private bool spriteRectIsDragging;
    [SerializeField] private SpriteRectDragHandle spriteRectDragHandle;
    [SerializeField] private Rect spriteRectDragStartRect;
    [SerializeField] private Vector2 spriteRectDragStartPoint;
    [SerializeField] private int spriteRectDragControlId;
    [NonSerialized] private bool spriteRectDragUndoRegistered;
    [SerializeField] private bool hasPendingChanges;
    [SerializeField] private bool bonesTreatAsWorldSpace;
    [SerializeField] private bool rotateLocalBoneOffsetsByParent = true;
    [SerializeField] private string selectedLayerSelectionKey;
    [SerializeField] private int bonePoseVersion;
    [NonSerialized] private int boneOrderVersion;
    [SerializeField] private bool bonePoseAtRest = true;
    [SerializeField, Range(0f, 1f)] private float boneInfluenceOverlay = 1f;
    [SerializeField] private List<ElbowLine> elbowLines = new List<ElbowLine>();
    [SerializeField] private List<LimbLine> limbLines = new List<LimbLine>();
    [SerializeField] private List<AppliedElbowVertex> appliedElbowVertices = new List<AppliedElbowVertex>();
    [SerializeField] private List<AutoGeometryPatch> autoGeometryPatches = new List<AutoGeometryPatch>();
    [SerializeField] private bool elbowIsCreating;
    [SerializeField] private string elbowCreateLayerStableKey;
    [SerializeField] private Vector2 elbowCreateStartDoc;
    [SerializeField] private Vector2 elbowCreateEndDoc;
    [SerializeField] private bool elbowIsDragging;
    [SerializeField] private int elbowDraggedLineIndex = -1;
    [SerializeField] private Vector2 elbowDragLastMouseDoc;
    [SerializeField] private int elbowSelectedLineIndex = -1;
    [SerializeField] private int elbowControlId;
    [SerializeField] private ElbowDragMode elbowDragMode;
    [SerializeField, Range(1, 20)] private int elbowRadialPoints = 1;
    [SerializeField, Range(-2f, 2f)] private float elbowBend = 1f;
    [SerializeField] private bool limbIsCreating;
    [SerializeField] private string limbCreateLayerStableKey;
    [SerializeField] private Vector2 limbCreateStartDoc;
    [SerializeField] private Vector2 limbCreateEndDoc;
    [SerializeField] private bool limbIsDragging;
    [SerializeField] private int limbDraggedLineIndex = -1;
    [SerializeField] private Vector2 limbDragLastMouseDoc;
    [SerializeField] private int limbSelectedLineIndex = -1;
    [SerializeField] private int limbControlId;
    [SerializeField] private ElbowDragMode limbDragMode;
    [SerializeField, Range(0, 20)] private int limbContourSteps = 2;
    [SerializeField] private float limbContourStepDistance = 16f;
    [SerializeField, Range(0f, 1f)] private float limbTowardCenterShift = 0.35f;
    [SerializeField] private AnimationCurve limbCurve = null;
    [SerializeField, Range(0f, 1f)] private float boobOutline = 0.3f;
    [SerializeField, Range(0, 16)] private int boobStages = 2;
    [SerializeField, Range(0, 1)] private int boobToolVersion = 1;
    [SerializeField] private bool boobToolVersionInitialized;
    [SerializeField] private bool boobHasPoint;
    [SerializeField] private Vector2 boobCenterDoc;
    [SerializeField] private string boobLayerStableKey = string.Empty;
    [SerializeField, Range(0, 6)] private int boobV2SmoothingPasses = 2;
    [SerializeField, Range(8, 128)] private int boobV2ContourDensity = 32;
    [SerializeField, Range(0, 3)] private int boobV2Selection;
    [SerializeField] private bool boobV2HasTargetA;
    [SerializeField] private bool boobV2HasTargetB;
    [SerializeField] private Vector2 boobV2TargetADoc;
    [SerializeField] private Vector2 boobV2TargetBDoc;
    [SerializeField] private List<Vector2> boobV2RawContourADoc = new List<Vector2>();
    [SerializeField] private List<Vector2> boobV2RawContourBDoc = new List<Vector2>();
    [SerializeField] private List<Vector2> boobV2ContourADoc = new List<Vector2>();
    [SerializeField] private List<Vector2> boobV2ContourBDoc = new List<Vector2>();
    [SerializeField] private string boobV2LayerStableKey = string.Empty;
    [SerializeField] private string curveLayerStableKey = string.Empty;
    [SerializeField] private List<int> curveSelectedLoopPositions = new List<int>();
    [SerializeField] private string curveGraphSelectionSignature = string.Empty;
    [SerializeField] private List<float> curveEditedChildWeights = new List<float>();
    [SerializeField] private bool curveUseAbsoluteGeometry;
    [SerializeField] private string curveSecondBoneSelectionSignature = string.Empty;
    [SerializeField] private int curveSelectedFirstSpriteBone = -1;
    [SerializeField] private int curveSelectedSecondSpriteBone = -1;
    [SerializeField] private List<VolumeCurve> volumeCurves = new List<VolumeCurve>();
    [SerializeField] private int selectedVolumeCurveIndex = -1;
    [SerializeField] private int selectedVolumeCurvePointIndex = -1;
    [NonSerialized] private bool curveIsDragging;
    [NonSerialized] private Vector2 curveDragStartCanvas;
    [NonSerialized] private Vector2 curveDragCurrentCanvas;
    [NonSerialized] private int curveControlId;
    [NonSerialized] private string curveDragLayerSelectionKey = string.Empty;
    [NonSerialized] private bool curveGraphPointDragActive;
    [NonSerialized] private bool curveGraphPointMoveUndoRegistered;
    [NonSerialized] private bool volumeCurveIsDragging;
    [NonSerialized] private int volumeCurveControlId;
    [NonSerialized] private bool volumeCurvePointMoveUndoRegistered;
    [SerializeField] private int editGeometrySelectedVertexIndex = -1;
    [SerializeField] private List<int> editGeometrySelectedVertexIndices = new List<int>();
    [SerializeField] private bool editGeometryIsDragging;
    [SerializeField] private int editGeometryControlId;
    [SerializeField] private string editGeometrySelectedLayerStableKey = string.Empty;
    [NonSerialized] private bool editGeometryIsRectSelecting;
    [NonSerialized] private bool editGeometryRectToggleSelection;
    [NonSerialized] private Vector2 editGeometryRectStartCanvas;
    [NonSerialized] private Vector2 editGeometryRectCurrentCanvas;
    [NonSerialized] private Vector2 editGeometryDragLastBaseDoc;
    [NonSerialized] private bool editGeometryMoveUndoRegistered;
    [NonSerialized] private Action<string> undoRequest;
    [SerializeField] private List<int> weightSliderSelectedVertexIndices = new List<int>();
    [SerializeField] private string weightSliderSelectedLayerStableKey = string.Empty;
    [NonSerialized] private bool weightSliderIsRectSelecting;
    [NonSerialized] private bool weightSliderRectToggleSelection;
    [NonSerialized] private Vector2 weightSliderRectStartCanvas;
    [NonSerialized] private Vector2 weightSliderRectCurrentCanvas;
    [NonSerialized] private int weightSliderControlId;
    [NonSerialized] private bool weightSliderIsWeightDragging;
    [NonSerialized] private string weightSliderWeightDragLayerStableKey = string.Empty;
    [NonSerialized] private int weightSliderWeightDragVertexIndex = -1;
    [NonSerialized] private int[] weightSliderWeightDragBones = Array.Empty<int>();
    [NonSerialized] private WeightSliderVertexDragState[] weightSliderWeightDragStates = Array.Empty<WeightSliderVertexDragState>();
    [NonSerialized] private Vector2 weightSliderWeightDragStartCanvas;
    [NonSerialized] private Vector2 weightSliderWeightDragPointerOffsetCanvas;
    [NonSerialized] private bool weightSliderWeightDragMoved;
    [NonSerialized] private bool weightSliderWeightDragUndoRegistered;
    [NonSerialized] private float weightSliderSmoothStrength = 0.5f;
    [SerializeField] private string weightBrushLayerStableKey = string.Empty;
    [SerializeField, Range(1, 3)] private int weightBrushCapInfluenceCount = 3;
    [NonSerialized] private bool weightBrushStrokeUndoRegistered;
    [SerializeField] private int createEdgeStartVertexIndex = -1;
    [SerializeField] private string createEdgeLayerStableKey = string.Empty;
    [SerializeField] private bool radiusCullHasPoint;
    [SerializeField] private Vector2 radiusCullCenterDoc;
    [SerializeField] private string radiusCullLayerStableKey = string.Empty;
    [SerializeField] private float radiusCullHandleAngleRad = Mathf.PI * 0.25f;
    [SerializeField] private float jointStartAngleRad = -Mathf.PI * 0.16666667f;
    [SerializeField] private float jointEndAngleRad = Mathf.PI * 0.16666667f;
    [SerializeField] private float jointStartLongPointsOffset = 12f;
    [SerializeField] private float jointEndLongPointsOffset = 12f;
    [SerializeField] private bool jointCenterLockEnabled;
    [SerializeField] private Vector2 jointLockedCenterDrawDoc;
    [SerializeField] private int jointNearestBoneIndex = -1;
    [SerializeField] private Vector2 debugOverlayPanelOffset = new Vector2(8f, 8f);

    [NonSerialized] private Texture2D cachedTexture;
    [NonSerialized] private Texture2D cachedAtlasTexture;
    [NonSerialized] private readonly List<LayerInfo> sortedLayers = new List<LayerInfo>();
    [SerializeField] private List<BoneInfo> bones = new List<BoneInfo>();
    [NonSerialized] private readonly List<int> boneDrawOrder = new List<int>();
    [NonSerialized] private readonly Dictionary<string, LayerVertexData> vertexDataByLayerSelectionKey = new Dictionary<string, LayerVertexData>(StringComparer.OrdinalIgnoreCase);
    [NonSerialized] private readonly Dictionary<string, DefaultGeometrySnapshot> defaultGeometryBySpriteKey = new Dictionary<string, DefaultGeometrySnapshot>(StringComparer.OrdinalIgnoreCase);
    [NonSerialized] private string defaultGeometryAssetPath = string.Empty;
    [NonSerialized] private Material layerMeshMaterial;
    [NonSerialized] private Material influenceOverlayMaterial;
    [NonSerialized] private Material maskedInfluenceOverlayMaterial;
    [NonSerialized] private Vector3[] vertexEdgeSegments = Array.Empty<Vector3>();
    [NonSerialized] private Vector3[] vertexBoundarySegments = Array.Empty<Vector3>();
    [NonSerialized] private Vector3[] vertexPointSegments = Array.Empty<Vector3>();
    [NonSerialized] private Vector3[] vertexBoundarySegmentsShifted = Array.Empty<Vector3>();
    [NonSerialized] private readonly List<LayerInfo> spriteHitBuffer = new List<LayerInfo>();
    [NonSerialized] private bool spriteSelectionAllowedForCurrentDraw = true;
    [NonSerialized] private bool spriteSelectionLockedForCurrentDraw;
    [NonSerialized] private bool isolateSelectedSpriteForCurrentDraw;
    [NonSerialized] private bool isolateBoneModificationForCurrentDraw;
    [NonSerialized] private readonly List<int> elbowConnectionBuffer = new List<int>(8);
    [NonSerialized] private string boneRecoveryAttemptedPath;
    [NonSerialized] private bool boobIsDragging;
    [NonSerialized] private int boobControlId;
    [NonSerialized] private int boobV2ActiveTarget = -1;
    [NonSerialized] private int boobV2DrawingContour = -1;
    [NonSerialized] private Vector2 boobV2LastStrokeView;
    [NonSerialized] private readonly List<Vector2> boobV2WorkingContourDoc = new List<Vector2>();
    [NonSerialized] private bool radiusCullIsDragging;
    [NonSerialized] private bool radiusCullHandleIsDragging;
    [NonSerialized] private int radiusCullControlId;
    [NonSerialized] private bool jointIsDragging;
    [NonSerialized] private bool jointStartHandleIsDragging;
    [NonSerialized] private bool jointEndHandleIsDragging;
    [NonSerialized] private bool jointMiddleHandleIsDragging;
    [NonSerialized] private bool jointStartOffsetHandleIsDragging;
    [NonSerialized] private bool jointEndOffsetHandleIsDragging;
    [NonSerialized] private int jointControlId;
    [NonSerialized] private bool radiusCullHasRadiusOverride;
    [NonSerialized] private float radiusCullRadiusOverride;
    [NonSerialized] private RadiusCullPreviewCache radiusCullPreviewCache = new RadiusCullPreviewCache();
    [NonSerialized] private LimbPreviewCache limbPreviewCache = new LimbPreviewCache();
    [NonSerialized] private int radiusCullPreviewRevision;
    [NonSerialized] private bool limbPreviewNeedsRepaint;
    [NonSerialized] private bool limbNearestBoneMissingLogged;
    [NonSerialized] private bool debugOverlayDragging;
    [NonSerialized] private Vector2 debugOverlayDragStartMouse;
    [NonSerialized] private Vector2 debugOverlayDragStartOffset;
    [NonSerialized] private Rect debugContentBoundsDoc;
    [NonSerialized] private Rect debugBoneBoundsDocBefore;
    [NonSerialized] private Rect debugBoneBoundsDocAfter;
    [NonSerialized] private Rect debugRawBoneBoundsDoc;
    [NonSerialized] private float debugModeScoreLocalRot;
    [NonSerialized] private float debugModeScoreLocalNoRot;
    [NonSerialized] private float debugModeScoreWorld;
    [NonSerialized] private string debugChosenBoneMode = string.Empty;
    [NonSerialized] private double nextAssetDependencyHashCheckTime;
    [NonSerialized] private readonly Dictionary<string, Rect> debugLayerVertexBoundsDocByLayerKey = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
    [NonSerialized] private readonly Dictionary<string, float> debugLayerVertexScoreByLayerKey = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    [NonSerialized] private readonly Dictionary<string, LayerGeometryDebugInfo> debugLayerGeometryByLayerKey = new Dictionary<string, LayerGeometryDebugInfo>(StringComparer.OrdinalIgnoreCase);

    private enum LimbGeneratedPointType : byte
    {
        Contour = 0,
        Center = 1,
        Shifted = 2
    }

    private enum SpriteRectDragHandle
    {
        None = 0,
        Move = 1,
        Left = 2,
        Right = 3,
        Bottom = 4,
        Top = 5,
        TopLeft = 6,
        TopRight = 7,
        BottomLeft = 8,
        BottomRight = 9
    }

    private struct LimbGeneratedPoint
    {
        public Vector2 pointDoc;
        public int contourSourceIndex;
        public LimbGeneratedPointType type;
    }

    public IReadOnlyList<LayerInfo> Layers => layerInfos;
    public IReadOnlyList<LayerInfo> SortedLayers => sortedLayers;
    public IReadOnlyList<LayerHierarchyNode> LayerHierarchyNodes => layerHierarchyNodes;
    public IReadOnlyList<BoneInfo> Bones => bones;
    public string SelectedLayerSelectionKey => selectedLayerSelectionKey ?? string.Empty;
    public int SelectedBoneIndex => selectedBoneIndex;
    public Vector2Int DocumentSize => cachedDocumentSize;
    public bool HasPendingChanges => hasPendingChanges;
    public Action<string> UndoRequest
    {
        get => undoRequest;
        set => undoRequest = value;
    }

    private void RegisterUndo(string operationName)
    {
        undoRequest?.Invoke(string.IsNullOrWhiteSpace(operationName) ? "Reanimator Change" : operationName);
    }
    public bool IsDebugModeEnabled => debugMode != null && debugMode.Enabled;
    public bool IsDebugModeDoublePressArmed => debugMode != null && debugMode.IsArmed;
    public bool IsWeightSliderWeightDragging => weightSliderIsWeightDragging;
    public float WeightSliderSmoothStrength
    {
        get => Mathf.Clamp01(weightSliderSmoothStrength);
        set => weightSliderSmoothStrength = Mathf.Clamp01(value);
    }
    public float BoneInfluenceOverlay
    {
        get => Mathf.Clamp01(boneInfluenceOverlay);
        set => boneInfluenceOverlay = Mathf.Clamp01(value);
    }
    public bool TryGetSelectedLayer(out LayerInfo layer)
    {
        layer = null;
        if (string.IsNullOrEmpty(selectedLayerSelectionKey))
            return false;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var candidate = layerInfos[i];
            if (candidate == null)
                continue;
            if (!string.Equals(BuildSelectionKey(candidate), selectedLayerSelectionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            layer = candidate;
            return true;
        }

        return false;
    }
    public bool IsLayerSelected(LayerInfo layer)
    {
        if (layer == null || string.IsNullOrEmpty(selectedLayerSelectionKey))
            return false;
        return string.Equals(BuildSelectionKey(layer), selectedLayerSelectionKey, StringComparison.OrdinalIgnoreCase);
    }
    public bool SelectLayer(LayerInfo layer)
    {
        var nextKey = layer != null ? BuildSelectionKey(layer) : string.Empty;
        if (string.Equals(selectedLayerSelectionKey, nextKey, StringComparison.OrdinalIgnoreCase))
            return false;

        selectedLayerSelectionKey = nextKey;
        return true;
    }
    public bool SelectBone(int boneIndex)
    {
        var nextIndex = boneIndex >= 0 && boneIndex < bones.Count ? boneIndex : -1;
        if (selectedBoneIndex == nextIndex)
            return false;

        selectedBoneIndex = nextIndex;
        return true;
    }
    public int ElbowRadialPoints
    {
        get => Mathf.Clamp(elbowRadialPoints, 1, 20);
        set => elbowRadialPoints = Mathf.Clamp(value, 1, 20);
    }
    public float ElbowBend
    {
        get => Mathf.Clamp(elbowBend, -2f, 2f);
        set => elbowBend = Mathf.Clamp(value, -2f, 2f);
    }
    public int LimbContourSteps
    {
        get => Mathf.Clamp(limbContourSteps, 0, 20);
        set => limbContourSteps = Mathf.Clamp(value, 0, 20);
    }
    public float LimbContourStepDistance
    {
        get => Mathf.Max(0.01f, limbContourStepDistance);
        set => limbContourStepDistance = Mathf.Max(0.01f, value);
    }
    public float LimbTowardCenterShift
    {
        get => Mathf.Clamp01(limbTowardCenterShift);
        set => limbTowardCenterShift = Mathf.Clamp01(value);
    }
    public AnimationCurve LimbCurve
    {
        get
        {
            if (limbCurve == null)
                limbCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            return limbCurve;
        }
        set => limbCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
    public float BoobOutline
    {
        get => Mathf.Clamp01(boobOutline);
        set => boobOutline = Mathf.Clamp01(value);
    }
    public int BoobStages
    {
        get => Mathf.Clamp(boobStages, 0, 16);
        set => boobStages = Mathf.Clamp(value, 0, 16);
    }
    public int BoobToolVersion
    {
        get
        {
            if (!boobToolVersionInitialized)
            {
                boobToolVersion = 1;
                boobToolVersionInitialized = true;
            }
            return Mathf.Clamp(boobToolVersion, 0, 1);
        }
        set
        {
            var next = Mathf.Clamp(value, 0, 1);
            boobToolVersionInitialized = true;
            if (boobToolVersion == next)
                return;
            if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
                GUIUtility.hotControl = 0;
            boobIsDragging = false;
            boobControlId = 0;
            boobV2ActiveTarget = -1;
            boobV2DrawingContour = -1;
            boobV2WorkingContourDoc.Clear();
            boobToolVersion = next;
        }
    }
    public int BoobV2SmoothingPasses
    {
        get => Mathf.Clamp(boobV2SmoothingPasses, 0, 6);
        set
        {
            var next = Mathf.Clamp(value, 0, 6);
            if (boobV2SmoothingPasses == next)
                return;
            boobV2SmoothingPasses = next;
            RebuildBoobV2ContoursFromRaw();
        }
    }
    public int BoobV2ContourDensity
    {
        get => boobV2ContourDensity > 0 ? Mathf.Clamp(boobV2ContourDensity, 8, 128) : 32;
        set
        {
            var next = Mathf.Clamp(value, 8, 128);
            if (BoobV2ContourDensity == next)
                return;
            boobV2ContourDensity = next;
            RebuildBoobV2ContoursFromRaw();
        }
    }
    public int BoobV2Selection
    {
        get => Mathf.Clamp(boobV2Selection, 0, 3);
        set
        {
            var next = Mathf.Clamp(value, 0, 3);
            if (boobV2Selection == next)
                return;
            if (GUIUtility.hotControl == boobControlId && boobControlId != 0)
                GUIUtility.hotControl = 0;
            boobIsDragging = false;
            boobControlId = 0;
            boobV2ActiveTarget = -1;
            boobV2DrawingContour = -1;
            boobV2WorkingContourDoc.Clear();
            boobV2Selection = next;
        }
    }
    public bool CurveUseAbsoluteGeometry
    {
        get => curveUseAbsoluteGeometry;
        set
        {
            if (curveUseAbsoluteGeometry == value)
                return;

            curveUseAbsoluteGeometry = value;
            ResetCurveGraphToCurrentWeights();
        }
    }
    public float JointStartLongPointsOffset
    {
        get => Mathf.Max(0f, jointStartLongPointsOffset);
        set => jointStartLongPointsOffset = Mathf.Max(0f, value);
    }
    public float JointEndLongPointsOffset
    {
        get => Mathf.Max(0f, jointEndLongPointsOffset);
        set => jointEndLongPointsOffset = Mathf.Max(0f, value);
    }
    public bool JointCenterLockEnabled
    {
        get => jointCenterLockEnabled;
        set
        {
            if (jointCenterLockEnabled == value)
                return;

            jointCenterLockEnabled = value;
            if (jointCenterLockEnabled)
                CaptureJointLockedCenterDrawDoc();
            InvalidateRadiusCullPreviewCache(false);
        }
    }

    public PsdPreviewDebugMode.PressResult ToggleDebugModeByDoublePress()
    {
        if (debugMode == null)
            debugMode = new PsdPreviewDebugMode();
        return debugMode.PressToggle();
    }

    public void MarkDirty()
    {
        CancelPreviewCacheRefresh();
        // Keep cachedPath so a reselect of the same PSD does not look like a path switch.
        // Rebuild will still happen because cachedTexture is cleared.
        cachedTexture = null;
        cachedAtlasTexture = null;
        cachedDocumentSize = default;
        layerInfos.Clear();
        layerHierarchyNodes.Clear();
        sortedLayers.Clear();
        bones.Clear();
        boneDrawOrder.Clear();
        vertexDataByLayerSelectionKey.Clear();
        defaultGeometryBySpriteKey.Clear();
        defaultGeometryAssetPath = string.Empty;
        hasPendingChanges = false;
        ClearDirtyLayerSpriteState();
        selectedBoneIndex = -1;
        isRotating = false;
        isDragging = false;
        isResizingBone = false;
        boneTransformChangedDuringDrag = false;
        selectedLayerSelectionKey = string.Empty;
        bonePoseVersion = 0;
        boneOrderVersion = 0;
        bonePoseAtRest = true;
        elbowLines.Clear();
        elbowIsCreating = false;
        elbowCreateLayerStableKey = string.Empty;
        elbowCreateStartDoc = Vector2.zero;
        elbowCreateEndDoc = Vector2.zero;
        elbowIsDragging = false;
        elbowDraggedLineIndex = -1;
        elbowDragLastMouseDoc = Vector2.zero;
        elbowSelectedLineIndex = -1;
        elbowControlId = 0;
        elbowDragMode = ElbowDragMode.None;
        limbLines.Clear();
        limbIsCreating = false;
        limbCreateLayerStableKey = string.Empty;
        limbCreateStartDoc = Vector2.zero;
        limbCreateEndDoc = Vector2.zero;
        limbIsDragging = false;
        limbDraggedLineIndex = -1;
        limbDragLastMouseDoc = Vector2.zero;
        limbSelectedLineIndex = -1;
        limbControlId = 0;
        limbDragMode = ElbowDragMode.None;
        boobHasPoint = false;
        boobCenterDoc = Vector2.zero;
        boobLayerStableKey = string.Empty;
        boobIsDragging = false;
        boobControlId = 0;
        boobV2Selection = 0;
        boobV2HasTargetA = false;
        boobV2HasTargetB = false;
        boobV2TargetADoc = Vector2.zero;
        boobV2TargetBDoc = Vector2.zero;
        boobV2RawContourADoc.Clear();
        boobV2RawContourBDoc.Clear();
        boobV2ContourADoc.Clear();
        boobV2ContourBDoc.Clear();
        boobV2LayerStableKey = string.Empty;
        boobV2ActiveTarget = -1;
        boobV2DrawingContour = -1;
        boobV2WorkingContourDoc.Clear();
        curveLayerStableKey = string.Empty;
        curveSelectedLoopPositions.Clear();
        curveGraphSelectionSignature = string.Empty;
        curveEditedChildWeights.Clear();
        ClearCurveSecondBoneSelection();
        curveIsDragging = false;
        curveDragStartCanvas = Vector2.zero;
        curveDragCurrentCanvas = Vector2.zero;
        curveControlId = 0;
        curveDragLayerSelectionKey = string.Empty;
        editGeometrySelectedVertexIndex = -1;
        editGeometrySelectedVertexIndices.Clear();
        editGeometryIsDragging = false;
        editGeometryControlId = 0;
        editGeometrySelectedLayerStableKey = string.Empty;
        editGeometryIsRectSelecting = false;
        editGeometryRectToggleSelection = false;
        editGeometryRectStartCanvas = Vector2.zero;
        editGeometryRectCurrentCanvas = Vector2.zero;
        editGeometryDragLastBaseDoc = Vector2.zero;
        weightSliderSelectedVertexIndices.Clear();
        weightSliderSelectedLayerStableKey = string.Empty;
        weightSliderIsRectSelecting = false;
        weightSliderRectToggleSelection = false;
        weightSliderControlId = 0;
        weightSliderIsWeightDragging = false;
        weightSliderWeightDragLayerStableKey = string.Empty;
        weightSliderWeightDragVertexIndex = -1;
        weightSliderWeightDragBones = Array.Empty<int>();
        weightSliderWeightDragStates = Array.Empty<WeightSliderVertexDragState>();
        weightSliderWeightDragStartCanvas = Vector2.zero;
        weightSliderWeightDragPointerOffsetCanvas = Vector2.zero;
        weightSliderWeightDragMoved = false;
        createEdgeStartVertexIndex = -1;
        createEdgeLayerStableKey = string.Empty;
        radiusCullHasPoint = false;
        radiusCullCenterDoc = Vector2.zero;
        radiusCullLayerStableKey = string.Empty;
        radiusCullHandleAngleRad = Mathf.PI * 0.25f;
        jointStartAngleRad = -Mathf.PI * 0.16666667f;
        jointEndAngleRad = Mathf.PI * 0.16666667f;
        jointNearestBoneIndex = -1;
        radiusCullIsDragging = false;
        radiusCullHandleIsDragging = false;
        radiusCullControlId = 0;
        jointIsDragging = false;
        jointStartHandleIsDragging = false;
        jointEndHandleIsDragging = false;
        jointMiddleHandleIsDragging = false;
        jointStartOffsetHandleIsDragging = false;
        jointEndOffsetHandleIsDragging = false;
        jointControlId = 0;
        radiusCullHasRadiusOverride = false;
        radiusCullRadiusOverride = 0f;
        InvalidateRadiusCullPreviewCache(true);
        limbPreviewNeedsRepaint = false;
        limbNearestBoneMissingLogged = false;
        if (appliedElbowVertices != null)
            appliedElbowVertices.Clear();
        autoGeometryPatches.Clear();
        boneRecoveryAttemptedPath = null;
        cachedAssetDependencyHash = string.Empty;
        cachedPreviewPipelineVersion = -1;
        debugLayerVertexBoundsDocByLayerKey.Clear();
        debugLayerVertexScoreByLayerKey.Clear();
        debugLayerGeometryByLayerKey.Clear();
        debugChosenBoneMode = string.Empty;
        debugModeScoreLocalRot = 0f;
        debugModeScoreLocalNoRot = 0f;
        debugModeScoreWorld = 0f;
        debugContentBoundsDoc = Rect.zero;
        debugBoneBoundsDocBefore = Rect.zero;
        debugBoneBoundsDocAfter = Rect.zero;
        debugRawBoneBoundsDoc = Rect.zero;
    }

    public void MarkPendingChanges()
    {
        hasPendingChanges = true;
    }

    public bool TryGetLayerCenterCanvas(LayerInfo layer, Rect localRect, bool spriteAtlasMode, out Vector2 centerCanvas)
    {
        centerCanvas = default;
        if (layer == null)
            return false;
        if (localRect.width <= 0f || localRect.height <= 0f)
            return false;

        var docSize = cachedDocumentSize;
        if (docSize.x <= 0 || docSize.y <= 0)
        {
            docSize = cachedTexture != null
                ? new Vector2Int(cachedTexture.width, cachedTexture.height)
                : new Vector2Int(1, 1);
        }

        var layoutMode = spriteAtlasMode ? PsdPreviewLayoutMode.SpriteAtlas : PsdPreviewLayoutMode.Skeleton;
        var coordinates = new PsdCoordinateSpace(localRect, docSize, layerInfos, null, layoutMode);
        centerCanvas = !spriteAtlasMode && TryGetLayerDrawViewBounds(layer, coordinates, out var drawBounds)
            ? drawBounds.center
            : coordinates.LayerToViewRect(layer).center;
        return true;
    }

    public bool TryGetLayerHierarchyNodeCenterCanvas(string nodeKey, Rect localRect, bool spriteAtlasMode, out Vector2 centerCanvas)
    {
        centerCanvas = default;
        if (string.IsNullOrEmpty(nodeKey))
            return false;
        if (localRect.width <= 0f || localRect.height <= 0f)
            return false;

        var layerIndices = new List<int>();
        CollectLayerHierarchyNodeLayerIndices(nodeKey, layerIndices, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (layerIndices.Count == 0)
            return false;

        var docSize = cachedDocumentSize;
        if (docSize.x <= 0 || docSize.y <= 0)
        {
            docSize = cachedTexture != null
                ? new Vector2Int(cachedTexture.width, cachedTexture.height)
                : new Vector2Int(1, 1);
        }

        var layoutMode = spriteAtlasMode ? PsdPreviewLayoutMode.SpriteAtlas : PsdPreviewLayoutMode.Skeleton;
        var coordinates = new PsdCoordinateSpace(localRect, docSize, layerInfos, null, layoutMode);
        var hasBounds = false;
        var bounds = Rect.zero;
        for (var i = 0; i < layerIndices.Count; i++)
        {
            var layerIndex = layerIndices[i];
            if (layerIndex < 0 || layerIndex >= layerInfos.Count)
                continue;

            var layer = layerInfos[layerIndex];
            var layerRect = !spriteAtlasMode && TryGetLayerDrawViewBounds(layer, coordinates, out var drawBounds)
                ? drawBounds
                : coordinates.LayerToViewRect(layer);
            if (layerRect.width <= 0f || layerRect.height <= 0f)
                continue;

            bounds = hasBounds ? UnionRects(bounds, layerRect) : layerRect;
            hasBounds = true;
        }

        if (!hasBounds)
            return false;

        centerCanvas = bounds.center;
        return true;
    }

    private void CollectLayerHierarchyNodeLayerIndices(string nodeKey, List<int> layerIndices, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(nodeKey) || layerIndices == null || visited == null || !visited.Add(nodeKey))
            return;

        for (var i = 0; i < layerHierarchyNodes.Count; i++)
        {
            var node = layerHierarchyNodes[i];
            if (node == null)
                continue;

            if (string.Equals(node.key, nodeKey, StringComparison.OrdinalIgnoreCase))
            {
                if (node.layerIndex >= 0)
                    layerIndices.Add(node.layerIndex);
                continue;
            }

            if (string.Equals(node.parentKey, nodeKey, StringComparison.OrdinalIgnoreCase))
                CollectLayerHierarchyNodeLayerIndices(node.key, layerIndices, visited);
        }
    }

    private static Rect UnionRects(Rect a, Rect b)
    {
        var xMin = Mathf.Min(a.xMin, b.xMin);
        var yMin = Mathf.Min(a.yMin, b.yMin);
        var xMax = Mathf.Max(a.xMax, b.xMax);
        var yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

}

#endif

