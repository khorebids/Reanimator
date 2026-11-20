#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public static string VerifyCollapsedImporterRecovery()
    {
        var data=JsonUtility.FromJson<LayerVertexData>(System.IO.File.ReadAllText("Assets/Plugins/Reanimator/Tests/Fixtures/RightHandImporterMesh.json"));
        var binding=new LayerSpriteBinding {rect=new Rect(0,0,data.layer.sourceRect.width,data.layer.sourceRect.height),pixelsPerUnit=100};
        var old=new Vertex2DMetaData[data.vertices.Length];
        for(int i=0;i<old.Length;i++){old[i].position=new Vector2(i,0);old[i].boneWeight=data.boneWeights[i];}
        var output=BuildMeshMetaVertices(data,binding,old,data.triangles);
        var indices=data.triangles.ToArray();var edges=BuildImporterEdgesFromIndices(indices,output.Length);
        if(ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(output,indices,binding.rect,out _))throw new Exception("Collapsed importer fixture did not reproduce the failure.");
        RepairCollapsedImporterGeometry(data,binding,ref output,ref indices,ref edges);
        if(!ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(output,indices,binding.rect,out var error))throw new Exception(error);
        for(int i=0;i<data.vertices.Length;i++)
            if(output[i].position!=PsdCoordinateSpace.DocumentToLayerLocalPoint(data.layer,data.vertices[i]) || !output[i].boneWeight.Equals(data.boneWeights[i]))
                throw new Exception("Recovery changed authoritative geometry or weights.");
        var first=output;
        RepairCollapsedImporterGeometry(data,binding,ref output,ref indices,ref edges);
        if(!ReferenceEquals(first,output))throw new Exception("Repeated recovery rewrote a valid mesh.");
        // The recovery must not conceal a genuinely collapsed source mesh.
        data.vertices=Enumerable.Range(0,data.vertices.Length).Select(i=>new Vector2(i,0)).ToArray();
        output=old;indices=data.triangles.ToArray();
        RepairCollapsedImporterGeometry(data,binding,ref output,ref indices,ref edges);
        if(ReanimatorImporterGeometryAlgorithms.IsWritableImporterMeshSane(output,indices,binding.rect,out _))throw new Exception("Invalid source was accepted.");
        return "PASS: captured r_hand with collapsed importer repaired; geometry and weights exact; repeat is a no-op; invalid source stays rejected.";
    }
}
#endif
