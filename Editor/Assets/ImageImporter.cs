using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Imports downloaded images into the Unity project as Sprites
    /// with correct TextureImporter settings.
    /// Uses batch operations to minimize asset pipeline overhead.
    /// </summary>
    internal static class ImageImporter
    {
        /// <summary>
        /// Copy a downloaded image to the Assets folder without triggering import yet.
        /// Call FinalizeBatchImport after all files are copied.
        /// </summary>
        public static void CopyToAssets(string absolutePath, string assetPath)
        {
            var fullAssetPath = Path.GetFullPath(assetPath);
            var dir = Path.GetDirectoryName(fullAssetPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (Path.GetFullPath(absolutePath) != fullAssetPath)
                File.Copy(absolutePath, fullAssetPath, true);
        }

        /// <summary>
        /// Batch import: configure all TextureImporters at once, then reimport once.
        /// Much faster than importing one by one.
        /// </summary>
        public static Dictionary<string, Sprite> BatchImport(
            IReadOnlyList<string> assetPaths,
            float scale = 2f)
        {
            var result = new Dictionary<string, Sprite>();

            if (assetPaths.Count == 0)
                return result;

            // First pass: import all assets so TextureImporter becomes available
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var assetPath in assetPaths)
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Second pass: configure all importers
            var pathsToReimport = new List<string>();
            foreach (var assetPath in assetPaths)
            {
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;

                bool needsChange = false;

                if (importer.textureType != TextureImporterType.Sprite)
                { importer.textureType = TextureImporterType.Sprite; needsChange = true; }

                if (importer.spriteImportMode != SpriteImportMode.Single)
                { importer.spriteImportMode = SpriteImportMode.Single; needsChange = true; }

                float targetPPU = 100f * scale;
                if (!Mathf.Approximately(importer.spritePixelsPerUnit, targetPPU))
                { importer.spritePixelsPerUnit = targetPPU; needsChange = true; }

                if (importer.mipmapEnabled)
                { importer.mipmapEnabled = false; needsChange = true; }

                if (importer.filterMode != FilterMode.Bilinear)
                { importer.filterMode = FilterMode.Bilinear; needsChange = true; }

                if (!importer.alphaIsTransparency)
                { importer.alphaIsTransparency = true; needsChange = true; }

                if (importer.isReadable)
                { importer.isReadable = false; needsChange = true; }

                importer.textureCompression = TextureImporterCompression.Compressed;

                if (needsChange)
                {
                    importer.SaveAndReimport();
                    pathsToReimport.Add(assetPath);
                }
            }

            // Load sprites
            foreach (var assetPath in assetPaths)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                    result[assetPath] = sprite;
            }

            return result;
        }

        /// <summary>
        /// Import a single PNG file as a Sprite (legacy, for small operations).
        /// </summary>
        public static Sprite ImportAsSprite(string absolutePath, string assetPath, float scale = 2f)
        {
            CopyToAssets(absolutePath, assetPath);
            var batch = BatchImport(new[] { assetPath }, scale);
            return batch.TryGetValue(assetPath, out var sprite) ? sprite : null;
        }

        /// <summary>
        /// Set 9-slice borders on an already-imported sprite.
        /// </summary>
        public static void SetSliceBorders(string assetPath, Vector4 borders)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.spriteBorder = borders;
            importer.SaveAndReimport();
        }
    }
}
