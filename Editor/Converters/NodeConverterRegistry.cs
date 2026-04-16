using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Registry that maps FigmaNode types to their converters.
    /// </summary>
    internal sealed class NodeConverterRegistry
    {
        private readonly List<INodeConverter> _converters;

        public NodeConverterRegistry()
        {
            _converters = new List<INodeConverter>
            {
                new TextConverter(),
                new VectorConverter(),
                new RectangleConverter(),
                new InstanceConverter(),
                new FrameConverter(), // Must be last: it's the catch-all for FRAME/GROUP/COMPONENT/etc.
            };
        }

        public INodeConverter GetConverter(FigmaNode node)
        {
            foreach (var converter in _converters)
            {
                if (converter.CanConvert(node))
                    return converter;
            }
            return null;
        }

        /// <summary>
        /// Check if a node type should be skipped entirely (not converted).
        /// </summary>
        public static bool ShouldSkip(FigmaNode node)
        {
            if (!node.Visible)
                return true;

            var t = node.NodeType;
            return t == FigmaNodeType.SLICE ||
                   t == FigmaNodeType.STICKY ||
                   t == FigmaNodeType.CONNECTOR ||
                   t == FigmaNodeType.WASHI_TAPE ||
                   t == FigmaNodeType.SHAPE_WITH_TEXT ||
                   t == FigmaNodeType.UNKNOWN;
        }

        /// <summary>
        /// Check if this node type needs rasterization via Figma Images API.
        /// </summary>
        public static bool NeedsRasterization(FigmaNode node)
        {
            var t = node.NodeType;

            // Vector types always need rasterization
            if (t == FigmaNodeType.VECTOR ||
                t == FigmaNodeType.ELLIPSE ||
                t == FigmaNodeType.STAR ||
                t == FigmaNodeType.LINE ||
                t == FigmaNodeType.REGULAR_POLYGON ||
                t == FigmaNodeType.BOOLEAN_OPERATION)
                return true;

            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (!fill.Visible || fill.Opacity <= 0f)
                        continue;

                    // Gradient fills need rasterization
                    if (fill.IsGradient)
                        return true;

                    // Image fills with FILL scaleMode (crop) need rasterization
                    // as the node itself to capture the correct crop
                    if (fill.IsImage && fill.ScaleMode == "FILL")
                        return true;
                }
            }

            // Nodes with visible effects (shadow, blur) need rasterization
            if (node.Effects != null)
            {
                foreach (var effect in node.Effects)
                {
                    if (effect.Visible)
                        return true;
                }
            }

            // Non-uniform corner radius on a filled frame needs rasterization
            if (node.RectangleCornerRadii != null && node.RectangleCornerRadii.Length == 4 &&
                node.HasVisibleFills)
            {
                var r = node.RectangleCornerRadii;
                if (r[0] != r[1] || r[1] != r[2] || r[2] != r[3])
                    return true;
            }

            // Nodes with isMask flag → always rasterize.
            // This captures the final mask shape including any clipsContent cropping.
            if (node.IsMask)
                return true;

            // Nodes with visible image fills → rasterize to capture image rendering
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a node is a mask shape container (has isMask child).
        /// Used by FrameConverter to apply Unity Mask component.
        /// </summary>
        public static bool IsMaskContainer(FigmaNode node)
        {
            if (node.Children == null) return false;
            foreach (var child in node.Children)
                if (child.IsMask) return true;
            return false;
        }
    }
}
