using SoobakFigma2Unity.Editor.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Layout
{
    /// <summary>
    /// Maps Figma Auto Layout to Unity LayoutGroup + LayoutElement + ContentSizeFitter.
    /// Comprehensive handling of layoutSizing (FIXED/FILL/HUG), layoutAlign (STRETCH),
    /// layoutGrow, and child positioning.
    /// </summary>
    internal static class AutoLayoutMapper
    {
        public static void Apply(GameObject go, FigmaNode node)
        {
            if (!node.IsAutoLayout)
                return;

            var isHorizontal = node.LayoutMode == "HORIZONTAL";

            // A LayoutGroup may already exist when `go` is a PrefabInstance whose source
            // prefab was extracted with the same auto-layout configuration (typical: a
            // COMPONENT_SET variant linked into the screen via InstanceConverter — the
            // variant prefab carries its own LayoutGroup). Unity's [DisallowMultipleComponent]
            // makes a second AddComponent<HorizontalLayoutGroup>() return null, then the
            // following padding assignment NREs. Re-use the existing instance instead so
            // padding/spacing/etc. for this node still apply.
            HorizontalOrVerticalLayoutGroup layoutGroup;
            if (isHorizontal)
                layoutGroup = go.GetComponent<HorizontalLayoutGroup>() ?? go.AddComponent<HorizontalLayoutGroup>();
            else
                layoutGroup = go.GetComponent<VerticalLayoutGroup>() ?? go.AddComponent<VerticalLayoutGroup>();

            // Padding
            layoutGroup.padding = new RectOffset(
                Mathf.RoundToInt(node.PaddingLeft),
                Mathf.RoundToInt(node.PaddingRight),
                Mathf.RoundToInt(node.PaddingTop),
                Mathf.RoundToInt(node.PaddingBottom)
            );

            // Spacing
            layoutGroup.spacing = node.ItemSpacing;

            // Child alignment
            layoutGroup.childAlignment = ComputeAlignment(
                node.PrimaryAxisAlignItems,
                node.CounterAxisAlignItems,
                isHorizontal
            );

            // SPACE_BETWEEN: distribute children with space between
            // Approximation via childForceExpand on the primary axis
            bool spaceBetween = node.PrimaryAxisAlignItems == "SPACE_BETWEEN";
            if (isHorizontal)
            {
                layoutGroup.childForceExpandWidth = spaceBetween;
                layoutGroup.childForceExpandHeight = false;
            }
            else
            {
                layoutGroup.childForceExpandWidth = false;
                layoutGroup.childForceExpandHeight = spaceBetween;
            }

            // childControl=true is required for FILL/HUG/STRETCH to work correctly.
            // ApplyChildLayoutProperties sets LayoutElement values BEFORE LayoutGroup
            // first runs by writing them immediately after AddComponent<LayoutElement>.
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;

            // ContentSizeFitter for parent's own sizing (primaryAxisSizingMode/counterAxisSizingMode = AUTO)
            ApplyContentSizeFitter(go, node, isHorizontal);

            // layoutWrap: Unity's LayoutGroup doesn't support wrapping by default
            if (node.LayoutWrap == "WRAP")
            {
                Debug.LogWarning($"[SoobakFigma2Unity] '{node.Name}' uses layoutWrap=WRAP — Unity LayoutGroup doesn't support wrapping. Children will overflow on a single line.");
            }
        }

        /// <summary>
        /// Configure a child of an auto-layout container.
        /// Sets LayoutElement values FIRST (so LayoutGroup gets correct values on its
        /// first layout pass), then handles layoutAlign STRETCH and HUG ContentSizeFitter.
        /// </summary>
        public static void ApplyChildLayoutProperties(GameObject childGo, FigmaNode childNode, FigmaNode parentNode)
        {
            if (!parentNode.IsAutoLayout)
                return;

            var rt = childGo.GetComponent<RectTransform>();
            var childSize = SizeCalculator.GetSize(childNode);
            bool parentIsHorizontal = parentNode.LayoutMode == "HORIZONTAL";

            // 1) Add LayoutElement and IMMEDIATELY set values (before any layout pass).
            //    LayoutGroup with childControl=true uses these to size children.
            var le = childGo.AddComponent<LayoutElement>();

            var hSizing = childNode.LayoutSizingHorizontal ?? "FIXED";
            var vSizing = childNode.LayoutSizingVertical ?? "FIXED";

            // Horizontal sizing
            switch (hSizing)
            {
                case "FIXED":
                    le.preferredWidth = childSize.x;
                    le.minWidth = childSize.x;
                    le.flexibleWidth = -1; // disabled
                    break;
                case "FILL":
                    // Fill remaining horizontal space proportionally to layoutGrow
                    le.flexibleWidth = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    le.preferredWidth = childSize.x; // fallback when no flex space available
                    break;
                case "HUG":
                    // Sized to content via ContentSizeFitter (added below)
                    le.preferredWidth = -1;
                    le.flexibleWidth = -1;
                    break;
            }

            // Vertical sizing
            switch (vSizing)
            {
                case "FIXED":
                    le.preferredHeight = childSize.y;
                    le.minHeight = childSize.y;
                    le.flexibleHeight = -1;
                    break;
                case "FILL":
                    le.flexibleHeight = childNode.LayoutGrow > 0 ? childNode.LayoutGrow : 1f;
                    le.preferredHeight = childSize.y;
                    break;
                case "HUG":
                    le.preferredHeight = -1;
                    le.flexibleHeight = -1;
                    break;
            }

            // 2) layoutAlign STRETCH: child stretches across the cross-axis of parent.
            //    HORIZONTAL parent → STRETCH means fill parent height
            //    VERTICAL parent → STRETCH means fill parent width
            if (childNode.LayoutAlign == "STRETCH")
            {
                if (parentIsHorizontal)
                {
                    le.flexibleHeight = 1f;
                    // Allow LayoutGroup to size beyond preferred
                    le.minHeight = -1;
                }
                else
                {
                    le.flexibleWidth = 1f;
                    le.minWidth = -1;
                }
            }

            // 3) HUG sizing → ContentSizeFitter on the child itself so it sizes to content
            if (hSizing == "HUG" || vSizing == "HUG")
            {
                var fitter = childGo.GetComponent<ContentSizeFitter>()
                    ?? childGo.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = hSizing == "HUG"
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = vSizing == "HUG"
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
            }

            // 4) RT initial state. With childControl=true, LayoutGroup will overwrite
            //    anchors/sizeDelta to its own scheme during layout. We set sane defaults
            //    so the child renders correctly even if layout hasn't run yet.
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = childSize;
        }

        private static void ApplyContentSizeFitter(GameObject go, FigmaNode node, bool isHorizontal)
        {
            var primaryAuto = node.PrimaryAxisSizingMode == "AUTO";
            var counterAuto = node.CounterAxisSizingMode == "AUTO";

            if (!primaryAuto && !counterAuto)
                return;

            var fitter = go.GetComponent<ContentSizeFitter>()
                ?? go.AddComponent<ContentSizeFitter>();

            if (isHorizontal)
            {
                fitter.horizontalFit = primaryAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = counterAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                fitter.verticalFit = primaryAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
                fitter.horizontalFit = counterAuto
                    ? ContentSizeFitter.FitMode.PreferredSize
                    : ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        private static TextAnchor ComputeAlignment(
            string primaryAlign, string counterAlign, bool isHorizontal)
        {
            int row, col;

            if (isHorizontal)
            {
                col = MapAlignToIndex(primaryAlign);
                row = MapAlignToIndex(counterAlign);
            }
            else
            {
                row = MapAlignToIndex(primaryAlign);
                col = MapAlignToIndex(counterAlign);
            }

            return (TextAnchor)(row * 3 + col);
        }

        private static int MapAlignToIndex(string align)
        {
            return align switch
            {
                "MIN" => 0,
                "CENTER" => 1,
                "MAX" => 2,
                "SPACE_BETWEEN" => 0, // handled via childForceExpand
                "BASELINE" => 0,
                _ => 0
            };
        }
    }
}
