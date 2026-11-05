#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private bool TryInsertJointContourIntersections(
        LayerVertexData data,
        Vector2 centerDoc,
        int parentSpriteBone,
        out int updatedCount)
    {
        updatedCount = 0;
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return false;

        var boundaryPairs = ResolveBoundaryPairsForJointIntersections(data);
        if (boundaryPairs == null || boundaryPairs.Length < 2)
            return false;

        var parentWeight = BuildSingleBoneWeight(parentSpriteBone);
        var hasStartHit = TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, jointStartAngleRad, out var startHitDoc);
        var hasEndHit = TryGetNearestContourRayIntersection(data.vertices, boundaryPairs, centerDoc, jointEndAngleRad, out var endHitDoc);
        if (!hasStartHit && !hasEndHit)
            return false;

        if (hasStartHit && TryEnsureJointContourIntersectionVertex(data, startHitDoc, parentWeight, out var startChanged) && startChanged)
            updatedCount++;

        const float intersectionMergeDistanceSqr = 0.0001f;
        if (hasEndHit &&
            (!hasStartHit || (endHitDoc - startHitDoc).sqrMagnitude > intersectionMergeDistanceSqr) &&
            TryEnsureJointContourIntersectionVertex(data, endHitDoc, parentWeight, out var endChanged) &&
            endChanged)
        {
            updatedCount++;
        }

        return updatedCount > 0;
    }

    private static int[] ResolveBoundaryPairsForJointIntersections(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length < 3)
            return Array.Empty<int>();

        if (data.boundaryEdgePairs != null && data.boundaryEdgePairs.Length >= 6)
            return data.boundaryEdgePairs;

        if (data.triangles == null || data.triangles.Length < 3)
            return Array.Empty<int>();

        BuildEdgePairs(data.triangles, data.vertices.Length, out _, out var fallbackBoundaryPairs);
        return fallbackBoundaryPairs != null && fallbackBoundaryPairs.Length >= 6
            ? fallbackBoundaryPairs
            : Array.Empty<int>();
    }

    private static bool TryGetNearestContourRayIntersection(
        IReadOnlyList<Vector2> vertices,
        IReadOnlyList<int> boundaryPairs,
        Vector2 rayOriginDoc,
        float angleRad,
        out Vector2 hitPointDoc)
    {
        return ReanimatorJointGeometryAlgorithms.TryGetNearestContourRayIntersection(vertices, boundaryPairs, rayOriginDoc, angleRad, out hitPointDoc);
    }

    private bool TryEnsureJointContourIntersectionVertex(
        LayerVertexData data,
        Vector2 intersectionDoc,
        BoneWeight parentWeight,
        out bool changed)
    {
        changed = false;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        const float existingVertexSnapDistanceSqr = 0.0016f;
        var vertexIndex = FindNearestVertexIndex(intersectionDoc, data.vertices);
        if (vertexIndex < 0 || (data.vertices[vertexIndex] - intersectionDoc).sqrMagnitude > existingVertexSnapDistanceSqr)
        {
            if (!InsertAppliedVertexIntoLayerData(data, intersectionDoc, true))
                return false;

            changed = true;
            vertexIndex = FindNearestVertexIndex(intersectionDoc, data.vertices);
        }

        if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
            return false;
        if ((data.vertices[vertexIndex] - intersectionDoc).sqrMagnitude > existingVertexSnapDistanceSqr * 4f)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return false;

        data.boneWeights[vertexIndex] = parentWeight;
        changed = true;
        return true;
    }

    private bool TryEnsureJointInteriorVertex(
        LayerVertexData data,
        Vector2 pointDoc,
        BoneWeight parentWeight,
        out bool changed)
    {
        changed = false;
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        const float existingVertexSnapDistanceSqr = 0.0016f;
        var vertexIndex = FindNearestVertexIndex(pointDoc, data.vertices);
        if (vertexIndex < 0 || (data.vertices[vertexIndex] - pointDoc).sqrMagnitude > existingVertexSnapDistanceSqr)
        {
            var contourBefore = data.boundaryEdgePairs != null ? (int[])data.boundaryEdgePairs.Clone() : Array.Empty<int>();
            if (!InsertAppliedVertexIntoLayerData(data, pointDoc, true))
                return false;

            changed = true;
            vertexIndex = FindNearestVertexIndex(pointDoc, data.vertices);
            if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
                return false;

            if (contourBefore.Length >= 6)
            {
                data.boundaryEdgePairs = contourBefore;
                if (!TryRetriangulateKeepingVertexSet(data) && !TryRetriangulateWithDirectFallback(data))
                    return false;

                data.triangles = SanitizeTriangles(data.triangles, data.vertices.Length);
                if (data.triangles == null || data.triangles.Length < 3)
                    return false;

                BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out _);
                data.boundaryEdgePairs = contourBefore;
            }
        }

        if (vertexIndex < 0 || vertexIndex >= data.vertices.Length)
            return false;
        if ((data.vertices[vertexIndex] - pointDoc).sqrMagnitude > existingVertexSnapDistanceSqr * 4f)
            return false;

        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        if (vertexIndex < 0 || vertexIndex >= data.boneWeights.Length)
            return false;

        data.boneWeights[vertexIndex] = parentWeight;
        changed = true;
        return true;
    }
}
#endif
