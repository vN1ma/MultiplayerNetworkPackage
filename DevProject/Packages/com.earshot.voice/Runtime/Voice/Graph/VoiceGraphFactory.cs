using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Authoring- und Generator-API (Masterplan Phase 2/4): baut Zone- und Portal-
    /// Objekte programmatisch. Das Editor-Tool (Marker -> Zone) nutzt sie genauso
    /// wie spaeter der Stockwerk-Generator - beide Wege munden in dieselben
    /// <see cref="VoiceZone"/>-/<see cref="VoicePortal"/>-Komponenten, die der Graph
    /// einliest. Bewusst reine Runtime-API ohne UnityEditor-Abhaengigkeit; fuer
    /// Undo im Editor kapselt das Authoring-Fenster die Aufrufe.
    /// </summary>
    public static class VoiceGraphFactory
    {
        /// <summary>
        /// Wandabzug in Metern, damit die Trigger-Box nicht in Nachbarraeume ragt
        /// und der Zonen-Umfang stabil bleibt, wenn Wanddicke variiert.
        /// </summary>
        public const float DefaultWallInset = 0.1f;

        /// <summary>Maximale Raycast-Reichweite bei der Raum-Sondierung.</summary>
        public const float DefaultMaxProbeDistance = 30f;

        /// <summary>
        /// Erzeugt eine Zone als achsparallele Trigger-Box. Das Objekt heisst
        /// "AudioZone_&lt;name&gt;" (Namensvertrag des Authoring-Tools) und liegt,
        /// wenn ein Parent uebergeben wird, unter dessen Zones-Ordner.
        /// </summary>
        public static VoiceZone CreateZone(Bounds bounds, string zoneName, Transform parent)
        {
            var go = new GameObject("AudioZone_" + zoneName);
            go.transform.position = bounds.center;
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: true);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = bounds.size;

            var zone = go.AddComponent<VoiceZone>();
            zone.SetZoneName(zoneName);
            return zone;
        }

        /// <summary>
        /// Portal an einem Tuer-/Oeffnungs-Objekt. Idempotent: existiert bereits
        /// ein Portal am Objekt, wird nur die Art nachgezogen, nichts dupliziert.
        /// </summary>
        public static VoicePortal CreatePortal(GameObject host, VoicePortalKind kind)
        {
            if (host == null) return null;

            var portal = host.GetComponent<VoicePortal>();
            if (portal == null)
            {
                portal = host.AddComponent<VoicePortal>();
            }

            portal.SetKind(kind);
            return portal;
        }

        /// <summary>
        /// Verbindet zwei Raeume fest ueber ein Portal (umgeht die Achsen-Probe).
        /// Noetig fuer Bruecken (z.B. Teleport-Tueren, Phase 3) und unguenstige
        /// Geometrie. Fuer Bruecken zusaetzlich <see cref="VoicePortal.SetTravelLength"/>
        /// setzen, sonst zaehlt die geometrische Distanz der Zonenzentren.
        /// </summary>
        public static void LinkZones(VoicePortal portal, VoiceZone a, VoiceZone b)
        {
            if (portal == null) return;
            portal.SetExplicitZones(a, b);
        }

        /// <summary>
        /// Sondiert einen Raum von einem Punkt im Inneren: ein Raycast nach unten
        /// (Boden), nach oben (Decke) und vier horizontal (Waende) ergibt die Box,
        /// die den Innenraum fuellt. Moebel-Collider lassen sich per Layer-Maske
        /// ausblenden (z.B. nur Architektur-Layer zulassen).
        /// <para>
        /// Offene Seiten landen als Hinweis in <paramref name="issues"/> und werden
        /// mit der vollen Maximaldistanz weitergezaehlt (bewusster Fallback fuer
        /// Nischen/offene Uebergaenge - der Bericht macht das sichtbar). Fehlt Boden
        /// oder Decke, ist der Punkt kein brauchbarer Raum-Innenpunkt: false.
        /// </para>
        /// </summary>
        public static bool ProbeRoomBounds(
            Vector3 marker,
            LayerMask mask,
            float maxDistance,
            float wallInset,
            out Bounds bounds,
            List<string> issues)
        {
            bounds = default;

            bool hasFloor = Physics.Raycast(
                marker, Vector3.down, out RaycastHit floor,
                maxDistance, mask, QueryTriggerInteraction.Ignore);
            bool hasCeiling = Physics.Raycast(
                marker, Vector3.up, out RaycastHit ceiling,
                maxDistance, mask, QueryTriggerInteraction.Ignore);

            if (!hasFloor || !hasCeiling)
            {
                issues?.Add(
                    (!hasFloor ? "kein Boden" : "keine Decke") +
                    " ueber dem Marker gefunden (Raycast-Reichweite " +
                    maxDistance.ToString("0.0") + " m)");
                return false;
            }

            float xPlus = ProbeDirection(marker, Vector3.right, mask, maxDistance, "+X", issues);
            float xMinus = ProbeDirection(marker, Vector3.left, mask, maxDistance, "-X", issues);
            float zPlus = ProbeDirection(marker, Vector3.forward, mask, maxDistance, "+Z", issues);
            float zMinus = ProbeDirection(marker, Vector3.back, mask, maxDistance, "-Z", issues);

            float inset = Mathf.Max(0f, wallInset);
            float sizeX = Mathf.Max(0.5f, xPlus + xMinus - 2f * inset);
            float sizeZ = Mathf.Max(0.5f, zPlus + zMinus - 2f * inset);

            bounds = new Bounds(
                new Vector3(
                    marker.x + (xPlus - xMinus) * 0.5f,
                    (floor.point.y + ceiling.point.y) * 0.5f,
                    marker.z + (zPlus - zMinus) * 0.5f),
                new Vector3(
                    sizeX,
                    Mathf.Max(0.5f, ceiling.point.y - floor.point.y),
                    sizeZ));

            return true;
        }

        private static float ProbeDirection(
            Vector3 origin,
            Vector3 direction,
            LayerMask mask,
            float maxDistance,
            string label,
            List<string> issues)
        {
            if (Physics.Raycast(
                    origin, direction, out RaycastHit hit,
                    maxDistance, mask, QueryTriggerInteraction.Ignore))
            {
                return hit.distance;
            }

            issues?.Add(
                "offene Seite " + label + " - Zone reicht bis zur Maximaldistanz");
            return maxDistance;
        }
    }
}