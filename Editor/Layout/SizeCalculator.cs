using SoobakFigma2Unity.Editor.Models;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Layout
{
    /// <summary>
    /// Converts Figma coordinate system (Y-down, absolute) to
    /// Unity RectTransform coordinates (Y-up, relative to parent).
    /// </summary>
    internal static class SizeCalculator
    {
        /// <summary>
        /// Compute the position of a child relative to its parent in Figma coordinates.
        /// Returns (relX, relY) where relX is offset from parent left, relY is offset from parent top.
        /// </summary>
        public static Vector2 GetRelativePosition(FigmaNode child, FigmaNode parent)
        {
            if (child.AbsoluteBoundingBox == null || parent.AbsoluteBoundingBox == null)
                return Vector2.zero;

            float relX = child.AbsoluteBoundingBox.X - parent.AbsoluteBoundingBox.X;
            float relY = child.AbsoluteBoundingBox.Y - parent.AbsoluteBoundingBox.Y;
            return new Vector2(relX, relY);
        }

        /// <summary>
        /// Get the size of a node as a Vector2.
        /// </summary>
        public static Vector2 GetSize(FigmaNode node)
        {
            if (node.AbsoluteBoundingBox != null)
            {
                float w = node.AbsoluteBoundingBox.Width;
                float h = node.AbsoluteBoundingBox.Height;

                // Vector paths with zero thickness on one axis (a horizontal or
                // vertical stroke, e.g. a 4-px dotted divider) report a degenerate
                // bounding box — width or height = 0 — because Figma's bbox is the
                // geometric path extent and ignores stroke. The actual rendered
                // extent (stroke included) is in absoluteRenderBounds. Without this
                // fallback the element would land in Unity with sizeDelta.y = 0
                // and the LayoutElement that the auto-layout mapper attaches would
                // get preferredHeight = 0, hiding the row entirely.
                if (node.AbsoluteRenderBounds != null)
                {
                    if (w <= 0f && node.AbsoluteRenderBounds.Width > 0f)
                        w = node.AbsoluteRenderBounds.Width;
                    if (h <= 0f && node.AbsoluteRenderBounds.Height > 0f)
                        h = node.AbsoluteRenderBounds.Height;
                }
                return new Vector2(w, h);
            }
            if (node.Size != null)
                return new Vector2(node.Size.X, node.Size.Y);
            return Vector2.zero;
        }

        /// <summary>
        /// Get parent size.
        /// </summary>
        public static Vector2 GetParentSize(FigmaNode parent)
        {
            return GetSize(parent);
        }

        /// <summary>
        /// Compute margins (left, top, right, bottom) of a child within its parent.
        /// </summary>
        public static (float left, float top, float right, float bottom) GetMargins(FigmaNode child, FigmaNode parent)
        {
            var relPos = GetRelativePosition(child, parent);
            var childSize = GetSize(child);
            var parentSize = GetParentSize(parent);

            float left = relPos.x;
            float top = relPos.y;
            float right = parentSize.x - (relPos.x + childSize.x);
            float bottom = parentSize.y - (relPos.y + childSize.y);

            return (left, top, right, bottom);
        }
    }
}
