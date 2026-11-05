#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Animation;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;

public static class SpriteSkinPrefabBoneAssignerWindow
{
    internal static string AssignSelectedInOpenPrefab()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.prefabContentsRoot == null)
            return "No prefab is open.";

        var stats = AssignBonesOnSelectedSpriteRenderers(stage.prefabContentsRoot, Selection.gameObjects);
        if (stats.ChangedSpriteSkins > 0 || stats.AddedSpriteSkins > 0)
        {
            EditorSceneManager.MarkSceneDirty(stage.scene);
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
        }

        return stats.ToStatus();
    }

    internal static string RenameSelectedLinkedPsdBonePlaceholders()
    {
        if (!TryGetTargetSpriteRenderer(Selection.activeGameObject, out var spriteRenderer, out var error))
            return error;

        var stats = RenameLinkedPsdBonePlaceholders(spriteRenderer);
        return stats.ToStatus();
    }

    internal static string RecreateSelectedSkeleton()
    {
        if (!TryGetTargetSpriteRenderer(Selection.activeGameObject, out var spriteRenderer, out var error))
            return error;

        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        var hierarchyRoot = ResolveSkeletonHierarchyRoot(stage, spriteRenderer.gameObject);
        var stats = RecreateSkeleton(hierarchyRoot, spriteRenderer);
        if (stats.ChangedSpriteSkins > 0)
        {
            if (stage != null && stage.prefabContentsRoot != null && spriteRenderer.transform.IsChildOf(stage.prefabContentsRoot.transform))
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(spriteRenderer.gameObject.scene);
            }
        }

        return stats.ToStatus();
    }

    internal static MissingSpriteFillStats FillMissingSpritesFromAsset(
        string spriteAssetPath,
        IReadOnlyList<GameObject> selectedObjects,
        bool includeChildren)
    {
        var stats = new MissingSpriteFillStats { AssetPath = spriteAssetPath };
        if (string.IsNullOrWhiteSpace(spriteAssetPath))
        {
            stats.Error = "Select PSD/PSB sprite sheet in Reanimator.";
            return stats;
        }

        var sprites = AssetDatabase.LoadAllAssetsAtPath(spriteAssetPath)
            .OfType<Sprite>()
            .Where(sprite => sprite != null && !string.IsNullOrWhiteSpace(sprite.name))
            .ToArray();
        if (sprites.Length == 0)
        {
            stats.Error = "Selected asset has no sprites.";
            return stats;
        }

        return FillMissingSpritesFromSprites(spriteAssetPath, sprites, selectedObjects, includeChildren);
    }

    internal static MissingSpriteFillStats FillMissingSpritesFromSprites(
        string spriteAssetPath,
        IReadOnlyList<Sprite> sprites,
        IReadOnlyList<GameObject> selectedObjects,
        bool includeChildren)
    {
        var stats = new MissingSpriteFillStats { AssetPath = spriteAssetPath };
        if (sprites == null || sprites.Count == 0)
        {
            stats.Error = "Selected asset has no sprites.";
            return stats;
        }

        if (selectedObjects == null || selectedObjects.Count == 0)
        {
            stats.Error = "Select one or more GameObjects with SpriteRenderer.";
            return stats;
        }

        var spriteLookup = BuildSpriteNameLookup(sprites);
        var renderers = CollectSpriteRenderers(selectedObjects, includeChildren);
        if (renderers.Count == 0)
        {
            stats.Error = "Selection has no SpriteRenderer.";
            return stats;
        }

        var changedRenderers = new List<SpriteRenderer>();
        var unmatchedExamples = new List<string>();
        var ambiguousExamples = new List<string>();
        for (var i = 0; i < renderers.Count; i++)
        {
            var spriteRenderer = renderers[i];
            if (spriteRenderer == null)
                continue;

            stats.SpriteRenderers++;
            if (spriteRenderer.sprite != null)
                continue;

            stats.MissingSprites++;
            var name = spriteRenderer.gameObject != null ? spriteRenderer.gameObject.name : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                stats.UnmatchedSprites++;
                continue;
            }

            if (!TryResolveSpriteByName(spriteLookup, name, out var sprite, out var ambiguous) || sprite == null)
            {
                if (ambiguous)
                {
                    stats.AmbiguousSprites++;
                    AddExample(ambiguousExamples, name);
                }
                else
                {
                    stats.UnmatchedSprites++;
                    AddExample(unmatchedExamples, name);
                }
                continue;
            }

            Undo.RecordObject(spriteRenderer, "Fill Missing Sprite");
            spriteRenderer.sprite = sprite;
            EditorUtility.SetDirty(spriteRenderer);
            changedRenderers.Add(spriteRenderer);
            stats.FilledSprites++;
        }

        stats.UnmatchedExamples = string.Join(", ", unmatchedExamples);
        stats.AmbiguousExamples = string.Join(", ", ambiguousExamples);
        PersistChangedSpriteRenderers(changedRenderers);
        return stats;
    }

    internal static bool CanRecreateSkeletonForSelection(GameObject selectedGameObject)
    {
        return selectedGameObject != null && selectedGameObject.GetComponent<SpriteRenderer>() != null;
    }

    internal static bool TryGetTargetSpriteRenderer(
        GameObject selectedGameObject,
        out SpriteRenderer spriteRenderer,
        out string error)
    {
        spriteRenderer = null;
        error = null;

        if (selectedGameObject == null)
        {
            error = "Select a GameObject with SpriteRenderer.";
            return false;
        }

        spriteRenderer = selectedGameObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            error = "Selected GameObject has no SpriteRenderer.";
            return false;
        }

        return true;
    }

    private sealed class SpriteNameLookup
    {
        public readonly Dictionary<string, Sprite> exact = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        public readonly HashSet<string> exactAmbiguous = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, Sprite> normalized = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        public readonly HashSet<string> normalizedAmbiguous = new HashSet<string>(StringComparer.Ordinal);
    }

    private static SpriteNameLookup BuildSpriteNameLookup(IReadOnlyList<Sprite> sprites)
    {
        var lookup = new SpriteNameLookup();
        if (sprites == null)
            return lookup;

        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
                continue;

            AddSpriteLookupCandidate(lookup.exact, lookup.exactAmbiguous, sprite.name, sprite);
            AddSpriteLookupCandidate(lookup.normalized, lookup.normalizedAmbiguous, NormalizeSpriteMatchName(sprite.name), sprite);
        }

        return lookup;
    }

    private static void AddSpriteLookupCandidate(
        Dictionary<string, Sprite> target,
        HashSet<string> ambiguous,
        string key,
        Sprite sprite)
    {
        if (target == null || ambiguous == null || sprite == null || string.IsNullOrWhiteSpace(key))
            return;

        if (target.TryGetValue(key, out var existing) && existing != sprite)
        {
            target.Remove(key);
            ambiguous.Add(key);
            return;
        }

        if (!ambiguous.Contains(key))
            target[key] = sprite;
    }

    private static bool TryResolveSpriteByName(
        SpriteNameLookup lookup,
        string rendererName,
        out Sprite sprite,
        out bool ambiguous)
    {
        sprite = null;
        ambiguous = false;
        if (lookup == null || string.IsNullOrWhiteSpace(rendererName))
            return false;

        if (lookup.exactAmbiguous.Contains(rendererName))
        {
            ambiguous = true;
            return false;
        }

        if (lookup.exact.TryGetValue(rendererName, out sprite) && sprite != null)
            return true;

        var normalized = NormalizeSpriteMatchName(rendererName);
        if (string.IsNullOrEmpty(normalized))
            return false;

        if (lookup.normalizedAmbiguous.Contains(normalized))
        {
            ambiguous = true;
            return false;
        }

        return lookup.normalized.TryGetValue(normalized, out sprite) && sprite != null;
    }

    private static string NormalizeSpriteMatchName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        if (value.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - "(Clone)".Length);

        var chars = new char[value.Length];
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch))
                continue;
            chars[count++] = char.ToLowerInvariant(ch);
        }

        var normalized = count > 0 ? new string(chars, 0, count) : string.Empty;
        if (normalized.StartsWith("left", StringComparison.Ordinal))
            normalized = "l" + normalized.Substring(4);
        else if (normalized.StartsWith("right", StringComparison.Ordinal))
            normalized = "r" + normalized.Substring(5);

        if (normalized.StartsWith("sprite", StringComparison.Ordinal) && normalized.Length > 6)
            normalized = normalized.Substring(6);

        return normalized;
    }

    private static void AddExample(List<string> examples, string value)
    {
        if (examples == null || examples.Count >= 3 || string.IsNullOrWhiteSpace(value))
            return;
        if (!examples.Contains(value))
            examples.Add(value);
    }

    private static List<SpriteRenderer> CollectSpriteRenderers(IReadOnlyList<GameObject> selectedObjects, bool includeChildren)
    {
        var result = new List<SpriteRenderer>();
        var seen = new HashSet<SpriteRenderer>();
        if (selectedObjects == null)
            return result;

        for (var i = 0; i < selectedObjects.Count; i++)
        {
            var selected = selectedObjects[i];
            if (selected == null)
                continue;

            if (includeChildren)
            {
                var childRenderers = selected.GetComponentsInChildren<SpriteRenderer>(true);
                for (var j = 0; j < childRenderers.Length; j++)
                    AddUniqueRenderer(childRenderers[j], seen, result);
                continue;
            }

            AddUniqueRenderer(selected.GetComponent<SpriteRenderer>(), seen, result);
        }

        return result;
    }

    private static void AddUniqueRenderer(
        SpriteRenderer spriteRenderer,
        HashSet<SpriteRenderer> seen,
        List<SpriteRenderer> result)
    {
        if (spriteRenderer == null || seen == null || result == null)
            return;
        if (seen.Add(spriteRenderer))
            result.Add(spriteRenderer);
    }

    private static void PersistChangedSpriteRenderers(IReadOnlyList<SpriteRenderer> renderers)
    {
        if (renderers == null || renderers.Count == 0)
            return;

        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        var savedPrefabStage = false;
        var markedScenes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < renderers.Count; i++)
        {
            var spriteRenderer = renderers[i];
            if (spriteRenderer == null)
                continue;

            if (prefabStage != null &&
                prefabStage.prefabContentsRoot != null &&
                spriteRenderer.transform.IsChildOf(prefabStage.prefabContentsRoot.transform))
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
                if (!savedPrefabStage)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabStage.assetPath);
                    savedPrefabStage = true;
                }
                continue;
            }

            var scene = spriteRenderer.gameObject.scene;
            var sceneKey = !string.IsNullOrWhiteSpace(scene.path) ? scene.path : scene.handle.ToString();
            if (markedScenes.Add(sceneKey))
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static GameObject ResolveSkeletonHierarchyRoot(PrefabStage stage, GameObject selectedGameObject)
    {
        if (stage != null &&
            stage.prefabContentsRoot != null &&
            selectedGameObject != null &&
            selectedGameObject.transform.IsChildOf(stage.prefabContentsRoot.transform))
        {
            return stage.prefabContentsRoot;
        }

        return selectedGameObject != null && selectedGameObject.transform.root != null
            ? selectedGameObject.transform.root.gameObject
            : selectedGameObject;
    }

    internal static bool TryGetTargetSpriteSkin(
        GameObject prefabRoot,
        GameObject selectedGameObject,
        out SpriteSkin spriteSkin,
        out string error)
    {
        spriteSkin = null;
        error = null;

        if (prefabRoot == null)
        {
            error = "No prefab is open.";
            return false;
        }

        if (selectedGameObject == null)
        {
            error = "Select a GameObject with SpriteSkin.";
            return false;
        }

        if (!selectedGameObject.transform.IsChildOf(prefabRoot.transform))
        {
            error = "Selection is outside the open prefab.";
            return false;
        }

        spriteSkin = selectedGameObject.GetComponent<SpriteSkin>();
        if (spriteSkin == null)
        {
            error = "Selected GameObject has no SpriteSkin.";
            return false;
        }

        return true;
    }

    internal static AssignmentStats AssignBones(GameObject prefabRoot, SpriteSkin spriteSkin)
    {
        var stats = new AssignmentStats();
        if (prefabRoot == null || spriteSkin == null)
            return stats;

        stats.SpriteSkins = 1;
        var allTransforms = prefabRoot.GetComponentsInChildren<Transform>(true);
        var allLookup = BuildLookup(allTransforms);
        if (AssignBones(spriteSkin, allLookup, out var assigned, out var missing, out var changed))
        {
            stats.AssignedBones = assigned;
            stats.MissingBones = missing;
            RefreshSpriteSkin(spriteSkin);
            if (changed)
                stats.ChangedSpriteSkins = 1;
        }
        else
        {
            stats.SkippedSpriteSkins = 1;
        }

        return stats;
    }

    internal static AssignmentStats AssignBonesOnSelectedSpriteRenderers(
        GameObject prefabRoot,
        IReadOnlyList<GameObject> selectedObjects)
    {
        var stats = new AssignmentStats();
        if (prefabRoot == null)
        {
            stats.Error = "No prefab is open.";
            return stats;
        }

        if (selectedObjects == null || selectedObjects.Count == 0)
        {
            stats.Error = "Select one or more GameObjects with SpriteRenderer.";
            return stats;
        }

        var spriteSkins = new List<SpriteSkin>();
        var seen = new HashSet<SpriteSkin>();
        for (var i = 0; i < selectedObjects.Count; i++)
        {
            var selectedObject = selectedObjects[i];
            if (selectedObject == null || !selectedObject.transform.IsChildOf(prefabRoot.transform))
                continue;

            if (selectedObject.GetComponent<SpriteRenderer>() == null)
                continue;

            var spriteSkin = selectedObject.GetComponent<SpriteSkin>();
            if (spriteSkin == null)
            {
                spriteSkin = Undo.AddComponent<SpriteSkin>(selectedObject);
                stats.AddedSpriteSkins++;
            }

            if (spriteSkin != null && seen.Add(spriteSkin))
                spriteSkins.Add(spriteSkin);
        }

        if (spriteSkins.Count == 0)
        {
            stats.Error = "Selected prefab objects have no SpriteRenderer.";
            return stats;
        }

        var assignStats = AssignBones(prefabRoot, spriteSkins);
        assignStats.AddedSpriteSkins = stats.AddedSpriteSkins;
        return assignStats;
    }

    internal static AssignmentStats AssignBones(GameObject prefabRoot, IReadOnlyList<SpriteSkin> spriteSkins)
    {
        var stats = new AssignmentStats();
        if (prefabRoot == null || spriteSkins == null)
            return stats;

        var allTransforms = prefabRoot.GetComponentsInChildren<Transform>(true);
        var allLookup = BuildLookup(allTransforms);

        for (var i = 0; i < spriteSkins.Count; i++)
        {
            var spriteSkin = spriteSkins[i];
            if (spriteSkin == null)
                continue;

            stats.SpriteSkins++;
            if (AssignBones(spriteSkin, allLookup, out var assigned, out var missing, out var changed))
            {
                stats.AssignedBones += assigned;
                stats.MissingBones += missing;
                RefreshSpriteSkin(spriteSkin);
                if (changed)
                    stats.ChangedSpriteSkins++;
            }
            else
            {
                stats.SkippedSpriteSkins++;
            }
        }

        return stats;
    }

    internal static AssignmentStats AssignBonesToCreatedSkeleton(
        IReadOnlyList<SpriteSkin> spriteSkins,
        IReadOnlyList<Transform> createdBones)
    {
        var stats = new AssignmentStats();
        if (spriteSkins == null || createdBones == null)
            return stats;

        var createdLookup = BuildLookup(createdBones);
        for (var i = 0; i < spriteSkins.Count; i++)
        {
            var spriteSkin = spriteSkins[i];
            if (spriteSkin == null)
                continue;

            stats.SpriteSkins++;
            if (BindSpriteSkinToCreatedSkeleton(spriteSkin, createdLookup, out var assigned, out var missing, out var changed))
            {
                stats.AssignedBones += assigned;
                stats.MissingBones += missing;
                RefreshSpriteSkin(spriteSkin);
                if (changed)
                    stats.ChangedSpriteSkins++;
            }
            else
            {
                stats.SkippedSpriteSkins++;
            }
        }

        return stats;
    }

    internal static SkeletonRecreateStats RecreateSkeleton(GameObject hierarchyRoot, SpriteRenderer spriteRenderer)
    {
        var stats = new SkeletonRecreateStats();
        if (hierarchyRoot == null || spriteRenderer == null)
        {
            stats.Error = "Select a GameObject with SpriteRenderer.";
            return stats;
        }

        stats.SpriteSkins = 1;
        if (spriteRenderer.sprite == null)
        {
            stats.SkippedSpriteSkins = 1;
            stats.Error = "Selected SpriteRenderer has no sprite.";
            return stats;
        }

        if (!TryLoadPsdSkeletonBones(spriteRenderer.sprite, out var spriteBones, out var error))
        {
            stats.SkippedSpriteSkins = 1;
            stats.Error = error;
            return stats;
        }

        return RecreateSkeletonFromBones(hierarchyRoot, spriteRenderer, spriteBones);
    }

    internal static SkeletonRecreateStats RecreateSkeletonFromBones(
        GameObject hierarchyRoot,
        SpriteRenderer spriteRenderer,
        IReadOnlyList<SpriteBone> spriteBones)
    {
        var stats = new SkeletonRecreateStats();
        if (hierarchyRoot == null || spriteRenderer == null)
        {
            stats.Error = "Select a GameObject with SpriteRenderer.";
            return stats;
        }

        if (spriteBones == null || spriteBones.Count == 0)
        {
            stats.SkippedSpriteSkins = 1;
            stats.Error = "Linked PSD has no bones.";
            return stats;
        }

        Undo.RegisterFullObjectHierarchyUndo(hierarchyRoot, "Recreate SpriteSkin Skeleton");
        var spriteSkin = spriteRenderer.GetComponent<SpriteSkin>();
        if (spriteSkin == null)
            spriteSkin = Undo.AddComponent<SpriteSkin>(spriteRenderer.gameObject);

        var spriteSkins = hierarchyRoot.GetComponentsInChildren<SpriteSkin>(true);
        var skeletonParent = ResolveSkeletonSiblingParent(hierarchyRoot.transform, spriteRenderer.transform);
        stats.SpriteSkins = spriteSkins.Length;
        stats.DeletedBones = DeleteExistingSkeletonRoots(hierarchyRoot.transform, spriteSkins);
        stats.DeletedBones += DeleteExistingSiblingBones(skeletonParent, spriteRenderer.transform, spriteBones);

        var created = CreateSkeletonTransforms(skeletonParent, spriteBones, ResolveImporterBonePositionScale(spriteRenderer.sprite));
        stats.RecreatedBones = created.Length;
        var createdLookup = BuildLookup(created);
        for (var i = 0; i < spriteSkins.Length; i++)
        {
            var current = spriteSkins[i];
            if (current == null)
                continue;

            if (BindSpriteSkinToCreatedSkeleton(current, createdLookup, out _, out _, out var changed))
            {
                RefreshSpriteSkin(current);
                if (changed)
                    stats.ChangedSpriteSkins++;
            }
            else
            {
                stats.SkippedSpriteSkins++;
            }
        }

        return stats;
    }

    internal static bool TryLoadPsdSkeletonBones(Sprite sprite, out List<SpriteBone> bones, out string error)
    {
        bones = null;
        error = null;

        if (sprite == null)
        {
            error = "Selected SpriteRenderer has no sprite.";
            return false;
        }

        var assetPath = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = "Selected sprite is not linked to a PSD asset.";
            return false;
        }

        var dataProvider = AssetImporter.GetAtPath(assetPath) as ISpriteEditorDataProvider;
        if (dataProvider == null)
        {
            error = "Selected sprite asset is not a PSD Sprite Editor asset.";
            return false;
        }

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            error = "Failed to read linked PSD Sprite Editor data.";
            return false;
        }

        if (dataProvider.HasDataProvider(typeof(ICharacterDataProvider)))
        {
            var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
            var characterData = characterProvider != null ? characterProvider.GetCharacterData() : default;
            if (characterData.bones != null && characterData.bones.Length > 0)
            {
                bones = new List<SpriteBone>(characterData.bones);
                return true;
            }
        }

        var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
        var spriteRects = dataProvider.GetSpriteRects();
        bones = BuildLargestImporterBoneSet(boneProvider, spriteRects);
        if (bones != null && bones.Count > 0)
            return true;

        error = "Linked PSD has no bones.";
        bones = null;
        return false;
    }

    internal static PsdBoneRenameStats RenameLinkedPsdBonePlaceholders(SpriteRenderer spriteRenderer)
    {
        var stats = new PsdBoneRenameStats();
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        if (sprite == null)
        {
            stats.Error = "Selected SpriteRenderer has no sprite.";
            return stats;
        }

        var assetPath = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            stats.Error = "Selected sprite is not linked to a PSD asset.";
            return stats;
        }

        stats.AssetPath = assetPath;
        var importer = AssetImporter.GetAtPath(assetPath);
        var dataProvider = importer as ISpriteEditorDataProvider;
        if (dataProvider == null)
        {
            stats.Error = "Selected sprite asset is not a PSD Sprite Editor asset.";
            return stats;
        }

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
        }
        catch
        {
            stats.Error = "Failed to read linked PSD Sprite Editor data.";
            return stats;
        }

        var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
        if (characterProvider == null)
        {
            stats.Error = "Linked PSD has no character data provider.";
            return stats;
        }

        CharacterData characterData;
        try
        {
            characterData = characterProvider.GetCharacterData();
        }
        catch (Exception ex)
        {
            stats.Error = $"Failed to read character data: {ex.Message}";
            return stats;
        }

        var characterBones = characterData.bones;
        if (characterBones == null || characterBones.Length == 0)
        {
            stats.Error = "Linked PSD character skeleton has no bones.";
            return stats;
        }

        var boneProvider = dataProvider.GetDataProvider<ISpriteBoneDataProvider>();
        if (boneProvider == null)
        {
            stats.Error = "Linked PSD has no sprite bone data provider.";
            return stats;
        }

        var spriteRects = dataProvider.GetSpriteRects();
        if (spriteRects == null || spriteRects.Length == 0)
        {
            stats.Error = "Linked PSD has no sprite rects.";
            return stats;
        }

        Undo.RegisterCompleteObjectUndo(importer, "Rename PSD Sprite Bones");

        var characterBoneIndexByName = BuildCharacterBoneIndexByName(characterBones);
        var partIndexBySpriteId = BuildCharacterPartIndexBySpriteId(characterData.parts);
        var changed = false;

        for (var i = 0; i < spriteRects.Length; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect == null)
                continue;

            stats.SpriteRects++;
            List<SpriteBone> spriteBones;
            try
            {
                spriteBones = boneProvider.GetBones(spriteRect.spriteID);
            }
            catch
            {
                stats.SkippedSpriteRects++;
                continue;
            }

            if (spriteBones == null || spriteBones.Count == 0)
            {
                stats.SkippedSpriteRects++;
                continue;
            }

            var spriteBonesChanged = RenamePlaceholderSpriteBones(spriteBones, characterBones, ref stats);
            if (spriteBonesChanged)
            {
                boneProvider.SetBones(spriteRect.spriteID, spriteBones);
                changed = true;
            }

            var partBones = BuildCharacterPartBoneIndicesForSpriteBones(spriteBones, characterBoneIndexByName);
            if (partBones == null || partBones.Length == 0)
                continue;

            var spriteId = spriteRect.spriteID.ToString();
            if (partIndexBySpriteId.TryGetValue(spriteId, out var partIndex) &&
                partIndex >= 0 &&
                characterData.parts != null &&
                partIndex < characterData.parts.Length)
            {
                var part = characterData.parts[partIndex];
                if (!AreIndicesEqual(part.bones, partBones))
                {
                    part.bones = partBones;
                    characterData.parts[partIndex] = part;
                    stats.UpdatedCharacterParts++;
                    changed = true;
                }
            }
        }

        if (!changed)
            return stats;

        try
        {
            characterProvider.SetCharacterData(characterData);
            dataProvider.Apply();
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            stats.Reimported = true;
        }
        catch (Exception ex)
        {
            stats.Error = $"Failed to save PSD bone rename: {ex.Message}";
        }

        return stats;
    }

    private static bool RenamePlaceholderSpriteBones(
        IList<SpriteBone> spriteBones,
        IReadOnlyList<SpriteBone> characterBones,
        ref PsdBoneRenameStats stats)
    {
        var changed = false;
        if (spriteBones == null || characterBones == null)
            return false;

        for (var i = 0; i < spriteBones.Count; i++)
        {
            var spriteBone = spriteBones[i];
            if (!TryResolveCharacterBoneName(spriteBone.name, characterBones, out var characterBoneName))
                continue;

            if (string.Equals(spriteBone.name, characterBoneName, StringComparison.Ordinal))
                continue;

            spriteBone.name = characterBoneName;
            spriteBones[i] = spriteBone;
            stats.RenamedSpriteBones++;
            changed = true;
        }

        return changed;
    }

    internal static bool TryResolveCharacterBoneName(
        string spriteBoneName,
        IReadOnlyList<SpriteBone> characterBones,
        out string characterBoneName)
    {
        characterBoneName = null;
        if (string.IsNullOrWhiteSpace(spriteBoneName) || characterBones == null)
            return false;

        const string prefix = "bone_";
        if (!spriteBoneName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var indexText = spriteBoneName.Substring(prefix.Length);
        if (!int.TryParse(indexText, out var index) || index < 0 || index >= characterBones.Count)
            return false;

        var candidate = characterBones[index].name;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        characterBoneName = candidate;
        return true;
    }

    internal static int[] BuildCharacterPartBoneIndicesForSpriteBones(
        IReadOnlyList<SpriteBone> spriteBones,
        IReadOnlyDictionary<string, int> characterBoneIndexByName)
    {
        if (spriteBones == null || spriteBones.Count == 0 || characterBoneIndexByName == null)
            return Array.Empty<int>();

        var indices = new int[spriteBones.Count];
        for (var i = 0; i < spriteBones.Count; i++)
        {
            var boneName = spriteBones[i].name;
            if (string.IsNullOrWhiteSpace(boneName) ||
                !characterBoneIndexByName.TryGetValue(boneName, out var characterBoneIndex))
            {
                return Array.Empty<int>();
            }

            indices[i] = characterBoneIndex;
        }

        return indices;
    }

    private static Dictionary<string, int> BuildCharacterBoneIndexByName(IReadOnlyList<SpriteBone> characterBones)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (characterBones == null)
            return result;

        for (var i = 0; i < characterBones.Count; i++)
        {
            var name = characterBones[i].name;
            if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name))
                continue;

            result[name] = i;
        }

        return result;
    }

    private static Dictionary<string, int> BuildCharacterPartIndexBySpriteId(IReadOnlyList<CharacterPart> parts)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (parts == null)
            return result;

        for (var i = 0; i < parts.Count; i++)
        {
            var spriteId = parts[i].spriteId;
            if (string.IsNullOrWhiteSpace(spriteId) || result.ContainsKey(spriteId))
                continue;

            result[spriteId] = i;
        }

        return result;
    }

    private static bool AreIndicesEqual(IReadOnlyList<int> lhs, IReadOnlyList<int> rhs)
    {
        if (lhs == null || rhs == null)
            return lhs == rhs || (lhs == null && rhs != null && rhs.Count == 0) || (rhs == null && lhs != null && lhs.Count == 0);
        if (lhs.Count != rhs.Count)
            return false;

        for (var i = 0; i < lhs.Count; i++)
        {
            if (lhs[i] != rhs[i])
                return false;
        }

        return true;
    }

    private static List<SpriteBone> BuildLargestImporterBoneSet(
        ISpriteBoneDataProvider boneProvider,
        IReadOnlyList<SpriteRect> spriteRects)
    {
        if (boneProvider == null || spriteRects == null || spriteRects.Count == 0)
            return null;

        List<SpriteBone> best = null;
        for (var i = 0; i < spriteRects.Count; i++)
        {
            var spriteRect = spriteRects[i];
            if (spriteRect == null)
                continue;

            List<SpriteBone> candidate = null;
            try
            {
                candidate = boneProvider.GetBones(spriteRect.spriteID);
            }
            catch
            {
                candidate = null;
            }

            if (candidate == null || candidate.Count == 0)
                continue;
            if (best == null || candidate.Count > best.Count)
                best = new List<SpriteBone>(candidate);
        }

        return best;
    }

    private static bool AssignBones(
        SpriteSkin spriteSkin,
        IReadOnlyDictionary<string, List<Transform>> prefabLookup,
        out int assigned,
        out int missing,
        out bool changed)
    {
        assigned = 0;
        missing = 0;
        changed = false;

        var spriteRenderer = spriteSkin.GetComponent<SpriteRenderer>();
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteBones = sprite != null ? sprite.GetBones() : null;
        if (spriteBones == null || spriteBones.Length == 0)
            return false;

        var serialized = new SerializedObject(spriteSkin);
        var rootBoneProperty = serialized.FindProperty("m_RootBone");
        var boneTransformsProperty = serialized.FindProperty("m_BoneTransforms");
        if (rootBoneProperty == null || boneTransformsProperty == null || !boneTransformsProperty.isArray)
            return false;

        var searchRoot = rootBoneProperty.objectReferenceValue as Transform;
        var scopedLookup = searchRoot != null
            ? BuildLookup(searchRoot.GetComponentsInChildren<Transform>(true))
            : prefabLookup;
        var nextBones = ResolveBoneTransforms(spriteBones, scopedLookup, prefabLookup, out assigned, out missing);

        var rootBone = ResolveRootBone(spriteBones, nextBones) ??
                       FindCommonAncestor(nextBones) ??
                       nextBones.FirstOrDefault(x => x != null);
        Undo.RecordObject(spriteSkin, "Assign SpriteSkin Bones");

        if (rootBoneProperty.objectReferenceValue != rootBone)
        {
            rootBoneProperty.objectReferenceValue = rootBone;
            changed = true;
        }

        if (boneTransformsProperty.arraySize != nextBones.Length)
        {
            boneTransformsProperty.arraySize = nextBones.Length;
            changed = true;
        }

        for (var i = 0; i < nextBones.Length; i++)
        {
            var element = boneTransformsProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == nextBones[i])
                continue;

            element.objectReferenceValue = nextBones[i];
            changed = true;
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(spriteSkin);
            PrefabUtility.RecordPrefabInstancePropertyModifications(spriteSkin);
        }

        return true;
    }

    private static bool BindSpriteSkinToCreatedSkeleton(
        SpriteSkin spriteSkin,
        IReadOnlyDictionary<string, List<Transform>> createdLookup,
        out int assigned,
        out int missing,
        out bool changed)
    {
        assigned = 0;
        missing = 0;
        changed = false;

        var spriteRenderer = spriteSkin != null ? spriteSkin.GetComponent<SpriteRenderer>() : null;
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteBones = sprite != null ? sprite.GetBones() : null;
        if (spriteBones == null || spriteBones.Length == 0)
            return false;

        var nextBones = ResolveBoneTransforms(spriteBones, createdLookup, createdLookup, out assigned, out missing);
        if (assigned == 0)
            return false;

        var rootBone = ResolveRootBone(spriteBones, nextBones) ??
                       FindCommonAncestor(nextBones) ??
                       nextBones.FirstOrDefault(x => x != null);

        var serialized = new SerializedObject(spriteSkin);
        var rootBoneProperty = serialized.FindProperty("m_RootBone");
        var boneTransformsProperty = serialized.FindProperty("m_BoneTransforms");
        if (rootBoneProperty == null || boneTransformsProperty == null || !boneTransformsProperty.isArray)
            return false;

        Undo.RecordObject(spriteSkin, "Recreate SpriteSkin Skeleton");

        if (rootBoneProperty.objectReferenceValue != rootBone)
        {
            rootBoneProperty.objectReferenceValue = rootBone;
            changed = true;
        }

        if (boneTransformsProperty.arraySize != nextBones.Length)
        {
            boneTransformsProperty.arraySize = nextBones.Length;
            changed = true;
        }

        for (var i = 0; i < nextBones.Length; i++)
        {
            var element = boneTransformsProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == nextBones[i])
                continue;

            element.objectReferenceValue = nextBones[i];
            changed = true;
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(spriteSkin);
            PrefabUtility.RecordPrefabInstancePropertyModifications(spriteSkin);
        }

        return true;
    }

    internal static void RefreshSpriteSkin(SpriteSkin spriteSkin)
    {
        if (spriteSkin == null)
            return;

        spriteSkin.OnPreviewUpdate();
        if (spriteSkin.enabled)
        {
            spriteSkin.enabled = false;
            spriteSkin.enabled = true;
        }
        spriteSkin.OnPreviewUpdate();
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }

    internal static Transform[] ResolveBoneTransforms(
        IReadOnlyList<SpriteBone> spriteBones,
        IReadOnlyDictionary<string, List<Transform>> scopedLookup,
        IReadOnlyDictionary<string, List<Transform>> prefabLookup,
        out int assigned,
        out int missing)
    {
        assigned = 0;
        missing = 0;
        if (spriteBones == null)
            return Array.Empty<Transform>();

        var nextBones = new Transform[spriteBones.Count];
        var used = new HashSet<Transform>();
        for (var i = 0; i < spriteBones.Count; i++)
        {
            var boneName = spriteBones[i].name;
            var resolved = ResolveBone(boneName, scopedLookup, used) ??
                           ResolveBone(boneName, prefabLookup, used);
            nextBones[i] = resolved;
            if (resolved != null)
            {
                assigned++;
                used.Add(resolved);
            }
            else
            {
                missing++;
            }
        }

        return nextBones;
    }

    internal static Dictionary<string, List<Transform>> BuildLookup(IEnumerable<Transform> transforms)
    {
        var result = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);
        if (transforms == null)
            return result;

        foreach (var transform in transforms)
        {
            if (transform == null || string.IsNullOrWhiteSpace(transform.name))
                continue;

            if (!result.TryGetValue(transform.name, out var list))
            {
                list = new List<Transform>();
                result[transform.name] = list;
            }

            list.Add(transform);
        }

        return result;
    }

    private static Transform ResolveBone(
        string boneName,
        IReadOnlyDictionary<string, List<Transform>> lookup,
        HashSet<Transform> used)
    {
        if (string.IsNullOrWhiteSpace(boneName) || lookup == null)
            return null;
        if (!lookup.TryGetValue(boneName, out var candidates) || candidates == null || candidates.Count == 0)
            return null;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate != null && (used == null || !used.Contains(candidate)))
                return candidate;
        }

        return candidates.FirstOrDefault(x => x != null);
    }

    internal static Transform ResolveRootBone(IReadOnlyList<SpriteBone> spriteBones, IReadOnlyList<Transform> boneTransforms)
    {
        if (spriteBones == null || boneTransforms == null)
            return null;

        for (var i = 0; i < spriteBones.Count && i < boneTransforms.Count; i++)
        {
            if (spriteBones[i].parentId < 0 && boneTransforms[i] != null)
                return boneTransforms[i];
        }

        return null;
    }

    internal static Transform FindCommonAncestor(IReadOnlyList<Transform> transforms)
    {
        if (transforms == null)
            return null;

        Transform common = null;
        for (var i = 0; i < transforms.Count; i++)
        {
            var transform = transforms[i];
            if (transform == null)
                continue;

            common = common == null ? transform : FindCommonAncestor(common, transform);
            if (common == null)
                return null;
        }

        return common;
    }

    private static Transform FindCommonAncestor(Transform a, Transform b)
    {
        if (a == null || b == null)
            return null;

        var ancestors = new HashSet<Transform>();
        var current = a;
        while (current != null)
        {
            ancestors.Add(current);
            current = current.parent;
        }

        current = b;
        while (current != null)
        {
            if (ancestors.Contains(current))
                return current;
            current = current.parent;
        }

        return null;
    }

    private static Transform ResolveSkeletonSiblingParent(Transform hierarchyRoot, Transform spriteRendererTransform)
    {
        if (spriteRendererTransform == null)
            return hierarchyRoot;

        if (spriteRendererTransform.parent != null &&
            (hierarchyRoot == null || spriteRendererTransform.parent.IsChildOf(hierarchyRoot)))
        {
            return spriteRendererTransform.parent;
        }

        return spriteRendererTransform;
    }

    private static float ResolveImporterBonePositionScale(Sprite sprite)
    {
        return sprite != null && sprite.pixelsPerUnit > 0f
            ? 1f / sprite.pixelsPerUnit
            : 1f;
    }

    private static Transform[] CreateSkeletonTransforms(
        Transform skeletonParent,
        IReadOnlyList<SpriteBone> spriteBones,
        float positionScale)
    {
        if (skeletonParent == null || spriteBones == null)
            return Array.Empty<Transform>();

        var created = new Transform[spriteBones.Count];
        for (var i = 0; i < spriteBones.Count; i++)
        {
            var spriteBone = spriteBones[i];
            var boneObject = new GameObject(string.IsNullOrWhiteSpace(spriteBone.name) ? $"bone_{i}" : spriteBone.name);
            Undo.RegisterCreatedObjectUndo(boneObject, "Recreate SpriteSkin Skeleton");

            created[i] = boneObject.transform;
        }

        for (var i = 0; i < spriteBones.Count; i++)
        {
            var spriteBone = spriteBones[i];
            var parent = spriteBone.parentId >= 0 && spriteBone.parentId < created.Length
                ? created[spriteBone.parentId]
                : skeletonParent;
            var transform = created[i];
            transform.SetParent(parent, false);
            transform.localPosition = ReanimatorPrefabCooker.ResolveBoneLocalPosition(spriteBone.position, positionScale);
            transform.localRotation = spriteBone.rotation;
            transform.localScale = Vector3.one;
        }

        return created;
    }

    private static int DeleteExistingSiblingBones(
        Transform skeletonParent,
        Transform targetTransform,
        IReadOnlyList<SpriteBone> spriteBones)
    {
        if (skeletonParent == null || spriteBones == null || spriteBones.Count == 0)
            return 0;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < spriteBones.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(spriteBones[i].name))
                names.Add(spriteBones[i].name);
        }

        if (names.Count == 0)
            return 0;

        var deleted = 0;
        for (var i = skeletonParent.childCount - 1; i >= 0; i--)
        {
            var child = skeletonParent.GetChild(i);
            if (child == null || child == targetTransform || !names.Contains(child.name))
                continue;

            Undo.DestroyObjectImmediate(child.gameObject);
            deleted++;
        }

        return deleted;
    }

    private static Transform[] ResolveCreatedSkinBoneTransforms(
        IReadOnlyList<SpriteBone> spriteBones,
        IReadOnlyList<Transform> createdBones)
    {
        if (spriteBones == null || spriteBones.Count == 0)
            return Array.Empty<Transform>();

        var lookup = BuildLookup(createdBones);
        return ResolveBoneTransforms(spriteBones, lookup, lookup, out _, out _);
    }

    private static int DeleteExistingSkeletonRoots(Transform prefabRoot, IReadOnlyList<SpriteSkin> targets)
    {
        if (prefabRoot == null || targets == null)
            return 0;

        var roots = CollectExistingSkeletonRoots(prefabRoot, targets);
        var deleted = 0;
        for (var i = 0; i < roots.Count; i++)
        {
            var root = roots[i];
            if (root == null ||
                root == prefabRoot ||
                !root.IsChildOf(prefabRoot))
            {
                continue;
            }

            Undo.DestroyObjectImmediate(root.gameObject);
            deleted++;
        }

        return deleted;
    }

    private static List<Transform> CollectExistingSkeletonRoots(Transform prefabRoot, IReadOnlyList<SpriteSkin> targets)
    {
        var result = new List<Transform>();
        var candidates = new HashSet<Transform>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var target = targets[targetIndex];
            if (target == null)
                continue;

            var serialized = new SerializedObject(target);
            var boneTransformsProperty = serialized.FindProperty("m_BoneTransforms");
            if (boneTransformsProperty == null || !boneTransformsProperty.isArray)
                continue;

            for (var i = 0; i < boneTransformsProperty.arraySize; i++)
            {
                if (boneTransformsProperty.GetArrayElementAtIndex(i).objectReferenceValue is Transform bone)
                    candidates.Add(bone);
            }
        }

        foreach (var candidate in candidates)
        {
            if (candidate == null || !candidate.IsChildOf(prefabRoot))
                continue;

            var root = candidate;
            while (root.parent != null &&
                   root.parent != prefabRoot &&
                   candidates.Contains(root.parent))
            {
                root = root.parent;
            }

            if (!result.Contains(root))
                result.Add(root);
        }

        return result;
    }

    internal struct AssignmentStats
    {
        public int SpriteSkins;
        public int ChangedSpriteSkins;
        public int AddedSpriteSkins;
        public int SkippedSpriteSkins;
        public int AssignedBones;
        public int MissingBones;
        public string Error;

        public string ToStatus()
        {
            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            return $"SpriteSkins: {SpriteSkins}, added: {AddedSpriteSkins}, changed: {ChangedSpriteSkins}, assigned: {AssignedBones}, missing: {MissingBones}, skipped: {SkippedSpriteSkins}";
        }
    }

    internal struct SkeletonRecreateStats
    {
        public int SpriteSkins;
        public int ChangedSpriteSkins;
        public int SkippedSpriteSkins;
        public int DeletedBones;
        public int RecreatedBones;
        public string Error;

        public string ToStatus()
        {
            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            return $"SpriteSkins: {SpriteSkins}, changed: {ChangedSpriteSkins}, deleted: {DeletedBones}, recreated: {RecreatedBones}, skipped: {SkippedSpriteSkins}";
        }
    }

    internal struct PsdBoneRenameStats
    {
        public string AssetPath;
        public int SpriteRects;
        public int SkippedSpriteRects;
        public int RenamedSpriteBones;
        public int UpdatedCharacterParts;
        public bool Reimported;
        public string Error;

        public string ToStatus()
        {
            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            var asset = string.IsNullOrWhiteSpace(AssetPath) ? "PSD" : AssetPath;
            return $"{asset}: renamed bones: {RenamedSpriteBones}, updated parts: {UpdatedCharacterParts}, sprite rects: {SpriteRects}, skipped: {SkippedSpriteRects}, reimported: {Reimported}";
        }
    }

    internal struct MissingSpriteFillStats
    {
        public string AssetPath;
        public int SpriteRenderers;
        public int MissingSprites;
        public int FilledSprites;
        public int UnmatchedSprites;
        public int AmbiguousSprites;
        public string UnmatchedExamples;
        public string AmbiguousExamples;
        public string Error;

        public string ToStatus()
        {
            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            var asset = string.IsNullOrWhiteSpace(AssetPath) ? "PSD" : AssetPath;
            var status = $"{asset}: renderers: {SpriteRenderers}, missing: {MissingSprites}, filled: {FilledSprites}, unmatched: {UnmatchedSprites}, ambiguous: {AmbiguousSprites}";
            if (!string.IsNullOrWhiteSpace(UnmatchedExamples))
                status += $", unmatched examples: {UnmatchedExamples}";
            if (!string.IsNullOrWhiteSpace(AmbiguousExamples))
                status += $", ambiguous examples: {AmbiguousExamples}";
            return status;
        }
    }
}

