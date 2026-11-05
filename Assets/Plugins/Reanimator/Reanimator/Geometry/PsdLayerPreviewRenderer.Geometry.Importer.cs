#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static int FindNearestReferenceVertexIndex(Vector2[] referenceVertices, int referenceCount, Vector2 point)
    {
        return ReanimatorImporterGeometryAlgorithms.FindNearestReferenceVertexIndex(referenceVertices, referenceCount, point);
    }

    private static Vector2 ConvertDocToImporterPosition(
        LayerVertexData data,
        LayerSpriteBinding binding,
        IReadOnlyList<Vertex2DMetaData> currentImporterVertices,
        IReadOnlyList<int> currentImporterIndices,
        Vector2 docPos)
    {
        if (data == null || data.layer == null || data.vertices == null)
            return Vector2.zero;

        var importerCount = currentImporterVertices != null ? currentImporterVertices.Count : 0;
        var referenceCount = Mathf.Min(importerCount, data.vertices.Length);
        if (referenceCount >= 3 &&
            TryMapPointToImporterByTriangles(data.vertices, currentImporterVertices, currentImporterIndices, docPos, referenceCount, out var mapped))
            return mapped;

        return PsdCoordinateSpace.DocumentToLayerLocalPoint(data.layer, docPos);
    }

    private static bool TryMapPointToImporterByTriangles(
        Vector2[] referenceDocVertices,
        IReadOnlyList<Vertex2DMetaData> importerVertices,
        IReadOnlyList<int> importerIndices,
        Vector2 docPos,
        int referenceCount,
        out Vector2 mapped)
    {
        return ReanimatorImporterGeometryAlgorithms.TryMapPointToImporterByTriangles(
            referenceDocVertices,
            importerVertices,
            importerIndices,
            docPos,
            referenceCount,
            out mapped);
    }

    private static int[] BuildImporterIndices(LayerVertexData data, int vertexCount, IReadOnlyList<int> fallback)
    {
        return ReanimatorImporterGeometryAlgorithms.BuildImporterIndices(
            data != null ? data.triangles : null,
            vertexCount,
            fallback);
    }

    private static int[] SanitizeTriangles(int[] triangles, int vertexCount)
    {
        return ReanimatorImporterGeometryAlgorithms.SanitizeTriangles(triangles, vertexCount);
    }
}
#endif
