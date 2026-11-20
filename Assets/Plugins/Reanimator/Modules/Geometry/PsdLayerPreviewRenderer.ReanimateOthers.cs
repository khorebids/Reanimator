#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

internal enum ReanimateOtherMode { Skip, SC, SR, SRN, Projector }

public partial class PsdLayerPreviewRenderer
{
    internal bool TryInheritReanimateChest(LayerInfo target, LayerInfo body,
        IReadOnlyList<Vector2> left, IReadOnlyList<Vector2> right, out string error, int onlySide = -1)
    {
        error = null;
        if (body == null || target == null || !TryGetLayerVertexData(target, out var data))
        {
            error = "Body and clothing geometry are required for chest inheritance.";
            return false;
        }
        // Clothing inherits the existing body deformation, never its silhouette.
        // Projector inserts support points only inside the target's triangles.
        var contours = new[] { left, right };
        for (int side=0;side<contours.Length;side++)
        {
            if(onlySide>=0 && side!=onlySide)continue;
            var contour=contours[side];
            if (contour == null || contour.Count < 3)
            {
                error = "Both chest contours are required.";
                return false;
            }
            var region = MapFullBoobContourToBase(target, contour);
            bool overlap = false;
            foreach (var point in data.vertices)
                if (ContainsFaceMouthPoint(point, region)) { overlap = true; break; }
            if (!overlap)
                foreach (var point in region)
                    if (TryFindContainingTriangle(data.triangles, data.vertices, point,
                            out _, out _, out _, out _, out _, out _, out _, out _)) { overlap = true; break; }
            if (!overlap)
                for (int i = 0; i < region.Length && !overlap; i++)
                    for (int e = 0; e + 1 < data.allEdgePairs.Length; e += 2)
                        if (TryGetSegmentIntersection(region[i], region[(i + 1) % region.Length],
                                data.vertices[data.allEdgePairs[e]], data.vertices[data.allEdgePairs[e + 1]], out _, out _))
                        { overlap = true; break; }
            if (overlap && !TryApplyReanimateProjectorRegion(target, new[] { body }, true, contour, out error))
                return false;
        }
        return true;
    }

