#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private void DrawJointV2GeneratedPreview(
        PsdCoordinateSpace coordinates,
        AnimationCurve weightCurve,
        AnimationCurve specialWeightCurve,
        float specialJointDistance,
        float boneBaseOffset)
    {
        if (coordinates == null || Event.current == null || Event.current.type != EventType.Repaint)
            return;
        if (!TryBuildJointV2PreviewGeometry(
                out _,
                out var previewData,
                out var generatedVertexIndices,
                out var dynamicSpriteBone,
                out _,
                weightCurve,
                specialWeightCurve,
                specialJointDistance))
        {
            return;
        }

        List<BoneInfo> virtualBones = null;
        var attachedContourPoints = new List<Vector2>(16);
        if (TryGetJointV2AttachedGuidePoints(
                specialJointDistance,
                out var centerPoint,
                out _,
                out _,
                out _,
                attachedContourPoints))
        {
            var dynamicBoneBaseTarget = GetJointV2InterpolatedBaseTarget(centerPoint, boneBaseOffset);
            TryBuildJointV2VirtualBonePose(dynamicBoneBaseTarget, out virtualBones);
        }

        // Moving the generated Joint V2 base is a rest-skeleton edit, not an
        // additional pose deformation. Keep the geometry evaluated by the
        // untouched source pose and draw the derived bone independently.
        // Otherwise the old bind matrices apply the base relocation a second
        // time and visibly pull the seam away from the generated contour.
        var drawVertices = GetLayerVerticesForDraw(previewData);
        if (drawVertices == null || drawVertices.Length != previewData.vertices.Length)
            return;

        var toView = CreateLayerVertexToViewMapper(coordinates, previewData, drawVertices);
        DrawBoneInfluenceOnGeometryTriangles(
            toView,
            drawVertices,
            previewData.boneWeights,
            previewData.triangles,
            previewData.spriteBoneToRendererBone,
            0.68f,
            int.MaxValue,
            1f);

        Handles.BeginGUI();
        var previousColor = Handles.color;
        var contourEdges = BuildEdgeKeySet(previewData.boundaryEdgePairs, drawVertices.Length);
        if (previewData.allEdgePairs != null)
        {
            for (var pairIndex = 0; pairIndex + 1 < previewData.allEdgePairs.Length; pairIndex += 2)
            {
                var indexA = previewData.allEdgePairs[pairIndex];
                var indexB = previewData.allEdgePairs[pairIndex + 1];
                if (indexA < 0 || indexB < 0 || indexA >= drawVertices.Length || indexB >= drawVertices.Length)
                    continue;

                var isContour = contourEdges != null && contourEdges.Contains(MakeEdgeKey(indexA, indexB));
                Handles.color = isContour
                    ? new Color(1f, 0.48f, 0.08f, 0.96f)
                    : new Color(0.18f, 0.92f, 1f, 0.72f);
                Handles.DrawLine(toView(drawVertices[indexA]), toView(drawVertices[indexB]));
            }
        }

        var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 9
        };
        labelStyle.normal.textColor = Color.white;
        for (var generatedIndex = 0; generatedIndex < generatedVertexIndices.Count; generatedIndex++)
        {
            var vertexIndex = generatedVertexIndices[generatedIndex];
            if (vertexIndex < 0 || vertexIndex >= drawVertices.Length ||
                previewData.boneWeights == null || vertexIndex >= previewData.boneWeights.Length)
            {
                continue;
            }

            var point = toView(drawVertices[vertexIndex]);
            var pointColor = EvaluateInfluenceVertexColor(
                previewData.boneWeights[vertexIndex],
                previewData.spriteBoneToRendererBone,
                1f);
            pointColor.a = 1f;
            Handles.color = new Color(0.02f, 0.03f, 0.05f, 0.98f);
            Handles.DrawSolidDisc(point, Vector3.forward, 4.2f);
            Handles.color = pointColor;
            Handles.DrawSolidDisc(point, Vector3.forward, 2.8f);

            if (generatedIndex != 0 && generatedIndex != 4 && generatedIndex != 8 &&
                generatedIndex != 12 && generatedIndex != 16 && generatedIndex != 17 &&
                generatedIndex != generatedVertexIndices.Count - 1)
            {
                continue;
            }

            var dynamicWeight = GetSpriteBoneWeight(previewData.boneWeights[vertexIndex], dynamicSpriteBone);
            var labelRect = new Rect(point.x + 5f, point.y - 8f, 34f, 16f);
            EditorGUI.DrawRect(labelRect, new Color(0.03f, 0.04f, 0.06f, 0.82f));
            GUI.Label(labelRect, Mathf.RoundToInt(dynamicWeight * 100f).ToString(), labelStyle);
        }

        for (var generatedIndex = 0; generatedIndex < generatedVertexIndices.Count; generatedIndex++)
        {
            var vertexIndex = generatedVertexIndices[generatedIndex];
            if (vertexIndex < 0 || vertexIndex >= drawVertices.Length)
                continue;

            var point = toView(drawVertices[vertexIndex]);
            if (generatedIndex < 16)
            {
                DrawJointV2GuidePoint(
                    point,
                    new Color(1f, 0.04f, 0.42f, 1f),
                    5.4f);
            }
            else if (generatedIndex == generatedVertexIndices.Count - 1)
            {
                DrawJointV2GuidePoint(
                    point,
                    new Color(1f, 0.08f, 0.06f, 1f),
                    6.1f);
            }
        }

        if (virtualBones != null && selectedBoneIndex >= 0 && selectedBoneIndex < virtualBones.Count)
        {
            var virtualBone = virtualBones[selectedBoneIndex];
            if (virtualBone != null)
            {
                var start = coordinates.DocumentToView(virtualBone.worldPosition);
                var rotated = virtualBone.worldRotation * Vector3.right;
                var direction = new Vector2(rotated.x, rotated.y);
                var end = coordinates.DocumentToView(
                    virtualBone.worldPosition + direction * virtualBone.length);
                var previewBoneColor = virtualBone.color;
                previewBoneColor.a = Mathf.Max(0.82f, previewBoneColor.a);
                DrawBoneShape(start, end, previewBoneColor, 0.9f, true, false);
            }
        }

        Handles.color = previousColor;
        Handles.EndGUI();
    }

}
#endif
