using System;
using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Prefabs;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Pre-pass that runs before the main frame conversion. For every Figma component
    /// referenced anywhere in the imported subtree (whether by an in-tree COMPONENT
    /// definition or only by an INSTANCE), guarantees a .prefab file exists at
    /// <c>profile.ComponentOutputPath</c> and registers its path in
    /// <c>ctx.GeneratedPrefabs</c>.
    ///
    /// The main convert step then sees every INSTANCE's componentId already mapped, so
    /// PrefabInstanceLinker can produce a real PrefabInstance rather than falling back
    /// to inline conversion. Result: zero-config nested-prefab structure on first
    /// import; SmartMerge keeps designer edits to the extracted prefabs intact on
    /// subsequent imports.
    ///
    /// Components that belong to a COMPONENT_SET are still routed through
    /// <see cref="PrefabVariantBuilder"/> (Unity Prefab Variant chain). Standalone
    /// components and external library components extract here directly.
    /// </summary>
    internal static class ComponentExtractionPass
    {
        /// <summary>
        /// Runs the extraction. <paramref name="convertFunc"/> is the pipeline's
        /// node-to-GameObject converter (the same one used for the main frames).
        /// <paramref name="registerSavedPath"/> appends to the pipeline's
        /// <c>SavedPrefabPaths</c> so the editor window's Undo can rewind these too.
        /// </summary>
        public static void Run(
            IEnumerable<FigmaNode> roots,
            ComponentInventory inv,
            ImportContext ctx,
            ImportProfile profile,
            ImportLogger logger,
            Func<FigmaNode, GameObject> convertFunc,
            Action<string> registerSavedPath)
        {
            if (inv == null || inv.AllComponentIds.Count == 0) return;

            AssetFolderUtil.EnsureFolder(profile.ComponentOutputPath);

            // 1) COMPONENT_SETs first — let PrefabVariantBuilder own variant chains.
            //    It writes each variant's path into ctx.GeneratedPrefabs, so the
            //    standalone loop below sees them as already-handled and skips them.
            if (profile.GeneratePrefabVariants)
            {
                var vb = new PrefabVariantBuilder(logger);
                foreach (var setNode in EnumerateComponentSets(roots))
                {
                    var chain = vb.BuildVariantChain(setNode, convertFunc, profile.ComponentOutputPath, ctx);
                    if (chain != null)
                        foreach (var path in chain.Values)
                            if (!string.IsNullOrEmpty(path)) registerSavedPath?.Invoke(path);
                }
            }

            // 2) Cycle warning (defensive — Figma forbids cycles, but a corrupt file shouldn't crash).
            if (inv.Dependencies.HasCycle(out var cycle))
                logger?.Warn($"Component dependency cycle: {string.Join(" → ", cycle)} — order undefined for these.");

            // 3) Standalone components in topological (leaf-first) order. This way,
            //    when the container is converted, any nested INSTANCE already has its
            //    target prefab registered and PrefabInstanceLinker links cleanly.
            foreach (var componentId in inv.Dependencies.TopologicalOrder())
            {
                if (ctx.GeneratedPrefabs.ContainsKey(componentId))
                    continue; // PrefabVariantBuilder already produced this one.

                var master = inv.ComponentMasters.TryGetValue(componentId, out var mNode) ? mNode : null;
                if (master == null)
                {
                    // External library component — the master COMPONENT lives in another
                    // Figma file and isn't in our imported subtree. Earlier we'd grab the
                    // first INSTANCE's tree as a fallback "master" and run convertFunc on
                    // it, but the INSTANCE's tree carries that specific instance's overrides
                    // AND whatever sibling context it happens to sit in (we observed
                    // bg_slice_btn_primary_xl coming out with bg_slice_button_secondary_xl
                    // as its only child because the first primary instance was nested next
                    // to a secondary instance in the import tree). Producing a wrong prefab
                    // is worse than producing none — InstanceConverter falls through to
                    // inline conversion which at least renders the visible content via the
                    // raw Figma exports.
                    inv.ComponentNames.TryGetValue(componentId, out var fallbackName);
                    logger?.Warn($"Component {componentId}{(string.IsNullOrEmpty(fallbackName) ? "" : $" ({fallbackName})")}: master not in import tree (external library component); skipping auto-extract — the screen prefab will inline this instance instead. Add an explicit Component Map entry to override.");
                    continue;
                }

                inv.ComponentNames.TryGetValue(componentId, out var displayName);
                if (string.IsNullOrEmpty(displayName)) displayName = master.Name;

                var assetPath = ComponentPrefabNamer.Resolve(componentId, displayName, profile.ComponentOutputPath);

                var go = convertFunc(master);
                try
                {
                    var savedPath = PrefabMerger.MergeOrSave(
                        go, profile.ComponentOutputPath, displayName, ctx, profile.MergeMode, logger,
                        rootComponentId: componentId,
                        explicitAssetPath: assetPath);

                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        ctx.GeneratedPrefabs[componentId] = savedPath;
                        registerSavedPath?.Invoke(savedPath);
                        logger?.Success($"Component prefab: {savedPath}");
                    }
                }
                finally
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        private static IEnumerable<FigmaNode> EnumerateComponentSets(IEnumerable<FigmaNode> roots)
        {
            foreach (var root in roots)
                foreach (var n in EnumerateOfType(root, FigmaNodeType.COMPONENT_SET))
                    yield return n;
        }

        private static IEnumerable<FigmaNode> EnumerateOfType(FigmaNode node, FigmaNodeType type)
        {
            if (node == null) yield break;
            if (node.NodeType == type) { yield return node; yield break; }
            if (node.Children != null)
                foreach (var c in node.Children)
                    foreach (var x in EnumerateOfType(c, type))
                        yield return x;
        }
    }
}
