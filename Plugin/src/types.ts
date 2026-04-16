/**
 * Export manifest format — matches Unity C# FigmaNode model structure.
 */

export interface SoobakManifest {
  version: string;
  exportedAt: string;
  fileName: string;
  fileKey: string;
  imageScale: number;
  frames: SoobakNode[];
  components: Record<string, SoobakComponent>;
  componentSets: Record<string, SoobakComponentSet>;
  images: Record<string, string>;       // nodeId → relative image path
  imageFills: Record<string, string>;   // imageRef → relative image path
}

export interface SoobakNode {
  id: string;
  name: string;
  type: string;
  visible: boolean;
  children?: SoobakNode[];

  // Geometry
  absoluteBoundingBox?: SoobakRect;
  absoluteRenderBounds?: SoobakRect;
  relativeTransform?: number[][];
  size?: SoobakVector;

  // Appearance
  fills?: SoobakPaint[];
  strokes?: SoobakPaint[];
  strokeWeight?: number;
  strokeAlign?: string;
  opacity?: number;
  blendMode?: string;
  effects?: SoobakEffect[];
  isMask?: boolean;
  clipsContent?: boolean;

  // Corner radius
  cornerRadius?: number;
  rectangleCornerRadii?: number[];

  // Constraints
  constraints?: { vertical: string; horizontal: string };

  // Auto-layout
  layoutMode?: string;
  primaryAxisSizingMode?: string;
  counterAxisSizingMode?: string;
  primaryAxisAlignItems?: string;
  counterAxisAlignItems?: string;
  paddingLeft?: number;
  paddingRight?: number;
  paddingTop?: number;
  paddingBottom?: number;
  itemSpacing?: number;
  counterAxisSpacing?: number;
  layoutWrap?: string;

  // Auto-layout child
  layoutAlign?: string;
  layoutGrow?: number;
  layoutSizingHorizontal?: string;
  layoutSizingVertical?: string;
  layoutPositioning?: string;

  // Text
  characters?: string;
  style?: SoobakTypeStyle;

  // Component / Instance
  componentId?: string;
  componentSetId?: string;

  // Prototype interactions
  interactions?: SoobakInteraction[];
}

export interface SoobakRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface SoobakVector {
  x: number;
  y: number;
}

export interface SoobakColor {
  r: number;
  g: number;
  b: number;
  a: number;
}

export interface SoobakPaint {
  type: string;
  visible?: boolean;
  opacity?: number;
  color?: SoobakColor;
  blendMode?: string;
  gradientHandlePositions?: SoobakVector[];
  gradientStops?: { position: number; color: SoobakColor }[];
  scaleMode?: string;
  imageRef?: string;
  imageTransform?: number[][];
}

export interface SoobakEffect {
  type: string;
  visible?: boolean;
  radius?: number;
  color?: SoobakColor;
  blendMode?: string;
  offset?: SoobakVector;
  spread?: number;
}

export interface SoobakTypeStyle {
  fontFamily?: string;
  fontPostScriptName?: string;
  fontWeight?: number;
  fontSize?: number;
  textAlignHorizontal?: string;
  textAlignVertical?: string;
  letterSpacing?: number;
  lineHeightPx?: number;
  lineHeightPercent?: number;
  lineHeightUnit?: string;
  textAutoResize?: string;
  italic?: boolean;
  textDecoration?: string;
  textCase?: string;
  paragraphSpacing?: number;
  paragraphIndent?: number;
}

export interface SoobakComponent {
  key: string;
  name: string;
  description?: string;
  componentSetId?: string;
}

export interface SoobakComponentSet {
  key: string;
  name: string;
  description?: string;
}

export interface SoobakInteraction {
  trigger?: { type: string; timeout?: number };
  actions?: {
    type: string;
    destinationId?: string;
    url?: string;
    navigation?: string;
    transition?: {
      type: string;
      duration?: number;
      easing?: { type: string; easingFunctionCubicBezier?: { x1: number; y1: number; x2: number; y2: number } };
      direction?: string;
    };
  }[];
}
