using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Prefabs;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Converts INSTANCE nodes — and COMPONENT nodes whose master we already extracted as a
    /// prefab in this import (typical case: variants inside a COMPONENT_SET that the user is
    /// also importing as a screen frame; PrefabVariantBuilder writes each variant's prefab and
    /// registers the COMPONENT.Id → path entry, so the screen-side conversion can link to it
    /// here as a PrefabInstance instead of inlining the whole variant).
    ///
    /// If a matching prefab exists, creates a PrefabInstance. Otherwise falls back to inline
    /// conversion (same as FrameConverter).
    /// </summary>
    internal sealed class InstanceConverter : INodeConverter
    {
        private readonly FrameConverter _frameConverter = new FrameConverter();

        public bool CanConvert(FigmaNode node) =>
            node.NodeType == FigmaNodeType.INSTANCE || node.NodeType == FigmaNodeType.COMPONENT;

        public GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx)
        {
            // INSTANCE: componentId is on the instance node, points to the master COMPONENT.
            // COMPONENT (variant of a COMPONENT_SET): the node IS the master — its own Id is
            // what PrefabVariantBuilder keyed in ctx.GeneratedPrefabs, so use that for lookup.
            var componentId = node.NodeType == FigmaNodeType.COMPONENT
                ? node.Id
                : node.ComponentId;

            if (ctx.Profile.MapComponentInstances && !string.IsNullOrEmpty(componentId))
            {
                var linker = new PrefabInstanceLinker(ctx.Logger);
                var prefabInstance = linker.TryCreatePrefabInstance(node, parent, ctx, componentIdOverride: componentId);
                if (prefabInstance != null)
                {
                    ctx.NodeIdentities[prefabInstance.transform] = new ImportContext.NodeIdentityRecord(node.Id, componentId);
                    return prefabInstance;
                }
            }

            // Fallback: inline conversion. Happens when MapComponentInstances is off, the
            // node has no componentId (corrupt import), the prefab wasn't extracted (external
            // library component), or InstantiatePrefab returned null.
            if (node.NodeType == FigmaNodeType.INSTANCE
                && !string.IsNullOrEmpty(node.ComponentId)
                && ctx.Components.TryGetValue(node.ComponentId, out var component))
            {
                ctx.Logger.Info($"{node.Name}: instance of '{component.Name}' (inline — no prefab generated yet)");
            }

            var go = _frameConverter.Convert(node, parent, ctx);
            // Overwrite the identity FrameConverter recorded to include componentId.
            ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, componentId);
            return go;
        }
    }
}
