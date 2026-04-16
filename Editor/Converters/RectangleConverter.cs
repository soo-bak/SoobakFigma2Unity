using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts RECTANGLE nodes to Image components.
    /// </summary>
    internal sealed class RectangleConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node) => node.NodeType == FigmaNodeType.RECTANGLE;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = new GameObject(node.Name);
            go.AddComponent<RectTransform>();
            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            var nodeRef = go.AddComponent<FigmaNodeRef>();
            nodeRef.FigmaNodeId = node.Id;

            var image = go.AddComponent<Image>();

            // Solid color optimization
            if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
            {
                var color = SolidColorOptimizer.GetTopSolidColor(node);
                image.color = color != null
                    ? ColorSpaceHelper.Convert(color, node.Opacity)
                    : UnityEngine.Color.clear;
            }
            else if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.color = UnityEngine.Color.white;
            }
            else if (node.Fills != null)
            {
                // Try image fill
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && fill.ImageRef != null &&
                        ctx.FillSprites.TryGetValue(fill.ImageRef, out var fillSprite))
                    {
                        image.sprite = fillSprite;
                        break;
                    }
                }
            }

            // Apply corner radius hint for 9-slice (logged for user awareness)
            if (node.CornerRadius > 0)
            {
                ctx.Logger.Info($"{node.Name}: cornerRadius={node.CornerRadius}px (9-slice candidate)");
            }

            // Opacity
            if (node.Opacity < 1f && image.color.a >= 1f)
            {
                var c = image.color;
                c.a = node.Opacity;
                image.color = c;
            }

            return go;
        }
    }
}
