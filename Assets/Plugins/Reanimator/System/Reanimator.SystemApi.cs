#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private SystemApi systemApi;

    internal SystemApi Api => systemApi ?? (systemApi = new SystemApi(this));

    internal sealed class SystemApi
    {
        private readonly Reanimator owner;

        public SystemApi(Reanimator owner)
        {
            this.owner = owner;
        }

        public ReanimatorToolMode ActiveToolMode => owner != null ? owner.bottomToolMode : ReanimatorToolMode.NoSettings;
        public int ActiveFaceSubtoolIndex => owner != null ? owner.faceSubtoolIndex : -1;
        public float FaceEyesPointDensity => owner != null ? owner.faceEyesPointDensity : 0.5f;
        public float FaceEyesPointOffset => owner != null ? owner.faceEyesPointOffset : 4f;
        public float FaceEyesOutlineOffset => owner != null ? owner.faceEyesOutlineOffset : 4f;
        public AnimationCurve FaceEyesWeightCurve => owner != null && owner.faceEyesWeightCurve != null
            ? owner.faceEyesWeightCurve
            : AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public PsdLayerPreviewRenderer PreviewRenderer => owner != null ? owner.layerPreviewRenderer : null;
        public string ActiveAssetPath => owner != null ? owner.GetSelectedAssetPath() : null;
        public bool IsDebugDiagnosticsVisible => owner != null && owner.ShouldShowReleaseDiagnostics();
        public int WeightBrushCapInfluenceCount
        {
            get => owner != null && owner.layerPreviewRenderer != null
                ? owner.layerPreviewRenderer.WeightBrushCapInfluenceCount
                : 3;
            set
            {
                if (owner != null && owner.layerPreviewRenderer != null)
                    owner.layerPreviewRenderer.WeightBrushCapInfluenceCount = value;
            }
        }

        public void SetToolMode(ReanimatorToolMode mode)
        {
            owner?.SetBottomToolMode(mode);
        }

        public void SetNoSettingsToolMode()
        {
            owner?.SetNoSettingsToolMode();
        }

        public void ExitActiveTool()
        {
            owner?.ExitElbowModeIfActive();
        }

        public void RestorePreviewPose()
        {
            owner?.RestorePreviewPose();
        }

        public void ApplyAutoWeights()
        {
            owner?.ApplyAutoWeights();
        }

        public string GetDebugModeButtonLabel()
        {
            return owner != null ? owner.GetDebugModeButtonLabel() : "Debug [OFF]";
        }

        public void ToggleDebugModeByDoublePress()
        {
            owner?.ToggleDebugModeByDoublePress();
        }

        public void GenerateMissingGeometry()
        {
            owner?.GenerateMissingGeometry();
        }

        public void FixSelectedSpriteSkinWeights()
        {
            owner?.FixSelectedSpriteSkinWeights();
        }

        public bool HasSelectedSprite()
        {
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetSelectedLayer(out _);
        }

        public bool KillSelectedSpriteWeights(out string layerName, out int clearedCount, out string error)
        {
            layerName = string.Empty;
            clearedCount = 0;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ClearSelectedSpriteWeights(out layerName, out clearedCount, out error);
        }

        public string GetDeleteAllBonesButtonLabel()
        {
            return owner != null ? owner.GetDeleteAllBonesButtonLabel() : "DelBon x5";
        }

        public void PressDeleteAllBones()
        {
            owner?.PressDeleteAllBones();
        }

        public void CopyBonesFromAnotherPsd()
        {
            owner?.CopyBonesFromAnotherPsd();
        }

        public void ForceCleanPreviewCache()
        {
            owner?.ForceCleanPreviewCache();
        }

        public void DrawPreviewPoseSettings(float availableHeight)
        {
            owner?.DrawPreviewPoseToolPanel(availableHeight);
        }

        public void DrawFaceSettings(float availableHeight)
        {
            owner?.DrawFaceToolPanel(availableHeight);
        }

        public void DrawEditBoneSettings(float availableHeight)
        {
            owner?.DrawEditBoneToolPanel(availableHeight);
        }

        public void DrawAutoGeometrySettings(float availableHeight)
        {
            owner?.DrawAutoGeometryToolPanel(availableHeight);
        }

        public void DrawElbowSettings(float availableHeight)
        {
            owner?.DrawElbowToolPanel(availableHeight);
        }

        public void DrawLimbsSettings(float availableHeight)
        {
            owner?.DrawLimbsToolPanel(availableHeight);
        }

        public void DrawJointSettings(float availableHeight)
        {
            owner?.DrawJointToolPanel(availableHeight);
        }

        public void DrawBoobSettings(float availableHeight)
        {
            owner?.DrawBoobToolPanel(availableHeight);
        }

        public void DrawCurveSettings(float availableHeight)
        {
            owner?.DrawCurveToolPanel(availableHeight);
        }

        public void DrawCreateEdgeSettings(float availableHeight)
        {
            owner?.DrawCreateEdgeToolPanel(availableHeight);
        }

        public void DrawSplitEdgeSettings(float availableHeight)
        {
            owner?.DrawSplitEdgeToolPanel(availableHeight);
        }

        public void DrawEditGeometrySettings(float availableHeight)
        {
            owner?.DrawEditGeometryToolPanel(availableHeight);
        }

        public void DrawRadiusCullSettings(float availableHeight)
        {
            owner?.DrawRadiusCullToolPanel(availableHeight);
        }

        public void DrawWeightSliderSettings(float availableHeight)
        {
            owner?.DrawWeightPanel(availableHeight);
        }

        public void DrawBoneInfluenceSettings(float availableHeight)
        {
            owner?.DrawBoneInfluenceToolPanel(availableHeight);
        }

        public void DrawSpriteInfluenceSettings(float availableHeight)
        {
            owner?.DrawSpriteInfluenceToolPanel(availableHeight);
        }

        public bool TryGetWeightBrushTargetInfo(out PsdLayerPreviewRenderer.WeightBrushTargetInfo info)
        {
            info = default;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetWeightBrushTargetInfo(out info);
        }

        public bool TryApplyWeightBrushAt(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            int spriteBoneIndex,
            int modeIndex,
            float amount,
            float radius,
            float hardness,
            bool normalize,
            bool subtractModifier,
            bool smoothModifier,
            int capInfluenceCount)
        {
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryApplyWeightBrushAt(
                       context,
                       mouseCanvas,
                       spriteBoneIndex,
                       modeIndex,
                       amount,
                       radius,
                       hardness,
                       normalize,
                       subtractModifier,
                       smoothModifier,
                       capInfluenceCount);
        }

        public void BeginWeightBrushStroke()
        {
            owner?.layerPreviewRenderer?.BeginWeightBrushStroke();
        }

        public bool TrySelectWeightBrushBone(int spriteBoneIndex)
        {
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TrySelectWeightBrushBone(spriteBoneIndex);
        }

        public bool TrySelectWeightBrushBoneAt(ReanimatorPreviewContext context, Vector2 mouseCanvas, out int spriteBoneIndex)
        {
            spriteBoneIndex = -1;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TrySelectWeightBrushBoneAt(context, mouseCanvas, out spriteBoneIndex);
        }

        public bool TryReplaceWeightBrushBoneAt(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            int spriteBoneIndex,
            out int replacedSpriteBoneIndex)
        {
            replacedSpriteBoneIndex = -1;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryReplaceWeightBrushBoneAt(context, mouseCanvas, spriteBoneIndex, out replacedSpriteBoneIndex);
        }

        public bool TryAddWeightBrushBoneAt(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out int addedSpriteBoneIndex,
            out string error)
        {
            addedSpriteBoneIndex = -1;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryAddWeightBrushBoneAt(
                       context,
                       mouseCanvas,
                       out addedSpriteBoneIndex,
                       out error);
        }

        public PsdLayerPreviewRenderer.WeightBrushBoneCandidateInfo[] GetNearestWeightBrushBoneCandidates(int maxCount)
        {
            if (owner == null || owner.layerPreviewRenderer == null)
                return System.Array.Empty<PsdLayerPreviewRenderer.WeightBrushBoneCandidateInfo>();

            return owner.layerPreviewRenderer.GetNearestWeightBrushBoneCandidates(maxCount);
        }

        public bool AssignWeightBrushBoneSlot(
            int requestedSpriteBoneIndex,
            int rendererBoneIndex,
            out int assignedSpriteBoneIndex,
            out string error)
        {
            assignedSpriteBoneIndex = -1;
            error = "Preview unavailable";
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.AssignWeightBrushBoneSlot(
                       requestedSpriteBoneIndex,
                       rendererBoneIndex,
                       out assignedSpriteBoneIndex,
                       out error);
        }

        public bool RemoveWeightBrushBoneSlot(
            int spriteBoneIndex,
            out int removedInfluencedVertexCount,
            out string error)
        {
            removedInfluencedVertexCount = 0;
            error = "Preview unavailable";
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.RemoveWeightBrushBoneSlot(
                       spriteBoneIndex,
                       out removedInfluencedVertexCount,
                       out error);
        }

        public PsdLayerPreviewRenderer.ProjectorCandidateInfo[] GetProjectorCandidates(
            out string targetLayerName,
            out string targetKey,
            out string error)
        {
            targetLayerName = string.Empty;
            targetKey = string.Empty;
            error = null;
            if (owner == null || owner.layerPreviewRenderer == null)
            {
                error = "Preview unavailable";
                return System.Array.Empty<PsdLayerPreviewRenderer.ProjectorCandidateInfo>();
            }

            return owner.layerPreviewRenderer.GetProjectorCandidates(out targetLayerName, out targetKey, out error);
        }

        public bool TryGetProjectorSelectedTarget(out string targetLayerName, out string targetKey, out string error)
        {
            targetLayerName = string.Empty;
            targetKey = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetProjectorSelectedTarget(out targetLayerName, out targetKey, out error);
        }

        public PsdLayerPreviewRenderer.ProjectorCandidateInfo[] GetProjectorCandidates(
            string targetSelectionKey,
            out string targetLayerName,
            out string targetKey,
            out string error)
        {
            targetLayerName = string.Empty;
            targetKey = string.Empty;
            error = null;
            if (owner == null || owner.layerPreviewRenderer == null)
            {
                error = "Preview unavailable";
                return System.Array.Empty<PsdLayerPreviewRenderer.ProjectorCandidateInfo>();
            }

            return owner.layerPreviewRenderer.GetProjectorCandidates(targetSelectionKey, out targetLayerName, out targetKey, out error);
        }

        public bool ApplyProjectorWeights(
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool clearTargetWeights,
            out int weightedCount,
            out string targetLayerName,
            out string error)
        {
            weightedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorWeights(sourceSelectionKeys, clearTargetWeights, out weightedCount, out targetLayerName, out error);
        }

        public bool ApplyProjectorWeights(
            string targetSelectionKey,
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool clearTargetWeights,
            bool postProcessExpandedCoverage,
            bool smoothPostProcessWeights,
            out int weightedCount,
            out string targetLayerName,
            out string error)
        {
            weightedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorWeights(
                       targetSelectionKey,
                       sourceSelectionKeys,
                       clearTargetWeights,
                       postProcessExpandedCoverage,
                       smoothPostProcessWeights,
                       out weightedCount,
                       out targetLayerName,
                       out error);
        }

        public bool FixMissingVertexWeights(
            out int fixedCount,
            out int missingCount,
            out string layerName,
            out string error)
        {
            fixedCount = 0;
            missingCount = 0;
            layerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.FixMissingVertexWeights(out fixedCount, out missingCount, out layerName, out error);
        }

        public bool ApplyProjectorVertexCull(
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            out int removedCount,
            out string targetLayerName,
            out string error)
        {
            removedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorVertexCull(sourceSelectionKeys, out removedCount, out targetLayerName, out error);
        }

        public bool ApplyProjectorVertexCull(
            string targetSelectionKey,
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            out int removedCount,
            out string targetLayerName,
            out string error)
        {
            removedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorVertexCull(targetSelectionKey, sourceSelectionKeys, out removedCount, out targetLayerName, out error);
        }

        public bool TryGetVertexCullTargetInfo(
            out string layerName,
            out int vertexCount,
            out string error)
        {
            layerName = string.Empty;
            vertexCount = 0;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetVertexCullTargetInfo(out layerName, out vertexCount, out error);
        }

        public bool TryAnalyzeVertexCull(
            float distance,
            out string layerName,
            out int vertexCount,
            out int mergeGroupCount,
            out int removedCount,
            out string error)
        {
            layerName = string.Empty;
            vertexCount = 0;
            mergeGroupCount = 0;
            removedCount = 0;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryAnalyzeVertexCull(distance, out layerName, out vertexCount, out mergeGroupCount, out removedCount, out error);
        }

        public bool ApplyVertexCull(
            float distance,
            out int mergeGroupCount,
            out int removedCount,
            out string layerName,
            out string error)
        {
            mergeGroupCount = 0;
            removedCount = 0;
            layerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyVertexCull(distance, out mergeGroupCount, out removedCount, out layerName, out error);
        }

        public bool RetriangulateSelectedGeometry(
            out string layerName,
            out int triangleCount,
            out string error)
        {
            layerName = string.Empty;
            triangleCount = 0;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.RetriangulateSelectedGeometry(out layerName, out triangleCount, out error);
        }

        public void DrawVertexCullPreviewOverlay(ReanimatorPreviewContext context, float distance)
        {
            owner?.layerPreviewRenderer?.DrawVertexCullPreviewOverlay(context, distance);
        }

        public bool ApplyProjectorStretch(
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool sourceInsideTargetOnly,
            bool preserveContour,
            bool useVertexCull,
            float vertexCullDistance,
            out int insertedCount,
            out int removedCount,
            out string targetLayerName,
            out string error)
        {
            insertedCount = 0;
            removedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorStretch(sourceSelectionKeys, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out insertedCount, out removedCount, out targetLayerName, out error);
        }

        public bool ApplyProjectorStretch(
            string targetSelectionKey,
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool useApl,
            bool sourceInsideTargetOnly,
            bool preserveContour,
            bool useVertexCull,
            float vertexCullDistance,
            out int insertedCount,
            out int removedCount,
            out string targetLayerName,
            out string error)
        {
            insertedCount = 0;
            removedCount = 0;
            targetLayerName = string.Empty;
            error = null;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.ApplyProjectorStretch(targetSelectionKey, sourceSelectionKeys, useApl, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance, out insertedCount, out removedCount, out targetLayerName, out error);
        }

        public void DrawProjectorStretchPreviewOverlay(
            ReanimatorPreviewContext context,
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool sourceInsideTargetOnly,
            bool preserveContour,
            bool useVertexCull,
            float vertexCullDistance)
        {
            owner?.layerPreviewRenderer?.DrawProjectorStretchPreviewOverlay(context, sourceSelectionKeys, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance);
        }

        public void DrawProjectorStretchPreviewOverlay(
            string targetSelectionKey,
            ReanimatorPreviewContext context,
            System.Collections.Generic.IReadOnlyList<string> sourceSelectionKeys,
            bool useApl,
            bool sourceInsideTargetOnly,
            bool preserveContour,
            bool useVertexCull,
            float vertexCullDistance)
        {
            owner?.layerPreviewRenderer?.DrawProjectorStretchPreviewOverlay(targetSelectionKey, context, sourceSelectionKeys, useApl, sourceInsideTargetOnly, preserveContour, useVertexCull, vertexCullDistance);
        }

        public void DrawProjectorCandidateHoverOverlay(ReanimatorPreviewContext context, string sourceSelectionKey)
        {
            owner?.layerPreviewRenderer?.DrawProjectorCandidateHoverOverlay(context, sourceSelectionKey);
        }

        public void ShowNotification(string message)
        {
            if (owner == null)
                return;

            owner.ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(message) ? "Reanimator" : message));
        }

        public bool TryCenterPreviewOnRendererBone(int rendererBoneIndex)
        {
            return owner != null && owner.TryCenterPreviewOnRendererBone(rendererBoneIndex);
        }

        public bool TryGetSelectedBoneDebugInfo(out PsdLayerPreviewRenderer.BoneDebugInfo info)
        {
            info = default;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetSelectedBoneDebugInfo(out info);
        }

        public bool TryCenterPreviewOnDocumentPoint(ReanimatorPreviewContext context, Vector2 documentPoint)
        {
            if (owner == null || owner.previewNavigator == null || context == null || context.Coordinates == null)
                return false;

            owner.previewNavigator.CenterOn(context.LocalRect, context.Coordinates.DocumentToCanvasPoint(documentPoint));
            owner.Repaint();
            return true;
        }

        public bool TryCreateBone(ReanimatorPreviewContext context, Vector2 startCanvas, Vector2 endCanvas)
        {
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryCreateBone(context, startCanvas, endCanvas, out _);
        }

        public bool TryPrepareCreateBoneStart(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out Vector2 startCanvas,
            out bool shouldCreate)
        {
            startCanvas = mouseCanvas;
            shouldCreate = true;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryPrepareCreateBoneStart(context, mouseCanvas, out startCanvas, out shouldCreate);
        }

        public bool TryGetCreateBoneParentTip(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out int parentBoneIndex,
            out Vector2 tipCanvas)
        {
            parentBoneIndex = -1;
            tipCanvas = default;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetCreateBoneParentTip(context, mouseCanvas, out parentBoneIndex, out tipCanvas);
        }

        public bool TryGetCreateBoneParentAnchor(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out int parentBoneIndex,
            out Vector2 anchorCanvas)
        {
            return TryGetCreateBoneParentAnchor(context, mouseCanvas, out parentBoneIndex, out anchorCanvas, out _);
        }

        public bool TryGetCreateBoneParentAnchor(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out int parentBoneIndex,
            out Vector2 anchorCanvas,
            out PsdLayerPreviewRenderer.CreateBoneAnchorKind anchorKind)
        {
            parentBoneIndex = -1;
            anchorCanvas = default;
            anchorKind = PsdLayerPreviewRenderer.CreateBoneAnchorKind.None;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetCreateBoneParentAnchor(context, mouseCanvas, out parentBoneIndex, out anchorCanvas, out anchorKind);
        }

        public bool TryGetCreateBoneParentAnchor(
            ReanimatorPreviewContext context,
            Vector2 mouseCanvas,
            out int parentBoneIndex,
            out Vector2 anchorCanvas,
            out PsdLayerPreviewRenderer.CreateBoneAnchorKind anchorKind,
            out int anchorBoneIndex)
        {
            parentBoneIndex = -1;
            anchorCanvas = default;
            anchorKind = PsdLayerPreviewRenderer.CreateBoneAnchorKind.None;
            anchorBoneIndex = -1;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetCreateBoneParentAnchor(context, mouseCanvas, out parentBoneIndex, out anchorCanvas, out anchorKind, out anchorBoneIndex);
        }

        public bool TryGetCreateBoneAnchorMarkerRadii(
            ReanimatorPreviewContext context,
            int anchorBoneIndex,
            PsdLayerPreviewRenderer.CreateBoneAnchorKind anchorKind,
            out float outerRadius,
            out float innerRadius)
        {
            outerRadius = 0f;
            innerRadius = 0f;
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TryGetCreateBoneAnchorMarkerRadii(context, anchorBoneIndex, anchorKind, out outerRadius, out innerRadius);
        }

        public bool TrySplitBoneAt(ReanimatorPreviewContext context, Vector2 mouseCanvas)
        {
            return owner != null &&
                   owner.layerPreviewRenderer != null &&
                   owner.layerPreviewRenderer.TrySplitBoneAt(context, mouseCanvas, out _);
        }

        public void Repaint()
        {
            owner?.Repaint();
        }
    }
}
#endif
