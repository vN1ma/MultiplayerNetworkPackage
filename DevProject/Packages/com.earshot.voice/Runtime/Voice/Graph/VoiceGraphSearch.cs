using System.Collections.Generic;

namespace Earshot.Voice
{
    /// <summary>
    /// Dijkstra auf einem kleinen ungerichteten Graphen (Raeume als Knoten).
    /// Keine Unity-Abhaengigkeit, damit EditMode-Tests ohne Szene laufen.
    /// </summary>
    public sealed class VoiceGraphSearch
    {
        private readonly Dictionary<int, List<VoiceGraphEdge>> adjacency =
            new Dictionary<int, List<VoiceGraphEdge>>();

        public void Clear()
        {
            adjacency.Clear();
        }

        public void AddNode(int id)
        {
            if (!adjacency.ContainsKey(id))
            {
                adjacency[id] = new List<VoiceGraphEdge>(4);
            }
        }

        public void AddUndirectedEdge(int a, int b, float weight, int portalKey)
        {
            if (a == b) return;
            AddNode(a);
            AddNode(b);
            adjacency[a].Add(new VoiceGraphEdge(b, weight, portalKey));
            adjacency[b].Add(new VoiceGraphEdge(a, weight, portalKey));
        }

        public bool TryFindPath(int start, int goal, List<int> portalKeys, out float cost)
        {
            portalKeys.Clear();
            cost = 0f;

            if (start == goal)
            {
                return true;
            }

            if (!adjacency.ContainsKey(start) || !adjacency.ContainsKey(goal))
            {
                return false;
            }

            var dist = new Dictionary<int, float>(adjacency.Count);
            var prev = new Dictionary<int, int>(adjacency.Count);
            var prevPortal = new Dictionary<int, int>(adjacency.Count);
            var remaining = new List<int>(adjacency.Count);

            foreach (var pair in adjacency)
            {
                dist[pair.Key] = float.PositiveInfinity;
                remaining.Add(pair.Key);
            }

            dist[start] = 0f;

            while (remaining.Count > 0)
            {
                int bestIndex = 0;
                float best = dist[remaining[0]];
                for (int i = 1; i < remaining.Count; i++)
                {
                    float d = dist[remaining[i]];
                    if (d >= best) continue;
                    best = d;
                    bestIndex = i;
                }

                int current = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);

                if (current == goal)
                {
                    cost = dist[goal];
                    Reconstruct(goal, start, prev, prevPortal, portalKeys);
                    return true;
                }

                if (float.IsPositiveInfinity(best)) break;

                var edges = adjacency[current];
                for (int i = 0; i < edges.Count; i++)
                {
                    var edge = edges[i];
                    float next = dist[current] + edge.Weight;
                    if (next >= dist[edge.To]) continue;
                    dist[edge.To] = next;
                    prev[edge.To] = current;
                    prevPortal[edge.To] = edge.PortalKey;
                }
            }

            return false;
        }

        private static void Reconstruct(
            int goal,
            int start,
            Dictionary<int, int> prev,
            Dictionary<int, int> prevPortal,
            List<int> portalKeys)
        {
            var reverse = new List<int>(8);
            int walk = goal;
            while (walk != start)
            {
                reverse.Add(prevPortal[walk]);
                walk = prev[walk];
            }

            for (int i = reverse.Count - 1; i >= 0; i--)
            {
                portalKeys.Add(reverse[i]);
            }
        }
    }

    internal readonly struct VoiceGraphEdge
    {
        public readonly int To;
        public readonly float Weight;
        public readonly int PortalKey;

        public VoiceGraphEdge(int to, float weight, int portalKey)
        {
            To = to;
            Weight = weight;
            PortalKey = portalKey;
        }
    }
}
