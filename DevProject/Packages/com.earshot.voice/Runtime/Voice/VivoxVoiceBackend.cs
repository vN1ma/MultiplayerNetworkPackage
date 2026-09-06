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

        public string DisplayName => "Unity Vivox";

        public bool IsConnected => !string.IsNullOrEmpty(proximityChannelName);

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
            if (!IsConnected) return;

            if (!transmitting)
            {
                transmittingRadioLogicalId = null;
                await VivoxService.Instance.SetChannelTransmissionModeAsync(
                    TransmissionMode.Single, proximityChannelName);
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
            await VivoxService.Instance.SetChannelTransmissionModeAsync(
                TransmissionMode.Single, vivoxName);

            VoiceSessionLog.Note($"FUNK sendet auf '{id}' (Proximity stumm auf dem Draht)");
        }

        private async Task EnsureLoggedInAsync(string displayName)
        {
            if (!initialized)
            {
                await VivoxService.Instance.InitializeAsync();
                initialized = true;
            }

            if (VivoxService.Instance.IsLoggedIn) return;

            var options = new LoginOptions
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                // Mehrere Kanaele: sonst springt Vivox die Sendung auf den zuletzt betretenen.
                DisableAutomaticChannelTransmissionSwap = true
            };

            EarshotVoiceLog.Info("Anmeldung bei Vivox laeuft.");
            await VivoxService.Instance.LoginAsync(options);
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

        bool IVoiceBackendRecovery.IsSpeaking(string playerId)
        {
            foreach (var pair in participants)
            {
                if (!string.Equals(pair.Key.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var participant = pair.Value;
                if (participant != null &&
                    (participant.SpeechDetected || participant.AudioEnergy > 0.02))
                {
                    return true;
                }
            }

            return false;
        }

        void IVoiceBackendRecovery.RecoverSpeaker(string playerId)
        {
            var keys = new List<VoiceSpeakerKey>();
            foreach (var pair in participants)
            {
                if (string.Equals(pair.Key.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(pair.Key);
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                RecoverKey(keys[i]);
            }
        }

        private void RecoverKey(VoiceSpeakerKey key)
        {
            if (!participants.TryGetValue(key, out var participant) || participant == null) return;

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
        }
    }
}
