using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Resolves the one rendering case UGUI cannot do on its own: Figma's chroma-space
    /// blend modes (COLOR, HUE, SATURATION, DARKEN, LIGHTEN). Those blends need access
    /// to the destination pixel, which the default UGUI material doesn't expose, and the
    /// user ruled out custom shaders / materials.
    ///
    /// The workaround: Figma <i>does</i> bake the composite when it renders the parent
    /// frame via <c>/v1/images</c>. So for every chroma-blend child, we ensure the parent
    /// got rendered, then crop the parent's PNG down to the child's bounding-box area and
    /// re-save as the child's own PNG. The child's Image then just shows that pre-baked
    /// composite — no shaders, perfect visual match with Figma.
    /// </summary>
    internal static class CompositeCropService
    {
        public static void CropAll(ImportContext ctx, string tempDir, float scale, ImportLogger logger)
        {
            var parentDiscardCandidates = new HashSet<string>(ctx.CompositeCropParents);

            foreach (var kv in ctx.CompositeCropMap)
            {
                var childId = kv.Key;
                var parentId = kv.Value;

                if (!ctx.NodeImagePaths.TryGetValue(parentId, out var parentPath))
                {
                    logger?.Warn($"Composite crop: parent '{parentId}' has no downloaded image, skipping child '{childId}'.");
                    continue;
                }

                if (!ctx.NodeIndex.TryGetValue(childId, out var child) ||
                    !ctx.NodeIndex.TryGetValue(parentId, out var parent))
                {
                    logger?.Warn($"Composite crop: node index missing for '{childId}' or '{parentId}', skipping.");
                    continue;
                }

                var childPath = Path.Combine(tempDir, SanitizeId(childId) + "_composite.png");
                if (TryCrop(parentPath, childPath, parent, child, scale, logger))
                {
                    ctx.NodeImagePaths[childId] = childPath;
                    logger?.Info($"Composite crop: {child.Name} cropped from parent {parent.Name}");
                    // The parent's PNG was actually useful — its own converter might want it,
                    // or another crop still needs it. Keep it around for now; purge below.
                }
                else
                {
                    logger?.Warn($"Composite crop failed for {child.Name}; falling back to BlendModeHelper approximation.");
                }
            }

            // Parents that were ONLY downloaded for cropping purposes (not in NodesToRasterize)
            // must not become their own flat sprites — otherwise their converters would
            // replace the whole hierarchy with a rasterized image. Strip them from the
            // import list.
            foreach (var parentId in parentDiscardCandidates)
            {
                if (ctx.NodesToRasterize.Contains(parentId)) continue;
                if (ctx.NodeImagePaths.Remove(parentId))
                {
                    // Leave the temp file alone — it's in a temp dir and gets cleared with the pipeline.
                }
            }
        }

        private static bool TryCrop(string parentPngPath, string outputPath, FigmaNode parent, FigmaNode child, float scale, ImportLogger logger)
        {
            Texture2D src = null;
            Texture2D dst = null;
            try
            {
                if (!File.Exists(parentPngPath)) return false;
                var bytes = File.ReadAllBytes(parentPngPath);
                src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(src, bytes, markNonReadable: false))
                    return false;

                // Work out which parent rectangle the PNG spans. Figma's /v1/images returns
                // the node at its render bounds (includes effect halo), so we prefer those.
                var refRect = parent.AbsoluteRenderBounds ?? parent.AbsoluteBoundingBox;
                var childBox = child.AbsoluteBoundingBox ?? child.AbsoluteRenderBounds;
                if (refRect == null || childBox == null) return false;

                // The PNG may have a different pixel scale per-axis because Figma pads
                // render bounds differently in width and height. Derive scale from the
                // actual PNG dimensions so the crop lands where the child actually is.
                float scaleX = src.width / refRect.Width;
                float scaleY = src.height / refRect.Height;

                int cropX = Mathf.RoundToInt((childBox.X - refRect.X) * scaleX);
                int cropY = Mathf.RoundToInt((childBox.Y - refRect.Y) * scaleY);
                int cropW = Mathf.Max(1, Mathf.RoundToInt(childBox.Width * scaleX));
                int cropH = Mathf.Max(1, Mathf.RoundToInt(childBox.Height * scaleY));

                // Clamp to the PNG so we never sample off the texture.
                cropX = Mathf.Clamp(cropX, 0, src.width - 1);
                cropY = Mathf.Clamp(cropY, 0, src.height - 1);
                cropW = Mathf.Min(cropW, src.width - cropX);
                cropH = Mathf.Min(cropH, src.height - cropY);

                // Unity Texture2D has its origin at the bottom-left; Figma coordinates have
                // Y going downward. Flip the Y so "cropY from top" becomes "cropY from bottom".
                int pixelY = src.height - cropY - cropH;
                pixelY = Mathf.Clamp(pixelY, 0, src.height - cropH);

                var pixels = src.GetPixels(cropX, pixelY, cropW, cropH);

                dst = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false, true);
                dst.SetPixels(pixels);
                dst.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
                File.WriteAllBytes(outputPath, dst.EncodeToPNG());
                return true;
            }
            catch (System.Exception e)
            {
                logger?.Warn($"Composite crop exception: {e.Message}");
                return false;
            }
            finally
            {
                if (src != null) Object.DestroyImmediate(src);
                if (dst != null) Object.DestroyImmediate(dst);
            }
        }

        private static string SanitizeId(string id)
        {
            // Figma node IDs use ':' and ';' which Windows doesn't like in filenames.
            var sb = new System.Text.StringBuilder(id.Length);
            foreach (var c in id)
                sb.Append(c == ':' || c == ';' ? '_' : c);
            return sb.ToString();
        }
    }
}
