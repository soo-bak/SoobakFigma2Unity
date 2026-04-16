using System.Collections.Generic;
using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaNode
    {
        // Identity
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("visible")] public bool Visible = true;

        // Children
        [JsonProperty("children")] public List<FigmaNode> Children;

        // Geometry
        [JsonProperty("absoluteBoundingBox")] public FigmaRectangle AbsoluteBoundingBox;
        [JsonProperty("absoluteRenderBounds")] public FigmaRectangle AbsoluteRenderBounds;
        [JsonProperty("relativeTransform")] public float[][] RelativeTransform;
        [JsonProperty("size")] public FigmaVector Size;

        // Appearance
        [JsonProperty("fills")] public List<FigmaPaint> Fills;
        [JsonProperty("strokes")] public List<FigmaPaint> Strokes;
        [JsonProperty("strokeWeight")] public float StrokeWeight;
        [JsonProperty("strokeAlign")] public string StrokeAlign;
        [JsonProperty("opacity")] public float Opacity = 1f;
        [JsonProperty("blendMode")] public string BlendMode;
        [JsonProperty("effects")] public List<FigmaEffect> Effects;
        [JsonProperty("isMask")] public bool IsMask;
        [JsonProperty("clipsContent")] public bool ClipsContent;

        // Corner radius
        [JsonProperty("cornerRadius")] public float CornerRadius;
        [JsonProperty("rectangleCornerRadii")] public float[] RectangleCornerRadii;

        // Constraints (for non-auto-layout children)
        [JsonProperty("constraints")] public FigmaLayoutConstraint Constraints;

        // Auto-layout (on parent frame)
        [JsonProperty("layoutMode")] public string LayoutMode;
        [JsonProperty("primaryAxisSizingMode")] public string PrimaryAxisSizingMode;
        [JsonProperty("counterAxisSizingMode")] public string CounterAxisSizingMode;
        [JsonProperty("primaryAxisAlignItems")] public string PrimaryAxisAlignItems;
        [JsonProperty("counterAxisAlignItems")] public string CounterAxisAlignItems;
        [JsonProperty("paddingLeft")] public float PaddingLeft;
        [JsonProperty("paddingRight")] public float PaddingRight;
        [JsonProperty("paddingTop")] public float PaddingTop;
        [JsonProperty("paddingBottom")] public float PaddingBottom;
        [JsonProperty("itemSpacing")] public float ItemSpacing;
        [JsonProperty("counterAxisSpacing")] public float CounterAxisSpacing;
        [JsonProperty("layoutWrap")] public string LayoutWrap;

        // Auto-layout child properties
        [JsonProperty("layoutAlign")] public string LayoutAlign;
        [JsonProperty("layoutGrow")] public float LayoutGrow;
        [JsonProperty("layoutSizingHorizontal")] public string LayoutSizingHorizontal;
        [JsonProperty("layoutSizingVertical")] public string LayoutSizingVertical;
        [JsonProperty("layoutPositioning")] public string LayoutPositioning;

        // Text
        [JsonProperty("characters")] public string Characters;
        [JsonProperty("style")] public FigmaTypeStyle Style;

        // Component / Instance
        [JsonProperty("componentId")] public string ComponentId;
        [JsonProperty("componentSetId")] public string ComponentSetId;

        // Prototype interactions
        [JsonProperty("interactions")] public List<FigmaInteraction> Interactions;

        // Background (legacy, some files still use this)
        [JsonProperty("backgroundColor")] public FigmaColor BackgroundColor;

        // Parsed node type
        private FigmaNodeType? _nodeType;
        public FigmaNodeType NodeType
        {
            get
            {
                if (_nodeType == null)
                    _nodeType = System.Enum.TryParse<FigmaNodeType>(Type, out var t) ? t : FigmaNodeType.UNKNOWN;
                return _nodeType.Value;
            }
        }

        // Helper: has visible fills?
        public bool HasVisibleFills =>
            Fills != null && Fills.Exists(f => f.Visible && f.Opacity > 0f);

        // Helper: is auto-layout frame?
        public bool IsAutoLayout =>
            LayoutMode != null && LayoutMode != "NONE";

        // Helper: is this child absolutely positioned within an auto-layout parent?
        public bool IsAbsolutePositioned =>
            LayoutPositioning == "ABSOLUTE";

        // Helper: has children?
        public bool HasChildren =>
            Children != null && Children.Count > 0;

        // Helper: width/height from bounding box
        public float Width => AbsoluteBoundingBox?.Width ?? Size?.X ?? 0f;
        public float Height => AbsoluteBoundingBox?.Height ?? Size?.Y ?? 0f;
    }
}
