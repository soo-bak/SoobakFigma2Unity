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
            var scaleText = scale.ToString(CultureInfo.InvariantCulture);
            var boundsText = useAbsoluteBounds ? "true" : "false";
            return $"{BaseUrl}/images/{fileKey}?ids={System.Uri.EscapeDataString(ids)}&scale={scaleText}&format={format}&use_absolute_bounds={boundsText}";
        }

        public static string GetImageFills(string fileKey)
        {
            return $"{BaseUrl}/files/{fileKey}/images";
        }
    }
}
