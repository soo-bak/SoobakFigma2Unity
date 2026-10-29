using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SoobakFigma2Unity.Editor.Settings;
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
        public static void BuildProjectPrefabIndex(ImportProfile profile, ImportContext ctx)
        {
            ctx.ReusablePrefabPathsByName.Clear();
            ctx.ManagedPrefabPathsByComponentId.Clear();

            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                var manifest = prefab.GetComponent<FigmaPrefabManifest>();
                if (manifest != null && !string.IsNullOrEmpty(manifest.RootComponentId))
                {
                    if (!ctx.ManagedPrefabPathsByComponentId.TryGetValue(
                            manifest.RootComponentId, out var managedPaths))
                    {
                        managedPaths = new List<string>();
                        ctx.ManagedPrefabPathsByComponentId[manifest.RootComponentId] = managedPaths;
                    }
                    managedPaths.Add(path);
                    continue;
                }

                // Converter-managed component IDs are only unique inside their Figma
                // source file, so a project-wide ID match is unsafe. Those assets keep
                // using ComponentPrefabNamer.Resolve inside the configured output folder.

                // A screen prefab can share its filename with a nested Figma component.
                // Never turn the screen into an instance of itself. Other unmanaged
                // prefabs, including ones already inside ComponentOutputPath, are valid
                // designer-authored reuse candidates and must remain untouched.
                if (IsInDirectory(path, profile?.ScreenOutputPath))
                    continue;

                var name = SanitizeFileName(Path.GetFileNameWithoutExtension(path));
                if (!ctx.ReusablePrefabPathsByName.TryGetValue(name, out var matches))
                {
                    matches = new List<string>();
                    ctx.ReusablePrefabPathsByName[name] = matches;
                }
                matches.Add(path);
            }
        }

        public static List<string> BuildCandidateNames(
            string componentId,
            string fallbackName,
            ImportContext ctx)
        {
            var candidates = new List<string>();
            if (ctx.Components.TryGetValue(componentId, out var component))
            {
                if (!string.IsNullOrEmpty(component.ComponentSetId) &&
                    ctx.ComponentSets.TryGetValue(component.ComponentSetId, out var componentSet))
                {
                    AddCandidate(candidates, BuildVariantPrefabName(
                        componentSet.Name,
                        component.Name));
                }
                else
                {
                    AddCandidate(candidates, SanitizeFileName(component.Name));
                }
            }
            else
            {
                AddCandidate(candidates, SanitizeFileName(fallbackName));
            }
            return candidates;
        }

        public static bool TryResolveReusablePrefab(
            IReadOnlyList<string> candidates,
            ImportContext ctx,
            out string prefabPath,
            out string ambiguousName)
        {
            prefabPath = null;
            ambiguousName = null;
            foreach (var candidate in candidates)
            {
                if (!ctx.ReusablePrefabPathsByName.TryGetValue(candidate, out var matches))
                    continue;

                if (matches.Count == 1)
                {
                    prefabPath = matches[0];
                    return true;
                }

                if (matches.Count > 1)
                {
                    ambiguousName = candidate;
                    return false;
                }
            }
            return false;
        }

        public static bool TryResolveManagedPrefab(
            string componentId,
            IReadOnlyList<string> candidates,
            ImportContext ctx,
            out string prefabPath)
        {
            prefabPath = null;
            if (!ctx.ManagedPrefabPathsByComponentId.TryGetValue(componentId, out var paths))
                return false;

            var matches = paths.Where(path => candidates.Any(candidate =>
                    string.Equals(
                        SanitizeFileName(Path.GetFileNameWithoutExtension(path)),
                        candidate,
                        StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matches.Count != 1)
                return false;

            prefabPath = matches[0];
            return true;
        }

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

        /// <summary>
        /// Builds the stable prefab name for a COMPONENT_SET child.
        /// Every variant, including the first one, includes all of its property values:
        /// "Button" + "Style=Primary, State=Default" → "Button_Primary_Default".
        /// </summary>
        public static string BuildVariantPrefabName(string componentSetName, string variantName)
        {
            var baseName = SanitizeFileName(componentSetName);
            var variantValues = SanitizeFileName(variantName);
            return string.IsNullOrEmpty(variantValues)
                ? baseName
                : SanitizeFileName($"{baseName}_{variantValues}");
        }

        // Strip characters Unity / Windows reject in file names AND the Figma variant-
        // syntax characters that read as noise in a Unity Project window.
        //
        // Figma's variant component names look like "style=primary, size=XL, State=Default"
        // (key=value pairs, comma-separated). Saved as-is the prefab file ends up named
        // `style=primary, size=XL, State=Default.prefab` — visually unparseable in the
        // Project view and prone to collisions when only one property differs. We strip
        // any "<key>=" prefix from each comma-segment, leaving the values joined by
        // underscores: e.g. "primary_XL_Default". Designer-typed component names without
        // "=" pass through unchanged so we don't over-mangle conventional names.
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Component";

            // Variant-style flattening: "k=v, k=v" → "v_v".
            if (name.IndexOf('=') >= 0)
            {
                var parts = name.Split(',');
                var values = new System.Collections.Generic.List<string>(parts.Length);
                foreach (var part in parts)
                {
                    var eq = part.IndexOf('=');
                    var v = (eq >= 0 ? part.Substring(eq + 1) : part).Trim();
                    if (!string.IsNullOrEmpty(v)) values.Add(v);
                }
                if (values.Count > 0)
                    name = string.Join("_", values);
            }

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                bool ok = ch != '/' && ch != '\\' && ch != ':' && ch != '*' && ch != '?'
                          && ch != '"' && ch != '<' && ch != '>' && ch != '|'
                          && ch != '=' && ch != ',';
                sb.Append(ok ? ch : '_');
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? "Component" : s;
        }

        private static void AddCandidate(List<string> candidates, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            foreach (var existing in candidates)
            {
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            candidates.Add(candidate);
        }

        private static bool IsInDirectory(string assetPath, string directory)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(directory))
                return false;

            var normalizedPath = assetPath.Replace("\\", "/");
            var prefix = directory.Replace("\\", "/").TrimEnd('/') + "/";
            return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
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
