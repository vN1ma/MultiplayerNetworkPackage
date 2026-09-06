using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Die Bruecke zwischen Avatar und Sprachschicht. <see cref="EarshotProximityVoice"/>
    /// implementiert das. Ein eigener Adapter ist nicht noetig.
    /// </summary>
    public interface IProximityVoicePlayer
    {
        /// <summary>
        /// Unity-Gaming-Services-PlayerId. Muss mit der Vivox-Teilnehmer-ID uebereinstimmen,
        /// sonst laesst sich eine eingehende Stimme diesem Avatar nicht zuordnen. Leer,
        /// solange die Identitaet noch nicht bekannt ist (z.B. kurz nach dem Netzwerk-Spawn).
        /// </summary>
        string PlayerId { get; }

        /// <summary>Wahr, sobald <see cref="PlayerId"/> gesetzt ist.</summary>
        bool HasIdentity { get; }

        /// <summary>Nur auf dem eigenen Avatar wahr, niemals auf fremden.</summary>
        bool IsLocalPlayer { get; }

        /// <summary>
        /// Mund/Kopf. Eingehende und ausgehende Stimme haengen sich an diesen Punkt.
        /// Nie null - ohne eigenen Anker gilt das Transform des Avatars selbst.
        /// </summary>
        Transform VoiceAnchor { get; }

        /// <summary>Position des Ankers. Bequemer Zugriff ohne Transform-Dereferenzierung.</summary>
        Vector3 Position { get; }

        /// <summary>Nur fuer Logs und Anzeige. Kann der Anzeigename aus dem Spiel sein.</summary>
        string DisplayName { get; }
    }
}
