using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Beschreibt die Hoersituation zwischen dem lokalen Zuhoerer und einem Sprecher.
    /// Wird einmal pro Auswertungsschritt gefuellt und dann durch die Modifier-Kette
    /// gereicht. Rein lesend: Ein Modifier veraendert niemals den Kontext, sondern nur
    /// das <see cref="VoiceSample"/>.
    /// </summary>
    public struct VoiceContext
    {
        /// <summary>
        /// Das Profil, unter dem gerade gerechnet wird. Module lesen daraus Werte wie die
        /// Hoerweite, statt sie ein zweites Mal bei sich selbst einstellbar zu machen -
        /// zwei Quellen fuer dieselbe Zahl waeren eine sichere Fehlerquelle.
        /// </summary>
        public VoiceProfile Profile;

        /// <summary>Position des lokalen AudioListeners.</summary>
        public Vector3 ListenerPosition;

        /// <summary>Position des Sprechers (Mundhoehe des Avatars).</summary>
        public Vector3 SpeakerPosition;

        /// <summary>Abstand in Metern zwischen Zuhoerer und Sprecher (Luftlinie).</summary>
        public float Distance;

        /// <summary>
        /// Abstand, den die Entfernungsdaempfung benutzt. Gleich <see cref="Distance"/>,
        /// oder die Weglaenge durch den Raum-Portal-Graph, wenn der Graph gewinnt.
        /// </summary>
        public float HearingDistance;

        /// <summary>Wahr, wenn der Schall den Graph-Weg nimmt statt die Luftlinie.</summary>
        public bool UsedGraph;

        /// <summary>
        /// Wo die Stimme herzukommen scheint. Bei Graph-Weg die erste Tuer/Ecke,
        /// sonst die Sprecherposition.
        /// </summary>
        public Vector3 ApparentPosition;

        /// <summary>
        /// Richtung vom Ohr zur scheinbaren Quelle. Bei Graph-Weg aus der Tuer/Ecke,
        /// nicht durch die Wand.
        /// </summary>
        public Vector3 ApparentDirection;

        /// <summary>
        /// 0 = alle Tueren auf dem Graph-Weg offen, 1 = alle zu.
        /// Mehrere geschlossene Tueren addieren sich, geclippt auf 1.
        /// </summary>
        public float GraphClosedness;

        /// <summary>
        /// Wie stark feste Geometrie die direkte Linie blockiert.
        /// 0 = freie Sicht, 1 = vollstaendig durch massive Wand getrennt.
        /// </summary>
        public float OcclusionAmount;

        /// <summary>
        /// Durchlaessigkeit des am staerksten oeffnenden Portals auf der Linie.
        /// 1 = offene Tuer oder gar kein Portal, 0 = fest geschlossen.
        /// Nur gesetzt, wenn die Linie ueberhaupt durch ein Portal geht.
        /// </summary>
        public float PortalOpenness;

        /// <summary>Wahr, wenn die direkte Linie durch mindestens ein Portal fuehrt.</summary>
        public bool HasPortal;

        /// <summary>
        /// Das am staerksten geoeffnete Portal auf der Linie, aus dem
        /// <see cref="PortalOpenness"/> stammt. Null, wenn kein Portal im Weg liegt.
        /// </summary>
        public VoicePortal Portal;

        /// <summary>Zone, in der sich der Zuhoerer befindet. Kann null sein.</summary>
        public VoiceZone ListenerZone;

        /// <summary>Zone, in der sich der Sprecher befindet. Kann null sein.</summary>
        public VoiceZone SpeakerZone;

        /// <summary>Wahr, wenn beide im selben Raum stehen (oder beide in keinem).</summary>
        public bool SameZone;

        /// <summary>Sekunden seit der letzten Auswertung. Fuer zeitabhaengige Module.</summary>
        public float DeltaTime;
    }
}
