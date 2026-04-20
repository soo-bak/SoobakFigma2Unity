using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoobakFigma2Unity.Runtime
{
    /// <summary>
    /// Single-root component that tracks every Figma-originated GameObject inside a prefab
    /// via Transform references. Enables non-destructive re-import: on the next import,
    /// the merge engine matches existing Unity GameObjects to incoming Figma nodes by
    /// figmaNodeId and preserves user-added components, children, and (optionally) specific
    /// Figma-managed components the user has chosen to lock.
    ///
    /// Only one manifest lives on the prefab root. Lookups and mutations are O(log N) via a
    /// lazily-built index on Transform instance IDs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FigmaPrefabManifest : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Reference to the tracked GameObject's Transform. Unity keeps this stable across renames/reparenting.")]
            public Transform target;

            [Tooltip("Figma node ID (e.g. \"33:567\"). Stable across Figma renames/moves.")]
            public string figmaNodeId;

            [Tooltip("For INSTANCE nodes: the Figma component ID this instance references.")]
            public string figmaComponentId;

            [Tooltip("If true, the merge engine skips this GameObject entirely on re-import (fast-path full lock).")]
            public bool wholeGoLocked;

            [Tooltip("Assembly-qualified component type names that should NOT be synced from Figma for this GO (per-component override).")]
            public List<string> userPreservedTypes;
        }

        [SerializeField] private List<Entry> entries = new();

        // Lazy index: Transform instance ID → entries slot. Rebuilt on demand.
        [NonSerialized] private Dictionary<int, int> _indexByInstanceId;
        [NonSerialized] private bool _indexDirty = true;

        public IReadOnlyList<Entry> Entries => entries;

        // ─── Query ──────────────────────────────────────

        /// <summary>Returns true if the given Transform is tracked by this manifest.</summary>
        public bool IsTracked(Transform t)
        {
            if (t == null) return false;
            EnsureIndex();
            return _indexByInstanceId.TryGetValue(t.GetInstanceID(), out var slot)
                   && slot >= 0 && slot < entries.Count
                   && entries[slot].target == t;
        }

        /// <summary>Fetches a copy of the entry for the given Transform, or null if untracked.</summary>
        public Entry? GetEntry(Transform t)
        {
            if (t == null) return null;
            EnsureIndex();
            if (_indexByInstanceId.TryGetValue(t.GetInstanceID(), out var slot)
                && slot >= 0 && slot < entries.Count
                && entries[slot].target == t)
                return entries[slot];
            return null;
        }

        /// <summary>Returns the figmaNodeId for a tracked Transform, or null.</summary>
        public string GetNodeId(Transform t)
        {
            return GetEntry(t)?.figmaNodeId;
        }

        /// <summary>Finds the tracked Transform for a given figmaNodeId, or null.</summary>
        public Transform FindByNodeId(string figmaNodeId)
        {
            if (string.IsNullOrEmpty(figmaNodeId)) return null;
            foreach (var e in entries)
            {
                if (e.target != null && e.figmaNodeId == figmaNodeId)
                    return e.target;
            }
            return null;
        }

        /// <summary>Returns true if the GO is fully locked (merge engine should skip it).</summary>
        public bool IsWholeGoLocked(Transform t)
        {
            return GetEntry(t)?.wholeGoLocked ?? false;
        }

        /// <summary>Returns true if this GO has opted the specific component type out of Figma sync.</summary>
        public bool IsUserPreserved(Transform t, Type componentType)
        {
            if (componentType == null) return false;
            var entry = GetEntry(t);
            if (entry == null) return false;
            var list = entry.Value.userPreservedTypes;
            if (list == null || list.Count == 0) return false;
            var typeName = componentType.AssemblyQualifiedName;
            var shortName = componentType.FullName;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == typeName || list[i] == shortName) return true;
            }
            return false;
        }

        // ─── Mutation ───────────────────────────────────

        /// <summary>Sets the whole-GO lock flag. Creates the entry if missing is NOT supported here — use Rebuild for population.</summary>
        public bool SetWholeGoLocked(Transform t, bool locked)
        {
            if (t == null) return false;
            EnsureIndex();
            if (!_indexByInstanceId.TryGetValue(t.GetInstanceID(), out var slot)) return false;
            if (slot < 0 || slot >= entries.Count || entries[slot].target != t) return false;
            var e = entries[slot];
            if (e.wholeGoLocked == locked) return false;
            e.wholeGoLocked = locked;
            entries[slot] = e;
            return true;
        }

        /// <summary>Toggles a specific component type's user-preserved flag for this GO.</summary>
        public bool SetComponentPreserved(Transform t, Type componentType, bool preserved)
        {
            if (t == null || componentType == null) return false;
            EnsureIndex();
            if (!_indexByInstanceId.TryGetValue(t.GetInstanceID(), out var slot)) return false;
            if (slot < 0 || slot >= entries.Count || entries[slot].target != t) return false;
            var e = entries[slot];
            e.userPreservedTypes ??= new List<string>();
            var typeName = componentType.AssemblyQualifiedName;
            bool changed = false;
            if (preserved)
            {
                if (!e.userPreservedTypes.Contains(typeName))
                {
                    e.userPreservedTypes.Add(typeName);
                    changed = true;
                }
            }
            else
            {
                int removed = e.userPreservedTypes.RemoveAll(n => n == typeName || n == componentType.FullName);
                if (removed > 0) changed = true;
            }
            if (changed) entries[slot] = e;
            return changed;
        }

        /// <summary>
        /// Rebuilds the entries list from scratch while preserving existing per-GO overrides
        /// (wholeGoLocked, userPreservedTypes) for Transforms that survive the rebuild.
        /// Called by the import pipeline after each import.
        /// </summary>
        public void Rebuild(IEnumerable<Entry> newEntries, bool preserveUserOverrides = true)
        {
            var oldOverrides = preserveUserOverrides ? BuildOverridesSnapshot() : null;

            entries.Clear();
            foreach (var e in newEntries)
            {
                var copy = e;
                if (oldOverrides != null && copy.target != null &&
                    oldOverrides.TryGetValue(copy.target.GetInstanceID(), out var overrides))
                {
                    copy.wholeGoLocked = overrides.wholeGoLocked;
                    copy.userPreservedTypes = overrides.userPreservedTypes;
                }
                entries.Add(copy);
            }
            _indexDirty = true;
        }

        /// <summary>
        /// Removes entries whose Transform target has been destroyed (e.g. user deleted the GO).
        /// Returns the count removed.
        /// </summary>
        public int PruneDeadEntries()
        {
            int removed = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].target == null)
                {
                    entries.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0) _indexDirty = true;
            return removed;
        }

        // ─── Internals ──────────────────────────────────

        private Dictionary<int, (bool wholeGoLocked, List<string> userPreservedTypes)> BuildOverridesSnapshot()
        {
            var snapshot = new Dictionary<int, (bool, List<string>)>();
            foreach (var e in entries)
            {
                if (e.target == null) continue;
                if (!e.wholeGoLocked && (e.userPreservedTypes == null || e.userPreservedTypes.Count == 0))
                    continue;
                snapshot[e.target.GetInstanceID()] = (e.wholeGoLocked, e.userPreservedTypes);
            }
            return snapshot;
        }

        private void EnsureIndex()
        {
            if (!_indexDirty && _indexByInstanceId != null) return;
            _indexByInstanceId ??= new Dictionary<int, int>(entries.Count);
            _indexByInstanceId.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                var t = entries[i].target;
                if (t == null) continue;
                _indexByInstanceId[t.GetInstanceID()] = i;
            }
            _indexDirty = false;
        }

        private void OnValidate()
        {
            _indexDirty = true;
        }
    }
}
