import { SoobakManifest, SoobakNode, SoobakPaint, SoobakEffect, SoobakComponent, SoobakComponentSet } from './types';

// Show UI
figma.showUI(__html__, { width: 400, height: 500, themeColors: true });

// ─── Message handlers ──────────────────────────────────
figma.ui.onmessage = async (msg: { type: string; scale?: number }) => {
  if (msg.type === 'export') {
    const scale = msg.scale || 2;
    await runExport(scale);
  }
  if (msg.type === 'get-pages') {
    sendPageTree();
  }
};

// ─── Send page/frame tree to UI ────────────────────────
function sendPageTree() {
  const pages = figma.root.children.map(page => ({
    id: page.id,
    name: page.name,
    frames: page.children
      .filter(n => n.type === 'FRAME' || n.type === 'COMPONENT' || n.type === 'COMPONENT_SET' || n.type === 'SECTION')
      .map(f => ({ id: f.id, name: f.name, type: f.type }))
  }));
  figma.ui.postMessage({ type: 'page-tree', pages });
}

// ─── Main export logic ─────────────────────────────────
async function runExport(scale: number) {
  const selection = figma.currentPage.selection;
  if (selection.length === 0) {
    figma.notify('Please select one or more frames to export.', { error: true });
    return;
  }

  figma.ui.postMessage({ type: 'status', message: 'Collecting node data...' });

  const frames: SoobakNode[] = [];
  const images: Record<string, string> = {};
  const imageFills: Record<string, string> = {};
  const components: Record<string, SoobakComponent> = {};
  const componentSets: Record<string, SoobakComponentSet> = {};
  const imageBuffers: { name: string; data: Uint8Array }[] = [];

  // Collect components from the file
  collectFileComponents(components, componentSets);

  for (const node of selection) {
    figma.ui.postMessage({ type: 'status', message: `Processing: ${node.name}...` });

    // Serialize node tree
    const serialized = serializeNode(node as SceneNode);
    if (serialized) frames.push(serialized);

    // Collect and export images
    await collectAndExportImages(node as SceneNode, scale, images, imageFills, imageBuffers);
  }

  figma.ui.postMessage({ type: 'status', message: 'Generating export file...' });

  const manifest: SoobakManifest = {
    version: '1.0',
    exportedAt: new Date().toISOString(),
    fileName: figma.root.name,
    fileKey: figma.fileKey ?? '',
    imageScale: scale,
    frames,
    components,
    componentSets,
    images,
    imageFills,
  };

  // Send to UI for download
  figma.ui.postMessage({
    type: 'export-ready',
    manifest: JSON.stringify(manifest, null, 2),
    images: imageBuffers.map(b => ({ name: b.name, data: Array.from(b.data) })),
    fileName: figma.root.name,
  });
}

