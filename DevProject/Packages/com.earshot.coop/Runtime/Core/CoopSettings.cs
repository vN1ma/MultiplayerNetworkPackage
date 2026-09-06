using Earshot.Voice;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Zentrale Konfiguration von Earshot. Der Setup-Wizard legt dieses Asset unter
    /// <c>Assets/Resources/EarshotSettings.asset</c> an, damit es zur Laufzeit ohne
    /// Szenenreferenz gefunden wird.
    /// </summary>
    [CreateAssetMenu(menuName = "Earshot/Coop Settings", fileName = "EarshotSettings")]
    public class CoopSettings : ScriptableObject
    {
        /// <summary>Dateiname im Resources-Ordner, ohne Endung.</summary>
        public const string ResourcePath = "EarshotSettings";

        /// <summary>
        /// Obergrenze, fuer die diese Version ausgelegt ist. Darueber braucht es
        /// Voice-Culling und Interest-Management, sonst leidet die Bandbreite.
        /// </summary>
        public const int MaxSupportedPlayers = 8;

        [Header("Sitzung")]
        [SerializeField, Range(2, MaxSupportedPlayers)]
        [Tooltip("Wie viele Spieler gleichzeitig in einer Sitzung sein duerfen, den Host mitgezaehlt.")]
        private int maxPlayers = 4;

        [SerializeField]
        [Tooltip("Relay-Region erzwingen, etwa 'europe-west4'. Leer lassen heisst: Unity misst die Laufzeiten und waehlt die schnellste. Das ist fast immer die bessere Wahl.")]
        private string relayRegion = "";

        [Header("Szenen")]
        [SerializeField]
        [Tooltip("Beitretende Spieler behalten ihre eigenen geladenen Szenen. Dringend empfohlen: Ohne das entlaedt Netcode beim Beitritt die Szenen des Clients, was in einem bestehenden Projekt unerwartet Dinge zerstoert.")]
        private bool keepClientScenes = true;

        [Header("Stimme")]
        [SerializeField]
        [Tooltip("Proximity Voice Chat aktivieren.")]
        private bool voiceEnabled = true;

        [SerializeField]
        [Tooltip("Das Klangverhalten. Ohne Profil nutzt Earshot eine schlichte Entfernungsdaempfung und weist im Log darauf hin.")]
        private VoiceProfile voiceProfile;

        [SerializeField]
        [Tooltip("Mikrofon beim Beitreten stummgeschaltet lassen, bis der Spieler es selbst aktiviert.")]
        private bool microphoneMutedOnJoin = false;

        [Header("Entwicklung")]
        [SerializeField]
        [Tooltip("Ausfuehrliche Meldungen zum Verbindungsablauf in der Konsole.")]
        private bool verboseLogging = true;

        [SerializeField]
        [Tooltip("Jede Editor-Instanz meldet sich als eigener Spieler an. Noetig, damit im Multiplayer Play Mode mehrere Instanzen gleichzeitig funktionieren. In Builds ohne Wirkung - dort hilft stattdessen das Startargument '-profile NAME' fuer den Solo-Test mit zwei EXE-Fenstern.")]
        private bool uniqueProfilePerEditorInstance = true;

        public int MaxPlayers => Mathf.Clamp(maxPlayers, 2, MaxSupportedPlayers);
        public string RelayRegion => string.IsNullOrWhiteSpace(relayRegion) ? null : relayRegion.Trim();
        public bool KeepClientScenes => keepClientScenes;
        public bool VoiceEnabled => voiceEnabled;
        public VoiceProfile VoiceProfile => voiceProfile;
        public bool MicrophoneMutedOnJoin => microphoneMutedOnJoin;

        /// <summary>
        /// Wechselt das Klangprofil zur Laufzeit. Vorgesehen fuer Szenen mit anderem
        /// Massstab, ohne das Testhaus-Profil zu ueberschreiben.
        /// </summary>
        public void SetVoiceProfile(VoiceProfile profile)
        {
            voiceProfile = profile;
        }

        public bool VerboseLogging => verboseLogging;
        public bool UniqueProfilePerEditorInstance => uniqueProfilePerEditorInstance;

        private static CoopSettings cached;
        private static bool warnedAboutMissingAsset;

        /// <summary>
        /// Die aktive Konfiguration. Sucht das Asset in einem Resources-Ordner und faellt
        /// auf Standardwerte zurueck, falls keines vorhanden ist. Damit laeuft Earshot auch
        /// dann, wenn jemand den Setup-Wizard uebersprungen hat.
        /// </summary>
        public static CoopSettings Instance
        {
            get
            {
                if (cached != null) return cached;

                cached = Resources.Load<CoopSettings>(ResourcePath);
                if (cached == null)
                {
                    cached = CreateInstance<CoopSettings>();
                    cached.name = "EarshotSettings (Standardwerte)";

                    if (!warnedAboutMissingAsset)
                    {
                        warnedAboutMissingAsset = true;
                        CoopLog.Warn(
                            $"Kein Einstellungs-Asset unter Resources/{ResourcePath} gefunden. " +
                            "Earshot laeuft mit Standardwerten weiter. " +
                            "Ueber Tools > Earshot > Setup laesst sich eines anlegen.");
                    }
                }

                CoopLog.Verbose = cached.verboseLogging;
                return cached;
            }
        }

        /// <summary>
        /// Setzt den zwischengespeicherten Verweis zurueck. Der Editor ruft das auf,
        /// nachdem der Wizard das Asset angelegt oder veraendert hat.
        /// </summary>
        public static void ClearCache()
        {
            cached = null;
            warnedAboutMissingAsset = false;
        }

        private void OnValidate()
        {
            CoopLog.Verbose = verboseLogging;

            if (voiceEnabled && voiceProfile == null)
            {
                CoopLog.Warn(
                    "Voice ist aktiviert, aber es ist kein VoiceProfile zugewiesen. " +
                    "Ohne Profil gibt es nur eine einfache Entfernungsdaempfung, " +
                    "keine Tueren, Waende oder Raeume.");
            }
        }
    }
}
