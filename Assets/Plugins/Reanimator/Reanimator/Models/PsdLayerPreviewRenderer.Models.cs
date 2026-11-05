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

public partial class PsdLayerPreviewRenderer
{
    [Serializable]
    public class BoneInfo
    {
        public int index;
        public string name;
        public int parentId;
        public Vector2 position;
        public Quaternion rotation;
        public Vector2 initialPosition;
        public Quaternion initialRotation;
        public float length;
        public int depth;
        public Color color;
        public bool visible;
        public Vector2 worldPosition;
        public Quaternion worldRotation;
    }

    private static void EnsureVertexBuffer(ref Vector3[] buffer, int size)
    {
        if (size < 0)
            size = 0;
        if (buffer == null || buffer.Length != size)
            buffer = new Vector3[size];
    }

    private sealed class LayerVertexData
    {
        public LayerInfo layer;
        public Texture2D texture;
        public float pixelsPerUnit;
        public Vector2 pivotPixels;
        public Vector2[] vertices;
        public Vector2[] vertexUvs;
        public string importerMode;
        public int[] triangles;
        public int[] allEdgePairs;
        public int[] boundaryEdgePairs;
        public BoneWeight[] boneWeights;
        public int[] spriteBoneToRendererBone;
        public Matrix4x4[] bindBoneInverseMatrices;
        public bool usesSpriteBindPoses;
        public bool hasSkinning;
        public int lastPoseVersion;
        public Vector2[] deformedVertices;
        public Matrix4x4[] skinMatrices;
        public int[] boneOrderTriangles;
        public int[] boneOrderSourceTriangles;
        public BoneWeight[] boneOrderSourceWeights;
        public int lastBoneOrderVersion = -1;
    }

    private sealed class LayerGeometryDebugInfo
    {
        public string sourceKind;
        public string rawSpace;
        public string uvSource;
        public Rect rawBounds;
        public Rect localBounds;
        public Rect worldBounds;
        public Rect uvBounds;
        public Vector2 rawSample;
        public Vector2 localSample;
        public Vector2 worldSample;
        public Vector2 uvSample;
        public float pixelsPerUnit;
        public Vector2 pivotPixels;
        public Vector2 layerPosition;
        public Rect layerSourceRect;
        public Vector2 textureSize;
        public Rect spriteRect;
        public Rect spriteTextureRect;
        public bool usedImporterMesh;
    }

    internal sealed class SerializedSpriteMeshData
    {
        public Vertex2DMetaData[] vertices = Array.Empty<Vertex2DMetaData>();
        public int[] indices = Array.Empty<int>();
        public Vector2Int[] edges = Array.Empty<Vector2Int>();
    }

    private sealed class RadiusCullPreviewCache
    {
        public bool hasGeometryResult;
        public string selectionKey;
        public string stableKey;
        public Vector2 centerDoc;
        public float radius;
        public bool removeContourVertices;
        public bool removeInnerVertices;
        public bool includeJointContourIntersections;
        public float jointCPairOffset;
        public int jointLongPoints;
        public float jointStartLongPointsOffset;
        public float jointEndLongPointsOffset;
        public bool jointCenterLockEnabled;
        public Vector2 jointLockedCenterDrawDoc;
        public int revision;
        public Vector2[] sourceVerticesRef;
        public int[] sourceTrianglesRef;
        public Vector2[] sourceDrawVerticesRef;
        public int sourceVertexCount;
        public int sourceTriangleCount;
        public int sourcePoseVersion;
        public int[] allEdgePairs = Array.Empty<int>();
        public int[] boundaryEdgePairs = Array.Empty<int>();
        public Vector2[] previewVerticesBase = Array.Empty<Vector2>();
        public Vector2[] previewVerticesDraw = Array.Empty<Vector2>();
        public int[] previewTriangles = Array.Empty<int>();
        public BoneWeight[] previewBoneWeights = Array.Empty<BoneWeight>();
        public int[] previewSpriteBoneToRendererBone = Array.Empty<int>();
        public double nextRebuildTime;

        public void Clear()
        {
            hasGeometryResult = false;
            selectionKey = string.Empty;
            stableKey = string.Empty;
            centerDoc = Vector2.zero;
            radius = 0f;
            removeContourVertices = true;
            removeInnerVertices = true;
            includeJointContourIntersections = false;
            jointCPairOffset = 0f;
            jointLongPoints = 0;
            jointStartLongPointsOffset = 0f;
            jointEndLongPointsOffset = 0f;
            jointCenterLockEnabled = false;
            jointLockedCenterDrawDoc = Vector2.zero;
            revision = 0;
            sourceVerticesRef = null;
            sourceTrianglesRef = null;
            sourceDrawVerticesRef = null;
            sourceVertexCount = 0;
            sourceTriangleCount = 0;
            sourcePoseVersion = int.MinValue;
            allEdgePairs = Array.Empty<int>();
            boundaryEdgePairs = Array.Empty<int>();
            previewVerticesBase = Array.Empty<Vector2>();
            previewVerticesDraw = Array.Empty<Vector2>();
            previewTriangles = Array.Empty<int>();
            previewBoneWeights = Array.Empty<BoneWeight>();
            previewSpriteBoneToRendererBone = Array.Empty<int>();
            nextRebuildTime = 0d;
        }
    }

