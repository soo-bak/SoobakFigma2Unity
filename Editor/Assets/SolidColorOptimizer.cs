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
    }
}
