#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private static bool TryTriangulateSimplePolygon(IReadOnlyList<Vector2> polygon, List<int> triangles)
    {
        return ReanimatorTriangulationAlgorithms.TryTriangulateSimplePolygon(polygon, triangles);
    }

    private static float ComputeSignedArea(IReadOnlyList<Vector2> polygon)
    {
        return ReanimatorTriangulationAlgorithms.ComputeSignedArea(polygon);
    }

    private static void SubdivideGeneratedGeometry(ref Vector2[] vertices, ref int[] triangles, int subdivide)
    {
        ReanimatorTriangulationAlgorithms.SubdivideGeneratedGeometry(ref vertices, ref triangles, subdivide);
    }

    private static void AddOrientedTriangle(List<int> triangles, int a, int b, int c, float windingSign)
    {
        ReanimatorTriangulationAlgorithms.AddOrientedTriangle(triangles, a, b, c, windingSign);
    }
}
#endif
