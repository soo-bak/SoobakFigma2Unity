using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Runtime;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Builds or rebuilds the <see cref="FigmaPrefabManifest"/> on a prefab root just before
    /// it is saved. Two modes:
    /// <list type="bullet">
    /// <item><b>Fresh build</b>: For newly-converted roots (no prior merge), every Transform's
    /// identity comes from <see cref="ImportContext.NodeIdentities"/>.</item>
    /// <item><b>Post-merge rebuild</b>: After SmartMerge, the live tree contains two kinds of
    /// tracked Transforms — (a) Transforms that were matched to existing GOs and therefore
    /// carry identity via the existing manifest, and (b) Transforms that were reparented in
    /// from the newly-built tree and therefore have identity in ctx.NodeIdentities. We fold
    /// both sources.</item>
    /// </list>
    /// Per-GO overrides (wholeGoLocked, userPreservedTypes) from the existing manifest are
    /// preserved so user lock choices survive re-import.
    /// </summary>
    internal static class ManifestBuilder
    {
        /// <summary>
        /// Ensures <paramref name="root"/> has a <see cref="FigmaPrefabManifest"/> and rebuilds
        /// its entries by walking the hierarchy. Identity is looked up first in
        /// <paramref name="ctx"/>.NodeIdentities, then falling back to the existing manifest's
        /// pre-rebuild entries (so matched-and-kept Transforms keep their nodeId even though
        /// they aren't in ctx.NodeIdentities).
        /// </summary>
        public static FigmaPrefabManifest AttachRootManifest(GameObject root, ImportContext ctx)
        {
            if (root == null || ctx == null)
                return null;

            var manifest = root.GetComponent<FigmaPrefabManifest>()
                           ?? root.AddComponent<FigmaPrefabManifest>();

            // Snapshot the pre-existing entries so matched Transforms keep their identity.
            // FigmaPrefabManifest.Rebuild already preserves wholeGoLocked / userPreservedTypes
            // for Transforms carried forward; we just need to re-supply figmaNodeId for those
            // Transforms that are not in ctx.NodeIdentities.
            var prior = new Dictionary<Transform, (string nodeId, string compId)>();
            foreach (var e in manifest.Entries)
            {
                if (e.target == null) continue;
                prior[e.target] = (e.figmaNodeId, e.figmaComponentId);
            }

            var entries = new List<FigmaPrefabManifest.Entry>();
            CollectEntries(root.transform, ctx, prior, entries);

            manifest.Rebuild(entries, preserveUserOverrides: true);
            return manifest;
        }

        private static void CollectEntries(
            Transform t,
            ImportContext ctx,
            Dictionary<Transform, (string nodeId, string compId)> prior,
            List<FigmaPrefabManifest.Entry> outEntries)
        {
            string nodeId = null;
            string compId = null;

            if (ctx.NodeIdentities.TryGetValue(t, out var identity))
            {
                nodeId = identity.FigmaNodeId;
                compId = identity.FigmaComponentId;
            }
            else if (prior != null && prior.TryGetValue(t, out var priorId))
            {
                nodeId = priorId.nodeId;
                compId = priorId.compId;
            }

            if (!string.IsNullOrEmpty(nodeId))
            {
                outEntries.Add(new FigmaPrefabManifest.Entry
                {
                    target = t,
                    figmaNodeId = nodeId,
                    figmaComponentId = compId,
                    wholeGoLocked = false,
                    userPreservedTypes = null
                });
            }

            for (int i = 0; i < t.childCount; i++)
                CollectEntries(t.GetChild(i), ctx, prior, outEntries);
        }
    }
}
