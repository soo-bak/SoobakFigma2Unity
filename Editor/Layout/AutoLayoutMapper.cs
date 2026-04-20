using SoobakFigma2Unity.Editor.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Layout
{
    /// <summary>
    /// Maps Figma Auto Layout to Unity LayoutGroup + ContentSizeFitter.
    /// </summary>
    internal static class AutoLayoutMapper
    {
        public static void Apply(GameObject go, FigmaNode node)
        {
            if (!node.IsAutoLayout)
                return;

            var isHorizontal = node.LayoutMode == "HORIZONTAL";

            HorizontalOrVerticalLayoutGroup layoutGroup;
            if (isHorizontal)
                layoutGroup = go.AddComponent<HorizontalLayoutGroup>();
            else
                layoutGroup = go.AddComponent<VerticalLayoutGroup>();

            // Padding
            layoutGroup.padding = new RectOffset(
                Mathf.RoundToInt(node.PaddingLeft),
                Mathf.RoundToInt(node.PaddingRight),
                Mathf.RoundToInt(node.PaddingTop),
                Mathf.RoundToInt(node.PaddingBottom)
            );

            // Spacing
            layoutGroup.spacing = node.ItemSpacing;

            // Child alignment
            layoutGroup.childAlignment = ComputeAlignment(
                node.PrimaryAxisAlignItems,
                node.CounterAxisAlignItems,
                isHorizontal
            );

            // SPACE_BETWEEN
            bool spaceAlong = node.PrimaryAxisAlignItems == "SPACE_BETWEEN";
            if (isHorizontal)
            {
                layoutGroup.childForceExpandWidth = spaceAlong;
                layoutGroup.childForceExpandHeight = false;
            }
            else
            {
                layoutGroup.childForceExpandWidth = false;
                layoutGroup.childForceExpandHeight = spaceAlong;
            }

            // IMPORTANT: childControl must be FALSE to prevent LayoutGroup from
            // overriding child sizeDelta. Unity rebuilds layout when LayoutElement
            // is added with default values (preferredSize=-1), causing sizeDelta
            // to become (0,0) before our preferredWidth/Height values are set.
            // Children manage their own sizeDelta via ApplyChildLayoutProperties.
            layoutGroup.childControlWidth = false;
            layoutGroup.childControlHeight = false;
            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;

            // ContentSizeFitter for auto-sizing parent
            ApplyContentSizeFitter(go, node, isHorizontal);
        }

        /// <summary>
        /// Apply LayoutElement + sizeDelta to a child within an auto-layout parent.
        /// This is critical: Unity LayoutGroup needs both LayoutElement AND sizeDelta
        /// to correctly size children.
        /// </summary>
        public static void ApplyChildLayoutProperties(GameObject childGo, FigmaNode childNode, FigmaNode parentNode)
        {
            if (!parentNode.IsAutoLayout)
                return;

            var rt = childGo.GetComponent<RectTransform>();
            var childSize = SizeCalculator.GetSize(childNode);

            // Reset anchors for layout children — LayoutGroup will manage positioning
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = childSize;

            var layoutElement = childGo.AddComponent<LayoutElement>();

            var hSizing = childNode.LayoutSizingHorizontal ?? "FIXED";
            var vSizing = childNode.LayoutSizingVertical ?? "FIXED";

            // Horizontal
            switch (hSizing)
            {
                case "FIXED":
                    layoutElement.preferredWidth = childSize.x;
                    layoutElement.minWidth = childSize.x;
                    break;
                case "FILL":
                    layoutElement.flexibleWidth = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    break;
                case "HUG":
                    // Prefer content-driven size
                    break;
            }

            // Vertical
            switch (vSizing)
            {
                case "FIXED":
                    layoutElement.preferredHeight = childSize.y;
                    layoutElement.minHeight = childSize.y;
                    break;
                case "FILL":
                    layoutElement.flexibleHeight = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    break;
                case "HUG":
                    break;
            }
        }

        private static void ApplyContentSizeFitter(GameObject go, FigmaNode node, bool isHorizontal)
        {
            var primaryAuto = node.PrimaryAxisSizingMode == "AUTO";
            var counterAuto = node.CounterAxisSizingMode == "AUTO";

            if (!primaryAuto && !counterAuto)
                return;

            var fitter = go.AddComponent<ContentSizeFitter>();

            if (isHorizontal)
            {
                fitter.horizontalFit = primaryAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = counterAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                fitter.verticalFit = primaryAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                fitter.horizontalFit = counterAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        private static TextAnchor ComputeAlignment(
            string primaryAlign, string counterAlign, bool isHorizontal)
        {
            int row, col;

            if (isHorizontal)
            {
                col = MapAlignToIndex(primaryAlign);
                row = MapAlignToIndex(counterAlign);
            }
            else
            {
                row = MapAlignToIndex(primaryAlign);
                col = MapAlignToIndex(counterAlign);
            }

            return (TextAnchor)(row * 3 + col);
        }

        private static int MapAlignToIndex(string align)
        {
            return align switch
            {
                "MIN" => 0,
                "CENTER" => 1,
                "MAX" => 2,
                "SPACE_BETWEEN" => 0,
                "BASELINE" => 0,
                _ => 0
            };
        }
    }
}
