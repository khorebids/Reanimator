#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    private Vector2 ConvertPointToBaseDoc(LayerVertexData data, Vector2 pointDoc)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return pointDoc;

        var drawVertices = GetLayerVerticesForDraw(data);
        if (drawVertices == null || drawVertices.Length != data.vertices.Length)
            return pointDoc;
        if (ReferenceEquals(drawVertices, data.vertices))
            return pointDoc;

        return PsdCoordinateSpace.MapMeshPointOrSelf(drawVertices, data.vertices, data.triangles, pointDoc);
    }

    private void MigrateLegacyAppliedVerticesToAsset(string assetPath)
    {
        if (appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return;

        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalizedAssetPath))
            return;

        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
                continue;
            if (!string.IsNullOrEmpty(NormalizeAssetPath(point.assetPath)))
                continue;
            point.assetPath = normalizedAssetPath;
        }
    }

    private void RemoveAppliedVerticesForAsset(string assetPath)
    {
        if (appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return;

        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalizedAssetPath))
            return;

        for (var i = appliedElbowVertices.Count - 1; i >= 0; i--)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
            {
                appliedElbowVertices.RemoveAt(i);
                continue;
            }

            if (IsAssetPathMatch(point.assetPath, normalizedAssetPath))
                appliedElbowVertices.RemoveAt(i);
        }
    }

    private bool TryAddAppliedVertex(string assetPath, string layerSelectionKey, string layerStableKey, Vector2 positionDoc, bool applyToGeometry)
    {
        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalizedAssetPath))
            normalizedAssetPath = NormalizeAssetPath(cachedPath);
        if (string.IsNullOrEmpty(normalizedAssetPath))
            return false;

        if (appliedElbowVertices == null)
            appliedElbowVertices = new List<AppliedElbowVertex>();
        MigrateLegacyAppliedVerticesToAsset(normalizedAssetPath);

        var storedPositionDoc = positionDoc;
        LayerVertexData targetData = null;
        if (TryGetLayerVertexDataForKeys(layerSelectionKey, layerStableKey, out targetData))
            storedPositionDoc = ConvertPointToBaseDoc(targetData, positionDoc);

        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var existing = appliedElbowVertices[i];
            if (existing == null)
                continue;
            if (!IsAssetPathMatch(existing.assetPath, normalizedAssetPath))
                continue;
            if (!IsLayerMatch(existing.layerSelectionKey, existing.layerStableKey, layerSelectionKey, layerStableKey))
                continue;
            if ((existing.positionDoc - storedPositionDoc).sqrMagnitude > 0.0001f)
                continue;
            if (applyToGeometry && targetData != null)
                InsertAppliedVertexIntoLayerData(targetData, positionDoc);
            return false;
        }

        appliedElbowVertices.Add(new AppliedElbowVertex
        {
            assetPath = normalizedAssetPath,
            layerSelectionKey = layerSelectionKey,
            layerStableKey = layerStableKey,
            positionDoc = storedPositionDoc
        });
        if (applyToGeometry && targetData != null)
            InsertAppliedVertexIntoLayerData(targetData, positionDoc);
        return true;
    }

    private void ApplyStoredAppliedVerticesToGeometry(string assetPath)
    {
        if (appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return;
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        MigrateLegacyAppliedVerticesToAsset(normalizedAssetPath);

        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
                continue;
            if (!IsAssetPathMatch(point.assetPath, normalizedAssetPath))
                continue;
            if (!TryGetLayerVertexDataForKeys(point.layerSelectionKey, point.layerStableKey, out var data))
                continue;
            InsertAppliedVertexIntoLayerData(data, point.positionDoc, true);
        }
    }

    private bool HasAppliedVerticesForLayer(string assetPath, string selectionKey, string stableKey)
    {
        if (appliedElbowVertices == null || appliedElbowVertices.Count == 0)
            return false;
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        for (var i = 0; i < appliedElbowVertices.Count; i++)
        {
            var point = appliedElbowVertices[i];
            if (point == null)
                continue;
            if (!IsAssetPathMatch(point.assetPath, assetPath))
                continue;
            if (IsLayerMatch(point.layerSelectionKey, point.layerStableKey, selectionKey, stableKey))
                return true;
        }

        return false;
    }

    private bool InsertAppliedVertexIntoLayerData(LayerVertexData data, Vector2 pointDoc, bool pointAlreadyInBaseSpace = false)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;

        var basePointDoc = pointAlreadyInBaseSpace ? pointDoc : ConvertPointToBaseDoc(data, pointDoc);
        var oldVertices = data.vertices;
        var oldCount = oldVertices.Length;
        for (var i = 0; i < oldCount; i++)
        {
            if ((oldVertices[i] - basePointDoc).sqrMagnitude <= 0.0001f)
                return false;
        }

        elbowConnectionBuffer.Clear();
        BuildAddVertexStyleConnections(data, oldVertices, basePointDoc, elbowConnectionBuffer);

        Array.Resize(ref data.vertices, oldCount + 1);
        data.vertices[oldCount] = basePointDoc;

        if (data.vertexUvs == null || data.vertexUvs.Length < oldCount)
        {
            var repaired = new Vector2[oldCount + 1];
            if (data.vertexUvs != null && data.vertexUvs.Length > 0)
                Array.Copy(data.vertexUvs, repaired, Mathf.Min(data.vertexUvs.Length, oldCount));
            data.vertexUvs = repaired;
        }
        else
        {
            Array.Resize(ref data.vertexUvs, oldCount + 1);
        }

        data.vertexUvs[oldCount] = ComputeUvForPoint(data.layer, basePointDoc);

        if (data.boneWeights != null && data.boneWeights.Length == oldCount)
        {
            var nearest = FindNearestVertexIndex(basePointDoc, oldVertices);
            var nearestWeight = nearest >= 0 ? data.boneWeights[nearest] : default;
            Array.Resize(ref data.boneWeights, oldCount + 1);
            data.boneWeights[oldCount] = nearestWeight;
        }

        if (data.deformedVertices != null && data.deformedVertices.Length == oldCount)
        {
            Array.Resize(ref data.deformedVertices, oldCount + 1);
            data.deformedVertices[oldCount] = basePointDoc;
        }

        var hadBoundaryInsert =
            TryInsertVertexIntoBoundaryLoop(data.boundaryEdgePairs, oldVertices, basePointDoc, oldCount, out var updatedBoundaryPairs) &&
            updatedBoundaryPairs != null &&
            updatedBoundaryPairs.Length >= 6;

        var triangulated = TryInsertVertexIntoTriangles(data, oldVertices, basePointDoc, oldCount);
        if (triangulated)
        {
            if (hadBoundaryInsert)
            {
                data.boundaryEdgePairs = updatedBoundaryPairs;
                if (!TryRetriangulateKeepingVertexSet(data))
                    TryRetriangulateWithDirectFallback(data);
                OptimizeLayerTriangleQuality(data);
                BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out _);
                data.boundaryEdgePairs = updatedBoundaryPairs;
            }
            else
            {
                OptimizeLayerTriangleQuality(data);
                BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out data.boundaryEdgePairs);
            }
        }
        else
        {
            for (var i = 0; i < elbowConnectionBuffer.Count; i++)
                AddEdgePairUnique(ref data.allEdgePairs, oldCount, elbowConnectionBuffer[i]);

            if (hadBoundaryInsert)
            {
                data.boundaryEdgePairs = updatedBoundaryPairs;
                if (!TryRetriangulateKeepingVertexSet(data))
                    TryRetriangulateWithDirectFallback(data);
                OptimizeLayerTriangleQuality(data);
                BuildEdgePairs(data.triangles, data.vertices.Length, out data.allEdgePairs, out _);
                data.boundaryEdgePairs = updatedBoundaryPairs;
            }
            else
            {
                TryAddBoundaryConnections(data, oldVertices, basePointDoc, oldCount);
            }
        }

        data.lastPoseVersion = -1;
        return true;
    }

    private static bool TryInsertVertexIntoTriangles(LayerVertexData data, Vector2[] verticesBeforeInsert, Vector2 pointDoc, int newIndex)
    {
        if (data == null || verticesBeforeInsert == null || verticesBeforeInsert.Length == 0)
            return false;
        if (data.triangles == null || data.triangles.Length < 3)
            return false;

        if (!TryFindContainingTriangle(
                data.triangles,
                verticesBeforeInsert,
                pointDoc,
                out var triangleStart,
                out var ia,
                out var ib,
                out var ic,
                out var dAB,
                out var dBC,
                out var dCA,
                out var avgEdgeLength))
            return false;

        var minDistanceToEdge = Mathf.Min(dAB, Mathf.Min(dBC, dCA));
        var edgeSnap = Mathf.Max(0.45f, avgEdgeLength * 0.1f);
        if (minDistanceToEdge <= edgeSnap)
        {
            var edgeA = ia;
            var edgeB = ib;
            if (dBC <= dAB && dBC <= dCA)
            {
                edgeA = ib;
                edgeB = ic;
            }
            else if (dCA <= dAB && dCA <= dBC)
            {
                edgeA = ic;
                edgeB = ia;
            }

            return SplitTrianglesOnEdge(ref data.triangles, verticesBeforeInsert, pointDoc, newIndex, edgeA, edgeB);
        }

        return SplitSingleTriangle(ref data.triangles, triangleStart, ia, ib, ic, newIndex);
    }

    private static bool SplitSingleTriangle(ref int[] triangles, int triangleStart, int ia, int ib, int ic, int newIndex)
    {
        return ReanimatorVertexInsertionAlgorithms.SplitSingleTriangle(ref triangles, triangleStart, ia, ib, ic, newIndex);
    }

    private static bool SplitTrianglesOnEdge(
        ref int[] triangles,
        Vector2[] verticesBeforeInsert,
        Vector2 pointDoc,
        int newIndex,
        int edgeA,
        int edgeB)
    {
        return ReanimatorVertexInsertionAlgorithms.SplitTrianglesOnEdge(ref triangles, verticesBeforeInsert, pointDoc, newIndex, edgeA, edgeB);
    }

    private static bool TryGetThirdVertexForEdge(int t0, int t1, int t2, int edgeA, int edgeB, out int third)
    {
        return ReanimatorVertexInsertionAlgorithms.TryGetThirdVertexForEdge(t0, t1, t2, edgeA, edgeB, out third);
    }

    private static void AppendEdgeSplitTriangles(
        List<int> target,
        int t0,
        int t1,
        int t2,
        int edgeA,
        int edgeB,
        int third,
        int newIndex,
        Vector2[] verticesBeforeInsert,
        Vector2 newPoint)
    {
        var a = verticesBeforeInsert[edgeA];
        var b = verticesBeforeInsert[edgeB];
        var c = verticesBeforeInsert[third];
        var originalSign = SignedTriangleArea(verticesBeforeInsert[t0], verticesBeforeInsert[t1], verticesBeforeInsert[t2]);

        var option1a = SignedTriangleArea(a, newPoint, c);
        var option1b = SignedTriangleArea(newPoint, b, c);
        var option1Valid = IsWindingCompatible(originalSign, option1a) && IsWindingCompatible(originalSign, option1b);

        if (option1Valid)
        {
            target.Add(edgeA);
            target.Add(newIndex);
            target.Add(third);
            target.Add(newIndex);
            target.Add(edgeB);
            target.Add(third);
            return;
        }

        target.Add(edgeB);
        target.Add(newIndex);
        target.Add(third);
        target.Add(newIndex);
        target.Add(edgeA);
        target.Add(third);
    }

    private static bool IsWindingCompatible(float expectedSign, float sign)
    {
        const float eps = 1e-6f;
        if (Mathf.Abs(expectedSign) <= eps || Mathf.Abs(sign) <= eps)
            return true;
        return Mathf.Sign(expectedSign) == Mathf.Sign(sign);
    }

    private static void OptimizeLayerTriangleQuality(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.triangles == null)
            return;
        if (data.vertices.Length < 4 || data.triangles.Length < 6)
            return;

        var protectedEdges = BuildEdgeKeySet(data.boundaryEdgePairs, data.vertices.Length) ?? new HashSet<ulong>();
        OptimizeTrianglesByEdgeFlips(data.vertices, data.triangles, protectedEdges);
    }

    private static void OptimizeTrianglesByEdgeFlips(Vector2[] vertices, int[] triangles, HashSet<ulong> protectedEdges)
    {
        ReanimatorVertexInsertionAlgorithms.OptimizeTrianglesByEdgeFlips(vertices, triangles, protectedEdges);
    }

    private static Dictionary<ulong, TriangleEdgeInfo> BuildTriangleEdgeInfos(int[] triangles, int vertexCount)
    {
        var result = new Dictionary<ulong, TriangleEdgeInfo>();
        if (triangles == null || vertexCount <= 0)
            return result;

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];
            if (!IsValidTriangleIndex(a, vertexCount) ||
                !IsValidTriangleIndex(b, vertexCount) ||
                !IsValidTriangleIndex(c, vertexCount) ||
                a == b || b == c || c == a)
                continue;

            AddTriangleEdgeInfo(result, a, b, c, i / 3);
            AddTriangleEdgeInfo(result, b, c, a, i / 3);
            AddTriangleEdgeInfo(result, c, a, b, i / 3);
        }

        return result;
    }

    private static void AddTriangleEdgeInfo(Dictionary<ulong, TriangleEdgeInfo> edges, int a, int b, int opposite, int triangleIndex)
    {
        if (edges == null || a < 0 || b < 0 || opposite < 0 || a == b || a == opposite || b == opposite)
            return;

        var key = MakeEdgeKey(a, b);
        if (edges.TryGetValue(key, out var edge))
        {
            edge.useCount++;
            if (edge.secondTriangle < 0)
            {
                edge.secondTriangle = triangleIndex;
                edge.secondOpposite = opposite;
            }
            edges[key] = edge;
            return;
        }

        edges[key] = new TriangleEdgeInfo
        {
            a = a,
            b = b,
            firstTriangle = triangleIndex,
            firstOpposite = opposite,
            secondTriangle = -1,
            secondOpposite = -1,
            useCount = 1
        };
    }

    private static bool TryFlipTriangleEdgeForQuality(Vector2[] vertices, int[] triangles, TriangleEdgeInfo edge)
    {
        if (vertices == null || triangles == null)
            return false;

        var a = edge.a;
        var b = edge.b;
        var c = edge.firstOpposite;
        var d = edge.secondOpposite;
        if (!IsValidTriangleIndex(a, vertices.Length) ||
            !IsValidTriangleIndex(b, vertices.Length) ||
            !IsValidTriangleIndex(c, vertices.Length) ||
            !IsValidTriangleIndex(d, vertices.Length))
            return false;
        if (a == b || a == c || a == d || b == c || b == d || c == d)
            return false;
        if (HasTriangleEdge(triangles, c, d))
            return false;
        if (!IsConvexFlipQuad(vertices[a], vertices[b], vertices[c], vertices[d]))
            return false;

        var beforeQuality = Mathf.Min(
            TriangleMinAngle(vertices[a], vertices[b], vertices[c]),
            TriangleMinAngle(vertices[b], vertices[a], vertices[d]));
        var afterQuality = Mathf.Min(
            TriangleMinAngle(vertices[c], vertices[d], vertices[a]),
            TriangleMinAngle(vertices[d], vertices[c], vertices[b]));
        if (afterQuality <= beforeQuality + 0.001f)
            return false;

        var firstOffset = edge.firstTriangle * 3;
        var secondOffset = edge.secondTriangle * 3;
        if (firstOffset < 0 || firstOffset + 2 >= triangles.Length ||
            secondOffset < 0 || secondOffset + 2 >= triangles.Length)
            return false;

        var firstSign = SignedTriangleArea(
            vertices[triangles[firstOffset]],
            vertices[triangles[firstOffset + 1]],
            vertices[triangles[firstOffset + 2]]);
        var secondSign = SignedTriangleArea(
            vertices[triangles[secondOffset]],
            vertices[triangles[secondOffset + 1]],
            vertices[triangles[secondOffset + 2]]);

        WriteTriangleWithWinding(vertices, triangles, firstOffset, c, d, a, firstSign);
        WriteTriangleWithWinding(vertices, triangles, secondOffset, d, c, b, secondSign);
        return true;
    }

    private static bool IsConvexFlipQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var sideC = SignedTriangleArea(a, b, c);
        var sideD = SignedTriangleArea(a, b, d);
        if (sideC * sideD >= -1e-6f)
            return false;

        var sideA = SignedTriangleArea(c, d, a);
        var sideB = SignedTriangleArea(c, d, b);
        return sideA * sideB < -1e-6f;
    }

    private static float TriangleMinAngle(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ba = a - b;
        var bc = c - b;
        var ca = a - c;
        var cb = b - c;
        return Mathf.Min(
            AngleBetween(ab, ac),
            Mathf.Min(AngleBetween(ba, bc), AngleBetween(ca, cb)));
    }

    private static float AngleBetween(Vector2 a, Vector2 b)
    {
        var denom = Mathf.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
        if (denom <= 1e-8f)
            return 0f;

        return Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b) / denom, -1f, 1f));
    }

    private static void WriteTriangleWithWinding(Vector2[] vertices, int[] triangles, int offset, int a, int b, int c, float expectedSign)
    {
        var sign = SignedTriangleArea(vertices[a], vertices[b], vertices[c]);
        if (!IsWindingCompatible(expectedSign, sign))
        {
            triangles[offset] = a;
            triangles[offset + 1] = c;
            triangles[offset + 2] = b;
            return;
        }

        triangles[offset] = a;
        triangles[offset + 1] = b;
        triangles[offset + 2] = c;
    }

    private struct TriangleEdgeInfo
    {
        public int a;
        public int b;
        public int firstTriangle;
        public int firstOpposite;
        public int secondTriangle;
        public int secondOpposite;
        public int useCount;
    }

    private static float SignedTriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return ReanimatorVertexInsertionAlgorithms.SignedTriangleArea(a, b, c);
    }

    private Vector2 ComputeUvForPoint(LayerInfo layer, Vector2 pointDoc)
    {
        var texture = layer != null && layer.texture != null ? layer.texture : GetPreviewAtlasTexture();
        if (layer == null || texture == null)
            return Vector2.zero;

        return ComputeTextureUvForLayerLocalPixel(
            layer,
            PsdCoordinateSpace.DocumentToLayerLocalPoint(layer, pointDoc),
            texture);
    }

    private static Vector2 ComputeTextureUvForLayerLocalPixel(
        LayerInfo layer,
        Vector2 localPixel,
        Texture2D texture)
    {
        if (layer == null || texture == null)
            return Vector2.zero;

        var documentPoint = PsdCoordinateSpace.LayerLocalToDocumentPoint(layer, localPixel);
        var uv = PsdCoordinateSpace.LayerDocumentToTextureUv(
            layer,
            documentPoint,
            new Vector2(texture.width, texture.height));
        return new Vector2(
            Mathf.Clamp01(uv.x),
            Mathf.Clamp01(uv.y));
    }

    private bool TryEnsureLayerVertexUvs(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertices.Length == 0)
            return false;
        if (AreLayerVertexUvsUsable(data))
            return true;
        if (data.layer == null)
            return false;

        var rebuilt = BuildVertexUvsFromLayer(data.layer, data.vertices);
        if (rebuilt == null || rebuilt.Length != data.vertices.Length)
            return false;

        data.vertexUvs = rebuilt;
        return AreLayerVertexUvsUsable(data);
    }

    private bool AreLayerVertexUvsUsable(LayerVertexData data)
    {
        if (data == null || data.vertices == null || data.vertexUvs == null)
            return false;
        if (data.vertexUvs.Length != data.vertices.Length)
            return false;

        var texture = data.texture != null
            ? data.texture
            : data.layer != null && data.layer.texture != null
                ? data.layer.texture
                : GetPreviewAtlasTexture();
        var padX = texture != null ? Mathf.Max(0.002f, 2f / Mathf.Max(1f, texture.width)) : 0.002f;
        var padY = texture != null ? Mathf.Max(0.002f, 2f / Mathf.Max(1f, texture.height)) : 0.002f;

        for (var i = 0; i < data.vertexUvs.Length; i++)
        {
            var uv = data.vertexUvs[i];
            if (!IsFinite(uv.x) || !IsFinite(uv.y))
                return false;
            if (uv.x < -padX || uv.x > 1f + padX || uv.y < -padY || uv.y > 1f + padY)
                return false;
        }

        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void TryAddBoundaryConnections(LayerVertexData data, Vector2[] verticesBeforeInsert, Vector2 pointDoc, int newIndex)
    {
        if (data == null || verticesBeforeInsert == null || verticesBeforeInsert.Length == 0)
            return;
        if (data.boundaryEdgePairs == null || data.boundaryEdgePairs.Length < 2)
            return;

        if (!TryInsertVertexIntoBoundaryLoop(data.boundaryEdgePairs, verticesBeforeInsert, pointDoc, newIndex, out var updatedBoundaryPairs))
            return;

        data.boundaryEdgePairs = updatedBoundaryPairs;
    }

    private static bool TryInsertVertexIntoBoundaryLoop(
        int[] boundaryEdgePairs,
        Vector2[] verticesBeforeInsert,
        Vector2 pointDoc,
        int newIndex,
        out int[] updatedBoundaryPairs)
    {
        return ReanimatorVertexInsertionAlgorithms.TryInsertVertexIntoBoundaryLoop(boundaryEdgePairs, verticesBeforeInsert, pointDoc, newIndex, out updatedBoundaryPairs);
    }

    private static void AddEdgePairUnique(ref int[] edgePairs, int a, int b)
    {
        if (a < 0 || b < 0 || a == b)
            return;
        if (edgePairs != null)
        {
            for (var i = 0; i + 1 < edgePairs.Length; i += 2)
            {
                var e0 = edgePairs[i];
                var e1 = edgePairs[i + 1];
                if ((e0 == a && e1 == b) || (e0 == b && e1 == a))
                    return;
            }
        }

        var oldLength = edgePairs != null ? edgePairs.Length : 0;
        Array.Resize(ref edgePairs, oldLength + 2);
        edgePairs[oldLength] = a;
        edgePairs[oldLength + 1] = b;
    }

}
#endif
