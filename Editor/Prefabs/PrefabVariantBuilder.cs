using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoobakFigma2Unity.Editor.Converters;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
            ManifestBuilder.AttachRootManifest(baseGo, ctx);
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
                ApplyOverrides(basePrefabInstance, variantGo);

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
        /// Recurses by name; copies property overrides on every match. Structural diffs —
        /// children present in only one of the two trees — are flagged in the log and left
        /// alone for now. The previous attempt to UnityEngine.Object.Instantiate variant-only
        /// subtrees into the base prefab triggered a Unity assertion on save when the cloned
        /// subtree contained a nested PrefabInstance, because the freshly-cloned objects
        /// inherit DontSaveInEditor flags from the source's editor-only state. The right fix
        /// is to either (a) extract structurally-distinct variants as independent prefabs
        /// instead of forcing them into a Variant chain, or (b) clone via SerializedObject
        /// so the prefab payload stays save-safe — both deferred to a follow-up pass.
        /// </summary>
        private void ApplyOverrides(GameObject target, GameObject source)
        {
            ApplyComponentOverrides(target, source);

            var targetChildren = new Dictionary<string, Transform>();
            for (int i = 0; i < target.transform.childCount; i++)
            {
                var c = target.transform.GetChild(i);
                targetChildren[c.name] = c;
            }
            var sourceChildren = new Dictionary<string, Transform>();
            for (int i = 0; i < source.transform.childCount; i++)
            {
                var c = source.transform.GetChild(i);
                sourceChildren[c.name] = c;
            }

            for (int i = 0; i < source.transform.childCount; i++)
            {
                var sourceChild = source.transform.GetChild(i);
                if (targetChildren.TryGetValue(sourceChild.name, out var targetChild))
                {
                    ApplyOverrides(targetChild.gameObject, sourceChild.gameObject);
                }
                else
                {
                    _logger?.Warn($"Variant '{source.name}' has a child '{sourceChild.name}' the base prefab doesn't — skipping (structural variant; will look incorrect until promoted to an independent prefab).");
                }
            }

            foreach (var pair in targetChildren)
            {
                if (sourceChildren.ContainsKey(pair.Key)) continue;
                _logger?.Warn($"Variant '{source.name}' is missing base child '{pair.Key}' — leaving it visible (structural variant).");
            }
        }

        private void ApplyComponentOverrides(GameObject target, GameObject source)
        {
            // RectTransform — full sync. The previous version only copied sizeDelta,
            // which left every variant inheriting the base's anchors/pivot/position
            // and produced visually wrong results for variants that moved or re-anchored
            // their children (which is most of them in real design systems).
            var targetRt = target.GetComponent<RectTransform>();
            var sourceRt = source.GetComponent<RectTransform>();
            if (targetRt != null && sourceRt != null)
            {
                if (targetRt.anchorMin       != sourceRt.anchorMin)       targetRt.anchorMin       = sourceRt.anchorMin;
                if (targetRt.anchorMax       != sourceRt.anchorMax)       targetRt.anchorMax       = sourceRt.anchorMax;
                if (targetRt.pivot           != sourceRt.pivot)           targetRt.pivot           = sourceRt.pivot;
                if (targetRt.sizeDelta       != sourceRt.sizeDelta)       targetRt.sizeDelta       = sourceRt.sizeDelta;
                if (targetRt.anchoredPosition != sourceRt.anchoredPosition) targetRt.anchoredPosition = sourceRt.anchoredPosition;
                if (targetRt.localRotation   != sourceRt.localRotation)   targetRt.localRotation   = sourceRt.localRotation;
                if (targetRt.localScale      != sourceRt.localScale)      targetRt.localScale      = sourceRt.localScale;
            }

            // Image — sprite, color, plus type/preserveAspect/fillCenter so 9-slice
            // and aspect-locked variants don't degenerate into stretched simple sprites.
            var targetImage = target.GetComponent<Image>();
            var sourceImage = source.GetComponent<Image>();
            if (targetImage != null && sourceImage != null)
            {
                if (targetImage.color           != sourceImage.color)           targetImage.color           = sourceImage.color;
                if (targetImage.sprite          != sourceImage.sprite)          targetImage.sprite          = sourceImage.sprite;
                if (targetImage.type            != sourceImage.type)            targetImage.type            = sourceImage.type;
                if (targetImage.preserveAspect  != sourceImage.preserveAspect)  targetImage.preserveAspect  = sourceImage.preserveAspect;
                if (targetImage.fillCenter      != sourceImage.fillCenter)      targetImage.fillCenter      = sourceImage.fillCenter;
            }

            // TextMeshProUGUI — text, color, fontSize, alignment, font asset.
            var targetText = target.GetComponent<TMPro.TextMeshProUGUI>();
            var sourceText = source.GetComponent<TMPro.TextMeshProUGUI>();
            if (targetText != null && sourceText != null)
            {
                if (targetText.text      != sourceText.text)               targetText.text      = sourceText.text;
                if (targetText.color     != sourceText.color)              targetText.color     = sourceText.color;
                if (!Mathf.Approximately(targetText.fontSize, sourceText.fontSize)) targetText.fontSize = sourceText.fontSize;
                if (targetText.alignment != sourceText.alignment)          targetText.alignment = sourceText.alignment;
                if (targetText.font      != sourceText.font && sourceText.font != null) targetText.font = sourceText.font;
                if (targetText.fontStyle != sourceText.fontStyle)          targetText.fontStyle = sourceText.fontStyle;
            }

            // CanvasGroup (opacity).
            var targetCg = target.GetComponent<CanvasGroup>();
            var sourceCg = source.GetComponent<CanvasGroup>();
            if (sourceCg != null)
            {
                if (targetCg == null) targetCg = target.AddComponent<CanvasGroup>();
                if (!Mathf.Approximately(targetCg.alpha, sourceCg.alpha)) targetCg.alpha = sourceCg.alpha;
            }

            // LayoutElement — preferred / min / flexible sizes drive how the parent
            // LayoutGroup positions this object, so per-variant layout differences
            // (a wider button having a wider preferredWidth) need to come through.
            var targetLe = target.GetComponent<LayoutElement>();
            var sourceLe = source.GetComponent<LayoutElement>();
            if (sourceLe != null)
            {
                if (targetLe == null) targetLe = target.AddComponent<LayoutElement>();
                if (!Mathf.Approximately(targetLe.preferredWidth,  sourceLe.preferredWidth))  targetLe.preferredWidth  = sourceLe.preferredWidth;
                if (!Mathf.Approximately(targetLe.preferredHeight, sourceLe.preferredHeight)) targetLe.preferredHeight = sourceLe.preferredHeight;
                if (!Mathf.Approximately(targetLe.minWidth,        sourceLe.minWidth))        targetLe.minWidth        = sourceLe.minWidth;
                if (!Mathf.Approximately(targetLe.minHeight,       sourceLe.minHeight))       targetLe.minHeight       = sourceLe.minHeight;
                if (!Mathf.Approximately(targetLe.flexibleWidth,   sourceLe.flexibleWidth))   targetLe.flexibleWidth   = sourceLe.flexibleWidth;
                if (!Mathf.Approximately(targetLe.flexibleHeight,  sourceLe.flexibleHeight))  targetLe.flexibleHeight  = sourceLe.flexibleHeight;
            }
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
