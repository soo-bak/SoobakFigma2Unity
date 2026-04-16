using SoobakFigma2Unity.Editor.Models;
using SoobakFigma2Unity.Editor.Pipeline;
using SoobakFigma2Unity.Editor.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Applies detected UI purpose to GameObjects by adding
    /// the appropriate Unity UI components (Button, InputField, ScrollRect, etc.).
    /// </summary>
    internal static class PurposeApplier
    {
        public static void Apply(
            GameObject go,
            FigmaNode node,
            NodePurposeDetector.DetectedPurpose purpose,
            ImportContext ctx)
        {
            switch (purpose)
            {
                case NodePurposeDetector.DetectedPurpose.Button:
                    ApplyButton(go, node, ctx);
                    break;
                case NodePurposeDetector.DetectedPurpose.InputField:
                    ApplyInputField(go, node, ctx);
                    break;
                case NodePurposeDetector.DetectedPurpose.Toggle:
                    ApplyToggle(go, node, ctx);
                    break;
                case NodePurposeDetector.DetectedPurpose.ScrollView:
                    ApplyScrollView(go, node, ctx);
                    break;
                case NodePurposeDetector.DetectedPurpose.Slider:
                    ApplySlider(go, node, ctx);
                    break;
            }
        }

        private static void ApplyButton(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (go.GetComponent<Button>() != null) return;

            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;

            var image = go.GetComponent<Image>();
            if (image != null)
                button.targetGraphic = image;

            ctx.Logger.Info($"{node.Name}: auto-detected as Button");
        }

        private static void ApplyInputField(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (go.GetComponent<TMP_InputField>() != null) return;

            // Find text children
            TextMeshProUGUI textArea = null;
            TextMeshProUGUI placeholder = null;

            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                var tmp = child.GetComponent<TextMeshProUGUI>();
                if (tmp == null) continue;

                if (placeholder == null && (
                    tmp.color.a < 0.8f ||
                    child.name.ToLower().Contains("placeholder") ||
                    child.name.ToLower().Contains("hint")))
                {
                    placeholder = tmp;
                }
                else
                {
                    textArea = tmp;
                }
            }

            if (textArea == null && placeholder != null)
            {
                textArea = placeholder;
                placeholder = null;
            }

            if (textArea == null) return;

            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textComponent = textArea;
            inputField.textViewport = go.GetComponent<RectTransform>();

            if (placeholder != null)
                inputField.placeholder = placeholder;

            var image = go.GetComponent<Image>();
            if (image != null)
                inputField.targetGraphic = image;

            ctx.Logger.Info($"{node.Name}: auto-detected as InputField");
        }

        private static void ApplyToggle(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (go.GetComponent<Toggle>() != null) return;

            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = false;
            toggle.transition = Selectable.Transition.ColorTint;

            var image = go.GetComponent<Image>();
            if (image != null)
                toggle.targetGraphic = image;

            // Try to find a checkmark graphic
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                var childImg = child.GetComponent<Image>();
                if (childImg != null)
                {
                    toggle.graphic = childImg;
                    break;
                }
            }

            ctx.Logger.Info($"{node.Name}: auto-detected as Toggle");
        }

        private static void ApplyScrollView(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (go.GetComponent<ScrollRect>() != null) return;

            var scrollRect = go.AddComponent<ScrollRect>();

            // The content is a child that holds all the scrollable items
            // Use the first child with auto-layout as content, or create a wrapper
            RectTransform contentRt = null;

            if (go.transform.childCount == 1)
            {
                contentRt = go.transform.GetChild(0).GetComponent<RectTransform>();
            }
            else if (go.transform.childCount > 1)
            {
                // Create a content wrapper
                var content = new GameObject("Content");
                var rt = content.AddComponent<RectTransform>();
                content.transform.SetParent(go.transform, false);

                // Reparent all existing children under content
                var children = new Transform[go.transform.childCount - 1];
                int idx = 0;
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i);
                    if (child.gameObject != content)
                        children[idx++] = child;
                }
                for (int i = 0; i < idx; i++)
                    children[i].SetParent(content.transform, true);

                // Size content to encompass all children
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0, 1);
                contentRt = rt;
            }

            if (contentRt != null)
            {
                scrollRect.content = contentRt;

                // Determine scroll direction from content overflow
                float contentH = 0, contentW = 0;
                for (int i = 0; i < contentRt.childCount; i++)
                {
                    var childRt = contentRt.GetChild(i).GetComponent<RectTransform>();
                    if (childRt != null)
                    {
                        contentH += childRt.sizeDelta.y;
                        contentW = Mathf.Max(contentW, childRt.sizeDelta.x);
                    }
                }

                scrollRect.vertical = contentH > go.GetComponent<RectTransform>().sizeDelta.y;
                scrollRect.horizontal = contentW > go.GetComponent<RectTransform>().sizeDelta.x;
            }

            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 20f;

            // Ensure mask exists
            if (go.GetComponent<Mask>() == null)
            {
                var image = go.GetComponent<Image>();
                if (image == null)
                {
                    image = go.AddComponent<Image>();
                    image.color = UnityEngine.Color.clear;
                }
                go.AddComponent<Mask>().showMaskGraphic = false;
            }

            ctx.Logger.Info($"{node.Name}: auto-detected as ScrollView");
        }

        private static void ApplySlider(GameObject go, FigmaNode node, ImportContext ctx)
        {
            if (go.GetComponent<Slider>() != null) return;

            var slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0.5f;

            var image = go.GetComponent<Image>();
            if (image != null)
                slider.targetGraphic = image;

            ctx.Logger.Info($"{node.Name}: auto-detected as Slider");
        }
    }
}
