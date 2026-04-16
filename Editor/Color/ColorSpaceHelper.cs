using SoobakFigma2Unity.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Color
{
    internal static class ColorSpaceHelper
    {
        /// <summary>
        /// Convert a Figma color (always sRGB) to the correct Unity color.
        ///
        /// Unity UI components (Image.color, TMP.color) expect sRGB values
        /// regardless of the project's color space setting. Unity handles
        /// the sRGB→Linear conversion internally when rendering.
        /// So we pass through the sRGB values directly.
        /// </summary>
        public static UnityEngine.Color Convert(FigmaColor figmaColor)
        {
            if (figmaColor == null)
                return UnityEngine.Color.white;

            return figmaColor.ToUnityColor();
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
    }
}
