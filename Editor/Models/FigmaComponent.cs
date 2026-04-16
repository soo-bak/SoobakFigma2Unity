using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaComponent
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("componentSetId")] public string ComponentSetId;
    }

    [System.Serializable]
    public sealed class FigmaComponentSet
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
    }
}
