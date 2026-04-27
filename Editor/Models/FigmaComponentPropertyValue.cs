using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    /// <summary>
    /// One value supplied by an INSTANCE for a component property.
    ///
    /// Figma component properties are the parameter system that lets a single
    /// COMPONENT take per-INSTANCE inputs (button label text, "Show Icon" toggle,
    /// which icon to swap in, which variant to render, ...). Each INSTANCE node
    /// carries a dictionary of these values; child nodes of the instance reference
    /// them via their own <c>componentPropertyReferences</c>.
    ///
    /// Property types:
    ///   BOOLEAN       — Value is bool (json true/false). Maps to GameObject.SetActive
    ///                   on whichever child references this property as its "visible".
    ///   TEXT          — Value is string. Maps to TextMeshProUGUI.text on the child
    ///                   that references this property as its "characters".
    ///   INSTANCE_SWAP — Value is a componentId (string). The child that references
    ///                   this as its "mainComponent" should be a PrefabInstance of
    ///                   that component. v1 keeps the default; v2 will swap.
    ///   VARIANT       — The INSTANCE picks a specific variant by setting these.
    ///                   Already encoded in the INSTANCE.componentId pointing at
    ///                   the chosen variant — we don't need to re-resolve here.
    /// </summary>
    [System.Serializable]
    public sealed class FigmaComponentPropertyValue
    {
        [JsonProperty("type")]  public string Type;
        [JsonProperty("value")] public object Value;

        public bool   AsBool()   => Value is bool b ? b : (Value is string s && bool.TryParse(s, out var p) && p);
        public string AsString() => Value as string;
    }
}
