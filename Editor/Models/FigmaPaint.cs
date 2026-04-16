using System.Collections.Generic;
using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaColorStop
    {
        [JsonProperty("position")] public float Position;
        [JsonProperty("color")] public FigmaColor Color;
    }

    [System.Serializable]
    public sealed class FigmaPaint
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("visible")] public bool Visible = true;
        [JsonProperty("opacity")] public float Opacity = 1f;
        [JsonProperty("color")] public FigmaColor Color;
        [JsonProperty("blendMode")] public string BlendMode;
        [JsonProperty("gradientHandlePositions")] public List<FigmaVector> GradientHandlePositions;
        [JsonProperty("gradientStops")] public List<FigmaColorStop> GradientStops;
        [JsonProperty("scaleMode")] public string ScaleMode;
        [JsonProperty("imageRef")] public string ImageRef;
        [JsonProperty("imageTransform")] public float[][] ImageTransform;

        public PaintType PaintType => System.Enum.TryParse<PaintType>(Type, out var t) ? t : PaintType.SOLID;
        public bool IsSolid => PaintType == PaintType.SOLID;
        public bool IsImage => PaintType == PaintType.IMAGE;
        public bool IsGradient => Type != null && Type.StartsWith("GRADIENT_");
    }
}
