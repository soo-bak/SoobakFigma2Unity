using System.IO;
using System.Text;

namespace SoobakFigma2Unity.Editor.Util
{
    /// <summary>
    /// File name sanitization shared by the image downloader and the asset-name allocator.
    /// Removes filesystem-invalid characters, collapses whitespace into underscores,
    /// squashes runs of underscores, and falls back to a deterministic placeholder
    /// when the input is empty or sanitizes to nothing.
    /// </summary>
    internal static class FileNameSanitizer
    {
        /// <summary>
        /// Sanitize an arbitrary string into something safe to use as a single filename
        /// segment (no extension, no path separators). Returns <paramref name="fallback"/>
        /// when the result is empty.
        /// </summary>
        public static string Sanitize(string raw, string fallback = "node")
        {
            if (string.IsNullOrEmpty(raw))
                return fallback;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            bool lastWasUnderscore = false;
            foreach (var ch in raw)
            {
                char c = ch;
                bool replace = false;
                if (c == ':' || c == '/' || c == '\\' || char.IsWhiteSpace(c))
                {
                    replace = true;
                }
                else
                {
                    foreach (var bad in invalid)
                    {
                        if (c == bad) { replace = true; break; }
                    }
                }

                if (replace)
                {
                    if (lastWasUnderscore) continue;
                    sb.Append('_');
                    lastWasUnderscore = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasUnderscore = false;
                }
            }

            // Strip leading/trailing underscores and dots — leading dots create hidden
            // files on POSIX, trailing dots/spaces are illegal on Windows.
            var result = sb.ToString().Trim('_', '.', ' ');
            return string.IsNullOrEmpty(result) ? fallback : result;
        }
    }
}
