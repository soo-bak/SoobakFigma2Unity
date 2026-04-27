using System.Collections.Generic;

namespace SoobakFigma2Unity.Editor.Pipeline
{
    /// <summary>
    /// Directed graph of "component A's master tree contains an INSTANCE of component B"
    /// edges. ComponentExtractionPass walks the topological order so leaf components
    /// (no nested instances) get their .prefab files written first; that way when a
    /// container component is extracted next, the inner instances can already be
    /// linked as PrefabInstances of the leaf prefabs we just produced.
    ///
    /// Cycles can't legally exist in Figma — a component can't INSTANCE itself or any
    /// of its ancestors — but the graph still detects them as a defensive log so a
    /// corrupted file doesn't silently produce wrong output.
    /// </summary>
    internal sealed class ComponentDependencyGraph
    {
        private readonly Dictionary<string, HashSet<string>> _outEdges = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _allNodes = new HashSet<string>();

        public void AddNode(string componentId)
        {
            if (string.IsNullOrEmpty(componentId)) return;
            _allNodes.Add(componentId);
            if (!_outEdges.ContainsKey(componentId))
                _outEdges[componentId] = new HashSet<string>();
        }

        /// <summary>Adds the edge "<paramref name="from"/> contains an instance of <paramref name="to"/>".</summary>
        public void AddEdge(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
            AddNode(from);
            AddNode(to);
            _outEdges[from].Add(to);
        }

        public IReadOnlyCollection<string> Nodes => _allNodes;

        /// <summary>
        /// Returns componentIds in leaf-first order. If a cycle is detected, the
        /// remaining cyclic nodes are appended at the end in arbitrary order so
        /// extraction still completes; <see cref="HasCycle"/> reports the issue.
        /// </summary>
        public List<string> TopologicalOrder()
        {
            var result = new List<string>(_allNodes.Count);
            var visited = new HashSet<string>();
            var temp = new HashSet<string>();
            foreach (var node in _allNodes)
                Visit(node, visited, temp, result);
            // Anything still unvisited is part of a cycle (Visit aborts on temp re-entry).
            // Append it so we still write something — caller logs the cycle separately.
            foreach (var node in _allNodes)
                if (!visited.Contains(node))
                    result.Add(node);
            return result;
        }

        private void Visit(string node, HashSet<string> visited, HashSet<string> temp, List<string> result)
        {
            if (visited.Contains(node)) return;
            if (temp.Contains(node)) return; // back-edge — cycle. Bail; caller surfaces it via HasCycle.
            temp.Add(node);
            if (_outEdges.TryGetValue(node, out var outs))
                foreach (var next in outs)
                    Visit(next, visited, temp, result);
            temp.Remove(node);
            visited.Add(node);
            result.Add(node);
        }

        public bool HasCycle(out List<string> cycle)
        {
            cycle = null;
            var color = new Dictionary<string, int>(); // 0=unvisited, 1=in-stack, 2=done
            var stack = new List<string>();
            foreach (var node in _allNodes)
            {
                if (color.TryGetValue(node, out var c) && c != 0) continue;
                if (CycleDfs(node, color, stack, out cycle))
                    return true;
            }
            return false;
        }

        private bool CycleDfs(string node, Dictionary<string, int> color, List<string> stack, out List<string> cycle)
        {
            color[node] = 1;
            stack.Add(node);
            if (_outEdges.TryGetValue(node, out var outs))
            {
                foreach (var next in outs)
                {
                    color.TryGetValue(next, out var nc);
                    if (nc == 1)
                    {
                        // Cycle found — slice the stack from `next` to the current top.
                        int start = stack.IndexOf(next);
                        cycle = stack.GetRange(start, stack.Count - start);
                        cycle.Add(next);
                        return true;
                    }
                    if (nc == 0 && CycleDfs(next, color, stack, out cycle))
                        return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            color[node] = 2;
            cycle = null;
            return false;
        }
    }
}
