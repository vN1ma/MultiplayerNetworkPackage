using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Ein Sprecher, dessen Stimme empfangen wird, samt der AudioSource, die sie abspielt.
    /// <para>
    /// Die AudioSource gehoert dem Backend. Earshot haengt sie an den Avatar und steuert
    /// Lautstaerke und Filter, zerstoert sie aber niemals selbst - bei Vivox ist die
    /// Lebensdauer an den Teilnehmer gekoppelt, und ein Eingriff von aussen fuehrt zu
    /// schwer auffindbaren Abstuerzen.
    /// </para>
    /// </summary>
    public class VoiceSpeaker
    {
        /// <summary>Unity-Gaming-Services-Spieler-ID. Der Schluessel zum passenden Avatar.</summary>
        public string PlayerId { get; }

        /// <summary>Die AudioSource, aus der die Stimme kommt.</summary>
        public AudioSource Source { get; }

        public VoiceSpeaker(string playerId, AudioSource source)
        {
            PlayerId = playerId;
            Source = source;
        }
    }

    /// <summary>
    /// Die Austauschgrenze zum Sprachdienst.
    /// <para>
    /// Alles unterhalb dieser Schnittstelle ist anbieterspezifisch, alles darueber - also
    /// die gesamte Logik fuer Entfernung, Waende, Tueren und Raeume - ist es nicht. Wer
    /// Vivox spaeter durch etwas anderes ersetzen will, schreibt eine neue Implementierung
    /// und behaelt sein komplettes Klangverhalten.
    /// </para>
    /// </summary>
    public interface IVoiceBackend
    {
        /// <summary>Anzeigename fuer Log-Ausgaben und den Setup-Wizard.</summary>
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

        /// <summary>Ein Sprecher hat den Kanal verlassen. Liefert dessen Spieler-ID.</summary>
        event Action<string> SpeakerRemoved;

        /// <summary>Betritt den Sprachkanal einer Sitzung.</summary>
        Task ConnectAsync(string channelName, string displayName);

        /// <summary>Verlaesst den Sprachkanal und raeumt auf.</summary>
        Task DisconnectAsync();
    }
}