    private sealed class LimbPreviewCache
    {
        public bool hasGeometryResult;
        public string selectionKey;
        public string stableKey;
        public Vector2 lineStartDoc;
        public Vector2 lineEndDoc;
        public Vector2 contourPointA;
        public Vector2 contourPointB;
        public int contourSteps;
        public float contourStepDistance;
        public float contourShift;
        public int curveHash;
        public Vector2 centerDoc;
        public float radius;
        public bool removeContourVertices;
        public bool removeInnerVertices;
        public int revision;
        public Vector2[] sourceVerticesRef;
        public int[] sourceTrianglesRef;
        public Vector2[] sourceDrawVerticesRef;
        public int sourceVertexCount;
        public int sourceTriangleCount;
        public int sourcePoseVersion;
        public Vector2[] previewVerticesBase = Array.Empty<Vector2>();
        public Vector2[] previewVerticesDraw = Array.Empty<Vector2>();
        public int[] previewTriangles = Array.Empty<int>();
        public BoneWeight[] previewBoneWeights = Array.Empty<BoneWeight>();
        public int[] innerEdgePairs = Array.Empty<int>();
        public int[] boundaryEdgePairs = Array.Empty<int>();
        public double nextRebuildTime;
        public int visibleTriangleCount;
        public int visibleInnerPairCount;
        public int visibleBoundaryPairCount;
        public int visiblePointCount;
        public int totalTriangleCount;
        public int totalInnerPairCount;
        public int totalBoundaryPairCount;
        public int totalPointCount;

        public void Clear()
        {
            hasGeometryResult = false;
            selectionKey = string.Empty;
            stableKey = string.Empty;
            lineStartDoc = Vector2.zero;
            lineEndDoc = Vector2.zero;
            contourPointA = Vector2.zero;
            contourPointB = Vector2.zero;
            contourSteps = 0;
            contourStepDistance = 0f;
            contourShift = 0f;
            curveHash = 0;
            centerDoc = Vector2.zero;
            radius = 0f;
            removeContourVertices = true;
            removeInnerVertices = true;
            revision = 0;
            sourceVerticesRef = null;
            sourceTrianglesRef = null;
            sourceDrawVerticesRef = null;
            sourceVertexCount = 0;
            sourceTriangleCount = 0;
            sourcePoseVersion = int.MinValue;
            previewVerticesBase = Array.Empty<Vector2>();
            previewVerticesDraw = Array.Empty<Vector2>();
            previewTriangles = Array.Empty<int>();
            previewBoneWeights = Array.Empty<BoneWeight>();
            innerEdgePairs = Array.Empty<int>();
            boundaryEdgePairs = Array.Empty<int>();
            nextRebuildTime = 0d;
            visibleTriangleCount = 0;
            visibleInnerPairCount = 0;
            visibleBoundaryPairCount = 0;
            visiblePointCount = 0;
            totalTriangleCount = 0;
            totalInnerPairCount = 0;
            totalBoundaryPairCount = 0;
            totalPointCount = 0;
        }
    }

    [Serializable]
    private sealed class ElbowLine
    {
        public string layerStableKey;
        public string layerSelectionKey;
        public Vector2 startDoc;
        public Vector2 endDoc;
        public bool hasContourPoints;
        public Vector2 contourPointA;
        public Vector2 contourPointB;
    }

    [Serializable]
    private sealed class LimbLine
    {
        public string layerStableKey;
        public string layerSelectionKey;
        public Vector2 startDoc;
        public Vector2 endDoc;
        public bool hasContourPoints;
        public Vector2 contourPointA;
        public Vector2 contourPointB;
        public bool hasRequiredJointAnchor;
        public Vector2 requiredJointAnchorDoc;
        public int requiredParentBoneIndex = -1;
        public int requiredChildBoneIndex = -1;
    }

    [Serializable]
    private sealed class AppliedElbowVertex
    {
        public string assetPath;
        public string layerStableKey;
        public string layerSelectionKey;
        public Vector2 positionDoc;
    }

