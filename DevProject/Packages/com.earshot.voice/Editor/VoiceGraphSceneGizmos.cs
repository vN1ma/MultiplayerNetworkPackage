using UnityEditor;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    public static class VoiceGraphSceneGizmos
    {
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawZone(VoiceZone zone, GizmoType type)
        {
            if (zone == null) return;
            var collider = zone.GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = new Color(0.31f, 0.66f, 0.87f, 0.12f);
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
            Gizmos.color = new Color(0.31f, 0.66f, 0.87f, 0.85f);
            Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawGraph(VoiceZone zone, GizmoType type)
        {
            if (zone == null) return;
            VoiceGraph.RebuildIfNeeded();

            var links = VoiceGraph.Connections;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link.A != zone && link.B != zone) continue;
                if (link.A == null || link.B == null) continue;

                Vector3 from = VoiceGraph.ZoneCenter(link.A);
                Vector3 to = VoiceGraph.ZoneCenter(link.B);
                Vector3 mid = VoiceGraph.PortalPosition(link.Portal);

                Gizmos.color = link.Portal != null && link.Portal.Kind == VoicePortalKind.Stair
                    ? new Color(0.35f, 0.7f, 1f, 0.9f)
                    : new Color(0.95f, 0.75f, 0.2f, 0.9f);
                Gizmos.DrawLine(from, mid);
                Gizmos.DrawLine(mid, to);
            }

            DrawLastPath();
        }

        private static void DrawLastPath()
        {
            var path = VoiceGraph.LastPath;
            if (!path.UsedGraph || path.Waypoints == null) return;

            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.95f);
            Vector3 previous = path.From;
            for (int i = 0; i < path.Waypoints.Count; i++)
            {
                Gizmos.DrawLine(previous, path.Waypoints[i]);
                Gizmos.DrawSphere(path.Waypoints[i], 0.12f);
                previous = path.Waypoints[i];
            }

            Gizmos.DrawLine(previous, path.To);
        }
    }
}
