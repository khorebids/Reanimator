#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow
{
    private void DrawBoneDebugPanel(float availableHeight)
    {
        var contentHeight = Mathf.Max(80f, availableHeight - 10f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(weightScroll, GUILayout.Height(contentHeight)))
            {
                weightScroll = scrollScope.scrollPosition;
                EditorGUILayout.LabelField("Bone Debug", EditorStyles.boldLabel);
                if (layerPreviewRenderer == null || !layerPreviewRenderer.TryGetSelectedBoneDebugInfo(out var info))
                {
                    EditorGUILayout.LabelField("Select a bone.");
                    return;
                }

                DrawInfluenceSwatch(info.color, $"{info.rendererBoneIndex}: {info.boneName}");
                DrawBoneDebugIssueIcons(info.issues);
                EditorGUILayout.LabelField("Parent", info.parentIndex >= 0 ? $"{info.parentIndex}: {info.parentName}" : "<root>");
                EditorGUILayout.LabelField("Children", info.childCount.ToString());
                EditorGUILayout.LabelField("Rest Pos", FormatDebugVector(info.restPosition));
                EditorGUILayout.LabelField("Local Pos", FormatDebugVector(info.localPosition));
                EditorGUILayout.LabelField("World Pos", FormatDebugVector(info.worldPosition));
                EditorGUILayout.LabelField("Length", info.length.ToString("0.###"));
                EditorGUILayout.LabelField("Pose Delta", $"{info.posePositionDelta:0.###} px, {info.poseRotationDeltaDeg:0.###} deg");

                GUILayout.Space(4f);
                EditorGUILayout.LabelField("Weighted Sprites", $"{info.weightedSpriteCount}/{info.mappedSpriteCount}");
                EditorGUILayout.LabelField("Vertices", $"{info.influencedVertexCount}/{info.mappedVertexCount}");
                EditorGUILayout.LabelField("Total Weight", info.totalWeight.ToString("0.###"));
                EditorGUILayout.LabelField("Max Weight", info.maxWeight.ToString("0.###"));

                DrawBoneDebugFixButtons(info);

                GUILayout.Space(6f);
                var sprites = info.sprites ?? Array.Empty<PsdLayerPreviewRenderer.BoneDebugSpriteInfo>();
                if (sprites.Length == 0)
                {
                    EditorGUILayout.LabelField("No sprite bone maps reference this bone.", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);
                    for (var i = 0; i < sprites.Length; i++)
                        DrawBoneDebugSpriteRow(sprites[i]);
                }
            }
        }
    }

    private void DrawBoneDebugFixButtons(PsdLayerPreviewRenderer.BoneDebugInfo info)
    {
        GUILayout.Space(5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope((info.issues & PsdLayerPreviewRenderer.BoneDebugIssueFlags.PoseChanged) == 0))
            {
                if (GUILayout.Button("Restore Pose", GUILayout.Height(22f)))
                    RestorePreviewPose();
            }

            using (new EditorGUI.DisabledScope(info.influencedVertexCount <= 0))
            {
                if (GUILayout.Button("Select Vertices", GUILayout.Height(22f)))
                {
                    if (layerPreviewRenderer.TrySelectSelectedBoneInfluenceVertices(out var selectedCount, out var layerName, out var boneName, out var error))
                        ShowNotification(new GUIContent($"{boneName}: {selectedCount} vertices on {layerName}"));
                    else
                        ShowNotification(new GUIContent(string.IsNullOrWhiteSpace(error) ? "No influenced vertices" : error));
                    Repaint();
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bone Tool", GUILayout.Height(22f)))
            {
                SetBottomToolMode(ReanimatorToolMode.BoneInfluence);
                Repaint();
            }

            if (GUILayout.Button("Generate Geo", GUILayout.Height(22f)))
                GenerateMissingGeometry();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fix Weights", GUILayout.Height(22f)))
                FixSelectedSpriteSkinWeights();

            if (GUILayout.Button("Auto Weights", GUILayout.Height(22f)))
                ApplyAutoWeights();
        }
    }

    private static void DrawBoneDebugSpriteRow(PsdLayerPreviewRenderer.BoneDebugSpriteInfo sprite)
    {
        var rect = GUILayoutUtility.GetRect(1f, BoneVisibilityRowHeight, GUILayout.ExpandWidth(true));
        if (Event.current != null && Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.19f, 1f));

        var iconWidth = sprite.layerIssues == PsdLayerPreviewRenderer.LayerDebugIssueFlags.None ? 18f : Mathf.Min(88f, Mathf.Max(18f, CountLayerIssueIcons(sprite.layerIssues) * 17f));
        var iconRect = new Rect(rect.xMax - iconWidth, rect.y, iconWidth, rect.height);
        var weightRect = new Rect(iconRect.x - 92f, rect.y, 88f, rect.height);
        var nameRect = new Rect(rect.x + 2f, rect.y, Mathf.Max(40f, weightRect.x - rect.x - 4f), rect.height);

        GUI.Label(nameRect, $"{sprite.layerName} [{sprite.mappedSpriteBoneIndex}]", EditorStyles.miniLabel);
        GUI.Label(weightRect, $"{sprite.influencedVertexCount}/{sprite.vertexCount} w:{sprite.totalWeight:0.##}", EditorStyles.centeredGreyMiniLabel);
        DrawLayerIssueIcons(iconRect, sprite.layerIssues);
    }

    private static void DrawBoneDebugIssueIcons(PsdLayerPreviewRenderer.BoneDebugIssueFlags issues)
    {
        var rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
        if (issues == PsdLayerPreviewRenderer.BoneDebugIssueFlags.None)
        {
            DrawLayerDebugIssueIcon(new Rect(rect.x, rect.y + 2f, 18f, rect.height - 4f), "OK", "Bone has no detected debug issues.", new Color(0.05f, 0.45f, 0.12f, 1f));
            return;
        }

        var x = rect.x;
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.Hidden, "H", "Bone is hidden.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.MissingParent, "P", "Parent bone index is missing.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.ZeroLength, "L", "Bone length is zero and it has no children.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.PoseChanged, "D", "Bone differs from its default/rest pose.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.NoWeightedSprites, "S", "No sprite vertices are weighted to this bone.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.MissingGeometry, "G", "A referenced sprite has missing geometry.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.WeightIssue, "W", "A referenced sprite has weight issues.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.BoneMapIssue, "B", "A referenced sprite has bone-map issues.");
        DrawBoneDebugIssueIconIfSet(ref x, rect, issues, PsdLayerPreviewRenderer.BoneDebugIssueFlags.BindPoseIssue, "M", "A referenced sprite has bind-pose issues.");
    }

    private static void DrawBoneDebugIssueIconIfSet(
        ref float x,
        Rect rowRect,
        PsdLayerPreviewRenderer.BoneDebugIssueFlags issues,
        PsdLayerPreviewRenderer.BoneDebugIssueFlags flag,
        string label,
        string tooltip)
    {
        if ((issues & flag) == 0)
            return;

        DrawLayerDebugIssueIcon(new Rect(x, rowRect.y + 2f, 18f, rowRect.height - 4f), label, tooltip, new Color(0.74f, 0.02f, 0.02f, 1f));
        x += 19f;
    }

    private static string FormatDebugVector(Vector2 value)
    {
        return $"{value.x:0.###}, {value.y:0.###}";
    }

}
#endif
