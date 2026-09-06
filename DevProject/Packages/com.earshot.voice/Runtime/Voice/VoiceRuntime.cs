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
        private readonly Dictionary<VoiceSpeakerKey, VoiceEmitter> byKey =
            new Dictionary<VoiceSpeakerKey, VoiceEmitter>();
        private readonly Dictionary<string, VoiceEmitter> byPlayerId =
            new Dictionary<string, VoiceEmitter>(System.StringComparer.OrdinalIgnoreCase);
        private readonly WalkieTalkArbitration radioArbitration = new WalkieTalkArbitration();
        private readonly List<string> radioChannelScratch = new List<string>(4);

        private IVoiceBackend backend;
        private AudioListener listener;
        private float nextEvaluation;
        private float lastEvaluation;

        // Selbstheilung fuer haengende Taps (siehe IVoiceBackendRecovery). Bewusst hier
        // und nicht im Backend: nur hier kennen wir TapIsPlaying, den einzigen Wert, der
        // wirklich beweist, dass ECHTES Audio ankommt - AudioSource.isPlaying nicht.
        private readonly Dictionary<string, float> deadSince =
            new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> lastRecoveryAttempt =
            new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        private readonly List<VoiceEmitter> stuckCheckBuffer = new List<VoiceEmitter>();

        // TapIsPlaying braucht selbst schon bis zu 0,6s ohne Signal, um "tot" zu melden.
        // Diese zusaetzliche Wartezeit kommt oben drauf, bevor wir eingreifen.
        private const float StuckSeconds = 1.2f;

        // Verhindert eine Neuaufbau-Schleife, falls die Zustellung fuer diesen
        // Teilnehmer dauerhaft gestoert ist (z.B. echtes Netzwerkproblem).
        private const float RecoveryCooldownSeconds = 6f;

        internal static VoiceRuntime Instance => instance;

        /// <summary>Das aktive Backend. Null, solange keine Sitzung laeuft.</summary>
        public IVoiceBackend Backend => backend;

        /// <summary>Alle aktuell empfangenen Stimmen.</summary>
        public IReadOnlyList<VoiceEmitter> Emitters => emitters;

        /// <summary>
        /// Ersetzt den Ort, an dem zugehoert wird. Siehe <see cref="EarshotVoice.ListenerOverride"/>.
        /// </summary>
        internal Transform ListenerOverride { get; set; }

        internal static VoiceRuntime EnsureExists()
        {
            if (instance != null) return instance;

            var go = new GameObject("Earshot Voice Runtime");
            DontDestroyOnLoad(go);

            instance = go.AddComponent<VoiceRuntime>();
            WalkieRadioSync.EnsureOn(instance);
            WalkieSidetoneCapture.EnsureOn(instance);
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

            VoiceRoster.IdentityReady += OnIdentityReady;
            WalkieRadioSync.EnsureOn(this);
            WalkieSidetoneCapture.EnsureOn(this);
            WalkieTalkieRegistry.NotifyChanged();
        }

        internal void DetachBackend()
        {
            VoiceRoster.IdentityReady -= OnIdentityReady;

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

            byKey.Clear();
            byPlayerId.Clear();
            deadSince.Clear();
            lastRecoveryAttempt.Clear();
            radioArbitration.ClearAll();
            WalkieRadioBus.ClearAll();
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

            var key = new VoiceSpeakerKey(speaker.PlayerId, speaker.PathKind, speaker.ChannelId);
            if (byKey.ContainsKey(key)) return;

            var profile = EarshotVoiceSettings.Instance.VoiceProfile;
            var emitter = speaker.Source.gameObject.AddComponent<VoiceEmitter>();
            emitter.Initialize(
                speaker.PlayerId,
                speaker.Source,
                profile,
                speaker.PathKind,
                speaker.ChannelId);

            if (speaker.PathKind == VoicePathKind.Radio)
            {
                WalkieTalkieRegistry.MarkRadioSpeaker(speaker.PlayerId, true);
            }
            else
            {
                TryBindEmitter(emitter);
                byPlayerId[speaker.PlayerId] = emitter;
            }

            emitters.Add(emitter);
            byKey[key] = emitter;
            VoiceSessionLog.Note(
                speaker.PathKind == VoicePathKind.Radio
                    ? $"TAP Funk an: {speaker.PlayerId} @{speaker.ChannelId}"
                    : $"TAP an: {speaker.PlayerId}");
        }

        private void OnSpeakerRemoved(VoiceSpeakerKey key)
        {
            if (!byKey.TryGetValue(key, out var emitter)) return;

            byKey.Remove(key);
            emitters.Remove(emitter);

            if (key.PathKind == VoicePathKind.Proximity &&
                byPlayerId.TryGetValue(key.PlayerId, out var mapped) &&
                mapped == emitter)
            {
                byPlayerId.Remove(key.PlayerId);
            }

            if (key.PathKind == VoicePathKind.Radio)
            {
                radioArbitration.SetSpeaking(key.ChannelId, key.PlayerId, false, Time.unscaledTime);

                bool stillRadio = false;
                for (int i = 0; i < emitters.Count; i++)
                {
                    var e = emitters[i];
                    if (e != null &&
                        e.PathKind == VoicePathKind.Radio &&
                        string.Equals(e.PlayerId, key.PlayerId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        stillRadio = true;
                        break;
                    }
                }

                if (!stillRadio) WalkieTalkieRegistry.MarkRadioSpeaker(key.PlayerId, false);
            }

            VoiceSessionLog.Note(
                key.PathKind == VoicePathKind.Radio
                    ? $"TAP Funk weg: {key.PlayerId} @{key.ChannelId}"
                    : $"TAP weg: {key.PlayerId}");

            if (emitter != null) Destroy(emitter);
        }

        private void OnIdentityReady(IProximityVoicePlayer player)
        {
            if (player == null || !player.HasIdentity) return;

            if (byPlayerId.TryGetValue(player.PlayerId, out var emitter) && emitter != null)
            {
                emitter.Anchor = player.VoiceAnchor;
                EarshotVoiceLog.Info($"Stimme von {player.DisplayName} ihrem Avatar zugeordnet.");
                VoiceSessionLog.Note($"AVATAR: Stimme von {player.DisplayName} am Kopf.");
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
            if (emitter.PathKind == VoicePathKind.Radio) return;
            if (emitter.PlayerId != null &&
                emitter.PlayerId.StartsWith("debug:", System.StringComparison.Ordinal))
            {
                return;
            }

            if (VoiceRoster.TryGetByPlayerId(emitter.PlayerId, out var matched) &&
                matched != null && !matched.IsLocalPlayer)
            {
                emitter.Anchor = matched.VoiceAnchor;
                VoiceSessionLog.Note($"AVATAR: Stimme {emitter.PlayerId} -> {matched.DisplayName}");
                return;
            }

            if (emitter.Anchor != null) return;

            IProximityVoicePlayer fallback = null;
            var players = VoiceRoster.Players;
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
                EarshotVoiceLog.Warn(
                    $"Stimme '{emitter.PlayerId}' an {fallback.DisplayName} gebunden " +
                    $"(ID '{fallback.PlayerId}').");
                VoiceSessionLog.Note(
                    $"AVATAR (Fallback): {emitter.PlayerId} -> {fallback.DisplayName}");
            }
        }

        private bool IsPlayerBound(IProximityVoicePlayer player)
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
            // Laeuft unabhaengig vom Auswertungstakt unten - ein haengen gebliebener
            // Tap soll nicht erst auf die naechste raeumliche Neuberechnung warten.
            if (backend is IVoiceBackendRecovery recovery)
            {
                PollStuckTaps(recovery);
            }

            if (emitters.Count == 0) return;

            var profile = EarshotVoiceSettings.Instance.VoiceProfile;
            float interval = profile != null ? profile.EvaluationInterval : 1f / 15f;

            if (Time.unscaledTime < nextEvaluation) return;

            float delta = Time.unscaledTime - lastEvaluation;
            lastEvaluation = Time.unscaledTime;
            nextEvaluation = Time.unscaledTime + interval;

            if (!TryGetListenerPosition(out Vector3 listenerPosition))
            {
                // Ohne Ohr nicht stumm bleiben - sonst bleibt der Tap bei Lautstaerke 0.
                for (int i = 0; i < emitters.Count; i++)
                {
                    var waiting = emitters[i];
                    if (waiting == null) continue;
                    var audible = VoiceSample.Default;
                    audible.SpatialBlend = 0f;
                    audible.Volume = 0.55f;
                    waiting.SetTarget(in audible);
                }

                return;
            }

            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null) continue;

                if (emitter.PathKind == VoicePathKind.Radio)
                {
                    EvaluateRadioEmitter(emitter, listenerPosition);
                    continue;
                }

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

                var sourceColor = VoiceSourceColor.Find(emitter.Anchor);
                if (sourceColor != null)
                {
                    sourceColor.Apply(ref sample);
                    if (profile != null)
                    {
                        profile.ApplyHearingLimits(context.HearingDistance, ref sample);
                    }

                    sample.Clamp();
                }

                // Funk ersetzt Mund: Proximity leiser, solange derselbe Sprecher funkt.
                bool onRadio = HasActiveRadioSpeech(emitter.PlayerId);
                float dampening = WalkieTalkieRegistry.ActiveMouthDampening;
                emitter.VolumeScale = WalkieRules.MouthVolumeScale(onRadio, dampening);

                emitter.LastContext = context;
                emitter.SetTarget(in sample);
            }

            PublishRadioWinners();
        }

        private void EvaluateRadioEmitter(VoiceEmitter emitter, Vector3 listenerPosition)
        {
            // Vivox-Tap bleibt stumm — Wiedergabe nur an WalkieDeviceOutput (alle Geraete).
            radioArbitration.SetSpeaking(
                emitter.ChannelId,
                emitter.PlayerId,
                emitter.TapIsPlaying,
                Time.unscaledTime);

            var silent = VoiceSample.Default;
            silent.Muted = true;
            silent.Volume = 0f;
            silent.SpatialBlend = 0f;
            emitter.VolumeScale = 0f;
            emitter.Anchor = WalkieTalkieRegistry.GetBestReceiveAnchor(
                emitter.ChannelId, listenerPosition);
            emitter.SetRadioDelaySeconds(0f);
            emitter.LastContext = new VoiceContext
            {
                ListenerPosition = listenerPosition,
                SpeakerPosition = emitter.Anchor != null ? emitter.Anchor.position : listenerPosition,
                ApparentPosition = emitter.Anchor != null ? emitter.Anchor.position : listenerPosition,
                PortalOpenness = 1f
            };
            emitter.SetTarget(in silent);
        }

        private void PublishRadioWinners()
        {
            radioChannelScratch.Clear();

            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                if (e == null || e.PathKind != VoicePathKind.Radio) continue;
                AddUniqueChannel(radioChannelScratch, e.ChannelId);
            }

            var devices = WalkieTalkieRegistry.Devices;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || !d.PoweredOn) continue;
                AddUniqueChannel(radioChannelScratch, d.ChannelId);
            }

            for (int i = 0; i < radioChannelScratch.Count; i++)
            {
                string channelId = radioChannelScratch[i];
                string winner = radioArbitration.GetWinnerPlayerId(channelId);

                // Waehrend lokalem PTT auf demselben Kanal: kein Fremdempfang (Half-Duplex).
                if (WalkieTalkieRegistry.LocalIsTransmitting &&
                    string.Equals(
                        WalkieTalkieRegistry.LocalTransmitChannelId,
                        channelId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    WalkieRadioBus.SetAudibleRemote(channelId, null);
                }
                else
                {
                    WalkieRadioBus.SetAudibleRemote(channelId, winner);
                }
            }
        }

        private static void AddUniqueChannel(List<string> list, string channelId)
        {
            if (string.IsNullOrEmpty(channelId) || list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], channelId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            list.Add(channelId);
        }

        private bool HasActiveRadioSpeech(string playerId)
        {
            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                if (e == null || e.PathKind != VoicePathKind.Radio) continue;
                if (!string.Equals(e.PlayerId, playerId, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (e.TapIsPlaying) return true;
            }

            return false;
        }

        /// <summary>
        /// Prueft jeden Frame den echten Signalpegel jedes Taps (<see cref="VoiceEmitter.TapIsPlaying"/>).
        /// Bleibt der laenger als <see cref="StuckSeconds"/> tot, WAEHREND der Dienst den
        /// Sprecher noch als aktiv redend meldet, ist das kein normales Sprechpausen-Schweigen
        /// mehr, sondern ein haengen gebliebener Empfang - dann baut das Backend den Tap neu auf.
        /// </summary>
        private void PollStuckTaps(IVoiceBackendRecovery recovery)
        {
            float now = Time.unscaledTime;

            // Erst eine Momentaufnahme, dann erst heilen: RecoverSpeaker() entfernt und
            // erzeugt Emitter synchron neu, wuerde also die Original-Liste mitten in der
            // Schleife veraendern.
            stuckCheckBuffer.Clear();
            stuckCheckBuffer.AddRange(emitters);

            for (int i = 0; i < stuckCheckBuffer.Count; i++)
            {
                var emitter = stuckCheckBuffer[i];
                if (emitter == null || string.IsNullOrEmpty(emitter.PlayerId)) continue;
                if (emitter.PlayerId.StartsWith("debug:", System.StringComparison.Ordinal)) continue;

                string playerId = emitter.PlayerId;

                if (emitter.TapIsPlaying)
                {
                    deadSince.Remove(playerId);
                    continue;
                }

                if (!deadSince.TryGetValue(playerId, out float since))
                {
                    deadSince[playerId] = now;
                    continue;
                }

                if (now - since < StuckSeconds) continue;

                // Redet der Dienst zufolge gerade niemand, ist die Stille normal
                // (Sprechpause) - dann gibt es nichts zu heilen.
                if (!recovery.IsSpeaking(playerId)) continue;

                lastRecoveryAttempt.TryGetValue(playerId, out float lastTry);
                if (now - lastTry < RecoveryCooldownSeconds) continue;

                lastRecoveryAttempt[playerId] = now;
                deadSince.Remove(playerId);
                recovery.RecoverSpeaker(playerId);
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
            var local = VoiceRoster.LocalPlayer;
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

            var local = VoiceRoster.LocalPlayer;
            if (local != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    var candidate = found[i];
                    if (candidate != null && candidate.isActiveAndEnabled &&
                        candidate.transform.IsChildOf(local.VoiceAnchor))
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
