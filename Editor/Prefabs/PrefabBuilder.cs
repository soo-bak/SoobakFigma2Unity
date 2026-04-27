using System.IO;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    internal static class PrefabBuilder
    {
        public static string SaveAsPrefab(GameObject root, string outputDir, string prefabName = null)
        {
            AssetFolderUtil.EnsureFolder(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        public static string SaveOrReplacePrefab(GameObject root, string outputDir, string prefabName = null)
        {
            AssetFolderUtil.EnsureFolder(outputDir);

            var name = SanitizeName(prefabName ?? root.name);
            var path = Path.Combine(outputDir, $"{name}.prefab");

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return path;
        }

        /// <summary>
        /// Saves (or replaces) a prefab after attaching a FigmaPrefabManifest populated from
        /// <paramref name="ctx"/>. Use this for every Figma-originated prefab so non-destructive
        /// re-import has the identity data it needs.
        /// </summary>
        public static string SaveOrReplacePrefabWithManifest(
            GameObject root, string outputDir, string prefabName, ImportContext ctx)
        {
            ManifestBuilder.AttachRootManifest(root, ctx);
            return SaveOrReplacePrefab(root, outputDir, prefabName);
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
