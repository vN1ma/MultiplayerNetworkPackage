using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>Proximity-Mund oder Funkgeraet-Pfad.</summary>
    public enum VoicePathKind
    {
        Proximity = 0,
        Radio = 1
    }

    /// <summary>
    /// Ein Sprecher, dessen Stimme empfangen wird, samt der AudioSource, die sie abspielt.
    /// <para>
    /// Die AudioSource gehoert dem Backend. Earshot haengt sie an den Avatar bzw. das
    /// Funkgeraet und steuert Lautstaerke und Filter, zerstoert sie aber niemals selbst.
    /// </para>
    /// </summary>
    public class VoiceSpeaker
    {
        /// <summary>Unity-Gaming-Services-Spieler-ID. Der Schluessel zum passenden Avatar.</summary>
        public string PlayerId { get; }

        /// <summary>Die AudioSource, aus der die Stimme kommt.</summary>
        public AudioSource Source { get; }

        /// <summary>Mund-Naehe oder Funkkanal.</summary>
        public VoicePathKind PathKind { get; }

        /// <summary>
        /// Bei Proximity: Vivox-Matchkanal. Bei Radio: logische Funkkanal-ID (ohne Prefix).
        /// </summary>
        public string ChannelId { get; }

        public VoiceSpeaker(
            string playerId,
            AudioSource source,
            VoicePathKind pathKind = VoicePathKind.Proximity,
            string channelId = null)
        {
            PlayerId = playerId;
            Source = source;
            PathKind = pathKind;
            ChannelId = channelId ?? string.Empty;
        }
    }

    /// <summary>Schluessel zum Entfernen eines Empfangspfads.</summary>
    public readonly struct VoiceSpeakerKey : IEquatable<VoiceSpeakerKey>
    {
        public string PlayerId { get; }
        public VoicePathKind PathKind { get; }
        public string ChannelId { get; }

        public VoiceSpeakerKey(string playerId, VoicePathKind pathKind, string channelId)
        {
            PlayerId = playerId ?? string.Empty;
            PathKind = pathKind;
            ChannelId = channelId ?? string.Empty;
        }

        public bool Equals(VoiceSpeakerKey other)
        {
            return PathKind == other.PathKind &&
                   string.Equals(PlayerId, other.PlayerId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ChannelId, other.ChannelId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => obj is VoiceSpeakerKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(PlayerId ?? string.Empty);
                hash = (hash * 397) ^ (int)PathKind;
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ChannelId ?? string.Empty);
                return hash;
            }
        }

        public override string ToString() => $"{PathKind}:{ChannelId}:{PlayerId}";
    }

    /// <summary>
    /// Die Austauschgrenze zum Sprachdienst.
    /// </summary>
    public interface IVoiceBackend
    {
        /// <summary>Anzeigename fuer Log-Ausgaben.</summary>
        string DisplayName { get; }

        /// <summary>Wahr, wenn ein Sprachkanal aktiv ist.</summary>
        bool IsConnected { get; }

        /// <summary>Eigenes Mikrofon stummschalten.</summary>
        bool MicrophoneMuted { get; set; }

        /// <summary>
        /// Ein entfernter Sprecher ist verfuegbar und seine AudioSource bereit.
        /// Der eigene Spieler wird hier nie gemeldet - man hoert sich nicht selbst.
        /// </summary>
        event Action<VoiceSpeaker> SpeakerAdded;

        /// <summary>Ein Sprecher-Pfad hat den Kanal verlassen.</summary>
        event Action<VoiceSpeakerKey> SpeakerRemoved;

        /// <summary>Betritt den Sprachkanal einer Sitzung.</summary>
        Task ConnectAsync(string channelName, string displayName);

        /// <summary>Verlaesst den Sprachkanal und raeumt auf.</summary>
        Task DisconnectAsync();
    }

    /// <summary>
    /// Optionale Erweiterung: separater Funkkanal fuer Walkie-Talkies.
    /// </summary>
    public interface IVoiceRadioBackend
    {
        bool IsRadioChannelJoined(string logicalChannelId);

        void CopyJoinedRadioChannels(List<string> into);

        Task EnsureRadioChannelAsync(string logicalChannelId);

        Task LeaveRadioChannelAsync(string logicalChannelId);

        /// <summary>
        /// true: nur in den Funkkanal senden (Mund/Proximity stumm auf dem Draht).
        /// false: wieder nur Proximity senden.
        /// </summary>
        Task SetRadioTransmittingAsync(string logicalChannelId, bool transmitting);
    }

    /// <summary>
    /// Optionale Erweiterung fuer Backends, deren zugrunde liegender Dienst weiss, ob ein
    /// Sprecher GERADE aktiv sendet, und die einen haengen gebliebenen Empfang von aussen
    /// neu aufbauen koennen.
    /// </summary>
    internal interface IVoiceBackendRecovery
    {
        /// <summary>Meldet der zugrunde liegende Dienst gerade aktive Sprachaktivitaet von diesem Spieler?</summary>
        bool IsSpeaking(string playerId);

        /// <summary>Baut den Empfangsweg (z.B. den Audio Tap) fuer diesen Spieler komplett neu auf.</summary>
        void RecoverSpeaker(string playerId);
    }
}
