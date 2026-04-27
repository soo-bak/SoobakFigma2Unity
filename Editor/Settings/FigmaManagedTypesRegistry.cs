using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Settings
{
    /// <summary>
    /// Global default list of component types that Figma "owns" — i.e. on re-import,
    /// these types are synchronised from the incoming Figma data. Everything else is
    /// preserved as user-added.
    ///
    /// Per-GameObject overrides are stored in <c>FigmaPrefabManifest.Entry.userPreservedTypes</c>
    /// so the user can opt a specific component type out of Figma control for one GO
    /// without changing the global defaults.
    ///
    /// Stored as a ScriptableObject asset so users can edit the list via Unity Inspector
    /// (or the SoobakFigma2Unity settings window). Asset path:
    /// <c>Assets/Soobak/Settings/FigmaManagedTypes.asset</c>.
    /// </summary>
    public sealed class FigmaManagedTypesRegistry : ScriptableObject
    {
        [Tooltip("Assembly-qualified type names (falls back to full names). Types not in this list are treated as user-owned.")]
        [SerializeField] private List<string> managedTypeNames = new();

        public IReadOnlyList<string> ManagedTypeNames => managedTypeNames;

        // Cached resolved Types for IsManaged lookups.
        [NonSerialized] private HashSet<Type> _resolved;
        [NonSerialized] private int _resolvedVersion = -1;
        private int _version;

        /// <summary>
        /// Returns true if the given component type is in the managed list.
        /// </summary>
        public bool IsManaged(Type t)
        {
            if (t == null) return false;
            EnsureResolved();
            return _resolved.Contains(t);
        }

        public bool Add(Type t)
        {
            if (t == null) return false;
            var key = t.AssemblyQualifiedName;
            if (string.IsNullOrEmpty(key)) return false;
            if (managedTypeNames.Contains(key) || managedTypeNames.Contains(t.FullName))
                return false;
            managedTypeNames.Add(key);
            BumpVersion();
            return true;
        }

        public bool Remove(Type t)
        {
            if (t == null) return false;
            int removed = managedTypeNames.RemoveAll(n => n == t.AssemblyQualifiedName || n == t.FullName);
            if (removed > 0)
            {
                BumpVersion();
                return true;
            }
            return false;
        }

        public void ResetToDefaults()
        {
            managedTypeNames.Clear();
            foreach (var name in DefaultManagedTypeNames)
                managedTypeNames.Add(name);
            BumpVersion();
        }

        private void BumpVersion()
        {
            _version++;
            EditorUtility.SetDirty(this);
        }

        private void EnsureResolved()
        {
            if (_resolved != null && _resolvedVersion == _version) return;
            _resolved = new HashSet<Type>();
            foreach (var name in managedTypeNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var t = Type.GetType(name, throwOnError: false);
                if (t == null)
                {
                    // Fallback: search loaded assemblies by full name.
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = asm.GetType(name, throwOnError: false);
                        if (t != null) break;
                    }
                }
                if (t != null) _resolved.Add(t);
            }
            _resolvedVersion = _version;
        }

        private void OnValidate()
        {
            _version++;
        }

        /// <summary>
        /// Default component types that Figma typically authors during conversion. Ordered
        /// roughly by frequency. Users can edit this list via the settings UI.
        /// </summary>
        public static readonly string[] DefaultManagedTypeNames =
        {
            "UnityEngine.RectTransform, UnityEngine.CoreModule",
            "UnityEngine.CanvasRenderer, UnityEngine.UIModule",
            "UnityEngine.CanvasGroup, UnityEngine.UIModule",
            "UnityEngine.UI.Image, UnityEngine.UI",
            "UnityEngine.UI.Mask, UnityEngine.UI",
            "UnityEngine.UI.RectMask2D, UnityEngine.UI",
            "UnityEngine.UI.Shadow, UnityEngine.UI",
            "UnityEngine.UI.Outline, UnityEngine.UI",
            "UnityEngine.UI.HorizontalLayoutGroup, UnityEngine.UI",
            "UnityEngine.UI.VerticalLayoutGroup, UnityEngine.UI",
            "UnityEngine.UI.GridLayoutGroup, UnityEngine.UI",
            "UnityEngine.UI.ContentSizeFitter, UnityEngine.UI",
            "UnityEngine.UI.LayoutElement, UnityEngine.UI",
            "TMPro.TextMeshProUGUI, Unity.TextMeshPro",
            "SoobakFigma2Unity.Runtime.FigmaPrefabManifest, SoobakFigma2Unity.Runtime",
            "SoobakFigma2Unity.Runtime.FigmaMaskInfo, SoobakFigma2Unity.Runtime",
        };
    }
}
