using SoobakFigma2Unity.Editor.Models;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Layout
{
    /// <summary>
    /// Maps Figma constraints to Unity RectTransform anchors and offsets.
    ///
    /// Figma coordinate system: origin top-left, Y increases downward.
    /// Unity RectTransform: anchor-relative, Y increases upward.
    /// </summary>
    internal static class AnchorMapper
    {
        public static void Apply(RectTransform rt, FigmaNode node, FigmaNode parent)
        {
            if (node.AbsoluteBoundingBox == null || parent.AbsoluteBoundingBox == null)
                return;

            var constraints = node.Constraints;
            if (constraints == null)
            {
                // Default: top-left anchored
                ApplyFixed(rt, node, parent);
                return;
            }

            var childSize = SizeCalculator.GetSize(node);
            var parentSize = SizeCalculator.GetParentSize(parent);
            var (left, top, right, bottom) = SizeCalculator.GetMargins(node, parent);

            // Horizontal
            float anchorMinX, anchorMaxX, offsetMinX, offsetMaxX;
            ComputeHorizontal(constraints.HorizontalType, childSize.x, parentSize.x, left, right,
                out anchorMinX, out anchorMaxX, out offsetMinX, out offsetMaxX);

            // Vertical (Figma Y-down → Unity Y-up: swap top/bottom roles)
            float anchorMinY, anchorMaxY, offsetMinY, offsetMaxY;
            ComputeVertical(constraints.VerticalType, childSize.y, parentSize.y, top, bottom,
                out anchorMinY, out anchorMaxY, out offsetMinY, out offsetMaxY);

            rt.anchorMin = new Vector2(anchorMinX, anchorMinY);
            rt.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
            rt.offsetMin = new Vector2(offsetMinX, offsetMinY);
            rt.offsetMax = new Vector2(offsetMaxX, offsetMaxY);
        }

        private static void ComputeHorizontal(
            ConstraintType constraint, float childWidth, float parentWidth,
            float leftMargin, float rightMargin,
            out float anchorMin, out float anchorMax,
            out float offsetMin, out float offsetMax)
        {
            switch (constraint)
            {
                case ConstraintType.MIN: // LEFT
                    anchorMin = 0f;
                    anchorMax = 0f;
                    offsetMin = leftMargin;
                    offsetMax = leftMargin + childWidth;
                    break;

                case ConstraintType.MAX: // RIGHT
                    anchorMin = 1f;
                    anchorMax = 1f;
                    offsetMin = -(rightMargin + childWidth);
                    offsetMax = -rightMargin;
                    break;

                case ConstraintType.CENTER:
                    anchorMin = 0.5f;
                    anchorMax = 0.5f;
                    float centerOffset = leftMargin - (parentWidth - childWidth) / 2f;
                    offsetMin = centerOffset;
                    offsetMax = centerOffset + childWidth;
                    break;

                case ConstraintType.STRETCH: // LEFT_RIGHT
                    anchorMin = 0f;
                    anchorMax = 1f;
                    offsetMin = leftMargin;
                    offsetMax = -rightMargin;
                    break;

                case ConstraintType.SCALE:
                    anchorMin = parentWidth > 0 ? leftMargin / parentWidth : 0f;
                    anchorMax = parentWidth > 0 ? (leftMargin + childWidth) / parentWidth : 1f;
                    offsetMin = 0f;
                    offsetMax = 0f;
                    break;

                default:
                    goto case ConstraintType.MIN;
            }
        }

        private static void ComputeVertical(
            ConstraintType constraint, float childHeight, float parentHeight,
            float topMargin, float bottomMargin,
            out float anchorMin, out float anchorMax,
            out float offsetMin, out float offsetMax)
        {
            // Unity Y-up: anchorMin.y=0 is bottom, anchorMin.y=1 is top.
            // Figma Y-down: TOP constraint means element stays near top (Unity anchor=1).
            switch (constraint)
            {
                case ConstraintType.MIN: // TOP in Figma
                    anchorMin = 1f;
                    anchorMax = 1f;
                    offsetMin = -(topMargin + childHeight);
                    offsetMax = -topMargin;
                    break;

                case ConstraintType.MAX: // BOTTOM in Figma
                    anchorMin = 0f;
                    anchorMax = 0f;
                    offsetMin = bottomMargin;
                    offsetMax = bottomMargin + childHeight;
                    break;

                case ConstraintType.CENTER:
                    anchorMin = 0.5f;
                    anchorMax = 0.5f;
                    float centerOffset = (parentHeight - childHeight) / 2f - topMargin;
                    offsetMin = -centerOffset - childHeight / 2f;
                    offsetMax = -centerOffset + childHeight / 2f;
                    break;

                case ConstraintType.STRETCH: // TOP_BOTTOM
                    anchorMin = 0f;
                    anchorMax = 1f;
                    offsetMin = bottomMargin;
                    offsetMax = -topMargin;
                    break;

                case ConstraintType.SCALE:
                    anchorMin = parentHeight > 0 ? bottomMargin / parentHeight : 0f;
                    anchorMax = parentHeight > 0 ? (bottomMargin + childHeight) / parentHeight : 1f;
                    offsetMin = 0f;
                    offsetMax = 0f;
                    break;

                default:
                    goto case ConstraintType.MIN;
            }
        }

        /// <summary>
        /// Default fixed positioning: top-left anchored.
        /// </summary>
        private static void ApplyFixed(RectTransform rt, FigmaNode node, FigmaNode parent)
        {
            var childSize = SizeCalculator.GetSize(node);
            var (left, top, _, _) = SizeCalculator.GetMargins(node, parent);

            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = childSize;
        }
    }
}
