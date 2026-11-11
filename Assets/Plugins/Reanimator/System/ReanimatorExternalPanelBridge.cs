#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct ReanimatorExternalBone
{
    public readonly int Index;
    public readonly string Name;
    public readonly int ParentIndex;
    public readonly Vector2 WorldPosition;
    public readonly Quaternion WorldRotation;
    public readonly float Length;
    public readonly Color Color;
    public readonly bool Visible;
    public readonly bool Selected;

    public ReanimatorExternalBone(
        int index,
        string name,
        int parentIndex,
        Vector2 worldPosition,
        Quaternion worldRotation,
        float length,
        Color color,
        bool visible,
        bool selected)
    {
        Index = index;
        Name = name ?? string.Empty;
        ParentIndex = parentIndex;
        WorldPosition = worldPosition;
        WorldRotation = worldRotation;
        Length = length;
        Color = color;
        Visible = visible;
        Selected = selected;
    }
}

public interface IReanimatorExternalPanel
{
    string Id { get; }
    string Title { get; }
    void DrawLeftPanel(IReanimatorExternalPanelHost host);
    void DrawRightPanel(IReanimatorExternalPanelHost host, float availableHeight);
    void DrawPreviewOverlay(IReanimatorExternalPanelHost host);
}

public interface IReanimatorExternalCommandPanel
{
    bool InvokeCommand(string label, IReanimatorExternalPanelHost host);
}

public interface IReanimatorExternalBlockingProgressPanel
{
    bool TryGetBlockingProgress(out string title, out string status, out float progress);
}

public interface IReanimatorExternalPanelHost
{
    string CurrentAssetPath { get; }
    Rect PreviewRect { get; }
    bool TryValidateAllInPreflight(out string error);
    IReadOnlyList<ReanimatorExternalBone> CaptureBones();
    string CaptureWorkspacePng();
    int SecondaryArmLayerCount { get; }
    string CaptureAutoSkeletonArmPassWorkspacePng(bool secondaryPair);
    Vector2 WorkspaceNormalizedToDocument(Vector2 normalizedTopLeft);
    string ApplyDefaultAutoGeometry();
    string ApplyAutoSkeletonPoseJson(
        string poseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls);
    string ApplyAutoSkeletonPoseJsonWithSecondaryArms(
        string primaryPoseJson,
        string secondaryArmsPoseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls);
    string ApplyDeterministicAutoWeights();
    string ApplyFaceEyesAutomation();
    string LocateNipples(
        out Vector2 leftDocumentPoint,
        out Vector2 rightDocumentPoint,
        out float leftConfidence,
        out float rightConfidence);
    bool TryConstrainBoobContourToVisibleSprite(
        Vector2 centerDocumentPoint,
        IReadOnlyList<Vector2> contourDocumentPoints,
        out Vector2[] constrainedDocumentPoints,
        out string layerName);
    bool HasSeparatedBreastSprites();
    string ApplyFullBoobsWeights(
        Vector2 viewerLeftNippleDocumentPoint,
        Vector2 viewerRightNippleDocumentPoint,
        IReadOnlyList<Vector2> viewerLeftContourDocumentPoints,
        IReadOnlyList<Vector2> viewerRightContourDocumentPoints);
    bool HasFashionableFolder();
    string ApplyFashionable();
    string CookPrefabNextToPsd();
    Vector2 DocumentToPreview(Vector2 documentPoint);
    Vector2 PreviewToDocument(Vector2 previewPoint);
    string RunUndoGroup(string operationName, Func<string> apply);
    void BeginUndo(string operationName);
    void SelectBone(int boneIndex);
    void SetBoneWorldTransform(int boneIndex, Vector2 worldPosition, Quaternion worldRotation, float length, bool commit);
    void CommitCurrentBonePoseAsRest();
    void ShowNotification(string message);
    void RepaintHost();
}

public static class ReanimatorExternalPanelRegistry
{
    private static readonly Dictionary<string, IReanimatorExternalPanel> Panels =
        new Dictionary<string, IReanimatorExternalPanel>(StringComparer.OrdinalIgnoreCase);

    public static void Register(IReanimatorExternalPanel panel)
    {
        if (panel == null || string.IsNullOrWhiteSpace(panel.Id))
            return;

        Panels[panel.Id] = panel;
    }

    public static bool TryGet(string id, out IReanimatorExternalPanel panel)
    {
        panel = null;
        return !string.IsNullOrWhiteSpace(id) && Panels.TryGetValue(id, out panel) && panel != null;
    }
}
#endif
