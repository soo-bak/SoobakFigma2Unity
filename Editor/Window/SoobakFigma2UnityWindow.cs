using System.Threading;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Window
{
    public sealed class SoobakFigma2UnityWindow : EditorWindow
    {
        [MenuItem("Window/SoobakFigma2Unity")]
        public static void ShowWindow()
        {
            var window = GetWindow<SoobakFigma2UnityWindow>();
            window.titleContent = new GUIContent("SoobakFigma2Unity");
            window.minSize = new Vector2(420, 600);
        }

        // State
        private ImportProfile _profile = new ImportProfile();
        private FrameTreeView _treeView = new FrameTreeView();
        private ImportLogger _logger = new ImportLogger();
        private CancellationTokenSource _cts;
        private bool _isFetching;
        private bool _isImporting;
        private Vector2 _mainScroll;
        private Vector2 _logScroll;

        // Scale options
        private static readonly string[] ScaleLabels = { "1x", "2x", "3x", "4x" };
        private static readonly float[] ScaleValues = { 1f, 2f, 3f, 4f };
        private int _scaleIndex = 1; // default 2x

        // Import mode labels
        private static readonly string[] ModeLabels =
        {
            "Components Only",
            "Screen Only",
            "Screen + Components"
        };

        // Color space labels
        private static readonly string[] ColorSpaceLabels = { "Auto Detect", "Linear", "Gamma" };

        private void OnEnable()
        {
            _profile.PersonalAccessToken = SoobakSettings.Token;
            _profile.PrefabOutputPath = SoobakSettings.PrefabPath;
            _profile.ScreenOutputPath = SoobakSettings.ScreenPath;
            _profile.ImageOutputPath = SoobakSettings.ImagePath;
            _profile.ImageScale = SoobakSettings.ImageScale;
            _scaleIndex = System.Array.IndexOf(ScaleValues, _profile.ImageScale);
            if (_scaleIndex < 0) _scaleIndex = 1;

            _logger.OnLogUpdated += Repaint;
        }

        private void OnDisable()
        {
            _logger.OnLogUpdated -= Repaint;
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            DrawConnectionSection();
            DrawFrameSelectionSection();
            DrawImportModeSection();
            DrawImageSection();
            DrawLayoutSection();
            DrawColorSection();
            DrawPrefabSection();
            DrawOutputSection();
            DrawImportButton();
            DrawLogSection();

            EditorGUILayout.EndScrollView();
        }

        // ─── Connection ─────────────────────────────────
        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _profile.FigmaUrl = EditorGUILayout.TextField("Figma URL", _profile.FigmaUrl);

            EditorGUILayout.BeginHorizontal();
            _profile.PersonalAccessToken = EditorGUILayout.PasswordField("Token", _profile.PersonalAccessToken);
            if (GUILayout.Button("Save", GUILayout.Width(50)))
            {
                SoobakSettings.Token = _profile.PersonalAccessToken;
                _logger.Info("Token saved.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(_isFetching || _isImporting);
            if (GUILayout.Button("Fetch", GUILayout.Width(80)))
                FetchFile();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Frame Selection ────────────────────────────
        private void DrawFrameSelectionSection()
        {
            EditorGUILayout.LabelField("Frame Selection", EditorStyles.boldLabel);
            _treeView.OnGUI(200);
            EditorGUILayout.Space(8);
        }

        // ─── Import Mode ────────────────────────────────
        private void DrawImportModeSection()
        {
            EditorGUILayout.LabelField("Import Mode", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.Mode = (ImportMode)GUILayout.SelectionGrid(
                (int)_profile.Mode, ModeLabels, 1, EditorStyles.radioButton);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Image ──────────────────────────────────────
        private void DrawImageSection()
        {
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _scaleIndex = EditorGUILayout.Popup("Scale", _scaleIndex, ScaleLabels);
            _profile.ImageScale = ScaleValues[_scaleIndex];

            _profile.AutoNineSlice = EditorGUILayout.Toggle("Auto 9-slice detection", _profile.AutoNineSlice);
            _profile.SolidColorOptimization = EditorGUILayout.Toggle(
                new GUIContent("Solid color → no image", "Use Color tint instead of downloading image for solid fills"),
                _profile.SolidColorOptimization);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Layout ─────────────────────────────────────
        private void DrawLayoutSection()
        {
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _profile.ConvertAutoLayout = EditorGUILayout.Toggle("Convert Auto Layout → LayoutGroup", _profile.ConvertAutoLayout);
            _profile.FlattenEmptyGroups = EditorGUILayout.Toggle("Flatten empty groups", _profile.FlattenEmptyGroups);
            _profile.ApplyConstraints = EditorGUILayout.Toggle("Apply constraints → Anchors", _profile.ApplyConstraints);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Color ──────────────────────────────────────
        private void DrawColorSection()
        {
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _profile.ColorSpace = (ColorSpaceMode)EditorGUILayout.Popup(
                "Color Space", (int)_profile.ColorSpace, ColorSpaceLabels);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Prefab ─────────────────────────────────────
        private void DrawPrefabSection()
        {
            EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _profile.GeneratePrefabVariants = EditorGUILayout.Toggle(
                "Generate Prefab Variants from Figma Variants", _profile.GeneratePrefabVariants);
            _profile.MapComponentInstances = EditorGUILayout.Toggle(
                "Map Component Instances → Prefab Instances", _profile.MapComponentInstances);
            _profile.PreserveOnReimport = EditorGUILayout.Toggle(
                "Preserve user modifications on re-import", _profile.PreserveOnReimport);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Output ─────────────────────────────────────
        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _profile.PrefabOutputPath = DrawPathField("Prefab Path", _profile.PrefabOutputPath);
            _profile.ScreenOutputPath = DrawPathField("Screen Path", _profile.ScreenOutputPath);
            _profile.ImageOutputPath = DrawPathField("Image Path", _profile.ImageOutputPath);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        private string DrawPathField(string label, string currentPath)
        {
            EditorGUILayout.BeginHorizontal();
            var path = EditorGUILayout.TextField(label, currentPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var selected = EditorUtility.OpenFolderPanel(label, currentPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert absolute path to relative Assets path
                    if (selected.StartsWith(Application.dataPath))
                        path = "Assets" + selected.Substring(Application.dataPath.Length);
                    else
                        _logger.Warn("Please select a folder inside the Assets directory.");
                }
            }
            EditorGUILayout.EndHorizontal();
            return path;
        }

        // ─── Import Button ──────────────────────────────
        private void DrawImportButton()
        {
            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(_isFetching || _isImporting);
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            if (GUILayout.Button(_isImporting ? "Importing..." : "Import Selected", buttonStyle))
                RunImport();
            EditorGUI.EndDisabledGroup();

            if (_isImporting)
            {
                if (GUILayout.Button("Cancel"))
                    _cts?.Cancel();
            }

            EditorGUILayout.Space(8);
        }

        // ─── Log ────────────────────────────────────────
        private void DrawLogSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
                _logger.Clear();
            EditorGUILayout.EndHorizontal();

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll,
                EditorStyles.helpBox, GUILayout.Height(150));

            foreach (var entry in _logger.Entries)
            {
                var style = new GUIStyle(EditorStyles.label) { wordWrap = true };
                style.normal.textColor = entry.Level switch
                {
                    LogLevel.Error => new UnityEngine.Color(1f, 0.3f, 0.3f),
                    LogLevel.Warning => new UnityEngine.Color(1f, 0.8f, 0.2f),
                    LogLevel.Success => new UnityEngine.Color(0.3f, 0.9f, 0.3f),
                    _ => style.normal.textColor
                };
                EditorGUILayout.LabelField(entry.ToString(), style);
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Actions ────────────────────────────────────
        private void FetchFile()
        {
            var urlInfo = FigmaUrlParser.Parse(_profile.FigmaUrl);
            if (!urlInfo.IsValid)
            {
                _logger.Error("Invalid Figma URL. Use: https://www.figma.com/file/FILEKEY/...");
                return;
            }

            if (string.IsNullOrEmpty(_profile.PersonalAccessToken))
            {
                _logger.Error("Please enter a Figma Personal Access Token.");
                return;
            }

            _isFetching = true;
            _logger.Info($"Fetching file structure (key: {urlInfo.FileKey})...");

            AsyncHelper.RunAsync(async () =>
            {
                using var api = new FigmaApiClient(_profile.PersonalAccessToken);
                var file = await api.GetFileAsync(urlInfo.FileKey, depth: 2);

                AsyncHelper.RunOnMainThread(() =>
                {
                    _treeView.BuildFromDocument(file.Document);
                    _logger.Success($"Fetched: {file.Name} ({_treeView.Roots.Count} pages)");
                    _isFetching = false;
                    Repaint();
                });
            },
            error =>
            {
                _logger.Error($"Fetch failed: {error.Message}");
                _isFetching = false;
                Repaint();
            });
        }

        private void RunImport()
        {
            var urlInfo = FigmaUrlParser.Parse(_profile.FigmaUrl);
            if (!urlInfo.IsValid)
            {
                _logger.Error("Invalid Figma URL.");
                return;
            }

            var selectedIds = _treeView.GetSelectedNodeIds();
            if (selectedIds.Count == 0)
            {
                _logger.Error("No frames selected. Check the frames you want to import.");
                return;
            }

            // Save settings
            SoobakSettings.Token = _profile.PersonalAccessToken;
            SoobakSettings.PrefabPath = _profile.PrefabOutputPath;
            SoobakSettings.ScreenPath = _profile.ScreenOutputPath;
            SoobakSettings.ImagePath = _profile.ImageOutputPath;
            SoobakSettings.ImageScale = _profile.ImageScale;

            _isImporting = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _logger.Info($"Starting import of {selectedIds.Count} frame(s)...");

            AsyncHelper.RunAsync(async () =>
            {
                using var api = new FigmaApiClient(_profile.PersonalAccessToken);
                var pipeline = new ImportPipeline(api, _logger);
                await pipeline.RunAsync(_profile, urlInfo.FileKey, selectedIds, _cts.Token);

                AsyncHelper.RunOnMainThread(() =>
                {
                    _isImporting = false;
                    Repaint();
                });
            },
            error =>
            {
                _logger.Error($"Import failed: {error.Message}");
                _isImporting = false;
                Repaint();
            });
        }
    }
}
