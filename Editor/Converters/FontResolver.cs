using System;
using System.Collections.Generic;
using System.Text;
using SoobakFigma2Unity.Editor.Models;
using TMPro;

namespace SoobakFigma2Unity.Editor.Converters
{
    internal static class FontResolver
    {
        public static TMP_FontAsset Resolve(FigmaTypeStyle style, IReadOnlyList<TMP_FontAsset> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return TMP_Settings.defaultFontAsset;

            var fallback = GetFirstValid(candidates) ?? TMP_Settings.defaultFontAsset;
            if (style == null)
                return fallback;

            var family = Normalize(style.FontFamily);
            var postScriptName = Normalize(style.FontPostScriptName);
            if (family.Length == 0 && postScriptName.Length == 0)
                return fallback;

            var best = fallback;
            var bestScore = int.MaxValue;
            foreach (var candidate in candidates)
            {
                if (candidate == null)
                    continue;

                var assetName = Normalize(candidate.name);
                var sourceName = Normalize(candidate.sourceFontFile != null ? candidate.sourceFontFile.name : "");
                var score = Math.Min(
                    Score(postScriptName, assetName, sourceName),
                    Score(family, assetName, sourceName));
                if (score >= bestScore)
                    continue;

                best = candidate;
                bestScore = score;
            }

            return best;
        }

        private static TMP_FontAsset GetFirstValid(IReadOnlyList<TMP_FontAsset> candidates)
        {
            foreach (var candidate in candidates)
                if (candidate != null)
                    return candidate;
            return null;
        }

        private static int Score(string target, string assetName, string sourceName)
        {
            if (target.Length == 0)
                return int.MaxValue;

            return Math.Min(Score(target, assetName), Score(target, sourceName));
        }

        private static int Score(string target, string candidate)
        {
            if (candidate.Length == 0)
                return int.MaxValue;
            if (target == candidate)
                return 0;
            if (target.Contains(candidate) || candidate.Contains(target))
                return Math.Abs(target.Length - candidate.Length) + 1;

            return LevenshteinDistance(target, candidate) + 10;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var result = new StringBuilder(value.Length);
            foreach (var c in value)
                if (char.IsLetterOrDigit(c))
                    result.Append(char.ToLowerInvariant(c));

            const string sdfSuffix = "sdf";
            if (result.Length > sdfSuffix.Length && result.ToString().EndsWith(sdfSuffix, StringComparison.Ordinal))
                result.Length -= sdfSuffix.Length;

            return result.ToString();
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];
            for (var j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= right.Length; j++)
                {
                    var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + substitutionCost);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[right.Length];
        }
    }
}
