using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Ein Baustein der Klangberechnung. Jeder Modifier bekommt die Hoersituation
    /// beschrieben und darf das Ergebnis veraendern.
    /// <para>
    /// Das ist der Erweiterungspunkt des gesamten Systems: Eigenes Klangverhalten
    /// entsteht durch ein neues Modul, nicht durch Aendern vorhandenen Codes.
    /// </para>
    /// </summary>
    public interface IVoiceModifier
    {
        /// <summary>
        /// Reihenfolge in der Kette, aufsteigend. Module mit kleinerem Wert laufen
        /// zuerst. Siehe <see cref="VoiceModifierOrder"/> fuer die belegten Bereiche.
        /// </summary>
        int Order { get; }

        /// <summary>Wahr, wenn das Modul aktuell mitrechnen soll.</summary>
        bool Enabled { get; }

        /// <summary>
        /// Veraendert das Klangergebnis anhand der Situation.
        /// Wird oft aufgerufen (etwa 15-mal pro Sekunde pro Sprecher), sollte also
        /// keine Allokationen verursachen.
        /// </summary>
        void Apply(in VoiceContext context, ref VoiceSample sample);
    }

    /// <summary>
    /// Empfohlene Reihenfolgewerte. Frei waehlbar, aber wer sich daran haelt, bekommt
    /// vorhersagbares Verhalten beim Mischen eigener und mitgelieferter Module.
    /// </summary>
    public static class VoiceModifierOrder
    {
        /// <summary>Grundlautstaerke nach Entfernung. Laeuft als Erstes.</summary>
        public const int Distance = 100;

        /// <summary>Daempfung durch feste Geometrie.</summary>
        public const int Occlusion = 200;

        /// <summary>Tueren und Fenster lockern die Occlusion wieder auf.</summary>
        public const int Portal = 300;

        /// <summary>Weg durch Raeume und Tueren, wenn die Sichtlinie blockiert ist.</summary>
        public const int Graph = 350;

        /// <summary>Raumeigenschaften wie Hall und Grunddaempfung.</summary>
        public const int Zone = 400;

        /// <summary>Platz fuer eigene Module des Spiels.</summary>
        public const int Gameplay = 500;

        /// <summary>
        /// Uebertragungswege wie Funk oder Telefon, die alles Vorherige aushebeln.
        /// Laeuft als Letztes.
        /// </summary>
        public const int Transmission = 900;
    }

    /// <summary>
    /// Basisklasse fuer Modifier, die als Asset im Projekt liegen und im Inspector
    /// eingestellt werden. Das ist der uebliche Weg, eigenes Klangverhalten zu bauen.
    /// </summary>
    public abstract class VoiceModifierAsset : ScriptableObject, IVoiceModifier
    {
        [SerializeField]
        [Tooltip("Modul voruebergehend abschalten, ohne es aus dem Profil zu entfernen.")]
        private bool enabled = true;

        public bool Enabled => enabled;

        public abstract int Order { get; }

        public abstract void Apply(in VoiceContext context, ref VoiceSample sample);
    }
}
