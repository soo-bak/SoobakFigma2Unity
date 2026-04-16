using System.Collections.Generic;
using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    /// <summary>
    /// Figma prototype interaction data from the REST API.
    /// These are returned on nodes that have prototype interactions defined.
    /// </summary>
    [System.Serializable]
    public sealed class FigmaInteraction
    {
        [JsonProperty("trigger")] public FigmaTrigger Trigger;
        [JsonProperty("actions")] public List<FigmaAction> Actions;
    }

    [System.Serializable]
    public sealed class FigmaTrigger
    {
        [JsonProperty("type")] public string Type; // "ON_CLICK", "ON_HOVER", "ON_PRESS", "ON_DRAG", "AFTER_TIMEOUT", "MOUSE_ENTER", "MOUSE_LEAVE"
        [JsonProperty("timeout")] public float Timeout; // for AFTER_TIMEOUT
    }

    [System.Serializable]
    public sealed class FigmaAction
    {
        [JsonProperty("type")] public string Type; // "NODE", "BACK", "CLOSE", "URL", "SCROLL_TO"
        [JsonProperty("destinationId")] public string DestinationId;
        [JsonProperty("url")] public string Url;
        [JsonProperty("navigation")] public string Navigation; // "NAVIGATE", "SWAP", "OVERLAY", "SCROLL_TO"
        [JsonProperty("transition")] public FigmaTransition Transition;
        [JsonProperty("preserveScrollPosition")] public bool PreserveScrollPosition;
    }

    [System.Serializable]
    public sealed class FigmaTransition
    {
        [JsonProperty("type")] public string Type; // "DISSOLVE", "SMART_ANIMATE", "MOVE_IN", "MOVE_OUT", "PUSH", "SLIDE_IN", "SLIDE_OUT"
        [JsonProperty("duration")] public float Duration; // seconds
        [JsonProperty("easing")] public FigmaEasing Easing;
        [JsonProperty("direction")] public string Direction; // "LEFT", "RIGHT", "TOP", "BOTTOM"
    }

    [System.Serializable]
    public sealed class FigmaEasing
    {
        [JsonProperty("type")] public string Type; // "EASE_IN", "EASE_OUT", "EASE_IN_AND_OUT", "LINEAR", "EASE_IN_BACK", "EASE_OUT_BACK", "EASE_IN_AND_OUT_BACK", "CUSTOM_CUBIC_BEZIER"
        [JsonProperty("easingFunctionCubicBezier")] public FigmaCubicBezier CubicBezier;
    }

    [System.Serializable]
    public sealed class FigmaCubicBezier
    {
        [JsonProperty("x1")] public float X1;
        [JsonProperty("y1")] public float Y1;
        [JsonProperty("x2")] public float X2;
        [JsonProperty("y2")] public float Y2;
    }
}
