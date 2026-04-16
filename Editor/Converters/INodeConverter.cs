using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Converters
{
    internal interface INodeConverter
    {
        bool CanConvert(FigmaNode node);
        GameObject Convert(FigmaNode node, GameObject parent, ImportContext ctx);
    }
}
