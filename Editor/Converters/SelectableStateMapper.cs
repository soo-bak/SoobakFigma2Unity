using System.Collections.Generic;
using System.Linq;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Maps Figma Component Variant states to Unity Selectable transitions.
    ///
    /// Detects variant properties that match UI state naming:
    ///   "State=Default"  → Selectable normal
    ///   "State=Hover"    → Selectable highlighted
    ///   "State=Pressed"  → Selectable pressed
    ///   "State=Disabled" → Selectable disabled
    ///   "State=Selected" → Selectable selected
    ///
    /// Supports both ColorTint (when only colors differ) and SpriteSwap
    /// (when sprites/images differ between states).
    /// </summary>
    internal sealed class SelectableStateMapper
    {
        private readonly ImportLogger _logger;

        // Known state names (case-insensitive matching)
        private static readonly Dictionary<string, string> StateAliases = new Dictionary<string, string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            { "Default", "Normal" },
            { "Normal", "Normal" },
            { "Rest", "Normal" },
            { "Idle", "Normal" },
            { "Hover", "Highlighted" },
            { "Hovered", "Highlighted" },
            { "Highlighted", "Highlighted" },
            { "Focus", "Highlighted" },
            { "Focused", "Highlighted" },
            { "Pressed", "Pressed" },
            { "Active", "Pressed" },
            { "Clicked", "Pressed" },
            { "Tap", "Pressed" },
            { "Disabled", "Disabled" },
            { "Inactive", "Disabled" },
            { "Selected", "Selected" },
            { "On", "Selected" },
            { "Checked", "Selected" },
        };

        public SelectableStateMapper(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyze a ComponentSet's variants and determine if they represent UI states.
        /// If so, configure the Selectable component on the base prefab.
        /// </summary>
        public bool TryApplyStates(
            GameObject basePrefabGo,
            FigmaNode componentSetNode,
            Dictionary<string, FigmaNode> variantNodes,
            ImportContext ctx)
        {
            // Parse variant names and look for state property
            var stateMap = new Dictionary<string, FigmaNode>(); // "Normal"/"Highlighted"/etc. → node
            string statePropertyName = null;

            foreach (var variant in variantNodes)
            {
                var props = Prefabs.PrefabVariantBuilder.ParseVariantName(variant.Value.Name);
                foreach (var kv in props)
                {
                    if (StateAliases.TryGetValue(kv.Value, out var normalizedState))
                    {
                        statePropertyName = kv.Key;
                        stateMap[normalizedState] = variant.Value;
                    }
                }
            }

            // Need at least Normal + one other state
            if (!stateMap.ContainsKey("Normal") || stateMap.Count < 2)
                return false;

            _logger.Info($"{componentSetNode.Name}: detected UI states ({string.Join(", ", stateMap.Keys)})");

            var normalNode = stateMap["Normal"];

            // Determine transition type: ColorTint or SpriteSwap
            bool hasColorDifferences = false;
            bool hasSpriteDifferences = false;

            foreach (var kv in stateMap)
            {
                if (kv.Key == "Normal") continue;

                var diff = CompareNodes(normalNode, kv.Value);
                if (diff.ColorChanged) hasColorDifferences = true;
                if (diff.SpriteChanged) hasSpriteDifferences = true;
            }

            // Add Button component (most common Selectable)
            var button = basePrefabGo.GetComponent<Button>();
            if (button == null)
                button = basePrefabGo.AddComponent<Button>();

            if (hasSpriteDifferences)
            {
                ApplySpriteSwapTransition(button, stateMap, ctx);
            }
            else if (hasColorDifferences)
            {
                ApplyColorTintTransition(button, stateMap);
            }
            else
            {
                // States exist but no visual difference detected — use color tint with defaults
                button.transition = Selectable.Transition.ColorTint;
                _logger.Warn($"{componentSetNode.Name}: states detected but no visual difference found");
            }

            return true;
        }

        private void ApplyColorTintTransition(Button button, Dictionary<string, FigmaNode> states)
        {
            button.transition = Selectable.Transition.ColorTint;

            var colorBlock = button.colors;

            if (states.TryGetValue("Normal", out var normalNode))
                colorBlock.normalColor = GetPrimaryColor(normalNode);
            if (states.TryGetValue("Highlighted", out var hoverNode))
                colorBlock.highlightedColor = GetPrimaryColor(hoverNode);
            if (states.TryGetValue("Pressed", out var pressedNode))
                colorBlock.pressedColor = GetPrimaryColor(pressedNode);
            if (states.TryGetValue("Disabled", out var disabledNode))
                colorBlock.disabledColor = GetPrimaryColor(disabledNode);
            if (states.TryGetValue("Selected", out var selectedNode))
                colorBlock.selectedColor = GetPrimaryColor(selectedNode);

            colorBlock.fadeDuration = 0.1f;
            button.colors = colorBlock;

            _logger.Info("Applied ColorTint transition");
        }

        private void ApplySpriteSwapTransition(Button button, Dictionary<string, FigmaNode> states, ImportContext ctx)
        {
            button.transition = Selectable.Transition.SpriteSwap;

            var spriteState = new SpriteState();

            if (states.TryGetValue("Highlighted", out var hoverNode))
                spriteState.highlightedSprite = GetNodeSprite(hoverNode, ctx);
            if (states.TryGetValue("Pressed", out var pressedNode))
                spriteState.pressedSprite = GetNodeSprite(pressedNode, ctx);
            if (states.TryGetValue("Disabled", out var disabledNode))
                spriteState.disabledSprite = GetNodeSprite(disabledNode, ctx);
            if (states.TryGetValue("Selected", out var selectedNode))
                spriteState.selectedSprite = GetNodeSprite(selectedNode, ctx);

            button.spriteState = spriteState;

            _logger.Info("Applied SpriteSwap transition");
        }

        private UnityEngine.Color GetPrimaryColor(FigmaNode node)
        {
            if (node.Fills == null) return UnityEngine.Color.white;

            foreach (var fill in node.Fills)
            {
                if (fill.Visible && fill.IsSolid && fill.Color != null)
                    return ColorSpaceHelper.Convert(fill.Color, node.Opacity);
            }
            return UnityEngine.Color.white;
        }

        private Sprite GetNodeSprite(FigmaNode node, ImportContext ctx)
        {
            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
                return sprite;
            return null;
        }

        private struct NodeDiff
        {
            public bool ColorChanged;
            public bool SpriteChanged;
        }

        private NodeDiff CompareNodes(FigmaNode a, FigmaNode b)
        {
            var diff = new NodeDiff();

            var colorA = GetPrimaryFigmaColor(a);
            var colorB = GetPrimaryFigmaColor(b);

            if (colorA != null && colorB != null)
            {
                if (System.Math.Abs(colorA.R - colorB.R) > 0.01f ||
                    System.Math.Abs(colorA.G - colorB.G) > 0.01f ||
                    System.Math.Abs(colorA.B - colorB.B) > 0.01f ||
                    System.Math.Abs(colorA.A - colorB.A) > 0.01f)
                {
                    diff.ColorChanged = true;
                }
            }

            // Check if fill images differ
            var imgA = GetImageRef(a);
            var imgB = GetImageRef(b);
            if (imgA != imgB)
                diff.SpriteChanged = true;

            return diff;
        }

        private FigmaColor GetPrimaryFigmaColor(FigmaNode node)
        {
            if (node.Fills == null) return null;
            foreach (var fill in node.Fills)
            {
                if (fill.Visible && fill.IsSolid && fill.Color != null)
                    return fill.Color;
            }
            return null;
        }

        private string GetImageRef(FigmaNode node)
        {
            if (node.Fills == null) return null;
            foreach (var fill in node.Fills)
            {
                if (fill.Visible && fill.IsImage)
                    return fill.ImageRef;
            }
            return null;
        }
    }
}
