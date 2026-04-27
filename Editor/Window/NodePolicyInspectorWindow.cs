using System;
using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Inspector;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Window
{
    /// <summary>
    /// Dockable inspector that watches <see cref="Selection.activeGameObject"/> and, when
    /// that GameObject is tracked by a <see cref="FigmaPrefabManifest"/>, lets the user
    /// choose on a per-GO basis which of its components should sync with Figma on
    /// re-import and which should be preserved untouched.
    /// </summary>
    public sealed class NodePolicyInspectorWindow : EditorWindow
    {
        [MenuItem("Window/SoobakFigma2Unity/Node Policy Inspector")]
        public static void Open()
        {
            var win = GetWindow<NodePolicyInspectorWindow>();
            win.titleContent = new GUIContent("Node Policy");
            win.minSize = new Vector2(320, 200);
            win.Show();
        }

        private Vector2 _scroll;

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject to see its Figma sync policy.", MessageType.Info);
                return;
            }

            var manifest = FindManifest(go);
            if (manifest == null)
            {
                EditorGUILayout.HelpBox($"'{go.name}' is not inside a Figma-tracked prefab.", MessageType.Info);
                return;
            }

            var entry = manifest.GetEntry(go.transform);
            if (!entry.HasValue)
            {
                EditorGUILayout.HelpBox($"'{go.name}' is user-added — it has no Figma identity and will always be preserved on re-import.", MessageType.Info);
                return;
            }

            var policy = FigmaManagedTypesRegistryProvider.Get();
            var e = entry.Value;

            EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Figma Node: {e.figmaNodeId}");
            if (!string.IsNullOrEmpty(e.figmaComponentId))
                EditorGUILayout.LabelField($"Figma Component: {e.figmaComponentId}");
            EditorGUILayout.Space(4);

            // Whole-GO lock fast-path
            var newLock = EditorGUILayout.ToggleLeft(
                new GUIContent("🔒 Lock this GameObject",
                    "When locked, re-import will skip this GameObject and everything below it entirely."),
                e.wholeGoLocked);
            if (newLock != e.wholeGoLocked)
            {
                ManifestEditAction.Apply(
                    go,
                    "Toggle Figma Lock",
                    (m, t) => m.SetWholeGoLocked(t, newLock));
                EditorApplication.RepaintHierarchyWindow();
            }

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(newLock))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                EditorGUILayout.LabelField("Figma-managed components", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                int managedCount = DrawComponentList(go, manifest, policy, e, includeManaged: true);
                EditorGUI.indentLevel--;
                if (managedCount == 0)
                    EditorGUILayout.LabelField(" — none —", EditorStyles.miniLabel);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("User-added components (always preserved)", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                int userCount = DrawComponentList(go, manifest, policy, e, includeManaged: false);
                EditorGUI.indentLevel--;
                if (userCount == 0)
                    EditorGUILayout.LabelField(" — none —", EditorStyles.miniLabel);

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset to Defaults"))
                    {
                        var preservedTypes = entry.HasValue && entry.Value.userPreservedTypes != null
                            ? new List<string>(entry.Value.userPreservedTypes)
                            : new List<string>();
                        ManifestEditAction.Apply(go, "Reset Figma Overrides", (m, t) =>
                        {
                            m.SetWholeGoLocked(t, false);
                            foreach (var name in preservedTypes)
                            {
                                var type = System.Type.GetType(name, throwOnError: false);
                                if (type != null) m.SetComponentPreserved(t, type, false);
                            }
                        });
                    }
                    if (GUILayout.Button("Preserve All"))
                    {
                        var managedTypesOnGo = new List<System.Type>();
                        foreach (var c in go.GetComponents<Component>())
                        {
                            if (c == null) continue;
                            var ct = c.GetType();
                            if (ct == typeof(FigmaPrefabManifest)) continue;
                            if (policy.IsManaged(ct)) managedTypesOnGo.Add(ct);
                        }
                        ManifestEditAction.Apply(go, "Preserve all managed components", (m, t) =>
                        {
                            foreach (var ct in managedTypesOnGo)
                                m.SetComponentPreserved(t, ct, true);
                        });
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private int DrawComponentList(
            GameObject go,
            FigmaPrefabManifest manifest,
            FigmaManagedTypesRegistry policy,
            FigmaPrefabManifest.Entry entry,
            bool includeManaged)
        {
            int drawn = 0;
            var components = go.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t == typeof(FigmaPrefabManifest)) continue;
                bool managed = policy.IsManaged(t);
                if (managed != includeManaged) continue;

                drawn++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (managed)
                    {
                        var preserved = IsPreservedByName(entry, t);
                        EditorGUILayout.LabelField(new GUIContent(preserved ? "🔒" : "🎨"), GUILayout.Width(24));
                        EditorGUILayout.LabelField(t.Name);
                        if (GUILayout.Button(preserved ? "Sync" : "Preserve", GUILayout.Width(80)))
                        {
                            bool newPreserved = !preserved;
                            ManifestEditAction.Apply(
                                go,
                                newPreserved ? "Preserve component" : "Sync component from Figma",
                                (m, tr) => m.SetComponentPreserved(tr, t, newPreserved));
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField(new GUIContent("╌"), GUILayout.Width(24));
                        EditorGUILayout.LabelField(t.Name);
                    }
                }
            }
            return drawn;
        }

        private static bool IsPreservedByName(FigmaPrefabManifest.Entry entry, Type t)
        {
            if (entry.userPreservedTypes == null) return false;
            var qname = t.AssemblyQualifiedName;
            var fname = t.FullName;
            for (int i = 0; i < entry.userPreservedTypes.Count; i++)
                if (entry.userPreservedTypes[i] == qname || entry.userPreservedTypes[i] == fname) return true;
            return false;
        }

        private static FigmaPrefabManifest FindManifest(GameObject go)
        {
            return go.GetComponentInParent<FigmaPrefabManifest>(true);
        }
    }
}
