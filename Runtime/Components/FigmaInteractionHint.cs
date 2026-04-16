using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoobakFigma2Unity.Runtime
{
    /// <summary>
    /// Stores Figma prototype interaction data as hints for developers.
    /// These hints describe the intended navigation/animation behavior
    /// that the designer specified in Figma's prototype mode.
    ///
    /// Developers can reference this data to implement the actual interactions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FigmaInteractionHint : MonoBehaviour
    {
        [Serializable]
        public struct InteractionData
        {
            [Tooltip("Trigger type: OnClick, OnHover, OnPress, OnDrag, AfterTimeout, MouseEnter, MouseLeave")]
            public string Trigger;

            [Tooltip("Timeout in seconds (for AfterTimeout trigger)")]
            public float TriggerTimeout;

            [Tooltip("Action type: Navigate, Swap, Overlay, ScrollTo, Back, Close, OpenURL")]
            public string ActionType;

            [Tooltip("Navigation target: Figma node ID of the destination")]
            public string DestinationNodeId;

            [Tooltip("Navigation target: name of the destination frame")]
            public string DestinationName;

            [Tooltip("URL to open (for OpenURL action)")]
            public string Url;

            [Tooltip("Transition type: Dissolve, SmartAnimate, MoveIn, MoveOut, Push, SlideIn, SlideOut")]
            public string TransitionType;

            [Tooltip("Transition direction: Left, Right, Top, Bottom")]
            public string TransitionDirection;

            [Tooltip("Transition duration in seconds")]
            public float Duration;

            [Tooltip("Easing type: EaseIn, EaseOut, EaseInOut, Linear, Custom")]
            public string EasingType;

            [Tooltip("Custom cubic bezier control points (x1, y1, x2, y2)")]
            public Vector4 CustomBezier;
        }

        [SerializeField] private List<InteractionData> interactions = new List<InteractionData>();

        public IReadOnlyList<InteractionData> Interactions => interactions;

        public void AddInteraction(InteractionData data)
        {
            interactions.Add(data);
        }

        public void ClearInteractions()
        {
            interactions.Clear();
        }
    }
}
