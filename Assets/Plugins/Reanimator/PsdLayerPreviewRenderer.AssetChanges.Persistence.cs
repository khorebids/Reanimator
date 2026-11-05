#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.U2D.Sprites;

public partial class PsdLayerPreviewRenderer
{
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
