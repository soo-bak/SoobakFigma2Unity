using System.Threading;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Import;
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
            window.minSize = new Vector2(420, 650);
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

        // Source mode
        private enum SourceMode { FigmaAPI, LocalFile }
        private SourceMode _sourceMode = SourceMode.FigmaAPI;
        private static readonly string[] SourceModeLabels = { "Figma API", "Local Export (.soobak.json)" };

        // API fields
        private string _figmaUrl = "";
        private string _token = "";

        // Local file fields
        private string _exportFilePath = "";
        private SoobakExportFile _loadedExport;

        // Scale
        private static readonly string[] ScaleLabels = { "1x", "2x", "3x", "4x" };
        private static readonly float[] ScaleValues = { 1f, 2f, 3f, 4f };
        private int _scaleIndex = 1;

        private static readonly string[] ModeLabels = { "Components Only", "Screen Only", "Screen + Components" };
        private static readonly string[] ColorSpaceLabels = { "Auto Detect", "Linear", "Gamma" };

        private void OnEnable()
        {
            _token = SoobakSettings.Token;
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

            DrawSourceSection();

            if (_sourceMode == SourceMode.FigmaAPI)
                DrawApiSection();
            else
                DrawLocalFileSection();

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

        // ─── Source Mode ────────────────────────────────
        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _sourceMode = (SourceMode)GUILayout.Toolbar((int)_sourceMode, SourceModeLabels);
            EditorGUILayout.Space(8);
        }

        // ─── Figma API ──────────────────────────────────
        private void DrawApiSection()
        {
            EditorGUILayout.LabelField("Figma API Connection", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _figmaUrl = EditorGUILayout.TextField("Figma URL", _figmaUrl);

            EditorGUILayout.BeginHorizontal();
            _token = EditorGUILayout.PasswordField("Token", _token);
            if (GUILayout.Button("Save", GUILayout.Width(50)))
            {
                SoobakSettings.Token = _token;
                _logger.Info("Token saved.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(_isFetching || _isImporting);
            if (GUILayout.Button("Fetch", GUILayout.Width(80)))
                FetchFromApi();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Local File ─────────────────────────────────
        private void DrawLocalFileSection()
        {
            EditorGUILayout.LabelField("Export File", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            _exportFilePath = EditorGUILayout.TextField("Path", _exportFilePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var path = EditorUtility.OpenFilePanel("Select .soobak.json", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    _exportFilePath = path;
                    LoadExportFile();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(_isImporting || string.IsNullOrEmpty(_exportFilePath));
            if (GUILayout.Button("Load"))
                LoadExportFile();
            EditorGUI.EndDisabledGroup();

            if (_loadedExport != null)
            {
                EditorGUILayout.HelpBox(
                    $"File: {_loadedExport.Manifest.FileName}\n" +
                    $"Frames: {_loadedExport.Manifest.Frames?.Count ?? 0} | " +
                    $"Images: {_loadedExport.EmbeddedImages?.Count ?? 0} | " +
                    $"Scale: {_loadedExport.Manifest.ImageScale}x",
                    MessageType.Info);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Frame Selection ────────────────────────────
        private void DrawFrameSelectionSection()
        {
            EditorGUILayout.LabelField("Frame Selection", EditorStyles.boldLabel);
            _treeView.OnGUI(180);
            EditorGUILayout.Space(8);
        }

        // ─── Import Mode ────────────────────────────────
        private void DrawImportModeSection()
        {
            EditorGUILayout.LabelField("Import Mode", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.Mode = (ImportMode)GUILayout.SelectionGrid((int)_profile.Mode, ModeLabels, 1, EditorStyles.radioButton);
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
                new GUIContent("Solid color → no image"), _profile.SolidColorOptimization);
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
            _profile.ColorSpace = (ColorSpaceMode)EditorGUILayout.Popup("Color Space", (int)_profile.ColorSpace, ColorSpaceLabels);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        // ─── Prefab ─────────────────────────────────────
        private void DrawPrefabSection()
        {
            EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _profile.GeneratePrefabVariants = EditorGUILayout.Toggle("Generate Prefab Variants", _profile.GeneratePrefabVariants);
            _profile.MapComponentInstances = EditorGUILayout.Toggle("Map Instances → Prefab Instances", _profile.MapComponentInstances);
            _profile.PreserveOnReimport = EditorGUILayout.Toggle("Preserve modifications on re-import", _profile.PreserveOnReimport);
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
                    if (selected.StartsWith(Application.dataPath))
                        path = "Assets" + selected.Substring(Application.dataPath.Length);
                    else _logger.Warn("Select a folder inside Assets.");
                }
            }
            EditorGUILayout.EndHorizontal();
            return path;
        }

        // ─── Import Button ──────────────────────────────
        private void DrawImportButton()
        {
            EditorGUILayout.Space(4);
            var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 32 };

            EditorGUI.BeginDisabledGroup(_isFetching || _isImporting);
            if (GUILayout.Button(_isImporting ? "Importing..." : "Import Selected", style))
                RunImport();
            EditorGUI.EndDisabledGroup();

            if (_isImporting && GUILayout.Button("Cancel"))
                _cts?.Cancel();

            EditorGUILayout.Space(8);
        }

        // ─── Log ────────────────────────────────────────
        private void DrawLogSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear", GUILayout.Width(50))) _logger.Clear();
            EditorGUILayout.EndHorizontal();

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, EditorStyles.helpBox, GUILayout.Height(150));
            foreach (var entry in _logger.Entries)
            {
                var s = new GUIStyle(EditorStyles.label) { wordWrap = true };
                s.normal.textColor = entry.Level switch
                {
                    LogLevel.Error => new UnityEngine.Color(1f, 0.3f, 0.3f),
                    LogLevel.Warning => new UnityEngine.Color(1f, 0.8f, 0.2f),
                    LogLevel.Success => new UnityEngine.Color(0.3f, 0.9f, 0.3f),
                    _ => s.normal.textColor
                };
                EditorGUILayout.LabelField(entry.ToString(), s);
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Actions ────────────────────────────────────

        private void FetchFromApi()
        {
            var urlInfo = FigmaUrlParser.Parse(_figmaUrl);
            if (!urlInfo.IsValid) { _logger.Error("Invalid Figma URL."); return; }
            if (string.IsNullOrEmpty(_token)) { _logger.Error("Enter a Figma Personal Access Token."); return; }

            _isFetching = true;
            _logger.Info($"Fetching file (key: {urlInfo.FileKey})...");

            AsyncHelper.RunAsync(async () =>
            {
                using var api = new FigmaApiClient(_token);
                var file = await api.GetFileAsync(urlInfo.FileKey, depth: 2);
                AsyncHelper.RunOnMainThread(() =>
                {
                    _treeView.BuildFromDocument(file.Document);
                    _logger.Success($"Fetched: {file.Name} ({_treeView.Roots.Count} pages)");
                    _isFetching = false;
                    Repaint();
                });
            }, e => { _logger.Error($"Fetch failed: {e.Message}"); _isFetching = false; Repaint(); });
        }

        private void LoadExportFile()
        {
            if (string.IsNullOrEmpty(_exportFilePath)) { _logger.Error("No file selected."); return; }
            var loader = new SoobakExportLoader(_logger);
            _loadedExport = loader.Load(_exportFilePath);
            if (_loadedExport?.Manifest?.Frames != null)
            {
                var doc = new Models.FigmaNode
                {
                    Id = "doc", Name = _loadedExport.Manifest.FileName, Type = "DOCUMENT",
                    Children = new System.Collections.Generic.List<Models.FigmaNode>
                    {
                        new Models.FigmaNode
                        {
                            Id = "page", Name = "Exported", Type = "CANVAS",
                            Children = _loadedExport.Manifest.Frames
                        }
                    }
                };
                _treeView.BuildFromDocument(doc);
                foreach (var r in _treeView.Roots)
                { r.Selected = true; foreach (var c in r.Children) c.Selected = true; }
                _profile.ImageScale = _loadedExport.Manifest.ImageScale;
                _scaleIndex = System.Array.IndexOf(ScaleValues, _profile.ImageScale);
                if (_scaleIndex < 0) _scaleIndex = 1;
            }
            Repaint();
        }

        private void RunImport()
        {
            var selectedIds = _treeView.GetSelectedNodeIds();
            if (selectedIds.Count == 0) { _logger.Error("No frames selected."); return; }

            SoobakSettings.PrefabPath = _profile.PrefabOutputPath;
            SoobakSettings.ScreenPath = _profile.ScreenOutputPath;
            SoobakSettings.ImagePath = _profile.ImageOutputPath;
            SoobakSettings.ImageScale = _profile.ImageScale;

            _isImporting = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            if (_sourceMode == SourceMode.FigmaAPI)
            {
                var urlInfo = FigmaUrlParser.Parse(_figmaUrl);
                if (!urlInfo.IsValid) { _logger.Error("Invalid Figma URL."); _isImporting = false; return; }
                SoobakSettings.Token = _token;

                _logger.Info($"Importing {selectedIds.Count} frame(s) via API...");
                AsyncHelper.RunAsync(async () =>
                {
                    var pipeline = new ImportPipeline(_logger);
                    await pipeline.RunFromApiAsync(_profile, _token, urlInfo.FileKey, selectedIds, _cts.Token);
                    AsyncHelper.RunOnMainThread(() => { _isImporting = false; Repaint(); });
                }, e => { _logger.Error($"Import failed: {e.Message}"); _isImporting = false; Repaint(); });
            }
            else
            {
                if (_loadedExport == null) { _logger.Error("No export file loaded."); _isImporting = false; return; }
                _logger.Info("Importing from local export...");
                try
                {
                    var pipeline = new ImportPipeline(_logger);
                    pipeline.RunFromExport(_exportFilePath, _profile);
                }
                catch (System.Exception e) { _logger.Error($"Import failed: {e.Message}"); }
                finally { _isImporting = false; Repaint(); }
            }
        }
    }
}
