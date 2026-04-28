using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoobakFigma2Unity.Editor.Converters;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    /// <summary>
    /// Saves each Figma COMPONENT_SET variant as its own standalone prefab.
    ///
    /// Earlier versions wired the variants into a Unity Prefab Variant chain off the first
    /// variant ("base"), then tried to record only the property differences as overrides.
    /// In practice that lost visual fidelity: variants in a Figma component set commonly
    /// differ in size, padding, child layout — not just colors and text — so applying a
    /// partial override on top of a structurally-different base produced collapsed children
    /// and mis-positioned content. Worse, Unity's automatic LayoutGroup pass ran on the
    /// PrefabInstance between Instantiate and Save and silently injected its own RT diffs
    /// as overrides, which we couldn't reliably strip.
    ///
    /// The standalone path mirrors what Screen mode does when it inlines the variant tree
    /// directly: convertFunc(variant) already produces a complete, correct GameObject tree
    /// for the variant — we save that tree as its own .prefab. Same code path as the screen,
    /// same visual result. Trade-off: variants don't share a base, so disk usage is
    /// slightly higher and edits to one don't propagate. Visual correctness wins.
    /// </summary>
    internal sealed class PrefabVariantBuilder
    {
        private readonly ImportLogger _logger;

        public PrefabVariantBuilder(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Given a COMPONENT_SET node with COMPONENT children (variants), save each variant
        /// as a standalone prefab. Returns mapping of componentId → prefab asset path.
        /// </summary>
        public Dictionary<string, string> BuildVariantChain(
            FigmaNode componentSetNode,
            System.Func<FigmaNode, GameObject> convertFunc,
            string outputDir,
            ImportContext ctx)
        {
            var result = new Dictionary<string, string>();

            if (componentSetNode.Children == null || componentSetNode.Children.Count == 0)
                return result;

            // Separate variants — components are children of the component set
            var variants = componentSetNode.Children
                .Where(c => c.NodeType == FigmaNodeType.COMPONENT)
                .ToList();

            if (variants.Count == 0)
                return result;

            AssetFolderUtil.EnsureFolder(outputDir);

            // Parse variant properties: "State=Default, Size=Large" → dictionary
            var parsedVariants = variants
                .Select(v => new
                {
                    Node = v,
                    Props = ParseVariantName(v.Name),
                    ComponentId = v.Id
                })
                .ToList();

            var baseVariant = parsedVariants[0];
            var baseName = SanitizeName(componentSetNode.Name);

            for (int i = 0; i < parsedVariants.Count; i++)
            {
                var variant = parsedVariants[i];
                var variantGo = convertFunc(variant.Node);

                // The first variant takes the bare component-set name; the rest get a
                // suffix derived from the props that differ from the first variant.
                var fileName = i == 0
                    ? baseName
                    : BuildVariantName(baseName, variant.Props, baseVariant.Props);
                var variantPath = Path.Combine(outputDir, $"{fileName}.prefab");

                // Each variant prefab carries its own componentId so InstanceConverter's
                // ctx.GeneratedPrefabs lookup resolves to this exact file on the screen
                // pass, and ComponentPrefabNamer.Resolve can re-find it on later imports.
                ManifestBuilder.AttachRootManifest(variantGo, ctx, variant.ComponentId);

                PrefabUtility.SaveAsPrefabAsset(variantGo, variantPath);
                result[variant.ComponentId] = variantPath;
                ctx.GeneratedPrefabs[variant.ComponentId] = variantPath;
                _logger.Success($"{(i == 0 ? "Base" : "Variant")} prefab: {variantPath}");

                Object.DestroyImmediate(variantGo);
            }

            // Apply Selectable state mapping on the base prefab (if it's a Button-like
            // component set with State=Default/Hover/Pressed/Disabled variants). Loaded
            // back from disk because the live baseGo was destroyed above.
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(result[baseVariant.ComponentId]);
            if (basePrefab != null)
            {
                var variantNodeMap = variants.ToDictionary(v => v.Id, v => v);
                var baseInstance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                try
                {
                    var stateMapper = new SelectableStateMapper(_logger);
                    if (stateMapper.TryApplyStates(baseInstance, componentSetNode, variantNodeMap, ctx))
                        PrefabUtility.SaveAsPrefabAsset(baseInstance, result[baseVariant.ComponentId]);
                }
                finally
                {
                    Object.DestroyImmediate(baseInstance);
                }
            }

            return result;
        }

        /// <summary>
        /// Parse Figma variant name like "State=Default, Size=Large" into key-value pairs.
        /// </summary>
        internal static Dictionary<string, string> ParseVariantName(string name)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(name))
                return result;

            var parts = name.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Split('=');
                if (kv.Length == 2)
                    result[kv[0].Trim()] = kv[1].Trim();
            }

            // If no key=value format, treat the whole name as a single property
            if (result.Count == 0)
                result["Variant"] = name.Trim();

            return result;
        }

        /// <summary>
        /// Build a variant prefab name from the differing properties.
        /// e.g., base="Button", props differ in "State" → "Button_Hover"
        /// </summary>
        private string BuildVariantName(
            string baseName,
            Dictionary<string, string> variantProps,
            Dictionary<string, string> baseProps)
        {
            var diffs = new List<string>();
            foreach (var kv in variantProps)
            {
                if (!baseProps.TryGetValue(kv.Key, out var baseVal) || baseVal != kv.Value)
                    diffs.Add(kv.Value);
            }

            if (diffs.Count == 0)
                diffs.Add("Variant");

            return SanitizeName($"{baseName}_{string.Join("_", diffs)}");
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim().Trim('.');
        }
    }
}
