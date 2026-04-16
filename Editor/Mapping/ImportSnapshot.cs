using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Persists a snapshot of the last import state so subsequent imports
    /// can detect what changed and only update affected nodes.
    ///
    /// Saved as a JSON file alongside the output prefabs.
    /// </summary>
    [System.Serializable]
    internal sealed class ImportSnapshot
    {
        /// <summary>Figma file key.</summary>
        [JsonProperty("fileKey")] public string FileKey;

        /// <summary>Figma file version string from last import.</summary>
        [JsonProperty("fileVersion")] public string FileVersion;

        /// <summary>Figma file lastModified timestamp.</summary>
        [JsonProperty("lastModified")] public string LastModified;

        /// <summary>Per-node hashes: nodeId → hash of serialized node properties.</summary>
        [JsonProperty("nodeHashes")] public Dictionary<string, string> NodeHashes = new Dictionary<string, string>();

        /// <summary>Image scale used in last import.</summary>
        [JsonProperty("imageScale")] public float ImageScale;

        private const string FileName = ".soobak_import_snapshot.json";

        public static string GetSnapshotPath(string outputDir)
        {
            return Path.Combine(outputDir, FileName);
        }

        public static ImportSnapshot Load(string outputDir)
        {
            var path = GetSnapshotPath(outputDir);
            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ImportSnapshot>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Save(string outputDir)
        {
            var path = GetSnapshotPath(outputDir);
            Directory.CreateDirectory(outputDir);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Compute a hash of a node's visual properties for change detection.
        /// </summary>
        public static string ComputeNodeHash(Models.FigmaNode node)
        {
            // Hash key visual properties that affect the output
            var sb = new System.Text.StringBuilder();
            sb.Append(node.Name);
            sb.Append('|');
            sb.Append(node.Type);
            sb.Append('|');
            sb.Append(node.Visible);
            sb.Append('|');
            sb.Append(node.Opacity);

            if (node.AbsoluteBoundingBox != null)
            {
                sb.Append('|');
                sb.Append(node.AbsoluteBoundingBox.X);
                sb.Append(',');
                sb.Append(node.AbsoluteBoundingBox.Y);
                sb.Append(',');
                sb.Append(node.AbsoluteBoundingBox.Width);
                sb.Append(',');
                sb.Append(node.AbsoluteBoundingBox.Height);
            }

            if (node.Fills != null)
            {
                foreach (var fill in node.Fills)
                {
                    sb.Append('|');
                    sb.Append(fill.Type);
                    sb.Append(',');
                    sb.Append(fill.Visible);
                    if (fill.Color != null)
                    {
                        sb.Append(',');
                        sb.Append(fill.Color.R);
                        sb.Append(',');
                        sb.Append(fill.Color.G);
                        sb.Append(',');
                        sb.Append(fill.Color.B);
                        sb.Append(',');
                        sb.Append(fill.Color.A);
                    }
                    if (fill.ImageRef != null)
                    {
                        sb.Append(',');
                        sb.Append(fill.ImageRef);
                    }
                }
            }

            if (node.Characters != null)
            {
                sb.Append("|T:");
                sb.Append(node.Characters);
            }

            if (node.Constraints != null)
            {
                sb.Append("|C:");
                sb.Append(node.Constraints.Horizontal);
                sb.Append(',');
                sb.Append(node.Constraints.Vertical);
            }

            sb.Append("|L:");
            sb.Append(node.LayoutMode);
            sb.Append(',');
            sb.Append(node.ItemSpacing);

            sb.Append("|R:");
            sb.Append(node.CornerRadius);

            // Simple hash
            var str = sb.ToString();
            int hash = 17;
            foreach (char c in str)
                hash = hash * 31 + c;
            return hash.ToString("X8");
        }

        /// <summary>
        /// Compare with current node tree and return IDs of changed/new nodes.
        /// </summary>
        public HashSet<string> GetChangedNodeIds(Models.FigmaNode rootNode)
        {
            var changed = new HashSet<string>();
            CollectChanges(rootNode, changed);
            return changed;
        }

        private void CollectChanges(Models.FigmaNode node, HashSet<string> changed)
        {
            var currentHash = ComputeNodeHash(node);

            if (!NodeHashes.TryGetValue(node.Id, out var prevHash) || prevHash != currentHash)
            {
                changed.Add(node.Id);
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectChanges(child, changed);
            }
        }

        /// <summary>
        /// Update the snapshot with current node hashes.
        /// </summary>
        public void UpdateHashes(Models.FigmaNode rootNode)
        {
            UpdateHashesRecursive(rootNode);
        }

        private void UpdateHashesRecursive(Models.FigmaNode node)
        {
            NodeHashes[node.Id] = ComputeNodeHash(node);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    UpdateHashesRecursive(child);
            }
        }
    }
}
