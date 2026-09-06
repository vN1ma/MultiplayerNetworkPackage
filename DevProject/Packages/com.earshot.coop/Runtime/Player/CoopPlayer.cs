using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Macht ein Spielerobjekt fuer Earshot ansprechbar. Gehoert auf das Player-Prefab,
    /// zusammen mit einem <see cref="NetworkObject"/>.
    /// <para>
    /// Die Hauptaufgabe ist unscheinbar, aber zentral: Diese Komponente traegt die
    /// Unity-Gaming-Services-Spieler-ID ihres Besitzers ueber das Netzwerk. Genau diese ID
    /// nutzt auch Vivox, wodurch sich eine ankommende Stimme dem richtigen Avatar zuordnen
    /// laesst. Ohne diese Bruecke waere nicht feststellbar, wer da gerade spricht.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Coop Player")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class CoopPlayer : NetworkBehaviour
    {
        [Header("Stimme")]
        [SerializeField]
        [Tooltip("Woher die Stimme im Raum kommt, idealerweise in Kopfhoehe. Leer lassen nimmt dieses Objekt selbst.")]
        private Transform voiceAnchor;

        private readonly NetworkVariable<FixedString64Bytes> ugsPlayerId =
            new NetworkVariable<FixedString64Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<FixedString32Bytes> displayName =
            new NetworkVariable<FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private bool announcedIdentity;

        /// <summary>
        /// Anonyme Spieler-ID von Unity Gaming Services. Der gemeinsame Schluessel zwischen
        /// Netcode und Vivox. Kann kurz nach dem Spawn noch leer sein, bis der Wert
        /// vom Besitzer uebertragen wurde.
        /// </summary>
        public string UgsPlayerId => ugsPlayerId.Value.ToString();

        /// <summary>Anzeigename des Spielers.</summary>
        public string DisplayName
        {
            get
            {
                string value = displayName.Value.ToString();
                return string.IsNullOrEmpty(value) ? $"Spieler {OwnerClientId}" : value;
            }
        }

        /// <summary>Wahr, sobald die Identitaet uebertragen wurde und zugeordnet werden kann.</summary>
        public bool HasIdentity => !string.IsNullOrEmpty(UgsPlayerId);

        /// <summary>Position, aus der die Stimme dieses Spielers ertoent.</summary>
        public Transform VoiceAnchor => voiceAnchor != null ? voiceAnchor : transform;

        /// <summary>
        /// Setzt die Pose dieses Avatars. Auf dem Server entschieden, beim Besitzer
        /// ausgefuehrt - noetig, weil die Bewegung spaeter dem Besitzer gehoert.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            ApplyPose(position, rotation);

            if (IsSpawned && IsServer && !IsOwner)
            {
                TeleportOwnerRpc(position, rotation);
            }
        }

        [Rpc(SendTo.Owner)]
        private void TeleportOwnerRpc(Vector3 position, Quaternion rotation)
        {
            ApplyPose(position, rotation);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            var networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

            // NetworkTransform.Teleport() wirft auf der nicht-autoritativen Seite eine
            // Exception (bei Owner-Autoritaet ist das der Server, wenn er den Avatar eines
            // fremden Clients bewegt). Der Server ruft Teleport() trotzdem lokal auf, rein
            // als sofortige optische Vorschau, bis die TeleportOwnerRpc beim Besitzer
            // ankommt - dafuer reicht ein einfaches Transform-Setzen ohne die
            // Autoritaetspruefung von NetworkTransform.
            if (networkTransform != null && networkTransform.IsSpawned && networkTransform.CanCommitToTransform)
            {
                networkTransform.Teleport(position, rotation, transform.localScale);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            if (controller != null) controller.enabled = true;
        }

        // Ob dieser Avatar dem lokalen Spieler gehoert, beantwortet die geerbte
        // Eigenschaft IsLocalPlayer aus NetworkBehaviour bereits korrekt.

        public override void OnNetworkSpawn()
        {
            EnsureVoiceAnchor();

            if (IsOwner)
            {
                StartCoroutine(PublishIdentityWhenReady());
            }

            ugsPlayerId.OnValueChanged += OnIdentityChanged;

            PlayerRegistry.Register(this);
            TryAnnounceIdentity();
        }

        private IEnumerator PublishIdentityWhenReady()
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            while (string.IsNullOrEmpty(CoopServices.PlayerId) &&
                   !Coop.IsLocalSession &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            string id = CoopServices.PlayerId;
            if (string.IsNullOrEmpty(id) && Coop.IsLocalSession)
            {
                id = "local-" + OwnerClientId;
            }

            ugsPlayerId.Value = new FixedString64Bytes(Truncate(id, 61));
            displayName.Value = new FixedString32Bytes(Truncate(Coop.LocalPlayerName, 29));
        }

        public override void OnNetworkDespawn()
        {
            ugsPlayerId.OnValueChanged -= OnIdentityChanged;
            PlayerRegistry.Unregister(this);
            announcedIdentity = false;
        }

        private void OnIdentityChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            TryAnnounceIdentity();
        }

        /// <summary>
        /// Meldet die Identitaet genau einmal weiter, sobald sie tatsaechlich da ist.
        /// Notwendig, weil der Spawn und die Uebertragung der Netzwerkvariablen zwei
        /// getrennte Ereignisse sind und die Reihenfolge nicht garantiert ist.
        /// </summary>
        private void TryAnnounceIdentity()
        {
            if (announcedIdentity || !HasIdentity) return;

            announcedIdentity = true;
            PlayerRegistry.NotifyIdentityReady(this);
        }

        /// <summary>
        /// Kuerzt einen Text so, dass er in eine FixedString passt. Gemessen wird in Bytes,
        /// weil Umlaute in UTF-8 mehr als ein Byte belegen und ein zu langer Name sonst
        /// zur Laufzeit eine Ausnahme werfen wuerde.
        /// </summary>
        private static string Truncate(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var encoding = System.Text.Encoding.UTF8;
            if (encoding.GetByteCount(value) <= maxBytes) return value;

            int length = value.Length;
            while (length > 0 && encoding.GetByteCount(value.Substring(0, length)) > maxBytes)
            {
                length--;
            }

            return value.Substring(0, length);
        }

        private void OnValidate()
        {
            EnsureVoiceAnchor();
        }

        private void EnsureVoiceAnchor()
        {
            if (voiceAnchor != null) return;

            // Ein haeufiger Kopf-Kandidat, damit das Prefab ohne Handarbeit gut klingt.
            var head = transform.Find("Head") ?? transform.Find("Camera");
            if (head != null) voiceAnchor = head;
        }
    }
}
