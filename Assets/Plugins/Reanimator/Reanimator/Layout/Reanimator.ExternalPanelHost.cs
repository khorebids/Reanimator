#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    bool IReanimatorExternalPanelHost.TryValidateAllInPreflight(out string error)
    {
        error = BuildNamingIndicatorAllInBlockReason(
            layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null);
        return string.IsNullOrEmpty(error);
    }

    string IReanimatorExternalPanelHost.CurrentAssetPath => GetSelectedAssetPath();
    Rect IReanimatorExternalPanelHost.PreviewRect => cachedPreviewRect;

    IReadOnlyList<ReanimatorExternalBone> IReanimatorExternalPanelHost.CaptureBones()
    {
        var source = layerPreviewRenderer != null ? layerPreviewRenderer.Bones : null;
        if (source == null || source.Count == 0)
            return System.Array.Empty<ReanimatorExternalBone>();

        var result = new ReanimatorExternalBone[source.Count];
        var selected = layerPreviewRenderer.SelectedBoneIndex;
        for (var i = 0; i < source.Count; i++)
        {
            var bone = source[i];
            result[i] = new ReanimatorExternalBone(
                i,
                bone != null ? bone.name : string.Empty,
                bone != null ? bone.parentId : -1,
                bone != null ? bone.worldPosition : Vector2.zero,
                bone != null ? bone.worldRotation : Quaternion.identity,
                bone != null ? bone.length : 0f,
                bone != null ? bone.color : Color.white,
                bone != null && bone.visible,
                i == selected);
        }

        return result;
    }

    string IReanimatorExternalPanelHost.CaptureWorkspacePng()
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path))
            throw new System.InvalidOperationException("Select PSD/PSB asset in Reanimator.");

        if (!layerPreviewRenderer.IsPreviewCacheReady(path))
        {
            layerPreviewRenderer.RequestPreviewCacheRefresh(path);
            throw new System.InvalidOperationException("PSD preview cache is not ready yet.");
        }

        var directory = Path.Combine(Application.temporaryCachePath, "Comfimator");
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, "reanimator_workspace_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        layerPreviewRenderer.SaveWorkspacePreviewPng(outputPath);
        return outputPath;
    }

    int IReanimatorExternalPanelHost.SecondaryArmLayerCount
    {
        get
        {
            var layers = layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null;
            if (layers == null)
                return 0;

            var hasLeft = false;
            var hasRight = false;
            for (var i = 0; i < layers.Count; i++)
            {
                var normalized = layers[i] != null
                    ? NormalizeNamingIndicatorName(layers[i].name)
                    : string.Empty;
                hasLeft |= normalized == "l_arm_2";
                hasRight |= normalized == "r_arm_2";
            }

            return (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);
        }
    }

    string IReanimatorExternalPanelHost.CaptureAutoSkeletonArmPassWorkspacePng(bool secondaryPair)
    {
        if (layerPreviewRenderer == null || layerPreviewRenderer.Layers == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var layers = layerPreviewRenderer.Layers;
        var originalVisibility = new bool[layers.Count];
        for (var i = 0; i < layers.Count; i++)
            originalVisibility[i] = layers[i] != null && layers[i].visible;

        try
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null)
                    continue;

                switch (NormalizeNamingIndicatorName(layer.name))
                {
                    case "l_arm":
                    case "r_arm":
                        layer.visible = !secondaryPair;
                        break;
                    case "l_arm_2":
                    case "r_arm_2":
                        layer.visible = secondaryPair;
                        break;
                }
            }

            Repaint();
            return ((IReanimatorExternalPanelHost)this).CaptureWorkspacePng();
        }
        finally
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null)
                    layers[i].visible = originalVisibility[i];
            }
            Repaint();
        }
    }

    Vector2 IReanimatorExternalPanelHost.WorkspaceNormalizedToDocument(Vector2 normalizedTopLeft)
    {
        var size = layerPreviewRenderer != null ? layerPreviewRenderer.DocumentSize : Vector2Int.zero;
        if (size.x <= 0 || size.y <= 0)
            throw new System.InvalidOperationException("Reanimator document size is unavailable.");

        return new Vector2(
            Mathf.Clamp01(normalizedTopLeft.x) * size.x,
            (1f - Mathf.Clamp01(normalizedTopLeft.y)) * size.y);
    }

    string IReanimatorExternalPanelHost.ApplyDefaultAutoGeometry()
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path))
            throw new System.InvalidOperationException("Select PSD/PSB asset in Reanimator.");

        var generated = layerPreviewRenderer.GenerateDefaultGeometryForAllSprites(path, out var error);
        if (generated <= 0)
            throw new System.InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Auto Geometry generated no sprites." : error);

        Repaint();
        return "generated geometry for " + generated + " sprite(s)";
    }

    string IReanimatorExternalPanelHost.ApplyAutoSkeletonPoseJson(
        string poseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls)
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var result = layerPreviewRenderer.ApplyAutoSkeletonPoseJson(
            poseJson,
            namingReferenceAssetPath,
            includeFingers,
            includeBreastControls,
            includeEyeControls);
        Repaint();
        return result;
    }

    string IReanimatorExternalPanelHost.ApplyDeterministicAutoWeights()
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var result = layerPreviewRenderer.ApplyRiggedLimbAutoWeights();
        Repaint();
        return result;
    }

    string IReanimatorExternalPanelHost.ApplyAutoSkeletonPoseJsonWithSecondaryArms(
        string primaryPoseJson,
        string secondaryArmsPoseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls)
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var result = layerPreviewRenderer.ApplyAutoSkeletonPoseJsonWithSecondaryArms(
            primaryPoseJson,
            secondaryArmsPoseJson,
            namingReferenceAssetPath,
            includeFingers,
            includeBreastControls,
            includeEyeControls);
        Repaint();
        return result;
    }

    string IReanimatorExternalPanelHost.ApplyFaceEyesAutomation()
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        return RunNativeUndoGroup("Apply Face Eyes", () =>
        {
            if (!layerPreviewRenderer.TryApplyFaceEyesAutomation(
                    faceEyesPointDensity,
                    faceEyesPointOffset,
                    faceEyesOutlineOffset,
                    faceEyesWeightCurve,
                    out var result,
                    out var error))
            {
                throw new System.InvalidOperationException(
                    string.IsNullOrWhiteSpace(error) ? "Face Eyes automation failed." : error);
            }

            Repaint();
            return result;
        });
    }

    string IReanimatorExternalPanelHost.RunUndoGroup(string operationName, System.Func<string> apply)
    {
        return RunNativeUndoGroup(operationName, apply);
    }

    private string RunNativeUndoGroup(string operationName, System.Func<string> apply)
    {
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(operationName);
        try
        {
            var result = apply();
            Undo.CollapseUndoOperations(group);
            Undo.SetCurrentGroupName(operationName);
            return result;
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            RefreshAfterUndoRedo();
            throw;
        }
        finally
        {
            Undo.IncrementCurrentGroup();
        }
    }

    string IReanimatorExternalPanelHost.LocateNipples(
        out Vector2 leftDocumentPoint,
        out Vector2 rightDocumentPoint,
        out float leftConfidence,
        out float rightConfidence)
    {
        leftDocumentPoint = Vector2.zero;
        rightDocumentPoint = Vector2.zero;
        leftConfidence = 0f;
        rightConfidence = 0f;
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        if (!layerPreviewRenderer.TryLocateNippleTargets(
                out leftDocumentPoint,
                out rightDocumentPoint,
                out leftConfidence,
                out rightConfidence,
                out var result,
                out var error))
        {
            throw new System.InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "Nipple detection failed." : error);
        }

        Repaint();
        return result;
    }

    bool IReanimatorExternalPanelHost.TryConstrainBoobContourToVisibleSprite(
        Vector2 centerDocumentPoint,
        IReadOnlyList<Vector2> contourDocumentPoints,
        out Vector2[] constrainedDocumentPoints,
        out string layerName)
    {
        constrainedDocumentPoints = System.Array.Empty<Vector2>();
        layerName = string.Empty;
        return layerPreviewRenderer != null &&
               layerPreviewRenderer.TryConstrainBoobContourToVisibleSprite(
                   centerDocumentPoint,
                   contourDocumentPoints,
                   out constrainedDocumentPoints,
               out layerName);
    }

    bool IReanimatorExternalPanelHost.HasSeparatedBreastSprites()
    {
        var layers = layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null;
        if (layers == null)
            return false;

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer == null || string.IsNullOrWhiteSpace(layer.name))
                continue;

            var normalized = NormalizeExternalBreastLayerName(layer.name);
            if (normalized == "lboob" || normalized == "rboob" ||
                normalized == "boobl" || normalized == "boobr" ||
                normalized == "leftboob" || normalized == "rightboob" ||
                normalized == "lbreast" || normalized == "rbreast" ||
                normalized == "breastl" || normalized == "breastr" ||
                normalized == "leftbreast" || normalized == "rightbreast")
            {
                return true;
            }
        }

        return false;
    }

    string IReanimatorExternalPanelHost.ApplyFullBoobsWeights(
        Vector2 viewerLeftNippleDocumentPoint,
        Vector2 viewerRightNippleDocumentPoint,
        IReadOnlyList<Vector2> viewerLeftContourDocumentPoints,
        IReadOnlyList<Vector2> viewerRightContourDocumentPoints)
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        if (!layerPreviewRenderer.TryApplyFullBoobsWeights(
                viewerLeftNippleDocumentPoint,
                viewerRightNippleDocumentPoint,
                viewerLeftContourDocumentPoints,
                viewerRightContourDocumentPoints,
                out var result,
                out var error))
        {
            throw new System.InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "FullBoobs weight application failed." : error);
        }

        Repaint();
        return result;
    }

    private static string NormalizeExternalBreastLayerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var buffer = new char[value.Length];
        var length = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsLetterOrDigit(c))
                continue;
            buffer[length++] = char.ToLowerInvariant(c);
        }
        return new string(buffer, 0, length);
    }

    bool IReanimatorExternalPanelHost.HasFashionableFolder()
    {
        return layerPreviewRenderer != null && layerPreviewRenderer.HasFashionableFolder();
    }

    string IReanimatorExternalPanelHost.ApplyFashionable()
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        var result = layerPreviewRenderer.ApplyFashionableProjectorApl();
        Repaint();
        return result;
    }

    string IReanimatorExternalPanelHost.CookPrefabNextToPsd()
    {
        var path = GetSelectedAssetPath();
        if (string.IsNullOrWhiteSpace(path) || !IsPsdImporterAsset(path))
            throw new System.InvalidOperationException("Select PSD/PSB asset in Reanimator.");

        ApplyBeforeCookPrefab(path);
        var result = ReanimatorPrefabCooker.CookNextToPsd(path);
        Repaint();
        return result.ToStatus();
    }

    private void ApplyBeforeCookPrefab(string path)
    {
        if (layerPreviewRenderer == null)
            throw new System.InvalidOperationException("Reanimator preview renderer is not ready.");

        CommitBoneRename();
        CommitBoneEditRestPoseIfActive();
        layerPreviewRenderer.StopPreviewTestAnimation();

        isApplyingChanges = true;
        try
        {
            if (layerPreviewRenderer.Apply(path, false, out var error))
            {
                DeferPreviewContent();
                ReselectAssetDeferred(path);
                ShowNotification(new GUIContent("Applied before prefab cook"));
                return;
            }

            throw new System.InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "Apply before prefab cook failed." : error);
        }
        finally
        {
            isApplyingChanges = false;
        }
    }

    Vector2 IReanimatorExternalPanelHost.DocumentToPreview(Vector2 documentPoint)
    {
        return CreateExternalPanelCoordinateSpace().DocumentToView(documentPoint);
    }

    Vector2 IReanimatorExternalPanelHost.PreviewToDocument(Vector2 previewPoint)
    {
        return CreateExternalPanelCoordinateSpace().ViewToDocument(previewPoint);
    }

    void IReanimatorExternalPanelHost.BeginUndo(string operationName)
    {
        RegisterReanimatorUndo(string.IsNullOrWhiteSpace(operationName) ? "External Panel Change" : operationName);
    }

    void IReanimatorExternalPanelHost.SelectBone(int boneIndex)
    {
        layerPreviewRenderer?.SelectBoneForExternalPanel(boneIndex);
    }

    void IReanimatorExternalPanelHost.SetBoneWorldTransform(
        int boneIndex,
        Vector2 worldPosition,
        Quaternion worldRotation,
        float length,
        bool commit)
    {
        if (layerPreviewRenderer == null)
            return;

        if (layerPreviewRenderer.SetBoneWorldTransformFromExternalPanel(
                boneIndex,
                worldPosition,
                worldRotation,
                length,
                commit))
        {
            Repaint();
        }
    }

    void IReanimatorExternalPanelHost.CommitCurrentBonePoseAsRest()
    {
        if (layerPreviewRenderer == null)
            return;

        layerPreviewRenderer.CommitCurrentBonePoseAsRestFromExternalPanel();
        Repaint();
    }

    void IReanimatorExternalPanelHost.ShowNotification(string message)
    {
        ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(message) ? "External panel" : message));
    }

    void IReanimatorExternalPanelHost.RepaintHost()
    {
        Repaint();
    }

    private PsdCoordinateSpace CreateExternalPanelCoordinateSpace()
    {
        var docSize = layerPreviewRenderer != null ? layerPreviewRenderer.DocumentSize : Vector2Int.zero;
        if (docSize.x <= 0 || docSize.y <= 0)
            docSize = new Vector2Int(1, 1);

        var layers = layerPreviewRenderer != null ? layerPreviewRenderer.Layers : null;
        return new PsdCoordinateSpace(cachedPreviewRect, docSize, layers, previewNavigator, PsdPreviewLayoutMode.Skeleton);
    }
}
#endif
