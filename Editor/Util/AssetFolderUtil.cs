using System.IO;
using UnityEditor;

namespace SoobakFigma2Unity.Editor.Util
{
    internal static class AssetFolderUtil
    {
        // Creating a folder via Directory.CreateDirectory leaves a metafile-less
        // folder on disk; the next AssetDatabase.CreateFolder then makes a numbered
        // duplicate ("Images 1") instead of registering the existing one. Always
        // route folder creation through AssetDatabase, and recover orphaned folders
        // from earlier buggy runs by re-importing rather than recreating them.
        public static void EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return;

            var parts = assetPath.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    if (Directory.Exists(next))
                    {
                        AssetDatabase.ImportAsset(current, ImportAssetOptions.ImportRecursive);
                        if (AssetDatabase.IsValidFolder(next))
                        {
                            current = next;
                            continue;
                        }
                    }
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
