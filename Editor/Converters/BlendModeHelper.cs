using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Applies Figma blend modes to Unity UI Image components
    /// by assigning the appropriate custom shader material.
    /// </summary>
    internal static class BlendModeHelper
    {
        private static readonly Dictionary<string, string> ShaderMap = new Dictionary<string, string>
        {
            { "MULTIPLY", "SoobakFigma2Unity/UI/Multiply" },
            { "SCREEN", "SoobakFigma2Unity/UI/Screen" },
            { "OVERLAY", "SoobakFigma2Unity/UI/Overlay" },
        };

        // Blend modes whose result depends on the destination's HSL (chroma/luma
        // redistribution). UGUI's default material can't reach the destination pixel,
        // so when the source is a neutral gray — the common "desaturate overlay" case
        // — the least-wrong rendering is to skip drawing the source at all. That lets
        // the underlying graphic show through instead of being covered by an opaque
        // gray block. Colored sources still get a semi-transparent fallback.
        private static readonly HashSet<string> ChromaBlendModes =
            new HashSet<string> { "COLOR", "HUE", "SATURATION", "DARKEN", "LIGHTEN" };

        /// <summary>
        /// Apply blend mode material to an Image component if needed.
        /// LUMINOSITY is approximated by desaturating image.color (Rec.601 luma);
        /// when a sprite is present, the gray tint multiplies it to a desaturated look.
        /// Returns true if a non-normal blend mode was applied.
        /// </summary>
        public static bool TryApply(Image image, string figmaBlendMode, ImportLogger logger)
        {
            if (image == null || string.IsNullOrEmpty(figmaBlendMode))
                return false;

            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return false;

            if (figmaBlendMode == "LUMINOSITY")
            {
                image.color = ApproximateLuminosity(image.color);
                return true;
            }

            if (ShaderMap.TryGetValue(figmaBlendMode, out var shaderName))
            {
                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    image.material = new Material(shader);
                    logger?.Info($"{image.gameObject.name}: blend mode '{figmaBlendMode}' applied via shader");
                    return true;
                }
                logger?.Warn($"{image.gameObject.name}: shader '{shaderName}' not found for blend mode '{figmaBlendMode}'");
                return false;
            }

            if (ChromaBlendModes.Contains(figmaBlendMode))
            {
                // Ideal case — CompositeCropService already baked the blend into this
                // sprite by cropping it out of the parent's Figma render. Just render it.
                if (image.sprite != null)
                {
                    logger?.Info($"{image.gameObject.name}: '{figmaBlendMode}' sourced from composite crop (parent render baked the blend)");
                    return true;
                }

                // Fallback — no composite sprite available (parent render failed, or
                // the node has no parent in our index). UGUI can't compute the blend
                // itself, so approximate to avoid covering the destination.
                if (IsApproximatelyGrayscale(image.color))
                {
                    image.color = new UnityEngine.Color(image.color.r, image.color.g, image.color.b, 0f);
                    logger?.Warn($"{image.gameObject.name}: '{figmaBlendMode}' with gray source — no composite crop available, hidden as fallback");
                    return true;
                }
                var c = image.color;
                image.color = new UnityEngine.Color(c.r, c.g, c.b, c.a * 0.35f);
                logger?.Warn($"{image.gameObject.name}: '{figmaBlendMode}' with colored source — no composite crop, 35% alpha fallback");
                return true;
            }

            logger?.Warn($"{image.gameObject.name}: blend mode '{figmaBlendMode}' not supported — using Normal");
            return false;
        }

        private static bool IsApproximatelyGrayscale(UnityEngine.Color c)
        {
            const float tol = 0.04f; // ≈ 10 / 255
            return Mathf.Abs(c.r - c.g) < tol
                && Mathf.Abs(c.g - c.b) < tol
                && Mathf.Abs(c.r - c.b) < tol;
        }

        /// <summary>
        /// Apply blend mode to a TMP text. Currently only LUMINOSITY is approximated
        /// (text color desaturated to its Rec.601 luma); shader-based modes are not
        /// supported on TMP and warn instead.
        /// </summary>
        public static bool TryApplyToText(TMP_Text text, string figmaBlendMode, ImportLogger logger)
        {
            if (text == null || string.IsNullOrEmpty(figmaBlendMode))
                return false;

            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return false;

            if (figmaBlendMode == "LUMINOSITY")
            {
                text.color = ApproximateLuminosity(text.color);
                return true;
            }

            if (ChromaBlendModes.Contains(figmaBlendMode))
            {
                if (IsApproximatelyGrayscale(text.color))
                {
                    text.color = new UnityEngine.Color(text.color.r, text.color.g, text.color.b, 0f);
                    logger?.Info($"{text.gameObject.name}: '{figmaBlendMode}' with gray source on text — hidden");
                    return true;
                }
                var c = text.color;
                text.color = new UnityEngine.Color(c.r, c.g, c.b, c.a * 0.35f);
                logger?.Warn($"{text.gameObject.name}: '{figmaBlendMode}' with colored source on text — rendered as 35% alpha");
                return true;
            }

            logger?.Warn($"{text.gameObject.name}: blend mode '{figmaBlendMode}' not supported on text — using Normal");
            return false;
        }

        /// <summary>
        /// For a wrapper FRAME/INSTANCE that has a blend mode but no own Graphic,
        /// approximate the blend by walking descendants. Currently only LUMINOSITY
        /// cascades (desaturate every Image and TMP_Text underneath).
        /// </summary>
        public static void PropagateApproximationToDescendants(GameObject root, string figmaBlendMode, ImportLogger logger)
        {
            if (root == null || string.IsNullOrEmpty(figmaBlendMode))
                return;
            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return;

            if (figmaBlendMode == "LUMINOSITY")
            {
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                    img.color = ApproximateLuminosity(img.color);
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                    tmp.color = ApproximateLuminosity(tmp.color);
                return;
            }

            logger?.Warn($"{root.name}: wrapper blend mode '{figmaBlendMode}' cannot cascade to descendants — ignored");
        }

        private static UnityEngine.Color ApproximateLuminosity(UnityEngine.Color c)
        {
            // Rec.601 luma — same weighting Figma uses for color → grayscale.
            float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return new UnityEngine.Color(l, l, l, c.a);
        }
    }
}
