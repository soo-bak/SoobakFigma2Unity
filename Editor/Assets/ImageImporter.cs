using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Imports downloaded images into the Unity project as Sprites
    /// with correct TextureImporter settings.
    /// </summary>
    internal static class ImageImporter
    {
        /// <summary>
        /// Import a PNG file as a Sprite asset with appropriate settings.
        /// </summary>
        /// <param name="absolutePath">Absolute path to the PNG file on disk.</param>
        /// <param name="assetPath">Unity-relative asset path (e.g. "Assets/UI/Images/node.png").</param>
        /// <param name="scale">The scale at which the image was exported (1x, 2x, etc.).</param>
        /// <returns>The imported Sprite, or null on failure.</returns>
        public static Sprite ImportAsSprite(string absolutePath, string assetPath, float scale = 2f)
        {
            // Ensure the file is in the Assets folder
            var fullAssetPath = Path.GetFullPath(assetPath);
            var dir = Path.GetDirectoryName(fullAssetPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Copy file to Assets if not already there
            if (Path.GetFullPath(absolutePath) != fullAssetPath)
                File.Copy(absolutePath, fullAssetPath, true);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // Configure TextureImporter
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f * scale;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;

            // Auto-detect max texture size based on image dimensions
            importer.maxTextureSize = DetermineMaxTextureSize(absolutePath);

            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>
        /// Set 9-slice borders on an already-imported sprite.
        /// </summary>
        public static void SetSliceBorders(string assetPath, Vector4 borders)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.spriteBorder = borders; // (left, bottom, right, top)
            importer.SaveAndReimport();
        }

        private static int DetermineMaxTextureSize(string path)
        {
            // Read image dimensions without fully loading
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                int maxDim = Mathf.Max(tex.width, tex.height);
                Object.DestroyImmediate(tex);

                if (maxDim <= 256) return 256;
                if (maxDim <= 512) return 512;
                if (maxDim <= 1024) return 1024;
                if (maxDim <= 2048) return 2048;
                return 4096;
            }
            catch
            {
                return 2048;
            }
        }
    }
}
