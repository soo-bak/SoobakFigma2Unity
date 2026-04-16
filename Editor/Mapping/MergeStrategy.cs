using System.Collections.Generic;
using SoobakFigma2Unity.Editor.Util;
using SoobakFigma2Unity.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Handles re-import merge: updates Figma-managed properties
    /// while preserving user-added components and modifications.
    ///
    /// Strategy:
    /// - Match existing GameObjects by FigmaNodeRef.FigmaNodeId
    /// - Update only "Figma-managed" properties (position, size, color, image, text)
    /// - Preserve any components not managed by Figma (scripts, animators, event triggers, etc.)
    /// - Add new nodes, remove nodes that no longer exist in Figma (with option to keep)
    /// </summary>
    internal sealed class MergeStrategy
    {
        private readonly ImportLogger _logger;

        // Components managed by Figma (will be updated on re-import)
        private static readonly HashSet<System.Type> FigmaManagedTypes = new HashSet<System.Type>
        {
            typeof(RectTransform),
            typeof(Image),
            typeof(TMPro.TextMeshProUGUI),
            typeof(HorizontalLayoutGroup),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(LayoutElement),
            typeof(Mask),
            typeof(CanvasGroup),
            typeof(FigmaNodeRef)
        };

        public MergeStrategy(ImportLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Merge a newly generated GameObject tree into an existing prefab.
        /// Returns the path of the updated prefab.
        /// </summary>
        public string MergeIntoPrefab(GameObject newRoot, string existingPrefabPath)
        {
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(existingPrefabPath);
            if (existingPrefab == null)
            {
                _logger.Warn($"Existing prefab not found at {existingPrefabPath}, creating new.");
                return null;
            }

            // Open prefab for editing
            var prefabContents = PrefabUtility.LoadPrefabContents(existingPrefabPath);

            try
            {
                // Build index of existing nodes by FigmaNodeId
                var existingIndex = BuildNodeIndex(prefabContents);

                // Merge recursively
                MergeNode(newRoot, prefabContents, existingIndex);

                // Save
                PrefabUtility.SaveAsPrefabAsset(prefabContents, existingPrefabPath);
                _logger.Success($"Merged into existing prefab: {existingPrefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            return existingPrefabPath;
        }

        /// <summary>
        /// Check if a prefab already exists for the given Figma node.
        /// </summary>
        public static string FindExistingPrefab(string figmaNodeId, string searchDir)
        {
            if (!System.IO.Directory.Exists(searchDir))
                return null;

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { searchDir });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var nodeRef = prefab.GetComponent<FigmaNodeRef>();
                if (nodeRef != null && nodeRef.FigmaNodeId == figmaNodeId)
                    return path;
            }

            return null;
        }

        private Dictionary<string, GameObject> BuildNodeIndex(GameObject root)
        {
            var index = new Dictionary<string, GameObject>();
            IndexRecursive(root, index);
            return index;
        }

        private void IndexRecursive(GameObject go, Dictionary<string, GameObject> index)
        {
            var nodeRef = go.GetComponent<FigmaNodeRef>();
            if (nodeRef != null && !string.IsNullOrEmpty(nodeRef.FigmaNodeId))
                index[nodeRef.FigmaNodeId] = go;

            for (int i = 0; i < go.transform.childCount; i++)
                IndexRecursive(go.transform.GetChild(i).gameObject, index);
        }

        private void MergeNode(GameObject newNode, GameObject existingNode, Dictionary<string, GameObject> existingIndex)
        {
            // Update Figma-managed properties on the existing node
            UpdateManagedProperties(existingNode, newNode);

            // Process children: match by FigmaNodeId
            var newChildren = new List<Transform>();
            for (int i = 0; i < newNode.transform.childCount; i++)
                newChildren.Add(newNode.transform.GetChild(i));

            var processedExisting = new HashSet<string>();

            foreach (var newChild in newChildren)
            {
                var newRef = newChild.GetComponent<FigmaNodeRef>();
                if (newRef == null || string.IsNullOrEmpty(newRef.FigmaNodeId))
                    continue;

                if (existingIndex.TryGetValue(newRef.FigmaNodeId, out var existingChild))
                {
                    // Existing node found — merge
                    MergeNode(newChild.gameObject, existingChild, existingIndex);
                    // Ensure correct sibling order
                    existingChild.transform.SetSiblingIndex(newChild.GetSiblingIndex());
                    processedExisting.Add(newRef.FigmaNodeId);
                }
                else
                {
                    // New node — add to existing parent
                    var clone = Object.Instantiate(newChild.gameObject, existingNode.transform);
                    clone.name = newChild.name;
                    _logger.Info($"Added new node: {newChild.name}");
                }
            }

            // Note: We do NOT remove existing children that are no longer in Figma.
            // They may be user-added GameObjects. Log them for awareness.
            for (int i = 0; i < existingNode.transform.childCount; i++)
            {
                var existingChild = existingNode.transform.GetChild(i);
                var ref_ = existingChild.GetComponent<FigmaNodeRef>();
                if (ref_ != null && !string.IsNullOrEmpty(ref_.FigmaNodeId) &&
                    !processedExisting.Contains(ref_.FigmaNodeId))
                {
                    _logger.Warn($"Node '{existingChild.name}' (Figma: {ref_.FigmaNodeId}) no longer in Figma design — kept.");
                }
            }
        }

        private void UpdateManagedProperties(GameObject existing, GameObject updated)
        {
            // RectTransform
            var existRt = existing.GetComponent<RectTransform>();
            var newRt = updated.GetComponent<RectTransform>();
            if (existRt != null && newRt != null)
            {
                existRt.anchorMin = newRt.anchorMin;
                existRt.anchorMax = newRt.anchorMax;
                existRt.offsetMin = newRt.offsetMin;
                existRt.offsetMax = newRt.offsetMax;
                existRt.pivot = newRt.pivot;
                existRt.sizeDelta = newRt.sizeDelta;
                existRt.anchoredPosition = newRt.anchoredPosition;
            }

            // Image
            var existImg = existing.GetComponent<Image>();
            var newImg = updated.GetComponent<Image>();
            if (newImg != null)
            {
                if (existImg == null)
                    existImg = existing.AddComponent<Image>();
                existImg.sprite = newImg.sprite;
                existImg.color = newImg.color;
                existImg.type = newImg.type;
                existImg.preserveAspect = newImg.preserveAspect;
            }

            // TextMeshProUGUI
            var existText = existing.GetComponent<TMPro.TextMeshProUGUI>();
            var newText = updated.GetComponent<TMPro.TextMeshProUGUI>();
            if (newText != null)
            {
                if (existText == null)
                    existText = existing.AddComponent<TMPro.TextMeshProUGUI>();
                existText.text = newText.text;
                existText.fontSize = newText.fontSize;
                existText.color = newText.color;
                existText.fontStyle = newText.fontStyle;
                existText.characterSpacing = newText.characterSpacing;
                existText.lineSpacing = newText.lineSpacing;
                existText.horizontalAlignment = newText.horizontalAlignment;
                existText.verticalAlignment = newText.verticalAlignment;
            }

            // CanvasGroup (opacity)
            var existCg = existing.GetComponent<CanvasGroup>();
            var newCg = updated.GetComponent<CanvasGroup>();
            if (newCg != null)
            {
                if (existCg == null)
                    existCg = existing.AddComponent<CanvasGroup>();
                existCg.alpha = newCg.alpha;
            }

            // LayoutGroup — update if present on new, but don't remove from existing
            var newHlg = updated.GetComponent<HorizontalLayoutGroup>();
            if (newHlg != null)
            {
                var existHlg = existing.GetComponent<HorizontalLayoutGroup>();
                if (existHlg == null) existHlg = existing.AddComponent<HorizontalLayoutGroup>();
                CopyLayoutGroup(existHlg, newHlg);
            }

            var newVlg = updated.GetComponent<VerticalLayoutGroup>();
            if (newVlg != null)
            {
                var existVlg = existing.GetComponent<VerticalLayoutGroup>();
                if (existVlg == null) existVlg = existing.AddComponent<VerticalLayoutGroup>();
                CopyLayoutGroup(existVlg, newVlg);
            }

            // Name update
            existing.name = updated.name;
        }

        private void CopyLayoutGroup(HorizontalOrVerticalLayoutGroup target, HorizontalOrVerticalLayoutGroup source)
        {
            target.padding = source.padding;
            target.spacing = source.spacing;
            target.childAlignment = source.childAlignment;
            target.childForceExpandWidth = source.childForceExpandWidth;
            target.childForceExpandHeight = source.childForceExpandHeight;
            target.childControlWidth = source.childControlWidth;
            target.childControlHeight = source.childControlHeight;
            target.childScaleWidth = source.childScaleWidth;
            target.childScaleHeight = source.childScaleHeight;
        }

        /// <summary>
        /// Check if a component type is managed by Figma (will be overwritten on re-import).
        /// User-added components of types NOT in this list will be preserved.
        /// </summary>
        public static bool IsFigmaManaged(System.Type componentType)
        {
            return FigmaManagedTypes.Contains(componentType);
        }
    }
}
