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

            // Frames/rectangles with gradient fills need rasterization
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.Opacity > 0f && fill.IsGradient)
                        return true;
                }
            }

            return false;
        }
    }
}
