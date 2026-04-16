using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Assets;
using SoobakFigma2Unity.Editor.Converters;
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
        private readonly FigmaApiClient _api;
        private readonly ImportLogger _logger;
        private readonly NodeConverterRegistry _registry;

        public ImportPipeline(FigmaApiClient api, ImportLogger logger)
        {
            _api = api;
            _logger = logger;
            _registry = new NodeConverterRegistry();
        }

        public async Task RunAsync(
            ImportProfile profile,
            string fileKey,
            IReadOnlyList<string> selectedNodeIds,
            CancellationToken ct = default)
        {
            var ctx = new ImportContext
            {
                Profile = profile,
                Logger = _logger,
                FileKey = fileKey
            };

            try
            {
                // Step 1: Fetch full node trees for selected frames
                _logger.Info("Fetching node data from Figma...");
                var nodesResponse = await _api.GetFileNodesAsync(fileKey, selectedNodeIds, ct);

                if (nodesResponse.Components != null)
                    ctx.Components = nodesResponse.Components;
                if (nodesResponse.ComponentSets != null)
                    ctx.ComponentSets = nodesResponse.ComponentSets;

                var framesToConvert = new List<FigmaNode>();
                foreach (var nodeId in selectedNodeIds)
                {
                    if (nodesResponse.Nodes.TryGetValue(nodeId, out var wrapper) && wrapper.Document != null)
                    {
                        framesToConvert.Add(wrapper.Document);
                        // Merge per-node components
                        if (wrapper.Components != null)
                        {
                            foreach (var kv in wrapper.Components)
                                ctx.Components[kv.Key] = kv.Value;
                        }
                    }
                    else
                    {
                        _logger.Warn($"Node {nodeId} not found in response.");
                    }
                }

                if (framesToConvert.Count == 0)
                {
                    _logger.Error("No frames to convert.");
                    return;
                }

                _logger.Success($"Fetched {framesToConvert.Count} frame(s).");

                // Step 2: Check for incremental update
                var snapshot = ImportSnapshot.Load(profile.ScreenOutputPath);
                HashSet<string> changedNodes = null;
                bool isIncremental = false;

                if (snapshot != null && snapshot.FileKey == fileKey)
                {
                    // Check if file version changed
                    if (snapshot.LastModified == nodesResponse.LastModified &&
                        Mathf.Approximately(snapshot.ImageScale, profile.ImageScale))
                    {
                        _logger.Info("No changes detected since last import. Skipping.");
                        return;
                    }

                    // Determine which nodes changed
                    changedNodes = new HashSet<string>();
                    foreach (var frame in framesToConvert)
                    {
                        var frameChanges = snapshot.GetChangedNodeIds(frame);
                        foreach (var id in frameChanges)
                            changedNodes.Add(id);
                    }

                    if (changedNodes.Count == 0)
                    {
                        _logger.Info("No node-level changes detected. Skipping.");
                        return;
                    }

                    isIncremental = true;
                    _logger.Info($"Incremental update: {changedNodes.Count} node(s) changed.");
                }

                // Step 3: Build node index and collect image requirements
                ctx.BuildNodeIndex(framesToConvert);
                foreach (var frame in framesToConvert)
                    CollectImageRequirements(frame, ctx);

                _logger.Info($"Nodes to rasterize: {ctx.NodesToRasterize.Count}, Image fills: {ctx.ImageFillRefs.Count}");

                // Step 3: Download images
                var imageDownloader = new ImageDownloader(_api, _logger);

                if (ctx.NodesToRasterize.Count > 0)
                {
                    var nodeImagePaths = await imageDownloader.DownloadNodeImagesAsync(
                        fileKey,
                        ctx.NodesToRasterize.ToList(),
                        GetTempImageDir(),
                        profile.ImageScale,
                        ct
                    );
                    ctx.NodeImagePaths = nodeImagePaths;
                }

                if (ctx.ImageFillRefs.Count > 0)
                {
                    var fillPaths = await imageDownloader.DownloadImageFillsAsync(
                        fileKey,
                        ctx.ImageFillRefs,
                        GetTempImageDir(),
                        ct
                    );
                    ctx.FillImagePaths = fillPaths;
                }

                // Step 4: Import images into Unity as Sprites
                _logger.Info("Importing images into Unity...");
                ImportAllImages(ctx, profile);

                // Step 5: Generate component prefabs first (so instances can link to them)
                if (profile.Mode != ImportMode.ScreenOnly)
                {
                    GenerateComponentPrefabs(framesToConvert, ctx, profile);
                }

                // Step 6: Convert each frame to GameObjects and save as prefabs
                AssetDatabase.StartAssetEditing();
                try
                {
                    if (profile.Mode != ImportMode.ComponentsOnly)
                    {
                        foreach (var frame in framesToConvert)
                        {
                            ct.ThrowIfCancellationRequested();
                            ConvertAndSaveFrame(frame, ctx, profile);
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.Refresh();
                }

                // Save snapshot for incremental updates
                var newSnapshot = new ImportSnapshot
                {
                    FileKey = fileKey,
                    FileVersion = nodesResponse.LastModified,
                    LastModified = nodesResponse.LastModified,
                    ImageScale = profile.ImageScale
                };
                foreach (var frame in framesToConvert)
                    newSnapshot.UpdateHashes(frame);
                newSnapshot.Save(profile.ScreenOutputPath);

                _logger.Success($"Import complete! {framesToConvert.Count} prefab(s) created.");
            }
            catch (System.OperationCanceledException)
            {
                _logger.Warn("Import cancelled.");
            }
            catch (System.Exception e)
            {
                _logger.Error($"Import failed: {e.Message}");
                Debug.LogException(e);
            }
        }

        private void ConvertAndSaveFrame(FigmaNode frameNode, ImportContext ctx, ImportProfile profile)
        {
            _logger.Info($"Converting: {frameNode.Name}");

            // Create root GameObject
            var rootGo = new GameObject(frameNode.Name);
            var rootRt = rootGo.AddComponent<RectTransform>();

            // Set root size from Figma frame
            var size = SizeCalculator.GetSize(frameNode);
            rootRt.sizeDelta = size;
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);

            // Add FigmaNodeRef
            var nodeRef = rootGo.AddComponent<SoobakFigma2Unity.Runtime.FigmaNodeRef>();
            nodeRef.FigmaNodeId = frameNode.Id;

            // Apply fills to root if any
            var frameConverter = new FrameConverter();
            ApplyFrameProperties(rootGo, frameNode, ctx);

            // Apply auto-layout to root if applicable
            if (profile.ConvertAutoLayout && frameNode.IsAutoLayout)
                AutoLayoutMapper.Apply(rootGo, frameNode);

            // Recursively convert children
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
                    prefabPath = merger.MergeIntoPrefab(rootGo, existingPath);
                    if (prefabPath == null)
                        prefabPath = PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
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

            // Cleanup
            Object.DestroyImmediate(rootGo);
        }

        private void ConvertChildren(FigmaNode parentNode, GameObject parentGo, ImportContext ctx, ImportProfile profile)
        {
            if (parentNode.Children == null)
                return;

            foreach (var childNode in parentNode.Children)
            {
                if (NodeConverterRegistry.ShouldSkip(childNode))
                    continue;

                // Flatten empty groups
                if (profile.FlattenEmptyGroups && IsEmptyGroup(childNode))
                {
                    // Skip this node but process its children under the current parent
                    ConvertChildren(childNode, parentGo, ctx, profile);
                    continue;
                }

                // Get converter
                var converter = _registry.GetConverter(childNode);
                if (converter == null)
                {
                    ctx.Logger.Warn($"No converter for node type '{childNode.Type}': {childNode.Name}");
                    continue;
                }

                // Convert node to GameObject
                var childGo = converter.Convert(childNode, parentGo, ctx);
                if (childGo == null)
                    continue;

                var rt = childGo.GetComponent<RectTransform>();

                // Apply positioning
                if (parentNode.IsAutoLayout && !childNode.IsAbsolutePositioned)
                {
                    // Child within auto-layout: use LayoutElement
                    if (profile.ConvertAutoLayout)
                        AutoLayoutMapper.ApplyChildLayoutProperties(childGo, childNode, parentNode);
                }
                else if (profile.ApplyConstraints)
                {
                    // Non-auto-layout: use constraints for anchoring
                    AnchorMapper.Apply(rt, childNode, parentNode);
                }
                else
                {
                    // Fallback: absolute positioning with top-left anchor
                    var relPos = SizeCalculator.GetRelativePosition(childNode, parentNode);
                    var childSize = SizeCalculator.GetSize(childNode);
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(relPos.x, -relPos.y);
                    rt.sizeDelta = childSize;
                }

                // Apply auto-layout if this child is also an auto-layout container
                if (profile.ConvertAutoLayout && childNode.IsAutoLayout)
                    AutoLayoutMapper.Apply(childGo, childNode);

                // Recurse into children
                if (childNode.HasChildren && childNode.NodeType != FigmaNodeType.TEXT)
                    ConvertChildren(childNode, childGo, ctx, profile);

                // Auto-detect UI purpose (Button, InputField, ScrollView, etc.)
                if (childNode.NodeType != FigmaNodeType.TEXT)
                {
                    var detector = new NodePurposeDetector(ctx.Logger, ctx.Components);
                    var purpose = detector.Detect(childNode);
                    if (purpose != NodePurposeDetector.DetectedPurpose.None)
                        PurposeApplier.Apply(childGo, childNode, purpose, ctx);
                }

                // Apply prototype interaction hints
                InteractionMapper.Apply(childGo, childNode, ctx);
            }
        }

        private void ApplyFrameProperties(GameObject go, FigmaNode node, ImportContext ctx)
        {
            // Apply fills using solid color optimization
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
                }
            }

            // Clips content
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

            // Opacity
            if (node.Opacity < 1f)
            {
                var cg = go.AddComponent<CanvasGroup>();
                cg.alpha = node.Opacity;
            }
        }

        /// <summary>
        /// Walk selected frames, find all COMPONENT and COMPONENT_SET nodes,
        /// and generate prefabs for them before screen conversion.
        /// </summary>
        private void GenerateComponentPrefabs(List<FigmaNode> frames, ImportContext ctx, ImportProfile profile)
        {
            var components = new List<FigmaNode>();
            var componentSets = new List<FigmaNode>();

            foreach (var frame in frames)
                CollectComponents(frame, components, componentSets);

            _logger.Info($"Found {components.Count} components, {componentSets.Count} component sets.");

            // Process component sets first (they generate base + variant prefabs)
            if (profile.GeneratePrefabVariants)
            {
                var variantBuilder = new PrefabVariantBuilder(_logger);
                foreach (var cs in componentSets)
                {
                    variantBuilder.BuildVariantChain(
                        cs,
                        node => ConvertNodeToGameObject(node, ctx, profile),
                        profile.PrefabOutputPath,
                        ctx
                    );
                }
            }

            // Process standalone components (not part of a component set)
            foreach (var comp in components)
            {
                // Skip if already handled as part of a component set
                if (ctx.GeneratedPrefabs.ContainsKey(comp.Id))
                    continue;

                // Check if this component belongs to a component set
                if (ctx.Components.TryGetValue(comp.Id, out var meta) &&
                    !string.IsNullOrEmpty(meta.ComponentSetId))
                    continue;

                var go = ConvertNodeToGameObject(comp, ctx, profile);
                var path = PrefabBuilder.SaveOrReplacePrefab(go, profile.PrefabOutputPath, comp.Name);
                ctx.GeneratedPrefabs[comp.Id] = path;
                _logger.Success($"Component prefab: {path}");
                Object.DestroyImmediate(go);
            }
        }

        private void CollectComponents(FigmaNode node, List<FigmaNode> components, List<FigmaNode> componentSets)
        {
            if (node.NodeType == FigmaNodeType.COMPONENT_SET)
            {
                componentSets.Add(node);
                return; // Children are variants, handled by PrefabVariantBuilder
            }

            if (node.NodeType == FigmaNodeType.COMPONENT)
                components.Add(node);

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectComponents(child, components, componentSets);
            }
        }

        /// <summary>
        /// Convert a single Figma node to a standalone GameObject tree (for component prefab generation).
        /// </summary>
        private GameObject ConvertNodeToGameObject(FigmaNode node, ImportContext ctx, ImportProfile profile)
        {
            var go = new GameObject(node.Name);
            var rt = go.AddComponent<RectTransform>();
            var size = SizeCalculator.GetSize(node);
            rt.sizeDelta = size;

            var nodeRef = go.AddComponent<SoobakFigma2Unity.Runtime.FigmaNodeRef>();
            nodeRef.FigmaNodeId = node.Id;

            ApplyFrameProperties(go, node, ctx);

            if (profile.ConvertAutoLayout && node.IsAutoLayout)
                AutoLayoutMapper.Apply(go, node);

            if (node.HasChildren)
                ConvertChildren(node, go, ctx, profile);

            return go;
        }

        private void CollectImageRequirements(FigmaNode node, ImportContext ctx)
        {
            if (NodeConverterRegistry.ShouldSkip(node))
                return;

            // Check if this node needs rasterization
            if (NodeConverterRegistry.NeedsRasterization(node))
                ctx.NodesToRasterize.Add(node.Id);

            // Check for image fill references
            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    if (fill.Visible && fill.IsImage && !string.IsNullOrEmpty(fill.ImageRef))
                        ctx.ImageFillRefs.Add(fill.ImageRef);
                }
            }

            // Recurse
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectImageRequirements(child, ctx);
            }
        }

        private void ImportAllImages(ImportContext ctx, ImportProfile profile)
        {
            var imageDir = profile.ImageOutputPath;
            var nineSliceDetector = profile.AutoNineSlice
                ? new NineSliceDetector(_logger, profile.ImageScale)
                : null;

            // Import rasterized node images
            foreach (var kv in ctx.NodeImagePaths)
            {
                var nodeId = kv.Key;
                var tempPath = kv.Value;
                var safeName = nodeId.Replace(":", "_");
                var assetPath = Path.Combine(imageDir, $"{safeName}.png");

                var sprite = ImageImporter.ImportAsSprite(tempPath, assetPath, profile.ImageScale);
                if (sprite != null)
                {
                    ctx.NodeSprites[nodeId] = sprite;

                    // Apply 9-slice if applicable
                    if (nineSliceDetector != null && ctx.NodeIndex.TryGetValue(nodeId, out var node))
                    {
                        var borders = nineSliceDetector.DetectBorders(node);
                        if (borders != Vector4.zero)
                        {
                            ImageImporter.SetSliceBorders(assetPath, borders);
                            // Reload sprite after border change
                            sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                            if (sprite != null)
                                ctx.NodeSprites[nodeId] = sprite;
                        }
                    }
                }
            }

            // Import fill images
            foreach (var kv in ctx.FillImagePaths)
            {
                var imageRef = kv.Key;
                var tempPath = kv.Value;
                var safeName = imageRef.Replace(":", "_").Replace("/", "_");
                var assetPath = Path.Combine(imageDir, $"fill_{safeName}.png");

                var sprite = ImageImporter.ImportAsSprite(tempPath, assetPath, profile.ImageScale);
                if (sprite != null)
                    ctx.FillSprites[imageRef] = sprite;
            }
        }

        private static bool IsEmptyGroup(FigmaNode node)
        {
            // A group with no visual content (no fills, no effects) is just structural
            if (node.NodeType != FigmaNodeType.GROUP)
                return false;
            if (node.HasVisibleFills)
                return false;
            if (node.Effects != null && node.Effects.Count > 0)
                return false;
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
