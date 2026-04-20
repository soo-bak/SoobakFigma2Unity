using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Determines if a node can be represented with just a solid color
    /// instead of downloading and importing an image.
    /// </summary>
    internal static class SolidColorOptimizer
    {
        /// <summary>
        /// Returns true if the node only has solid color fills (no gradients, images, or complex effects)
        /// and can be rendered with a Unity Image component using the built-in white sprite + color tint.
        /// </summary>
        public static bool CanUseSolidColor(FigmaNode node)
        {
            // Vector types and boolean operations always need rasterization
            // (their shape can't be represented by a simple colored rectangle)
            var t = node.NodeType;
            if (t == FigmaNodeType.VECTOR || t == FigmaNodeType.ELLIPSE ||
                t == FigmaNodeType.STAR || t == FigmaNodeType.LINE ||
                t == FigmaNodeType.REGULAR_POLYGON || t == FigmaNodeType.BOOLEAN_OPERATION)
                return false;

            // Must have fills
            if (node.Fills == null || node.Fills.Count == 0)
                return false;

            // Check all visible fills are solid
            foreach (var fill in node.Fills)
            {
                if (!fill.Visible || fill.Opacity <= 0f)
                    continue;

                if (!fill.IsSolid)
                    return false;
            }

            // Must not have visible stroke that requires an image
            if (node.Strokes != null)
            {
                foreach (var stroke in node.Strokes)
                {
                    if (stroke.Visible && stroke.Opacity > 0f && !stroke.IsSolid)
                        return false;
                }
            }

            // Must not have effects that require rasterization
            if (node.Effects != null)
            {
                foreach (var effect in node.Effects)
                {
                    if (effect.Visible && (effect.EffectType == EffectType.DROP_SHADOW ||
                                           effect.EffectType == EffectType.INNER_SHADOW))
                        return false;
                }
            }

            // Strokes with non-zero weight also need rasterization
            if (node.StrokeWeight > 0 && node.Strokes != null && node.Strokes.Count > 0)
            {
                foreach (var stroke in node.Strokes)
                {
                    if (stroke.Visible && stroke.Opacity > 0f)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the topmost visible solid color from fills.
        /// Figma fills are bottom-to-top; the last visible fill is on top.
        /// </summary>
        public static FigmaColor GetTopSolidColor(FigmaNode node)
        {
            if (node.Fills == null)
                return null;

            FigmaColor topColor = null;
            foreach (var fill in node.Fills)
            {
                if (fill.Visible && fill.Opacity > 0f && fill.IsSolid && fill.Color != null)
                {
                    topColor = fill.Color;
                }
            }
            return topColor;
        }

        /// <summary>
        /// Get the topmost visible solid fill's color AND its per-fill opacity.
        /// Multiply the returned opacity with node.Opacity to get the final alpha.
        /// </summary>
        public static (FigmaColor color, float opacity) GetTopSolidFill(FigmaNode node)
        {
            if (node.Fills == null) return (null, 1f);

            FigmaColor topColor = null;
            float topOpacity = 1f;
            foreach (var fill in node.Fills)
            {
                if (fill.Visible && fill.Opacity > 0f && fill.IsSolid && fill.Color != null)
                {
                    topColor = fill.Color;
                    topOpacity = fill.Opacity;
                }
            }
            return (topColor, topOpacity);
        }
    }
}
