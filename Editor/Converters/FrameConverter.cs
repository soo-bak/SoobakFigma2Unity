using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Color;
using SoobakFigma2Unity.Editor.Layout;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts FRAME and GROUP nodes to GameObjects with RectTransform.
    /// Adds Image component if the frame has fills.
    /// Adds LayoutGroup if the frame uses auto-layout.
    /// Adds Mask if clipsContent is true.
    /// </summary>
    internal sealed class FrameConverter : INodeConverter
    {
        public bool CanConvert(FigmaNode node)
        {
            var t = node.NodeType;
            return t == FigmaNodeType.FRAME || t == FigmaNodeType.GROUP ||
                   t == FigmaNodeType.COMPONENT || t == FigmaNodeType.COMPONENT_SET ||
                   t == FigmaNodeType.SECTION;
        }

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            var go = CreateGameObject(node, parent);
            var rt = go.GetComponent<RectTransform>();

            // Add FigmaNodeRef for re-import tracking
            var nodeRef = go.AddComponent<FigmaNodeRef>();
            nodeRef.FigmaNodeId = node.Id;

            // Apply fills
            ApplyFills(go, node, ctx);

            // Apply blend mode
            var image_ = go.GetComponent<Image>();
            if (image_ != null && !string.IsNullOrEmpty(node.BlendMode))
                BlendModeHelper.TryApply(image_, node.BlendMode, ctx.Logger);

            // isMask FRAME: this defines a sibling-style mask in Figma.
            // ConvertChildren reparents subsequent siblings under this GameObject;
            // Unity's Mask component then clips them to this frame's alpha shape.
            // The mask shape sprite is taken from this node's own rasterization,
            // or from a child vector's sprite (typical: bubble_mask FRAME with VECTOR child).
            if (node.IsMask)
            {
                var image = go.GetComponent<Image>();
                if (image == null)
                {
                    image = go.AddComponent<Image>();
                    Sprite shapeSprite = null;
                    if (ctx.NodeSprites.TryGetValue(node.Id, out var ownSprite))
                        shapeSprite = ownSprite;
                    else if (node.Children != null)
                    {
                        foreach (var ch in node.Children)
                        {
                            if (ctx.NodeSprites.TryGetValue(ch.Id, out var childSprite))
                            {
                                shapeSprite = childSprite;
                                break;
                            }
                        }
                    }
                    if (shapeSprite != null)
                        image.sprite = shapeSprite;
                    else
                        image.color = UnityEngine.Color.white;
                }
                var mask = go.AddComponent<Mask>();
                mask.showMaskGraphic = false; // mask shape is invisible; only its alpha clips children
            }
            // Clips content → Mask (clips own children to this frame's bounds)
            else if (node.ClipsContent)
            {
                var image = go.GetComponent<Image>();
                if (image == null)
                {
                    image = go.AddComponent<Image>();
                    image.color = UnityEngine.Color.white;
                }
                go.AddComponent<Mask>().showMaskGraphic = image.sprite != null;
            }

            // Opacity
            if (node.Opacity < 1f)
            {
                var canvasGroup = go.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.Opacity;
            }

            return go;
        }

        private GameObject CreateGameObject(FigmaNode node, GameObject parent)
        {
            var go = new GameObject(node.Name);
            go.AddComponent<RectTransform>();

            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            return go;
        }

        private void ApplyFills(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (!node.HasVisibleFills)
                return;

            // Check if we can use solid color optimization
            if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
            {
                var color = SolidColorOptimizer.GetTopSolidColor(node);
                if (color != null)
                {
                    var image = go.AddComponent<Image>();
                    image.color = ColorSpaceHelper.Convert(color, node.Opacity);
                    return;
                }
            }

            // Check for rasterized image
            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                // Use Sliced if sprite has borders (9-slice)
                image.type = (sprite.border != UnityEngine.Vector4.zero)
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                image.preserveAspect = false;
                return;
            }

            // Check for image fill
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && fill.ImageRef != null)
                    {
                        if (ctx.FillSprites.TryGetValue(fill.ImageRef, out var fillSprite))
                        {
                            var image = go.AddComponent<Image>();
                            image.sprite = fillSprite;
                            image.type = Image.Type.Simple;
                            image.preserveAspect = fill.ScaleMode == "FIT";
                            return;
                        }
                    }
                }
            }
        }
    }
}
