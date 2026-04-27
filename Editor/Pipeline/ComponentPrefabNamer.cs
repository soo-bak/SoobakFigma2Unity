using System.IO;
using System.Text;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Resolves the on-disk path for an extracted component prefab.
    ///
    /// Default path is "<output>/<sanitized name>.prefab". If a file already lives there,
    /// we read its FigmaPrefabManifest.RootComponentId and either:
    ///   - Reuse the same path (same componentId — this IS our component, just SmartMerge it).
    ///   - Append "_<componentId8>" suffix so we don't clobber an unrelated prefab that
    ///     happens to share the name (designer-made `Card.prefab` already at that path,
    ///     incoming Figma "Card" component has a different componentId).
    /// </summary>
    internal static class ComponentPrefabNamer
    {
        /// <summary>Resolve where to write (or merge into) the prefab for <paramref name="componentId"/>.</summary>
        public static string Resolve(string componentId, string componentName, string outputDir)
        {
            var sanitized = SanitizeFileName(componentName);
            var candidate = $"{outputDir}/{sanitized}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
            if (existing == null) return candidate;

            var manifest = existing.GetComponent<FigmaPrefabManifest>();
            if (manifest != null && manifest.RootComponentId == componentId)
                return candidate; // same component — re-import target is the same file.

            // Different componentId (or unmanaged designer prefab). Pick a non-clobbering name.
            var shortId = ShortenComponentId(componentId);
            return $"{outputDir}/{sanitized}_{shortId}.prefab";
        }

        // Strip characters Unity / Windows reject in file names. Mirrors the rule
        // PrefabBuilder uses elsewhere so screen and component naming stay consistent.
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Component";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                bool ok = ch != '/' && ch != '\\' && ch != ':' && ch != '*' && ch != '?'
                          && ch != '"' && ch != '<' && ch != '>' && ch != '|';
                sb.Append(ok ? ch : '_');
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "Component" : s;
        }

        // Figma componentIds look like "1234:5678". Hex-friendly compaction so the
        // suffix stays short but stable across re-imports.
        private static string ShortenComponentId(string componentId)
        {
            if (string.IsNullOrEmpty(componentId)) return "x";
            var hash = (uint)componentId.GetHashCode();
            return hash.ToString("x8");
        }
    }
}
