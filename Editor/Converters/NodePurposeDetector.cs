using System.Collections.Generic;
using System.Linq;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Util;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Auto-detects the UI purpose of a Figma node based on its structure,
    /// properties, and children — without requiring naming conventions.
    ///
    /// Heuristics:
    ///  - Button: interactive component with text + background, or instance of variant with states
    ///  - InputField: frame with single text child + placeholder text or underline/border
    ///  - Toggle: component with two states (on/off, checked/unchecked)
    ///  - ScrollView: frame with clipsContent + children taller/wider than the frame
    ///  - Slider: horizontal frame with a fill bar + handle
    /// </summary>
    internal sealed class NodePurposeDetector
    {
        private readonly ImportLogger _logger;
        private readonly Dictionary<string, FigmaComponent> _components;

        public NodePurposeDetector(ImportLogger logger, Dictionary<string, FigmaComponent> components)
        {
            _logger = logger;
            _components = components;
        }

        public enum DetectedPurpose
        {
            None,
            Button,
            InputField,
            Toggle,
            ScrollView,
            Slider,
            ProgressBar,
            Dropdown,
            TabBar
        }

        public DetectedPurpose Detect(FigmaNode node)
        {
            // Check from most specific to least specific

            if (IsScrollView(node))
                return DetectedPurpose.ScrollView;

            if (IsInputField(node))
                return DetectedPurpose.InputField;

            if (IsToggle(node))
                return DetectedPurpose.Toggle;

            if (IsSlider(node))
                return DetectedPurpose.Slider;

            if (IsButton(node))
                return DetectedPurpose.Button;

            return DetectedPurpose.None;
        }

        /// <summary>
        /// Button heuristics:
        /// - Has fills (background)
        /// - Has exactly 1-2 text children (label, maybe icon label)
        /// - Or is a component instance with state variants (Default/Hover/Pressed)
        /// - Corner radius > 0 (typical for buttons)
        /// - Relatively small size (not a full-screen panel)
        /// </summary>
        private bool IsButton(FigmaNode node)
        {
            if (!node.HasChildren) return false;
            if (node.Width > 600 || node.Height > 200) return false;

            // Check if it's a component instance with state-like variants
            if (node.NodeType == FigmaNodeType.INSTANCE && !string.IsNullOrEmpty(node.ComponentId))
            {
                if (_components.TryGetValue(node.ComponentId, out var comp))
                {
                    if (!string.IsNullOrEmpty(comp.ComponentSetId))
                        return true; // Part of a variant set → likely interactive
                }
            }

            // Structure check: has background fill + text child
            if (!node.HasVisibleFills) return false;

            int textCount = 0;
            int iconCount = 0;
            foreach (var child in node.Children)
            {
                if (child.NodeType == FigmaNodeType.TEXT)
                    textCount++;
                else if (IsIconLike(child))
                    iconCount++;
            }

            // Button: 1 text + optional icon, with background
            if (textCount >= 1 && textCount <= 2 && node.Children.Count <= 4)
            {
                // Extra confidence: has corner radius
                if (node.CornerRadius > 0)
                    return true;

                // Or auto-layout (buttons usually have padding)
                if (node.IsAutoLayout)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// InputField heuristics:
        /// - Horizontal auto-layout frame
        /// - Contains 1-2 TEXT children (value + placeholder)
        /// - Has border/stroke or underline effect
        /// - Wider than tall (text input aspect ratio)
        /// </summary>
        private bool IsInputField(FigmaNode node)
        {
            if (!node.HasChildren) return false;
            if (node.Width < 80 || node.Height > 100) return false;

            // Must be wider than tall
            if (node.Width <= node.Height * 1.5f) return false;

            int textCount = 0;
            bool hasPlaceholderHint = false;

            foreach (var child in node.Children)
            {
                if (child.NodeType == FigmaNodeType.TEXT)
                {
                    textCount++;
                    // Check for placeholder indicators
                    if (child.Opacity < 0.8f ||
                        (child.Name != null && (
                            child.Name.ToLower().Contains("placeholder") ||
                            child.Name.ToLower().Contains("hint") ||
                            child.Name.ToLower().Contains("label"))))
                    {
                        hasPlaceholderHint = true;
                    }
                }
            }

            if (textCount < 1 || textCount > 3) return false;

            // Has border/stroke
            bool hasStroke = node.Strokes != null && node.Strokes.Any(s => s.Visible);
            bool hasBottomBorder = hasStroke && node.StrokeAlign == "INSIDE";

            if (hasPlaceholderHint || hasStroke)
                return true;

            // Single text child in a bordered frame with cornerRadius
            if (textCount == 1 && node.CornerRadius > 0 && node.HasVisibleFills)
                return true;

            return false;
        }

        /// <summary>
        /// Toggle heuristics:
        /// - Component instance with variant names like "on/off", "checked/unchecked", "active/inactive"
        /// - Small square-ish size (checkbox) or pill shape (switch)
        /// </summary>
        private bool IsToggle(FigmaNode node)
        {
            if (node.NodeType != FigmaNodeType.INSTANCE) return false;
            if (string.IsNullOrEmpty(node.ComponentId)) return false;

            if (!_components.TryGetValue(node.ComponentId, out var comp)) return false;
            if (string.IsNullOrEmpty(comp.ComponentSetId)) return false;

            // Check name for toggle-like keywords
            var name = (comp.Name ?? "").ToLower();
            var setName = node.Name?.ToLower() ?? "";

            var toggleKeywords = new[] { "toggle", "switch", "checkbox", "check", "radio" };
            foreach (var kw in toggleKeywords)
            {
                if (name.Contains(kw) || setName.Contains(kw))
                    return true;
            }

            // Size heuristic: small and roughly square/pill
            if (node.Width <= 80 && node.Height <= 50)
            {
                // Check if variants suggest on/off states
                if (name.Contains("on") || name.Contains("off") ||
                    name.Contains("checked") || name.Contains("active"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// ScrollView heuristics:
        /// - clipsContent = true
        /// - Children total height > frame height (vertical scroll)
        ///   OR children total width > frame width (horizontal scroll)
        /// - Has auto-layout with overflow
        /// </summary>
        private bool IsScrollView(FigmaNode node)
        {
            if (!node.ClipsContent) return false;
            if (!node.HasChildren) return false;

            // Calculate total children extent
            float maxChildBottom = 0;
            float maxChildRight = 0;

            foreach (var child in node.Children)
            {
                if (child.AbsoluteBoundingBox == null) continue;

                float childBottom = child.AbsoluteBoundingBox.Y + child.AbsoluteBoundingBox.Height
                    - (node.AbsoluteBoundingBox?.Y ?? 0);
                float childRight = child.AbsoluteBoundingBox.X + child.AbsoluteBoundingBox.Width
                    - (node.AbsoluteBoundingBox?.X ?? 0);

                if (childBottom > maxChildBottom) maxChildBottom = childBottom;
                if (childRight > maxChildRight) maxChildRight = childRight;
            }

            // Content overflows → scroll view
            bool verticalOverflow = maxChildBottom > node.Height * 1.05f;
            bool horizontalOverflow = maxChildRight > node.Width * 1.05f;

            return verticalOverflow || horizontalOverflow;
        }

        /// <summary>
        /// Slider heuristics:
        /// - Horizontal frame
        /// - Contains a "track" (wide, thin rectangle) and a "handle" (small circle/square)
        /// - Or a fill bar that's a fraction of the total width
        /// </summary>
        private bool IsSlider(FigmaNode node)
        {
            if (!node.HasChildren) return false;
            if (node.Children.Count < 2 || node.Children.Count > 5) return false;

            // Must be wide and thin
            if (node.Width < node.Height * 3) return false;
            if (node.Height > 60) return false;

            bool hasTrack = false;
            bool hasHandle = false;

            foreach (var child in node.Children)
            {
                float ratio = child.Width / (child.Height + 0.001f);

                // Track: wide and thin
                if (ratio > 5 && child.Height < 20)
                    hasTrack = true;

                // Handle: roughly square, small
                if (child.Width < 40 && child.Height < 40 && ratio > 0.5f && ratio < 2f)
                    hasHandle = true;
            }

            return hasTrack && hasHandle;
        }

        /// <summary>
        /// Check if a node looks like an icon (small vector/image).
        /// </summary>
        private static bool IsIconLike(FigmaNode node)
        {
            if (node.Width > 48 || node.Height > 48) return false;

            var t = node.NodeType;
            return t == FigmaNodeType.VECTOR ||
                   t == FigmaNodeType.ELLIPSE ||
                   t == FigmaNodeType.STAR ||
                   t == FigmaNodeType.BOOLEAN_OPERATION ||
                   t == FigmaNodeType.INSTANCE ||
                   t == FigmaNodeType.FRAME;
        }
    }
}
