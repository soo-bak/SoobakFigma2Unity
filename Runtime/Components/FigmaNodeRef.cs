using UnityEngine;

namespace SoobakFigma2Unity.Runtime
{
    /// <summary>
    /// Stores the Figma node ID on a GameObject for re-import mapping.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FigmaNodeRef : MonoBehaviour
    {
        [SerializeField] private string figmaNodeId;
        [SerializeField] private string figmaComponentId;

        public string FigmaNodeId
        {
            get => figmaNodeId;
            set => figmaNodeId = value;
        }

        public string FigmaComponentId
        {
            get => figmaComponentId;
            set => figmaComponentId = value;
        }
    }
}
