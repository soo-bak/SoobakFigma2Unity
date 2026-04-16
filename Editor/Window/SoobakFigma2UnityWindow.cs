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
            window.minSize = new Vector2(420, 600);
        }

        // State
        private ImportProfile _profile = new ImportProfile();
        private FrameTreeView _treeView = new FrameTreeView();
        private ImportLogger _logger = new ImportLogger();
        private bool _isImporting;
        private Vector2 _mainScroll;
        private Vector2 _logScroll;
        private string _exportFilePath = "";
        private SoobakExportFile _loadedExport;

        // Scale options
        private static readonly string[] ScaleLabels = { "1x", "2x", "3x", "4x" };
        private static readonly float[] ScaleValues = { 1f, 2f, 3f, 4f };
        private int _scaleIndex = 1;

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
        }

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            DrawExportFileSection();
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

        // ─── Export File ────────────────────────────────
        private void DrawExportFileSection()
        {
            EditorGUILayout.LabelField("Export File", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            _exportFilePath = EditorGUILayout.TextField("Path", _exportFilePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var path = EditorUtility.OpenFilePanel(
                    "Select .soobak.json export file",
                    "",
                    "json");
                if (!string.IsNullOrEmpty(path))
                {
                    _exportFilePath = path;
                    LoadExportFile();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(_isImporting || string.IsNullOrEmpty(_exportFilePath));
            if (GUILayout.Button("Load", GUILayout.Width(80)))
                LoadExportFile();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_loadedExport != null)
            {
                EditorGUILayout.HelpBox(
                    $"File: {_loadedExport.Manifest.FileName}\n" +
                    $"Frames: {_loadedExport.Manifest.Frames?.Count ?? 0}\n" +
                    $"Images: {_loadedExport.EmbeddedImages?.Count ?? 0}\n" +
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

            EditorGUI.BeginDisabledGroup(_loadedExport != null);
            _scaleIndex = EditorGUILayout.Popup("Scale", _scaleIndex, ScaleLabels);
            _profile.ImageScale = ScaleValues[_scaleIndex];
            EditorGUI.EndDisabledGroup();

            if (_loadedExport != null)
            {
                EditorGUILayout.HelpBox(
                    $"Scale is determined by the export file ({_loadedExport.Manifest.ImageScale}x).",
                    MessageType.None);
                _profile.ImageScale = _loadedExport.Manifest.ImageScale;
            }

            _profile.AutoNineSlice = EditorGUILayout.Toggle("Auto 9-slice detection", _profile.AutoNineSlice);
            _profile.SolidColorOptimization = EditorGUILayout.Toggle(
                new GUIContent("Solid color → no image", "Use Color tint instead of image for solid fills"),
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

            EditorGUI.BeginDisabledGroup(_isImporting || _loadedExport == null);
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            if (GUILayout.Button(_isImporting ? "Importing..." : "Import", buttonStyle))
                RunImport();
            EditorGUI.EndDisabledGroup();

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
        private void LoadExportFile()
        {
            if (string.IsNullOrEmpty(_exportFilePath))
            {
                _logger.Error("No file path specified.");
                return;
            }

            var loader = new SoobakExportLoader(_logger);
            _loadedExport = loader.Load(_exportFilePath);

            if (_loadedExport != null)
            {
                // Build frame tree for selection
                BuildFrameTreeFromExport();

                // Use export scale
                _profile.ImageScale = _loadedExport.Manifest.ImageScale;
                _scaleIndex = System.Array.IndexOf(ScaleValues, _profile.ImageScale);
                if (_scaleIndex < 0) _scaleIndex = 1;
            }

            Repaint();
        }

        private void BuildFrameTreeFromExport()
        {
            if (_loadedExport?.Manifest?.Frames == null) return;

            // Create a virtual document for the tree view
            var doc = new Models.FigmaNode
            {
                Id = "doc",
                Name = _loadedExport.Manifest.FileName,
                Type = "DOCUMENT",
                Children = new System.Collections.Generic.List<Models.FigmaNode>
                {
                    new Models.FigmaNode
                    {
                        Id = "page",
                        Name = "Exported Frames",
                        Type = "CANVAS",
                        Children = _loadedExport.Manifest.Frames
                    }
                }
            };

            _treeView.BuildFromDocument(doc);

            // Auto-select all frames
            foreach (var root in _treeView.Roots)
            {
                root.Selected = true;
                foreach (var child in root.Children)
                    child.Selected = true;
            }
        }

        private void RunImport()
        {
            if (_loadedExport == null)
            {
                _logger.Error("No export file loaded. Click Load first.");
                return;
            }

            // Save settings
            SoobakSettings.PrefabPath = _profile.PrefabOutputPath;
            SoobakSettings.ScreenPath = _profile.ScreenOutputPath;
            SoobakSettings.ImagePath = _profile.ImageOutputPath;
            SoobakSettings.ImageScale = _profile.ImageScale;

            _isImporting = true;
            _logger.Info("Starting import...");

            try
            {
                var pipeline = new ImportPipeline(_logger);
                pipeline.RunFromExport(_exportFilePath, _profile);
            }
            catch (System.Exception e)
            {
                _logger.Error($"Import failed: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                _isImporting = false;
                Repaint();
            }
        }
    }
}
