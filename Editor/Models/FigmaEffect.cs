using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaEffect
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("visible")] public bool Visible = true;
        [JsonProperty("radius")] public float Radius;
        [JsonProperty("color")] public FigmaColor Color;
        [JsonProperty("blendMode")] public string BlendMode;
        [JsonProperty("offset")] public FigmaVector Offset;
        [JsonProperty("spread")] public float Spread;

        public EffectType EffectType => System.Enum.TryParse<EffectType>(Type, out var t) ? t : EffectType.DROP_SHADOW;
    }
}
