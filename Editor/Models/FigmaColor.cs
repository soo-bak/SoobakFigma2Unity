using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaColor
    {
        [JsonProperty("r")] public float R;
        [JsonProperty("g")] public float G;
        [JsonProperty("b")] public float B;
        [JsonProperty("a")] public float A = 1f;

        public UnityEngine.Color ToUnityColor()
        {
            return new UnityEngine.Color(R, G, B, A);
        }
    }
}
