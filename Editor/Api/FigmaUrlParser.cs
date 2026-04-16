using System.Text.RegularExpressions;

namespace SoobakFigma2Unity.Editor.Api
{
    public readonly struct FigmaUrlInfo
    {
        public readonly string FileKey;
        public readonly string NodeId;

        public FigmaUrlInfo(string fileKey, string nodeId = null)
        {
            FileKey = fileKey;
            NodeId = nodeId;
        }

        public bool IsValid => !string.IsNullOrEmpty(FileKey);
    }

    public static class FigmaUrlParser
    {
        // Matches:
        //   https://www.figma.com/file/FILEKEY/title
        //   https://www.figma.com/design/FILEKEY/title
        //   https://figma.com/file/FILEKEY/title?node-id=1-2
        //   https://www.figma.com/file/FILEKEY
        private static readonly Regex UrlPattern = new Regex(
            @"figma\.com/(?:file|design)/([a-zA-Z0-9]+)(?:/[^?]*)?(?:\?.*node-id=([^&]+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static FigmaUrlInfo Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return default;

            var match = UrlPattern.Match(url);
            if (!match.Success)
                return default;

            var fileKey = match.Groups[1].Value;
            var nodeId = match.Groups[2].Success
                ? System.Uri.UnescapeDataString(match.Groups[2].Value).Replace("-", ":")
                : null;

            return new FigmaUrlInfo(fileKey, nodeId);
        }
    }
}
