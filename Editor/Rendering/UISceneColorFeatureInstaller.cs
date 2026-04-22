#if SOOBAK_FIGMA2UNITY_URP
using System.Collections.Generic;
using System.Linq;
using SoobakFigma2Unity.Runtime.URP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SoobakFigma2Unity.Editor.URP
{
    /// <summary>
    /// On editor load, ensures every URP <see cref="ScriptableRendererData"/> in the
    /// project carries <see cref="UISceneColorCopyFeature"/>. Without that feature the
    /// Figma Appearance=Color shader has no destination texture to sample (the
    /// <c>_UISceneColor</c> global stays unbound) and the blend produces wrong output.
    ///
    /// Runs once per domain reload. Also exposes a manual menu item so the user can
    /// re-run the install at will, e.g. after creating new URP assets.
    /// </summary>
    [InitializeOnLoad]
    internal static class UISceneColorFeatureInstaller
    {
        private const string MenuPath = "Window/SoobakFigma2Unity/Reinstall URP Color-blend Feature";

        private static bool _ran;

        static UISceneColorFeatureInstaller()
        {
            Debug.Log("[SoobakFigma2Unity] UISceneColorFeatureInstaller static ctor fired (domain load).");
            // EditorApplication.delayCall is unreliable in Unity 6 when the editor
            // is mid-reload chain (the queue is cleared before the call fires). Hook
            // into .update instead — guaranteed to tick on the first idle frame,
            // and one-shot it with the _ran guard.
            EditorApplication.update += AutoInstall;
        }

        private static void AutoInstall()
        {
            if (_ran) return;
            _ran = true;
            EditorApplication.update -= AutoInstall;
            Debug.Log("[SoobakFigma2Unity] AutoInstall tick reached — running Install().");
            Install(verbose: true);
        }

        [MenuItem(MenuPath)]
        private static void ManualInstall() => Install(verbose: true);

        private static void Install(bool verbose)
        {
            try
            {
                var rendererDatas = FindAllRendererData();
                Debug.Log($"[SoobakFigma2Unity] Install: found {rendererDatas.Count} ScriptableRendererData asset(s).");
                if (rendererDatas.Count == 0)
                {
                    Debug.LogWarning("[SoobakFigma2Unity] No URP ScriptableRendererData assets found in project.");
                    return;
                }

                int installed = 0, alreadyPresent = 0;
                foreach (var rd in rendererDatas)
                {
                    Debug.Log($"[SoobakFigma2Unity] Processing '{rd.name}' ({rd.GetType().Name}, {rd.rendererFeatures.Count} existing feature(s))");
                    if (TryEnsureFeature(rd, out var added))
                    {
                        if (added) installed++;
                        else alreadyPresent++;
                    }
                }

                Debug.Log($"[SoobakFigma2Unity] URP Color-blend feature install: " +
                          $"{installed} added, {alreadyPresent} already present, " +
                          $"{rendererDatas.Count} renderers checked.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SoobakFigma2Unity] Installer threw: {e}");
            }
        }

        private static List<ScriptableRendererData> FindAllRendererData()
        {
            var results = new List<ScriptableRendererData>();
            var guids = AssetDatabase.FindAssets("t:ScriptableRendererData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (rd != null) results.Add(rd);
            }
            return results;
        }

        private static bool TryEnsureFeature(ScriptableRendererData rd, out bool added)
        {
            added = false;
            if (rd == null) return false;

            // Install on every ScriptableRendererData type including Renderer2DData.
            // URP projects routinely use the 2D renderer as their primary renderer
            // (that's what the test project does for Desktop_Ultra and Mobile_Low),
            // and a ScriptableRendererFeature hosted by Renderer2DData runs through
            // Render Graph with the same UniversalResourceData hooks we rely on.

            if (rd.rendererFeatures.Any(f => f is UISceneColorCopyFeature))
                return true;

            var feature = ScriptableObject.CreateInstance<UISceneColorCopyFeature>();
            feature.name = "UI Scene Color Copy (Figma COLOR blend)";

            // The feature must be a sub-asset of the renderer data so URP's inspector
            // and serialisation see it as part of that renderer.
            AssetDatabase.AddObjectToAsset(feature, rd);

            // URP stores renderer features as two parallel serialized arrays:
            //   m_RendererFeatures  — object references to the ScriptableRendererFeature sub-assets
            //   m_RendererFeatureMap — int64 unique IDs kept 1:1 with the list
            // If only the first array is populated (as a previous version of this
            // installer did, via `rd.rendererFeatures.Add(...)`), URP's inspector
            // and the serialization roundtrip drop the entry silently. Both arrays
            // must be updated through SerializedProperty to survive domain reload.
            var so = new SerializedObject(rd);
            so.Update();
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var featureMapProp = so.FindProperty("m_RendererFeatureMap");
            if (featuresProp == null || featureMapProp == null)
            {
                Debug.LogError($"[SoobakFigma2Unity] Could not locate m_RendererFeatures / m_RendererFeatureMap on {rd.name}. URP internal layout may have changed.");
                Object.DestroyImmediate(feature, true);
                return false;
            }

            int newIndex = featuresProp.arraySize;
            featuresProp.arraySize = newIndex + 1;
            featuresProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = feature;

            featureMapProp.arraySize = newIndex + 1;
            featureMapProp.GetArrayElementAtIndex(newIndex).longValue = feature.GetInstanceID();

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(rd);
            AssetDatabase.SaveAssets();

            added = true;
            return true;
        }
    }
}
#endif
