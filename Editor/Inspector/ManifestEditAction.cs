using System;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Inspector
{
    /// <summary>
    /// Mutating the <see cref="FigmaPrefabManifest"/> in the right place is subtle:
    /// <list type="bullet">
    /// <item>If the user is editing the prefab itself in <b>Prefab Stage</b>, modifying the
    /// in-stage component is correct — Unity persists it on save.</item>
    /// <item>If the user is editing a <b>Prefab Instance</b> in a scene, mutations only land
    /// as instance overrides and are <i>invisible to re-import</i>, which writes to the
    /// asset directly. To make a lock survive a re-import we have to load the asset and
    /// modify it ourselves.</item>
    /// </list>
    /// This helper picks the right path automatically so callers (hierarchy badge click,
    /// Node Policy Inspector toggles, etc.) don't have to.
    /// </summary>
    internal static class ManifestEditAction
    {
        /// <summary>
        /// Apply <paramref name="edit"/> to the manifest that owns <paramref name="go"/>,
        /// persisting the change so that the next Smart Merge re-import sees it.
        /// </summary>
        /// <param name="undoLabel">Shown in the Edit > Undo history when applicable.</param>
        public static void Apply(GameObject go, string undoLabel, Action<FigmaPrefabManifest, Transform> edit)
        {
            if (go == null || edit == null) return;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool inStage = stage != null && stage.IsPartOfPrefabContents(go);

            if (inStage)
            {
                ApplyInStage(go, undoLabel, edit, stage);
            }
            else
            {
                ApplyToAsset(go, edit);
            }
        }

        private static void ApplyInStage(
            GameObject go, string undoLabel, Action<FigmaPrefabManifest, Transform> edit,
            UnityEditor.SceneManagement.PrefabStage stage)
        {
            var manifest = go.GetComponentInParent<FigmaPrefabManifest>(true);
            if (manifest == null) return;

            Undo.RecordObject(manifest, undoLabel);
            edit(manifest, go.transform);
            EditorUtility.SetDirty(manifest);
            EditorSceneManager.MarkSceneDirty(stage.scene);
        }

        private static void ApplyToAsset(GameObject go, Action<FigmaPrefabManifest, Transform> edit)
        {
            // Identify the asset to modify and the Figma node ID we're targeting.
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogWarning(
                    $"[SoobakFigma2Unity] '{go.name}' is not part of a prefab asset; the change won't survive re-import.");
                return;
            }

            var instanceManifest = go.GetComponentInParent<FigmaPrefabManifest>(true);
            if (instanceManifest == null)
            {
                Debug.LogWarning($"[SoobakFigma2Unity] No manifest found in instance hierarchy of '{go.name}'.");
                return;
            }
            var entry = instanceManifest.GetEntry(go.transform);
            if (!entry.HasValue || string.IsNullOrEmpty(entry.Value.figmaNodeId))
            {
                Debug.LogWarning($"[SoobakFigma2Unity] '{go.name}' has no Figma identity in the instance manifest.");
                return;
            }
            string figmaNodeId = entry.Value.figmaNodeId;

            // Load the asset, mutate, save, unload.
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var assetManifest = contents.GetComponent<FigmaPrefabManifest>();
                if (assetManifest == null)
                {
                    Debug.LogWarning($"[SoobakFigma2Unity] {prefabPath}: no manifest on asset root.");
                    return;
                }
                var assetTransform = assetManifest.FindByNodeId(figmaNodeId);
                if (assetTransform == null)
                {
                    Debug.LogWarning($"[SoobakFigma2Unity] {prefabPath}: node id '{figmaNodeId}' not present in asset manifest. Run a Full Replace re-import to refresh.");
                    return;
                }
                edit(assetManifest, assetTransform);
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // Drop any previous instance-side override on the manifest so the scene picks
            // up the asset's freshly-saved value. Without this, an old override (e.g. from
            // a prior version of this code that wrote to the instance) would shadow the
            // change and confuse re-import diagnostics.
            try { PrefabUtility.RevertObjectOverride(instanceManifest, InteractionMode.AutomatedAction); }
            catch { /* not always overridable; safe to ignore */ }

            EditorUtility.SetDirty(instanceManifest);
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
