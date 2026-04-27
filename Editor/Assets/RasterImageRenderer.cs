using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Assets
{
    internal static class RasterImageRenderer
    {
        public static Image Apply(
            GameObject nodeGo,
            FigmaNode node,
            ImportContext ctx,
            Sprite sprite,
            bool raycastTarget = false,
            bool forceSameObject = false)
        {
            if (nodeGo == null || node == null || sprite == null)
                return null;

            if (!forceSameObject && ShouldUseRenderBoundsChild(node, ctx))
            {
                var visualGo = new GameObject($"{node.Name} Raster");
                visualGo.transform.SetParent(nodeGo.transform, false);
                visualGo.transform.SetSiblingIndex(0);
                ctx.NodeIdentities[visualGo.transform] =
                    new ImportContext.NodeIdentityRecord($"{node.Id}#raster", null);

                var visualRt = visualGo.AddComponent<RectTransform>();
                ApplyRenderBoundsRect(visualRt, node);

                var childImage = visualGo.AddComponent<Image>();
                Configure(childImage, sprite, raycastTarget);
                return childImage;
            }

            var image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
            Configure(image, sprite, raycastTarget);
            return image;
        }

        public static void Configure(Image image, Sprite sprite, bool raycastTarget = false)
        {
            if (image == null || sprite == null)
                return;

            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
            image.raycastTarget = raycastTarget;
        }

        private static bool ShouldUseRenderBoundsChild(FigmaNode node, ImportContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(node.Id))
                return false;
            if (!ctx.NodeRasterBoundsModes.TryGetValue(node.Id, out var mode) ||
                mode != RasterBoundsMode.RenderBounds)
                return false;
            if (node.AbsoluteBoundingBox == null || node.AbsoluteRenderBounds == null)
                return false;
            if (node.AbsoluteRenderBounds.Width <= 0f || node.AbsoluteRenderBounds.Height <= 0f)
                return false;

            return BoundsDiffer(node.AbsoluteBoundingBox, node.AbsoluteRenderBounds);
        }

        private static void ApplyRenderBoundsRect(RectTransform rt, FigmaNode node)
        {
            var layout = node.AbsoluteBoundingBox;
            var render = node.AbsoluteRenderBounds;
            var x = render.X - layout.X;
            var y = render.Y - layout.Y;
            var width = render.Width;
            var height = render.Height;

            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(x, -(y + height));
            rt.offsetMax = new Vector2(x + width, -y);
        }

        private static bool BoundsDiffer(FigmaRectangle a, FigmaRectangle b)
        {
            const float epsilon = 0.25f;
            return Mathf.Abs(a.X - b.X) > epsilon ||
                   Mathf.Abs(a.Y - b.Y) > epsilon ||
                   Mathf.Abs(a.Width - b.Width) > epsilon ||
                   Mathf.Abs(a.Height - b.Height) > epsilon;
        }
    }
}