    [Serializable]
    private sealed class AutoGeometryPatch
    {
        public string assetPath;
        public string layerStableKey;
        public string layerSelectionKey;
        public string spriteGuid;
        public string layerName;
        public int layerId;
        public Vector2 layerPosition;
        public Rect sourceRect;
        public Vector2[] verticesDoc;
        public Vector2[] vertexUvs;
        public int[] triangles;
        public int[] allEdgePairs;
        public int[] boundaryEdgePairs;
        public BoneWeight[] boneWeights;
        public int[] spriteBoneToRendererBone;
        public Matrix4x4[] bindBoneInverseMatrices;
        public string importerMode;
        public bool usesSpriteBindPoses;
        public bool generateWeights;
    }

    private sealed class DefaultGeometrySnapshot
    {
        public string spriteGuid;
        public string layerStableKey;
        public Vector2[] verticesDoc;
        public Vector2[] vertexUvs;
        public int[] triangles;
        public int[] allEdgePairs;
        public int[] boundaryEdgePairs;
        public BoneWeight[] boneWeights;
        public int[] spriteBoneToRendererBone;
        public Matrix4x4[] bindBoneInverseMatrices;
        public string importerMode;
        public bool usesSpriteBindPoses;
        public float pixelsPerUnit;
        public Vector2 pivotPixels;
        public Rect sourceRect;
        public Vector2 layerPosition;
    }

    internal struct LayerSpriteBinding
    {
        public GUID spriteId;
        public Vector2 pivotPixels;
        public float pixelsPerUnit;
        public Rect rect;
    }

    public readonly struct EditGeometrySelectionInfo
    {
        public readonly int layerToken;
        public readonly string layerName;
        public readonly int vertexIndex;
        public readonly int vertexCount;
        public readonly Vector2 positionDoc;
        public readonly BoneWeight boneWeight;
        public readonly int spriteBoneCount;
        public readonly string[] spriteBoneNames;

        public EditGeometrySelectionInfo(
            int layerToken,
            string layerName,
            int vertexIndex,
            int vertexCount,
            Vector2 positionDoc,
            BoneWeight boneWeight,
            int spriteBoneCount,
            string[] spriteBoneNames)
        {
            this.layerToken = layerToken;
            this.layerName = layerName;
            this.vertexIndex = vertexIndex;
            this.vertexCount = vertexCount;
            this.positionDoc = positionDoc;
            this.boneWeight = boneWeight;
            this.spriteBoneCount = spriteBoneCount;
            this.spriteBoneNames = spriteBoneNames;
        }
    }

    public readonly struct WeightBrushTargetInfo
    {
        public readonly int layerToken;
        public readonly string layerName;
        public readonly int vertexCount;
        public readonly int spriteBoneCount;
        public readonly string[] spriteBoneNames;
        public readonly int[] spriteBoneToRendererBone;
        public readonly Color[] spriteBoneColors;

        public WeightBrushTargetInfo(
            int layerToken,
            string layerName,
            int vertexCount,
            int spriteBoneCount,
            string[] spriteBoneNames,
            int[] spriteBoneToRendererBone,
            Color[] spriteBoneColors)
        {
            this.layerToken = layerToken;
            this.layerName = layerName;
            this.vertexCount = vertexCount;
            this.spriteBoneCount = spriteBoneCount;
            this.spriteBoneNames = spriteBoneNames;
            this.spriteBoneToRendererBone = spriteBoneToRendererBone;
            this.spriteBoneColors = spriteBoneColors;
        }
    }

    public readonly struct WeightBrushBoneCandidateInfo
    {
        public readonly int rendererBoneIndex;
        public readonly string boneName;
        public readonly float distance;
        public readonly Color color;

        public WeightBrushBoneCandidateInfo(int rendererBoneIndex, string boneName, float distance, Color color)
        {
            this.rendererBoneIndex = rendererBoneIndex;
            this.boneName = boneName;
            this.distance = distance;
            this.color = color;
        }
    }

    public readonly struct BoneInfluenceLayerInfo
    {
        public readonly string layerName;
        public readonly int vertexCount;
        public readonly int influencedVertexCount;
        public readonly float totalWeight;
        public readonly float maxWeight;

        public BoneInfluenceLayerInfo(string layerName, int vertexCount, int influencedVertexCount, float totalWeight, float maxWeight)
        {
            this.layerName = layerName;
            this.vertexCount = vertexCount;
            this.influencedVertexCount = influencedVertexCount;
            this.totalWeight = totalWeight;
            this.maxWeight = maxWeight;
        }
    }

