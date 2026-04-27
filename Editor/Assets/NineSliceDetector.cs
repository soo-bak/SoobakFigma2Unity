using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Util;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Detects 9-slice candidates from Figma node properties and
    /// calculates appropriate sprite border values.
    ///
    /// Detection is intentionally conservative: only simple rounded visuals that Figma
    /// constraints/layout say should scale are sliced. Everything else keeps the exact
    /// Figma raster so Unity does not reshape curves or edge pixels.
    /// </summary>
    internal sealed class NineSliceDetector
    {
        private readonly ImportLogger _logger;
        private readonly float _imageScale;

        public NineSliceDetector(ImportLogger logger, float imageScale)
        {
            _logger = logger;
            _imageScale = imageScale;
        }

        /// <summary>
        /// Check if this node is a 9-slice candidate and return border values if so.
        /// Returns Vector4(left, bottom, right, top) in pixel coordinates of the exported image.
        /// Returns Vector4.zero if not a 9-slice candidate.
        /// </summary>
        public Vector4 DetectBorders(FigmaNode node)
        {
            if (!IsCandidate(node))
                return Vector4.zero;

            // Non-uniform corner radii
            if (node.RectangleCornerRadii != null && node.RectangleCornerRadii.Length == 4)
            {
                return CalculateNonUniformBorders(node);
            }

            // Uniform corner radius
            if (node.CornerRadius > 0)
            {
                return CalculateUniformBorders(node);
            }

            return Vector4.zero;
        }

        /// <summary>
        /// Check if a node is a 9-slice candidate.
        /// </summary>
        public bool IsCandidate(FigmaNode node)
        {
            if (!ShouldScaleLikeNineSlice(node))
                return false;

            if (!HasRoundedCorners(node))
                return false;

            if (node.Width < 16 || node.Height < 16)
                return false;

            if (!IsSimpleSlicableVisual(node))
                return false;

            return true;
        }

        private static bool ShouldScaleLikeNineSlice(FigmaNode node)
        {
            if (node.Constraints != null &&
                (node.Constraints.HorizontalType == ConstraintType.STRETCH ||
                 node.Constraints.VerticalType == ConstraintType.STRETCH ||
                 node.Constraints.HorizontalType == ConstraintType.SCALE ||
                 node.Constraints.VerticalType == ConstraintType.SCALE))
                return true;

            return node.LayoutGrow > 0f ||
                   node.LayoutAlign == "STRETCH" ||
                   node.LayoutSizingHorizontal == "FILL" ||
                   node.LayoutSizingVertical == "FILL";
        }

        private static bool HasRoundedCorners(FigmaNode node)
        {
            if (node.CornerRadius > 0f)
                return true;
            if (node.RectangleCornerRadii == null || node.RectangleCornerRadii.Length != 4)
                return false;
            return node.RectangleCornerRadii[0] > 0f ||
                   node.RectangleCornerRadii[1] > 0f ||
                   node.RectangleCornerRadii[2] > 0f ||
                   node.RectangleCornerRadii[3] > 0f;
        }

        private static bool IsSimpleSlicableVisual(FigmaNode node)
        {
            var t = node.NodeType;
            if (t != FigmaNodeType.FRAME && t != FigmaNodeType.GROUP &&
                t != FigmaNodeType.RECTANGLE && t != FigmaNodeType.COMPONENT &&
                t != FigmaNodeType.INSTANCE)
                return false;

            if (!node.HasVisibleFills)
                return false;

            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (!fill.Visible || fill.Opacity <= 0f)
                        continue;
                    if (!fill.IsSolid)
                        return false;
                }
            }

            if (node.Effects != null)
            {
                foreach (var effect in node.Effects)
                    if (effect.Visible)
                        return false;
            }

            if (node.StrokeAlign == "OUTSIDE")
                return false;

            return true;
        }

        /// <summary>
        /// Calculate borders from uniform corner radius.
        /// Border size = cornerRadius + small padding to include the full curve.
        /// </summary>
        private Vector4 CalculateUniformBorders(FigmaNode node)
        {
            float radius = node.CornerRadius;

            // Scale the radius to match exported image resolution
            float border = Mathf.CeilToInt(radius * _imageScale);

            // Ensure border doesn't exceed half the image dimension
            float maxH = MaxBorderWithCenter(node.Width);
            float maxV = MaxBorderWithCenter(node.Height);
            border = Mathf.Min(border, Mathf.Min(maxH, maxV));

            if (border <= 0)
                return Vector4.zero;

            _logger.Info($"{node.Name}: 9-slice detected (uniform r={radius}px, border={border}px)");

            // Vector4(left, bottom, right, top)
            return new Vector4(border, border, border, border);
        }

        /// <summary>
        /// Calculate borders from non-uniform corner radii.
        /// RectangleCornerRadii = [topLeft, topRight, bottomRight, bottomLeft]
        /// </summary>
        private Vector4 CalculateNonUniformBorders(FigmaNode node)
        {
            var radii = node.RectangleCornerRadii;
            float topLeft = radii[0];
            float topRight = radii[1];
            float bottomRight = radii[2];
            float bottomLeft = radii[3];

            // Left border = max of topLeft, bottomLeft
            float left = Mathf.Max(topLeft, bottomLeft) * _imageScale;
            // Right border = max of topRight, bottomRight
            float right = Mathf.Max(topRight, bottomRight) * _imageScale;
            // Top border = max of topLeft, topRight
            float top = Mathf.Max(topLeft, topRight) * _imageScale;
            // Bottom border = max of bottomLeft, bottomRight
            float bottom = Mathf.Max(bottomLeft, bottomRight) * _imageScale;

            // Clamp to half dimensions
            float maxH = MaxBorderWithCenter(node.Width);
            float maxV = MaxBorderWithCenter(node.Height);
            left = Mathf.Min(Mathf.CeilToInt(left), maxH);
            right = Mathf.Min(Mathf.CeilToInt(right), maxH);
            top = Mathf.Min(Mathf.CeilToInt(top), maxV);
            bottom = Mathf.Min(Mathf.CeilToInt(bottom), maxV);

            if (left <= 0 && right <= 0 && top <= 0 && bottom <= 0)
                return Vector4.zero;

            _logger.Info($"{node.Name}: 9-slice detected (non-uniform, borders L={left} B={bottom} R={right} T={top})");

            return new Vector4(left, bottom, right, top);
        }

        private float MaxBorderWithCenter(float figmaSize)
        {
            // Unity sliced sprites need a non-empty center region. A pill with
            // radius == height / 2 is common in Figma; using that exact value makes
            // top + bottom consume the whole sprite and the sliced image degenerates.
            return Mathf.Max(0f, Mathf.Floor((figmaSize * _imageScale - 1f) / 2f));
        }
    }
}
