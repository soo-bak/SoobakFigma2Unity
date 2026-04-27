using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Result of a single tree walk that gathers everything ComponentExtractionPass
    /// needs to know about Figma components in the imported subtree:
    /// which componentIds exist, where their master nodes are (or that they're
    /// external library components with no master), what they're called, and
    /// how they depend on each other through nested instances.
    /// </summary>
    internal sealed class ComponentInventory
    {
        /// <summary>Every componentId encountered, whether as a COMPONENT node or as an INSTANCE target.</summary>
        public readonly HashSet<string> AllComponentIds = new HashSet<string>();

        /// <summary>componentId → the COMPONENT node when it lives inside the imported tree.</summary>
        public readonly Dictionary<string, FigmaNode> ComponentMasters = new Dictionary<string, FigmaNode>();

        /// <summary>componentId → the first INSTANCE found, used as a fallback master when the component
        /// itself isn't in the imported tree (typical for components defined in another file or page).</summary>
        public readonly Dictionary<string, FigmaNode> FirstInstanceFallback = new Dictionary<string, FigmaNode>();

        /// <summary>componentId → display name. Comes from the master node when available, otherwise the first
        /// INSTANCE's name, otherwise the file-level Components metadata.</summary>
        public readonly Dictionary<string, string> ComponentNames = new Dictionary<string, string>();

        /// <summary>Edges of "A's master tree contains an INSTANCE of B".</summary>
        public readonly ComponentDependencyGraph Dependencies = new ComponentDependencyGraph();
    }

    /// <summary>
    /// Walks the imported root frames once and produces a <see cref="ComponentInventory"/>.
    /// Used as the pre-pass before <see cref="ComponentExtractionPass"/>.
    /// </summary>
    internal static class ComponentInventoryCollector
    {
        public static ComponentInventory Collect(IEnumerable<FigmaNode> roots, ImportContext ctx)
        {
            var inv = new ComponentInventory();
            foreach (var root in roots)
                Walk(root, currentComponent: null, inv, ctx);
            return inv;
        }

        // currentComponent: the nearest enclosing COMPONENT/COMPONENT_SET while walking — used to
        // attribute dependency edges (when we hit an INSTANCE inside a COMPONENT we add an edge
        // from the enclosing component to the instance's target).
        private static void Walk(FigmaNode node, string currentComponent, ComponentInventory inv, ImportContext ctx)
        {
            if (node == null) return;

            if (node.NodeType == FigmaNodeType.COMPONENT)
            {
                inv.AllComponentIds.Add(node.Id);
                inv.ComponentMasters[node.Id] = node;
                inv.ComponentNames[node.Id] = ResolveName(node, ctx);
                inv.Dependencies.AddNode(node.Id);
                currentComponent = node.Id;
            }
            else if (node.NodeType == FigmaNodeType.COMPONENT_SET)
            {
                // The COMPONENT_SET itself doesn't extract; its variant children
                // (real COMPONENTs) do. PrefabVariantBuilder wires the chain.
                // Walk into children with no currentComponent attribution — the variant
                // COMPONENT branches will set it themselves.
                if (node.Children != null)
                    foreach (var c in node.Children) Walk(c, currentComponent: null, inv, ctx);
                return;
            }

            if (node.NodeType == FigmaNodeType.INSTANCE && !string.IsNullOrEmpty(node.ComponentId))
            {
                var cid = node.ComponentId;
                inv.AllComponentIds.Add(cid);
                inv.Dependencies.AddNode(cid);
                if (!inv.FirstInstanceFallback.ContainsKey(cid))
                    inv.FirstInstanceFallback[cid] = node;
                if (!inv.ComponentNames.ContainsKey(cid))
                    inv.ComponentNames[cid] = ResolveName(node, ctx);
                if (!string.IsNullOrEmpty(currentComponent))
                    inv.Dependencies.AddEdge(currentComponent, cid);

                // Component property of type INSTANCE_SWAP carries a componentId in `Value`
                // pointing at the swap target. Treat it as a dependency edge so the swap
                // target is extracted before the containing component.
                if (node.ComponentProperties != null)
                {
                    foreach (var kv in node.ComponentProperties)
                    {
                        if (kv.Value?.Type == "INSTANCE_SWAP")
                        {
                            var swapTarget = kv.Value.AsString();
                            if (!string.IsNullOrEmpty(swapTarget))
                            {
                                inv.AllComponentIds.Add(swapTarget);
                                inv.Dependencies.AddNode(swapTarget);
                                if (!string.IsNullOrEmpty(currentComponent))
                                    inv.Dependencies.AddEdge(currentComponent, swapTarget);
                            }
                        }
                    }
                }
            }

            if (node.Children != null)
                foreach (var c in node.Children)
                    Walk(c, currentComponent, inv, ctx);
        }

        private static string ResolveName(FigmaNode node, ImportContext ctx)
        {
            // Prefer the file's Components metadata name (it survives the case where the
            // master node was renamed locally) and fall back to whatever the node carries.
            if (node.ComponentId != null && ctx.Components != null
                && ctx.Components.TryGetValue(node.ComponentId, out var meta)
                && !string.IsNullOrEmpty(meta?.Name))
                return meta.Name;
            return node.Name;
        }
    }
}
