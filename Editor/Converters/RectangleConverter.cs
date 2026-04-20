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
    /// Converts RECTANGLE nodes to Image components — but only when there is
    /// actual visual content. Empty rectangles (no fill, no sprite) get just
    /// a RectTransform so they don't render as blank white blocks that occlude
    /// content beneath them.
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

            // Decide visual content (don't add Image until we have something to show)
            Sprite chosenSprite = null;
            UnityEngine.Color? chosenColor = null;

            // 1) Rasterized sprite (vector op, image-fill cropped, etc.)
            if (ctx.NodeSprites.TryGetValue(node.Id, out var nodeSprite))
            {
                chosenSprite = nodeSprite;
            }
            // 2) Solid color optimization
            else if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
            {
                var color = SolidColorOptimizer.GetTopSolidColor(node);
                if (color != null)
                    chosenColor = ColorSpaceHelper.Convert(color, node.Opacity);
            }
            // 3) Raw image fill (uncropped — fallback when node wasn't rasterized)
            else if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && fill.ImageRef != null &&
                        ctx.FillSprites.TryGetValue(fill.ImageRef, out var fillSprite))
                    {
                        chosenSprite = fillSprite;
                        break;
                    }
                }
            }

            // Only add Image when there is actual content to render
            if (chosenSprite != null || chosenColor != null)
            {
                var image = go.AddComponent<Image>();
                if (chosenSprite != null)
                {
                    image.sprite = chosenSprite;
                    image.type = (chosenSprite.border != UnityEngine.Vector4.zero)
                        ? Image.Type.Sliced
                        : Image.Type.Simple;
                    image.color = UnityEngine.Color.white;
                }
                else
                {
                    image.color = chosenColor.Value;
                }

                // Opacity adjustment
                if (node.Opacity < 1f && image.color.a >= 1f)
                {
                    var c = image.color;
                    c.a = node.Opacity;
                    image.color = c;
                }
            }

            if (node.CornerRadius > 0)
                ctx.Logger.Info($"{node.Name}: cornerRadius={node.CornerRadius}px (9-slice candidate)");

            // isMask RECTANGLE: in Figma the mask shape itself is invisible — it only
            // defines the clipping alpha for subsequent siblings. Add Unity Mask with
            // showMaskGraphic=false so the rectangle is hidden but its alpha clips
            // children that ConvertChildren reparents under it.
            if (node.IsMask)
            {
                var maskImage = go.GetComponent<Image>();
                if (maskImage == null)
                {
                    // No image was added (e.g., not rasterized) — use solid white as alpha source
                    maskImage = go.AddComponent<Image>();
                    maskImage.color = UnityEngine.Color.white;
                }
                if (go.GetComponent<Mask>() == null)
                    go.AddComponent<Mask>().showMaskGraphic = false;
            }

            return go;
        }
    }
}
