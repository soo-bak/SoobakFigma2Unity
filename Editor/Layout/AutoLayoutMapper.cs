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

            // Add the appropriate layout group
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

            // Child alignment from primaryAxisAlignItems + counterAxisAlignItems
            layoutGroup.childAlignment = ComputeAlignment(
                node.PrimaryAxisAlignItems,
                node.CounterAxisAlignItems,
                isHorizontal
            );

            // Child force expand: only true for SPACE_BETWEEN
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

            // Control child size: enabled so layout group manages sizing
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;

            // Scale width/height: disabled
            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;

            // ContentSizeFitter for auto-sizing
            ApplyContentSizeFitter(go, node, isHorizontal);
        }

        /// <summary>
        /// Apply LayoutElement to a child within an auto-layout parent.
        /// </summary>
        public static void ApplyChildLayoutProperties(GameObject childGo, FigmaNode childNode, FigmaNode parentNode)
        {
            if (!parentNode.IsAutoLayout)
                return;

            var layoutElement = childGo.AddComponent<LayoutElement>();
            var childSize = SizeCalculator.GetSize(childNode);

            var hSizing = childNode.LayoutSizingHorizontal ?? "FIXED";
            var vSizing = childNode.LayoutSizingVertical ?? "FIXED";

            // Horizontal sizing
            switch (hSizing)
            {
                case "FIXED":
                    layoutElement.preferredWidth = childSize.x;
                    layoutElement.flexibleWidth = -1;
                    break;
                case "FILL":
                    layoutElement.flexibleWidth = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    break;
                case "HUG":
                    layoutElement.flexibleWidth = -1;
                    // Will be sized by own ContentSizeFitter or preferred size
                    break;
            }

            // Vertical sizing
            switch (vSizing)
            {
                case "FIXED":
                    layoutElement.preferredHeight = childSize.y;
                    layoutElement.flexibleHeight = -1;
                    break;
                case "FILL":
                    layoutElement.flexibleHeight = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    break;
                case "HUG":
                    layoutElement.flexibleHeight = -1;
                    break;
            }

            // Minimum size
            layoutElement.minWidth = -1;
            layoutElement.minHeight = -1;
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
            // Map primary (along main axis) and counter (cross axis) to TextAnchor.
            // TextAnchor is: UpperLeft, UpperCenter, UpperRight,
            //                MiddleLeft, MiddleCenter, MiddleRight,
            //                LowerLeft, LowerCenter, LowerRight

            int row, col; // row: 0=Upper, 1=Middle, 2=Lower; col: 0=Left, 1=Center, 2=Right

            if (isHorizontal)
            {
                // Primary = horizontal (col), Counter = vertical (row)
                col = MapAlignToIndex(primaryAlign);
                row = MapAlignToIndex(counterAlign);
            }
            else
            {
                // Primary = vertical (row), Counter = horizontal (col)
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
                "SPACE_BETWEEN" => 0, // Fallback; SPACE_BETWEEN handled via childForceExpand
                "BASELINE" => 0,      // Fallback
                _ => 0
            };
        }
    }
}