    internal void SoftenReanimateOtherGaps(LayerInfo layer, IReadOnlyList<Vector2[]> contours,IReadOnlyList<Vector2[]> protectedContours=null)
    {
        if (contours.Count == 0 || !TryGetLayerVertexData(layer, out var data))
            return;
        var regions = new List<Vector2[]>();
        foreach (var contour in contours)
            regions.Add(MapFullBoobContourToBase(layer, contour));
        EnsureBoneWeightsArraySize(data, data.vertices.Length);
        var excluded=new bool[data.vertices.Length];
        if(protectedContours!=null)foreach(var contour in protectedContours)
        {
            var polygon=MapFullBoobContourToBase(layer,contour);
            for(int i=0;i<excluded.Length;i++)if(ContainsFaceMouthPoint(data.vertices[i],polygon))excluded[i]=true;
        }
        SoftenOtherGapWeights(data.vertices, data.boneWeights, regions,excluded);
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, BuildSelectionKey(layer), BuildKey(layer.name, layer.layerId));
    }

    private static void SoftenOtherGapWeights(Vector2[] vertices, BoneWeight[] boneWeights, IReadOnlyList<Vector2[]> regions,bool[] excluded=null)
    {
        var original = (BoneWeight[])boneWeights.Clone();
        var samples = new List<int>[regions.Count];
        var covered = excluded!=null?(bool[])excluded.Clone():new bool[vertices.Length];
        for (var r = 0; r < regions.Count; r++)
        {
            samples[r] = new List<int>();
            for (var i = 0; i < vertices.Length; i++)
                if ((excluded==null || !excluded[i]) && ContainsFaceMouthPoint(vertices[i], regions[r]))
                {
                    covered[i] = true;
                    samples[r].Add(i);
                }
        }
        for (var i = 0; i < vertices.Length; i++)
        {
            if (covered[i])
                continue;
            var weights = new Dictionary<int, float>();
            foreach (var regionSamples in samples)
            {
                var nearest = -1;
                var distance = float.PositiveInfinity;
                foreach (var sample in regionSamples)
                {
                    var candidate = (vertices[i] - vertices[sample]).sqrMagnitude;
                    if (candidate < distance)
                    {
                        nearest = sample;
                        distance = candidate;
                    }
                }
                if (nearest < 0)
                    continue;
                var source = original[nearest];
                var influence = 1f / Mathf.Max(distance, 0.0001f);
                Add(source.boneIndex0, source.weight0 * influence);
                Add(source.boneIndex1, source.weight1 * influence);
                Add(source.boneIndex2, source.weight2 * influence);
                Add(source.boneIndex3, source.weight3 * influence);
            }
            var ranked = new List<KeyValuePair<int, float>>(weights);
            ranked.Sort((a, b) => { var order = b.Value.CompareTo(a.Value); return order != 0 ? order : a.Key.CompareTo(b.Key); });
            var count = Mathf.Min(4, ranked.Count);
            var total = 0f;
            for (var j = 0; j < count; j++)
                total += ranked[j].Value;
            if (total <= 0f)
                continue;
            var blended = new BoneWeight();
            for (var j = 0; j < count; j++)
            {
                var bone = ranked[j].Key;
                var value = ranked[j].Value / total;
                switch (j)
                {
                    case 0: blended.boneIndex0 = bone; blended.weight0 = value; break;
                    case 1: blended.boneIndex1 = bone; blended.weight1 = value; break;
                    case 2: blended.boneIndex2 = bone; blended.weight2 = value; break;
                    case 3: blended.boneIndex3 = bone; blended.weight3 = value; break;
                }
            }
            boneWeights[i] = blended;

            void Add(int bone, float weight)
            {
                if (weight > 0f)
                    weights[bone] = weights.TryGetValue(bone, out var previous) ? previous + weight : weight;
            }
        }
    }

    internal bool TryPrepareReanimateProjectorGeometry(LayerInfo layer, out string error)
    {
        var previousSelection = selectedLayerSelectionKey;
        selectedLayerSelectionKey = BuildSelectionKey(layer);
        try
        {
            // Keep the existing skinning as the baseline outside the projected regions.
            return GenerateAutoGeometryForSelected(cachedPath, 90, 250, 90, false,
                ReanimatorAutoGeometryMode.Default, ReanimatorReanimaxGeometryMode.Hard,
                5d, 0f, 0.5f, 3, 13f, 4, 2.8f, 30, 5f, 5, out error);
        }
        finally
        {
            selectedLayerSelectionKey = previousSelection;
        }
    }

    internal bool TryApplyReanimateProjectorRegion(LayerInfo layer, IReadOnlyList<LayerInfo> sourceLayers,
        bool stretch, IReadOnlyList<Vector2> contour, out string error)
    {
        if (!TryPrepareOtherRegion(layer, contour, out var data, out var staged, out var region, out _, out error, exactBoundary: true))
            return false;
        var key = BuildSelectionKey(layer);
        var stableKey = BuildKey(layer.name, layer.layerId);
        var keys = new string[sourceLayers.Count];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = BuildSelectionKey(sourceLayers[i]);
        var sources = BuildProjectorSourceList(keys, key, stableKey, true, staged);
        if (sources.Count == 0)
        {
            error = "Selected Projector sources do not intersect the sprite or have no weights.";
            return false;
        }

        if (stretch)
        {
            // Region transfer consumes candidate points, not global merged
            // triangles. Insert them into the target's existing topology.
            foreach (var point in BuildProjectorAplProjectedFallbackCandidates(staged, sources))
                if (ContainsFaceMouthPoint(point, region))
                    InsertOtherRegionPoint(staged, point, exactBoundary: true);
        }
        var weighted = CloneProjectorLayerData(staged);
        if (!TryBuildProjectorWeights(weighted, sources, false, true, true,
                out _, out var weights, out var projectedMask, out _))
        {
            error = "No source weights could be projected.";
            return false;
        }

        weighted.boneWeights = weights;
        EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
        var applied = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            if (!projectedMask[i] || !ContainsFaceMouthPoint(staged.vertices[i], region) ||
                !TryRemapProjectorWeight(weights[i], weighted, staged, out var weight, out _))
                continue;
            staged.boneWeights[i] = weight;
            applied++;
        }
        if (applied == 0)
        {
            error = "No projected weights fall inside the region.";
            return false;
        }
        RegisterUndo("Apply Projector Region");
        CommitOtherRegion(layer, data, staged);
        return true;
    }

    internal bool TryApplyOtherSprite(LayerInfo layer, ReanimateOtherMode mode, IReadOnlyList<Vector2> documentPoints,
        IReadOnlyList<Vector2> contour, int regionIndex, out string error, string parentBone = null,
        bool smoothRoot = true, string rootSourceBone = null)
    {
        if (!TryPrepareOtherRegion(layer, contour, out var data, out var staged, out var region, out var center, out error))
            return false;

        var count = mode == ReanimateOtherMode.SRN ? documentPoints.Count - 1 : 1;
        if ((mode == ReanimateOtherMode.SRN && count < 2) ||
            (mode == ReanimateOtherMode.SR && documentPoints.Count < 2))
        {
            error = "Draw the required bones first.";
            return false;
        }

        var points = new Vector2[count + 1];
        if (mode == ReanimateOtherMode.SC)
        {
            points[0] = center;
            var bottom = float.PositiveInfinity;
            var top = float.NegativeInfinity;
            foreach (var point in region)
            {
                bottom = Mathf.Min(bottom, point.y);
                top = Mathf.Max(top, point.y);
            }
            points[1] = points[0] + Vector2.down * Mathf.Max(1f, (top - bottom) * 0.15f);
        }
        else
        {
            for (var i = 0; i < points.Length; i++)
                points[i] = MapNippleGuessToBase(layer, documentPoints[i]);
        }
        for (var i = 0; i < count; i++)
        {
            if ((points[i + 1] - points[i]).sqrMagnitude < 0.0001f)
            {
                error = "The bone endpoints must be separate.";
                return false;
            }
        }

        var parent = FindReanimateAccessoryParent(points[0]);
        if(mode==ReanimateOtherMode.SC && NormalizeAutoWeightName(layer.name).Contains("hair") && TryFindBoneIndexByName("head",out var hairParent))parent=hairParent;
        if ((mode is ReanimateOtherMode.SR or ReanimateOtherMode.SRN) &&
            !string.IsNullOrEmpty(parentBone) && !TryFindBoneIndexByName(parentBone, out parent))
        {
            error = $"Parent bone '{parentBone}' is missing.";
            return false;
        }
        if (parent < 0)
        {
            error = "A skeleton is required.";
            return false;
        }

        var sourceSlot = -1;
        if(mode==ReanimateOtherMode.SC && !TryEnsureFullBoobSpriteBone(staged,parent,out sourceSlot))
        {error="Could not bind the static parent of the SC region.";return false;}
        if (mode == ReanimateOtherMode.SRN && smoothRoot)
        {
            var sourceName = string.IsNullOrEmpty(rootSourceBone) ? bones[parent].name : rootSourceBone;
            if (!TryFindBoneIndexByName(sourceName, out var sourceBone) ||
                !TryEnsureFullBoobSpriteBone(staged, sourceBone, out sourceSlot))
            {
                error = $"SRN blend source bone '{sourceName}' is missing.";
                return false;
            }
        }
        RegisterUndo("Apply Reanimate " + mode);
        var map = new int[count];
        var prefix = layer.name.Trim() + (regionIndex > 0 ? "_" + (regionIndex + 1) : "");
        for (var i = 0; i < count; i++)
        {
            var suffix = mode == ReanimateOtherMode.SRN ? "_SRN_" + (i + 1) : "_" + mode;
            if(!TryFindBoneIndexByName(prefix+suffix,out var index))index=FindOrCreateVGenBone(parent,prefix+suffix,points[i],true);
            var bone = bones[index];
            bone.parentId=parent;
            var direction = points[i + 1] - points[i];
            var rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
            bone.position = bone.initialPosition = ConvertBoneWorldPositionToLocal(parent, points[i]);
            bone.rotation = bone.initialRotation = ConvertBoneWorldRotationToLocal(parent, rotation);
            bone.length = direction.magnitude;
            RecomputeBoneWorldTransforms();
            if (!TryEnsureFullBoobSpriteBone(staged, index, out map[i]))
            {
                error = "Could not bind a region bone.";
                return false;
            }
            TryBuildBindInverseMatrixForRendererBone(staged,index,out staged.bindBoneInverseMatrices[map[i]]);
            parent = index;
        }
        EnsureBoneWeightsArraySize(staged, staged.vertices.Length);
        if(mode==ReanimateOtherMode.SC)
            for(int i=0;i<staged.boneWeights.Length;i++)
            {
                // Repeated runs can arrive with this SC on the entire imported
                // sprite. Strip only this region's previous influence; preserve
                // other authored regions, and anchor unweighted space statically.
                var old=staged.boneWeights[i];var values=new Dictionary<int,float>();
                Add(old.boneIndex0,old.weight0);Add(old.boneIndex1,old.weight1);
                Add(old.boneIndex2,old.weight2);Add(old.boneIndex3,old.weight3);
                if(values.Count==0)values[sourceSlot]=1f;
                staged.boneWeights[i]=BuildVertexFixWeight(values);
                void Add(int bone,float value){if(value>0f)AccumulateWeightChannel(values,bone==map[0]?sourceSlot:bone,value);}
            }
        for (var i = 0; i < staged.vertices.Length; i++)
        {
            if (!ContainsFaceMouthPoint(staged.vertices[i], region))
                continue;
            var weight = count == 1 ? BuildSingleBoneWeight(0) : EvaluateOtherChainWeight(staged.vertices[i], points);
            weight.boneIndex0 = map[weight.boneIndex0];
            weight.boneIndex1 = map[weight.boneIndex1];
            if (mode==ReanimateOtherMode.SRN && sourceSlot >= 0)
                weight = SmoothOtherChainRootWeight(staged.vertices[i], points, weight, map[0], sourceSlot);
            staged.boneWeights[i] = weight;
        }
        BuildBoneDrawOrder();
        MarkBoneStateChanged();
        CommitOtherRegion(layer, data, staged);
        return true;
    }

    private bool TryPrepareOtherRegion(LayerInfo layer, IReadOnlyList<Vector2> contour, out LayerVertexData data,
        out LayerVertexData staged, out Vector2[] region, out Vector2 center, out string error, bool exactBoundary = false)
    {
        staged = null;
        region = null;
        center = default;
        if (!TryGetLayerVertexData(layer, out data) || data.vertices == null || data.triangles == null ||
            contour == null || contour.Count < 3)
        {
            error = "Draw a closed region over a sprite with geometry.";
            return false;
        }
        region = MapFullBoobContourToBase(layer, contour);
        if (!TryGetVGenCenter(region, out center))
        {
            error = "The region has no interior.";
            return false;
        }
        staged = CloneProjectorLayerData(data);
        var edges = staged.allEdgePairs;
        if (edges == null || edges.Length == 0)
            BuildEdgePairs(staged.triangles, staged.vertices.Length, out edges, out _);
        var vertices = staged.vertices;
        for (var i = 0; i < region.Length; i++)
        {
            var start = region[i];
            var end = region[(i + 1) % region.Length];
            InsertOtherRegionPoint(staged, start, exactBoundary);
            for (var edge = 0; edge + 1 < edges.Length; edge += 2)
                if (TryGetSegmentIntersection(start, end, vertices[edges[edge]], vertices[edges[edge + 1]], out var t, out _))
                    InsertOtherRegionPoint(staged, Vector2.Lerp(start, end, t), exactBoundary);
        }
        InsertOtherRegionPoint(staged, center, exactBoundary);
        foreach (var point in staged.vertices)
            if (ContainsFaceMouthPoint(point, region))
            {
                error = null;
                return true;
            }
        error = "The region does not overlap the sprite mesh.";
        return false;
    }

    private void InsertOtherRegionPoint(LayerVertexData data, Vector2 point, bool exactBoundary = false)
    {
        foreach (var vertex in data.vertices)
            if ((vertex - point).sqrMagnitude <= 0.0001f)
                return;
        if (TryFindContainingTriangle(data.triangles, data.vertices, point,
                out _, out _, out _, out _, out _, out _, out _, out _))
            InsertAppliedVertexIntoLayerData(data, point, true, allowBoundarySnap: !exactBoundary, optimizeQuality: false);
    }

    private void CommitOtherRegion(LayerInfo layer, LayerVertexData data, LayerVertexData staged)
    {
        OptimizeLayerTriangleQuality(staged);
        data.vertices = staged.vertices;
        data.vertexUvs = staged.vertexUvs;
        data.triangles = staged.triangles;
        data.allEdgePairs = staged.allEdgePairs;
        data.boundaryEdgePairs = staged.boundaryEdgePairs;
        data.boneWeights = staged.boneWeights;
        data.spriteBoneToRendererBone = staged.spriteBoneToRendererBone;
        data.bindBoneInverseMatrices = staged.bindBoneInverseMatrices;
        data.usesSpriteBindPoses = staged.usesSpriteBindPoses;
        MarkSkinningWeightsChanged(data);
        MarkEditGeometryDataChanged(data, BuildSelectionKey(layer), BuildKey(layer.name, layer.layerId));
    }

    private int FindReanimateAccessoryParent(Vector2 point)
    {
        var best = -1;
        var distance = float.PositiveInfinity;
        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone == null || string.Equals(bone.name, "root", StringComparison.OrdinalIgnoreCase) ||
                bone.name.EndsWith("_SC", StringComparison.OrdinalIgnoreCase) ||
                ReaniIK.IsSpringRotationBoneName(bone.name))
                continue;
            var start = GetBoneBasePoint(bone);
            var direction = (Vector2)(bone.worldRotation * Vector3.right) * bone.length;
            var t = Mathf.Clamp01(Vector2.Dot(point - start, direction) / Mathf.Max(direction.sqrMagnitude, 0.0001f));
            var candidate = (point - (start + direction * t)).sqrMagnitude;
            if (candidate >= distance)
                continue;
            distance = candidate;
            best = i;
        }
        return best;
    }

    private static BoneWeight SmoothOtherChainRootWeight(Vector2 point, IReadOnlyList<Vector2> joints,
        BoneWeight weight, int firstBone, int sourceBone)
    {
        var direction = joints[1] - joints[0];
        var t = Mathf.Clamp01(Vector2.Dot(point - joints[0], direction) / Mathf.Max(direction.sqrMagnitude, .0001f));
        var anchor = 1f - t * t * (3f - 2f * t);
        var accum = new Dictionary<int, float>();
        Add(weight.boneIndex0, weight.weight0);
        Add(weight.boneIndex1, weight.weight1);
        Add(weight.boneIndex2, weight.weight2);
        Add(weight.boneIndex3, weight.weight3);
        return BuildVertexFixWeight(accum);

        void Add(int bone, float value)
        {
            if (bone == firstBone)
            {
                AccumulateWeightChannel(accum, sourceBone, value * anchor);
                value *= 1f - anchor;
            }
            AccumulateWeightChannel(accum, bone, value);
        }
    }

    private static BoneWeight EvaluateOtherChainWeight(Vector2 point, IReadOnlyList<Vector2> joints)
    {
        var best = 0;
        var bestT = 0f;
        var distance = float.PositiveInfinity;
        for (var i = 0; i < joints.Count - 1; i++)
        {
            var direction = joints[i + 1] - joints[i];
            var t = Mathf.Clamp01(Vector2.Dot(point - joints[i], direction) / direction.sqrMagnitude);
            var candidate = (point - (joints[i] + direction * t)).sqrMagnitude;
            if (candidate >= distance)
                continue;
            distance = candidate;
            best = i;
            bestT = t;
        }
        return best == joints.Count - 2
            ? BuildSingleBoneWeight(best)
            : BuildLimbTwoBoneWeight(best, 1f - bestT, best + 1, bestT);
    }
}
#endif
