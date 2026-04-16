using SoobakFigma2Unity.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Color
{
    internal static class ColorSpaceHelper
    {
        /// <summary>
        /// Convert a Figma color (always sRGB) to the correct Unity color
        /// based on the project's color space setting.
        /// </summary>
        public static UnityEngine.Color Convert(FigmaColor figmaColor)
        {
            if (figmaColor == null)
                return UnityEngine.Color.white;

            var color = figmaColor.ToUnityColor();
            return AdjustForColorSpace(color);
        }

        /// <summary>
        /// Convert a Figma color with additional opacity applied.
        /// </summary>
        public static UnityEngine.Color Convert(FigmaColor figmaColor, float opacity)
        {
            var color = Convert(figmaColor);
            color.a *= opacity;
            return color;
        }

        /// <summary>
        /// Adjust a color for the project's color space.
        /// Figma always outputs sRGB. In Linear color space, we need to convert.
        /// </summary>
        public static UnityEngine.Color AdjustForColorSpace(UnityEngine.Color srgbColor)
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                // Convert sRGB → linear for RGB, keep alpha as-is
                return new UnityEngine.Color(
                    Mathf.GammaToLinearSpace(srgbColor.r),
                    Mathf.GammaToLinearSpace(srgbColor.g),
                    Mathf.GammaToLinearSpace(srgbColor.b),
                    srgbColor.a
                );
            }
            return srgbColor;
        }
    }
}
