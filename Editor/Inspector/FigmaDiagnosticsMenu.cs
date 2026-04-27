using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Inspector
{
    /// <summary>
    /// Quick "is it working?" menu item. Select a prefab (or any GameObject inside one) and
    /// run this to see a summary: does it have a manifest, how many entries, which ancestor
    /// carries it. Meant for diagnosing "I don't see any badges / policy info" situations.
    /// </summary>
    internal static class FigmaDiagnosticsMenu
    {
        [MenuItem("Window/SoobakFigma2Unity/Diagnose Selection")]
        private static void Diagnose()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogWarning("[SoobakFigma2Unity] Select a GameObject (or a prefab's root) first.");
                return;
            }

            var manifest = go.GetComponentInParent<FigmaPrefabManifest>(true);
            if (manifest == null)
            {
                Debug.LogWarning($"[SoobakFigma2Unity] '{go.name}' has no FigmaPrefabManifest in its ancestors. " +
                                 $"This GameObject is not part of a Figma-tracked prefab — either (a) it was never imported " +
                                 $"through this tool, (b) the import was run before the manifest feature was added (do a " +
                                 $"Full Replace re-import once to migrate), or (c) the manifest component was manually removed.");
                return;
            }

            int entries = manifest.Entries.Count;
            int locked = 0, withOverrides = 0, deadTargets = 0;
            foreach (var e in manifest.Entries)
            {
                if (e.target == null) { deadTargets++; continue; }
                if (e.wholeGoLocked) locked++;
                if (e.userPreservedTypes != null && e.userPreservedTypes.Count > 0) withOverrides++;
            }

            var entry = manifest.GetEntry(go.transform);
            string selfStatus = entry.HasValue
                ? (entry.Value.wholeGoLocked ? "locked" : "tracked")
                : "NOT tracked (user-added or missing entry)";

            Debug.Log(
                $"[SoobakFigma2Unity] Manifest on '{manifest.gameObject.name}':\n" +
                $"  Tracked GameObjects: {entries}\n" +
                $"  Locked (whole-GO): {locked}\n" +
                $"  With per-component overrides: {withOverrides}\n" +
                $"  Dead target refs: {deadTargets}\n" +
                $"  Selected GO '{go.name}': {selfStatus}\n" +
                $"  Figma node id (selected): {entry?.figmaNodeId ?? "(none)"}");
        }
    }
}
