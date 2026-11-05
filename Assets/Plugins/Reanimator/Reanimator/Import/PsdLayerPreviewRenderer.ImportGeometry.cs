#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    private static SerializedProperty ResolveSpriteList(SerializedObject importer)
    {
        return ReanimatorSpriteListResolver.ResolveSpriteList(importer);
    }

    private static List<SerializedProperty> ResolveSpriteLists(SerializedObject importer)
    {
        return ReanimatorSpriteListResolver.ResolveSpriteLists(importer);
    }

    private static List<PsdLayerDataInfo> GetPsdLayerData(string assetPath)
    {
        var result = new List<PsdLayerDataInfo>();
        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (var asset in assets)
        {
            if (asset == null) continue;
            if (asset.GetType().Name != "PSDImportData") continue;

            var so = new SerializedObject(asset);
            var arrayProp = so.FindProperty("m_PsdLayerData");
            if (arrayProp == null || !arrayProp.isArray) return result;

            for (var i = 0; i < arrayProp.arraySize; i++)
            {
                var el = arrayProp.GetArrayElementAtIndex(i);
                if (el == null) continue;

                var nameProp = FindRelativeProperty(el, "m_Name", "name");
                var idProp = FindRelativeProperty(el, "m_LayerID", "layerID", "layerId");
                var visProp = FindRelativeProperty(el, "m_IsVisible", "isVisible");
                var groupProp = FindRelativeProperty(el, "m_IsGroup", "isGroup");
                var parentProp = FindRelativeProperty(el, "m_ParentIndex", "parentIndex");
                var layerSizeProp = FindRelativeProperty(el, "m_LayerSizeOnFile", "layerSizeOnFile");

                result.Add(new PsdLayerDataInfo
                {
                    index = i,
                    name = nameProp != null ? nameProp.stringValue : $"Layer {i}",
                    layerId = idProp != null ? idProp.intValue : 0,
                    isVisible = visProp != null && visProp.boolValue,
                    isGroup = groupProp != null && groupProp.boolValue,
                    parentIndex = parentProp != null ? parentProp.intValue : -1,
                    layerSizeOnFile = ReadVector2IntProperty(layerSizeProp)
                });
            }

            return result;
        }

        return result;
    }

    private static Vector2Int ReadVector2IntProperty(SerializedProperty property)
    {
        if (property == null)
            return default;

        if (property.propertyType == SerializedPropertyType.Vector2Int)
            return property.vector2IntValue;
        if (property.propertyType == SerializedPropertyType.Vector2)
        {
            var value = property.vector2Value;
            return new Vector2Int(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
        }

        var x = property.FindPropertyRelative("x");
        var y = property.FindPropertyRelative("y");
        if (x == null || y == null)
            return default;

        return new Vector2Int(
            Mathf.RoundToInt(ReadSerializedNumber(x)),
            Mathf.RoundToInt(ReadSerializedNumber(y)));
    }

    private static float ReadSerializedNumber(SerializedProperty property)
    {
        if (property == null)
            return 0f;

        if (property.propertyType == SerializedPropertyType.Integer)
            return property.intValue;
        if (property.propertyType == SerializedPropertyType.Float)
            return property.floatValue;

        return 0f;
    }

    private static SerializedProperty FindRelativeProperty(SerializedProperty parent, params string[] names)
    {
        if (parent == null || names == null)
            return null;

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name))
                continue;

            var property = parent.FindPropertyRelative(name);
            if (property != null)
                return property;
        }

        return null;
    }

    private static string BuildKey(string name, int layerId)
    {
        return layerId != 0 ? layerId.ToString() : name ?? string.Empty;
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return string.Empty;
        return assetPath.Replace('\\', '/');
    }

    private static bool IsAssetPathMatch(string lhs, string rhs)
    {
        return string.Equals(NormalizeAssetPath(lhs), NormalizeAssetPath(rhs), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetPivotPointDoc(PivotPreset preset, Vector2 customNormalized, out Vector2 pivotPoint)
    {
        pivotPoint = Vector2.zero;
        if (preset == PivotPreset.RootBonePosition)
            return TryGetRootBonePosition(out pivotPoint);

        var bounds = GetContentBoundsDoc();
        var normalized = preset == PivotPreset.Custom
            ? customNormalized
            : GetPresetNormalizedPosition(preset);
        normalized = new Vector2(Mathf.Clamp(normalized.x, -10f, 10f), Mathf.Clamp(normalized.y, -10f, 10f));
        pivotPoint = new Vector2(
            bounds.xMin + bounds.width * normalized.x,
            bounds.yMin + bounds.height * normalized.y);
        return true;
    }

    private Rect GetContentBoundsDoc()
    {
        var docW = Mathf.Max(1f, cachedDocumentSize.x > 0 ? cachedDocumentSize.x : (cachedTexture != null ? cachedTexture.width : 1f));
        var docH = Mathf.Max(1f, cachedDocumentSize.y > 0 ? cachedDocumentSize.y : (cachedTexture != null ? cachedTexture.height : 1f));

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            min = Vector2.Min(min, layer.position);
            max = Vector2.Max(max, layer.position + new Vector2(layer.sourceRect.width, layer.sourceRect.height));
        }

        if (min.x == float.MaxValue)
        {
            min = Vector2.zero;
            max = new Vector2(docW, docH);
        }

        min = Vector2.Min(min, Vector2.zero);
        max = Vector2.Max(max, new Vector2(docW, docH));
        var size = max - min;
        if (size.x <= 0f || size.y <= 0f)
            size = new Vector2(docW, docH);

        return new Rect(min, size);
    }

    private static string BuildSelectionKey(LayerInfo layer)
    {
        if (layer == null)
            return string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3:F3}|{4:F3}|{5:F3}|{6:F3}|{7:F3}|{8:F3}",
            layer.layerId,
            layer.name ?? string.Empty,
            layer.spriteGuid ?? string.Empty,
            layer.position.x,
            layer.position.y,
            layer.sourceRect.x,
            layer.sourceRect.y,
            layer.sourceRect.width,
            layer.sourceRect.height);
    }

    private static Vector2Int GetDocumentSize(string assetPath)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (var asset in assets)
        {
            if (asset == null) continue;
            if (asset.GetType().Name != "PSDImportData") continue;

            var so = new SerializedObject(asset);
            var docProp = so.FindProperty("m_DocumentSize");
            if (docProp != null)
                return docProp.vector2IntValue;
        }

        return Vector2Int.zero;
    }

    private static List<Sprite> LoadSpriteCandidates(string assetPath)
    {
        var result = new List<Sprite>();
        if (string.IsNullOrWhiteSpace(assetPath))
            return result;

        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        if (assets == null)
            return result;

        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite sprite)
                result.Add(sprite);
        }

        return result;
    }

    private static Texture2D ResolveAtlasTexture(IReadOnlyList<Sprite> sprites, Texture2D fallback)
    {
        if (sprites != null)
        {
            for (var i = 0; i < sprites.Count; i++)
            {
                var texture = sprites[i] != null ? sprites[i].texture : null;
                if (texture != null)
                    return texture;
            }
        }

        return fallback;
    }

    private Texture2D GetPreviewAtlasTexture()
    {
        return cachedAtlasTexture != null ? cachedAtlasTexture : cachedTexture;
    }

    private Vector2Int GetPreviewAtlasSize()
    {
        var texture = GetPreviewAtlasTexture();
        var max = texture != null
            ? new Vector2(Mathf.Max(1, texture.width), Mathf.Max(1, texture.height))
            : Vector2.one;

        if (layerInfos != null)
        {
            for (var i = 0; i < layerInfos.Count; i++)
            {
                var layer = layerInfos[i];
                if (layer == null)
                    continue;

                if (layer.texture != null)
                    max = Vector2.Max(max, new Vector2(layer.texture.width, layer.texture.height));
                if (layer.textureRect.width > 0f && layer.textureRect.height > 0f)
                    max = Vector2.Max(max, new Vector2(layer.textureRect.xMax, layer.textureRect.yMax));
                if (layer.sourceRect.width > 0f && layer.sourceRect.height > 0f)
                    max = Vector2.Max(max, new Vector2(layer.sourceRect.xMax, layer.sourceRect.yMax));
            }
        }

        return new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(max.x)),
            Mathf.Max(1, Mathf.CeilToInt(max.y)));
    }

    private void BuildDrawOrder()
    {
        sortedLayers.Clear();
        if (layerInfos.Count == 0)
            return;

        sortedLayers.AddRange(layerInfos);
        sortedLayers.Sort((a, b) =>
        {
            var cmp = b.depth.CompareTo(a.depth);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.name, b.name);
        });
    }

    private void BuildVertices(string assetPath)
    {
        foreach (var _ in BuildVerticesIncremental(assetPath, true))
        {
        }
    }

    private void BuildPreviewLayersWithoutSpriteMeshes()
    {
        vertexDataByLayerSelectionKey.Clear();
        defaultGeometryBySpriteKey.Clear();
        defaultGeometryAssetPath = string.Empty;
        debugLayerVertexBoundsDocByLayerKey.Clear();
        debugLayerVertexScoreByLayerKey.Clear();
        debugLayerGeometryByLayerKey.Clear();
        if (layerInfos == null || layerInfos.Count == 0)
            return;

        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            if (layer == null)
                continue;

            layer.texture = GetPreviewAtlasTexture();
            if (layer.textureRect.width <= 0f || layer.textureRect.height <= 0f)
            {
                layer.textureRect = layer.sourceRect;
            }
            if (layer.spriteSheetOptimizeReferenceRect.width <= 0f || layer.spriteSheetOptimizeReferenceRect.height <= 0f)
            {
                layer.spriteSheetOptimizeReferenceRect = layer.sourceRect;
                layer.spriteSheetOptimizeReferencePosition = layer.position;
            }
        }
    }

    private IEnumerable BuildVerticesIncremental(string assetPath, bool useEditorDataProvider)
    {
        vertexDataByLayerSelectionKey.Clear();
        debugLayerVertexBoundsDocByLayerKey.Clear();
        debugLayerVertexScoreByLayerKey.Clear();
        debugLayerGeometryByLayerKey.Clear();
        if (string.IsNullOrWhiteSpace(assetPath) || layerInfos.Count == 0)
            yield break;

        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (!IsAssetPathMatch(defaultGeometryAssetPath, normalizedAssetPath))
        {
            defaultGeometryBySpriteKey.Clear();
            defaultGeometryAssetPath = normalizedAssetPath;
        }

        SetPreviewCacheRefreshProgress(0.82f, "Preparing sprite geometry...");
        var spriteCandidates = LoadSpriteCandidates(assetPath);
        cachedAtlasTexture = ResolveAtlasTexture(spriteCandidates, cachedTexture);

        yield return null;

        SetPreviewCacheRefreshProgress(0.84f, useEditorDataProvider ? "Reading sprite bindings..." : "Reading serialized geometry...");
        var spriteIndexByGuid = ReanimatorLayerSpriteBindingResolver.BuildSpriteIndexByGuid(spriteCandidates);

        Dictionary<string, LayerSpriteBinding> layerBindings = null;
        ISpriteMeshDataProvider meshProvider = null;
        ISpriteBoneDataProvider boneProvider = null;
        var importer = AssetImporter.GetAtPath(assetPath) as PSDImporter;
        var dataProvider = useEditorDataProvider ? importer as ISpriteEditorDataProvider : null;
        Dictionary<string, SerializedSpriteMeshData> serializedMeshBySelectionKey = null;
        if (dataProvider != null)
        {
            try
            {
                dataProvider.InitSpriteEditorDataProvider();
                layerBindings = BuildLayerSpriteBindings(dataProvider);
                meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
                boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
            }
            catch
            {
                layerBindings = null;
                meshProvider = null;
                boneProvider = null;
            }
        }

        yield return null;

        if ((meshProvider == null || layerBindings == null || layerBindings.Count == 0) && importer != null)
        {
            SetPreviewCacheRefreshProgress(0.85f, "Reading serialized geometry fallback...");
            serializedMeshBySelectionKey = BuildSerializedSpriteMeshDataBySelectionKey(importer);
            yield return null;
        }

        var used = new bool[spriteCandidates.Count];
        for (var i = 0; i < layerInfos.Count; i++)
        {
            var layer = layerInfos[i];
            var selectionKey = BuildSelectionKey(layer);
            if (string.IsNullOrEmpty(selectionKey))
                continue;
            var stableKey = BuildKey(layer.name, layer.layerId);

            var spriteIndex = ReanimatorLayerSpriteBindingResolver.ResolveSpriteIndex(spriteCandidates, used, spriteIndexByGuid, layer);
            Sprite sprite = null;
            if (spriteIndex >= 0)
            {
                if (string.IsNullOrEmpty(layer.spriteGuid))
                    used[spriteIndex] = true;
                sprite = spriteCandidates[spriteIndex];
                layer.textureRect = PsdCoordinateSpace.ResolveLayerTextureRect(
                    layer.sourceRect,
                    layer.textureRect,
                    sprite.textureRect,
                    sprite.texture != null ? new Vector2(sprite.texture.width, sprite.texture.height) : Vector2.zero);
                layer.textureSamplingRect = PsdCoordinateSpace.ResolveCurrentTextureSamplingRect(
                    layer.textureRect,
                    sprite.rect,
                    sprite.texture != null ? new Vector2(sprite.texture.width, sprite.texture.height) : Vector2.zero);
                layer.spriteSheetOptimizeReferenceRect = layer.sourceRect;
                layer.spriteSheetOptimizeReferencePosition = layer.position;
            }
            var layerTexture = sprite != null && sprite.texture != null ? sprite.texture : cachedTexture;
            layer.texture = layerTexture;

            Vector2[] localPixels = null;
            Vector2[] worldVertices = null;
            Vector2[] vertexUvs = null;
            int[] triangles = Array.Empty<int>();
            int[] allEdgePairs = Array.Empty<int>();
            int[] boundaryEdgePairs = Array.Empty<int>();
            BoneWeight[] boneWeights = null;
            int[] spriteBoneToRendererBone = null;
            Matrix4x4[] bindBoneInverseMatrices = null;
            var usesSpriteBindPoses = false;
            var ppu = 1f;
            var pivot = Vector2.zero;
            var rawInputPoints = Array.Empty<Vector2>();
            var rawSpace = "none";
            var sourceKind = "none";
            var uvSource = "none";
            var importerModeForWrite = string.Empty;
            var layerSourceRect = new Rect(layer.sourceRect.x, layer.sourceRect.y, layer.sourceRect.width, layer.sourceRect.height);
            var textureSize = layerTexture != null
                ? new Vector2(layerTexture.width, layerTexture.height)
                : Vector2.zero;
            var spriteRect = Rect.zero;
            var spriteTextureRect = Rect.zero;
            if (sprite != null)
            {
                spriteRect = sprite.rect;
                spriteTextureRect = sprite.textureRect;
            }

            LayerSpriteBinding binding = default;
            var hasBinding = layerBindings != null && layerBindings.TryGetValue(selectionKey, out binding);
            var spriteBones = hasBinding ? TryGetSpriteBones(boneProvider, binding) : null;
            if (sprite == null && hasBinding &&
                (layer.textureRect.width <= 0f || layer.textureRect.height <= 0f))
            {
                layer.textureRect = binding.rect;
            }
            if (sprite == null && hasBinding &&
                (layer.textureSamplingRect.width <= 0f || layer.textureSamplingRect.height <= 0f))
            {
                layer.textureSamplingRect = binding.rect;
            }
            if (hasBinding && (layer.spriteSheetOptimizeReferenceRect.width <= 0f || layer.spriteSheetOptimizeReferenceRect.height <= 0f))
            {
                layer.spriteSheetOptimizeReferenceRect = layer.sourceRect;
                layer.spriteSheetOptimizeReferencePosition = layer.position;
            }
            var builtFromImporter = false;
            if (meshProvider != null &&
                hasBinding &&
                TryBuildWorldVerticesFromImporterMesh(
                    layer,
                    binding,
                    meshProvider,
                    layerTexture,
                    out localPixels,
                    out var uvLocalPixels,
                    out worldVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs,
                    out boneWeights,
                    out var importerMode))
            {
                ppu = Mathf.Max(1e-4f, binding.pixelsPerUnit);
                pivot = binding.pivotPixels;
                vertexUvs = BuildVertexUvsFromLocalPixels(layer, uvLocalPixels, layerTexture);
                builtFromImporter = true;
                importerModeForWrite = importerMode;
                sourceKind = "importer_vertices_auto";
                uvSource = "importer_local_pixels";
                rawSpace = $"importer_raw[{importerMode}]";
                rawInputPoints = ExtractImporterRawPositions(meshProvider.GetVertices(binding.spriteId));
            }

            if (!builtFromImporter &&
                serializedMeshBySelectionKey != null &&
                serializedMeshBySelectionKey.TryGetValue(selectionKey, out var serializedMesh) &&
                ShouldPreferSerializedMesh(serializedMesh, worldVertices, triangles) &&
                TryBuildWorldVerticesFromSerializedMesh(
                    layer,
                    binding,
                    serializedMesh,
                    layerTexture,
                    out localPixels,
                    out uvLocalPixels,
                    out worldVertices,
                    out triangles,
                    out allEdgePairs,
                    out boundaryEdgePairs,
                    out boneWeights,
                    out importerMode))
            {
                ppu = hasBinding ? Mathf.Max(1e-4f, binding.pixelsPerUnit) : ppu;
                pivot = hasBinding ? binding.pivotPixels : pivot;
                vertexUvs = BuildVertexUvsFromLocalPixels(layer, uvLocalPixels, layerTexture);
                builtFromImporter = true;
                importerModeForWrite = importerMode;
                sourceKind = "serialized_importer_vertices";
                uvSource = "serialized_importer_local_pixels";
                rawSpace = $"serialized_raw[{importerMode}]";
                rawInputPoints = ExtractImporterRawPositions(serializedMesh.vertices);
            }

            if (!builtFromImporter)
            {
                if (sprite == null)
                    continue;

                var verts = sprite.vertices;
                if (verts == null || verts.Length == 0)
                    continue;

                ppu = Mathf.Max(1e-4f, sprite.pixelsPerUnit);
                pivot = sprite.pivot;
                var spriteUvs = sprite.uv;
                var uvMode = "none";
                var builtFromUvs = spriteUvs != null &&
                                   spriteUvs.Length == verts.Length &&
                                   TryBuildWorldVerticesFromSpriteUvs(layer, sprite, spriteUvs, layerTexture, out localPixels, out worldVertices, out uvMode);
                if (!builtFromUvs)
                    BuildBestWorldVertices(layer, verts, ppu, pivot, out localPixels, out worldVertices);
                sourceKind = builtFromUvs ? "sprite_uv" : "sprite_vertices";
                rawSpace = builtFromUvs ? $"uv_0_to_1[{uvMode}]" : "sprite_units";
                if (builtFromUvs && spriteUvs != null)
                {
                    rawInputPoints = new Vector2[spriteUvs.Length];
                    Array.Copy(spriteUvs, rawInputPoints, spriteUvs.Length);
                }
                else
                {
                    rawInputPoints = verts != null ? new Vector2[verts.Length] : Array.Empty<Vector2>();
                    if (verts != null)
                    {
                        for (var vi = 0; vi < verts.Length; vi++)
                            rawInputPoints[vi] = verts[vi];
                    }
                }

                if (spriteUvs != null && spriteUvs.Length == verts.Length)
                {
                    vertexUvs = new Vector2[verts.Length];
                    Array.Copy(spriteUvs, vertexUvs, verts.Length);
                    uvSource = "sprite_uv";
                }
                else
                {
                    vertexUvs = BuildVertexUvsFromLocalPixels(layer, localPixels, layerTexture);
                    uvSource = "sprite_local_pixels";
                }

                triangles = ConvertTriangles(sprite.triangles);
                BuildEdgePairs(sprite.triangles, verts.Length, out allEdgePairs, out boundaryEdgePairs);
            }

            if (worldVertices == null || worldVertices.Length == 0)
                continue;

            if (vertexUvs == null || vertexUvs.Length != worldVertices.Length)
            {
                if (builtFromImporter)
                {
                    vertexUvs = BuildVertexUvsFromLocalPixels(layer, localPixels, layerTexture);
                    uvSource = string.Equals(uvSource, "none", StringComparison.Ordinal) ? "importer_local_pixels_rebuilt" : uvSource + "_rebuilt";
                }
                else if (TryCopySpriteUvs(sprite, worldVertices.Length, out var spriteUvCopy))
                {
                    vertexUvs = spriteUvCopy;
                    uvSource = "sprite_uv_rebuilt";
                }
                else
                {
                    vertexUvs = BuildVertexUvsFromLocalPixels(layer, localPixels, layerTexture);
                    uvSource = "local_pixels_rebuilt";
                }
            }

            var hasSkinningData = false;
            if (ShouldPreferImporterPreviewSkinning(builtFromImporter, boneWeights, worldVertices.Length) &&
                TryBuildImporterSkinningData(
                    boneWeights,
                    spriteBones,
                    out spriteBoneToRendererBone,
                    out bindBoneInverseMatrices))
            {
                usesSpriteBindPoses = false;
                hasSkinningData = true;
            }

            if (!hasSkinningData && sprite != null)
            {
                TryBuildSkinningData(sprite, layer, worldVertices.Length, out var spriteWeights, out var spriteMap, out var spriteBind, out var spriteUsesBindPoses);
                if (spriteWeights != null &&
                    spriteWeights.Length == worldVertices.Length &&
                    spriteMap != null &&
                    spriteBind != null &&
                    spriteMap.Length == spriteBind.Length &&
                    spriteBind.Length > 0 &&
                    HasMappedRendererBone(spriteMap))
                {
                    boneWeights = spriteWeights;
                    spriteBoneToRendererBone = spriteMap;
                    bindBoneInverseMatrices = spriteBind;
                    usesSpriteBindPoses = spriteUsesBindPoses;
                    hasSkinningData = true;
                }
            }

            if (!hasSkinningData &&
                boneWeights != null &&
                boneWeights.Length == worldVertices.Length &&
                TryBuildImporterSkinningData(
                    boneWeights,
                    spriteBones,
                    out spriteBoneToRendererBone,
                    out bindBoneInverseMatrices))
            {
                usesSpriteBindPoses = false;
                hasSkinningData = true;
            }

            var layerData = new LayerVertexData
            {
                layer = layer,
                texture = layerTexture,
                pixelsPerUnit = ppu,
                pivotPixels = pivot,
                vertices = worldVertices,
                vertexUvs = vertexUvs,
                importerMode = builtFromImporter ? importerModeForWrite : null,
                triangles = triangles,
                allEdgePairs = allEdgePairs,
                boundaryEdgePairs = boundaryEdgePairs,
                boneWeights = boneWeights,
                spriteBoneToRendererBone = spriteBoneToRendererBone,
                bindBoneInverseMatrices = bindBoneInverseMatrices,
                usesSpriteBindPoses = usesSpriteBindPoses,
                hasSkinning = boneWeights != null
                              && spriteBoneToRendererBone != null
                              && bindBoneInverseMatrices != null
                              && boneWeights.Length == worldVertices.Length
                              && bindBoneInverseMatrices.Length == spriteBoneToRendererBone.Length
                              && bindBoneInverseMatrices.Length > 0,
                lastPoseVersion = -1
            };
            vertexDataByLayerSelectionKey[selectionKey] = layerData;
            StoreDefaultGeometrySnapshot(layer, layerData);

            var layerRect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
            if (TryGetBounds(worldVertices, out var vertexBounds))
                debugLayerVertexBoundsDocByLayerKey[selectionKey] = vertexBounds;
            debugLayerVertexScoreByLayerKey[selectionKey] = ScoreVerticesAgainstLayer(worldVertices, layerRect);
            debugLayerGeometryByLayerKey[selectionKey] = BuildLayerGeometryDebugInfo(
                sourceKind,
                rawSpace,
                rawInputPoints,
                localPixels,
                worldVertices,
                ppu,
                pivot,
                layer.position,
                layerSourceRect,
                textureSize,
                spriteRect,
                spriteTextureRect,
                builtFromImporter,
                uvSource,
                vertexUvs);

            if ((i & 3) == 3)
            {
                SetPreviewCacheRefreshProgress(
                    Mathf.Lerp(0.84f, 0.94f, (i + 1f) / Mathf.Max(1, layerInfos.Count)),
                    $"Building geometry {i + 1}/{layerInfos.Count}...");
                yield return null;
            }
        }

        SetPreviewCacheRefreshProgress(0.95f, "Checking geometry diagnostics...");
        yield return null;

        if (IsDebugModeEnabled && debugLayerVertexScoreByLayerKey.Count > 0)
        {
            var worstKey = string.Empty;
            var worstScore = float.MinValue;
            foreach (var kv in debugLayerVertexScoreByLayerKey)
            {
                if (kv.Value <= worstScore)
                    continue;
                worstScore = kv.Value;
                worstKey = kv.Key;
            }

            var worstBounds = debugLayerVertexBoundsDocByLayerKey.TryGetValue(worstKey, out var bounds)
                ? RectToDebugString(bounds)
                : "<none>";
            ReanimatorLog.Trace(
                $"[PSD DEBUG RED] VertexImport layers={debugLayerVertexScoreByLayerKey.Count} " +
                $"worstLayer={worstKey} worstScore={worstScore:F3} bounds={worstBounds}",
                IsDebugModeEnabled);
        }

        SetPreviewCacheRefreshProgress(0.96f, "Applying preview geometry...");
        yield return null;
        ApplyPendingAutoGeometryToPreview(assetPath);
        yield return null;
        ApplyStoredAppliedVerticesToGeometry(assetPath);
    }

    private static void BuildBestWorldVertices(
        LayerInfo layer,
        Vector2[] verts,
        float ppu,
        Vector2 pivot,
        out Vector2[] localPixels,
        out Vector2[] worldVertices)
    {
        var transform = new GeometryPreviewTransform(layer);
        transform.FromSpriteVertices(verts, ppu, pivot, out localPixels, out worldVertices);
    }

    private bool TryBuildWorldVerticesFromSpriteUvs(
        LayerInfo layer,
        Sprite sprite,
        IReadOnlyList<Vector2> spriteUvs,
        Texture2D texture,
        out Vector2[] localPixels,
        out Vector2[] worldVertices,
        out string uvMode)
    {
        localPixels = Array.Empty<Vector2>();
        worldVertices = Array.Empty<Vector2>();
        uvMode = "none";
        if (layer == null || sprite == null || spriteUvs == null || spriteUvs.Count == 0 || texture == null)
            return false;

        var transform = new GeometryPreviewTransform(layer);
        var targetRect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
        var bestScore = float.MaxValue;
        Vector2[] bestLocal = null;
        Vector2[] bestWorld = null;
        var bestMode = "none";
        var texRect = sprite.textureRect;
        var sourceRect = layer.sourceRect;

        void EvaluateCandidate(string mode, Vector2 offsetPixels, bool flipY, float flipHeight)
        {
            transform.FromSpriteUvs(
                spriteUvs,
                texture.width,
                texture.height,
                offsetPixels,
                flipY,
                flipHeight,
                out var candidateLocal,
                out var candidateWorld);
            if (candidateWorld == null || candidateWorld.Length == 0)
                return;

            var score = ScoreVerticesAgainstLayer(candidateWorld, targetRect);
            if (score >= bestScore)
                return;

            bestScore = score;
            bestLocal = candidateLocal;
            bestWorld = candidateWorld;
            bestMode = mode;
        }

        EvaluateCandidate("tex_rect", new Vector2(texRect.x, texRect.y), false, texRect.height);
        EvaluateCandidate("source_rect", new Vector2(sourceRect.x, sourceRect.y), false, sourceRect.height);
        EvaluateCandidate("tex_rect_flip_y", new Vector2(texRect.x, texRect.y), true, texRect.height);
        EvaluateCandidate("source_rect_flip_y", new Vector2(sourceRect.x, sourceRect.y), true, sourceRect.height);

        if (bestWorld != null && bestWorld.Length > 0)
        {
            localPixels = bestLocal;
            worldVertices = bestWorld;
            uvMode = bestMode;
        }

        return worldVertices != null && worldVertices.Length > 0;
    }

    private static LayerGeometryDebugInfo BuildLayerGeometryDebugInfo(
        string sourceKind,
        string rawSpace,
        IReadOnlyList<Vector2> rawPoints,
        IReadOnlyList<Vector2> localPixels,
        IReadOnlyList<Vector2> worldVertices,
        float pixelsPerUnit,
        Vector2 pivotPixels,
        Vector2 layerPosition,
        Rect layerSourceRect,
        Vector2 textureSize,
        Rect spriteRect,
        Rect spriteTextureRect,
        bool usedImporterMesh,
        string uvSource,
        IReadOnlyList<Vector2> vertexUvs)
    {
        var info = new LayerGeometryDebugInfo
        {
            sourceKind = string.IsNullOrEmpty(sourceKind) ? "unknown" : sourceKind,
            rawSpace = string.IsNullOrEmpty(rawSpace) ? "unknown" : rawSpace,
            pixelsPerUnit = pixelsPerUnit,
            pivotPixels = pivotPixels,
            layerPosition = layerPosition,
            layerSourceRect = layerSourceRect,
            textureSize = textureSize,
            spriteRect = spriteRect,
            spriteTextureRect = spriteTextureRect,
            usedImporterMesh = usedImporterMesh,
            uvSource = string.IsNullOrEmpty(uvSource) ? "unknown" : uvSource,
            rawBounds = Rect.zero,
            localBounds = Rect.zero,
            worldBounds = Rect.zero,
            uvBounds = Rect.zero,
            rawSample = Vector2.zero,
            localSample = Vector2.zero,
            worldSample = Vector2.zero,
            uvSample = Vector2.zero
        };

        if (rawPoints != null && rawPoints.Count > 0)
        {
            info.rawSample = rawPoints[0];
            if (TryGetBounds(rawPoints, out var rawBounds))
                info.rawBounds = rawBounds;
        }

        if (localPixels != null && localPixels.Count > 0)
        {
            info.localSample = localPixels[0];
            if (TryGetBounds(localPixels, out var localBounds))
                info.localBounds = localBounds;
        }

        if (worldVertices != null && worldVertices.Count > 0)
        {
            info.worldSample = worldVertices[0];
            if (TryGetBounds(worldVertices, out var worldBounds))
                info.worldBounds = worldBounds;
        }

        if (vertexUvs != null && vertexUvs.Count > 0)
        {
            info.uvSample = vertexUvs[0];
            if (TryGetBounds(vertexUvs, out var uvBounds))
                info.uvBounds = uvBounds;
        }

        return info;
    }

    private static int FindMatchingSprite(IReadOnlyList<Sprite> sprites, IReadOnlyList<bool> used, LayerInfo layer)
    {
        return ReanimatorLayerSpriteBindingResolver.FindMatchingSprite(sprites, used, layer);
    }

    private static Dictionary<string, int> BuildSpriteIndexByGuid(IReadOnlyList<Sprite> sprites)
    {
        return ReanimatorLayerSpriteBindingResolver.BuildSpriteIndexByGuid(sprites);
    }

    private static string TryGetSpriteStableGuid(Sprite sprite)
    {
        return ReanimatorLayerSpriteBindingResolver.TryGetSpriteStableGuid(sprite);
    }

    private static int FindMatchingSpriteRect(IReadOnlyList<SpriteRect> spriteRects, IReadOnlyList<bool> used, LayerInfo layer)
    {
        return ReanimatorLayerSpriteBindingResolver.FindMatchingSpriteRect(spriteRects, used, layer);
    }

    private Dictionary<string, LayerSpriteBinding> BuildLayerSpriteBindings(ISpriteEditorDataProvider dataProvider)
    {
        return ReanimatorLayerSpriteBindingResolver.BuildBindings(dataProvider, layerInfos, BuildSelectionKey);
    }

    private static int[] ConvertTriangles(ushort[] source)
    {
        if (source == null || source.Length < 3)
            return Array.Empty<int>();

        var result = new int[source.Length];
        for (var i = 0; i < source.Length; i++)
            result[i] = source[i];
        return result;
    }
}
#endif
