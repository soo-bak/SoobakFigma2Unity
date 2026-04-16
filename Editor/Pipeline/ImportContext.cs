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
    }
}
