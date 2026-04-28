using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// CPU-side compositor for "decorative + text" Figma containers (KeyHint: Ellipse +
    /// highlight + Y, etc.). Takes the individually-rendered PNGs of each decorative leaf
    /// and alpha-blends them in z-order onto a transparent canvas sized to the container's
    /// bounding box, then writes the result as a single PNG asset.
    ///
    /// Why this exists: rendering the parent container via Figma's /v1/images endpoint
    /// would include any TEXT descendants in the bake, and overlaying an editable TMP on
    /// top would leave the baked text peeking through if the user edits the string. The
    /// only way to ship a clean decorative composite + editable text overlay is to bake
    /// the decoratives ourselves from their per-leaf renders. Figma's compositing is plain
    /// src-over alpha blend for default NORMAL blend mode, which matches Unity's
    /// ImageConversion + Color32 lerp byte-for-byte at the pixel level.
    /// </summary>
    internal static class CompositeBuilder
    {
        /// <summary>
        /// Composes the decorative leaves of <paramref name="container"/> onto a single
        /// transparent canvas. Returns the absolute path of the saved PNG, or null if the
        /// container has no usable bounds or no leaves landed.
        /// </summary>
        public static string Compose(
            FigmaNode container,
            IReadOnlyList<FigmaNode> decorativeLeaves,
            IReadOnlyDictionary<string, string> downloadedPngPaths,
            IReadOnlyDictionary<string, ImportContext.RasterBoundsMode> rasterBoundsModes,
            float scale,
            string outputDir,
            ImportLogger logger)
        {
            if (container == null || container.AbsoluteBoundingBox == null) return null;
            if (decorativeLeaves == null || decorativeLeaves.Count == 0) return null;

            int width  = Mathf.Max(1, Mathf.RoundToInt(container.AbsoluteBoundingBox.Width  * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(container.AbsoluteBoundingBox.Height * scale));

            // Default Color32 is (0,0,0,0) — transparent black, exactly what we want as the
            // starting canvas. SetPixels32 writes the entire array in one go.
            var canvasPixels = new Color32[width * height];

            int blittedCount = 0;
            foreach (var leaf in decorativeLeaves)
            {
                if (leaf == null || leaf.AbsoluteBoundingBox == null) continue;
                if (!downloadedPngPaths.TryGetValue(leaf.Id, out var leafPath)) continue;
                if (string.IsNullOrEmpty(leafPath) || !File.Exists(leafPath)) continue;

                var boundsMode = ImportContext.RasterBoundsMode.AbsoluteBounds;
                rasterBoundsModes?.TryGetValue(leaf.Id, out boundsMode);
                if (BlitLeaf(leaf, container, leafPath, boundsMode, canvasPixels, width, height, scale))
                    blittedCount++;
            }

            if (blittedCount == 0)
            {
                logger?.Warn($"Composite of '{container.Name}' produced no layers — nothing saved.");
                return null;
            }

            Texture2D canvas = null;
            try
            {
                canvas = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                canvas.SetPixels32(canvasPixels);
                canvas.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                Directory.CreateDirectory(outputDir);
                var fileName = $"_composite_{SanitiseId(container.Id)}.png";
                var fullPath = Path.Combine(outputDir, fileName);
                File.WriteAllBytes(fullPath, canvas.EncodeToPNG());
                return fullPath;
            }
            finally
            {
                if (canvas != null) Object.DestroyImmediate(canvas);
            }
        }

        // Loads the leaf PNG, alpha-blends each of its pixels onto the canvas at the leaf's
        // offset within the container. Figma uses Y-down coordinates, Unity textures are
        // Y-up after ImageConversion's automatic flip — the offset math handles both ends.
        private static bool BlitLeaf(
            FigmaNode leaf, FigmaNode container, string leafPath,
            ImportContext.RasterBoundsMode boundsMode,
            Color32[] canvas, int canvasWidth, int canvasHeight, float scale)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(leafPath); }
            catch { return false; }

            Texture2D leafTex = null;
            try
            {
                leafTex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!ImageConversion.LoadImage(leafTex, bytes, markNonReadable: false))
                    return false;

                int leafW = leafTex.width;
                int leafH = leafTex.height;
                if (leafW <= 0 || leafH <= 0) return false;

                var leafPixels = leafTex.GetPixels32();

                var placementBounds = GetPlacementBounds(leaf, boundsMode);
                if (placementBounds == null) return false;

                // Leaf PNG's top-left in container-local Figma coords. AbsoluteBounds
                // exports match absoluteBoundingBox; RenderBounds exports include stroke /
                // effect bleed and must be placed by absoluteRenderBounds or they drift.
                float dxFigma = placementBounds.X - container.AbsoluteBoundingBox.X;
                float dyFigma = placementBounds.Y - container.AbsoluteBoundingBox.Y;

                // Convert to canvas pixel offset. Figma Y-down → texture row-from-bottom:
                //   canvasYFromBottom_of_leaf_top = canvasHeight - dyFigma*scale - leafH
                int offsetX = Mathf.RoundToInt(dxFigma * scale);
                int offsetY = canvasHeight - Mathf.RoundToInt(dyFigma * scale) - leafH;

                for (int ly = 0; ly < leafH; ly++)
                {
                    int dy = offsetY + ly;
                    if (dy < 0 || dy >= canvasHeight) continue;
                    int leafRowStart = ly * leafW;
                    int canvasRowStart = dy * canvasWidth;

                    for (int lx = 0; lx < leafW; lx++)
                    {
                        int dx = offsetX + lx;
                        if (dx < 0 || dx >= canvasWidth) continue;

                        var src = leafPixels[leafRowStart + lx];
                        if (src.a == 0) continue;

                        int idx = canvasRowStart + dx;
                        if (src.a == 255)
                        {
                            // Fully opaque — replace destination outright.
                            canvas[idx] = src;
                            continue;
                        }

                        var dst = canvas[idx];
                        // Standard src-over alpha blend, all in 8-bit space.
                        int aSrc = src.a;
                        int aDstScaled = dst.a * (255 - aSrc) / 255;
                        int aOut = aSrc + aDstScaled;
                        if (aOut <= 0) { canvas[idx] = new Color32(0, 0, 0, 0); continue; }

                        int r = (src.r * aSrc + dst.r * aDstScaled) / aOut;
                        int g = (src.g * aSrc + dst.g * aDstScaled) / aOut;
                        int b = (src.b * aSrc + dst.b * aDstScaled) / aOut;

                        canvas[idx] = new Color32(
                            (byte)Mathf.Clamp(r, 0, 255),
                            (byte)Mathf.Clamp(g, 0, 255),
                            (byte)Mathf.Clamp(b, 0, 255),
                            (byte)Mathf.Clamp(aOut, 0, 255));
                    }
                }
                return true;
            }
            finally
            {
                if (leafTex != null) Object.DestroyImmediate(leafTex);
            }
        }

        private static FigmaRectangle GetPlacementBounds(
            FigmaNode leaf,
            ImportContext.RasterBoundsMode boundsMode)
        {
            if (boundsMode == ImportContext.RasterBoundsMode.RenderBounds &&
                leaf.AbsoluteRenderBounds != null &&
                leaf.AbsoluteRenderBounds.Width > 0f &&
                leaf.AbsoluteRenderBounds.Height > 0f)
                return leaf.AbsoluteRenderBounds;

            return leaf.AbsoluteBoundingBox;
        }

        private static string SanitiseId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "container";
            var sb = new System.Text.StringBuilder(id.Length);
            foreach (var ch in id)
                sb.Append(ch == ':' || ch == '/' || ch == '\\' || ch == ';' ? '_' : ch);
            return sb.ToString();
        }
    }
}
