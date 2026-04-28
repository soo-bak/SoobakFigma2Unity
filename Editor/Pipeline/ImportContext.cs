using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Shared state for a single import operation.
    /// Passed to all converters so they can access images, settings, etc.
    /// </summary>
    internal sealed class ImportContext
    {
        /// <summary>Import configuration.</summary>
        public ImportProfile Profile { get; set; }

        /// <summary>Logger for the import operation.</summary>
        public ImportLogger Logger { get; set; }

        /// <summary>Mapping of Figma node ID -> local image file path (for rasterized nodes).</summary>
        public Dictionary<string, string> NodeImagePaths { get; set; } = new Dictionary<string, string>();

        /// <summary>Mapping of Figma imageRef -> local image file path (for image fills).</summary>
        public Dictionary<string, string> FillImagePaths { get; set; } = new Dictionary<string, string>();

        /// <summary>Mapping of Figma node ID -> imported Unity Sprite.</summary>
        public Dictionary<string, Sprite> NodeSprites { get; set; } = new Dictionary<string, Sprite>();

        /// <summary>Mapping of imageRef -> imported Unity Sprite.</summary>
        public Dictionary<string, Sprite> FillSprites { get; set; } = new Dictionary<string, Sprite>();

        /// <summary>Components from the Figma file (componentId -> metadata).</summary>
        public Dictionary<string, FigmaComponent> Components { get; set; } = new Dictionary<string, FigmaComponent>();

        /// <summary>Component sets (componentSetId -> metadata).</summary>
        public Dictionary<string, FigmaComponentSet> ComponentSets { get; set; } = new Dictionary<string, FigmaComponentSet>();

        /// <summary>Generated prefabs (componentId -> prefab path). For Instance linking.</summary>
        public Dictionary<string, string> GeneratedPrefabs { get; set; } = new Dictionary<string, string>();

        /// <summary>Figma file key (for API calls during import).</summary>
        public string FileKey { get; set; }

        /// <summary>All nodes that need rasterized images (collected before download).</summary>
        public HashSet<string> NodesToRasterize { get; set; } = new HashSet<string>();

        /// <summary>
        /// Per-node export mode. Nodes whose stroke or effects extend past the bounding
        /// box ship their PNG with use_absolute_bounds=false so the outline isn't clipped.
        /// All other nodes default to AbsoluteBounds (sprite size = RectTransform size).
        /// </summary>
        public Dictionary<string, RasterBoundsMode> NodeRasterBoundsModes { get; set; } = new Dictionary<string, RasterBoundsMode>();

        /// <summary>
        /// Container nodes whose decorative descendants get CPU-composited into a single
        /// background PNG, with TEXT descendants overlaid as editable TMP children. Set
        /// during CollectImageRequirements; consumed in ImportAllImages and the convert
        /// path. Membership means "ApplyFrameProperties should treat ctx.NodeSprites[id]
        /// as an opaque background and the convert path should not walk decorative
        /// children — only TEXT descendants get overlay TMP components".
        /// </summary>
        public HashSet<string> CompositeContainerIds { get; set; } = new HashSet<string>();

        /// <summary>All image fill refs that need downloading.</summary>
        public HashSet<string> ImageFillRefs { get; set; } = new HashSet<string>();

        /// <summary>
        /// imageRef → name of the first node that referenced this fill. Used to give
        /// downloaded fill PNGs a human-readable filename instead of the opaque hash
        /// Figma returns. Pure presentational hint; multiple nodes can share an
        /// imageRef and we just keep whichever was visited first.
        /// </summary>
        public Dictionary<string, string> ImageFillNameHints { get; set; } = new Dictionary<string, string>();

        /// <summary>Index of all nodes by ID (for lookups during image import).</summary>
        public Dictionary<string, FigmaNode> NodeIndex { get; set; } = new Dictionary<string, FigmaNode>();

        /// <summary>
        /// Identity record for every GameObject the converters produce during this import.
        /// Keyed by Transform so the ManifestBuilder can attach a FigmaPrefabManifest on the
        /// prefab root just before SaveAsPrefabAsset.
        /// </summary>
        public Dictionary<Transform, NodeIdentityRecord> NodeIdentities { get; set; } = new Dictionary<Transform, NodeIdentityRecord>();

        /// <summary>
        /// Which bounds Figma should use when rasterizing a node:
        ///   • <see cref="AbsoluteBounds"/> — exports at the absoluteBoundingBox extent.
        ///     PNG dimensions match the RectTransform exactly, but any stroke that lives
        ///     outside the path (strokeAlign = OUTSIDE/CENTER) is clipped, as are drop
        ///     shadows that bleed past the layout box. Default for the typical node.
        ///   • <see cref="RenderBounds"/> — passes use_absolute_bounds=false so Figma
        ///     ships the actual rendered area (path + stroke + effects). Sprite ends up
        ///     slightly larger than the RectTransform; UGUI Image squishes it back to
        ///     fit. The squish is invisible at typical 1–4 px stroke widths but the
        ///     outline survives — exactly the trade-off the user picked over "outline
        ///     just gone".
        /// </summary>
        public enum RasterBoundsMode
        {
            AbsoluteBounds = 0,
            RenderBounds   = 1,
        }

        public readonly struct NodeIdentityRecord
        {
            public readonly string FigmaNodeId;
            public readonly string FigmaComponentId;
            public NodeIdentityRecord(string figmaNodeId, string figmaComponentId)
            {
                FigmaNodeId = figmaNodeId;
                FigmaComponentId = figmaComponentId;
            }
        }

        /// <summary>Build the node index from a list of root frames.</summary>
        public void BuildNodeIndex(IEnumerable<FigmaNode> roots)
        {
            foreach (var root in roots)
                IndexNode(root);
        }

        private void IndexNode(FigmaNode node)
        {
            if (node == null) return;
            NodeIndex[node.Id] = node;
            if (node.Children != null)
                foreach (var child in node.Children)
                    IndexNode(child);
        }
    }
}
