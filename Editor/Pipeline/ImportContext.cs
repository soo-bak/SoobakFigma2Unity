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

        /// <summary>All image fill refs that need downloading.</summary>
        public HashSet<string> ImageFillRefs { get; set; } = new HashSet<string>();

        /// <summary>
        /// Maps a chroma-blend node's ID (COLOR/HUE/SATURATION/etc.) to the parent whose
        /// rasterisation contains the correctly-composited blend result. After images are
        /// downloaded we crop the parent's PNG to this child's area and use the crop as
        /// the child's sprite — UGUI can't reach the destination pixel to blend itself,
        /// so we let Figma bake the blend for us.
        /// </summary>
        public Dictionary<string, string> CompositeCropMap { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Parents added to the image download list solely to serve <see cref="CompositeCropMap"/>.
        /// They are NOT meant to become sprites themselves (their converters should run the
        /// normal hierarchical path). We use this set to discard the parent PNGs after
        /// cropping so they don't end up in <see cref="NodeSprites"/>.
        /// </summary>
        public HashSet<string> CompositeCropParents { get; set; } = new HashSet<string>();

        /// <summary>Index of all nodes by ID (for lookups during image import).</summary>
        public Dictionary<string, FigmaNode> NodeIndex { get; set; } = new Dictionary<string, FigmaNode>();

        /// <summary>
        /// Identity record for every GameObject the converters produce during this import.
        /// Keyed by Transform so the ManifestBuilder can attach a FigmaPrefabManifest on the
        /// prefab root just before SaveAsPrefabAsset.
        /// </summary>
        public Dictionary<Transform, NodeIdentityRecord> NodeIdentities { get; set; } = new Dictionary<Transform, NodeIdentityRecord>();

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
