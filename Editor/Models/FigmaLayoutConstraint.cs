using Newtonsoft.Json;

namespace SoobakFigma2Unity.Editor.Models
{
    [System.Serializable]
    public sealed class FigmaLayoutConstraint
    {
        [JsonProperty("vertical")] public string Vertical;
        [JsonProperty("horizontal")] public string Horizontal;

        public ConstraintType VerticalType => ParseVertical(Vertical);

        public ConstraintType HorizontalType => ParseHorizontal(Horizontal);

        private static ConstraintType ParseVertical(string value)
        {
            switch (value)
            {
                case "TOP":
                case "MIN":
                    return ConstraintType.MIN;
                case "BOTTOM":
                case "MAX":
                    return ConstraintType.MAX;
                case "TOP_BOTTOM":
                case "STRETCH":
                    return ConstraintType.STRETCH;
                case "CENTER":
                    return ConstraintType.CENTER;
                case "SCALE":
                    return ConstraintType.SCALE;
                default:
                    return ConstraintType.MIN;
            }
        }

        private static ConstraintType ParseHorizontal(string value)
        {
            switch (value)
            {
                case "LEFT":
                case "MIN":
                    return ConstraintType.MIN;
                case "RIGHT":
                case "MAX":
                    return ConstraintType.MAX;
                case "LEFT_RIGHT":
                case "STRETCH":
                    return ConstraintType.STRETCH;
                case "CENTER":
                    return ConstraintType.CENTER;
                case "SCALE":
                    return ConstraintType.SCALE;
                default:
                    return ConstraintType.MIN;
            }
        }
    }
}
