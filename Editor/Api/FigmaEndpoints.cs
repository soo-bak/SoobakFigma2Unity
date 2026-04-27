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

        public static string GetImages(string fileKey, IEnumerable<string> nodeIds, float scale = 2f, string format = "png")
        {
            var ids = string.Join(",", nodeIds);
            // CultureInfo.InvariantCulture: locales that format floats with a comma decimal
            // (de_DE, fr_FR, ko_KR's old config) would otherwise emit "scale=2,5" which
            // Figma rejects as a malformed query string.
            var scaleText = scale.ToString(CultureInfo.InvariantCulture);
            // use_absolute_bounds=true: by default Figma trims the rendered PNG to the
            // visible content. A 100×50 node whose visible area is only 80×40 (because of
            // padding, hidden children, or an offset stroke) ships back as an 80×40 image
            // and there's no way to position it back inside the original 100×50 RectTransform
            // without an offset table the Figma API doesn't expose. Forcing absolute bounds
            // makes Figma export at the bounding box dimensions — sprite size matches the
            // RectTransform exactly, no per-axis nudging needed downstream.
            return $"{BaseUrl}/images/{fileKey}?ids={System.Uri.EscapeDataString(ids)}&scale={scaleText}&format={format}&use_absolute_bounds=true";
        }

        public static string GetImageFills(string fileKey)
        {
            return $"{BaseUrl}/files/{fileKey}/images";
        }
    }
}
