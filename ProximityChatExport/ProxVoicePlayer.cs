using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Auf jeden spielbaren Charakter setzen (lokal und remote). Die Sprachschicht
    /// braucht nur zwei Dinge: ob das der eigene Avatar ist, und die Unity-Auth-ID,
    /// die Vivox fuer denselben Menschen verwendet.
    /// </summary>
    [AddComponentMenu("Earshot Proximity/Voice Player")]
    [DisallowMultipleComponent]
    public class ProxVoicePlayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Mund/Kopf. Stimmen werden an diesen Punkt gehaengt. Leer = dieses Transform.")]
        private Transform voiceAnchor;

        [SerializeField]
        [Tooltip("Nur auf dem eigenen Charakter aktivieren, nicht auf fremden Avataren.")]
        private bool isLocalPlayer;

        [SerializeField]
        [Tooltip("Unity Authentication PlayerId. Muss mit der Vivox-Teilnehmer-ID uebereinstimmen.")]
        private string playerId;

        [SerializeField]
        [Tooltip("Nur fuer Logs. Kann der Anzeigename aus dem Spiel sein.")]
        private string displayName;

        public Transform VoiceAnchor => voiceAnchor != null ? voiceAnchor : transform;
        public bool IsLocalPlayer => isLocalPlayer;
        public string PlayerId => playerId;
        public bool HasIdentity => !string.IsNullOrEmpty(playerId);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        /// Verbindet diesen Avatar mit einer Spieler-ID. Vom eigenen Netzcode aufrufen,
        /// sobald die ID bekannt ist (Spawn / Sync).
        /// </summary>
        public void Bind(string ugsPlayerId, bool local, string playerDisplayName = null)
        {
            playerId = ugsPlayerId ?? string.Empty;
            isLocalPlayer = local;
            if (!string.IsNullOrWhiteSpace(playerDisplayName)) displayName = playerDisplayName;

            ProxVoiceRoster.Register(this);
            if (HasIdentity) ProxVoiceRoster.NotifyIdentityReady(this);
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
            ProxVoiceRoster.Register(this);
            if (HasIdentity) ProxVoiceRoster.NotifyIdentityReady(this);
        }

        private void OnDisable()
        {
            ProxVoiceRoster.Unregister(this);
        }

        private void EnsureVoiceAnchor()
        {
            if (voiceAnchor != null) return;

            var head = transform.Find("Head") ?? transform.Find("Camera");
            if (head != null) voiceAnchor = head;
        }
    }
}