    public readonly struct BoneInfluenceInfo
    {
        public readonly int rendererBoneIndex;
        public readonly string boneName;
        public readonly Color color;
        public readonly int vertexCount;
        public readonly int influencedVertexCount;
        public readonly float totalWeight;
        public readonly float maxWeight;
        public readonly BoneInfluenceLayerInfo[] layers;

        public BoneInfluenceInfo(
            int rendererBoneIndex,
            string boneName,
            Color color,
            int vertexCount,
            int influencedVertexCount,
            float totalWeight,
            float maxWeight,
            BoneInfluenceLayerInfo[] layers)
        {
            this.rendererBoneIndex = rendererBoneIndex;
            this.boneName = boneName;
            this.color = color;
            this.vertexCount = vertexCount;
            this.influencedVertexCount = influencedVertexCount;
            this.totalWeight = totalWeight;
            this.maxWeight = maxWeight;
            this.layers = layers;
        }
    }

    public readonly struct SpriteInfluenceBoneInfo
    {
        public readonly int spriteBoneIndex;
        public readonly int rendererBoneIndex;
        public readonly string boneName;
        public readonly Color color;
        public readonly int vertexCount;
        public readonly float totalWeight;
        public readonly float maxWeight;

        public SpriteInfluenceBoneInfo(
            int spriteBoneIndex,
            int rendererBoneIndex,
            string boneName,
            Color color,
            int vertexCount,
            float totalWeight,
            float maxWeight)
        {
            this.spriteBoneIndex = spriteBoneIndex;
            this.rendererBoneIndex = rendererBoneIndex;
            this.boneName = boneName;
            this.color = color;
            this.vertexCount = vertexCount;
            this.totalWeight = totalWeight;
            this.maxWeight = maxWeight;
        }
    }

    public readonly struct SpriteInfluenceInfo
    {
        public readonly string layerName;
        public readonly int vertexCount;
        public readonly SpriteInfluenceBoneInfo[] bones;

        public SpriteInfluenceInfo(string layerName, int vertexCount, SpriteInfluenceBoneInfo[] bones)
        {
            this.layerName = layerName;
            this.vertexCount = vertexCount;
            this.bones = bones;
        }
    }

    public enum VolumeCurvePointType
    {
        Linear = 0,
        Euler = 1,
        Catmull = 2,
        Bezier = 3
    }

    public enum VolumeCurveFalloffPreset
    {
        Soft = 0,
        Linear = 1,
        Hard = 2
    }

    [Serializable]
    public sealed class VolumeCurvePoint
    {
        public Vector2 positionDoc;
        public VolumeCurvePointType type = VolumeCurvePointType.Catmull;
        public VolumeCurvePointType inType = VolumeCurvePointType.Catmull;
        public VolumeCurvePointType outType = VolumeCurvePointType.Catmull;
    }

    [Serializable]
    public sealed class VolumeCurve
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Curve";
        public bool closed;
        public float radius = 32f;
        public float strength = 1f;
        public int targetRendererBoneIndex = -1;
        public AnimationCurve falloff = CreateDefaultVolumeFalloffCurve();
        public List<VolumeCurvePoint> points = new List<VolumeCurvePoint>();
    }

    public readonly struct ProjectorCandidateInfo
    {
        public readonly string SelectionKey;
        public readonly string StableKey;
        public readonly string LayerName;
        public readonly int VertexCount;
        public readonly LayerInfo Layer;

        public ProjectorCandidateInfo(string selectionKey, string stableKey, string layerName, int vertexCount)
            : this(selectionKey, stableKey, layerName, vertexCount, null)
        {
        }

        public ProjectorCandidateInfo(string selectionKey, string stableKey, string layerName, int vertexCount, LayerInfo layer)
        {
            SelectionKey = selectionKey;
            StableKey = stableKey;
            LayerName = layerName;
            VertexCount = vertexCount;
            Layer = layer;
        }
    }

    private struct LineHit
    {
        public float t;
        public Vector2 point;
    }

    private enum ElbowDragMode
    {
        None,
        MoveLine,
        MoveStart,
        MoveEnd
    }

    private enum ElbowHandleType
    {
        None,
        Line,
        Start,
        End
    }

    private readonly struct ElbowHandleHit
    {
        public static readonly ElbowHandleHit None = new ElbowHandleHit(-1, ElbowHandleType.None);

        public readonly int index;
        public readonly ElbowHandleType type;

        public ElbowHandleHit(int index, ElbowHandleType type)
        {
            this.index = index;
            this.type = type;
        }
    }

    private struct PsdLayerDataInfo
    {
        public int index;
        public string name;
        public int layerId;
        public bool isVisible;
        public bool isGroup;
        public int parentIndex;
        public Vector2Int layerSizeOnFile;
    }
}

public sealed class ReanimatorVolumeCurvePresetAsset : ScriptableObject
{
    public List<PsdLayerPreviewRenderer.VolumeCurve> curves = new List<PsdLayerPreviewRenderer.VolumeCurve>();
}
#endif
