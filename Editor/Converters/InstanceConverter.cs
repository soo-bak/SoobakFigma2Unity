using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts INSTANCE nodes.
    /// P0: Converts inline (same as frame).
    /// P1: Will link to existing prefabs as PrefabInstance.
    /// </summary>
    internal sealed class InstanceConverter : INodeConverter
    {
        private readonly FrameConverter _frameConverter = new FrameConverter();

        public bool CanConvert(FigmaNode node) => node.NodeType == FigmaNodeType.INSTANCE;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            // P0: treat as inline frame (same as FrameConverter)
            // Log the component reference for awareness
            if (!string.IsNullOrEmpty(node.ComponentId) &&
                ctx.Components.TryGetValue(node.ComponentId, out var component))
            {
                ctx.Logger.Info($"{node.Name}: instance of component '{component.Name}' (inline, P1 will use PrefabInstance)");
            }

            var go = _frameConverter.Convert(node, parent, ctx);

            // Store componentId for future P1 linking
            var nodeRef = go.GetComponent<FigmaNodeRef>();
            if (nodeRef != null)
                nodeRef.FigmaComponentId = node.ComponentId;

            return go;
        }
    }
}
