using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts vector-type nodes (VECTOR, ELLIPSE, STAR, LINE, REGULAR_POLYGON,
    /// BOOLEAN_OPERATION) to Image components using rasterized images from Figma API.
    /// </summary>
    internal sealed class VectorConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node)
        {
            var t = node.NodeType;
            return t == FigmaNodeType.VECTOR ||
                   t == FigmaNodeType.ELLIPSE ||
                   t == FigmaNodeType.STAR ||
                   t == FigmaNodeType.LINE ||
                   t == FigmaNodeType.REGULAR_POLYGON ||
                   t == FigmaNodeType.BOOLEAN_OPERATION;
        }

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = new GameObject(node.Name);
            go.AddComponent<RectTransform>();
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            var nodeRef = go.AddComponent<FigmaNodeRef>();
            nodeRef.FigmaNodeId = node.Id;

            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
            {
                ctx.Logger.Warn($"{node.Name}: vector node has no rasterized image");
            }

            // Opacity
            if (node.Opacity < 1f)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            return go;
        }
    }
}
