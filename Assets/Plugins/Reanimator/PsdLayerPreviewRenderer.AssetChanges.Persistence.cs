#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;

public partial class PsdLayerPreviewRenderer
{
    private const int MaxApplyBackups = 5;

    private static void SaveApplyBackup(string assetPath, byte[] originalMetadata)
    {
        var source = Path.GetFullPath(assetPath);
        var fileName = Path.GetFileName(source);
        // Unity ignores directories ending in '~', so the backups are never imported as assets.
        var directory = Path.Combine(Path.GetDirectoryName(source), fileName + ".backups~");
        Directory.CreateDirectory(directory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        var destination = Path.Combine(directory, "apply-" + timestamp + "-" + Guid.NewGuid().ToString("N") + ".zip");
        var pending = destination + ".tmp";
        try
        {
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var input = File.OpenRead(source))
                using (var entry = archive.CreateEntry(fileName).Open())
                    input.CopyTo(entry);
                using (var entry = archive.CreateEntry(fileName + ".meta").Open())
                    entry.Write(originalMetadata, 0, originalMetadata.Length);
            }

            File.Move(pending, destination);
            var obsolete = Directory.EnumerateFiles(directory, "apply-*.zip")
                .Where(path => !string.Equals(path, destination, StringComparison.Ordinal))
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Skip(MaxApplyBackups - 1)
                .ToArray();
            foreach (var path in obsolete)
                File.Delete(path);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }

    private static void ApplyImporterSerializedProperties(SerializedObject serializedObject, UnityEngine.Object importer)
    {
        if (serializedObject == null)
            return;

        serializedObject.ApplyModifiedProperties();
        if (importer != null)
            EditorUtility.SetDirty(importer);
    }

    private static void PersistImporterMetadataAndReimport(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        var originalVerifySavingAssets = EditorPrefs.GetBool("VerifySavingAssets", false);
        try
        {
            EditorPrefs.SetBool("VerifySavingAssets", false);
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                throw new System.InvalidOperationException("Asset importer disappeared before saving: " + assetPath);

            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        finally
        {
            EditorPrefs.SetBool("VerifySavingAssets", originalVerifySavingAssets);
        }
    }

    private static bool ApplyImmediateDataProviderChanges(
        ISpriteEditorDataProvider dataProvider,
        string operationName,
        out string error)
    {
        error = null;
        if (dataProvider == null)
        {
            error = $"{operationName} data provider is unavailable.";
            return false;
        }

        try
        {
            dataProvider.Apply();
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool CommitImmediateDataProviderOperation(
        string assetPath,
        ISpriteEditorDataProvider dataProvider,
        string operationName,
        out string error)
    {
        if (!ApplyImmediateDataProviderChanges(dataProvider, operationName, out error))
            return false;

        PersistImporterMetadataAndReimport(assetPath);
        return true;
    }
}
#endif