internal static class ReanimatorPrefabCooker
{
    public static CookResult CookNextToPsd(string psdAssetPath)
    {
        var result = new CookResult { PsdAssetPath = NormalizeAssetPath(psdAssetPath) };
        if (string.IsNullOrWhiteSpace(result.PsdAssetPath))
        {
            result.Error = "Select PSD/PSB asset first.";
            return result;
        }

        var mainAsset = AssetDatabase.LoadMainAssetAtPath(result.PsdAssetPath);
        if (mainAsset == null)
        {
            result.Error = "PSD asset was not found.";
            return result;
        }

        var sprites = AssetDatabase.LoadAllAssetsAtPath(result.PsdAssetPath)
            .OfType<Sprite>()
            .Where(sprite => sprite != null && !string.IsNullOrWhiteSpace(sprite.name))
            .ToArray();
        if (sprites.Length == 0)
        {
            result.Error = "PSD has no sprites.";
            return result;
        }

        var characterLayout = BuildCharacterLayout(result.PsdAssetPath);
        var spriteOrders = BuildSpriteOrders(result.PsdAssetPath, sprites);
        sprites = sprites
            .Select((sprite, index) => new { Sprite = sprite, Index = index, Order = ResolveSpriteOrder(sprite, spriteOrders, index) })
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Sprite)
            .ToArray();

