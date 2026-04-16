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

            // Update FigmaNodeRef
            var nodeRef = instance.GetComponent<FigmaNodeRef>();
            if (nodeRef == null)
                nodeRef = instance.AddComponent<FigmaNodeRef>();
            nodeRef.FigmaNodeId = instanceNode.Id;
            nodeRef.FigmaComponentId = instanceNode.ComponentId;

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
                            image.color = ColorSpaceHelper.Convert(fill.Color, instanceNode.Opacity);
                            break;
                        }
                    }
                }
            }

            // Recursively apply child overrides by matching names
            if (instanceNode.Children != null)
            {
                ApplyChildOverrides(instance, instanceNode, ctx);
            }
        }

        private void ApplyChildOverrides(GameObject parentGo, FigmaNode parentNode, ImportContext ctx)
        {
            if (parentNode.Children == null) return;

            foreach (var childNode in parentNode.Children)
            {
                // Find matching child in the prefab instance by name
                var childTransform = parentGo.transform.Find(childNode.Name);
                if (childTransform == null) continue;

                var childGo = childTransform.gameObject;

                // Text overrides
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
                                    tmp.color = ColorSpaceHelper.Convert(fill.Color);
                                    break;
                                }
                            }
                        }
                    }
                }

                // Image/fill overrides
                var image = childGo.GetComponent<Image>();
                if (image != null && childNode.HasVisibleFills)
                {
                    foreach (var fill in childNode.Fills)
                    {
                        if (fill.Visible && fill.IsSolid && fill.Color != null)
                        {
                            image.color = ColorSpaceHelper.Convert(fill.Color);
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

                // Visibility override
                if (!childNode.Visible)
                    childGo.SetActive(false);

                // Recurse
                if (childNode.Children != null)
                    ApplyChildOverrides(childGo, childNode, ctx);
            }
        }
    }
}
