using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Prefabs;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Entry point for non-destructive re-import.
    /// <para>
    /// <see cref="MergeOrSave"/> decides: either the prefab is new / the profile is in
    /// FullReplace → delegate to the current save path; or the prefab exists + we are in
    /// SmartMerge → load the existing prefab, run the in-place merge algorithm, and save
    /// the updated tree back.
    /// </para>
    /// <para>
    /// The merge algorithm is shared between <see cref="Apply"/> (real save) and (future)
    /// <c>BuildPlan</c> (dry-run for Preview UX) via the private <see cref="MergeRoot"/>
    /// routine so that what the Preview shows is exactly what Apply performs.
    /// </para>
    /// </summary>
    internal static class PrefabMerger
    {
        /// <summary>
        /// Saves <paramref name="newRoot"/> to the prefab at <paramref name="outputDir"/>/<paramref name="prefabName"/>.prefab.
        /// Runs a smart merge against the existing prefab when one is present and the profile
        /// allows it; otherwise overwrites.
        /// </summary>
        public static string MergeOrSave(
            GameObject newRoot,
            string outputDir,
            string prefabName,
            ImportContext ctx,
            MergeMode mode,
            ImportLogger logger,
            string rootComponentId = null,
            string explicitAssetPath = null)
        {
            if (newRoot == null) return null;

            string assetPath;
            if (!string.IsNullOrEmpty(explicitAssetPath))
            {
                AssetFolderUtil.EnsureFolder(Path.GetDirectoryName(explicitAssetPath).Replace("\\", "/"));
                assetPath = explicitAssetPath;
            }
            else
            {
                AssetFolderUtil.EnsureFolder(outputDir);
                var name = SanitizeName(prefabName ?? newRoot.name);
                assetPath = Path.Combine(outputDir, $"{name}.prefab").Replace("\\", "/");
            }

            bool exists = File.Exists(Path.GetFullPath(assetPath));

            // Whichever branch we take, the newly-built tree needs a fresh manifest so its
            // identity records are stored when we save. ComponentExtractionPass passes the
            // rootComponentId for prefabs extracted from a Figma COMPONENT so we can
            // re-find them on subsequent imports even if the file gets renamed.
            var attachedManifest = ManifestBuilder.AttachRootManifest(newRoot, ctx, rootComponentId);
            logger?.Info($"{assetPath}: manifest attached with {attachedManifest?.Entries.Count ?? 0} tracked GameObjects.");

            if (!exists || mode == MergeMode.FullReplace)
            {
                if (exists && mode == MergeMode.FullReplace)
                    MergeBackup.CreateSnapshot(assetPath, ctx.Profile?.BackupRetentionCount ?? 5);
                PrefabUtility.SaveAsPrefabAsset(newRoot, assetPath);
                return assetPath;
            }

            // SmartMerge — the path that makes this tool usable long-term.

            MergeBackup.CreateSnapshot(assetPath, ctx.Profile?.BackupRetentionCount ?? 5);

            // If the user has the target prefab open in Prefab Stage, edit the live stage
            // tree directly instead of LoadPrefabContents — that way the user sees the merge
            // happen in real time and we don't fight with the stage's edit buffer.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && PathsEqual(stage.assetPath, assetPath))
            {
                MergeIntoStage(newRoot, stage, ctx, logger, assetPath);
                return assetPath;
            }

            var existing = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var existingManifest = existing.GetComponent<FigmaPrefabManifest>();
                if (existingManifest == null)
                {
                    // Legacy prefab (no manifest) — can't safely merge. Full replace and log.
                    logger?.Warn($"{assetPath}: existing prefab has no FigmaPrefabManifest; treating as FullReplace (first merge-capable import).");
                    PrefabUtility.SaveAsPrefabAsset(newRoot, assetPath);
                    return assetPath;
                }

                var policy = FigmaManagedTypesRegistryProvider.Get();

                AssetDatabase.StartAssetEditing();
                try
                {
                    MergeRoot(newRoot, existing, existingManifest, policy, ctx, logger);

                    // Rebuild the manifest on the merged tree so future imports have up-to-date identity.
                    ManifestBuilder.AttachRootManifest(existing, ctx);
                    existingManifest = existing.GetComponent<FigmaPrefabManifest>();
                    existingManifest?.PruneDeadEntries();
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                PrefabUtility.SaveAsPrefabAsset(existing, assetPath);
                logger?.Success($"Smart-merged prefab: {assetPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(existing);
            }

            return assetPath;
        }

        /// <summary>
        /// SmartMerge variant for when the target prefab is currently open in Prefab Stage.
        /// We merge directly into the stage's live root, mark the stage dirty so the user
        /// sees the unsaved indicator, and let the stage's own save mechanism (manual or
        /// Auto Save) write to the asset.
        /// </summary>
        private static void MergeIntoStage(
            GameObject newRoot,
            UnityEditor.SceneManagement.PrefabStage stage,
            ImportContext ctx,
            ImportLogger logger,
            string assetPath)
        {
            var existing = stage.prefabContentsRoot;
            if (existing == null)
            {
                logger?.Warn($"{assetPath}: Prefab Stage has no contents root; falling back to FullReplace.");
                PrefabUtility.SaveAsPrefabAsset(newRoot, assetPath);
                return;
            }

            var existingManifest = existing.GetComponent<FigmaPrefabManifest>();
            if (existingManifest == null)
            {
                logger?.Warn($"{assetPath}: stage root has no manifest; falling back to FullReplace.");
                PrefabUtility.SaveAsPrefabAsset(newRoot, assetPath);
                return;
            }

            var policy = FigmaManagedTypesRegistryProvider.Get();

            // Register undo so user can Ctrl+Z the merge if they want to.
            Undo.RegisterFullObjectHierarchyUndo(existing, "Figma Smart Merge");

            MergeRoot(newRoot, existing, existingManifest, policy, ctx, logger);
            ManifestBuilder.AttachRootManifest(existing, ctx);
            existingManifest = existing.GetComponent<FigmaPrefabManifest>();
            existingManifest?.PruneDeadEntries();

            // Tell Unity the stage has unsaved edits. User must Ctrl+S (or have Auto Save on)
            // to persist. We deliberately do NOT call SaveAsPrefabAsset here — that would
            // fight with the stage's own save state machine.
            EditorSceneManager.MarkSceneDirty(stage.scene);

            logger?.Success($"Smart-merged into open Prefab Stage: {assetPath} — save the stage (Ctrl+S) to persist.");
        }

        // ─── Core algorithm (shared by Apply and the future BuildPlan dry-run) ───

        private static void MergeRoot(
            GameObject newGo,
            GameObject existingGo,
            FigmaPrefabManifest manifest,
            FigmaManagedTypesRegistry policy,
            ImportContext ctx,
            ImportLogger logger)
        {
            MergeNode(newGo, existingGo, manifest, policy, ctx, logger);
        }

        private static void MergeNode(
            GameObject newGo,
            GameObject existingGo,
            FigmaPrefabManifest manifest,
            FigmaManagedTypesRegistry policy,
            ImportContext ctx,
            ImportLogger logger)
        {
            var entry = manifest.GetEntry(existingGo.transform);

            // Whole-GO lock → skip entirely (name, components, children all preserved).
            if (entry.HasValue && entry.Value.wholeGoLocked)
            {
                logger?.Info($"{GetPath(existingGo.transform)}: locked by user, skipped.");
                return;
            }

            // Don't descend into Prefab Instances — Unity owns their internal state.
            // We still sync top-level fields (RectTransform, name) but not inner children.
            bool existingIsInstance = PrefabUtility.IsAnyPrefabInstanceRoot(existingGo);

            // Name is Figma-managed.
            if (!string.Equals(existingGo.name, newGo.name))
                existingGo.name = newGo.name;

            ComponentMerger.SyncComponents(newGo, existingGo, entry, policy);

            if (existingIsInstance)
                return; // Don't manipulate child hierarchy of a linked instance.

            MergeChildren(newGo, existingGo, manifest, policy, ctx, logger);
        }

        private static void MergeChildren(
            GameObject newGo,
            GameObject existingGo,
            FigmaPrefabManifest manifest,
            FigmaManagedTypesRegistry policy,
            ImportContext ctx,
            ImportLogger logger)
        {
            // Build lookup: figmaNodeId → existing child Transform (direct children only).
            var existingByNodeId = new Dictionary<string, Transform>();
            for (int i = 0; i < existingGo.transform.childCount; i++)
            {
                var child = existingGo.transform.GetChild(i);
                var id = manifest.GetNodeId(child);
                if (!string.IsNullOrEmpty(id))
                    existingByNodeId[id] = child;
            }

            // Snapshot new children because reparenting mutates the transform list.
            var newChildren = new List<Transform>(newGo.transform.childCount);
            for (int i = 0; i < newGo.transform.childCount; i++)
                newChildren.Add(newGo.transform.GetChild(i));

            var consumedExisting = new HashSet<Transform>();
            int figmaOrderIndex = 0;

            foreach (var newChild in newChildren)
            {
                string newChildId = null;
                if (ctx.NodeIdentities.TryGetValue(newChild, out var identity))
                    newChildId = identity.FigmaNodeId;

                if (newChildId != null && existingByNodeId.TryGetValue(newChildId, out var existingChild))
                {
                    if (existingChild.parent != existingGo.transform)
                        existingChild.SetParent(existingGo.transform, false);
                    existingChild.SetSiblingIndex(figmaOrderIndex++);
                    MergeNode(newChild.gameObject, existingChild.gameObject, manifest, policy, ctx, logger);
                    consumedExisting.Add(existingChild);
                }
                else
                {
                    // New Figma node — reparent the freshly-built subtree into existing.
                    newChild.SetParent(existingGo.transform, worldPositionStays: false);
                    newChild.SetSiblingIndex(figmaOrderIndex++);
                    logger?.Info($"{GetPath(existingGo.transform)}/{newChild.name}: added new Figma node.");
                }
            }

            // Process remaining existing children (not matched to a new Figma node).
            // Either: user-added (no manifest entry) → preserve at end,
            // or orphan (had manifest entry, Figma removed it) → delete if safe.
            for (int i = existingGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = existingGo.transform.GetChild(i);
                if (consumedExisting.Contains(child)) continue;

                var childNodeId = manifest.GetNodeId(child);
                if (string.IsNullOrEmpty(childNodeId))
                {
                    // User-added — leave alone. Position: keep at end (already there since new
                    // children were re-indexed first).
                    continue;
                }

                // Figma orphan: previously tracked, now absent from new tree.
                if (HasUserContent(child, manifest, policy))
                {
                    logger?.Warn($"{GetPath(child)}: Figma node removed but user content found — kept as orphan.");
                    continue;
                }

                logger?.Info($"{GetPath(child)}: Figma node removed, deleted.");
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static bool HasUserContent(Transform t, FigmaPrefabManifest manifest, FigmaManagedTypesRegistry policy)
        {
            var components = t.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null) return true;                    // MissingScript is user content
                if (c is Transform) continue;
                if (!policy.IsManaged(c.GetType())) return true;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (string.IsNullOrEmpty(manifest.GetNodeId(child))) return true;
                if (HasUserContent(child, manifest, policy)) return true;
            }
            return false;
        }

        // ─── Helpers ────────────────────────────────────

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim().Trim('.');
            if (string.IsNullOrEmpty(name)) name = "Unnamed";
            return name;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (a == null || b == null) return false;
            return a.Replace("\\", "/").Equals(b.Replace("\\", "/"), System.StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

    }
}
