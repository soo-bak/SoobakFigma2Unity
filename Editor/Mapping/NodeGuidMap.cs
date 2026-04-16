using System.Collections.Generic;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// ScriptableObject that persists the mapping between
    /// Figma node IDs and Unity asset GUIDs for re-import support.
    /// </summary>
    [CreateAssetMenu(fileName = "FigmaNodeGuidMap", menuName = "SoobakFigma2Unity/Node GUID Map")]
    public sealed class NodeGuidMap : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string FigmaNodeId;
            public string UnityAssetPath;
            public string LastImportHash;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, Entry> _cache;

        private void BuildCache()
        {
            _cache = new Dictionary<string, Entry>();
            foreach (var e in entries)
                _cache[e.FigmaNodeId] = e;
        }

        public bool TryGetEntry(string figmaNodeId, out Entry entry)
        {
            if (_cache == null) BuildCache();
            return _cache.TryGetValue(figmaNodeId, out entry);
        }

        public void SetEntry(string figmaNodeId, string unityAssetPath, string hash = null)
        {
            if (_cache == null) BuildCache();

            var newEntry = new Entry
            {
                FigmaNodeId = figmaNodeId,
                UnityAssetPath = unityAssetPath,
                LastImportHash = hash ?? ""
            };

            if (_cache.ContainsKey(figmaNodeId))
            {
                _cache[figmaNodeId] = newEntry;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].FigmaNodeId == figmaNodeId)
                    {
                        entries[i] = newEntry;
                        break;
                    }
                }
            }
            else
            {
                _cache[figmaNodeId] = newEntry;
                entries.Add(newEntry);
            }
        }

        public void Clear()
        {
            entries.Clear();
            _cache?.Clear();
        }
    }
}
