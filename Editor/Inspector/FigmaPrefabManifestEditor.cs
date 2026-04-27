using SoobakFigma2Unity.Editor.Window;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Inspector
{
    /// <summary>
    /// Custom inspector for <see cref="FigmaPrefabManifest"/>. Shows a summary (entry count)
    /// and a safety warning against manual deletion; defers detail editing to the Node Policy
    /// Inspector window so this Inspector stays readable on large prefabs.
    /// </summary>
    [CustomEditor(typeof(FigmaPrefabManifest))]
    internal sealed class FigmaPrefabManifestEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var manifest = (FigmaPrefabManifest)target;

            EditorGUILayout.HelpBox(
                "This is the Figma identity map for this prefab. Do not remove it — if you do, " +
                "the next import will have to rebuild the prefab from scratch and you'll lose " +
                "any Unity-side locks you've set on individual GameObjects.\n\n" +
                "Use Window → SoobakFigma2Unity → Node Policy Inspector to edit per-GameObject sync/preserve settings.",
                MessageType.Warning);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Tracked GameObjects", manifest.Entries.Count.ToString());

            int locked = 0, withOverrides = 0;
            foreach (var e in manifest.Entries)
            {
                if (e.target == null) continue;
                if (e.wholeGoLocked) locked++;
                if (e.userPreservedTypes != null && e.userPreservedTypes.Count > 0) withOverrides++;
            }
            EditorGUILayout.LabelField("Locked (whole-GO)", locked.ToString());
            EditorGUILayout.LabelField("Per-component overrides", withOverrides.ToString());

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Open Node Policy Inspector"))
                NodePolicyInspectorWindow.Open();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Prune dead entries"))
            {
                Undo.RecordObject(manifest, "Prune Figma manifest");
                int n = manifest.PruneDeadEntries();
                Debug.Log($"[SoobakFigma2Unity] Pruned {n} dead entries from {manifest.gameObject.name}.");
                EditorUtility.SetDirty(manifest);
            }
        }
    }
}
