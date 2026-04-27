using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaLayoutConstraint
    {
        [JsonProperty("vertical")] public string Vertical;
        [JsonProperty("horizontal")] public string Horizontal;

        public ConstraintType VerticalType =>
            System.Enum.TryParse<ConstraintType>(Vertical, out var t) ? t : ConstraintType.MIN;

        public ConstraintType HorizontalType =>
            System.Enum.TryParse<ConstraintType>(Horizontal, out var t) ? t : ConstraintType.MIN;
    }
}
