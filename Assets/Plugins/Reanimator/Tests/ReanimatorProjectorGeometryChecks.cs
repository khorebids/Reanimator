#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyPaddedSnapshotDocumentCoordinates()
    {
        var layer = new LayerInfo { position = new Vector2(359, 0), sourceRect = new Rect(1036, 4, 665, 853) };
        var original = new[] { new Vector2(370, 200), new Vector2(580, 230), new Vector2(420, 520) };
        var snapshot = new DefaultGeometrySnapshot { sourceRect = layer.sourceRect, layerPosition = layer.position, verticesDoc = original };
        for (int pass=0; pass<2; pass++)
        {
            if (!TryBuildDefaultGeometryVerticesForCurrentRect(snapshot, layer, out var result) ||
                result.Where((p,i) => (p-original[i]).sqrMagnitude > .0001f).Any())
                throw new Exception("Padded atlas rect shifted document-space snapshot vertices.");
            snapshot.verticesDoc = result;
        }
        return "PASS: padded snapshot preserves document positions across repeated remapping.";
    }
    // Run against the current source garment; no PSD or prefab writes.
    public string VerifyProjectorGarmentGeometry(string layerName)
    {
        var layer = layerInfos.First(l => l.name == layerName);
        Vector2[] firstVertices = null;
        int[] firstTriangles = null;
        for (int pass = 0; pass < 2; pass++)
        {
            if (!TryPrepareReanimateProjectorGeometry(layer, out var error))
                throw new Exception("Projector garment preparation failed: " + error);
            if (!TryGetLayerVertexData(layer, out var data) || data.vertices.Length < 3 || data.triangles.Length < 3)
                throw new Exception("Projector garment has no geometry.");
            if (!ReanimatorImporterGeometryAlgorithms.HasClosedSpriteOutline(data.triangles, data.vertices.Length))
                throw new Exception("Projector garment outline is not closed.");
            if (data.vertexUvs.Length != data.vertices.Length || data.vertices.Any(p => float.IsNaN(p.x) || float.IsNaN(p.y)))
                throw new Exception("Invalid garment vertices or UVs.");
            if (pass > 0 && (!firstVertices.SequenceEqual(data.vertices) || !firstTriangles.SequenceEqual(data.triangles)))
                throw new Exception("Repeated projector preparation is not deterministic.");
            firstVertices = data.vertices.ToArray();
            firstTriangles = data.triangles.ToArray();
        }
        return "PASS: " + layerName + " projector geometry is closed, has UVs and is deterministic across two preparations.";
    }
}
#endif
