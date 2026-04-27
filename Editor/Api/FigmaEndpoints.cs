using System.Collections.Generic;
using System.Globalization;

namespace SoobakFigma2Unity.Editor.Api
{
    internal static class FigmaEndpoints
    {
        private const string BaseUrl = "https://api.figma.com/v1";

        public static string GetFile(string fileKey, int? depth = null)
        {
            var url = $"{BaseUrl}/files/{fileKey}";
            if (depth.HasValue)
                url += $"?depth={depth.Value}";
            return url;
        }

        public static string GetFileNodes(string fileKey, IEnumerable<string> nodeIds)
        {
            var ids = string.Join(",", nodeIds);
            return $"{BaseUrl}/files/{fileKey}/nodes?ids={System.Uri.EscapeDataString(ids)}";
        }

        public static string GetImages(
            string fileKey,
            IEnumerable<string> nodeIds,
            float scale = 2f,
            string format = "png",
            bool useAbsoluteBounds = true)
        {
            var ids = string.Join(",", nodeIds);
            // CultureInfo.InvariantCulture: locales that format floats with a comma decimal
            // (de_DE, fr_FR, ko_KR's old config) would otherwise emit "scale=2,5" which
            // Figma rejects as a malformed query string.
            var scaleText = scale.ToString(CultureInfo.InvariantCulture);
            // use_absolute_bounds:
            //   true  → exports at the absoluteBoundingBox extent. Sprite size matches
            //           the RectTransform but any outside stroke / overflowing shadow gets
            //           clipped. Default for nodes whose visual stays inside the layout box.
            //   false → exports at the actual render area (path + stroke + effects). Sprite
            //           is slightly larger than the RectTransform; UGUI Image squishes it
            //           to fit. Used for nodes with strokeAlign != INSIDE so the outline
            //           survives instead of being trimmed off.
            var bounds = useAbsoluteBounds ? "true" : "false";
            return $"{BaseUrl}/images/{fileKey}?ids={System.Uri.EscapeDataString(ids)}&scale={scaleText}&format={format}&use_absolute_bounds={bounds}";
        }

        public static string GetImageFills(string fileKey)
        {
            return $"{BaseUrl}/files/{fileKey}/images";
        }
    }
}
