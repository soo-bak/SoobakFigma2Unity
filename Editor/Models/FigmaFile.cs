using System.Collections.Generic;
using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaFileResponse
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("lastModified")] public string LastModified;
        [JsonProperty("version")] public string Version;
        [JsonProperty("document")] public FigmaNode Document;
        [JsonProperty("components")] public Dictionary<string, FigmaComponent> Components;
        [JsonProperty("componentSets")] public Dictionary<string, FigmaComponentSet> ComponentSets;
    }

    [System.Serializable]
    public sealed class FigmaNodesResponse
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("lastModified")] public string LastModified;
        [JsonProperty("nodes")] public Dictionary<string, FigmaNodeWrapper> Nodes;
        [JsonProperty("components")] public Dictionary<string, FigmaComponent> Components;
        [JsonProperty("componentSets")] public Dictionary<string, FigmaComponentSet> ComponentSets;
    }

    [System.Serializable]
    public sealed class FigmaNodeWrapper
    {
        [JsonProperty("document")] public FigmaNode Document;
        [JsonProperty("components")] public Dictionary<string, FigmaComponent> Components;
    }

    [System.Serializable]
    public sealed class FigmaImageResponse
    {
        [JsonProperty("err")] public string Error;
        [JsonProperty("images")] public Dictionary<string, string> Images;
    }

    [System.Serializable]
    public sealed class FigmaImageFillsResponse
    {
        [JsonProperty("error")] public bool HasError;
        [JsonProperty("status")] public int Status;
        [JsonProperty("meta")] public FigmaImageFillsMeta Meta;
    }

    [System.Serializable]
    public sealed class FigmaImageFillsMeta
    {
        [JsonProperty("images")] public Dictionary<string, string> Images;
    }
}
