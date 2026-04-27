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

        /// <summary>Prefab asset paths written during the current/last Run*Async call. Used by
        /// the editor window to enable "Undo Last Import".</summary>
        public List<string> SavedPrefabPaths { get; } = new List<string>();

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

                // 3. Download images (parallel). Split by RasterBoundsMode so outside-stroke /
                // outward-effect nodes ship with use_absolute_bounds=false (their full render
                // area, outline preserved). Most nodes default to AbsoluteBounds and ride the
                // happy path where sprite size matches the RectTransform exactly.
                progress.Step($"Downloading {ctx.NodesToRasterize.Count} images...");
                var downloader = new ImageDownloader(api, _logger);
                if (ctx.NodesToRasterize.Count > 0)
                {
                    var allIds = ctx.NodesToRasterize.ToList();
                    var absoluteIds = allIds
                        .Where(id => !ctx.NodeRasterBoundsModes.TryGetValue(id, out var m)
                                     || m == ImportContext.RasterBoundsMode.AbsoluteBounds)
                        .ToList();
                    var renderIds = allIds
                        .Where(id => ctx.NodeRasterBoundsModes.TryGetValue(id, out var m)
                                     && m == ImportContext.RasterBoundsMode.RenderBounds)
                        .ToList();

                    var imagePaths = new Dictionary<string, string>();
                    if (absoluteIds.Count > 0)
                    {
                        var paths = await downloader.DownloadNodeImagesAsync(
                            fileKey, absoluteIds, GetTempImageDir(), profile.ImageScale,
                            useAbsoluteBounds: true, ct);
                        foreach (var kv in paths) imagePaths[kv.Key] = kv.Value;
                    }
                    if (renderIds.Count > 0)
                    {
                        _logger.Info($"Re-fetching {renderIds.Count} stroke/effect nodes with use_absolute_bounds=false to keep their outline.");
                        var paths = await downloader.DownloadNodeImagesAsync(
                            fileKey, renderIds, GetTempImageDir(), profile.ImageScale,
                            useAbsoluteBounds: false, ct);
                        foreach (var kv in paths) imagePaths[kv.Key] = kv.Value;
                    }
                    ctx.NodeImagePaths = imagePaths;
                }
                if (ctx.ImageFillRefs.Count > 0)
                    ctx.FillImagePaths = await downloader.DownloadImageFillsAsync(
                        fileKey, ctx.ImageFillRefs, GetTempImageDir(), ct);

                // 3.5. Build composite backgrounds for "decorative + text" containers.
                // Walks each container we marked during CollectImageRequirements, gathers
                // the per-leaf PNGs that just landed, and alpha-blends them into a single
                // container-sized PNG saved next to the others. Registers the composite
                // path under the container's node id so ImportAllImages picks it up the
                // same way it picks up Figma-rendered nodes — and the convert path will
                // see ctx.NodeSprites[containerId] populated.
                if (ctx.CompositeContainerIds.Count > 0)
                {
                    int compositeOk = 0;
                    foreach (var containerId in ctx.CompositeContainerIds)
                    {
                        if (!ctx.NodeIndex.TryGetValue(containerId, out var container)) continue;
                        var leaves = new List<FigmaNode>();
                        CollectDecorativeLeaves(container, leaves);
                        if (leaves.Count == 0) continue;

                        var compositePath = CompositeBuilder.Compose(
                            container, leaves, ctx.NodeImagePaths, ctx.NodeRasterBoundsModes,
                            profile.ImageScale, GetTempImageDir(), _logger);
                        if (!string.IsNullOrEmpty(compositePath))
                        {
                            ctx.NodeImagePaths[containerId] = compositePath;
                            compositeOk++;
                        }
                    }
                    _logger.Info($"Composited {compositeOk} of {ctx.CompositeContainerIds.Count} mixed text+decorative containers.");
                }

                // 4-6. Shared conversion
                progress.Step("Importing images...");
                ImportAllImages(ctx, profile);
                progress.Step("Generating prefabs...");
                GenerateAndConvert(frames, ctx, profile);
                progress.Step("Done!");

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

            // Identity for the root GameObject so ManifestBuilder records an entry for it,
            // and SmartMerge can match the existing prefab's root → existing root → run
            // ComponentMerger.SyncComponents on it. Without this, the root is anonymous in
            // ctx.NodeIdentities; the existing manifest still carries the root entry by
            // Transform reference, but matching breaks if the existing entry is missing —
            // and any leftover Figma-managed components on the root (e.g. a stale Image
            // from a pre-container-guard import) never get cleaned up.
            ctx.NodeIdentities[rootGo.transform] =
                new ImportContext.NodeIdentityRecord(frameNode.Id, frameNode.ComponentId);

            ApplyFrameProperties(rootGo, frameNode, ctx);

            if (profile.ConvertAutoLayout && frameNode.IsAutoLayout)
                AutoLayoutMapper.Apply(rootGo, frameNode);
            if (frameNode.HasChildren)
                ConvertChildren(frameNode, rootGo, ctx, profile);

            var prefabPath = PrefabMerger.MergeOrSave(rootGo, profile.ScreenOutputPath, frameNode.Name, ctx, profile.MergeMode, _logger);
            if (!string.IsNullOrEmpty(prefabPath)) SavedPrefabPaths.Add(prefabPath);
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
                // Mask redirection: subsequent siblings become children of the mask node.
                // Works in both regular and auto-layout containers — when reparented under
                // the mask, children are no longer direct children of the auto-layout parent,
                // so LayoutGroup naturally stops managing them. Position calc uses the mask
                // node as reference (posParentNode below).
                bool redirectIntoMask = currentMaskGo != null && !isMaskNode;

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

                bool childIsComposite = ctx.CompositeContainerIds.Contains(childNode.Id);
                if (childIsComposite)
                {
                    // Composite container child: the Image already holds the CPU-composite
                    // of every decorative descendant. Skip the normal recursion (it would
                    // re-render those decoratives on top of the composite) and instead add
                    // editable TMP overlays for each TEXT descendant.
                    AddTextOverlays(childNode, childGo, ctx, profile);
                }
                else if (childNode.HasChildren && childNode.NodeType != FigmaNodeType.TEXT
                    && !isRasterized && !isMaskFrameConsumedChild)
                {
                    ConvertChildren(childNode, childGo, ctx, profile);
                }

                // Wrapper FRAME/INSTANCE with a non-trivial blend mode but no own
                // Image: cascade the blend approximation onto descendants. (Image-bearing
                // converters already applied node.BlendMode to themselves.)
                if (!string.IsNullOrEmpty(childNode.BlendMode)
                    && childGo.GetComponent<UnityEngine.UI.Image>() == null)
                    BlendModeHelper.PropagateApproximationToDescendants(childGo, childNode.BlendMode, ctx.Logger);

                // Set up mask wrapper *after* this node is fully processed —
                // so the mask itself is positioned relative to its real Figma parent
                // (auto-layout flow positions it; constraint anchoring otherwise).
                if (isMaskNode)
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
                        // Fill color uses fill.Opacity only; node.Opacity → CanvasGroup below.
                        img.color = Color.ColorSpaceHelper.Convert(color, fillOpacity);
                        if (node.CornerRadius > 0)
                        {
                            var rounded = RoundedRectSpriteGenerator.GetOrGenerate(
                                node.CornerRadius, ctx.Profile.ImageScale, ctx.Profile.ImageOutputPath, ctx.Logger);
                            if (rounded != null)
                            {
                                img.sprite = rounded;
                                img.type = UnityEngine.UI.Image.Type.Sliced;
                            }
                        }
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
                // See FrameConverter for the same rule: if there's a real Image to mask
                // against, use UGUI Mask. Otherwise fall back to RectMask2D so we don't
                // leave a placeholder Image with no sprite assigned in the saved prefab.
                var img = go.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    go.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = img.sprite != null;
                else
                    go.AddComponent<UnityEngine.UI.RectMask2D>();
            }
            // Skip CanvasGroup when a rasterized sprite is the visual — the PNG already
            // has node.opacity baked into its alpha channel, so applying it again here
            // would multiply opacity twice (e.g. 0.4 × 0.4 = 0.16, almost invisible).
            bool spriteIsRasterized = ctx.NodeSprites.ContainsKey(node.Id);
            if (node.Opacity < 1f && !spriteIsRasterized)
                go.AddComponent<CanvasGroup>().alpha = node.Opacity;
        }

        // ─── Component prefabs ──────────────────────────

        private void GenerateComponentPrefabs(List<FigmaNode> frames, ImportContext ctx, ImportProfile profile)
        {
            // Auto-extraction pre-pass. Walks the entire imported subtree once to build
            // a componentId inventory + dependency graph (for nested instances), then
            // ensures each Figma component has a .prefab on disk with its componentId
            // recorded in the manifest's RootComponentId so re-imports re-find it.
            //
            // After this method returns, ctx.GeneratedPrefabs is populated for every
            // componentId the InstanceConverter will encounter — so PrefabInstanceLinker
            // produces a real PrefabInstance instead of inlining the children.
            if (!profile.ExtractFigmaComponentsAsPrefabs)
            {
                _logger.Info("Auto component extraction disabled (profile.ExtractFigmaComponentsAsPrefabs = false).");
                return;
            }

            var inventory = ComponentInventoryCollector.Collect(frames, ctx);
            _logger.Info($"Component inventory: {inventory.AllComponentIds.Count} unique componentIds " +
                         $"({inventory.ComponentMasters.Count} with master in tree, " +
                         $"{inventory.AllComponentIds.Count - inventory.ComponentMasters.Count} external/instance-only).");

            ComponentExtractionPass.Run(
                frames, inventory, ctx, profile, _logger,
                convertFunc: n => ConvertNodeToGameObject(n, ctx, profile),
                registerSavedPath: p => SavedPrefabPaths.Add(p));
        }

        private GameObject ConvertNodeToGameObject(FigmaNode node, ImportContext ctx, ImportProfile profile)
        {
            var go = new GameObject(node.Name);
            go.AddComponent<RectTransform>().sizeDelta = SizeCalculator.GetSize(node);
            ApplyFrameProperties(go, node, ctx);
            if (profile.ConvertAutoLayout && node.IsAutoLayout) AutoLayoutMapper.Apply(go, node);
            bool isRasterized = ctx.NodeSprites.ContainsKey(node.Id);
            bool isComposite = ctx.CompositeContainerIds.Contains(node.Id);

            if (isComposite)
            {
                // Composite container: the Image is the CPU-composited decoratives. We
                // must NOT walk decorative children (they're baked into the composite),
                // but we DO need to overlay each TEXT descendant as an editable TMP so
                // the user can change the string at runtime. Walk text-only.
                AddTextOverlays(node, go, ctx, profile);
            }
            else if (node.HasChildren && !isRasterized)
            {
                ConvertChildren(node, go, ctx, profile);
            }
            return go;
        }

        // Creates editable TextMeshProUGUI overlays for TEXT descendants of a composite
        // container. Keep the transparent wrapper hierarchy on the text path instead of
        // flattening TEXT directly under the composite root: INSTANCE descendants often
        // report child bboxes in source-component space, and parent-relative placement is
        // the only stable way to preserve the final Figma position.
        private void AddTextOverlays(FigmaNode container, GameObject containerGo, ImportContext ctx, ImportProfile profile)
        {
            if (container.Children == null) return;
            foreach (var child in container.Children)
                AddTextOverlayNode(child, container, containerGo, ctx, profile);
        }

        private void AddTextOverlayNode(
            FigmaNode node,
            FigmaNode parentNode,
            GameObject parentGo,
            ImportContext ctx,
            ImportProfile profile)
        {
            if (node == null || NodeConverterRegistry.ShouldSkip(node)) return;
            if (!HasTextDescendantInternal(node)) return;

            GameObject go;
            if (node.NodeType == FigmaNodeType.TEXT)
            {
                var textConverter = _registry.GetConverter(node);
                if (textConverter == null) return;
                try { go = textConverter.Convert(node, parentGo, ctx); }
                catch (System.Exception e)
                {
                    ctx.Logger.Error($"Composite text overlay convert failed for '{node.Name}': {e.Message}");
                    return;
                }
            }
            else
            {
                go = new GameObject(node.Name);
                go.AddComponent<RectTransform>();
                go.transform.SetParent(parentGo.transform, false);
                ctx.NodeIdentities[go.transform] = new ImportContext.NodeIdentityRecord(node.Id, node.ComponentId);
            }

            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                ApplyOverlayLayout(rt, go, node, parentNode, profile);

            if (node.NodeType != FigmaNodeType.TEXT)
            {
                if (profile.ConvertAutoLayout && node.IsAutoLayout)
                    AutoLayoutMapper.Apply(go, node);
                if (node.Children != null)
                    foreach (var child in node.Children)
                        AddTextOverlayNode(child, node, go, ctx, profile);
            }
        }

        private static void ApplyOverlayLayout(
            RectTransform rt,
            GameObject go,
            FigmaNode node,
            FigmaNode parentNode,
            ImportProfile profile)
        {
            if (parentNode.IsAutoLayout && !node.IsAbsolutePositioned)
            {
                if (profile.ConvertAutoLayout)
                    AutoLayoutMapper.ApplyChildLayoutProperties(go, node, parentNode);
                return;
            }

            var relPos = SizeCalculator.GetRelativePosition(node, parentNode);
            var size = SizeCalculator.GetSize(node);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(relPos.x, -(relPos.y + size.y));
            rt.offsetMax = new Vector2(relPos.x + size.x, -relPos.y);
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
                    if (ctx.CompositeContainerIds.Contains(kv.Key))
                        continue;

                    // 9-slice border math is in pixels of the EXPORTED sprite, derived from
                    // node dimensions × scale. That equation only holds when the sprite size
                    // equals the bounding box — i.e. AbsoluteBounds export. RenderBounds
                    // exports are slightly larger (stroke / shadow padding) and the borders
                    // would land in the wrong place, breaking the slice. Skip 9-slice for
                    // those nodes — they get drawn as Image.Type.Simple from the FullRect
                    // sprite, which is fine for a stroked rectangle that doesn't tile.
                    if (ctx.NodeRasterBoundsModes.TryGetValue(kv.Key, out var mode)
                        && mode == ImportContext.RasterBoundsMode.RenderBounds)
                        continue;

                    // Atomic visual groups bake their entire subtree into one PNG; if the
                    // group contains multiple inner shapes (Rectangle 1465 + 1466 + a vector
                    // accent + ...), 9-slicing the bake would stretch every interior pixel
                    // proportionally and warp the inner layout. The outer corner radius is
                    // sliceable in theory but separating bg curve from baked interior here
                    // would require re-rendering, which defeats the point. Force
                    // Image.Type.Simple by leaving spriteBorder at zero.
                    if (IsAtomicVisualGroup(node))
                        continue;

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

            // Atomic-visual-group rasterisation: a FRAME / GROUP / INSTANCE whose
            // entire descendant tree is purely decorative (no TEXT nodes, no nested
            // INSTANCEs — only rectangles, vectors, ellipses, etc.) ships as one PNG
            // for that whole subtree.
            bool atomicVisualGroup = ctx.Profile != null
                && ctx.Profile.RasterizeAtomicVisualGroups
                && IsAtomicVisualGroup(node);

            // Composite container: mixed text + visuals (decoratives and/or nested
            // INSTANCEs). We do NOT bake the container as one PNG (that would include
            // text and produce a baked-text artefact behind the TMP overlay). Instead
            // each visual leaf gets rasterised individually, and CompositeBuilder
            // alpha-blends them into one container-sized PNG after download. Each TEXT
            // descendant becomes an editable TMP overlaid on the composite.
            //
            // Force-add every visual leaf to NodesToRasterize here. The recursive walk
            // would catch most decoratives via NeedsRasterization, but it WOULDN'T catch
            // nested INSTANCEs (the container guard inside NeedsRasterization rejects
            // INSTANCEs that have their own children, which is the common case). Without
            // this explicit add, the composite would be missing the INSTANCE layers.
            bool compositeContainer = ctx.Profile != null
                && ctx.Profile.RasterizeAtomicVisualGroups
                && IsCompositeContainer(node);
            if (compositeContainer)
            {
                ctx.CompositeContainerIds.Add(node.Id);
                var leaves = new List<FigmaNode>();
                CollectDecorativeLeaves(node, leaves);
                foreach (var leaf in leaves)
                {
                    if (string.IsNullOrEmpty(leaf.Id)) continue;
                    ctx.NodesToRasterize.Add(leaf.Id);
                    if (!ctx.NodeRasterBoundsModes.ContainsKey(leaf.Id))
                        ctx.NodeRasterBoundsModes[leaf.Id] = ChooseRasterBoundsMode(leaf);
                }
            }

            bool needsRaster = atomicVisualGroup || NodeConverterRegistry.NeedsRasterization(node);
            if (needsRaster)
            {
                ctx.NodesToRasterize.Add(node.Id);
                ctx.NodeRasterBoundsModes[node.Id] = ChooseRasterBoundsMode(node);
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

            // Atomic visual group: the whole subtree is folded into the parent's PNG, so we
            // shouldn't queue its descendants for individual rasterisation. Stop walking
            // here — the Figma export at this node level captures everything inside.
            if (atomicVisualGroup) return;

            if (node.Children != null)
                foreach (var child in node.Children)
                    CollectImageRequirements(child, ctx);
        }

        private static bool IsEmptyGroup(FigmaNode n) =>
            n.NodeType == FigmaNodeType.GROUP &&
            !n.HasVisibleFills &&
            (n.Effects == null || n.Effects.Count == 0) &&
            !NodeConverterRegistry.IsMaskContainer(n); // preserve mask containers

        // An "atomic visual group" is a container whose entire descendant tree is purely
        // decorative — only RECTANGLE / VECTOR / ELLIPSE / STAR / LINE / REGULAR_POLYGON /
        // BOOLEAN_OPERATION primitives. No TEXT (we want text editable), no nested INSTANCE
        // (we want its prefab linkage preserved). Such groups ship as one PNG of the whole
        // container; the descendant tree isn't walked at all.
        private static bool IsAtomicVisualGroup(FigmaNode node)
        {
            if (node == null || !node.HasChildren) return false;
            var t = node.NodeType;
            if (t != FigmaNodeType.FRAME && t != FigmaNodeType.GROUP && t != FigmaNodeType.INSTANCE)
                return false;
            foreach (var child in node.Children)
                if (HasTextOrInstanceDescendant(child))
                    return false;
            return true;
        }

        private static bool HasTextOrInstanceDescendant(FigmaNode node)
        {
            if (node == null) return false;
            var t = node.NodeType;
            if (t == FigmaNodeType.TEXT) return true;
            if (t == FigmaNodeType.INSTANCE) return true;
            if (node.Children != null)
                foreach (var child in node.Children)
                    if (HasTextOrInstanceDescendant(child)) return true;
            return false;
        }

        // A "composite container" is the mixed case: a FRAME / GROUP / INSTANCE that holds
        // BOTH text descendants AND visual descendants (decorative primitives OR nested
        // INSTANCEs). We can't bake the whole thing as one PNG because Figma's render
        // would include the text and the user explicitly forbids baked-text bleeding
        // through any TMP overlay.
        //
        // Instead the pipeline:
        //   1. Lets each visual leaf get rasterised individually — decorative primitives
        //      via NeedsRasterization, nested INSTANCEs via Figma's render of the
        //      INSTANCE node (we already collect them).
        //   2. After the downloads land, CompositeBuilder alpha-blends the visual
        //      leaves into one container-sized PNG at their relative positions — text
        //      not included.
        //   3. ConvertNodeToGameObject for the container places that single Image, then
        //      walks only the TEXT descendants and creates them as editable TMP children.
        //
        // Trade-off for nested INSTANCE descendants: they get baked into the composite
        // and lose their PrefabInstance linkage. For external-library instances (which
        // the auto-extract path skips anyway) this loses nothing — they were going to
        // inline as raw pixels regardless. For internal instances, the user can disable
        // RasterizeAtomicVisualGroups to recover separate prefab references, accepting
        // the compositing drift in return.
        //
        // The visible result: pixel-accurate visual composite from Figma, text editable,
        // zero risk of baked-text artefact behind the TMP.
        private static bool IsCompositeContainer(FigmaNode node)
        {
            if (node == null || !node.HasChildren) return false;
            var t = node.NodeType;
            if (t != FigmaNodeType.FRAME && t != FigmaNodeType.GROUP && t != FigmaNodeType.INSTANCE)
                return false;
            bool hasText = false;
            bool hasVisual = false;
            foreach (var child in node.Children)
            {
                if (HasTextDescendantInternal(child)) hasText = true;
                if (HasVisualDescendantInternal(child)) hasVisual = true;
                if (hasText && hasVisual) break;
            }
            return hasText && hasVisual;
        }

        private static bool HasTextDescendantInternal(FigmaNode node)
        {
            if (node == null) return false;
            if (node.NodeType == FigmaNodeType.TEXT) return true;
            if (node.Children != null)
                foreach (var c in node.Children)
                    if (HasTextDescendantInternal(c)) return true;
            return false;
        }

        // A visual descendant is anything that contributes pixels we'd want to bake
        // into the composite — decorative primitives OR nested INSTANCEs. INSTANCEs
        // qualify because Figma will rasterise them as their own PNGs (the standard
        // pipeline already does this) and we can blend those PNGs into the composite
        // at the instance's offset within the container.
        private static bool HasVisualDescendantInternal(FigmaNode node)
        {
            if (node == null) return false;
            var t = node.NodeType;
            if (t == FigmaNodeType.TEXT) return false;
            if (t == FigmaNodeType.INSTANCE) return true;
            if (NodeConverterRegistry.NeedsRasterization(node)) return true;
            if (node.Children != null)
                foreach (var c in node.Children)
                    if (HasVisualDescendantInternal(c)) return true;
            return false;
        }

        // Visual leaves of a composite container — every node that gets its own PNG
        // export and contributes a layer to the CPU composite. Walks depth-first so
        // the caller can process leaves in Figma's document order (= z-order, bottom
        // first).
        //
        // INSTANCE handling has two cases:
        //   • INSTANCE with NO text inside → bake as one leaf. Figma's render of the
        //     instance is exactly what we want — one decorative blob with no editable
        //     text underneath, the instance's prefab linkage traded for fidelity.
        //   • INSTANCE WITH text inside → must NOT bake the whole instance, otherwise
        //     the inner text gets pixel-baked into the composite and the matching
        //     TMP overlay (added later by AddTextOverlays) would draw on top of a
        //     baked text artefact — exactly the doubled-text scenario the user
        //     forbade. Walk into the instance instead so the inner decoratives go
        //     into the composite individually and the inner text becomes its own
        //     editable TMP.
        internal static void CollectDecorativeLeaves(FigmaNode node, System.Collections.Generic.List<FigmaNode> output)
        {
            if (node == null) return;
            var t = node.NodeType;
            if (t == FigmaNodeType.TEXT) return;
            if (t == FigmaNodeType.INSTANCE)
            {
                if (HasTextDescendantInternal(node))
                {
                    if (node.Children != null)
                        foreach (var c in node.Children)
                            CollectDecorativeLeaves(c, output);
                    return;
                }
                if (node.AbsoluteBoundingBox != null) output.Add(node);
                return;
            }
            if (NodeConverterRegistry.NeedsRasterization(node) && node.AbsoluteBoundingBox != null)
            {
                output.Add(node);
                return;
            }
            if (node.Children != null)
                foreach (var c in node.Children)
                    CollectDecorativeLeaves(c, output);
        }

        // Text descendants of a composite container — each becomes a TextMeshProUGUI
        // overlaid on the composite Image at the text's relative position to the container.
        internal static void CollectTextDescendants(FigmaNode node, System.Collections.Generic.List<FigmaNode> output)
        {
            if (node == null) return;
            if (node.NodeType == FigmaNodeType.TEXT)
            {
                output.Add(node);
                return;
            }
            if (node.Children != null)
                foreach (var child in node.Children)
                    CollectTextDescendants(child, output);
        }

        // Decides whether Figma should rasterize this node at its absoluteBoundingBox or
        // at its actual render area. The default — AbsoluteBounds — keeps sprite size
        // matching the RectTransform exactly, which is what we want for the typical node.
        // The escape hatch fires when the node's visible footprint extends past the layout
        // box: an outside / center stroke, or a drop shadow / outer-glow effect that would
        // otherwise get clipped flush to the path.
        private static ImportContext.RasterBoundsMode ChooseRasterBoundsMode(FigmaNode node)
        {
            if (node == null) return ImportContext.RasterBoundsMode.AbsoluteBounds;

            // Stroke that paints outside the path. Figma's default is INSIDE; OUTSIDE and
            // CENTER both bleed past the bounding box and get clipped at use_absolute_bounds=true.
            bool hasOutsideStroke = node.StrokeWeight > 0f
                && node.Strokes != null
                && node.Strokes.Exists(s => s.Visible && s.Opacity > 0f)
                && (node.StrokeAlign == "OUTSIDE" || node.StrokeAlign == "CENTER");

            // Drop shadow / outer glow / layer blur expand the rendered area beyond the path.
            // Inner shadow / inner glow stay inside, so we don't trip on those.
            bool hasOutwardEffect = false;
            if (node.Effects != null)
            {
                foreach (var fx in node.Effects)
                {
                    if (fx == null || !fx.Visible) continue;
                    if (fx.Type == "DROP_SHADOW" || fx.Type == "LAYER_BLUR")
                    { hasOutwardEffect = true; break; }
                }
            }

            return hasOutsideStroke || hasOutwardEffect
                ? ImportContext.RasterBoundsMode.RenderBounds
                : ImportContext.RasterBoundsMode.AbsoluteBounds;
        }

        private static string GetTempImageDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "SoobakFigma2Unity_Images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
