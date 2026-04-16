using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaTypeStyle
    {
        [JsonProperty("fontFamily")] public string FontFamily;
        [JsonProperty("fontPostScriptName")] public string FontPostScriptName;
        [JsonProperty("fontWeight")] public int FontWeight = 400;
        [JsonProperty("fontSize")] public float FontSize = 14f;
        [JsonProperty("textAlignHorizontal")] public string TextAlignHorizontal;
        [JsonProperty("textAlignVertical")] public string TextAlignVertical;
        [JsonProperty("letterSpacing")] public float LetterSpacing;
        [JsonProperty("lineHeightPx")] public float LineHeightPx;
        [JsonProperty("lineHeightPercent")] public float LineHeightPercent = 100f;
        [JsonProperty("lineHeightUnit")] public string LineHeightUnit;
        [JsonProperty("textAutoResize")] public string TextAutoResize;
        [JsonProperty("italic")] public bool Italic;
        [JsonProperty("textDecoration")] public string TextDecoration;
        [JsonProperty("textCase")] public string TextCase;
        [JsonProperty("paragraphSpacing")] public float ParagraphSpacing;
        [JsonProperty("paragraphIndent")] public float ParagraphIndent;
    }
}
