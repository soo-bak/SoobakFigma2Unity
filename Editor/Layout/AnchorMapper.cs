using SoobakFigma2Unity.Editor.Models;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Layout
{
    /// <summary>
    /// Maps Figma constraints to Unity RectTransform anchors and offsets.
    ///
    /// Figma coordinate system: origin top-left, Y increases downward.
    /// Unity RectTransform: anchor-relative, Y increases upward.
    ///
    /// Uses offsetMin/offsetMax (not anchoredPosition/sizeDelta) for non-stretch cases too,
    /// which is pivot-independent and more reliable.
    /// </summary>
    internal static class AnchorMapper
    {
        public static void Apply(RectTransform rt, FigmaNode node, FigmaNode parent)
        {
            if (node.AbsoluteBoundingBox == null || parent.AbsoluteBoundingBox == null)
                return;

            // Always reset pivot to center for consistency
            rt.pivot = new Vector2(0.5f, 0.5f);

            var constraints = node.Constraints;
            if (constraints == null)
            {
                ApplyDefault(rt, node, parent);
                return;
            }

            var childSize = SizeCalculator.GetSize(node);
            var parentSize = SizeCalculator.GetParentSize(parent);
            var (left, top, right, bottom) = SizeCalculator.GetMargins(node, parent);

            // Horizontal
            float anchorMinX, anchorMaxX, offsetMinX, offsetMaxX;
            ComputeHorizontal(constraints.HorizontalType, childSize.x, parentSize.x, left, right,
                out anchorMinX, out anchorMaxX, out offsetMinX, out offsetMaxX);

            // Vertical (Figma Y-down → Unity Y-up)
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
                    float hCenter = (leftMargin - rightMargin) / 2f;
                    offsetMin = hCenter - childWidth / 2f;
                    offsetMax = hCenter + childWidth / 2f;
                    break;

                case ConstraintType.STRETCH:
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
            switch (constraint)
            {
                case ConstraintType.MIN: // TOP in Figma → top in Unity (anchor=1)
                    anchorMin = 1f;
                    anchorMax = 1f;
                    offsetMin = -(topMargin + childHeight);
                    offsetMax = -topMargin;
                    break;

                case ConstraintType.MAX: // BOTTOM in Figma → bottom in Unity (anchor=0)
                    anchorMin = 0f;
                    anchorMax = 0f;
                    offsetMin = bottomMargin;
                    offsetMax = bottomMargin + childHeight;
                    break;

                case ConstraintType.CENTER:
                    anchorMin = 0.5f;
                    anchorMax = 0.5f;
                    float vCenter = (bottomMargin - topMargin) / 2f;
                    offsetMin = vCenter - childHeight / 2f;
                    offsetMax = vCenter + childHeight / 2f;
                    break;

                case ConstraintType.STRETCH:
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
        /// Default: top-left anchored with absolute position.
        /// </summary>
        private static void ApplyDefault(RectTransform rt, FigmaNode node, FigmaNode parent)
        {
            var childSize = SizeCalculator.GetSize(node);
            var (left, top, right, bottom) = SizeCalculator.GetMargins(node, parent);

            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -(top + childSize.y));
            rt.offsetMax = new Vector2(left + childSize.x, -top);
        }
    }
}
