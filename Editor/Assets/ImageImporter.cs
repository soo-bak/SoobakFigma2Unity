using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Util;
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
            var assetDir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            AssetFolderUtil.EnsureFolder(assetDir);

            var fullAssetPath = Path.GetFullPath(assetPath);
            if (Path.GetFullPath(absolutePath) != fullAssetPath)
                File.Copy(absolutePath, fullAssetPath, true);
        }

        // Linear color space compensates low-alpha overlays less aggressively than
        // Figma's sRGB-space compositing. These constants are a deliberate heuristic:
        // below LowAlphaThreshold we multiply alpha by LowAlphaBoostFactor so a blurred
        // 8% tint that Figma shows as visible-but-subtle actually reads as such in a
        // Linear project. Anything at or above the threshold is left alone so fully
        // opaque sprites aren't pushed past 1.0.
        private const byte LowAlphaThreshold = 128;       // 0.5 (byte)
        private const float LowAlphaBoostFactor = 1.4f;   // empirical sweet-spot

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

            // In Linear projects, pre-boost the alpha of low-alpha PNGs so Unity's
            // Linear-space alpha blend reads closer to Figma's sRGB-space compositing.
            // Modifies the file on disk before import so the texture Unity samples
            // already has the compensated alpha channel.
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                foreach (var assetPath in assetPaths)
                {
                    var fullPath = Path.GetFullPath(assetPath);
                    if (File.Exists(fullPath))
                        TryBoostLowAlpha(fullPath);
                }
            }

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

        /// <summary>
        /// Loads a PNG, and if its peak alpha is at-or-below <see cref="LowAlphaThreshold"/>,
        /// multiplies every pixel's alpha by <see cref="LowAlphaBoostFactor"/> (clamped to 255)
        /// and rewrites the file. Otherwise leaves the file untouched. Silent no-op on any error.
        /// </summary>
        private static void TryBoostLowAlpha(string fullPath)
        {
            Texture2D tex = null;
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(tex, bytes, markNonReadable: false))
                    return;

                var pixels = tex.GetPixels32();

                byte maxAlpha = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var a = pixels[i].a;
                    if (a > maxAlpha) { maxAlpha = a; if (maxAlpha > LowAlphaThreshold) break; }
                }
                if (maxAlpha == 0 || maxAlpha > LowAlphaThreshold)
                    return; // fully transparent or already opaque enough — nothing to do

                for (int i = 0; i < pixels.Length; i++)
                {
                    int boosted = Mathf.RoundToInt(pixels[i].a * LowAlphaBoostFactor);
                    pixels[i].a = (byte)Mathf.Clamp(boosted, 0, 255);
                }
                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            }
            catch
            {
                // Best-effort optimisation; ignore failures so the normal import still runs.
            }
            finally
            {
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }
    }
}
