using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    /// <summary>
    /// Creates and saves prefabs from generated GameObjects.
    /// </summary>
    internal static class PrefabBuilder
    {
        /// <summary>
        /// Save a root GameObject as a prefab asset.
        /// Returns the path to the saved prefab.
        /// </summary>
        public static string SaveAsPrefab(GameObject root, string outputDir, string prefabName = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        /// <summary>
        /// Save a root GameObject as a prefab, replacing an existing prefab if it exists.
        /// </summary>
        public static string SaveOrReplacePrefab(GameObject root, string outputDir, string prefabName = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");

            if (File.Exists(path))
            {
                // Replace existing prefab
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existingPrefab != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    return path;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            // Remove leading/trailing whitespace and dots
            name = name.Trim().Trim('.');
            if (string.IsNullOrEmpty(name))
                name = "Unnamed";
            return name;
        }
    }
}
