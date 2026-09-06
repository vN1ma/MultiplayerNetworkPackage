using System.Threading.Tasks;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Die einzige Pflichtkomponente von Earshot Voice. Auf jeden spielbaren Charakter
    /// setzen (lokal und remote) - mehr Verdrahtung braucht es im Standardfall nicht.
    /// <para>
    /// <b>Zero-Config:</b> Ohne jede weitere Einstellung gilt dieser Avatar als der eigene
    /// (<see cref="IsLocalPlayer"/> = wahr), und die Spieler-ID wird automatisch aus
    /// <see cref="EarshotVoice.LocalPlayerId"/> uebernommen, sobald die Anmeldung bei Unity
    /// Services durch ist. Das ist fuer ein Spiel ohne eigenes Multiplayer-Framework bereits
    /// die vollstaendige Einrichtung.
    /// </para>
    /// <para>
    /// <b>Advanced:</b> Ein eigener Netzwerk-Adapter (z.B. fuer Netcode, Mirror, Photon)
    /// ruft <see cref="Bind"/> auf, sobald die echte Identitaet feststeht - typischerweise
    /// "lokal = Owner, ID = die eigene UGS-PlayerId" auf dem Besitzer und "lokal = falsch,
    /// ID = die synchronisierte PlayerId" auf allen anderen. Das ueberschreibt den
    /// Zero-Config-Zustand vollstaendig; die Inspector-Felder lassen sich fuer denselben
    /// Zweck auch manuell setzen, wenn kein Adapter existiert.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Proximity Voice")]
    [DisallowMultipleComponent]
    public class EarshotProximityVoice : MonoBehaviour, IProximityVoicePlayer
    {
        [Header("Zero-Config (Standard)")]
        [SerializeField]
        [Tooltip("Ohne Netzwerk-Adapter: Dieser Avatar ist der eigene. Ein Adapter (z.B. NetcodeVoicePlayer) ueberschreibt das automatisch per Bind().")]
        private bool isLocalPlayer = true;

        [Header("Advanced (optional, ueberschreibt Zero-Config)")]
        [SerializeField]
        [Tooltip("Mund/Kopf. Stimmen werden an diesen Punkt gehaengt. Leer = dieses Transform.")]
        private Transform voiceAnchor;

        [SerializeField]
        [Tooltip("Unity Authentication PlayerId. Muss mit der Vivox-Teilnehmer-ID uebereinstimmen. Leer = wird bei isLocalPlayer automatisch befuellt, sobald die Anmeldung durch ist.")]
        private string playerId;

        [SerializeField]
        [Tooltip("Nur fuer Logs. Kann der Anzeigename aus dem Spiel sein.")]
        private string displayName;

        private bool waitingForLocalIdentity;

        public Transform VoiceAnchor => voiceAnchor != null ? voiceAnchor : transform;
        public Vector3 Position => VoiceAnchor.position;
        public bool IsLocalPlayer => isLocalPlayer;
        public string PlayerId => playerId;
        public bool HasIdentity => !string.IsNullOrEmpty(playerId);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        /// Verbindet diesen Avatar mit einer Spieler-ID. Von einem Netzwerk-Adapter
        /// aufrufen, sobald die Identitaet bekannt ist (Spawn/Sync). Ersetzt den
        /// Zero-Config-Zustand vollstaendig.
        /// </summary>
        public void Bind(string ugsPlayerId, bool local, string playerDisplayName = null)
        {
            playerId = ugsPlayerId ?? string.Empty;
            isLocalPlayer = local;
            waitingForLocalIdentity = false;
            if (!string.IsNullOrWhiteSpace(playerDisplayName)) displayName = playerDisplayName;

            VoiceRoster.Register(this);
            if (HasIdentity) VoiceRoster.NotifyIdentityReady(this);
        }

        public void SetVoiceAnchor(Transform anchor)
        {
            voiceAnchor = anchor;
        }

        private void Reset()
        {
            EnsureVoiceAnchor();
        }

        private void OnValidate()
        {
            EnsureVoiceAnchor();
        }

        private void OnEnable()
        {
            EnsureVoiceAnchor();
            VoiceRoster.Register(this);

            if (HasIdentity)
            {
                VoiceRoster.NotifyIdentityReady(this);
            }
            else if (isLocalPlayer && Application.isPlaying)
            {
                // Zero-Config: die eigene PlayerId kommt erst, sobald Unity Services
                // angemeldet ist. Bis dahin ohne Identitaet weiterlaufen - der Fallback
                // in VoiceRuntime bindet eingehende Stimmen trotzdem an registrierte,
                // nicht-lokale Avatare.
                waitingForLocalIdentity = true;
                _ = WaitForLocalIdentityAsync();
            }
        }

        private void OnDisable()
        {
            waitingForLocalIdentity = false;
            VoiceRoster.Unregister(this);
        }

        private async Task WaitForLocalIdentityAsync()
        {
            const int pollMs = 100;
            const int timeoutMs = 15000;
            int waited = 0;

            while (waitingForLocalIdentity && waited < timeoutMs)
            {
                string id = EarshotVoice.LocalPlayerId;
                if (!string.IsNullOrEmpty(id))
                {
                    playerId = id;
                    waitingForLocalIdentity = false;
                    VoiceRoster.NotifyIdentityReady(this);
                    return;
                }

                await Task.Delay(pollMs);
                waited += pollMs;
            }
        }

        private void EnsureVoiceAnchor()
        {
            if (voiceAnchor != null) return;

            var head = transform.Find("Head") ?? transform.Find("Camera");
            if (head != null) voiceAnchor = head;
        }
    }
}
