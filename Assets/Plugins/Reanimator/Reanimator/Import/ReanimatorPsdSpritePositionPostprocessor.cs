#if UNITY_EDITOR
using System;
using UnityEditor;

internal sealed class ReanimatorPsdSpritePositionPostprocessor : AssetPostprocessor
{
    private sealed class SuppressionScope : IDisposable
    {
        private bool disposed;

        public SuppressionScope()
        {
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
        }
    }

    public static IDisposable SuppressForAsset(string assetPath)
    {
        return new SuppressionScope();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // Reanimator writes PSD sprite metadata only from explicit editor actions.
        // Passive import callbacks can run after Unity updates SourceAssetDB and
        // must not rewrite .meta from stale CharacterData.
    }

}
#endif
