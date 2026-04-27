using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoobakFigma2Unity.Editor.Converters;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    /// <summary>
    /// Creates Prefab Variants from Figma Component Sets.
    ///
    /// Figma structure:
    ///   ComponentSet "Button" (COMPONENT_SET)
    ///     ├── Component "State=Default" (COMPONENT)
    ///     ├── Component "State=Hover" (COMPONENT)
    ///     ├── Component "State=Pressed" (COMPONENT)
    ///     └── Component "State=Disabled" (COMPONENT)
    ///
    /// Unity result:
    ///   Button.prefab (base, from first variant)
    ///     ├── Button_Hover.prefab (variant, overrides differ from base)
    ///     ├── Button_Pressed.prefab (variant)
    ///     └── Button_Disabled.prefab (variant)
    /// </summary>
    internal sealed class PrefabVariantBuilder
    {
        private readonly ImportLogger _logger;

        public PrefabVariantBuilder(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Given a COMPONENT_SET node with COMPONENT children (variants),
        /// create a base prefab from the first variant and Prefab Variants for the rest.
        /// Returns mapping of componentId -> prefab asset path.
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

            // First variant becomes the base prefab
            var baseVariant = parsedVariants[0];
            var baseGo = convertFunc(baseVariant.Node);
            var baseName = SanitizeName(componentSetNode.Name);
            var basePath = Path.Combine(outputDir, $"{baseName}.prefab");

            // Try to apply Selectable state mapping (Button/Toggle states)
            var variantNodeMap = variants.ToDictionary(v => v.Id, v => v);
            var stateMapper = new SelectableStateMapper(_logger);
            stateMapper.TryApplyStates(baseGo, componentSetNode, variantNodeMap, ctx);

            // Base component prefab owns the manifest; variants inherit it via prefab linkage.
            ManifestBuilder.AttachRootManifest(baseGo, ctx, baseVariant.ComponentId);
            var basePrefab = PrefabUtility.SaveAsPrefabAsset(baseGo, basePath);
            result[baseVariant.ComponentId] = basePath;
            ctx.GeneratedPrefabs[baseVariant.ComponentId] = basePath;
            _logger.Success($"Base prefab: {basePath}");

            Object.DestroyImmediate(baseGo);

            // Remaining variants become Prefab Variants
            for (int i = 1; i < parsedVariants.Count; i++)
            {
                var variant = parsedVariants[i];
                var variantGo = convertFunc(variant.Node);
                var variantName = BuildVariantName(baseName, variant.Props, baseVariant.Props);
                var variantPath = Path.Combine(outputDir, $"{variantName}.prefab");

                // Instantiate base prefab, apply overrides from variant, save as variant
                var basePrefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);

                // Apply visual differences from the variant
                ApplyOverrides(basePrefabInstance, variantGo, ctx, FigmaManagedTypesRegistryProvider.Get());
                ManifestBuilder.AttachRootManifest(basePrefabInstance, ctx, variant.ComponentId);

                var variantPrefab = PrefabUtility.SaveAsPrefabAsset(basePrefabInstance, variantPath);
                result[variant.ComponentId] = variantPath;
                ctx.GeneratedPrefabs[variant.ComponentId] = variantPath;
                _logger.Success($"Variant prefab: {variantPath}");

                Object.DestroyImmediate(basePrefabInstance);
                Object.DestroyImmediate(variantGo);
            }

            return result;
        }

        /// <summary>
        /// Apply visual differences from source (variant) onto target (base prefab instance).
        /// Variant COMPONENT children do not share node IDs across variants, so matching
        /// uses Figma metadata first (component property refs / componentId), then sibling
        /// occurrence. This avoids the duplicate-name failure mode common in buttons
        /// (left icon and right icon both named "icon").
        /// </summary>
        private void ApplyOverrides(
            GameObject target,
            GameObject source,
            ImportContext ctx,
            FigmaManagedTypesRegistry policy)
        {
            SyncIdentity(target.transform, source.transform, ctx);
            if (target.activeSelf != source.activeSelf)
                target.SetActive(source.activeSelf);

            ApplyComponentOverrides(target, source, policy);

            var targetChildren = new List<Transform>();
            for (int i = 0; i < target.transform.childCount; i++)
                targetChildren.Add(target.transform.GetChild(i));

            var sourceChildren = new List<Transform>();
            for (int i = 0; i < source.transform.childCount; i++)
                sourceChildren.Add(source.transform.GetChild(i));

            var targetManifest = target.GetComponentInParent<FigmaPrefabManifest>(true);
            var targetById = BuildTargetByNodeId(targetChildren, targetManifest);
            var targetBySemanticKey = BuildTargetBySemanticKey(targetChildren, ctx, targetManifest);
            var targetByOccurrenceKey = BuildTargetByOccurrenceKey(targetChildren, ctx, targetManifest);
            var sourceSemanticCounts = CountSourceSemanticKeys(sourceChildren, ctx);
            var sourceOccurrenceIndex = new Dictionary<string, int>();
            var matchedTargets = new HashSet<Transform>();

            for (int i = 0; i < sourceChildren.Count; i++)
            {
                var sourceChild = sourceChildren[i];
                var sourceNode = GetNodeForSource(sourceChild, ctx);
                var targetChild = FindMatchingTargetChild(
                    sourceChild,
                    sourceNode,
                    targetById,
                    targetBySemanticKey,
                    targetByOccurrenceKey,
                    sourceSemanticCounts,
                    sourceOccurrenceIndex,
                    matchedTargets);

                if (targetChild != null)
                {
                    targetChild.SetSiblingIndex(i);
                    matchedTargets.Add(targetChild);
                    ApplyOverrides(targetChild.gameObject, sourceChild.gameObject, ctx, policy);
                }
                else
                {
                    var added = UnityEngine.Object.Instantiate(sourceChild.gameObject, target.transform);
                    added.name = sourceChild.name;
                    added.transform.SetSiblingIndex(i);
                    CopyIdentityRecursive(sourceChild, added.transform, ctx);
                    matchedTargets.Add(added.transform);
                }
            }

            foreach (var child in targetChildren)
            {
                if (matchedTargets.Contains(child)) continue;
                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
        }

        private void ApplyComponentOverrides(
            GameObject target,
            GameObject source,
            FigmaManagedTypesRegistry policy)
        {
            ComponentMerger.SyncComponents(source, target, null, policy);
        }

        private static Dictionary<string, Transform> BuildTargetByNodeId(
            List<Transform> targetChildren,
            FigmaPrefabManifest manifest)
        {
            var result = new Dictionary<string, Transform>();
            if (manifest == null) return result;

            foreach (var child in targetChildren)
            {
                var id = manifest.GetNodeId(child);
                if (!string.IsNullOrEmpty(id))
                    result[id] = child;
            }
            return result;
        }

        private static Dictionary<string, List<Transform>> BuildTargetBySemanticKey(
            List<Transform> targetChildren,
            ImportContext ctx,
            FigmaPrefabManifest manifest)
        {
            var result = new Dictionary<string, List<Transform>>();
            foreach (var child in targetChildren)
            {
                var key = GetSemanticKey(GetNodeForTarget(child, ctx, manifest));
                if (string.IsNullOrEmpty(key)) continue;
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<Transform>();
                    result[key] = list;
                }
                list.Add(child);
            }
            return result;
        }

        private static Dictionary<string, List<Transform>> BuildTargetByOccurrenceKey(
            List<Transform> targetChildren,
            ImportContext ctx,
            FigmaPrefabManifest manifest)
        {
            var result = new Dictionary<string, List<Transform>>();
            foreach (var child in targetChildren)
            {
                var key = GetOccurrenceKey(GetNodeForTarget(child, ctx, manifest), child.name);
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<Transform>();
                    result[key] = list;
                }
                list.Add(child);
            }
            return result;
        }

        private static Dictionary<string, int> CountSourceSemanticKeys(List<Transform> sourceChildren, ImportContext ctx)
        {
            var result = new Dictionary<string, int>();
            foreach (var sourceChild in sourceChildren)
            {
                var key = GetSemanticKey(GetNodeForSource(sourceChild, ctx));
                if (string.IsNullOrEmpty(key)) continue;
                result[key] = result.TryGetValue(key, out var count) ? count + 1 : 1;
            }
            return result;
        }

        private static Transform FindMatchingTargetChild(
            Transform sourceChild,
            FigmaNode sourceNode,
            Dictionary<string, Transform> targetById,
            Dictionary<string, List<Transform>> targetBySemanticKey,
            Dictionary<string, List<Transform>> targetByOccurrenceKey,
            Dictionary<string, int> sourceSemanticCounts,
            Dictionary<string, int> sourceOccurrenceIndex,
            HashSet<Transform> matchedTargets)
        {
            var sourceId = sourceNode?.Id;
            if (!string.IsNullOrEmpty(sourceId)
                && targetById.TryGetValue(sourceId, out var byId)
                && !matchedTargets.Contains(byId))
                return byId;

            var semanticKey = GetSemanticKey(sourceNode);
            if (!string.IsNullOrEmpty(semanticKey)
                && sourceSemanticCounts.TryGetValue(semanticKey, out var sourceCount)
                && sourceCount == 1
                && targetBySemanticKey.TryGetValue(semanticKey, out var semanticTargets)
                && semanticTargets.Count == 1
                && !matchedTargets.Contains(semanticTargets[0]))
                return semanticTargets[0];

            var occurrenceKey = GetOccurrenceKey(sourceNode, sourceChild.name);
            var occurrence = sourceOccurrenceIndex.TryGetValue(occurrenceKey, out var index) ? index : 0;
            sourceOccurrenceIndex[occurrenceKey] = occurrence + 1;

            if (targetByOccurrenceKey.TryGetValue(occurrenceKey, out var occurrenceTargets))
            {
                for (int i = occurrence; i < occurrenceTargets.Count; i++)
                    if (!matchedTargets.Contains(occurrenceTargets[i]))
                        return occurrenceTargets[i];
                for (int i = 0; i < occurrenceTargets.Count; i++)
                    if (!matchedTargets.Contains(occurrenceTargets[i]))
                        return occurrenceTargets[i];
            }

            return null;
        }

        private static FigmaNode GetNodeForSource(Transform source, ImportContext ctx)
        {
            if (ctx != null
                && ctx.NodeIdentities.TryGetValue(source, out var identity)
                && !string.IsNullOrEmpty(identity.FigmaNodeId))
                return TryGetNode(ctx, identity.FigmaNodeId);
            return null;
        }

        private static FigmaNode GetNodeForTarget(
            Transform target,
            ImportContext ctx,
            FigmaPrefabManifest manifest)
        {
            var id = manifest?.GetNodeId(target);
            return TryGetNode(ctx, id);
        }

        private static FigmaNode TryGetNode(ImportContext ctx, string id)
        {
            if (ctx == null || string.IsNullOrEmpty(id)) return null;
            if (ctx.NodeIndex.TryGetValue(id, out var node)) return node;

            var sourceId = GetInstanceSourceNodeId(id);
            if (!string.IsNullOrEmpty(sourceId) && ctx.NodeIndex.TryGetValue(sourceId, out node))
                return node;

            return null;
        }

        private static string GetSemanticKey(FigmaNode node)
        {
            if (node == null) return null;

            if (node.ComponentPropertyReferences != null && node.ComponentPropertyReferences.Count > 0)
            {
                var refs = node.ComponentPropertyReferences
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}={kv.Value}");
                return $"{node.Type}|{node.Name}|refs:{string.Join(";", refs)}";
            }

            if (!string.IsNullOrEmpty(node.ComponentId))
                return $"{node.Type}|{node.Name}|component:{node.ComponentId}";

            return $"{node.Type}|{node.Name}";
        }

        private static string GetOccurrenceKey(FigmaNode node, string fallbackName)
        {
            if (node == null)
                return fallbackName ?? string.Empty;
            return $"{node.Type}|{node.Name}";
        }

        private static string GetInstanceSourceNodeId(string nodeId)
        {
            var semi = nodeId?.LastIndexOf(';') ?? -1;
            return semi >= 0 && semi + 1 < nodeId.Length ? nodeId.Substring(semi + 1) : null;
        }

        private static void SyncIdentity(Transform target, Transform source, ImportContext ctx)
        {
            if (ctx != null && ctx.NodeIdentities.TryGetValue(source, out var identity))
                ctx.NodeIdentities[target] = identity;
        }

        private static void CopyIdentityRecursive(Transform source, Transform target, ImportContext ctx)
        {
            SyncIdentity(target, source, ctx);
            int count = Mathf.Min(source.childCount, target.childCount);
            for (int i = 0; i < count; i++)
                CopyIdentityRecursive(source.GetChild(i), target.GetChild(i), ctx);
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
