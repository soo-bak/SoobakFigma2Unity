using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Util;
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
        /// Returns true if a non-normal blend mode was applied.
        /// </summary>
        public static bool TryApply(Image image, string figmaBlendMode, ImportLogger logger)
        {
            if (image == null || string.IsNullOrEmpty(figmaBlendMode))
                return false;

            // Normal/PassThrough don't need special handling
            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return false;

            if (ShaderMap.TryGetValue(figmaBlendMode, out var shaderName))
            {
                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    image.material = new Material(shader);
                    logger?.Info($"{image.gameObject.name}: blend mode '{figmaBlendMode}' applied via shader");
                    return true;
                }
                else
                {
                    logger?.Warn($"{image.gameObject.name}: shader '{shaderName}' not found for blend mode '{figmaBlendMode}'");
                }
            }
            else
            {
                logger?.Warn($"{image.gameObject.name}: blend mode '{figmaBlendMode}' not supported — using Normal");
            }

            return false;
        }
    }
}