        if (!SpriteSkinPrefabBoneAssignerWindow.TryLoadPsdSkeletonBones(sprites[0], out var spriteBones, out var skeletonError))
        {
            result.Error = skeletonError;
            return result;
        }

        var directory = System.IO.Path.GetDirectoryName(result.PsdAssetPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = "Assets";

        var prefabName = System.IO.Path.GetFileNameWithoutExtension(result.PsdAssetPath);
        var prefabPath = NormalizeAssetPath(System.IO.Path.Combine(directory, prefabName + ".prefab"));
        var prefabRoot = new GameObject(prefabName);
        try
        {
            var root = CreateChild(prefabRoot.transform, "root");
            var spritesRoot = CreateChild(root, "sprites");
            var skeletonRoot = CreateChild(root, "skeleton");
            var rigsRoot = CreateChild(root, "rigs");

            result.Sprites = CreateSpriteObjects(spritesRoot, sprites, spriteOrders, characterLayout);
            var createdBones = CreateSkeleton(
                skeletonRoot,
                spriteBones,
                ResolveImporterBonePositionScale(sprites[0], characterLayout),
                characterLayout.DocumentPivotPixels);
            result.Bones = createdBones.Length;
            AddIkManagerIfAvailable(rigsRoot, createdBones, ref result);

            var spriteSkins = prefabRoot.GetComponentsInChildren<SpriteSkin>(true);
            var assignStats = SpriteSkinPrefabBoneAssignerWindow.AssignBonesToCreatedSkeleton(spriteSkins, createdBones);
            result.SpriteSkins = assignStats.SpriteSkins;
            result.AssignedBones = assignStats.AssignedBones;
            result.MissingBones = assignStats.MissingBones;

            var saved = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath, out var success);
            if (!success || saved == null)
            {
                result.Error = "Failed to save prefab.";
                return result;
            }

            result.PrefabAssetPath = prefabPath;
            AssetDatabase.ImportAsset(prefabPath);
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            return result;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefabRoot);
        }
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child.transform;
    }

    private static int CreateSpriteObjects(
        Transform spritesRoot,
        IReadOnlyList<Sprite> sprites,
        IReadOnlyDictionary<Sprite, int> spriteOrders,
        CharacterLayout characterLayout)
    {
        if (spritesRoot == null || sprites == null)
            return 0;

        var maxOrder = spriteOrders != null && spriteOrders.Count > 0 ? spriteOrders.Values.Max() : Mathf.Max(0, sprites.Count - 1);
        var created = 0;
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite == null)
                continue;

            var psdOrder = ResolveSpriteOrder(sprite, spriteOrders, i);
            var spriteObject = new GameObject(sprite.name);
            spriteObject.transform.SetParent(spritesRoot, false);
            spriteObject.transform.SetSiblingIndex(created);
            spriteObject.transform.localPosition = ResolveSpriteLocalPosition(sprite, characterLayout);
            spriteObject.transform.localRotation = Quaternion.identity;
            spriteObject.transform.localScale = Vector3.one;

            var renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = maxOrder - psdOrder;
            spriteObject.AddComponent<SpriteSkin>();
            created++;
        }

        return created;
    }

    private static Dictionary<Sprite, int> BuildSpriteOrders(string psdAssetPath, IReadOnlyList<Sprite> sprites)
    {
        var result = new Dictionary<Sprite, int>();
        if (string.IsNullOrWhiteSpace(psdAssetPath) || sprites == null || sprites.Count == 0)
            return result;

        var importer = AssetImporter.GetAtPath(psdAssetPath);
        var orderByGuid = BuildCharacterPartOrderByGuid(importer);
        if (orderByGuid.Count == 0)
            return result;

        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            var guid = ReanimatorLayerSpriteBindingResolver.TryGetSpriteStableGuid(sprite);
            if (string.IsNullOrEmpty(guid) || !orderByGuid.TryGetValue(guid, out var order))
                continue;

            result[sprite] = order;
        }

        return result;
    }

    private static Dictionary<string, int> BuildCharacterPartOrderByGuid(AssetImporter importer)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (importer == null)
            return result;

        var serialized = new SerializedObject(importer);
        var rootNames = new[] { "characterData", "m_CharacterData" };
        for (var rootIndex = 0; rootIndex < rootNames.Length; rootIndex++)
        {
            var characterData = serialized.FindProperty(rootNames[rootIndex]);
            var parts = characterData != null
                ? characterData.FindPropertyRelative("parts") ?? characterData.FindPropertyRelative("m_Parts")
                : null;
            if (parts == null || !parts.isArray)
                continue;

            for (var i = 0; i < parts.arraySize; i++)
            {
                var part = parts.GetArrayElementAtIndex(i);
                if (part == null)
                    continue;

                var guid = ReadGuidString(part.FindPropertyRelative("spriteId") ??
                                          part.FindPropertyRelative("m_SpriteId") ??
                                          part.FindPropertyRelative("spriteID") ??
                                          part.FindPropertyRelative("m_SpriteID"));
                if (string.IsNullOrEmpty(guid))
                    continue;

                var orderProp = part.FindPropertyRelative("order") ?? part.FindPropertyRelative("m_Order");
                var order = orderProp != null && orderProp.propertyType == SerializedPropertyType.Integer
                    ? orderProp.intValue
                    : i;
                result[guid] = order;
            }

            if (result.Count > 0)
                return result;
        }

        return result;
    }

    private static int ResolveSpriteOrder(Sprite sprite, IReadOnlyDictionary<Sprite, int> spriteOrders, int fallback)
    {
        return sprite != null && spriteOrders != null && spriteOrders.TryGetValue(sprite, out var order)
            ? order
            : fallback;
    }

    private static string ReadGuidString(SerializedProperty property)
    {
        if (property == null)
            return string.Empty;

        if (property.propertyType == SerializedPropertyType.String)
            return ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(property.stringValue);

        if (property.propertyType != SerializedPropertyType.Generic)
            return string.Empty;

        var guid = property.FindPropertyRelative("m_GuidString") ??
                   property.FindPropertyRelative("guid") ??
                   property.FindPropertyRelative("m_Guid");
        return guid != null && guid.propertyType == SerializedPropertyType.String
            ? ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(guid.stringValue)
            : string.Empty;
    }

    private static CharacterLayout BuildCharacterLayout(string psdAssetPath)
    {
        var result = new CharacterLayout();
        if (string.IsNullOrWhiteSpace(psdAssetPath))
            return result;

        var dataProvider = AssetImporter.GetAtPath(psdAssetPath) as ISpriteEditorDataProvider;
        if (dataProvider == null)
            return result;

        try
        {
            dataProvider.InitSpriteEditorDataProvider();
            if (!dataProvider.HasDataProvider(typeof(ICharacterDataProvider)))
                return result;

            var characterProvider = dataProvider.GetDataProvider<ICharacterDataProvider>();
            if (characterProvider == null)
                return result;

            var characterData = characterProvider.GetCharacterData();
            if (characterData.dimension.x > 0 && characterData.dimension.y > 0)
            {
                result.DocumentPivotPixels = Vector2.Scale(
                    new Vector2(characterData.dimension.x, characterData.dimension.y),
                    characterData.pivot);
            }

            if (characterData.parts == null)
                return result;

            for (var i = 0; i < characterData.parts.Length; i++)
            {
                var part = characterData.parts[i];
                var guid = ReanimatorRebuildMetaAlgorithms.NormalizeGuidString(part.spriteId);
                if (!string.IsNullOrEmpty(guid))
                    result.PartByGuid[guid] = part.spritePosition;
            }
        }
        catch
        {
            // Fall back to the legacy atlas calculation for non-PSD/custom providers.
        }

        return result;
    }

    private static Vector3 ResolveSpriteLocalPosition(Sprite sprite, CharacterLayout characterLayout)
    {
        if (sprite == null)
            return Vector3.zero;

        var stableGuid = ReanimatorLayerSpriteBindingResolver.TryGetSpriteStableGuid(sprite);
        if (characterLayout != null &&
            !string.IsNullOrEmpty(stableGuid) &&
            characterLayout.PartByGuid.TryGetValue(stableGuid, out var partRect))
        {
            var definitionScale = ResolveCharacterPartDefinitionScale(sprite.rect, partRect);
            return ResolveCharacterSpriteLocalPosition(
                partRect,
                sprite.pivot,
                characterLayout.DocumentPivotPixels,
                definitionScale,
                sprite.pixelsPerUnit);
        }

        var texture = sprite.texture;
        var ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
        var textureWidth = texture != null ? texture.width : sprite.rect.xMax;
        var textureHeight = texture != null ? texture.height : sprite.rect.yMax;
        var pivotDocument = new Vector2(
            sprite.rect.x + sprite.pivot.x - textureWidth * 0.5f,
            sprite.rect.y + sprite.pivot.y - textureHeight * 0.5f);
        return new Vector3(pivotDocument.x / ppu, pivotDocument.y / ppu, 0f);
    }

    internal static float ResolveCharacterPartDefinitionScale(Rect spriteRect, RectInt characterPartRect)
    {
        var widthScale = characterPartRect.width > 0 ? spriteRect.width / characterPartRect.width : 0f;
        var heightScale = characterPartRect.height > 0 ? spriteRect.height / characterPartRect.height : 0f;
        if (widthScale > 0f && heightScale > 0f)
            return Mathf.Min(widthScale, heightScale);
        if (widthScale > 0f)
            return widthScale;
        if (heightScale > 0f)
            return heightScale;
        return 1f;
    }

    internal static Vector3 ResolveCharacterSpriteLocalPosition(
        RectInt characterPartRect,
        Vector2 importedSpritePivotPixels,
        Vector2 documentPivotPixels,
        float definitionScale,
        float pixelsPerUnit)
    {
        var scale = definitionScale > 0f ? definitionScale : 1f;
        var ppu = pixelsPerUnit > 0f ? pixelsPerUnit : 100f;
        var positionPixels = new Vector2(characterPartRect.x, characterPartRect.y) * scale + importedSpritePivotPixels;
        positionPixels -= documentPivotPixels;
        return new Vector3(positionPixels.x / ppu, positionPixels.y / ppu, 0f);
    }

    private static Transform[] CreateSkeleton(
        Transform skeletonRoot,
        IReadOnlyList<SpriteBone> spriteBones,
        float positionScale,
        Vector2 documentPivotPixels)
    {
        if (skeletonRoot == null || spriteBones == null)
            return Array.Empty<Transform>();

        var created = new Transform[spriteBones.Count];
        for (var i = 0; i < spriteBones.Count; i++)
        {
            var spriteBone = spriteBones[i];
            var boneObject = new GameObject(string.IsNullOrWhiteSpace(spriteBone.name) ? "bone_" + i : spriteBone.name);
            created[i] = boneObject.transform;
        }

        for (var i = 0; i < spriteBones.Count; i++)
        {
            var spriteBone = spriteBones[i];
            var parent = spriteBone.parentId >= 0 && spriteBone.parentId < created.Length
                ? created[spriteBone.parentId]
                : skeletonRoot;

            var bone = created[i];
            bone.SetParent(parent, false);
            bone.localPosition = ResolveBoneLocalPosition(spriteBone.position, positionScale);
            if (parent == skeletonRoot)
                bone.localPosition -= (Vector3)(documentPivotPixels * positionScale);
            bone.localRotation = spriteBone.rotation;
            bone.localScale = Vector3.one;
        }

        return created;
    }

    private static float ResolveImporterBonePositionScale(Sprite sprite, CharacterLayout characterLayout)
    {
        if (sprite == null || sprite.pixelsPerUnit <= 0f)
            return 1f;

        var definitionScale = 1f;
        var stableGuid = ReanimatorLayerSpriteBindingResolver.TryGetSpriteStableGuid(sprite);
        if (characterLayout != null &&
            !string.IsNullOrEmpty(stableGuid) &&
            characterLayout.PartByGuid.TryGetValue(stableGuid, out var partRect))
        {
            definitionScale = ResolveCharacterPartDefinitionScale(sprite.rect, partRect);
        }

        return ResolveCharacterBonePositionScale(definitionScale, sprite.pixelsPerUnit);
    }

    internal static float ResolveCharacterBonePositionScale(float definitionScale, float spritePixelsPerUnit)
    {
        var scale = definitionScale > 0f ? definitionScale : 1f;
        return spritePixelsPerUnit > 0f ? scale / spritePixelsPerUnit : 1f;
    }

    internal static Vector3 ResolveBoneLocalPosition(Vector3 spriteBonePosition, float positionScale)
    {
        return spriteBonePosition * positionScale;
    }

    private static void AddIkManagerIfAvailable(Transform rigsRoot, IReadOnlyList<Transform> createdBones, ref CookResult result)
    {
        if (rigsRoot == null)
            return;

        var manager = rigsRoot.gameObject.AddComponent<IKManager2D>();
        result.IkManagerAdded = true;
        result.IkSolvers = AddLimbSolvers(manager, rigsRoot, createdBones);
    }

    private sealed class CharacterLayout
    {
        public readonly Dictionary<string, RectInt> PartByGuid =
            new Dictionary<string, RectInt>(StringComparer.OrdinalIgnoreCase);

        public Vector2 DocumentPivotPixels;
    }

    internal static int AddLimbSolvers(IKManager2D manager, Transform rigsRoot, IReadOnlyList<Transform> createdBones)
    {
        if (manager == null || rigsRoot == null || createdBones == null)
            return 0;

        var created = 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "l_foot", "l_leg_limb", "l_foot_target", true) ? 1 : 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "r_foot", "r_leg_limb", "r_foot_target", true) ? 1 : 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "l_hand", "l_arm_limb", "l_hand_target", false) ? 1 : 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "r_hand", "r_arm_limb", "r_hand_target", false) ? 1 : 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "l_hand_2", "l_arm_2_limb", "l_hand_2_target", false) ? 1 : 0;
        created += AddLimbSolver(manager, rigsRoot, createdBones, "r_hand_2", "r_arm_2_limb", "r_hand_2_target", false) ? 1 : 0;
        return created;
    }

    private static bool AddLimbSolver(
        IKManager2D manager,
        Transform rigsRoot,
        IReadOnlyList<Transform> createdBones,
        string effectorBoneName,
        string solverName,
        string targetName,
        bool offsetTargetDown)
    {
        var effector = FindCreatedBone(createdBones, effectorBoneName);
        if (effector == null || effector.parent == null || effector.parent.parent == null)
            return false;

        var target = CreateChild(rigsRoot, targetName);
        target.position = effector.position;
        if (offsetTargetDown)
            target.position += Vector3.down * ResolveFootIkTargetOffset(effector);
        target.rotation = effector.rotation;

        var solverTransform = CreateChild(rigsRoot, solverName);
        var solver = solverTransform.gameObject.AddComponent<LimbSolver2D>();
        solver.weight = 1f;

        var chain = solver.GetChain(0);
        chain.effector = effector;
        chain.target = target;
        chain.transformCount = 3;
        chain.Initialize();
        solver.Initialize();
        manager.AddSolver(solver);
        return true;
    }

    private static Transform FindCreatedBone(IReadOnlyList<Transform> createdBones, string name)
    {
        if (createdBones == null || string.IsNullOrWhiteSpace(name))
            return null;

        for (var i = 0; i < createdBones.Count; i++)
        {
            var bone = createdBones[i];
            if (bone != null && string.Equals(bone.name, name, StringComparison.OrdinalIgnoreCase))
                return bone;
        }

        return null;
    }

    private static float ResolveFootIkTargetOffset(Transform foot)
    {
        var lowerLength = foot != null ? foot.localPosition.magnitude : 0f;
        return Mathf.Clamp(lowerLength * 0.2f, 0.08f, 0.35f);
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }

    internal struct CookResult
    {
        public string PsdAssetPath;
        public string PrefabAssetPath;
        public int Sprites;
        public int Bones;
        public int SpriteSkins;
        public int AssignedBones;
        public int MissingBones;
        public bool IkManagerAdded;
        public int IkSolvers;
        public string Error;

        public string ToStatus()
        {
            if (!string.IsNullOrWhiteSpace(Error))
                return Error;

            return "Prefab: " + PrefabAssetPath +
                   ", sprites: " + Sprites +
                   ", bones: " + Bones +
                   ", sprite skins: " + SpriteSkins +
                   ", assigned bones: " + AssignedBones +
                   ", missing bones: " + MissingBones +
                   ", IK Manager: " + (IkManagerAdded ? "added" : "missing") +
                   ", IK solvers: " + IkSolvers;
        }
    }
}
#endif
