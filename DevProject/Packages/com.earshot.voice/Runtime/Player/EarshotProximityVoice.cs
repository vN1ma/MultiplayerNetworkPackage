using System.Threading.Tasks;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Die einzige Pflichtkomponente von Earshot Voice. Auf jeden spielbaren Charakter
    /// setzen (lokal und remote). Verbindung, Besitz und Stimmen-Zuordnung laufen
    /// automatisch — kein <c>ConnectAsync</c> und kein <c>Bind</c> im Spielcode.
    /// </summary>
    [AddComponentMenu("Earshot Voice/Proximity Voice")]
    [DisallowMultipleComponent]
    public class EarshotProximityVoice : MonoBehaviour, IProximityVoicePlayer
    {
        [Header("Zero-Config (Standard)")]
        [SerializeField]
        [Tooltip("Fallback, wenn kein Netzwerk-Objekt am Avatar haengt. Mit Netcode/Mirror/Photon setzt die Komponente das selbst.")]
        private bool isLocalPlayer = true;

        [Header("Advanced (optional)")]
        [SerializeField]
        [Tooltip("Mund/Kopf. Leer = Kind Head/Camera oder dieses Transform.")]
        private Transform voiceAnchor;

        [SerializeField]
        [Tooltip("Leer lassen. Wird automatisch gefuellt.")]
        private string playerId;

        [SerializeField]
        [Tooltip("Nur fuer Logs.")]
        private string displayName;

        [SerializeField]
        [Tooltip("Leer = Unity-Lobby-ID, sonst der Kanal aus den Settings, sonst 'earshot'. Nur setzen, wenn ihr einen festen Kanal wollt.")]
        private string channelName;

        [SerializeField]
        [Tooltip("Aus = nur zuhoeren (Hoertest), kein Vivox-Kanal.")]
        private bool joinVoiceChannel = true;

        private bool waitingForLocalIdentity;
        private bool identityLocked;
        private bool startedSession;
        private int runId;

        public Transform VoiceAnchor => voiceAnchor != null ? voiceAnchor : transform;
        public Vector3 Position => VoiceAnchor.position;
        public bool IsLocalPlayer => isLocalPlayer;
        public string PlayerId => playerId;
        public bool HasIdentity => !string.IsNullOrEmpty(playerId);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        /// Advanced: Identitaet fest setzen. Im Standardweg unnoetig.
        /// </summary>
        public void Bind(string ugsPlayerId, bool local, string playerDisplayName = null)
        {
            identityLocked = true;
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

        /// <summary>Hoertest: kein Vivox, nur lokaler Pipeline-Klang.</summary>
        public void SetJoinVoiceChannel(bool value)
        {
            joinVoiceChannel = value;
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

            if (!Application.isPlaying)
            {
                VoiceRoster.Register(this);
                return;
            }

            runId++;
            _ = RunAsync(runId);
        }

        private void OnDisable()
        {
            runId++;
            waitingForLocalIdentity = false;
            VoiceRoster.Unregister(this);

            if (startedSession)
            {
                startedSession = false;
                _ = EarshotVoice.DisconnectAsync();
            }
        }

        private async Task RunAsync(int id)
        {
            try
            {
                await ResolveOwnershipAsync(id);
                if (!StillCurrent(id)) return;

                VoiceRoster.Register(this);

                if (isLocalPlayer) await BecomeLocalAndConnectAsync(id);
                else await WaitForRemoteIdentityAsync(id);
            }
            catch (System.Exception ex)
            {
                if (StillCurrent(id))
                {
                    EarshotVoiceLog.Exception("Proximity Voice: Start fehlgeschlagen", ex);
                }
            }
        }

        private async Task ResolveOwnershipAsync(int id)
        {
            if (identityLocked) return;

            const int spawnTimeoutMs = 8000;
            const int unconfirmedRemoteMs = 600;
            int waited = 0;

            while (StillCurrent(id) && waited < spawnTimeoutMs)
            {
                var ownership = NetworkOwnershipProbe.Read(gameObject);
                if (ownership.Status == NetworkOwnershipProbe.Status.Ready)
                {
                    ApplyOwnership(ownership.IsLocal);
                    return;
                }

                if (ownership.Status == NetworkOwnershipProbe.Status.None)
                {
                    return;
                }

                if (!ownership.WaitingForSpawn && waited >= unconfirmedRemoteMs)
                {
                    ApplyOwnership(false);
                    return;
                }

                await Task.Delay(50);
                waited += 50;
            }

            if (StillCurrent(id) && NetworkOwnershipProbe.Read(gameObject).Status != NetworkOwnershipProbe.Status.None)
            {
                ApplyOwnership(false);
            }
        }

        private void ApplyOwnership(bool local)
        {
            if (identityLocked) return;
            isLocalPlayer = local;
        }

        private async Task BecomeLocalAndConnectAsync(int id)
        {
            waitingForLocalIdentity = true;

            if (!joinVoiceChannel || !EarshotVoiceSettings.Instance.AutoConnect)
            {
                await WaitForLocalIdentityAsync(id);
                return;
            }

            string channel = await VoiceChannelResolver.ResolveAsync(channelName);
            if (!StillCurrent(id)) return;

            await EarshotVoice.ConnectAsync(channel, DisplayName);
            if (!StillCurrent(id))
            {
                await EarshotVoice.DisconnectAsync();
                return;
            }

            if (EarshotVoice.IsConnected) startedSession = true;

            if (string.IsNullOrEmpty(playerId))
            {
                playerId = EarshotVoice.LocalPlayerId;
            }

            waitingForLocalIdentity = false;
            VoiceRoster.Register(this);
            if (HasIdentity) VoiceRoster.NotifyIdentityReady(this);

            EarshotVoiceLog.Info(
                "Proximity Voice bereit. Kanal='" + channel +
                "'  lokal='" + playerId + "'");
        }

        private async Task WaitForLocalIdentityAsync(int id)
        {
            const int pollMs = 100;
            const int timeoutMs = 15000;
            int waited = 0;

            while (StillCurrent(id) && waitingForLocalIdentity && waited < timeoutMs)
            {
                string localId = EarshotVoice.LocalPlayerId;
                if (!string.IsNullOrEmpty(localId))
                {
                    playerId = localId;
                    waitingForLocalIdentity = false;
                    VoiceRoster.NotifyIdentityReady(this);
                    return;
                }

                await Task.Delay(pollMs);
                waited += pollMs;
            }
        }

        private async Task WaitForRemoteIdentityAsync(int id)
        {
            const int pollMs = 100;
            const int timeoutMs = 15000;
            int waited = 0;

            while (StillCurrent(id) && waited < timeoutMs)
            {
                if (TryApplyRemoteIdentity()) return;

                await Task.Delay(pollMs);
                waited += pollMs;
            }
        }

        private bool TryApplyRemoteIdentity()
        {
            if (HasIdentity)
            {
                VoiceRoster.NotifyIdentityReady(this);
                return true;
            }

            if (!NetworkOwnershipProbe.TryReadSyncedPlayerId(gameObject, out string synced))
            {
                return false;
            }

            if (string.Equals(synced, EarshotVoice.LocalPlayerId, System.StringComparison.Ordinal))
            {
                return false;
            }

            playerId = synced;
            VoiceRoster.Register(this);
            VoiceRoster.NotifyIdentityReady(this);
            return true;
        }

        private bool StillCurrent(int id)
        {
            return this && runId == id;
        }

        private void EnsureVoiceAnchor()
        {
            if (voiceAnchor != null) return;

            var head = transform.Find("Head") ?? transform.Find("Camera");
            if (head != null) voiceAnchor = head;
        }
    }
}
