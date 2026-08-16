using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Das Herz der Sprachschicht: verbindet eingehende Stimmen mit den passenden Avataren
    /// und laesst in festem Takt die Pipeline darauf laufen.
    /// <para>
    /// Diese Komponente entsteht von selbst, sobald eine Sitzung startet, und verschwindet
    /// nie wieder. Sie muss in keiner Szene liegen und in keinem Prefab stehen.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-50)]
    public class VoiceRuntime : MonoBehaviour
    {
        private static VoiceRuntime instance;

        private readonly VoicePipeline pipeline = new VoicePipeline();
        private readonly List<VoiceEmitter> emitters = new List<VoiceEmitter>();
        private readonly Dictionary<string, VoiceEmitter> byPlayerId =
            new Dictionary<string, VoiceEmitter>(System.StringComparer.OrdinalIgnoreCase);

        private IVoiceBackend backend;
        private AudioListener listener;
        private float nextEvaluation;
        private float lastEvaluation;

        internal static VoiceRuntime Instance => instance;

        /// <summary>Das aktive Backend. Null, solange keine Sitzung laeuft.</summary>
        public IVoiceBackend Backend => backend;

        /// <summary>Alle aktuell empfangenen Stimmen.</summary>
        public IReadOnlyList<VoiceEmitter> Emitters => emitters;

        /// <summary>
        /// Ersetzt den Ort, an dem zugehoert wird. Siehe <see cref="CoopVoice.ListenerOverride"/>.
        /// </summary>
        internal Transform ListenerOverride { get; set; }

        internal static VoiceRuntime EnsureExists()
        {
            if (instance != null) return instance;

            var go = new GameObject("Earshot Voice");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.NotEditable;

            instance = go.AddComponent<VoiceRuntime>();
            return instance;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        internal void AttachBackend(IVoiceBackend value)
        {
            DetachBackend();

            backend = value;
            backend.SpeakerAdded += OnSpeakerAdded;
            backend.SpeakerRemoved += OnSpeakerRemoved;

            PlayerRegistry.IdentityReady += OnIdentityReady;
        }

        internal void DetachBackend()
        {
            PlayerRegistry.IdentityReady -= OnIdentityReady;

            if (backend != null)
            {
                backend.SpeakerAdded -= OnSpeakerAdded;
                backend.SpeakerRemoved -= OnSpeakerRemoved;
                backend = null;
            }

            // Nur echte Sprecher entfernen. Debug-Stimmen bleiben, sonst waere der
            // Testraum nach einem Disconnect stumm.
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                var emitter = emitters[i];
                if (emitter != null && emitter.PlayerId != null &&
                    emitter.PlayerId.StartsWith("debug:", System.StringComparison.Ordinal))
                {
                    continue;
                }

                emitters.RemoveAt(i);
            }

            byPlayerId.Clear();
        }

        /// <summary>
        /// Nimmt eine Teststimme auf, die nicht von Vivox kommt. Bleibt bestehen, wenn
        /// das Backend getrennt wird, damit der Testraum ohne Account funktioniert.
        /// </summary>
        internal void RegisterDebugEmitter(VoiceEmitter emitter)
        {
            if (emitter == null) return;
            if (!emitters.Contains(emitter)) emitters.Add(emitter);
        }

        internal void UnregisterDebugEmitter(VoiceEmitter emitter)
        {
            if (emitter == null) return;
            emitters.Remove(emitter);
        }

        private void OnSpeakerAdded(VoiceSpeaker speaker)
        {
            if (speaker?.Source == null) return;
            if (byPlayerId.ContainsKey(speaker.PlayerId)) return;

            var profile = CoopSettings.Instance.VoiceProfile;
            var emitter = speaker.Source.gameObject.AddComponent<VoiceEmitter>();
            emitter.Initialize(speaker.PlayerId, speaker.Source, profile);

            TryBindEmitter(emitter);

            emitters.Add(emitter);
            byPlayerId[speaker.PlayerId] = emitter;
        }

        private void OnSpeakerRemoved(string playerId)
        {
            if (!byPlayerId.TryGetValue(playerId, out var emitter)) return;

            byPlayerId.Remove(playerId);
            emitters.Remove(emitter);

            // Nur die eigene Komponente entfernen. Das GameObject gehoert dem Backend,
            // und bei Vivox raeumt es der Dienst selbst ab.
            if (emitter != null) Destroy(emitter);
        }

        private void OnIdentityReady(CoopPlayer player)
        {
            if (player == null || !player.HasIdentity) return;

            if (byPlayerId.TryGetValue(player.UgsPlayerId, out var emitter) && emitter != null)
            {
                emitter.Anchor = player.VoiceAnchor;
                CoopLog.Info($"Stimme von {player.DisplayName} ihrem Avatar zugeordnet.");
                return;
            }

            for (int i = 0; i < emitters.Count; i++)
            {
                TryBindEmitter(emitters[i]);
            }
        }

        /// <summary>
        /// Bindet eine Stimme an den passenden Avatar. Bei zwei Spielern reicht
        /// "der andere", auch wenn die IDs nicht exakt gleich geschrieben sind.
        /// </summary>
        private void TryBindEmitter(VoiceEmitter emitter)
        {
            if (emitter == null) return;
            if (emitter.PlayerId != null &&
                emitter.PlayerId.StartsWith("debug:", System.StringComparison.Ordinal))
            {
                return;
            }

            if (PlayerRegistry.TryGetByUgsId(emitter.PlayerId, out var matched) &&
                matched != null && !matched.IsLocalPlayer)
            {
                emitter.Anchor = matched.VoiceAnchor;
                return;
            }

            if (emitter.Anchor != null) return;

            CoopPlayer fallback = null;
            var players = PlayerRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.IsLocalPlayer) continue;
                fallback = player;
                if (!IsPlayerBound(player)) break;
            }

            if (fallback != null)
            {
                emitter.Anchor = fallback.VoiceAnchor;
                CoopLog.Warn(
                    $"Stimme '{emitter.PlayerId}' an {fallback.DisplayName} gebunden " +
                    $"(UGS '{fallback.UgsPlayerId}').");
            }
        }

        private bool IsPlayerBound(CoopPlayer player)
        {
            if (player == null) return false;
            Transform anchor = player.VoiceAnchor;
            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter != null && emitter.Anchor == anchor) return true;
            }

            return false;
        }

        private void Update()
        {
            if (emitters.Count == 0) return;

            var profile = CoopSettings.Instance.VoiceProfile;
            float interval = profile != null ? profile.EvaluationInterval : 1f / 15f;

            if (Time.unscaledTime < nextEvaluation) return;

            float delta = Time.unscaledTime - lastEvaluation;
            lastEvaluation = Time.unscaledTime;
            nextEvaluation = Time.unscaledTime + interval;

            if (!TryGetListenerPosition(out Vector3 listenerPosition)) return;

            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null) continue;

                if (emitter.Anchor == null)
                {
                    TryBindEmitter(emitter);
                }

                if (emitter.Anchor == null)
                {
                    // Lieber flach hoeren als stumm bleiben, bis der Avatar da ist.
                    var audible = VoiceSample.Default;
                    audible.SpatialBlend = 0f;
                    emitter.SetTarget(in audible);
                    continue;
                }

                var sample = pipeline.Evaluate(
                    profile,
                    listenerPosition,
                    emitter.Anchor.position,
                    delta,
                    out var context);

                emitter.LastContext = context;
                emitter.SetTarget(in sample);
            }
        }

        private bool TryGetListenerPosition(out Vector3 position)
        {
            // Ein gesetztes Override gewinnt immer. Das ist der Weg fuer Zuschauerkameras
            // und aehnliche Faelle, in denen man nicht dort hoeren soll, wo die Kamera steht.
            if (ListenerOverride != null)
            {
                position = ListenerOverride.position;
                return true;
            }

            // Sobald ein eigener Avatar da ist, zaehlt der Kopf - nicht irgendein
            // AudioListener in der Szene. Die Lobbykamera bleibt sonst oft das "Ohr",
            // auch wenn ihre Komponente schon aus ist, und der Klang aendert sich beim
            // Laufen nicht.
            var local = PlayerRegistry.LocalPlayer;
            if (local != null)
            {
                var anchor = local.VoiceAnchor;
                if (anchor != null)
                {
                    position = anchor.position;
                    return true;
                }
            }

            if (listener == null || !listener.isActiveAndEnabled)
            {
                listener = FindListener();
            }

            if (listener == null || !listener.isActiveAndEnabled)
            {
                position = default;
                return false;
            }

            position = listener.transform.position;
            return true;
        }

        private static AudioListener FindListener()
        {
            // FindObjectsByType findet auch Listener, deren Komponente aus ist, solange
            // das GameObject aktiv bleibt. Genau das macht die Lobbykamera: Sie schaltet
            // nur den Listener ab. Ohne diese Filterung wuerde Earshot den Schall weiter
            // von der Lobby aus rechnen, egal wo der Spieler steht.
#if UNITY_6000_5_OR_NEWER
            var found = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#else
            var found = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            var local = PlayerRegistry.LocalPlayer;
            if (local != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    var candidate = found[i];
                    if (candidate != null && candidate.isActiveAndEnabled &&
                        candidate.transform.IsChildOf(local.transform))
                    {
                        return candidate;
                    }
                }
            }

            for (int i = 0; i < found.Length; i++)
            {
                var candidate = found[i];
                if (candidate != null && candidate.isActiveAndEnabled) return candidate;
            }

            return null;
        }
    }
}
