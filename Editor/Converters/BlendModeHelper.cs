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
            }
            else
            {
                logger?.Warn($"{image.gameObject.name}: blend mode '{figmaBlendMode}' not supported — using Normal");
            }

            return false;
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
