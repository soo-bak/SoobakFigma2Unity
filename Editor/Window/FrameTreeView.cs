using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Models;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Window
{
    /// <summary>
    /// Data structure for the frame selection tree displayed in the Editor Window.
    /// </summary>
    internal sealed class FrameTreeItem
    {
        public string NodeId;
        public string Name;
        public string Type;
        public bool Selected;
        public bool Expanded = true;
        public List<FrameTreeItem> Children = new List<FrameTreeItem>();
        public int Depth;
    }

    internal sealed class FrameTreeView
    {
        private List<FrameTreeItem> _roots = new List<FrameTreeItem>();
        private Vector2 _scrollPosition;

        public IReadOnlyList<FrameTreeItem> Roots => _roots;

        public void Clear()
        {
            _roots.Clear();
        }

        /// <summary>
        /// Build tree from Figma document node (pages and their children).
        /// </summary>
        public void BuildFromDocument(FigmaNode document)
        {
            _roots.Clear();

            if (document?.Children == null)
                return;

            // Document children are pages (CANVAS)
            foreach (var page in document.Children)
            {
                if (page.NodeType != FigmaNodeType.CANVAS)
                    continue;

                var pageItem = new FrameTreeItem
                {
                    NodeId = page.Id,
                    Name = page.Name,
                    Type = "PAGE",
                    Selected = false,
                    Depth = 0
                };

                if (page.Children != null)
                {
                    foreach (var frame in page.Children)
                    {
                        // Show top-level frames, components, component sets
                        var t = frame.NodeType;
                        if (t == FigmaNodeType.FRAME || t == FigmaNodeType.COMPONENT ||
                            t == FigmaNodeType.COMPONENT_SET || t == FigmaNodeType.SECTION)
                        {
                            pageItem.Children.Add(new FrameTreeItem
                            {
                                NodeId = frame.Id,
                                Name = frame.Name,
                                Type = frame.Type,
                                Selected = false,
                                Depth = 1
                            });
                        }
                    }
                }

                _roots.Add(pageItem);
            }
        }

        /// <summary>
        /// Draw the tree with checkboxes in the Editor Window.
        /// </summary>
        public void OnGUI(float height)
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition,
                GUILayout.Height(height));

            foreach (var root in _roots)
                DrawItem(root);

            GUILayout.EndScrollView();
        }

        private void DrawItem(FrameTreeItem item)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(item.Depth * 20f);

            if (item.Children.Count > 0)
            {
                item.Expanded = UnityEditor.EditorGUILayout.Foldout(item.Expanded, "", true);
            }
            else
            {
                GUILayout.Space(16f);
            }

            bool wasSelected = item.Selected;
            item.Selected = GUILayout.Toggle(item.Selected, "", GUILayout.Width(16));

            // If page selection changed, propagate to children
            if (item.Selected != wasSelected && item.Children.Count > 0)
            {
                foreach (var child in item.Children)
                    child.Selected = item.Selected;
            }

            var label = item.Type == "PAGE"
                ? $"Page: {item.Name}"
                : $"{item.Name} [{item.Type}]";
            GUILayout.Label(label);

            GUILayout.EndHorizontal();

            if (item.Expanded)
            {
                foreach (var child in item.Children)
                    DrawItem(child);
            }
        }

        /// <summary>
        /// Get the Figma node IDs of all selected frames (not pages).
        /// </summary>
        public List<string> GetSelectedNodeIds()
        {
            var result = new List<string>();
            foreach (var root in _roots)
            {
                foreach (var child in root.Children)
                {
                    if (child.Selected)
                        result.Add(child.NodeId);
                }
            }
            return result;
        }
    }
}
