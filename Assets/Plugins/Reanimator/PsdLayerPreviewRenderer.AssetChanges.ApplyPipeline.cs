#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;

public partial class PsdLayerPreviewRenderer
{
    public bool Apply(string assetPath, out string error)
    {
        return Apply(assetPath, false, out error);
    }

    public bool Apply(string assetPath, bool allowExternalOverwrite, out string error)
    {
        error = null;
        if (!allowExternalOverwrite && HasExternalAssetChanges(assetPath))
        {
            error = "Source asset changed outside Reanimator. Reload, overwrite, or cancel before applying.";
            return false;
        }

        if (!TryCreateApplySession(assetPath, out var session, out error))
            return false;

        using var suspendedSkins = new ApplySpriteSkinSuspension(session.AssetPath);
        var failureState = CaptureApplyFailureWorkingState();
        var applyCompleted = false;
        byte[] originalMetadata = null;
        var spritePositionSyncSuppression = ReanimatorPsdSpritePositionPostprocessor.SuppressForAsset(assetPath);
        try
        {
            originalMetadata = File.ReadAllBytes(assetPath + ".meta");
            if (UndoRequest != null && (hasPendingChanges || HasPendingMeshWorkForAsset(assetPath)))
            {
                const string undoName = "Apply Reanimator Changes";
                RegisterUndo(undoName);
                ReanimatorAssetMetaPersistence.RegisterImporterUndo(session.Importer, undoName);
            }
            RestorePreviewTestAnimationPose();
            RestorePoseForAssetTool();

            if (!ApplyDataProviderMeshAndRectStage(session, out error))
                return false;
            if (!FlushDataProviderStageIfNeeded(session, "Selected asset importer disappeared after SpriteDataProvider.Apply.", out error))
                return false;

            ApplySerializedPreviewStateStage(session);

            if (!session.HasImporterChanges)
            {
                CompleteNoOpApply(session);
                applyCompleted = true;
                return true;
            }

            StageApplyOldEditorMetadata(session);

            CapturePendingUiVisibilityState(session.AssetPath);
            PersistImporterMetadataAndReimport(session.AssetPath);
            applyCompleted = true;
            RefreshAfterCommittedApply(session);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (!applyCompleted)
                {
                    if (originalMetadata != null)
                    {
                        try
                        {
                            RestoreApplyImporterMetadata(assetPath, originalMetadata);
                        }
                        catch (Exception rollbackError)
                        {
                            error += " Importer rollback failed: " + rollbackError.Message;
                            ReanimatorLog.Exception(rollbackError);
                        }
                    }
                    RestoreApplyFailureWorkingState(failureState);
                }
            }
            finally
            {
                spritePositionSyncSuppression.Dispose();
            }
        }
    }

    // SpriteSkin keeps native views of imported vertex data. Unregister live skins
    // before the importer replaces that data, including skins in preview scenes.
    internal sealed class ApplySpriteSkinSuspension : IDisposable
    {
        private readonly List<UnityEngine.U2D.Animation.SpriteSkin> skins = new();

        public ApplySpriteSkinSuspension(string assetPath)
        {
            try
            {
                foreach (var skin in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.U2D.Animation.SpriteSkin>())
                {
                    if (!skin || !skin.isActiveAndEnabled || EditorUtility.IsPersistent(skin) ||
                        !skin.TryGetComponent<UnityEngine.SpriteRenderer>(out var renderer) ||
                        !string.Equals(AssetDatabase.GetAssetPath(renderer.sprite), assetPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    skins.Add(skin);
                    skin.enabled = false;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var skin in skins)
                if (skin) skin.enabled = true;
            skins.Clear();
        }
    }

    private sealed class ApplyFailureWorkingState
    {
        public List<BoneInfo> Bones;
        public Dictionary<string, LayerVertexData> LayerData;
        public List<AutoGeometryPatch> GeometryPatches;
        public List<AppliedElbowVertex> AppliedElbowVertices;
        public bool HasPendingChanges;
        public bool BonePoseAtRest;
        public int BonePoseVersion;
        public int SelectedBoneIndex;
        public string SelectedLayerSelectionKey;
    }

    private ApplyFailureWorkingState CaptureApplyFailureWorkingState()
    {
        var layerData = new Dictionary<string, LayerVertexData>(StringComparer.Ordinal);
        if (vertexDataByLayerSelectionKey != null)
        {
            foreach (var pair in vertexDataByLayerSelectionKey)
                layerData[pair.Key] = CloneProjectorLayerData(pair.Value);
        }

        var geometryPatches = new List<AutoGeometryPatch>();
        if (autoGeometryPatches != null)
        {
            for (var i = 0; i < autoGeometryPatches.Count; i++)
            {
                var clone = CloneApplyGeometryPatch(autoGeometryPatches[i]);
                if (clone != null)
                    geometryPatches.Add(clone);
            }
        }

        var elbowVertices = new List<AppliedElbowVertex>();
        if (appliedElbowVertices != null)
        {
            for (var i = 0; i < appliedElbowVertices.Count; i++)
            {
                var point = appliedElbowVertices[i];
                if (point == null)
                    continue;
                elbowVertices.Add(new AppliedElbowVertex
                {
                    assetPath = point.assetPath,
                    layerStableKey = point.layerStableKey,
                    layerSelectionKey = point.layerSelectionKey,
                    positionDoc = point.positionDoc
                });
            }
        }

        return new ApplyFailureWorkingState
        {
            Bones = CloneBones(bones),
            LayerData = layerData,
            GeometryPatches = geometryPatches,
            AppliedElbowVertices = elbowVertices,
            HasPendingChanges = hasPendingChanges,
            BonePoseAtRest = bonePoseAtRest,
            BonePoseVersion = bonePoseVersion,
            SelectedBoneIndex = selectedBoneIndex,
            SelectedLayerSelectionKey = selectedLayerSelectionKey
        };
    }

    private void RestoreApplyFailureWorkingState(ApplyFailureWorkingState state)
    {
        if (state == null)
            return;

        bones = CloneBones(state.Bones);
        vertexDataByLayerSelectionKey.Clear();
        if (state.LayerData != null)
        {
            foreach (var pair in state.LayerData)
                vertexDataByLayerSelectionKey[pair.Key] = CloneProjectorLayerData(pair.Value);
        }

        autoGeometryPatches = new List<AutoGeometryPatch>();
        if (state.GeometryPatches != null)
        {
            for (var i = 0; i < state.GeometryPatches.Count; i++)
            {
                var clone = CloneApplyGeometryPatch(state.GeometryPatches[i]);
                if (clone != null)
                    autoGeometryPatches.Add(clone);
            }
        }

        appliedElbowVertices = state.AppliedElbowVertices ?? new List<AppliedElbowVertex>();
        hasPendingChanges = state.HasPendingChanges || autoGeometryPatches.Count > 0 || appliedElbowVertices.Count > 0;
        selectedBoneIndex = state.SelectedBoneIndex;
        selectedLayerSelectionKey = state.SelectedLayerSelectionKey ?? string.Empty;
        RecomputeBoneWorldTransforms();
        BuildBoneDrawOrder();
        bonePoseVersion = state.BonePoseVersion;
        bonePoseAtRest = state.BonePoseAtRest;
    }

    private static AutoGeometryPatch CloneApplyGeometryPatch(AutoGeometryPatch source)
    {
        if (source == null)
            return null;
        return new AutoGeometryPatch
        {
            assetPath = source.assetPath,
            layerStableKey = source.layerStableKey,
            layerSelectionKey = source.layerSelectionKey,
            spriteGuid = source.spriteGuid,
            layerName = source.layerName,
            layerId = source.layerId,
            layerPosition = source.layerPosition,
            sourceRect = source.sourceRect,
            verticesDoc = CloneArray(source.verticesDoc),
            vertexUvs = CloneArray(source.vertexUvs),
            triangles = CloneArray(source.triangles),
            allEdgePairs = CloneArray(source.allEdgePairs),
            boundaryEdgePairs = CloneArray(source.boundaryEdgePairs),
            boneWeights = CloneArray(source.boneWeights),
            spriteBoneToRendererBone = CloneArray(source.spriteBoneToRendererBone),
            bindBoneInverseMatrices = CloneArray(source.bindBoneInverseMatrices),
            importerMode = source.importerMode,
            usesSpriteBindPoses = source.usesSpriteBindPoses,
            generateWeights = source.generateWeights
        };
    }

    public bool HasExternalAssetChanges(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var currentDependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        return ReanimatorAssetChangeAlgorithms.HasSourceAssetChangedSinceSnapshot(
            cachedPath,
            assetPath,
            cachedAssetDependencyHash,
            currentDependencyHash);
    }

    private sealed class ApplySession
    {
        public string AssetPath;
        public PSDImporter Importer;
        public ISpriteEditorDataProvider DataProvider;
        public bool HasImporterChanges;
        public bool HasMeshChanges;
        public bool HasDataProviderSpriteChanges;
        public bool HasBoneOrderChanges;
        public bool HasPendingMeshWork;
        public bool ShouldWritePreviewState;
        public bool ShouldSyncDataProviderSpriteRects;
        public DirtyLayerLookup DirtyLayerPositionSnapshot;
        public DirtyLayerLookup DirtyLayerSpriteSnapshot;
        public DirtyLayerLookup DirtyLayerImportSnapshot;
        public DirtyLayerLookup DataProviderCharacterSnapshot;
    }

    private bool TryCreateApplySession(string assetPath, out ApplySession session, out string error)
    {
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "PSD path is empty.";
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        if (importer == null)
        {
            error = "Selected asset is not a PSD importer.";
            return false;
        }

        var hasPendingMeshWork = HasPendingMeshWorkForAsset(assetPath);
        var dirtyLayerPositionSnapshot = HasDirtyLayerPositionState()
            ? BuildDirtyLayerLookup(positionState: true)
            : null;
        var dirtyLayerSpriteSnapshot = HasDirtyLayerSpriteState()
            ? BuildDirtyLayerLookup(positionState: false)
            : null;
        var dirtyLayerImportSnapshot = MergeDirtyLayerLookups(dirtyLayerSpriteSnapshot, dirtyLayerPositionSnapshot);

        session = new ApplySession
        {
            AssetPath = assetPath,
            Importer = importer,
            DataProvider = importer as ISpriteEditorDataProvider,
            HasPendingMeshWork = hasPendingMeshWork,
            ShouldWritePreviewState = ReanimatorAssetChangeAlgorithms.ShouldWritePreviewStateToImporter(hasPendingChanges, hasPendingMeshWork),
            ShouldSyncDataProviderSpriteRects = ReanimatorAssetChangeAlgorithms.ShouldSyncDataProviderSpriteRects(hasPendingChanges, hasPendingMeshWork),
            DirtyLayerPositionSnapshot = dirtyLayerPositionSnapshot,
            DirtyLayerSpriteSnapshot = dirtyLayerSpriteSnapshot,
            DirtyLayerImportSnapshot = dirtyLayerImportSnapshot,
            DataProviderCharacterSnapshot = dirtyLayerImportSnapshot
        };
        return true;
    }

    private bool ApplyDataProviderMeshAndRectStage(ApplySession session, out string error)
    {
        error = null;
        if (session == null ||
            session.DataProvider == null ||
            (!session.ShouldSyncDataProviderSpriteRects && !session.HasPendingMeshWork && !session.ShouldWritePreviewState))
        {
            return true;
        }

        session.DataProvider.InitSpriteEditorDataProvider();
        session.HasDataProviderSpriteChanges =
            session.ShouldSyncDataProviderSpriteRects &&
            SyncSpriteRectsToDataProvider(session.DataProvider, session.DirtyLayerSpriteSnapshot);
        session.HasDataProviderSpriteChanges |=
            session.ShouldWritePreviewState &&
            SyncCharacterPartPositionsToDataProvider(session.DataProvider, session.DataProviderCharacterSnapshot, session.DirtyLayerSpriteSnapshot);
        var hasAutoGeometryChanges = session.HasPendingMeshWork &&
                                     ApplyPendingAutoGeometryToImporter(session.AssetPath, session.DataProvider, out error);
        if (error != null)
            return false;
        var hasElbowMeshChanges = session.HasPendingMeshWork &&
                                  ApplyElbowVerticesToImporterMesh(session.AssetPath, session.DataProvider, out error);
        if (error != null)
            return false;
        session.HasMeshChanges = hasAutoGeometryChanges || hasElbowMeshChanges;
        session.HasBoneOrderChanges =
            session.ShouldWritePreviewState &&
            SyncBoneOrdersToDataProvider(session.DataProvider);
        if (!session.HasDataProviderSpriteChanges && !session.HasMeshChanges && !session.HasBoneOrderChanges)
            return true;

        EditorUtility.SetDirty(session.Importer);
        session.HasImporterChanges = true;
        return true;
    }

    private bool FlushDataProviderStageIfNeeded(ApplySession session, string missingImporterError, out string error)
    {
        error = null;
        if (session == null || session.DataProvider == null ||
            (!session.HasDataProviderSpriteChanges && !session.HasMeshChanges && !session.HasBoneOrderChanges))
            return true;

        session.DataProvider.Apply();
        return RefreshApplyImporter(session, missingImporterError, out error);
    }

    private bool RefreshApplyImporter(ApplySession session, string missingImporterError, out string error)
    {
        error = null;
        session.Importer = AssetImporter.GetAtPath(session.AssetPath) as PSDImporter;
        if (session.Importer == null)
        {
            error = missingImporterError;
            return false;
        }

        session.DataProvider = session.Importer as ISpriteEditorDataProvider;
        EditorUtility.SetDirty(session.Importer);
        return true;
    }

    private void ApplySerializedPreviewStateStage(ApplySession session)
    {
        if (session == null)
            return;

        if (session.ShouldWritePreviewState)
        {
            session.HasImporterChanges |= ApplyLayerPositionsToImporter(session.Importer, session.DirtyLayerPositionSnapshot);
            session.HasImporterChanges |= ApplySpriteChangesToImporter(session.Importer, session.DirtyLayerSpriteSnapshot);
            session.HasImporterChanges |= ApplyBoneChangesToImporter(session.Importer);
        }

        session.HasImporterChanges |= SyncSerializedSpriteBoneNamesFromCharacterData(session.Importer);
    }

    private void CompleteNoOpApply(ApplySession session)
    {
        if (session == null)
            return;

        RemoveAutoGeometryPatchesForAsset(session.AssetPath);
        hasPendingChanges = false;
        ClearDirtyLayerSpriteState();
    }

    private void StageApplyOldEditorMetadata(ApplySession session)
    {
        if (session == null || session.Importer == null)
            return;

        StageOldEditorCompatibleSpriteMetadata(
            session.Importer,
            clearResliceRequest: true,
            syncPsdLayerMosaicPositions: false);
    }

    internal static void RestoreApplyImporterMetadata(string assetPath, byte[] metadata)
    {
        File.WriteAllBytes(assetPath + ".meta", metadata);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }

    private void RefreshAfterCommittedApply(ApplySession session)
    {
        RemoveAutoGeometryPatchesForAsset(session.AssetPath);
        if (session.HasMeshChanges)
            RemoveAppliedVerticesForAsset(session.AssetPath);

        hasPendingChanges = false;
        ClearDirtyLayerSpriteState();
        InvalidatePreviewCacheAfterImport();
        try
        {
            Rebuild(session.AssetPath, true);
        }
        catch (Exception ex)
        {
            InvalidatePreviewCacheAfterImport();
            ReanimatorLog.Error("[Reanimator] Asset saved. Preview refresh will retry: " + ex);
        }
    }

    private void InvalidatePreviewCacheAfterImport()
    {
        CancelPreviewCacheRefresh();
        cachedTexture = null;
        cachedAtlasTexture = null;
        cachedDocumentSize = default;
        layerInfos.Clear();
        layerHierarchyNodes.Clear();
        sortedLayers.Clear();
        bones.Clear();
        boneDrawOrder.Clear();
        vertexDataByLayerSelectionKey.Clear();
        cachedAssetDependencyHash = string.Empty;
        cachedPreviewPipelineVersion = -1;
        boneRecoveryAttemptedPath = null;
    }
}
#endif
