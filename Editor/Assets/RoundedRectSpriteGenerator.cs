using System.IO;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    // Generates and caches rounded-rectangle sprites for wrapper nodes
    // (FRAME / INSTANCE / COMPONENT / GROUP) that have cornerRadius + a
    // solid fill. Unity's UGUI Image cannot draw rounded corners natively,
    // and rasterizing the wrapper through Figma's image API would bake child
    // sprites/text into the texture and destroy the editable hierarchy.
    // A reusable 9-sliceable sprite (white, alpha-shaped) lets Image.color
    // handle the tint while sliced borders preserve the corner radius at
    // any RectTransform size.
    internal static class RoundedRectSpriteGenerator
    {
        public static Sprite GetOrGenerate(float cornerRadius, float scale, string outputDir, ImportLogger logger)
        {
            if (cornerRadius <= 0f || string.IsNullOrEmpty(outputDir))
                return null;

            int radiusPx = Mathf.Max(1, Mathf.RoundToInt(cornerRadius * scale));
            // 4px center buffer keeps the sliced inner region non-degenerate.
            int size = radiusPx * 2 + 4;

            string assetPath = $"{outputDir}/_rounded_{radiusPx}.png".Replace("\\", "/");

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null)
                return existing;

            AssetFolderUtil.EnsureFolder(outputDir);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = ComputeAlpha(x, y, size, radiusPx);
                    byte ab = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, ab);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var pngBytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(Path.GetFullPath(assetPath), pngBytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteMeshType = SpriteMeshType.FullRect;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = 100f * scale;
                importer.spriteBorder = new Vector4(radiusPx, radiusPx, radiusPx, radiusPx);
                importer.SaveAndReimport();
            }

            logger?.Info($"Generated rounded sprite: {assetPath} (r={radiusPx}px)");
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static float ComputeAlpha(int x, int y, int size, int radius)
        {
            int r = radius;
            int cx, cy;

            if (x < r && y < r) { cx = r; cy = r; }
            else if (x >= size - r && y < r) { cx = size - r - 1; cy = r; }
            else if (x < r && y >= size - r) { cx = r; cy = size - r - 1; }
            else if (x >= size - r && y >= size - r) { cx = size - r - 1; cy = size - r - 1; }
            else return 1f;

            float dx = x - cx;
            float dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - dist + 0.5f);
        }
    }
}
