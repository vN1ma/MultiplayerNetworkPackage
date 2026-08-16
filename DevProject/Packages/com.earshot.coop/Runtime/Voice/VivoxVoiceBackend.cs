using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Sprachuebertragung ueber Unity Vivox.
    /// <para>
    /// Zwei Entwurfsentscheidungen praegen diese Klasse und sind der Grund, warum das
    /// Klangverhalten spaeter frei gestaltbar ist:
    /// </para>
    /// <para>
    /// Erstens ein <b>2D-Kanal statt Vivox' eingebautem 3D-Modus</b>. Im 3D-Modus
    /// berechnet Vivox die Lautstaerke selbst und nimmt uns damit genau die Kontrolle weg,
    /// die wir fuer Tueren und Waende brauchen. Im 2D-Kanal kommen alle Stimmen unveraendert
    /// an, und die raeumliche Berechnung machen wir lokal. Bei bis zu acht Spielern ist die
    /// dafuer noetige Bandbreite unkritisch.
    /// </para>
    /// <para>
    /// Zweitens <b>Audio Taps</b>. Statt die Stimmen direkt an die Lautsprecher zu geben,
    /// leitet Vivox jede einzelne in eine normale Unity-AudioSource um. Ab da ist eine
    /// Stimme fuer Unity ein Geraeusch wie jedes andere - mit allen Filtern, die dazugehoeren.
    /// </para>
    /// </summary>
    public class VivoxVoiceBackend : IVoiceBackend
    {
        private readonly Dictionary<string, VivoxParticipant> participants =
            new Dictionary<string, VivoxParticipant>(StringComparer.OrdinalIgnoreCase);

        private string channelName;
        private bool initialized;
        private bool micMuted;

        public string DisplayName => "Unity Vivox";

        public bool IsConnected => !string.IsNullOrEmpty(channelName);

        public event Action<VoiceSpeaker> SpeakerAdded;
        public event Action<string> SpeakerRemoved;

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

                CoopLog.Info(value ? "Mikrofon stummgeschaltet." : "Mikrofon aktiv.");
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

            CoopLog.Info($"Sprachkanal '{channel}' wird betreten.");

            // AudioOnly: Textnachrichten laufen ueber das Spiel, nicht ueber Vivox.
            await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.All, channel);

            channelName = channel;

            // Der Stummschaltungswunsch kann gesetzt worden sein, bevor Vivox bereit war.
            if (micMuted) VivoxService.Instance.MuteInputDevice();

            AttachExistingParticipants();

            CoopLog.Info(
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

            if (!string.IsNullOrEmpty(channelName))
            {
                string leaving = channelName;
                channelName = null;

                try
                {
                    await VivoxService.Instance.LeaveChannelAsync(leaving);
                    CoopLog.Info("Sprachkanal verlassen.");
                }
                catch (Exception ex)
                {
                    CoopLog.Warn($"Sprachkanal konnte nicht sauber verlassen werden: {ex.Message}");
                }
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

            var options = new LoginOptions
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName
            };

            CoopLog.Info("Anmeldung bei Vivox laeuft.");
            await VivoxService.Instance.LoginAsync(options);
        }

        /// <summary>
        /// Teilnehmer, die schon im Kanal waren, bevor wir zugehoert haben.
        /// </summary>
        private void AttachExistingParticipants()
        {
            if (string.IsNullOrEmpty(channelName)) return;
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(channelName, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                OnParticipantAdded(list[i]);
            }
        }

        /// <summary>
        /// Erzeugt fuer einen neuen Sprecher einen Audio Tap und meldet die entstandene
        /// AudioSource nach oben.
        /// </summary>
        private void OnParticipantAdded(VivoxParticipant participant)
        {
            // Sich selbst zu hoeren waere ein Echo. Vivox meldet den eigenen Teilnehmer mit.
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
            AttachExistingParticipants();
        }

        private void TryCreateTap(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;
            if (participants.ContainsKey(participant.PlayerId)) return;

            try
            {
                // Der zweite Parameter unterdrueckt den Standard-Kanalmix fuer diesen
                // Sprecher. Ohne ihn hoert man jede Stimme doppelt: einmal flach aus dem
                // Kanalmix und einmal raeumlich aus unserem Tap.
                participant.CreateVivoxParticipantTap(
                    $"Earshot Voice - {participant.PlayerId}", true);

                var source = participant.ParticipantTapAudioSource;

                if (source == null)
                {
                    CoopLog.Warn(
                        $"Vivox hat fuer Spieler {participant.PlayerId} keine AudioSource geliefert. " +
                        "Dieser Spieler bleibt stumm, bis der Audio-Zustand kommt.");
                    return;
                }

                participants[participant.PlayerId] = participant;

                CoopLog.Info($"Stimme empfangen von {participant.PlayerId}.");
                SpeakerAdded?.Invoke(new VoiceSpeaker(participant.PlayerId, source));
            }
            catch (Exception ex)
            {
                CoopLog.Exception($"Audio Tap fuer {participant.PlayerId} fehlgeschlagen", ex);
            }
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;

            participant.ParticipantAudioStateChanged -= OnParticipantAudioReady;

            if (!participants.Remove(participant.PlayerId)) return;

            CoopLog.Info($"Stimme verstummt: {participant.PlayerId}.");

            // Das Tap-GameObject raeumt Vivox selbst ab, sobald der Teilnehmer geht.
            // Ein eigener Destroy-Aufruf wuerde hier nur Schaden anrichten.
            SpeakerRemoved?.Invoke(participant.PlayerId);
        }
    }
}
