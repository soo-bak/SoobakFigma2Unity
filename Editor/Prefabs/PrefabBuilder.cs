using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    internal static class PrefabBuilder
    {
        public static string SaveAsPrefab(GameObject root, string outputDir, string prefabName = null)
        {
            EnsureDirectoryExists(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        public static string SaveOrReplacePrefab(GameObject root, string outputDir, string prefabName = null)
        {
            EnsureDirectoryExists(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        /// <summary>
        /// Create the directory on disk AND register it with AssetDatabase
        /// so Unity recognizes it for asset operations.
        /// </summary>
        private static void EnsureDirectoryExists(string assetPath)
        {
            // Convert relative asset path to absolute
            var fullPath = Path.GetFullPath(assetPath);

            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            // Make sure AssetDatabase knows about it by creating folders via API
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                // Build folder hierarchy: "Assets/UI/Screens" → create "UI" under "Assets", then "Screens" under "Assets/UI"
                var parts = assetPath.Replace("\\", "/").Split('/');
                var current = parts[0]; // "Assets"
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim().Trim('.');
            if (string.IsNullOrEmpty(name))
                name = "Unnamed";
            return name;
        }
    }
}
