using System;
using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Integration
{
    /// <summary>
    /// Reflection-friendly entry point for optional editor tools that want to import one
    /// Figma selection without taking a compile-time dependency on SoobakFigma2Unity.
    /// </summary>
    public static class QuickHierarchyImporter
    {
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(SoobakSettings.Token);
        public static bool IsBusy { get; private set; }
        public static bool LastOperationFailed { get; private set; }
        public static string Status { get; private set; } = "Ready";

        public static string StartFromClipboard(RectTransform parent, bool keepPrefabLink)
        {
            if (IsBusy)
                return "A Soobak import is already running.";

            if (parent == null || EditorUtility.IsPersistent(parent))
                return "Select a RectTransform in a scene or Prefab Mode first.";

            string figmaUrl = EditorGUIUtility.systemCopyBuffer?.Trim();
            FigmaUrlInfo urlInfo = FigmaUrlParser.Parse(figmaUrl);
            if (!urlInfo.IsValid || string.IsNullOrWhiteSpace(urlInfo.NodeId))
                return "In Figma, select a Frame and use Copy link to selection first.";

            if (!IsConfigured)
                return "Open Window > SoobakFigma2Unity > Importer and save a Figma token first.";

            ImportProfile profile = BuildProfile(figmaUrl);
            string token = SoobakSettings.Token;
            var logger = new ImportLogger();

            IsBusy = true;
            LastOperationFailed = false;
            Status = "Importing Figma hierarchy…";

            AsyncHelper.RunAsync(async () =>
            {
                var pipeline = new ImportPipeline(logger);
                await pipeline.RunFromApiAsync(
                    profile,
                    token,
                    urlInfo.FileKey,
                    new[] { urlInfo.NodeId });

                AsyncHelper.RunOnMainThread(() =>
                {
                    try
                    {
                        string prefabPath = pipeline.PrimaryPrefabPath;
                        if (string.IsNullOrWhiteSpace(prefabPath))
                        {
                            Fail(BuildImportFailureMessage(logger));
                            return;
                        }

                        InstantiateUnderParent(prefabPath, parent, keepPrefabLink);
                    }
                    catch (Exception exception)
                    {
                        Fail($"Soobak hierarchy paste failed: {exception.Message}");
                    }
                });
            }, exception => Fail($"Soobak import failed: {exception.Message}"));

            return string.Empty;
        }

        private static ImportProfile BuildProfile(string figmaUrl)
        {
            var profile = new ImportProfile
            {
                FigmaUrl = figmaUrl,
                PersonalAccessToken = SoobakSettings.Token,
                PrefabOutputPath = SoobakSettings.PrefabPath,
                ScreenOutputPath = SoobakSettings.ScreenPath,
                ImageOutputPath = SoobakSettings.ImagePath,
                ImageScale = SoobakSettings.ImageScale,
                ImportBoldFromFigma = SoobakSettings.ImportBoldFromFigma
            };

            foreach (TMP_FontAsset fontAsset in LoadFontAssets())
                profile.FontAssets.Add(fontAsset);

            return profile;
        }

        private static IEnumerable<TMP_FontAsset> LoadFontAssets()
        {
            string[] guids = SoobakSettings.FontAssetGuids.Split(
                new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null)
                    yield return font;
            }

            if (guids.Length == 0 && TMP_Settings.defaultFontAsset != null)
                yield return TMP_Settings.defaultFontAsset;
        }

        private static void InstantiateUnderParent(
            string prefabPath,
            RectTransform parent,
            bool keepPrefabLink)
        {
            if (parent == null)
            {
                Fail($"Import finished, but the selected parent no longer exists. Prefab: {prefabPath}");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Fail($"Import finished, but the generated prefab could not be loaded: {prefabPath}");
                return;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                Fail($"Import finished, but the generated prefab could not be instantiated: {prefabPath}");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Paste Figma Hierarchy");
            if (instance.transform is RectTransform rectTransform)
            {
                rectTransform.anchoredPosition3D = Vector3.zero;
                rectTransform.localRotation = Quaternion.identity;
                rectTransform.localScale = Vector3.one;
            }

            if (!keepPrefabLink && PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.UserAction);
            }

            if (instance.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(instance.scene);

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            IsBusy = false;
            LastOperationFailed = false;
            string resultType = keepPrefabLink ? "as a prefab instance" : "as an editable hierarchy";
            Status = $"Pasted {instance.name} under {parent.name} {resultType}.";
            Debug.Log($"[SoobakFigma2Unity] {Status} Source prefab: {prefabPath}", instance);
        }

        private static string BuildImportFailureMessage(ImportLogger logger)
        {
            IReadOnlyList<LogEntry> entries = logger.Entries;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index].Level == LogLevel.Error)
                    return entries[index].Message;
            }

            return "Soobak did not create a prefab for the selected Figma node.";
        }

        private static void Fail(string message)
        {
            IsBusy = false;
            LastOperationFailed = true;
            Status = message;
            Debug.LogError($"[SoobakFigma2Unity] {message}");
        }
    }
}
