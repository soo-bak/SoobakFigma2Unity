using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Maps Figma prototype interactions to FigmaInteractionHint components.
    /// Preserves the designer's intended navigation and animation for developer reference.
    /// </summary>
    internal static class InteractionMapper
    {
        /// <summary>
        /// Apply prototype interaction data to a GameObject if the node has interactions.
        /// </summary>
        public static void Apply(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (node.Interactions == null || node.Interactions.Count == 0)
                return;

            var hint = go.GetComponent<FigmaInteractionHint>();
            if (hint == null)
                hint = go.AddComponent<FigmaInteractionHint>();

            hint.ClearInteractions();

            foreach (var interaction in node.Interactions)
            {
                if (interaction == null || interaction.Trigger == null) continue;

                foreach (var action in interaction.Actions ?? new List<FigmaAction>())
                {
                    if (action == null) continue;

                    var data = new FigmaInteractionHint.InteractionData
                    {
                        Trigger = FormatTriggerType(interaction.Trigger.Type ?? ""),
                        TriggerTimeout = interaction.Trigger.Timeout,
                        ActionType = FormatActionType(action.Navigation ?? action.Type ?? ""),
                        DestinationNodeId = action.DestinationId ?? "",
                        Url = action.Url ?? ""
                    };

                    // Resolve destination name from node index
                    if (!string.IsNullOrEmpty(action.DestinationId) &&
                        ctx.NodeIndex.TryGetValue(action.DestinationId, out var destNode))
                    {
                        data.DestinationName = destNode.Name;
                    }

                    // Transition data
                    if (action.Transition != null)
                    {
                        data.TransitionType = FormatTransitionType(action.Transition.Type);
                        data.TransitionDirection = action.Transition.Direction ?? "";
                        data.Duration = action.Transition.Duration;

                        if (action.Transition.Easing != null)
                        {
                            data.EasingType = FormatEasingType(action.Transition.Easing.Type);

                            if (action.Transition.Easing.CubicBezier != null)
                            {
                                var cb = action.Transition.Easing.CubicBezier;
                                data.CustomBezier = new Vector4(cb.X1, cb.Y1, cb.X2, cb.Y2);
                            }
                        }
                    }

                    hint.AddInteraction(data);
                }
            }

            if (hint.Interactions.Count > 0)
            {
                ctx.Logger.Info($"{node.Name}: {hint.Interactions.Count} interaction(s) mapped");
            }
        }

        private static string FormatTriggerType(string type)
        {
            return type switch
            {
                "ON_CLICK" => "OnClick",
                "ON_HOVER" => "OnHover",
                "ON_PRESS" => "OnPress",
                "ON_DRAG" => "OnDrag",
                "AFTER_TIMEOUT" => "AfterTimeout",
                "MOUSE_ENTER" => "MouseEnter",
                "MOUSE_LEAVE" => "MouseLeave",
                "ON_KEY_DOWN" => "OnKeyDown",
                _ => type ?? "Unknown"
            };
        }

        private static string FormatActionType(string type)
        {
            return type switch
            {
                "NAVIGATE" => "Navigate",
                "SWAP" => "Swap",
                "OVERLAY" => "Overlay",
                "SCROLL_TO" => "ScrollTo",
                "BACK" => "Back",
                "CLOSE" => "Close",
                "URL" => "OpenURL",
                "NODE" => "Navigate",
                _ => type ?? "Unknown"
            };
        }

        private static string FormatTransitionType(string type)
        {
            return type switch
            {
                "DISSOLVE" => "Dissolve",
                "SMART_ANIMATE" => "SmartAnimate",
                "MOVE_IN" => "MoveIn",
                "MOVE_OUT" => "MoveOut",
                "PUSH" => "Push",
                "SLIDE_IN" => "SlideIn",
                "SLIDE_OUT" => "SlideOut",
                "INSTANT" => "Instant",
                _ => type ?? "None"
            };
        }

        private static string FormatEasingType(string type)
        {
            return type switch
            {
                "EASE_IN" => "EaseIn",
                "EASE_OUT" => "EaseOut",
                "EASE_IN_AND_OUT" => "EaseInOut",
                "LINEAR" => "Linear",
                "EASE_IN_BACK" => "EaseInBack",
                "EASE_OUT_BACK" => "EaseOutBack",
                "EASE_IN_AND_OUT_BACK" => "EaseInOutBack",
                "CUSTOM_CUBIC_BEZIER" => "Custom",
                _ => type ?? "Linear"
            };
        }
    }
}
