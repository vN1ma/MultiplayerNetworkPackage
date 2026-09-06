using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Markiert einen Raum oder Bereich mit eigenen akustischen Eigenschaften.
    /// Auf ein GameObject mit einem Collider setzen, der als Trigger konfiguriert ist.
    /// <para>
    /// Zonen wirken auf zwei Arten: Steht der Sprecher darin, faerbt die Zone seine
    /// Stimme ein (Hall des Raums, in dem er spricht). Stehen Sprecher und Zuhoerer in
    /// verschiedenen Zonen, kommt zusaetzlich eine Trennungsdaempfung dazu.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Proximity/Voice Zone")]
    [RequireComponent(typeof(Collider))]
    public class VoiceZone : MonoBehaviour
    {
        [Header("Kennzeichnung")]
        [SerializeField]
        [Tooltip("Nur zur Orientierung im Editor, etwa 'Zimmer 101' oder 'Lobby'.")]
        private string zoneName = "Room";

        [Header("Klang innerhalb der Zone")]
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Hallanteil fuer Stimmen, die aus diesem Raum kommen. Badezimmer viel, Teppichflur wenig.")]
        private float reverb = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Grunddaempfung innerhalb des Raums. Ein Raum voller Vorhaenge schluckt Klang.")]
        private float absorption = 0f;

        [Header("Klang ueber die Zonengrenze hinweg")]
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Lautstaerke uebrig bleibt, wenn Sprecher und Zuhoerer in verschiedenen Zonen sind.")]
        private float crossZoneVolume = 0.5f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie dumpf es ueber die Zonengrenze hinweg klingt. 0 = unveraendert, 1 = maximal dumpf.")]
        private float crossZoneMuffle = 0.4f;

        public string ZoneName => zoneName;
        public float Reverb => reverb;
        public float Absorption => absorption;
        public float CrossZoneVolume => crossZoneVolume;
        public float CrossZoneMuffle => crossZoneMuffle;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            zoneName = gameObject.name;
        }

        private void OnValidate()
        {
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                ProxLog.Warn(
                    $"VoiceZone '{name}' braucht einen Collider mit aktiviertem 'Is Trigger', " +
                    "sonst kann Earshot nicht erkennen, wer sich im Raum befindet.");
            }
        }

        /// <summary>
        /// Ermittelt die Zone an einer Weltposition. Bei ueberlappenden Zonen gewinnt die
        /// mit dem kleinsten Volumen, weil das in der Praxis der spezifischere Raum ist:
        /// Ein Schrank im Zimmer soll den Schrank ergeben, nicht das Zimmer.
        /// </summary>
        public static VoiceZone FindAt(Vector3 position, LayerMask layers, Collider[] buffer)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, 0.01f, buffer, layers, QueryTriggerInteraction.Collide);

            VoiceZone best = null;
            float bestSize = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var zone = buffer[i].GetComponentInParent<VoiceZone>();
                if (zone == null) continue;

                Vector3 extents = buffer[i].bounds.size;
                float size = extents.x * extents.y * extents.z;
                if (size < bestSize)
                {
                    bestSize = size;
                    best = zone;
                }
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.31f, 0.66f, 0.87f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
            Gizmos.color = new Color(0.31f, 0.66f, 0.87f, 0.9f);
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}