// ─── Node serialization ────────────────────────────────
function serializeNode(node: SceneNode): SoobakNode | null {
  if (!('visible' in node)) return null;

  const result: SoobakNode = {
    id: node.id,
    name: node.name,
    type: node.type,
    visible: node.visible,
  };

  // Geometry
  if ('absoluteBoundingBox' in node && node.absoluteBoundingBox) {
    result.absoluteBoundingBox = { ...node.absoluteBoundingBox };
  }
  if ('absoluteRenderBounds' in node && node.absoluteRenderBounds) {
    result.absoluteRenderBounds = { ...node.absoluteRenderBounds };
  }
  if ('relativeTransform' in node) {
    result.relativeTransform = node.relativeTransform.map(r => [...r]);
  }
  if ('width' in node && 'height' in node) {
    result.size = { x: node.width, y: node.height };
  }

  // Appearance
  if ('fills' in node && node.fills !== figma.mixed) {
    result.fills = (node.fills as ReadonlyArray<Paint>).map(serializePaint);
  }
  if ('strokes' in node) {
    result.strokes = (node.strokes as ReadonlyArray<Paint>).map(serializePaint);
  }
  if ('strokeWeight' in node && typeof node.strokeWeight === 'number') {
    result.strokeWeight = node.strokeWeight;
  }
  if ('strokeAlign' in node) {
    result.strokeAlign = node.strokeAlign;
  }
  if ('opacity' in node) {
    result.opacity = node.opacity;
  }
  if ('blendMode' in node) {
    result.blendMode = node.blendMode;
  }
  if ('effects' in node) {
    result.effects = (node.effects as ReadonlyArray<Effect>).map(serializeEffect);
  }
  if ('isMask' in node) {
    result.isMask = node.isMask;
  }
  if ('clipsContent' in node) {
    result.clipsContent = node.clipsContent;
  }

  // Corner radius
  if ('cornerRadius' in node && typeof node.cornerRadius === 'number') {
    result.cornerRadius = node.cornerRadius;
  }
  if ('rectangleCornerRadii' in node) {
    result.rectangleCornerRadii = [...(node as any).rectangleCornerRadii];
  }

  // Constraints
  if ('constraints' in node) {
    result.constraints = {
      vertical: node.constraints.vertical,
      horizontal: node.constraints.horizontal,
    };
  }

  // Auto-layout
  if ('layoutMode' in node) {
    result.layoutMode = node.layoutMode;
    if ('primaryAxisSizingMode' in node) result.primaryAxisSizingMode = node.primaryAxisSizingMode;
    if ('counterAxisSizingMode' in node) result.counterAxisSizingMode = node.counterAxisSizingMode;
    if ('primaryAxisAlignItems' in node) result.primaryAxisAlignItems = node.primaryAxisAlignItems;
    if ('counterAxisAlignItems' in node) result.counterAxisAlignItems = node.counterAxisAlignItems;
    if ('paddingLeft' in node) result.paddingLeft = node.paddingLeft;
    if ('paddingRight' in node) result.paddingRight = node.paddingRight;
    if ('paddingTop' in node) result.paddingTop = node.paddingTop;
    if ('paddingBottom' in node) result.paddingBottom = node.paddingBottom;
    if ('itemSpacing' in node) result.itemSpacing = node.itemSpacing;
    if ('counterAxisSpacing' in node) result.counterAxisSpacing = (node as any).counterAxisSpacing;
    if ('layoutWrap' in node) result.layoutWrap = (node as any).layoutWrap;
  }

  // Auto-layout child
  if ('layoutAlign' in node) result.layoutAlign = node.layoutAlign;
  if ('layoutGrow' in node) result.layoutGrow = node.layoutGrow;
  if ('layoutSizingHorizontal' in node) result.layoutSizingHorizontal = (node as any).layoutSizingHorizontal;
  if ('layoutSizingVertical' in node) result.layoutSizingVertical = (node as any).layoutSizingVertical;
  if ('layoutPositioning' in node) result.layoutPositioning = (node as any).layoutPositioning;

  // Text
  if (node.type === 'TEXT') {
    const textNode = node as TextNode;
    result.characters = textNode.characters;
    result.style = {
      fontFamily: textNode.fontName !== figma.mixed ? (textNode.fontName as FontName).family : undefined,
      fontWeight: textNode.fontWeight !== figma.mixed ? textNode.fontWeight as number : 400,
      fontSize: textNode.fontSize !== figma.mixed ? textNode.fontSize as number : 14,
      textAlignHorizontal: textNode.textAlignHorizontal,
      textAlignVertical: textNode.textAlignVertical,
      letterSpacing: textNode.letterSpacing !== figma.mixed
        ? (textNode.letterSpacing as LetterSpacing).value : 0,
      lineHeightPx: textNode.lineHeight !== figma.mixed && (textNode.lineHeight as LineHeight).unit === 'PIXELS'
        ? (textNode.lineHeight as LineHeight).value : 0,
      italic: textNode.fontName !== figma.mixed ? (textNode.fontName as FontName).style.toLowerCase().includes('italic') : false,
      paragraphSpacing: textNode.paragraphSpacing,
      paragraphIndent: textNode.paragraphIndent,
    };
  }

  // Component / Instance
  if (node.type === 'INSTANCE') {
    const inst = node as InstanceNode;
    if (inst.mainComponent) {
      result.componentId = inst.mainComponent.id;
    }
  }
  if (node.type === 'COMPONENT') {
    const comp = node as ComponentNode;
    if (comp.parent && comp.parent.type === 'COMPONENT_SET') {
      result.componentSetId = comp.parent.id;
    }
  }

  // Prototype interactions
  if ('reactions' in node) {
    const reactions = (node as any).reactions;
    if (reactions && reactions.length > 0) {
      result.interactions = reactions.map((r: any) => ({
        trigger: r.trigger ? { type: r.trigger.type, timeout: r.trigger.timeout } : undefined,
        actions: r.actions?.map((a: any) => ({
          type: a.type,
          destinationId: a.destinationId,
          url: a.url,
          navigation: a.navigation,
          transition: a.transition ? {
            type: a.transition.type,
            duration: a.transition.duration,
            direction: a.transition.direction,
            easing: a.transition.easing ? {
              type: a.transition.easing.type,
              easingFunctionCubicBezier: a.transition.easing.easingFunction,
            } : undefined,
          } : undefined,
        })),
      }));
    }
  }

  // Recurse children
  if ('children' in node) {
    const parent = node as ChildrenMixin;
    result.children = [];
    for (const child of parent.children) {
      const serializedChild = serializeNode(child as SceneNode);
      if (serializedChild) result.children.push(serializedChild);
    }
  }

  return result;
}

