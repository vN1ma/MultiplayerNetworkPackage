using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Sprachuebertragung ueber Unity Vivox — Proximity-Kanal plus optionale Funkkanaele.
    /// </summary>
    public class VivoxVoiceBackend : IVoiceBackend, IVoiceRadioBackend, IVoiceBackendRecovery
    {
        private readonly Dictionary<VoiceSpeakerKey, VivoxParticipant> participants =
            new Dictionary<VoiceSpeakerKey, VivoxParticipant>();

        private readonly HashSet<string> radioChannels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string proximityChannelName;
        private string transmittingRadioLogicalId;
        private bool initialized;
        private bool micMuted;

        /// <summary>
        /// v16-Diagnose-Schalter (default AUS): Schaltet die Vivox-native
        /// Wiedergabe nach dem Login stumm. Nur fuer die gezielte Gegenprobe
        /// der Vivox-native-These aktivieren — sie stoert F8/F7-Proben und
        /// stummt Teilnehmer ohne Tap. Siehe Begründung in EnsureLoggedInAsync
        /// und docs/walkie-talkie-debug-history.md (Abschnitt v16-Meta).
        /// </summary>
        public static bool DiagnosticMuteVivoxNativeOutputOnLogin = false;

        public string DisplayName => "Unity Vivox";

        public bool IsConnected => !string.IsNullOrEmpty(proximityChannelName);

        /// <summary>
        /// Name des Proximity-Kanals der aktiven Sitzung. Null, solange keine Sitzung laeuft.
        /// Dient dazu, den Sidetone-Capture-Tap stabil auf genau diesen Kanal zu pinnen,
        /// statt Vivox' Auto-Acquire auf den jeweils zuletzt gejointen Kanal umspringen zu lassen.
        /// </summary>
        public string ProximityChannelName => proximityChannelName;

        public event Action<VoiceSpeaker> SpeakerAdded;
        public event Action<VoiceSpeakerKey> SpeakerRemoved;

        public bool MicrophoneMuted
        {
            get => micMuted;
            set
            {
                if (micMuted == value) return;
                micMuted = value;

                if (!initialized) return;

                if (value) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();

                EarshotVoiceLog.Info(value ? "Mikrofon stummgeschaltet." : "Mikrofon aktiv.");
            }
        }

        public async Task ConnectAsync(string channel, string displayName)
        {
            if (string.IsNullOrEmpty(channel))
            {
                throw new ArgumentException("Kanalname ist leer.", nameof(channel));
            }

            await EnsureLoggedInAsync(displayName);

            VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;

            EarshotVoiceLog.Info($"Sprachkanal '{channel}' wird betreten.");

            await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, channel);

            proximityChannelName = channel;

            if (micMuted) VivoxService.Instance.MuteInputDevice();

            AttachExistingParticipants(channel);

            EarshotVoiceLog.Info(
                "Sprachkanal betreten. Eigene Vivox-ID: " +
                (VivoxService.Instance.SignedInPlayerId ?? "(leer)"));
        }

        public async Task DisconnectAsync()
        {
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;

            foreach (var participant in participants.Values)
            {
                if (participant != null)
                {
                    participant.ParticipantAudioStateChanged -= OnParticipantAudioReady;
                }
            }

            participants.Clear();
            radioChannels.Clear();
            transmittingRadioLogicalId = null;

            var toLeave = new List<string>();
            if (!string.IsNullOrEmpty(proximityChannelName)) toLeave.Add(proximityChannelName);

            foreach (var pair in VivoxService.Instance.ActiveChannels)
            {
                if (!toLeave.Contains(pair.Key)) toLeave.Add(pair.Key);
            }

            proximityChannelName = null;

            for (int i = 0; i < toLeave.Count; i++)
            {
                string leaving = toLeave[i];
                try
                {
                    await VivoxService.Instance.LeaveChannelAsync(leaving);
                    EarshotVoiceLog.Info($"Kanal verlassen: {leaving}");
                }
                catch (Exception ex)
                {
                    EarshotVoiceLog.Warn($"Kanal '{leaving}' konnte nicht sauber verlassen werden: {ex.Message}");
                }
            }
        }

        public bool IsRadioChannelJoined(string logicalChannelId)
        {
            string id = WalkieRules.SanitizeChannelId(logicalChannelId);
            return radioChannels.Contains(id);
        }

        public void CopyJoinedRadioChannels(List<string> into)
        {
            if (into == null) return;
            into.Clear();
            foreach (string id in radioChannels) into.Add(id);
        }

        public void CopyRadioChannelParticipantIds(string logicalChannelId, List<string> intoPlayerIds)
        {
            if (intoPlayerIds == null) return;
            intoPlayerIds.Clear();

            string id = WalkieRules.SanitizeChannelId(logicalChannelId);
            if (string.IsNullOrEmpty(id)) return;

            try
            {
                if (VivoxService.Instance == null ||
                    !VivoxService.Instance.ActiveChannels.TryGetValue(
                        WalkieRules.ToVivoxRadioChannel(id), out var list))
                {
                    return;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var participant = list[i];
                    if (participant == null) continue;
                    intoPlayerIds.Add(participant.IsSelf ? "ICH" : participant.PlayerId);
                }
            }
            catch (Exception)
            {
                // Diagnose-Pfad darf den Sync niemals werfen.
            }
        }

        /// <summary>
        /// v16-Diagnose: Teilnehmer des Proximity-Kanals. Der Proximity-Kanal war
        /// der blinde Fleck der v13-Auswertung (dort wurde nur der Funkkanal
        /// geloggt) — hier erscheint jedes Gruppenmitglied mit Namen.
        /// </summary>
        public void CopyProximityChannelParticipantIds(List<string> intoPlayerIds)
        {
            if (intoPlayerIds == null) return;
            intoPlayerIds.Clear();

            try
            {
                if (VivoxService.Instance == null ||
                    string.IsNullOrEmpty(proximityChannelName) ||
                    !VivoxService.Instance.ActiveChannels.TryGetValue(
                        proximityChannelName, out var list))
                {
                    return;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var participant = list[i];
                    if (participant == null) continue;
                    intoPlayerIds.Add(participant.IsSelf ? "ICH" : participant.PlayerId);
                }
            }
            catch (Exception)
            {
                // Diagnose-Pfad darf den Sync niemals werfen.
            }
        }

        public async Task EnsureRadioChannelAsync(string logicalChannelId)
        {
            if (!IsConnected) return;

            string id = WalkieRules.SanitizeChannelId(logicalChannelId);
            if (radioChannels.Contains(id)) return;

            string vivoxName = WalkieRules.ToVivoxRadioChannel(id);
            EarshotVoiceLog.Info($"Funkkanal '{id}' ({vivoxName}) wird betreten.");

            await VivoxService.Instance.JoinGroupChannelAsync(vivoxName, ChatCapability.AudioOnly);

            // Nach Join nicht automatisch auf Funk senden — Proximity bleibt Sendekanal,
            // bis SetRadioTransmittingAsync(true) kommt.
            if (string.IsNullOrEmpty(transmittingRadioLogicalId))
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(
                    TransmissionMode.Single, proximityChannelName);
            }

            radioChannels.Add(id);
            AttachExistingParticipants(vivoxName);
        }

        public async Task LeaveRadioChannelAsync(string logicalChannelId)
        {
            string id = WalkieRules.SanitizeChannelId(logicalChannelId);
            if (!radioChannels.Remove(id)) return;

            if (string.Equals(transmittingRadioLogicalId, id, StringComparison.OrdinalIgnoreCase))
            {
                transmittingRadioLogicalId = null;
                if (IsConnected)
                {
                    await VivoxService.Instance.SetChannelTransmissionModeAsync(
                        TransmissionMode.Single, proximityChannelName);
                }
            }

            string vivoxName = WalkieRules.ToVivoxRadioChannel(id);
            RemoveParticipantsForChannel(vivoxName);

            try
            {
                await VivoxService.Instance.LeaveChannelAsync(vivoxName);
                EarshotVoiceLog.Info($"Funkkanal verlassen: {id}");
            }
            catch (Exception ex)
            {
                EarshotVoiceLog.Warn($"Funkkanal '{id}' konnte nicht sauber verlassen werden: {ex.Message}");
            }
        }

        public async Task SetRadioTransmittingAsync(string logicalChannelId, bool transmitting)
        {
            // remote-hunt-v16.9: Frueher STILLER Rueckkehrpunkt. Ohne Log war ein PTT
            // ohne Vivox-Verbindung im Session-Log unsichtbar (Beweis Freund-Session
            // 20260920-005940: Die Sendung des zweiten Clients kam nie im Funkkanal an,
            // ohne dass irgendeine Zeile zeigte, wo die Kette abbrach).
            if (!IsConnected)
            {
                if (transmitting)
                {
                    VoiceSessionLog.Alert(
                        "FUNK sendet BLOCKIERT: Vivox nicht verbunden " +
                        "(SetRadioTransmittingAsync bricht ab — Sendung geht nirgendwo hin).");
                }
                return;
            }

            if (!transmitting)
            {
                transmittingRadioLogicalId = null;
                await VivoxService.Instance.SetChannelTransmissionModeAsync(
                    TransmissionMode.Single, proximityChannelName);
                VoiceSessionLog.Note("FUNK sendet aus (zurueck auf Proximity-Kanal)");
                return;
            }

            string id = WalkieRules.SanitizeChannelId(logicalChannelId);
            if (!radioChannels.Contains(id))
            {
                await EnsureRadioChannelAsync(id);
            }

            string vivoxName = WalkieRules.ToVivoxRadioChannel(id);
            transmittingRadioLogicalId = id;

            // Funk ersetzt Mund: nur in den Funkkanal senden.
            // remote-hunt-v16.9: Exceptions landen jetzt IM Session-Log — vorher
            // fraß nur die Konsole sie (WalkieRadioSync-Log-Datei blieb stumm).
            try
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(
                    TransmissionMode.Single, vivoxName);
            }
            catch (System.Exception ex)
            {
                VoiceSessionLog.Alert(
                    $"FUNK sendet FEHLGESCHLAGEN auf '{id}': Vivox-Moduswechsel wirft " +
                    $"({ex.GetType().Name}: {ex.Message}) — Sendung bleibt auf dem vorherigen Kanal.");
                throw;
            }

            // Bestaetigung mit Vivox-Sicht: TransmittingChannels ist die autoritative
            // SDK-Liste. Steht der Funkkanal dort nicht drin, ist der Moduswechsel
            // STILL gescheitert (ohne Exception) — genau dann greift diese Zeile.
            VoiceSessionLog.Note(
                $"FUNK sendet auf '{id}' (Proximity stumm auf dem Draht), " +
                $"vivoxTx=[{string.Join(" | ", ReadTransmittingChannelsSnapshot())}]");
        }

        private static string[] ReadTransmittingChannelsSnapshot()
        {
            try
            {
                var service = VivoxService.Instance;
                var channels = service != null ? service.TransmittingChannels : null;
                if (channels == null || channels.Count == 0) return new string[0];

                var result = new string[channels.Count];
                for (int i = 0; i < channels.Count; i++)
                {
                    result[i] = channels[i] ?? string.Empty;
                }
                return result;
            }
            catch
            {
                return new string[0];
            }
        }

        private async Task EnsureLoggedInAsync(string displayName)
        {
            if (!initialized)
            {
                await VivoxService.Instance.InitializeAsync();
                initialized = true;
            }

            if (VivoxService.Instance.IsLoggedIn) return;

            // DisableAutomaticChannelTransmissionSwap existiert in Vivox 16.10 noch nicht
            // (nur in Doku-Kommentaren). Nach jedem Funk-Join setzen wir die Sendung
            // explizit wieder auf Proximity bzw. den aktiven Walkie-Kanal.
            var options = new LoginOptions
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName
            };

            EarshotVoiceLog.Info("Anmeldung bei Vivox laeuft.");
            await VivoxService.Instance.LoginAsync(options);

            // v16-Diagnose-Option (default AUS): Vivox-NATIVE Wiedergabe nach Login
            // stumm schalten. Urspruengliche These: 'Hotel Game' schlug im sndvol
            // aus, waehrend masterPeak=0 (F12) — Vivox-native als Leak-Kandidat.
            // NACHTRAG 2026-09-19: Diese These ist geschaechtert — (a) ist nicht
            // dokumentiert, dass F12 WAHREND der sndvol-Beobachtung aktiv war,
            // (b) kann ein Solo-Funkkanal (1 Teilnehmer [ICH]) nichts zurueck-
            // spiegeln, und (c) erklaert die Parsec-Host-App (Mic-Playback auf dem
            // Host-Default-Output, siehe Debug-Historie v16-Meta) die sndvol-
            // Ausschlaege einfacher. Der Auto-Mute bleibt deshalb Diagnose-Mittel:
            // Er STOERT spaetere Gegenproben (F8/F7 werden bedeutungslos, ein
            // Teilnehmer ohne Tap waere stumm) und ist daher standardmäßig deaktiv.
            // Nur gezielt einschalten (Code oder Inspector), wenn die Vivox-native
            // These nach dem physischen Host-Test wieder auf dem Tisch liegt.
            if (DiagnosticMuteVivoxNativeOutputOnLogin)
            {
                VivoxService.Instance.MuteOutputDevice();
                VoiceSessionLog.Note(
                    "v16-DIAGNOSE: Vivox-native Ausgabe STUMM (MuteOutputDevice, " +
                    "Diagnose-Flag an). F7 (WalkieSidetoneCapture) schaltet sie fuer " +
                    "die Gegenprobe wieder AN.");
            }
        }

        private void AttachExistingParticipants(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                OnParticipantAdded(list[i]);
            }
        }

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;

            TryCreateTap(participant);

            if (!participant.IsInAudio)
            {
                participant.ParticipantAudioStateChanged -= OnParticipantAudioReady;
                participant.ParticipantAudioStateChanged += OnParticipantAudioReady;
            }
        }

        private void OnParticipantAudioReady()
        {
            if (!string.IsNullOrEmpty(proximityChannelName))
            {
                AttachExistingParticipants(proximityChannelName);
            }

            foreach (string id in radioChannels)
            {
                AttachExistingParticipants(WalkieRules.ToVivoxRadioChannel(id));
            }
        }

        private void TryCreateTap(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;

            if (!TryClassify(participant.ChannelName, out var pathKind, out string logicalId))
            {
                return;
            }

            var key = new VoiceSpeakerKey(participant.PlayerId, pathKind, logicalId);
            if (participants.ContainsKey(key)) return;

            try
            {
                string tapName = pathKind == VoicePathKind.Radio
                    ? $"Earshot Walkie - {logicalId} - {participant.PlayerId}"
                    : $"Earshot Voice - {participant.PlayerId}";

                participant.CreateVivoxParticipantTap(tapName, true);

                var source = participant.ParticipantTapAudioSource;
                if (source == null)
                {
                    EarshotVoiceLog.Warn(
                        $"Vivox hat fuer Spieler {participant.PlayerId} keine AudioSource geliefert. " +
                        "Dieser Spieler bleibt stumm, bis der Audio-Zustand kommt.");
                    return;
                }

                participants[key] = participant;

                EarshotVoiceLog.Info(
                    pathKind == VoicePathKind.Radio
                        ? $"Funkstimme empfangen von {participant.PlayerId} auf '{logicalId}'."
                        : $"Stimme empfangen von {participant.PlayerId}.");

                SpeakerAdded?.Invoke(new VoiceSpeaker(participant.PlayerId, source, pathKind, logicalId));
            }
            catch (Exception ex)
            {
                EarshotVoiceLog.Exception($"Audio Tap fuer {participant.PlayerId} fehlgeschlagen", ex);
            }
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;

            participant.ParticipantAudioStateChanged -= OnParticipantAudioReady;

            if (!TryClassify(participant.ChannelName, out var pathKind, out string logicalId))
            {
                return;
            }

            var key = new VoiceSpeakerKey(participant.PlayerId, pathKind, logicalId);
            if (!participants.Remove(key)) return;

            EarshotVoiceLog.Info(
                pathKind == VoicePathKind.Radio
                    ? $"Funkstimme verstummt: {participant.PlayerId} ({logicalId})."
                    : $"Stimme verstummt: {participant.PlayerId}.");

            SpeakerRemoved?.Invoke(key);
        }

        private void RemoveParticipantsForChannel(string vivoxChannelName)
        {
            if (!TryClassify(vivoxChannelName, out var pathKind, out string logicalId)) return;

            var toRemove = new List<VoiceSpeakerKey>();
            foreach (var pair in participants)
            {
                if (pair.Key.PathKind == pathKind &&
                    string.Equals(pair.Key.ChannelId, logicalId, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                var key = toRemove[i];
                if (!participants.Remove(key)) continue;
                SpeakerRemoved?.Invoke(key);
            }
        }

        private bool TryClassify(string vivoxChannelName, out VoicePathKind pathKind, out string logicalId)
        {
            pathKind = VoicePathKind.Proximity;
            logicalId = string.Empty;

            if (string.IsNullOrEmpty(vivoxChannelName)) return false;

            if (WalkieRules.TryParseLogicalChannel(vivoxChannelName, out logicalId))
            {
                pathKind = VoicePathKind.Radio;
                return true;
            }

            if (string.Equals(vivoxChannelName, proximityChannelName, StringComparison.Ordinal))
            {
                pathKind = VoicePathKind.Proximity;
                logicalId = vivoxChannelName;
                return true;
            }

            return false;
        }

        bool IVoiceBackendRecovery.IsSpeaking(VoiceSpeakerKey key)
        {
            return participants.TryGetValue(key, out var participant) &&
                   participant != null &&
                   (participant.SpeechDetected || participant.AudioEnergy > 0.02);
        }

        void IVoiceBackendRecovery.RecoverSpeaker(VoiceSpeakerKey key)
        {
            RecoverKey(key);
        }

        private void RecoverKey(VoiceSpeakerKey key)
        {
            if (!participants.TryGetValue(key, out var participant) || participant == null) return;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            VoiceSessionLog.Alert(
                $"SELBSTHEILUNG: Tap von {key} liefert kein echtes Signal mehr, " +
                "obwohl Vivox 'redet gerade' meldet - Tap wird neu aufgebaut.");

            bool wasTracked = participants.Remove(key);

            try
            {
                participant.DestroyVivoxParticipantTap();
            }
            catch (Exception ex)
            {
                EarshotVoiceLog.Exception($"Alten Tap fuer {key} loeschen fehlgeschlagen", ex);
            }

            if (wasTracked) SpeakerRemoved?.Invoke(key);

            TryCreateTap(participant);
            timer.Stop();
            VoiceSessionLog.Note(
                $"SELBSTHEILUNG fertig: {key}, {timer.ElapsedMilliseconds} ms, " +
                $"wiederhergestellt={participants.ContainsKey(key)}");
        }
    }
}
