using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Converters;
using SoobakFigma2Unity.Editor.Import;
using SoobakFigma2Unity.Editor.Layout;
using SoobakFigma2Unity.Editor.Mapping;
using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Prefabs;
using SoobakFigma2Unity.Editor.Settings;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    internal sealed class ImportPipeline
    {
        private readonly ImportLogger _logger;
        private readonly NodeConverterRegistry _registry;

        public ImportPipeline(ImportLogger logger)
        {
            _logger = logger;
            _registry = new NodeConverterRegistry();
        }

        /// <summary>
        /// Run import from a .soobak.json export file.
        /// </summary>
        public void RunFromExport(string exportFilePath, ImportProfile profile)
        {
            var progress = new ProgressReporter("SoobakFigma2Unity Import", 5);

            try
            {
                // Step 1: Load export file
                progress.Step("Loading export file...");
                var loader = new SoobakExportLoader(_logger);
                var export = loader.Load(exportFilePath);
                if (export == null) return;

                var manifest = export.Manifest;
                var ctx = new ImportContext
                {
                    Profile = profile,
                    Logger = _logger,
                    FileKey = manifest.FileKey ?? "",
                    Components = manifest.Components ?? new Dictionary<string, FigmaComponent>(),
                    ComponentSets = manifest.ComponentSets ?? new Dictionary<string, FigmaComponentSet>(),
                };

                var framesToConvert = manifest.Frames;
                if (framesToConvert == null || framesToConvert.Count == 0)
                {
                    _logger.Error("No frames in export file.");
                    return;
                }

                _logger.Success($"Loaded {framesToConvert.Count} frame(s) from '{manifest.FileName}'.");

                // Step 2: Extract embedded images
                progress.Step("Extracting images...");
                var tempImageDir = GetTempImageDir();
                loader.ExtractImages(export, tempImageDir, ctx);

                // Step 3: Import images into Unity as Sprites
                progress.Step("Importing images into Unity...");
                ctx.BuildNodeIndex(framesToConvert);
                ImportAllImages(ctx, profile);

                // Step 4: Generate component prefabs
                progress.Step("Generating prefabs...");
                if (profile.Mode != ImportMode.ScreenOnly)
                    GenerateComponentPrefabs(framesToConvert, ctx, profile);

                // Step 5: Convert frames to screen prefabs
                progress.Step("Converting frames...");
                AssetDatabase.StartAssetEditing();
                try
                {
                    if (profile.Mode != ImportMode.ComponentsOnly)
                    {
                        foreach (var frame in framesToConvert)
                            ConvertAndSaveFrame(frame, ctx, profile);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                }

                // Save snapshot for incremental updates
                var snapshot = new ImportSnapshot
                {
                    FileKey = manifest.FileKey ?? "",
                    FileVersion = manifest.ExportedAt,
                    LastModified = manifest.ExportedAt,
                    ImageScale = manifest.ImageScale
                };
                foreach (var frame in framesToConvert)
                    snapshot.UpdateHashes(frame);
                snapshot.Save(profile.ScreenOutputPath);

                _logger.Success($"Import complete! {framesToConvert.Count} prefab(s) created.");
            }
            catch (System.Exception e)
            {
                _logger.Error($"Import failed: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                progress.Done();
            }
        }

        // ─── Frame conversion ───────────────────────────────

        private void ConvertAndSaveFrame(FigmaNode frameNode, ImportContext ctx, ImportProfile profile)
        {
            _logger.Info($"Converting: {frameNode.Name}");

            var rootGo = new GameObject(frameNode.Name);
            var rootRt = rootGo.AddComponent<RectTransform>();

            var size = SizeCalculator.GetSize(frameNode);
            rootRt.sizeDelta = size;
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);

            var nodeRef = rootGo.AddComponent<Runtime.FigmaNodeRef>();
            nodeRef.FigmaNodeId = frameNode.Id;

            ApplyFrameProperties(rootGo, frameNode, ctx);

            if (profile.ConvertAutoLayout && frameNode.IsAutoLayout)
                AutoLayoutMapper.Apply(rootGo, frameNode);

            if (frameNode.HasChildren)
                ConvertChildren(frameNode, rootGo, ctx, profile);

            // Save as prefab (with merge if re-importing)
            var outputDir = profile.ScreenOutputPath;
            string prefabPath;

            if (profile.PreserveOnReimport)
            {
                var existingPath = MergeStrategy.FindExistingPrefab(frameNode.Id, outputDir);
                if (existingPath != null)
                {
                    var merger = new MergeStrategy(_logger);
                    prefabPath = merger.MergeIntoPrefab(rootGo, existingPath) ??
                        PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
                }
                else
                {
                    prefabPath = PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
                }
            }
            else
            {
                prefabPath = PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
            }

            _logger.Success($"Saved prefab: {prefabPath}");
            Object.DestroyImmediate(rootGo);
        }

        // ─── Child conversion (recursive) ───────────────────

        private void ConvertChildren(FigmaNode parentNode, GameObject parentGo, ImportContext ctx, ImportProfile profile)
        {
            if (parentNode.Children == null) return;

            foreach (var childNode in parentNode.Children)
            {
                if (NodeConverterRegistry.ShouldSkip(childNode))
                    continue;

                if (profile.FlattenEmptyGroups && IsEmptyGroup(childNode))
                {
                    ConvertChildren(childNode, parentGo, ctx, profile);
                    continue;
                }

                var converter = _registry.GetConverter(childNode);
                if (converter == null)
                {
                    ctx.Logger.Warn($"No converter for '{childNode.Type}': {childNode.Name}");
                    continue;
                }

                var childGo = converter.Convert(childNode, parentGo, ctx);
                if (childGo == null) continue;

                var rt = childGo.GetComponent<RectTransform>();

                // Positioning
                if (parentNode.IsAutoLayout && !childNode.IsAbsolutePositioned)
                {
                    if (profile.ConvertAutoLayout)
                        AutoLayoutMapper.ApplyChildLayoutProperties(childGo, childNode, parentNode);
                }
                else if (profile.ApplyConstraints)
                {
                    AnchorMapper.Apply(rt, childNode, parentNode);
                }
                else
                {
                    var relPos = SizeCalculator.GetRelativePosition(childNode, parentNode);
                    var childSize = SizeCalculator.GetSize(childNode);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.offsetMin = new Vector2(relPos.x, -(relPos.y + childSize.y));
                    rt.offsetMax = new Vector2(relPos.x + childSize.x, -relPos.y);
                }

                if (profile.ConvertAutoLayout && childNode.IsAutoLayout)
                    AutoLayoutMapper.Apply(childGo, childNode);

                if (childNode.HasChildren && childNode.NodeType != FigmaNodeType.TEXT)
                    ConvertChildren(childNode, childGo, ctx, profile);

                // Auto-detect UI purpose
                if (childNode.NodeType != FigmaNodeType.TEXT)
                {
                    var detector = new NodePurposeDetector(ctx.Logger, ctx.Components);
                    var purpose = detector.Detect(childNode);
                    if (purpose != NodePurposeDetector.DetectedPurpose.None)
                        PurposeApplier.Apply(childGo, childNode, purpose, ctx);
                }

                // Interaction hints
                InteractionMapper.Apply(childGo, childNode, ctx);
            }
        }

        // ─── Frame properties ───────────────────────────────

        private void ApplyFrameProperties(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (node.HasVisibleFills)
            {
                if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
                {
                    var color = SolidColorOptimizer.GetTopSolidColor(node);
                    if (color != null)
                    {
                        var image = go.AddComponent<UnityEngine.UI.Image>();
                        image.color = Color.ColorSpaceHelper.Convert(color, node.Opacity);
                    }
                }
                else if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
                {
                    var image = go.AddComponent<UnityEngine.UI.Image>();
                    image.sprite = sprite;
                    image.type = (sprite.border != Vector4.zero)
                        ? UnityEngine.UI.Image.Type.Sliced
                        : UnityEngine.UI.Image.Type.Simple;
                }
            }

            if (node.ClipsContent)
            {
                var image = go.GetComponent<UnityEngine.UI.Image>();
                if (image == null)
                {
                    image = go.AddComponent<UnityEngine.UI.Image>();
                    image.color = UnityEngine.Color.white;
                }
                go.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = image.sprite != null;
            }

            if (node.Opacity < 1f)
            {
                var cg = go.AddComponent<CanvasGroup>();
                cg.alpha = node.Opacity;
            }
        }

        // ─── Component prefab generation ────────────────────

        private void GenerateComponentPrefabs(List<FigmaNode> frames, ImportContext ctx, ImportProfile profile)
        {
            var components = new List<FigmaNode>();
            var componentSets = new List<FigmaNode>();
            foreach (var frame in frames)
                CollectComponents(frame, components, componentSets);

            _logger.Info($"Found {components.Count} components, {componentSets.Count} component sets.");

            if (profile.GeneratePrefabVariants)
            {
                var variantBuilder = new PrefabVariantBuilder(_logger);
                foreach (var cs in componentSets)
                {
                    variantBuilder.BuildVariantChain(
                        cs, node => ConvertNodeToGameObject(node, ctx, profile),
                        profile.PrefabOutputPath, ctx);
                }
            }

            foreach (var comp in components)
            {
                if (ctx.GeneratedPrefabs.ContainsKey(comp.Id)) continue;
                if (ctx.Components.TryGetValue(comp.Id, out var meta) &&
                    !string.IsNullOrEmpty(meta.ComponentSetId)) continue;

                var go = ConvertNodeToGameObject(comp, ctx, profile);
                var path = PrefabBuilder.SaveOrReplacePrefab(go, profile.PrefabOutputPath, comp.Name);
                ctx.GeneratedPrefabs[comp.Id] = path;
                _logger.Success($"Component prefab: {path}");
                Object.DestroyImmediate(go);
            }
        }

        private void CollectComponents(FigmaNode node, List<FigmaNode> components, List<FigmaNode> componentSets)
        {
            if (node.NodeType == FigmaNodeType.COMPONENT_SET) { componentSets.Add(node); return; }
            if (node.NodeType == FigmaNodeType.COMPONENT) components.Add(node);
            if (node.Children != null)
                foreach (var child in node.Children)
                    CollectComponents(child, components, componentSets);
        }

        private GameObject ConvertNodeToGameObject(FigmaNode node, ImportContext ctx, ImportProfile profile)
        {
            var go = new GameObject(node.Name);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = SizeCalculator.GetSize(node);

            var nodeRef = go.AddComponent<Runtime.FigmaNodeRef>();
            nodeRef.FigmaNodeId = node.Id;

            ApplyFrameProperties(go, node, ctx);
            if (profile.ConvertAutoLayout && node.IsAutoLayout)
                AutoLayoutMapper.Apply(go, node);
            if (node.HasChildren)
                ConvertChildren(node, go, ctx, profile);
            return go;
        }

        // ─── Image import ───────────────────────────────────

        private void ImportAllImages(ImportContext ctx, ImportProfile profile)
        {
            var imageDir = profile.ImageOutputPath;
            var nineSliceDetector = profile.AutoNineSlice
                ? new NineSliceDetector(_logger, profile.ImageScale)
                : null;

            var nodeIdToAssetPath = new Dictionary<string, string>();
            var fillRefToAssetPath = new Dictionary<string, string>();
            var allAssetPaths = new List<string>();

            foreach (var kv in ctx.NodeImagePaths)
            {
                var safeName = kv.Key.Replace(":", "_");
                var assetPath = Path.Combine(imageDir, $"{safeName}.png");
                ImageImporter.CopyToAssets(kv.Value, assetPath);
                nodeIdToAssetPath[kv.Key] = assetPath;
                allAssetPaths.Add(assetPath);
            }

            foreach (var kv in ctx.FillImagePaths)
            {
                var safeName = kv.Key.Replace(":", "_").Replace("/", "_");
                var assetPath = Path.Combine(imageDir, $"fill_{safeName}.png");
                ImageImporter.CopyToAssets(kv.Value, assetPath);
                fillRefToAssetPath[kv.Key] = assetPath;
                allAssetPaths.Add(assetPath);
            }

            if (allAssetPaths.Count == 0) return;

            _logger.Info($"Batch importing {allAssetPaths.Count} images...");
            var spritesByPath = ImageImporter.BatchImport(allAssetPaths, profile.ImageScale);

            foreach (var kv in nodeIdToAssetPath)
            {
                if (spritesByPath.TryGetValue(kv.Value, out var sprite))
                {
                    ctx.NodeSprites[kv.Key] = sprite;
                    if (nineSliceDetector != null && ctx.NodeIndex.TryGetValue(kv.Key, out var node))
                    {
                        var borders = nineSliceDetector.DetectBorders(node);
                        if (borders != Vector4.zero)
                        {
                            ImageImporter.SetSliceBorders(kv.Value, borders);
                            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(kv.Value);
                            if (sprite != null) ctx.NodeSprites[kv.Key] = sprite;
                        }
                    }
                }
            }

            foreach (var kv in fillRefToAssetPath)
            {
                if (spritesByPath.TryGetValue(kv.Value, out var sprite))
                    ctx.FillSprites[kv.Key] = sprite;
            }

            _logger.Success($"Imported {spritesByPath.Count} sprites.");
        }

        // ─── Helpers ────────────────────────────────────────

        private static bool IsEmptyGroup(FigmaNode node)
        {
            if (node.NodeType != FigmaNodeType.GROUP) return false;
            if (node.HasVisibleFills) return false;
            if (node.Effects != null && node.Effects.Count > 0) return false;
            return true;
        }

        private static string GetTempImageDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "SoobakFigma2Unity_Images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
