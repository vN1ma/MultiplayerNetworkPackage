using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Die einzige Bruecke, die dieses Paket zu einem Multiplayer-Framework braucht.
    /// <para>
    /// Wer ein eigenes Netzwerk-Framework anbindet (Netcode, Mirror, Photon, ...), schreibt
    /// einen kleinen Adapter, der dieses Interface auf dem Player-Prefab implementiert (oder
    /// <see cref="EarshotProximityVoice.Bind"/> aufruft, sobald die Identitaet feststeht).
    /// Alles unterhalb dieser Grenze - Registrierung, Zuordnung, Klang - kennt kein Netcode
    /// und keine bestimmte Multiplayer-Loesung.
    /// </para>
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
