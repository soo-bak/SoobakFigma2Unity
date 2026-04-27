using System.Collections.Generic;
using System.Linq;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Prefabs
{
    /// <summary>
    /// Links Figma INSTANCE nodes to existing Unity Prefabs,
    /// creating proper PrefabInstances with overrides instead of inline copies.
    /// </summary>
    internal sealed class PrefabInstanceLinker
    {
        private readonly ImportLogger _logger;

        public PrefabInstanceLinker(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Try to instantiate a prefab for the given INSTANCE node.
        /// Returns the instantiated GameObject if a matching prefab was found, null otherwise.
        /// </summary>
        public GameObject TryCreatePrefabInstance(FigmaNode instanceNode, GameObject parent, ImportContext ctx)
        {
            if (string.IsNullOrEmpty(instanceNode.ComponentId))
                return null;

            // Look up the prefab path for this component
            if (!ctx.GeneratedPrefabs.TryGetValue(instanceNode.ComponentId, out var prefabPath))
                return null;

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                _logger.Warn($"{instanceNode.Name}: prefab not found at {prefabPath}");
                return null;
            }

            // Instantiate as prefab instance
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            instance.name = instanceNode.Name;

            if (parent != null)
                instance.transform.SetParent(parent.transform, false);

            // Apply instance overrides from Figma
            ApplyInstanceOverrides(instance, instanceNode, ctx);

            _logger.Info($"{instanceNode.Name}: linked to prefab '{prefabAsset.name}'");
            return instance;
        }

        /// <summary>
        /// Apply Figma instance overrides to the prefab instance.
        /// This handles property changes that differ from the base component.
        /// </summary>
        private void ApplyInstanceOverrides(GameObject instance, FigmaNode instanceNode, ImportContext ctx)
        {
            // Size override
            var rt = instance.GetComponent<RectTransform>();
            if (rt != null && instanceNode.AbsoluteBoundingBox != null)
            {
                var size = new Vector2(instanceNode.AbsoluteBoundingBox.Width, instanceNode.AbsoluteBoundingBox.Height);
                rt.sizeDelta = size;
            }

            // Opacity override
            if (instanceNode.Opacity < 1f)
            {
                var cg = instance.GetComponent<CanvasGroup>();
                if (cg == null) cg = instance.AddComponent<CanvasGroup>();
                cg.alpha = instanceNode.Opacity;
            }

            // Fill color overrides on root
            if (instanceNode.HasVisibleFills)
            {
                var image = instance.GetComponent<Image>();
                if (image != null && instanceNode.Fills != null)
                {
                    foreach (var fill in instanceNode.Fills)
                    {
                        if (fill.Visible && fill.IsSolid && fill.Color != null)
                        {
                            image.color = ColorSpaceHelper.Convert(fill.Color, instanceNode.Opacity * fill.Opacity);
                            break;
                        }
                    }
                }
            }

            // Recursively apply child overrides by Figma identity/metadata. Pass the
            // INSTANCE's componentProperties down so child componentPropertyReferences can resolve
            // (BOOLEAN → SetActive, TEXT → TMP.text). PrefabUtility.RecordPrefabInstance...
            // is invoked at the end so Unity registers the changes as proper overrides
            // on the PrefabInstance rather than leaving them as un-tracked edits.
            if (instanceNode.Children != null)
            {
                ApplyChildOverrides(instance, instanceNode, ctx, instanceNode.ComponentProperties);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        }

        private void ApplyChildOverrides(
            GameObject parentGo, FigmaNode parentNode, ImportContext ctx,
            Dictionary<string, FigmaComponentPropertyValue> ownerProperties)
        {
            if (parentNode.Children == null) return;

            var occurrenceIndex = new Dictionary<string, int>();
            foreach (var childNode in parentNode.Children)
            {
                var childTransform = FindMatchingChild(parentGo.transform, childNode, ctx, occurrenceIndex);
                if (childTransform == null) continue;

                var childGo = childTransform.gameObject;

                // 1) componentProperty references — Figma's "Show Icon", "Label", etc.
                //    The CHILD references a property on the OWNING INSTANCE; that's why
                //    we pass ownerProperties from the outermost INSTANCE downward.
                ApplyComponentPropertyReferences(childGo, childNode, ownerProperties);

                // 2) Direct text overrides on TEXT nodes (when not driven by a TEXT property)
                if (childNode.NodeType == FigmaNodeType.TEXT)
                {
                    var tmp = childGo.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null && !string.IsNullOrEmpty(childNode.Characters))
                    {
                        tmp.text = childNode.Characters;

                        // Color override
                        if (childNode.Fills != null)
                        {
                            foreach (var fill in childNode.Fills)
                            {
                                if (fill.Visible && fill.IsSolid && fill.Color != null)
                                {
                                    tmp.color = ColorSpaceHelper.Convert(fill.Color, childNode.Opacity * fill.Opacity);
                                    break;
                                }
                            }
                        }
                    }
                }

                // 3) Image/fill overrides
                var image = childGo.GetComponent<Image>();
                if (image != null && childNode.HasVisibleFills)
                {
                    foreach (var fill in childNode.Fills)
                    {
                        if (fill.Visible && fill.IsSolid && fill.Color != null)
                        {
                            image.color = ColorSpaceHelper.Convert(fill.Color, childNode.Opacity * fill.Opacity);
                            break;
                        }

                        if (fill.Visible && fill.IsImage && fill.ImageRef != null &&
                            ctx.FillSprites.TryGetValue(fill.ImageRef, out var sprite))
                        {
                            image.sprite = sprite;
                            break;
                        }
                    }
                }

                // 4) Plain visibility (Figma's hide-this-layer toggle, distinct from a BOOLEAN
                //    component property). A BOOLEAN already set this above; if both apply, the
                //    later "false" wins which matches Figma's "false hides regardless" rule.
                if (!childNode.Visible)
                    childGo.SetActive(false);

                // 5) Recurse — but skip stepping into nested INSTANCE children. Those are
                //    their own PrefabInstances (linked in their own InstanceConverter pass)
                //    and shouldn't have the outer instance's overrides bled into them.
                if (childNode.NodeType != FigmaNodeType.INSTANCE && childNode.Children != null)
                    ApplyChildOverrides(childGo, childNode, ctx, ownerProperties);
            }
        }

        private static Transform FindMatchingChild(
            Transform parent,
            FigmaNode childNode,
            ImportContext ctx,
            Dictionary<string, int> occurrenceIndex)
        {
            var manifest = parent.GetComponentInParent<FigmaPrefabManifest>(true);

            if (manifest != null)
            {
                foreach (var candidateId in CandidateNodeIds(childNode.Id))
                {
                    if (string.IsNullOrEmpty(candidateId)) continue;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (manifest.GetNodeId(child) == candidateId)
                            return child;
                    }
                }

                var semanticKey = GetSemanticKey(childNode);
                if (!string.IsNullOrEmpty(semanticKey))
                {
                    Transform match = null;
                    int matches = 0;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        var targetNode = TryGetNode(ctx, manifest.GetNodeId(child));
                        if (GetSemanticKey(targetNode) != semanticKey) continue;
                        match = child;
                        matches++;
                    }
                    if (matches == 1)
                        return match;
                }
            }

            var occurrenceKey = GetOccurrenceKey(childNode);
            var occurrence = occurrenceIndex.TryGetValue(occurrenceKey, out var index) ? index : 0;
            occurrenceIndex[occurrenceKey] = occurrence + 1;

            var seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (GetOccurrenceKey(TryGetNode(ctx, manifest?.GetNodeId(child)), child.name) != occurrenceKey)
                    continue;
                if (seen == occurrence)
                    return child;
                seen++;
            }

            return parent.Find(childNode.Name);
        }

        private static IEnumerable<string> CandidateNodeIds(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) yield break;
            yield return nodeId;

            var semi = nodeId.LastIndexOf(';');
            if (semi >= 0 && semi + 1 < nodeId.Length)
                yield return nodeId.Substring(semi + 1);
        }

        private static FigmaNode TryGetNode(ImportContext ctx, string id)
        {
            if (ctx == null || string.IsNullOrEmpty(id)) return null;
            if (ctx.NodeIndex.TryGetValue(id, out var node)) return node;

            var semi = id.LastIndexOf(';');
            if (semi >= 0 && semi + 1 < id.Length)
            {
                var sourceId = id.Substring(semi + 1);
                if (ctx.NodeIndex.TryGetValue(sourceId, out node))
                    return node;
            }

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

        private static string GetOccurrenceKey(FigmaNode node, string fallbackName = null)
        {
            if (node == null)
                return fallbackName ?? string.Empty;
            return $"{node.Type}|{node.Name}";
        }

        private static void ApplyComponentPropertyReferences(
            GameObject childGo, FigmaNode childNode,
            Dictionary<string, FigmaComponentPropertyValue> ownerProperties)
        {
            if (ownerProperties == null) return;
            var refs = childNode.ComponentPropertyReferences;
            if (refs == null || refs.Count == 0) return;

            foreach (var pair in refs)
            {
                // pair.Key is the local property the child wants overridden ("visible",
                // "characters", "mainComponent"). pair.Value is the "Name#Id" key into
                // the owning INSTANCE's componentProperties dictionary.
                if (!ownerProperties.TryGetValue(pair.Value, out var propValue) || propValue == null)
                    continue;

                switch (pair.Key)
                {
                    case "visible":
                        if (propValue.Type == "BOOLEAN")
                            childGo.SetActive(propValue.AsBool());
                        break;
                    case "characters":
                        if (propValue.Type == "TEXT")
                        {
                            var tmp = childGo.GetComponent<TMPro.TextMeshProUGUI>();
                            var text = propValue.AsString();
                            if (tmp != null && text != null)
                                tmp.text = text;
                        }
                        break;
                    // case "mainComponent": INSTANCE_SWAP — v2; for now the prefab keeps
                    // whatever component the master extraction baked in.
                }
            }
        }
    }
}
