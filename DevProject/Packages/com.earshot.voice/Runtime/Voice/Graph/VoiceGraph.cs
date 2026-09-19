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

        /// <summary>
        /// Jede Tuer/Ecke kostet extra Meter. Sonst klingt der naechste Raum
        /// fast so laut wie der Flur davor.
        /// </summary>
        public const float OpeningTurnPenalty = 2.5f;

        public readonly struct Connection
        {
            public Connection(VoiceZone a, VoiceZone b, VoicePortal portal, float weight, float length)
            {
                A = a;
                B = b;
                Portal = portal;
                Weight = weight;
                Length = length;
            }

            public VoiceZone A { get; }
            public VoiceZone B { get; }
            public VoicePortal Portal { get; }
            public float Weight { get; }

            /// <summary>
            /// Kantenlaenge ohne Offenheits-Aufschlag. Basis fuer das inkrementelle
            /// Nachziehen der Gewichte bei Offenheits-Aenderungen (L5).
            /// </summary>
            public float Length { get; }
        }

        public readonly struct DebugPath
        {
            public DebugPath(
                Vector3 from,
                Vector3 to,
                IReadOnlyList<Vector3> waypoints,
                float hearingDistance,
                bool usedGraph)
            {
                From = from;
                To = to;
                Waypoints = waypoints;
                HearingDistance = hearingDistance;
                UsedGraph = usedGraph;
            }

            public Vector3 From { get; }
            public Vector3 To { get; }
            public IReadOnlyList<Vector3> Waypoints { get; }
            public float HearingDistance { get; }
            public bool UsedGraph { get; }
        }

        private static readonly VoiceGraphSearch search = new VoiceGraphSearch();
        private static readonly Dictionary<int, VoicePortal> portalsByKey =
            new Dictionary<int, VoicePortal>();
        private static readonly List<Connection> connections = new List<Connection>(32);
        private static readonly List<int> pathBuffer = new List<int>(8);
        private static readonly List<Vector3> debugWaypoints = new List<Vector3>(8);
        private static DebugPath lastPath;
        private static bool dirty = true;
        private static bool opennessDirty;
        private static int nodeCount;

        public static int NodeCount => nodeCount;
        public static int ConnectionCount => connections.Count;
        public static IReadOnlyList<Connection> Connections => connections;
        public static DebugPath LastPath => lastPath;

        public static void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>
        /// Reine Offenheits-Aenderung (Tuer bewegt sich): Die Struktur bleibt unangetastet,
        /// nur die Kantengewichte ziehen beim naechsten Zugriff nach. Kein Szenen-Scan,
        /// keine Zonen-Probes - das passiert pro Tuer-Schwingung mehrmals pro Sekunde (L5).
        /// </summary>
        public static void MarkOpennessDirty()
        {
            opennessDirty = true;
        }

        public static void RebuildIfNeeded()
        {
            if (dirty)
            {
                Rebuild();
                return;
            }

            if (opennessDirty)
            {
                UpdateOpennessWeights();
            }
        }

        public static void Rebuild()
        {
            dirty = false;
            opennessDirty = false;
            search.Clear();
            portalsByKey.Clear();
            connections.Clear();
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

                float length = EdgeLength(a, b, portal) + OpeningPenalty(portal);
                float weight = ComputeWeight(portal, length);

                search.AddUndirectedEdge(a.GetInstanceID(), b.GetInstanceID(), weight, key);
                connections.Add(new Connection(a, b, portal, weight, length));
            }
        }

        private static float ComputeWeight(VoicePortal portal, float length)
        {
            if (portal == null || portal.Openness >= OpennessCheapThreshold)
            {
                return length;
            }

            return length + (1f - portal.Openness) * ClosedLengthPenalty;
        }

        private static readonly Dictionary<int, float> weightScratch =
            new Dictionary<int, float>(32);

        /// <summary>
        /// Aktualisiert nach reinen Offenheits-Aenderungen nur die Kantengewichte -
        /// Struktur, Knoten und Probes bleiben unberuehrt. Ist eine Referenz inzwischen
        /// gestorben (Tuer zerstoert, Zone geloescht), wird der volle Rebuild angefordert:
        /// Dieser schnelle Pfad bleibt bewusst duemmlich und dadurch korrekt.
        /// </summary>
        private static void UpdateOpennessWeights()
        {
            opennessDirty = false;
            weightScratch.Clear();

            for (int i = 0; i < connections.Count; i++)
            {
                var link = connections[i];
                if (link.Portal == null || link.A == null || link.B == null)
                {
                    dirty = true;
                    return;
                }

                float weight = ComputeWeight(link.Portal, link.Length);
                if (!Mathf.Approximately(weight, link.Weight))
                {
                    connections[i] = new Connection(link.A, link.B, link.Portal, weight, link.Length);
                }

                weightScratch[link.Portal.GetInstanceID()] = weight;
            }

            search.UpdateEdgeWeights(weightScratch);
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

        public static void RememberPath(
            Vector3 from,
            Vector3 to,
            List<VoicePortal> portals,
            float hearingDistance,
            bool usedGraph)
        {
            debugWaypoints.Clear();
            if (portals != null)
            {
                for (int i = 0; i < portals.Count; i++)
                {
                    debugWaypoints.Add(PortalPosition(portals[i]));
                }
            }

            lastPath = new DebugPath(
                from,
                to,
                debugWaypoints.ToArray(),
                hearingDistance,
                usedGraph);
        }

        public static void CollectPreflight(List<string> warnings)
        {
            if (warnings == null) return;
            warnings.Clear();
            RebuildIfNeeded();

#if UNITY_6000_5_OR_NEWER
            var zones = Object.FindObjectsByType<VoiceZone>(FindObjectsInactive.Exclude);
            var portals = Object.FindObjectsByType<VoicePortal>(FindObjectsInactive.Exclude);
            var players = Object.FindObjectsByType<EarshotProximityVoice>(FindObjectsInactive.Exclude);
#else
            var zones = Object.FindObjectsByType<VoiceZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var portals = Object.FindObjectsByType<VoicePortal>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var players = Object.FindObjectsByType<EarshotProximityVoice>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            var connected = new HashSet<int>();
            for (int i = 0; i < connections.Count; i++)
            {
                var link = connections[i];
                if (link.A != null) connected.Add(link.A.GetInstanceID());
                if (link.B != null) connected.Add(link.B.GetInstanceID());
            }

            for (int i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                if (zone == null) continue;
                if (!connected.Contains(zone.GetInstanceID()))
                {
                    warnings.Add("Zone ohne Portal: " + zone.ZoneName);
                }
            }

            for (int i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                if (portal == null) continue;
                if ((portal.ZoneA != null) != (portal.ZoneB != null))
                {
                    warnings.Add("Portal mit nur einer expliziten Zone: " + portal.name);
                }
                if (!TryResolveSides(portal, out var a, out var b) || a == b)
                {
                    warnings.Add("Portal ohne zwei Zonen: " + portal.name);
                }
            }

            var probe = new Collider[8];
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null) continue;
                if (VoiceZone.FindAt(player.Position, ~0, probe) == null)
                {
                    warnings.Add("Spieler ausserhalb jeder Zone: " + player.name);
                }
            }
        }

        /// <summary>
        /// Authoring-API: liefert die beiden Raeume, die ein Portal verbindet. So
        /// entscheiden Werkzeug und Graph identisch (explizite Zuweisung schlaegt
        /// die Achsen-Probe, Details siehe unten im Rumpf).
        /// </summary>
        public static bool TryResolveSides(VoicePortal portal, out VoiceZone a, out VoiceZone b)
        {
            a = null;
            b = null;
            if (portal == null) return false;

            // Explizite Zuweisung schlaegt die Geometrie: Eine Bruecke verbindet Zonen,
            // die nirgends nebeneinander liegen (z.B. Teleport-Tuer, E4/L4).
            if (portal.HasExplicitZones)
            {
                a = portal.ZoneA;
                b = portal.ZoneB;
                return true;
            }

            Vector3 center = PortalCenter(portal);

            if (portal.Kind == VoicePortalKind.Stair)
            {
                a = ProbeZone(center, Vector3.up);
                b = ProbeZone(center, Vector3.down);
                if (a != null && b != null && a != b) return true;
            }

            Vector3 axis = portal.transform.forward;
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.forward;
            axis.Normalize();

            a = ProbeZone(center, axis);
            b = ProbeZone(center, -axis);
            return a != null && b != null;
        }

        private static VoiceZone ProbeZone(Vector3 center, Vector3 axis)
        {
            float[] offsets = { 0.4f, 1.2f, 2.4f, 4f };
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
            if (portal != null && portal.HasTravelLength) return portal.TravelLength;

            Vector3 portalPos = PortalCenter(portal);
            Vector3 from = ZoneCenter(a);
            Vector3 to = ZoneCenter(b);
            return Vector3.Distance(from, portalPos) + Vector3.Distance(portalPos, to);
        }

        public static Vector3 ZoneCenter(VoiceZone zone)
        {
            if (zone == null) return default;
            var col = zone.GetComponent<Collider>();
            return col != null ? col.bounds.center : zone.transform.position;
        }

        public static Vector3 PortalPosition(VoicePortal portal)
        {
            return portal != null ? PortalCenter(portal) : default;
        }

        public static float HopLength(Vector3 from, Vector3 to, VoicePortal portal)
        {
            float geometric = Vector3.Distance(from, to);
            if (portal != null && portal.HasTravelLength)
            {
                geometric = Mathf.Max(geometric, portal.TravelLength);
            }

            return geometric + OpeningPenalty(portal);
        }

        public static float OpeningPenalty(VoicePortal portal)
        {
            if (portal == null || portal.Kind == VoicePortalKind.Stair) return 0f;
            return OpeningTurnPenalty;
        }

        /// <summary>
        /// Wahr, wenn das Portal genau diese zwei Zonen bewegen.
        /// </summary>
        public static bool PortalJoins(VoicePortal portal, VoiceZone a, VoiceZone b)
        {
            if (portal == null || a == null || b == null || a == b) return false;
            if (!TryResolveSides(portal, out var left, out var right)) return false;
            return (left == a && right == b) || (left == b && right == a);
        }
    }
}
