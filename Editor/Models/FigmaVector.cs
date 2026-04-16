using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaVector
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
    }

    [System.Serializable]
    public sealed class FigmaRectangle
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("width")] public float Width;
        [JsonProperty("height")] public float Height;
    }
}
