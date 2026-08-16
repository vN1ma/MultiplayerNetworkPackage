using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Verteilt neu erschienene Spieler auf Startpunkte.
    /// <para>
    /// Netcode erzeugt das Prefab selbst; diese Komponente setzt nur die Pose. Das passiert
    /// ausschliesslich auf dem Host, sonst wuerden Client und Server denselben Avatar an
    /// verschiedene Orte schieben.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Player Spawner")]
    public class PlayerSpawner : MonoBehaviour
    {
        private const float FallbackRadius = 2f;

        [SerializeField]
        [Tooltip("Startpunkte, reihum in der Reihenfolge, in der die Spieler erscheinen. Leer lassen verteilt im Kreis um den Weltursprung.")]
        private Transform[] spawnPoints;

        /// <summary>
        /// Ersetzt die Spawnpunkte. Fuer den Setup der Beispielszene, damit man die
        /// Punkte nicht von Hand in das Array ziehen muss.
        /// </summary>
        public void SetSpawnPoints(params Transform[] points)
        {
            spawnPoints = points;
        }

        private bool warnedAboutFallback;

        private void OnEnable()
        {
            PlayerRegistry.PlayerAdded += OnPlayerAdded;

            // Spieler, die schon da waren, bevor dieser Spawner aktiv wurde.
            var existing = PlayerRegistry.Players;
            for (int i = 0; i < existing.Count; i++)
            {
                OnPlayerAdded(existing[i]);
            }
        }

        private void OnDisable()
        {
            PlayerRegistry.PlayerAdded -= OnPlayerAdded;
        }

        private void OnPlayerAdded(CoopPlayer player)
        {
            if (player == null) return;

            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return;

            int index = IndexOf(player);
            GetPose(index, out Vector3 position, out Quaternion rotation);
            player.Teleport(position, rotation);
        }

        private static int IndexOf(CoopPlayer player)
        {
            var list = PlayerRegistry.Players;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == player) return i;
            }

            return 0;
        }

        private void GetPose(int index, out Vector3 position, out Quaternion rotation)
        {
            var point = GetSpawnPoint(index);
            if (point != null)
            {
                position = point.position;
                rotation = point.rotation;
                return;
            }

            if (!warnedAboutFallback)
            {
                warnedAboutFallback = true;
                CoopLog.Warn(
                    "PlayerSpawner hat keine Spawnpunkte. Spieler werden im Kreis um den " +
                    "Weltursprung verteilt. Punkte im Inspector zuweisen.");
            }

            float angle = index * Mathf.PI * 0.5f;
            position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * FallbackRadius;
            rotation = Quaternion.LookRotation((Vector3.zero - position).normalized, Vector3.up);
        }

        private Transform GetSpawnPoint(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;

            int count = 0;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null) count++;
            }

            if (count == 0) return null;

            int wrapped = ((index % count) + count) % count;
            int seen = 0;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null) continue;
                if (seen == wrapped) return spawnPoints[i];
                seen++;
            }

            return null;
        }

        private void OnDrawGizmos()
        {
            if (spawnPoints != null)
            {
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    var point = spawnPoints[i];
                    if (point == null) continue;
                    DrawSpawnGizmo(point.position, i + 1, new Color(0.3f, 0.8f, 0.45f, 0.85f));
                }
            }

            bool any = false;
            if (spawnPoints != null)
            {
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    if (spawnPoints[i] != null)
                    {
                        any = true;
                        break;
                    }
                }
            }

            if (any) return;

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                var pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * FallbackRadius;
                DrawSpawnGizmo(pos, i + 1, new Color(0.85f, 0.7f, 0.2f, 0.7f));
            }
        }

        private static void DrawSpawnGizmo(Vector3 position, int number, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawSphere(position, 0.25f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.2f);

#if UNITY_EDITOR
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(position + Vector3.up * 1.35f, number.ToString());
#endif
        }
    }
}
