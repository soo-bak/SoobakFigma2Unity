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
        private readonly ImportLogger _logger;
        private readonly NodeConverterRegistry _registry;

        public ImportPipeline(ImportLogger logger)
        {
            _logger = logger;
            _registry = new NodeConverterRegistry();
        }

        // ═══════════════════════════════════════════════════
        //  Mode 1: REST API import
        // ═══════════════════════════════════════════════════

        public async Task RunFromApiAsync(
            ImportProfile profile, string token, string fileKey,
            IReadOnlyList<string> selectedNodeIds, CancellationToken ct = default)
        {
            var progress = new ProgressReporter("SoobakFigma2Unity Import", 6);

            using var api = new FigmaApiClient(token);
            var ctx = CreateContext(profile, fileKey);

            try
            {
                // 1. Fetch node trees
                progress.Step("Fetching node data from Figma...");
                _logger.Info("Fetching node data from Figma...");
                var nodesResponse = await api.GetFileNodesAsync(fileKey, selectedNodeIds, ct);

                if (nodesResponse.Components != null) ctx.Components = nodesResponse.Components;
                if (nodesResponse.ComponentSets != null) ctx.ComponentSets = nodesResponse.ComponentSets;

                var frames = CollectFrames(nodesResponse, selectedNodeIds, ctx);
                if (frames.Count == 0) { _logger.Error("No frames to convert."); return; }
                _logger.Success($"Fetched {frames.Count} frame(s).");

                // 2. Collect image requirements
                progress.Step("Analyzing nodes...");
                ctx.BuildNodeIndex(frames);
                foreach (var frame in frames)
                    CollectImageRequirements(frame, ctx);
                _logger.Info($"Nodes to rasterize: {ctx.NodesToRasterize.Count}, Image fills: {ctx.ImageFillRefs.Count}");

                // 3. Download images (parallel)
                progress.Step($"Downloading {ctx.NodesToRasterize.Count} images...");
                var downloader = new ImageDownloader(api, _logger);
                if (ctx.NodesToRasterize.Count > 0)
                    ctx.NodeImagePaths = await downloader.DownloadNodeImagesAsync(
                        fileKey, ctx.NodesToRasterize.ToList(), GetTempImageDir(), profile.ImageScale, ct);
                if (ctx.ImageFillRefs.Count > 0)
                    ctx.FillImagePaths = await downloader.DownloadImageFillsAsync(
                        fileKey, ctx.ImageFillRefs, GetTempImageDir(), ct);

                // 4-6. Shared conversion
                progress.Step("Importing images...");
                ImportAllImages(ctx, profile);
                progress.Step("Generating prefabs...");
                GenerateAndConvert(frames, ctx, profile);
                progress.Step("Done!");

                SaveSnapshot(frames, fileKey, nodesResponse.LastModified, profile);
                _logger.Success($"Import complete! {frames.Count} prefab(s) created.");
            }
            catch (System.OperationCanceledException) { _logger.Warn("Import cancelled."); }
            catch (System.Exception e) { _logger.Error($"Import failed: {e.Message}"); Debug.LogException(e); }
            finally { progress.Done(); }
        }

        // ═══════════════════════════════════════════════════
        //  Shared conversion logic
        // ═══════════════════════════════════════════════════

        private ImportContext CreateContext(ImportProfile profile, string fileKey)
        {
            return new ImportContext { Profile = profile, Logger = _logger, FileKey = fileKey };
        }

        private List<FigmaNode> CollectFrames(FigmaNodesResponse response, IReadOnlyList<string> nodeIds, ImportContext ctx)
        {
            var frames = new List<FigmaNode>();
            foreach (var nodeId in nodeIds)
            {
                if (response.Nodes.TryGetValue(nodeId, out var wrapper) && wrapper.Document != null)
                {
                    frames.Add(wrapper.Document);
                    if (wrapper.Components != null)
                        foreach (var kv in wrapper.Components)
                            ctx.Components[kv.Key] = kv.Value;
                }
                else _logger.Warn($"Node {nodeId} not found.");
            }
            return frames;
        }

        private void GenerateAndConvert(List<FigmaNode> frames, ImportContext ctx, ImportProfile profile)
        {
            if (profile.Mode != ImportMode.ScreenOnly)
                GenerateComponentPrefabs(frames, ctx, profile);

            if (profile.Mode != ImportMode.ComponentsOnly)
            {
                foreach (var frame in frames)
                    ConvertAndSaveFrame(frame, ctx, profile);
            }

            AssetDatabase.Refresh();
        }

        private void SaveSnapshot(List<FigmaNode> frames, string fileKey, string lastModified, ImportProfile profile)
        {
            var snapshot = new ImportSnapshot
            {
                FileKey = fileKey,
                FileVersion = lastModified,
                LastModified = lastModified,
                ImageScale = profile.ImageScale
            };
            foreach (var f in frames) snapshot.UpdateHashes(f);
            snapshot.Save(profile.ScreenOutputPath);
        }

        // ─── Frame conversion ───────────────────────────

        private void ConvertAndSaveFrame(FigmaNode frameNode, ImportContext ctx, ImportProfile profile)
        {
            _logger.Info($"Converting: {frameNode.Name}");
            var rootGo = new GameObject(frameNode.Name);
            var rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.sizeDelta = SizeCalculator.GetSize(frameNode);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);

            rootGo.AddComponent<Runtime.FigmaNodeRef>().FigmaNodeId = frameNode.Id;
            ApplyFrameProperties(rootGo, frameNode, ctx);

            if (profile.ConvertAutoLayout && frameNode.IsAutoLayout)
                AutoLayoutMapper.Apply(rootGo, frameNode);
            if (frameNode.HasChildren)
                ConvertChildren(frameNode, rootGo, ctx, profile);

            var outputDir = profile.ScreenOutputPath;
            string prefabPath;
            if (profile.PreserveOnReimport)
            {
                var existing = MergeStrategy.FindExistingPrefab(frameNode.Id, outputDir);
                if (existing != null)
                    prefabPath = new MergeStrategy(_logger).MergeIntoPrefab(rootGo, existing)
                        ?? PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
                else
                    prefabPath = PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);
            }
            else
                prefabPath = PrefabBuilder.SaveOrReplacePrefab(rootGo, outputDir, frameNode.Name);

            _logger.Success($"Saved prefab: {prefabPath}");
            Object.DestroyImmediate(rootGo);
        }

        // ─── Child conversion ───────────────────────────

        private void ConvertChildren(FigmaNode parentNode, GameObject parentGo, ImportContext ctx, ImportProfile profile, FigmaNode posOverride = null)
        {
            if (parentNode.Children == null) return;

            // The Figma node whose absolute bbox defines the coordinate space
            // for child positioning. Normally this is parentNode, but when we
            // are flattening a Group, posOverride points to the OUTER visual
            // parent so children compute correct positions in Unity hierarchy.
            var visualParent = posOverride ?? parentNode;

            // Mask redirection state — scoped to this child loop only.
            // When we encounter an isMask sibling, subsequent siblings get reparented
            // under it so Unity's parent-child Mask correctly clips them.
            GameObject currentMaskGo = null;
            FigmaNode currentMaskNode = null;
            bool parentIsAutoLayout = parentNode.IsAutoLayout;

            foreach (var childNode in parentNode.Children)
            {
                if (NodeConverterRegistry.ShouldSkip(childNode)) continue;
                if (profile.FlattenEmptyGroups && IsEmptyGroup(childNode))
                {
                    // Pass the actual visual parent so flattened children compute
                    // positions relative to the outer parent (where they'll live in Unity).
                    ConvertChildren(childNode, parentGo, ctx, profile, posOverride: visualParent);
                    continue;
                }

                var converter = _registry.GetConverter(childNode);
                if (converter == null) { ctx.Logger.Warn($"No converter for '{childNode.Type}': {childNode.Name}"); continue; }

                bool isMaskNode = childNode.IsMask;
                // In auto-layout containers, mask redirection conflicts with LayoutGroup.
                bool redirectIntoMask = !parentIsAutoLayout && currentMaskGo != null && !isMaskNode;

                GameObject targetParentGo = redirectIntoMask ? currentMaskGo : parentGo;
                FigmaNode posParentNode = redirectIntoMask ? currentMaskNode : visualParent;

                GameObject childGo;
                try
                {
                    childGo = converter.Convert(childNode, targetParentGo, ctx);
                }
                catch (System.Exception e)
                {
                    ctx.Logger.Error($"Convert failed for '{childNode.Name}': {e.Message}");
                    continue;
                }
                if (childGo == null) continue;

                var rt = childGo.GetComponent<RectTransform>();
                if (rt == null) continue;

                // Position relative to the *logical* parent (mask node when redirected, else real parent).
                // When flattening, visualParent points to the outer parent for correct positioning.
                if (!redirectIntoMask && parentNode.IsAutoLayout && !childNode.IsAbsolutePositioned)
                {
                    if (profile.ConvertAutoLayout)
                        AutoLayoutMapper.ApplyChildLayoutProperties(childGo, childNode, parentNode);
                }
                else
                {
                    // Constraint-based positioning (used for absolute children of auto-layout
                    // parents AND non-auto-layout siblings). For absolute children inside an
                    // auto-layout parent, also tell Unity LayoutGroup to ignore this child.
                    if (parentNode.IsAutoLayout && childNode.IsAbsolutePositioned)
                    {
                        var le = childGo.GetComponent<UnityEngine.UI.LayoutElement>()
                            ?? childGo.AddComponent<UnityEngine.UI.LayoutElement>();
                        le.ignoreLayout = true;
                    }

                    if (profile.ApplyConstraints)
                    {
                        AnchorMapper.Apply(rt, childNode, posParentNode);
                    }
                    else
                    {
                        var relPos = SizeCalculator.GetRelativePosition(childNode, posParentNode);
                        var childSize = SizeCalculator.GetSize(childNode);
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        rt.anchorMin = new Vector2(0f, 1f);
                        rt.anchorMax = new Vector2(0f, 1f);
                        rt.offsetMin = new Vector2(relPos.x, -(relPos.y + childSize.y));
                        rt.offsetMax = new Vector2(relPos.x + childSize.x, -relPos.y);
                    }
                }

                if (profile.ConvertAutoLayout && childNode.IsAutoLayout)
                    AutoLayoutMapper.Apply(childGo, childNode);

                // Recurse into children — but skip if rasterized OR if isMask FRAME already
                // consumed a child vector's sprite (avoid duplicate visual).
                bool isRasterized = ctx.NodeSprites.ContainsKey(childNode.Id);
                bool isMaskFrameConsumedChild =
                    isMaskNode &&
                    childNode.NodeType == FigmaNodeType.FRAME &&
                    childGo.GetComponent<UnityEngine.UI.Image>() != null &&
                    childGo.GetComponent<UnityEngine.UI.Image>().sprite != null &&
                    !ctx.NodeSprites.ContainsKey(childNode.Id);

                if (childNode.HasChildren && childNode.NodeType != FigmaNodeType.TEXT
                    && !isRasterized && !isMaskFrameConsumedChild)
                    ConvertChildren(childNode, childGo, ctx, profile);

                if (childNode.NodeType != FigmaNodeType.TEXT)
                {
                    var detector = new NodePurposeDetector(ctx.Logger, ctx.Components);
                    var purpose = detector.Detect(childNode);
                    if (purpose != NodePurposeDetector.DetectedPurpose.None)
                        PurposeApplier.Apply(childGo, childNode, purpose, ctx);
                }
                InteractionMapper.Apply(childGo, childNode, ctx);

                // Set up mask wrapper *after* this node is fully processed —
                // so the mask itself is positioned relative to its real Figma parent.
                if (isMaskNode && !parentIsAutoLayout)
                {
                    currentMaskGo = childGo;
                    currentMaskNode = childNode;
                    ctx.Logger.Info($"Mask wrapper: '{childNode.Name}' will clip subsequent siblings");
                }
            }
        }

        // ─── Frame properties ───────────────────────────

        private void ApplyFrameProperties(GameObject go, FigmaNode node, ImportContext ctx)
        {
            // Apply rasterized sprite first (handles effects/shadows even without visible fills)
            if (ctx.NodeSprites.TryGetValue(node.Id, out var sprite))
            {
                var img = go.AddComponent<UnityEngine.UI.Image>();
                img.sprite = sprite;
                img.type = sprite.border != Vector4.zero
                    ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            else if (node.HasVisibleFills)
            {
                if (ctx.Profile.SolidColorOptimization && SolidColorOptimizer.CanUseSolidColor(node))
                {
                    var (color, fillOpacity) = SolidColorOptimizer.GetTopSolidFill(node);
                    if (color != null)
                    {
                        var img = go.AddComponent<UnityEngine.UI.Image>();
                        img.color = Color.ColorSpaceHelper.Convert(color, node.Opacity * fillOpacity);
                    }
                }
            }
            // isMask precedence (matches FrameConverter behavior)
            if (node.IsMask)
            {
                var img = go.GetComponent<UnityEngine.UI.Image>();
                if (img == null)
                {
                    img = go.AddComponent<UnityEngine.UI.Image>();
                    Sprite shapeSprite = null;
                    if (ctx.NodeSprites.TryGetValue(node.Id, out var ownSprite))
                        shapeSprite = ownSprite;
                    else if (node.Children != null)
                    {
                        foreach (var ch in node.Children)
                        {
                            if (ctx.NodeSprites.TryGetValue(ch.Id, out var childSprite))
                            { shapeSprite = childSprite; break; }
                        }
                    }
                    if (shapeSprite != null) img.sprite = shapeSprite;
                    else img.color = UnityEngine.Color.white;
                }
                go.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            }
            else if (node.ClipsContent)
            {
                var img = go.GetComponent<UnityEngine.UI.Image>()
                    ?? go.AddComponent<UnityEngine.UI.Image>();
                if (img.sprite == null) img.color = UnityEngine.Color.white;
                go.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = img.sprite != null;
            }
            if (node.Opacity < 1f)
                go.AddComponent<CanvasGroup>().alpha = node.Opacity;
        }

        // ─── Component prefabs ──────────────────────────

        private void GenerateComponentPrefabs(List<FigmaNode> frames, ImportContext ctx, ImportProfile profile)
        {
            var components = new List<FigmaNode>();
            var componentSets = new List<FigmaNode>();
            foreach (var f in frames) CollectComponents(f, components, componentSets);
            _logger.Info($"Found {components.Count} components, {componentSets.Count} component sets.");

            if (profile.GeneratePrefabVariants)
            {
                var vb = new PrefabVariantBuilder(_logger);
                foreach (var cs in componentSets)
                    vb.BuildVariantChain(cs, n => ConvertNodeToGameObject(n, ctx, profile), profile.PrefabOutputPath, ctx);
            }
            foreach (var comp in components)
            {
                if (ctx.GeneratedPrefabs.ContainsKey(comp.Id)) continue;
                if (ctx.Components.TryGetValue(comp.Id, out var m) && !string.IsNullOrEmpty(m.ComponentSetId)) continue;
                var go = ConvertNodeToGameObject(comp, ctx, profile);
                ctx.GeneratedPrefabs[comp.Id] = PrefabBuilder.SaveOrReplacePrefab(go, profile.PrefabOutputPath, comp.Name);
                _logger.Success($"Component prefab: {ctx.GeneratedPrefabs[comp.Id]}");
                Object.DestroyImmediate(go);
            }
        }

        private void CollectComponents(FigmaNode node, List<FigmaNode> c, List<FigmaNode> cs)
        {
            if (node.NodeType == FigmaNodeType.COMPONENT_SET) { cs.Add(node); return; }
            if (node.NodeType == FigmaNodeType.COMPONENT) c.Add(node);
            if (node.Children != null) foreach (var ch in node.Children) CollectComponents(ch, c, cs);
        }

        private GameObject ConvertNodeToGameObject(FigmaNode node, ImportContext ctx, ImportProfile profile)
        {
            var go = new GameObject(node.Name);
            go.AddComponent<RectTransform>().sizeDelta = SizeCalculator.GetSize(node);
            go.AddComponent<Runtime.FigmaNodeRef>().FigmaNodeId = node.Id;
            ApplyFrameProperties(go, node, ctx);
            if (profile.ConvertAutoLayout && node.IsAutoLayout) AutoLayoutMapper.Apply(go, node);
            bool isRasterized = ctx.NodeSprites.ContainsKey(node.Id);
            if (node.HasChildren && !isRasterized) ConvertChildren(node, go, ctx, profile);
            return go;
        }

        // ─── Image import ───────────────────────────────

        private void ImportAllImages(ImportContext ctx, ImportProfile profile)
        {
            var imageDir = profile.ImageOutputPath;
            var nineSlice = profile.AutoNineSlice ? new NineSliceDetector(_logger, profile.ImageScale) : null;

            var nodeToAsset = new Dictionary<string, string>();
            var fillToAsset = new Dictionary<string, string>();
            var allPaths = new List<string>();

            foreach (var kv in ctx.NodeImagePaths)
            {
                var ap = Path.Combine(imageDir, $"{kv.Key.Replace(":", "_")}.png");
                ImageImporter.CopyToAssets(kv.Value, ap);
                nodeToAsset[kv.Key] = ap; allPaths.Add(ap);
            }
            foreach (var kv in ctx.FillImagePaths)
            {
                var ap = Path.Combine(imageDir, $"fill_{kv.Key.Replace(":", "_").Replace("/", "_")}.png");
                ImageImporter.CopyToAssets(kv.Value, ap);
                fillToAsset[kv.Key] = ap; allPaths.Add(ap);
            }
            if (allPaths.Count == 0) return;

            _logger.Info($"Batch importing {allPaths.Count} images...");
            var sprites = ImageImporter.BatchImport(allPaths, profile.ImageScale);

            foreach (var kv in nodeToAsset)
            {
                if (!sprites.TryGetValue(kv.Value, out var s)) continue;
                ctx.NodeSprites[kv.Key] = s;
                if (nineSlice != null && ctx.NodeIndex.TryGetValue(kv.Key, out var node))
                {
                    var borders = nineSlice.DetectBorders(node);
                    if (borders != Vector4.zero)
                    {
                        ImageImporter.SetSliceBorders(kv.Value, borders);
                        s = AssetDatabase.LoadAssetAtPath<Sprite>(kv.Value);
                        if (s != null) ctx.NodeSprites[kv.Key] = s;
                    }
                }
            }
            foreach (var kv in fillToAsset)
                if (sprites.TryGetValue(kv.Value, out var s)) ctx.FillSprites[kv.Key] = s;

            _logger.Success($"Imported {sprites.Count} sprites.");
        }

        // ─── Helpers ────────────────────────────────────

        private void CollectImageRequirements(FigmaNode node, ImportContext ctx)
        {
            if (NodeConverterRegistry.ShouldSkip(node)) return;

            bool needsRaster = NodeConverterRegistry.NeedsRasterization(node);
            if (needsRaster)
            {
                ctx.NodesToRasterize.Add(node.Id);
                // Node will be rasterized as a whole — don't also download the raw fill image
                // (raw fill is the uncropped sprite sheet, rasterization gives the correct crop)
            }
            else if (node.Fills != null)
            {
                // Only collect fill image refs for nodes that are NOT rasterized
                foreach (var fill in node.Fills)
                    if (fill.Visible && fill.IsImage && !string.IsNullOrEmpty(fill.ImageRef))
                        ctx.ImageFillRefs.Add(fill.ImageRef);
            }

            if (node.Children != null)
                foreach (var child in node.Children)
                    CollectImageRequirements(child, ctx);
        }

        private static bool IsEmptyGroup(FigmaNode n) =>
            n.NodeType == FigmaNodeType.GROUP &&
            !n.HasVisibleFills &&
            (n.Effects == null || n.Effects.Count == 0) &&
            !NodeConverterRegistry.IsMaskContainer(n); // preserve mask containers

        private static FigmaNode FindNodeById(FigmaNode root, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            if (root.Id == nodeId) return root;
            if (root.Children != null)
                foreach (var c in root.Children)
                {
                    var found = FindNodeById(c, nodeId);
                    if (found != null) return found;
                }
            return null;
        }

        private static string GetTempImageDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "SoobakFigma2Unity_Images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
