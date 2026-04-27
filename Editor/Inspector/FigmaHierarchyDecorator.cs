using System.Collections.Generic;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Inspector
{
    /// <summary>
    /// Draws a small status badge to the LEFT of every Hierarchy item that belongs to a
    /// prefab with a <see cref="FigmaPrefabManifest"/>:
    /// <list type="bullet">
    /// <item>🎨 Synced from Figma — click to toggle to 🔒</item>
    /// <item>🔒 Locked (whole-GO) — click to toggle back to 🎨</item>
    /// <item>(no badge) — the GO is not tracked by Figma (user-added)</item>
    /// </list>
    /// Positioned left of the name so it never overlaps the GameObject title. The badge
    /// shows a colored dot behind the glyph so even if the editor font can't render the
    /// emoji, the status is still visually distinguishable (cyan vs orange).
    /// Identity lookups are cached by Transform instance ID and invalidated on
    /// <see cref="EditorApplication.hierarchyChanged"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class FigmaHierarchyDecorator
    {
        private enum Tracked : byte { Unknown, No, Synced, Locked }

        private static readonly Dictionary<int, Tracked> _cache = new Dictionary<int, Tracked>();
        private static readonly Dictionary<int, FigmaPrefabManifest> _manifestCache = new Dictionary<int, FigmaPrefabManifest>();

        private static GUIStyle _glyphStyle;

        // Using \U escapes so the surrogate pair is generated at compile time regardless of
        // source-file encoding.
        private const string SyncedGlyph = "\U0001F3A8"; // 🎨
        private const string LockedGlyph = "\U0001F512"; // 🔒

        static FigmaHierarchyDecorator()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnItemGUI;
            EditorApplication.hierarchyChanged += InvalidateCache;
        }

        private static void InvalidateCache()
        {
            _cache.Clear();
            _manifestCache.Clear();
        }

        private static void OnItemGUI(int instanceId, Rect selectionRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var state = GetState(go, out var manifest);
            if (state == Tracked.No) return;

            if (_glyphStyle == null)
            {
                _glyphStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            var glyph = state == Tracked.Locked ? LockedGlyph : SyncedGlyph;
            var tooltip = state == Tracked.Locked
                ? "Locked — Figma sync disabled for this GameObject. Click to unlock."
                : "Synced with Figma. Click to lock (preserve all edits on re-import).";

            // Sits at the very start of selectionRect, overlapping the default GameObject icon
            // slot. This keeps the foldout arrow (which lives to the left of selectionRect.x)
            // fully clickable.
            var rect = new Rect(selectionRect.x, selectionRect.y, 16, selectionRect.height);

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                ToggleLock(go, manifest);
                evt.Use();
                return;
            }

            GUI.Label(rect, new GUIContent(glyph, tooltip), _glyphStyle);
        }

        private static Tracked GetState(GameObject go, out FigmaPrefabManifest manifest)
        {
            manifest = null;
            var key = go.GetInstanceID();
            if (_cache.TryGetValue(key, out var cached))
            {
                _manifestCache.TryGetValue(key, out manifest);
                return cached;
            }

            manifest = FindManifest(go);
            if (manifest == null)
            {
                _cache[key] = Tracked.No;
                return Tracked.No;
            }

            var entry = manifest.GetEntry(go.transform);
            Tracked state;
            if (!entry.HasValue) state = Tracked.No;
            else state = entry.Value.wholeGoLocked ? Tracked.Locked : Tracked.Synced;

            _cache[key] = state;
            _manifestCache[key] = manifest;
            return state;
        }

        private static FigmaPrefabManifest FindManifest(GameObject go)
        {
            // Walk up until we find a manifest. Handles both Prefab Stage (manifest on the stage
            // root) and scene-instanced prefabs (manifest on the instance's root, not the scene root).
            return go.GetComponentInParent<FigmaPrefabManifest>(true);
        }

        private static void ToggleLock(GameObject go, FigmaPrefabManifest manifest)
        {
            if (manifest == null) return;
            var entry = manifest.GetEntry(go.transform);
            if (!entry.HasValue) return;

            bool newLockState = !entry.Value.wholeGoLocked;
            ManifestEditAction.Apply(
                go,
                newLockState ? "Figma Lock GameObject" : "Figma Unlock GameObject",
                (m, t) => m.SetWholeGoLocked(t, newLockState));

            InvalidateCache();
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
