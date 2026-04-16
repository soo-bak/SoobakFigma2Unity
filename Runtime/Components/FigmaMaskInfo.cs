using UnityEngine;

namespace SoobakFigma2Unity.Runtime
{
    /// <summary>
    /// Preserves metadata about Figma mask and boolean operations
    /// on the rasterized Unity GameObject.
    ///
    /// Since masks and boolean ops are rasterized to PNG (no Unity equivalent),
    /// this component stores the original Figma info for reference and debugging.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FigmaMaskInfo : MonoBehaviour
    {
        [Header("Mask Info")]
        [SerializeField] private bool isMask;
        [SerializeField] private string maskType;

        [Header("Boolean Operation")]
        [SerializeField] private bool isBooleanOperation;
        [SerializeField] private string booleanOperation;
        [SerializeField] private string[] childNodeIds;

        [Header("Note")]
        [SerializeField, TextArea] private string note =
            "This node was rasterized from a Figma mask/boolean. Original vector data is not preserved.";

        public bool IsMask { get => isMask; set => isMask = value; }
        public string MaskType { get => maskType; set => maskType = value; }
        public bool IsBooleanOperation { get => isBooleanOperation; set => isBooleanOperation = value; }
        public string BooleanOperation { get => booleanOperation; set => booleanOperation = value; }
        public string[] ChildNodeIds { get => childNodeIds; set => childNodeIds = value; }
    }
}
