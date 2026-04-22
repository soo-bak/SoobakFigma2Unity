#if SOOBAK_FIGMA2UNITY_URP
using System.Linq;
using SoobakFigma2Unity.Runtime.URP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SoobakFigma2Unity.Editor.URP
{
    /// <summary>
    /// On editor load, ensures the active URP renderer carries
    /// <see cref="UISceneColorCopyFeature"/>. Without that feature the Figma
    /// Appearance=Color shader has no destination texture to sample and
    /// renders as solid black, so the auto-install removes the entire setup
    /// burden from the artist.
    ///
    /// Re-runs after every domain reload — if the user manually removes the
    /// feature it will silently come back, which is the correct behaviour for
    /// "tool ships its own rendering dependency".
    /// </summary>
    [InitializeOnLoad]
    internal static class UISceneColorFeatureInstaller
    {
        static UISceneColorFeatureInstaller()
        {
            // Defer to avoid running during a still-loading domain.
            EditorApplication.delayCall += EnsureInstalled;
        }

        private static void EnsureInstalled()
        {
            EditorApplication.delayCall -= EnsureInstalled;

            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset
                      ?? QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return; // not a URP project — nothing to do

            // URP exposes its renderer list only via SerializedObject; we need that to
            // grab the underlying ScriptableRendererData asset (which owns the feature
            // sub-assets we want to add).
            var so = new SerializedObject(urp);
            var renderersProp = so.FindProperty("m_RendererDataList");
            if (renderersProp == null || !renderersProp.isArray) return;

            for (int i = 0; i < renderersProp.arraySize; i++)
            {
                var rendererData = renderersProp.GetArrayElementAtIndex(i).objectReferenceValue
                                   as ScriptableRendererData;
                if (rendererData == null) continue;
                EnsureFeatureOnRenderer(rendererData);
            }
        }

        private static void EnsureFeatureOnRenderer(ScriptableRendererData rendererData)
        {
            if (rendererData.rendererFeatures.Any(f => f is UISceneColorCopyFeature)) return;

            var feature = ScriptableObject.CreateInstance<UISceneColorCopyFeature>();
            feature.name = "UI Scene Color Copy (Figma COLOR blend)";

            // Adding as a sub-asset of the renderer data is what makes URP's inspector
            // recognise it as a real feature on that renderer (rather than an orphan).
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SoobakFigma2Unity] Installed UISceneColorCopyFeature on renderer '{rendererData.name}'.");
        }
    }
}
#endif
