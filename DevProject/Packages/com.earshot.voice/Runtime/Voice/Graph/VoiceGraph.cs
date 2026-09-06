using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Raeume als Knoten, Tueren/Treppen als Kanten. Wird aus den
    /// <see cref="VoiceZone"/>- und <see cref="VoicePortal"/>-Komponenten in der Szene
    /// gebaut. Ohne Zonen bleibt der Graph leer — dann gilt weiter die Sichtlinie.
    /// </summary>
    public static class VoiceGraph
    {
        public const float ClosedLengthPenalty = 12f;
        public const float OpennessCheapThreshold = 0.5f;

        private static readonly VoiceGraphSearch search = new VoiceGraphSearch();
        private static readonly Dictionary<int, VoicePortal> portalsByKey =
            new Dictionary<int, VoicePortal>();
        private static readonly List<int> pathBuffer = new List<int>(8);
        private static bool dirty = true;
        private static int nodeCount;

        public static int NodeCount => nodeCount;

        public static void MarkDirty()
        {
            dirty = true;
        }

        public static void RebuildIfNeeded()
        {
            if (!dirty) return;
            Rebuild();
        }

        public static void Rebuild()
        {
            dirty = false;
            search.Clear();
            portalsByKey.Clear();
            nodeCount = 0;

#if UNITY_6000_5_OR_NEWER
            var zones = Object.FindObjectsByType<VoiceZone>(FindObjectsInactive.Exclude);
            var portals = Object.FindObjectsByType<VoicePortal>(FindObjectsInactive.Exclude);
#else
            var zones = Object.FindObjectsByType<VoiceZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var portals = Object.FindObjectsByType<VoicePortal>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null) continue;
                search.AddNode(zones[i].GetInstanceID());
                nodeCount++;
            }

            for (int i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                if (portal == null) continue;
                if (!TryResolveSides(portal, out var a, out var b)) continue;
                if (a == b) continue;

                int key = portal.GetInstanceID();
                portalsByKey[key] = portal;

                float length = EdgeLength(a, b, portal);
                float closed = 1f - portal.Openness;
                float weight = length + closed * ClosedLengthPenalty;
                if (portal.Openness >= OpennessCheapThreshold)
                {
                    weight = length;
                }

                search.AddUndirectedEdge(a.GetInstanceID(), b.GetInstanceID(), weight, key);
            }
        }

        /// <summary>
        /// Sucht den kuerzesten akustischen Weg zwischen zwei Zonen. Liefert die
        /// Portale in Laufreihenfolge und die Summe der Kantengewichte.
        /// </summary>
        public static bool TryFindPath(
            VoiceZone from,
            VoiceZone to,
            List<VoicePortal> portals,
            out float cost)
        {
            portals.Clear();
            cost = 0f;
            if (from == null || to == null) return false;
            if (from == to) return true;

            RebuildIfNeeded();

            if (!search.TryFindPath(from.GetInstanceID(), to.GetInstanceID(), pathBuffer, out cost))
            {
                return false;
            }

            for (int i = 0; i < pathBuffer.Count; i++)
            {
                if (portalsByKey.TryGetValue(pathBuffer[i], out var portal) && portal != null)
                {
                    portals.Add(portal);
                }
            }

            return true;
        }

        internal static bool TryResolveSides(VoicePortal portal, out VoiceZone a, out VoiceZone b)
        {
            a = null;
            b = null;
            if (portal == null) return false;

            Vector3 center = PortalCenter(portal);
            Vector3 axis = portal.transform.forward;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.forward;
            axis.Normalize();

            a = ProbeZone(center, axis);
            b = ProbeZone(center, -axis);
            return a != null && b != null;
        }

        private static VoiceZone ProbeZone(Vector3 center, Vector3 axis)
        {
            float[] offsets = { 0.4f, 1.2f, 2.4f };
            for (int i = 0; i < offsets.Length; i++)
            {
                var zone = VoiceZone.FindAt(center + axis * offsets[i], ~0, ProbeBuffer);
                if (zone != null) return zone;
            }

            return null;
        }

        private static readonly Collider[] ProbeBuffer = new Collider[8];

        private static Vector3 PortalCenter(VoicePortal portal)
        {
            var col = portal.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : portal.transform.position;
        }

        private static float EdgeLength(VoiceZone a, VoiceZone b, VoicePortal portal)
        {
            Vector3 portalPos = PortalCenter(portal);
            Vector3 from = ZoneCenter(a);
            Vector3 to = ZoneCenter(b);
            return Vector3.Distance(from, portalPos) + Vector3.Distance(portalPos, to);
        }

        internal static Vector3 ZoneCenter(VoiceZone zone)
        {
            if (zone == null) return default;
            var col = zone.GetComponent<Collider>();
            return col != null ? col.bounds.center : zone.transform.position;
        }

        internal static Vector3 PortalPosition(VoicePortal portal)
        {
            return portal != null ? PortalCenter(portal) : default;
        }
    }
}
