#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

public sealed class ReanimatorPrefabSpriteIssueWindow : EditorWindow
{
    private const string MenuPath = "KhorebdisLab/Reanimator/Prefab Sprite Issues";
    private const string WindowTitle = "Prefab Sprite Issues";
    private const string FolderPathPrefsKey = "Reanimator.PrefabSpriteIssues.FolderPath";
    private const string IncludeInactivePrefsKey = "Reanimator.PrefabSpriteIssues.IncludeInactive";

    private readonly List<PrefabIssueGroup> groups = new List<PrefabIssueGroup>();
    private readonly HashSet<string> expandedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [SerializeField] private string folderPath = "Assets";
    [SerializeField] private bool includeInactive = true;

    private string status = "Select a Project folder and scan.";
    private Vector2 scroll;

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var window = GetWindow<ReanimatorPrefabSpriteIssueWindow>(WindowTitle);
        window.minSize = new Vector2(540f, 320f);
        window.UseSelectedFolder();
    }

    private void OnEnable()
    {
        LoadPersistedSettings();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawStatus();
        DrawResults();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Use Selected Folder", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                UseSelectedFolder();

            if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                Scan();

            EditorGUI.BeginChangeCheck();
            includeInactive = GUILayout.Toggle(includeInactive, "Inactive", EditorStyles.toolbarButton, GUILayout.Width(72f));
            if (EditorGUI.EndChangeCheck())
                SavePersistedSettings();

            GUILayout.Space(8f);
            GUILayout.Label("Folder", GUILayout.Width(42f));
            EditorGUI.BeginChangeCheck();
            folderPath = GUILayout.TextField(folderPath, EditorStyles.toolbarTextField);
            if (EditorGUI.EndChangeCheck())
                SavePersistedSettingsIfFolderIsValid();
        }
    }

    private void DrawStatus()
    {
        EditorGUILayout.HelpBox(status, groups.Count == 0 ? MessageType.Info : MessageType.Warning);
    }

    private void DrawResults()
    {
        using (var scope = new EditorGUILayout.ScrollViewScope(scroll))
        {
            scroll = scope.scrollPosition;

            if (groups.Count == 0)
                return;

            foreach (var group in groups)
                DrawGroup(group);
        }
    }

    private void DrawGroup(PrefabIssueGroup group)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var expanded = expandedPrefabs.Contains(group.AssetPath);
                var nextExpanded = EditorGUILayout.Foldout(expanded, $"{group.DisplayName} ({group.Issues.Count})", true);
                if (nextExpanded)
                    expandedPrefabs.Add(group.AssetPath);
                else
                    expandedPrefabs.Remove(group.AssetPath);

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                    PingAsset(group.AssetPath);
                if (GUILayout.Button("Open", GUILayout.Width(52f)))
                    OpenAsset(group.AssetPath);
            }

            EditorGUILayout.LabelField(group.AssetPath, EditorStyles.miniLabel);
            if (!expandedPrefabs.Contains(group.AssetPath))
                return;

            for (var i = 0; i < group.Issues.Count; i++)
                DrawIssue(group.AssetPath, group.Issues[i]);
        }
    }

    private static void DrawIssue(string assetPath, PrefabSpriteIssue issue)
    {
        var icon = GetIssueIcon(issue.Kind);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(icon, GUILayout.Width(20f), GUILayout.Height(18f));
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField($"{issue.ObjectPath}: {issue.Message}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(issue.Kind.ToString(), EditorStyles.miniLabel);
            }

            if (TryResolveOpenPrefabIssueTarget(assetPath, issue.ObjectPath, out var target))
            {
                if (GUILayout.Button("Ping GO", GUILayout.Width(70f)))
                    PingGameObject(target);
            }
        }
    }

    private void UseSelectedFolder()
    {
        if (TryGetSelectedFolder(out var selectedFolder))
        {
            SetFolderPath(selectedFolder);
            status = $"Folder: {folderPath}";
            Repaint();
            return;
        }

        status = "Select a folder in Project window.";
        Repaint();
    }

    private void Scan()
    {
        groups.Clear();
        expandedPrefabs.Clear();
        folderPath = NormalizeFolderPath(folderPath);

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            status = $"Invalid folder: {folderPath}";
            Repaint();
            return;
        }

        SavePersistedSettings();
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        var startedAt = DateTime.Now;

        try
        {
            for (var i = 0; i < prefabGuids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar(
                    WindowTitle,
                    assetPath,
                    prefabGuids.Length == 0 ? 1f : (float)i / prefabGuids.Length);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                    continue;

                var group = AnalyzePrefabRoot(prefab, assetPath, includeInactive);
                if (group.Issues.Count == 0)
                    continue;

                groups.Add(group);
                expandedPrefabs.Add(group.AssetPath);
            }

            var issueCount = groups.Sum(group => group.Issues.Count);
            var elapsed = DateTime.Now - startedAt;
            status = $"Scanned {prefabGuids.Length} prefab(s) in {folderPath}. Issues: {issueCount}. Time: {elapsed.TotalSeconds:0.0}s.";
        }
        catch (Exception e)
        {
            status = $"Scan failed: {e.Message}";
            ReanimatorLog.Exception(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private void SetFolderPath(string path)
    {
        folderPath = NormalizeFolderPath(path);
        SavePersistedSettingsIfFolderIsValid();
    }

    private void LoadPersistedSettings()
    {
        includeInactive = EditorPrefs.GetBool(IncludeInactivePrefsKey, includeInactive);
        var savedFolder = NormalizeFolderPath(EditorPrefs.GetString(FolderPathPrefsKey, folderPath));
        if (AssetDatabase.IsValidFolder(savedFolder))
        {
            folderPath = savedFolder;
            status = $"Folder: {folderPath}";
            return;
        }

        folderPath = AssetDatabase.IsValidFolder(folderPath) ? NormalizeFolderPath(folderPath) : "Assets";
    }

    private void SavePersistedSettingsIfFolderIsValid()
    {
        if (!AssetDatabase.IsValidFolder(NormalizeFolderPath(folderPath)))
            return;

        SavePersistedSettings();
    }

    private void SavePersistedSettings()
    {
        EditorPrefs.SetString(FolderPathPrefsKey, NormalizeFolderPath(folderPath));
        EditorPrefs.SetBool(IncludeInactivePrefsKey, includeInactive);
    }

    private static string NormalizeFolderPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Assets";

        return path.Replace('\\', '/').TrimEnd('/');
    }

    internal static PrefabIssueGroup AnalyzePrefabRoot(GameObject root, string assetPath, bool includeInactive)
    {
        var group = new PrefabIssueGroup(assetPath);
        if (root == null)
            return group;

        var spriteRenderers = root.GetComponentsInChildren<SpriteRenderer>(includeInactive);
        for (var i = 0; i < spriteRenderers.Length; i++)
            AddSpriteRendererIssues(group, root.transform, spriteRenderers[i]);

        var spriteSkins = root.GetComponentsInChildren<SpriteSkin>(includeInactive);
        for (var i = 0; i < spriteSkins.Length; i++)
            AddSpriteSkinIssues(group, root.transform, spriteSkins[i]);

        return group;
    }

    private static void AddSpriteRendererIssues(PrefabIssueGroup group, Transform root, SpriteRenderer spriteRenderer)
    {
        if (group == null || spriteRenderer == null)
            return;
        if (spriteRenderer.sprite != null)
            return;

        group.Issues.Add(new PrefabSpriteIssue(
            PrefabSpriteIssueKind.SpriteRendererMissingSprite,
            GetTransformPath(root, spriteRenderer.transform),
            "SpriteRenderer has no sprite."));
    }

    private static void AddSpriteSkinIssues(PrefabIssueGroup group, Transform root, SpriteSkin spriteSkin)
    {
        if (group == null || spriteSkin == null)
            return;

        var path = GetTransformPath(root, spriteSkin.transform);
        var spriteRenderer = spriteSkin.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinMissingSpriteRenderer,
                path,
                "SpriteSkin has no SpriteRenderer on the same GameObject."));
            return;
        }

        if (spriteRenderer.sprite == null)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinMissingSprite,
                path,
                "SpriteSkin renderer has no sprite."));
            return;
        }

        var spriteBones = GetSpriteBones(spriteRenderer.sprite);
        if (spriteBones.Length == 0)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinSpriteHasNoBones,
                path,
                "SpriteSkin sprite has no sprite bones."));
        }

        var serialized = new SerializedObject(spriteSkin);
        var rootBoneProperty = serialized.FindProperty("m_RootBone");
        if (rootBoneProperty != null && rootBoneProperty.objectReferenceValue == null)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinMissingRootBone,
                path,
                "SpriteSkin Root Bone is empty."));
        }

        var boneTransformsProperty = serialized.FindProperty("m_BoneTransforms");
        if (boneTransformsProperty == null || !boneTransformsProperty.isArray)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinInvalidBoneTransforms,
                path,
                "SpriteSkin bone transform array is unavailable."));
            return;
        }

        if (spriteBones.Length > 0 && boneTransformsProperty.arraySize != spriteBones.Length)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinBoneCountMismatch,
                path,
                $"SpriteSkin bone count mismatch: transforms={boneTransformsProperty.arraySize}, sprite bones={spriteBones.Length}."));
        }

        var missingIndexes = GetMissingBoneTransformIndexes(boneTransformsProperty);
        if (missingIndexes.Count > 0)
        {
            group.Issues.Add(new PrefabSpriteIssue(
                PrefabSpriteIssueKind.SpriteSkinMissingBoneTransforms,
                path,
                $"SpriteSkin has missing bone transforms at indexes: {string.Join(", ", missingIndexes)}."));
        }
    }

    private static SpriteBone[] GetSpriteBones(Sprite sprite)
    {
        if (sprite == null)
            return Array.Empty<SpriteBone>();

        try
        {
            return sprite.GetBones() ?? Array.Empty<SpriteBone>();
        }
        catch
        {
            return Array.Empty<SpriteBone>();
        }
    }

    private static List<int> GetMissingBoneTransformIndexes(SerializedProperty boneTransformsProperty)
    {
        var result = new List<int>();
        if (boneTransformsProperty == null || !boneTransformsProperty.isArray)
            return result;

        for (var i = 0; i < boneTransformsProperty.arraySize; i++)
        {
            var element = boneTransformsProperty.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == null)
                result.Add(i);
        }

        return result;
    }

    private static bool TryGetSelectedFolder(out string selectedFolder)
    {
        selectedFolder = string.Empty;
        var active = Selection.activeObject;
        if (active == null)
            return false;

        var path = AssetDatabase.GetAssetPath(active);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (AssetDatabase.IsValidFolder(path))
        {
            selectedFolder = path;
            return true;
        }

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && AssetDatabase.IsValidFolder(directory))
        {
            selectedFolder = directory.Replace('\\', '/');
            return true;
        }

        return false;
    }

    private static string GetTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return string.Empty;
        if (root == target)
            return root.name;

        var stack = new Stack<string>();
        var current = target;
        while (current != null)
        {
            stack.Push(current.name);
            if (current == root)
                break;

            current = current.parent;
        }

        return string.Join("/", stack);
    }

    internal static bool TryFindTransformByPath(Transform root, string objectPath, out Transform target)
    {
        target = null;
        if (root == null || string.IsNullOrWhiteSpace(objectPath))
            return false;

        var parts = objectPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;
        if (!string.Equals(parts[0], root.name, StringComparison.Ordinal))
            return false;

        target = root;
        for (var i = 1; i < parts.Length; i++)
        {
            var child = FindDirectChild(target, parts[i]);
            if (child == null)
            {
                target = null;
                return false;
            }

            target = child;
        }

        return target != null;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static GUIContent GetIssueIcon(PrefabSpriteIssueKind kind)
    {
        switch (kind)
        {
            case PrefabSpriteIssueKind.SpriteRendererMissingSprite:
            case PrefabSpriteIssueKind.SpriteSkinMissingSprite:
                return EditorGUIUtility.IconContent("console.warnicon");
            default:
                return EditorGUIUtility.IconContent("console.erroricon");
        }
    }

    private static void PingAsset(string assetPath)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null)
            return;

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static void OpenAsset(string assetPath)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset != null)
            AssetDatabase.OpenAsset(asset);
    }

    private static bool TryResolveOpenPrefabIssueTarget(string assetPath, string objectPath, out GameObject target)
    {
        target = null;
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || stage.prefabContentsRoot == null)
            return false;
        if (!IsSameAssetPath(stage.assetPath, assetPath))
            return false;
        if (!TryFindTransformByPath(stage.prefabContentsRoot.transform, objectPath, out var transform))
            return false;

        target = transform.gameObject;
        return target != null;
    }

    private static bool IsSameAssetPath(string lhs, string rhs)
    {
        return string.Equals(
            NormalizeAssetPath(lhs),
            NormalizeAssetPath(rhs),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static void PingGameObject(GameObject target)
    {
        if (target == null)
            return;

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }
}

internal sealed class PrefabIssueGroup
{
    public readonly string AssetPath;
    public readonly List<PrefabSpriteIssue> Issues = new List<PrefabSpriteIssue>();

    public PrefabIssueGroup(string assetPath)
    {
        AssetPath = assetPath ?? string.Empty;
    }

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AssetPath))
                return "<prefab>";
            return System.IO.Path.GetFileNameWithoutExtension(AssetPath);
        }
    }
}

internal readonly struct PrefabSpriteIssue
{
    public readonly PrefabSpriteIssueKind Kind;
    public readonly string ObjectPath;
    public readonly string Message;

    public PrefabSpriteIssue(PrefabSpriteIssueKind kind, string objectPath, string message)
    {
        Kind = kind;
        ObjectPath = objectPath ?? string.Empty;
        Message = message ?? string.Empty;
    }
}

internal enum PrefabSpriteIssueKind
{
    SpriteRendererMissingSprite,
    SpriteSkinMissingSpriteRenderer,
    SpriteSkinMissingSprite,
    SpriteSkinSpriteHasNoBones,
    SpriteSkinMissingRootBone,
    SpriteSkinInvalidBoneTransforms,
    SpriteSkinBoneCountMismatch,
    SpriteSkinMissingBoneTransforms
}
#endif
