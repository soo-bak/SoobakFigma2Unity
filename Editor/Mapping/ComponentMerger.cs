using System;
using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Per-GameObject component synchronisation. Given a freshly-converted GameObject
    /// (<c>newGo</c>) and the live prefab-contents GameObject (<c>existingGo</c>):
    ///
    /// * For each "Figma-managed" component type (per <see cref="FigmaManagedTypesRegistry"/>
    ///   with per-GO overrides from the manifest), copy values from <c>newGo</c>'s
    ///   component onto <c>existingGo</c>'s component — <b>in place</b>, so references from
    ///   user-added components (e.g. <c>Button.targetGraphic</c> pointing at the Image) stay valid.
    /// * For each managed component on <c>existingGo</c> that does not appear on <c>newGo</c>,
    ///   remove it — Figma has decided this node no longer needs it.
    /// * All non-managed components (user scripts, Animator, user-added UI, etc.) are
    ///   preserved untouched.
    /// </summary>
    internal static class ComponentMerger
    {
        /// <summary>
        /// Sync every managed component on <paramref name="newGo"/> onto <paramref name="existingGo"/>.
        /// Returns counts for logging.
        /// </summary>
        public static (int updated, int added, int removed, int preserved) SyncComponents(
            GameObject newGo,
            GameObject existingGo,
            FigmaPrefabManifest.Entry? entry,
            FigmaManagedTypesRegistry policy)
        {
            int updated = 0, added = 0, removed = 0, preserved = 0;

            var newComps = newGo.GetComponents<Component>();
            var seenTypesInNew = new HashSet<Type>();

            foreach (var nc in newComps)
            {
                if (nc == null) continue;
                var t = nc.GetType();
                if (!ShouldSync(t, entry, policy))
                {
                    preserved++;
                    continue;
                }
                seenTypesInNew.Add(t);

                var ec = existingGo.GetComponent(t);
                if (ec == null)
                {
                    ec = existingGo.AddComponent(t);
                    added++;
                }
                else
                {
                    updated++;
                }
                CopyComponentInPlace(nc, ec);
            }

            // Remove managed components present on existing but absent from new.
            var existingComps = existingGo.GetComponents<Component>();
            foreach (var ec in existingComps)
            {
                if (ec == null) continue;
                var t = ec.GetType();
                if (!ShouldSync(t, entry, policy))
                {
                    preserved++;
                    continue;
                }
                if (seenTypesInNew.Contains(t)) continue;
                if (ec is Transform) continue; // Transform cannot be removed
                UnityEngine.Object.DestroyImmediate(ec);
                removed++;
            }

            return (updated, added, removed, preserved);
        }

        /// <summary>
        /// Decides whether a component type should be synced from Figma for this specific GO.
        /// Combines the global managed-types registry with per-GO overrides.
        /// The manifest component itself is always excluded — its list is owned by
        /// <see cref="ManifestBuilder"/> which rebuilds it after merge.
        /// </summary>
        public static bool ShouldSync(Type t, FigmaPrefabManifest.Entry? entry, FigmaManagedTypesRegistry policy)
        {
            if (t == null || policy == null) return false;
            if (t == typeof(FigmaPrefabManifest)) return false;
            if (entry.HasValue && entry.Value.wholeGoLocked) return false;
            if (!policy.IsManaged(t)) return false;
            if (entry.HasValue && entry.Value.userPreservedTypes != null)
            {
                var qname = t.AssemblyQualifiedName;
                var fname = t.FullName;
                foreach (var n in entry.Value.userPreservedTypes)
                {
                    if (n == qname || n == fname) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Copies values from <paramref name="source"/> onto <paramref name="target"/> without
        /// destroying the target component. This is what makes references from user-added
        /// components survive re-import.
        ///
        /// RectTransform and TextMeshProUGUI get field-by-field copies because
        /// <see cref="EditorUtility.CopySerialized"/> can touch internal state we don't want
        /// to overwrite (material fallbacks, dirty flags, etc.).
        /// </summary>
        public static void CopyComponentInPlace(Component source, Component target)
        {
            if (source == null || target == null) return;

            if (source is RectTransform srcRt && target is RectTransform dstRt)
            {
                CopyRectTransform(srcRt, dstRt);
                return;
            }

            if (source is TextMeshProUGUI srcTmp && target is TextMeshProUGUI dstTmp)
            {
                CopyTextMeshPro(srcTmp, dstTmp);
                return;
            }

            EditorUtility.CopySerialized(source, target);
        }

        private static void CopyRectTransform(RectTransform src, RectTransform dst)
        {
            dst.anchorMin = src.anchorMin;
            dst.anchorMax = src.anchorMax;
            dst.pivot = src.pivot;
            dst.offsetMin = src.offsetMin;
            dst.offsetMax = src.offsetMax;
            dst.localRotation = src.localRotation;
            dst.localScale = src.localScale;
        }

        private static void CopyTextMeshPro(TextMeshProUGUI src, TextMeshProUGUI dst)
        {
            dst.text = src.text;
            if (src.font != null) dst.font = src.font;
            dst.fontSize = src.fontSize;
            dst.fontStyle = src.fontStyle;
            dst.color = src.color;
            dst.characterSpacing = src.characterSpacing;
            dst.wordSpacing = src.wordSpacing;
            dst.lineSpacing = src.lineSpacing;
            dst.paragraphSpacing = src.paragraphSpacing;
            dst.horizontalAlignment = src.horizontalAlignment;
            dst.verticalAlignment = src.verticalAlignment;
            dst.enableAutoSizing = src.enableAutoSizing;
            dst.overflowMode = src.overflowMode;
            dst.textWrappingMode = src.textWrappingMode;
            dst.raycastTarget = src.raycastTarget;
            dst.richText = src.richText;
            dst.margin = src.margin;
        }
    }
}
