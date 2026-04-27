using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Prefabs;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts INSTANCE nodes.
    /// If a matching prefab exists (generated from COMPONENT), creates a PrefabInstance.
    /// Otherwise, falls back to inline conversion (same as FrameConverter).
    /// </summary>
    internal sealed class InstanceConverter : INodeConverter
    {
        private readonly FrameConverter _frameConverter = new FrameConverter();

        public bool CanConvert(FigmaNode node) => node.NodeType == FigmaNodeType.INSTANCE;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            // P1: Try to link as PrefabInstance if MapComponentInstances is enabled
            if (ctx.Profile.MapComponentInstances && !string.IsNullOrEmpty(node.ComponentId))
            {
                var linker = new PrefabInstanceLinker(ctx.Logger);
                var prefabInstance = linker.TryCreatePrefabInstance(node, parent, ctx);
                if (prefabInstance != null)
                {
                    ctx.NodeIdentities[prefabInstance.transform] = new ImportContext.NodeIdentityRecord(node.Id, node.ComponentId);
                    return prefabInstance;
                }
            }

            // Fallback: inline conversion
            if (!string.IsNullOrEmpty(node.ComponentId) &&
                ctx.Components.TryGetValue(node.ComponentId, out var component))
            {
                ctx.Logger.Info($"{node.Name}: instance of '{component.Name}' (inline — no prefab generated yet)");
            }

            var go = _frameConverter.Convert(node, parent, ctx);
            // Overwrite the identity FrameConverter recorded to include componentId.
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, node.ComponentId);
            return go;
        }
    }
}
