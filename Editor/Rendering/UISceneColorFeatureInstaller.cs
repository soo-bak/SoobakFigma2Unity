#if SOOBAK_FIGMA2UNITY_URP
using System;
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
    /// Opt-in installer for <see cref="UISceneColorCopyFeature"/>.
    ///
    /// This feature exists only for Figma Appearance=Color (and sibling chroma
    /// blends). Host projects that just import the package must not have their
    /// URP renderer assets mutated, and deleting the feature must not make it
    /// come back on the next domain reload.
    ///
    /// Install is therefore menu-only and limited to renderer datas referenced
    /// by the active URP assets. Remove walks every project-local renderer so
    /// leftover auto-injected copies (including demo/third-party assets) can
    /// be cleaned up.
    /// </summary>
    internal static class UISceneColorFeatureInstaller
    {
        private const string InstallMenuPath = "Window/SoobakFigma2Unity/Install URP Color-blend Feature";
        private const string RemoveMenuPath = "Window/SoobakFigma2Unity/Remove URP Color-blend Feature";
        private const string FeatureObjectName = "UI Scene Color Copy (Figma COLOR blend)";

        [MenuItem(InstallMenuPath)]
        private static void ManualInstall()
        {
            var rendererDatas = FindActivePipelineRendererData();
            if (rendererDatas.Count == 0)
            {
                Debug.LogWarning("[SoobakFigma2Unity] No project-local ScriptableRendererData referenced by the active URP assets.");
                return;
            }

            int installed = 0, alreadyPresent = 0;
            foreach (var rd in rendererDatas)
            {
                if (!TryEnsureFeature(rd, out var added))
                    continue;
                if (added) installed++;
                else alreadyPresent++;
            }

            Debug.Log("[SoobakFigma2Unity] URP Color-blend feature install: " +
                      $"{installed} added, {alreadyPresent} already present, " +
                      $"{rendererDatas.Count} active-pipeline renderer(s) checked.");
        }

        [MenuItem(RemoveMenuPath)]
        private static void ManualRemove()
        {
            var rendererDatas = FindAllProjectRendererData();
            if (rendererDatas.Count == 0)
            {
                Debug.LogWarning("[SoobakFigma2Unity] No project-local ScriptableRendererData assets found.");
                return;
            }

            int removed = 0;
            foreach (var rd in rendererDatas)
            {
                if (TryRemoveFeature(rd))
                    removed++;
            }

            Debug.Log("[SoobakFigma2Unity] URP Color-blend feature remove: " +
                      $"{removed} renderer(s) cleaned, {rendererDatas.Count} project renderer(s) checked.");
        }

        private static List<ScriptableRendererData> FindActivePipelineRendererData()
        {
            var results = new List<ScriptableRendererData>();
            var seen = new HashSet<ScriptableRendererData>();

            foreach (var urp in FindActiveUrpAssets())
            {
                foreach (var rd in urp.rendererDataList)
                {
                    if (rd == null || !seen.Add(rd))
                        continue;
                    if (!IsProjectLocalAsset(rd))
                        continue;
                    results.Add(rd);
                }
            }

            return results;
        }

        private static List<UniversalRenderPipelineAsset> FindActiveUrpAssets()
        {
            var assets = new List<UniversalRenderPipelineAsset>();
            var seen = new HashSet<UniversalRenderPipelineAsset>();

            void Add(RenderPipelineAsset pipeline)
            {
                if (pipeline is UniversalRenderPipelineAsset urp && seen.Add(urp))
                    assets.Add(urp);
            }

            Add(GraphicsSettings.defaultRenderPipeline);
            Add(GraphicsSettings.currentRenderPipeline);
            Add(QualitySettings.renderPipeline);
            for (int i = 0; i < QualitySettings.count; i++)
                Add(QualitySettings.GetRenderPipelineAssetAt(i));

            return assets;
        }

        private static List<ScriptableRendererData> FindAllProjectRendererData()
        {
            var results = new List<ScriptableRendererData>();
            var guids = AssetDatabase.FindAssets("t:ScriptableRendererData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (rd != null) results.Add(rd);
            }
            return results;
        }

        private static bool IsProjectLocalAsset(ScriptableRendererData rd)
        {
            var path = AssetDatabase.GetAssetPath(rd);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static bool TryEnsureFeature(ScriptableRendererData rd, out bool added)
        {
            added = false;
            if (rd == null) return false;

            if (rd.rendererFeatures.Any(f => f is UISceneColorCopyFeature))
                return true;

            var feature = ScriptableObject.CreateInstance<UISceneColorCopyFeature>();
            feature.name = FeatureObjectName;

            // The feature must be a sub-asset of the renderer data so URP's inspector
            // and serialisation see it as part of that renderer.
            AssetDatabase.AddObjectToAsset(feature, rd);

            // URP stores renderer features as two parallel serialized arrays:
            //   m_RendererFeatures  — object references to the ScriptableRendererFeature sub-assets
            //   m_RendererFeatureMap — int64 unique IDs kept 1:1 with the list
            // Both arrays must be updated through SerializedProperty to survive domain reload.
            var so = new SerializedObject(rd);
            so.Update();
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var featureMapProp = so.FindProperty("m_RendererFeatureMap");
            if (featuresProp == null || featureMapProp == null)
            {
                Debug.LogError($"[SoobakFigma2Unity] Could not locate m_RendererFeatures / m_RendererFeatureMap on {rd.name}. URP internal layout may have changed.");
                UnityEngine.Object.DestroyImmediate(feature, true);
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

        private static bool TryRemoveFeature(ScriptableRendererData rd)
        {
            if (rd == null) return false;

            var so = new SerializedObject(rd);
            so.Update();
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var featureMapProp = so.FindProperty("m_RendererFeatureMap");
            if (featuresProp == null)
                return false;

            var doomed = new List<UISceneColorCopyFeature>();
            bool removed = false;
            for (int i = featuresProp.arraySize - 1; i >= 0; i--)
            {
                var feature = featuresProp.GetArrayElementAtIndex(i).objectReferenceValue as UISceneColorCopyFeature;
                if (feature == null)
                    continue;

                // Object-reference arrays null on the first delete and shrink on the second.
                featuresProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
                featuresProp.DeleteArrayElementAtIndex(i);
                if (featureMapProp != null && i < featureMapProp.arraySize)
                    featureMapProp.DeleteArrayElementAtIndex(i);

                doomed.Add(feature);
                removed = true;
            }

            if (!removed)
                return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var feature in doomed)
            {
                if (feature != null)
                    UnityEngine.Object.DestroyImmediate(feature, true);
            }

            EditorUtility.SetDirty(rd);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
#endif
