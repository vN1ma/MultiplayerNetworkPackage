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

        /// <summary>Ein Sprecher hat den Kanal verlassen. Liefert dessen Spieler-ID.</summary>
        event Action<string> SpeakerRemoved;

        /// <summary>Betritt den Sprachkanal einer Sitzung.</summary>
        Task ConnectAsync(string channelName, string displayName);

        /// <summary>Verlaesst den Sprachkanal und raeumt auf.</summary>
        Task DisconnectAsync();
    }

    /// <summary>
    /// Optionale Erweiterung fuer Backends, deren zugrunde liegender Dienst weiss, ob ein
    /// Sprecher GERADE aktiv sendet, und die einen haengen gebliebenen Empfang von aussen
    /// neu aufbauen koennen. Getrennt von <see cref="IVoiceBackend"/>, damit einfachere
    /// Backends (Tests, zukuenftige Anbieter) das nicht implementieren muessen.
    /// <para>
    /// Der Grund, warum das ueberhaupt noetig ist: <c>AudioSource.isPlaying</c> taugt
    /// NICHT als Beweis, dass wieder echtes Audio ankommt. Vivox' eigener
    /// <c>VivoxAudioProcessor</c> pausiert die AudioSource, wenn ueber ~400 ms kein neues
    /// Netzwerk-Audio ankommt, kann sie aber auch wieder "spielend" markieren, ohne dass
    /// je wieder echte Sprachdaten fliessen. Deshalb entscheidet <see cref="VoiceRuntime"/>
    /// ausschliesslich anhand des echten Signalpegels (<see cref="VoiceEmitter.TapIsPlaying"/>),
    /// ob ein Tap haengt, und fragt hier nur noch, ob der Dienst selbst meint, der
    /// Teilnehmer rede gerade (um normale Sprechpausen nicht mit einem echten Haenger zu
    /// verwechseln).
    /// </para>
    /// </summary>
    internal interface IVoiceBackendRecovery
    {
        /// <summary>Meldet der zugrunde liegende Dienst gerade aktive Sprachaktivitaet von diesem Spieler?</summary>
        bool IsSpeaking(string playerId);

        /// <summary>Baut den Empfangsweg (z.B. den Audio Tap) fuer diesen Spieler komplett neu auf.</summary>
        void RecoverSpeaker(string playerId);
    }
}