// ─── Paint serialization ───────────────────────────────
function serializePaint(paint: Paint): SoobakPaint {
  const result: SoobakPaint = {
    type: paint.type,
    visible: paint.visible ?? true,
    opacity: paint.opacity ?? 1,
    blendMode: paint.blendMode,
  };

  if (paint.type === 'SOLID') {
    result.color = { r: paint.color.r, g: paint.color.g, b: paint.color.b, a: paint.opacity ?? 1 };
  }

  if ('gradientHandlePositions' in paint) {
    result.gradientHandlePositions = (paint as any).gradientHandlePositions;
    result.gradientStops = (paint as any).gradientStops;
  }

  if (paint.type === 'IMAGE') {
    result.scaleMode = (paint as ImagePaint).scaleMode;
    result.imageRef = (paint as ImagePaint).imageHash ?? undefined;
    result.imageTransform = (paint as ImagePaint).imageTransform
      ? (paint as ImagePaint).imageTransform!.map(r => [...r])
      : undefined;
  }

  return result;
}

// ─── Effect serialization ──────────────────────────────
function serializeEffect(effect: Effect): SoobakEffect {
  const result: SoobakEffect = {
    type: effect.type,
    visible: effect.visible ?? true,
  };
  if ('radius' in effect) result.radius = effect.radius;
  if ('color' in effect) {
    const c = (effect as any).color;
    result.color = { r: c.r, g: c.g, b: c.b, a: c.a };
  }
  if ('offset' in effect) {
    const o = (effect as any).offset;
    result.offset = { x: o.x, y: o.y };
  }
  if ('spread' in effect) result.spread = (effect as any).spread;
  if ('blendMode' in effect) result.blendMode = (effect as any).blendMode;
  return result;
}

// ─── Image collection + export ─────────────────────────
async function collectAndExportImages(
  node: SceneNode,
  scale: number,
  images: Record<string, string>,
  imageFills: Record<string, string>,
  buffers: { name: string; data: Uint8Array }[]
) {
  // Check if this node needs rasterization
  if (needsRasterization(node)) {
    try {
      const data = await (node as ExportMixin).exportAsync({
        format: 'PNG',
        constraint: { type: 'SCALE', value: scale },
      });
      const safeName = node.id.replace(/:/g, '_');
      const fileName = `images/${safeName}.png`;
      images[node.id] = fileName;
      buffers.push({ name: fileName, data });
    } catch (e) {
      console.error(`Failed to export ${node.name}:`, e);
    }
  }

  // Check for image fills
  if ('fills' in node && node.fills !== figma.mixed) {
    for (const fill of node.fills as ReadonlyArray<Paint>) {
      if (fill.type === 'IMAGE' && (fill as ImagePaint).imageHash) {
        const imageHash = (fill as ImagePaint).imageHash!;
        if (!imageFills[imageHash]) {
          try {
            const image = figma.getImageByHash(imageHash);
            if (image) {
              const data = await image.getBytesAsync();
              const safeName = imageHash.replace(/[^a-zA-Z0-9]/g, '_');
              const fileName = `images/fill_${safeName}.png`;
              imageFills[imageHash] = fileName;
              buffers.push({ name: fileName, data });
            }
          } catch (e) {
            console.error(`Failed to get image fill ${imageHash}:`, e);
          }
        }
      }
    }
  }

  // Recurse children
  if ('children' in node) {
    for (const child of (node as ChildrenMixin).children) {
      await collectAndExportImages(child as SceneNode, scale, images, imageFills, buffers);
    }
  }
}

// ─── Rasterization check ───────────────────────────────
function needsRasterization(node: SceneNode): boolean {
  const vectorTypes = ['VECTOR', 'ELLIPSE', 'STAR', 'LINE', 'REGULAR_POLYGON', 'BOOLEAN_OPERATION'];
  if (vectorTypes.includes(node.type)) return true;

  if ('fills' in node && node.fills !== figma.mixed) {
    for (const fill of node.fills as ReadonlyArray<Paint>) {
      if (!fill.visible) continue;
      // Gradient fills
      if (fill.type.startsWith('GRADIENT_')) return true;
      // Cropped image fills
      if (fill.type === 'IMAGE' && (fill as ImagePaint).scaleMode === 'FILL') return true;
    }
  }

  // Nodes with visible effects
  if ('effects' in node) {
    for (const effect of (node as any).effects) {
      if (effect.visible) return true;
    }
  }

  return false;
}

// ─── Collect file components ───────────────────────────
function collectFileComponents(
  components: Record<string, SoobakComponent>,
  componentSets: Record<string, SoobakComponentSet>
) {
  function walk(node: BaseNode) {
    if (node.type === 'COMPONENT') {
      const comp = node as ComponentNode;
      components[comp.id] = {
        key: comp.key,
        name: comp.name,
        description: comp.description,
        componentSetId: comp.parent?.type === 'COMPONENT_SET' ? comp.parent.id : undefined,
      };
    }
    if (node.type === 'COMPONENT_SET') {
      const cs = node as ComponentSetNode;
      componentSets[cs.id] = {
        key: cs.key,
        name: cs.name,
        description: cs.description,
      };
    }
    if ('children' in node) {
      for (const child of (node as ChildrenMixin).children) {
        walk(child);
      }
    }
  }
  walk(figma.root);
}
