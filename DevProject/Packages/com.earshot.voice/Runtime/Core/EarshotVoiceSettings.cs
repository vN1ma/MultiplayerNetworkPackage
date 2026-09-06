using Earshot.Voice.Modifiers;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Einstellungen fuer Earshot Voice. Als Asset unter
    /// <c>Assets/Resources/EarshotVoiceSettings.asset</c> ablegen, damit es zur Laufzeit
    /// ohne Szenenreferenz gefunden wird.
    /// </summary>
    [CreateAssetMenu(menuName = "Earshot Voice/Settings", fileName = "EarshotVoiceSettings")]
    public class EarshotVoiceSettings : ScriptableObject
    {
        public const string ResourcePath = "EarshotVoiceSettings";

        [Header("Stimme")]
        [SerializeField]
        [Tooltip("Proximity Voice Chat aktivieren.")]
        private bool voiceEnabled = true;

        [SerializeField]
        [Tooltip("Das Klangverhalten. Ohne Profil gilt ein Laufzeit-Standard mit Entfernung und Waenden.")]
        private VoiceProfile voiceProfile;

        [SerializeField]
        [Tooltip("Mikrofon beim Beitreten stummgeschaltet lassen, bis der Spieler es selbst aktiviert.")]
        private bool microphoneMutedOnJoin = false;

        [Header("Verbindung")]
        [SerializeField]
        [Tooltip("Der lokale EarshotProximityVoice tritt dem Sprachkanal von selbst bei.")]
        private bool autoConnect = true;

        [SerializeField]
        [Tooltip("Fallback-Kanal, wenn keine Unity-Lobby gefunden wird und das Inspector-Feld leer ist.")]
        private string channelName = VoiceChannelResolver.DefaultChannel;

        [Header("Entwicklung")]
        [SerializeField]
        [Tooltip("Ausfuehrliche Meldungen zum Verbindungsablauf in der Konsole.")]
        private bool verboseLogging = true;

        public bool VoiceEnabled => voiceEnabled;
        public bool MicrophoneMutedOnJoin => microphoneMutedOnJoin;
        public bool AutoConnect => autoConnect;
        public string ChannelName => channelName;
        public bool VerboseLogging => verboseLogging;

        public VoiceProfile VoiceProfile
        {
            get
            {
                EnsureProfile();
                return voiceProfile;
            }
        }

        public void SetVoiceProfile(VoiceProfile profile)
        {
            voiceProfile = profile;
            defaultsInjected = false;
        }

        /// <summary>
        /// Schreibt die Player-Hoerregler in das laufende Profil und die Standard-Module.
        /// </summary>
        public void ApplyHearingTuning(VoiceHearingTuning tuning)
        {
            if (tuning == null) return;
            EnsureProfile();
            voiceProfile.ApplyTuning(tuning);

            var modifiers = voiceProfile.SortedModifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                switch (modifiers[i])
                {
                    case DistanceFalloffModifier distance:
                        distance.Configure(tuning);
                        break;
                    case OcclusionModifier occlusion:
                        occlusion.Configure(tuning);
                        break;
                    case PortalModifier portal:
                        portal.Configure(tuning);
                        break;
                    case GraphModifier graph:
                        graph.Configure(tuning);
                        break;
                    case ZoneModifier zone:
                        zone.Configure(tuning);
                        break;
                }
            }

            EarshotVoice.HeardVoiceVolume = tuning.heardVolume;
        }

        private static EarshotVoiceSettings cached;
        private static bool warnedAboutMissingAsset;
        private bool defaultsInjected;

        public static EarshotVoiceSettings Instance
        {
            get
            {
                if (cached != null)
                {
                    EarshotVoiceLog.Verbose = cached.verboseLogging;
                    return cached;
                }

                cached = Resources.Load<EarshotVoiceSettings>(ResourcePath);
                if (cached == null)
                {
                    cached = CreateInstance<EarshotVoiceSettings>();
                    cached.name = "EarshotVoiceSettings (Standardwerte)";

                    if (!warnedAboutMissingAsset)
                    {
                        warnedAboutMissingAsset = true;
                        EarshotVoiceLog.Warn(
                            "Kein Asset unter Resources/" + ResourcePath + " gefunden. " +
                            "Earshot Voice laeuft mit Standardwerten.");
                    }
                }

                EarshotVoiceLog.Verbose = cached.verboseLogging;
                return cached;
            }
        }

        public static void ClearCache()
        {
            cached = null;
            warnedAboutMissingAsset = false;
        }

        private void EnsureProfile()
        {
            if (voiceProfile == null)
            {
                voiceProfile = CreateInstance<VoiceProfile>();
                voiceProfile.name = "Runtime Default Voice Profile";
                EarshotVoiceLog.Warn(
                    "Kein Voice Profile zugewiesen. Es gilt 25 m Hoerweite. " +
                    "Lege ein Asset per Create > Earshot Voice > Voice Profile an.");
            }

            if (defaultsInjected) return;
            if (voiceProfile.SortedModifiers.Count > 0) return;

            voiceProfile.AddRuntimeModifier(CreateInstance<DistanceFalloffModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<OcclusionModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<PortalModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<GraphModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<ZoneModifier>());
            defaultsInjected = true;
        }

        private void OnValidate()
        {
            EarshotVoiceLog.Verbose = verboseLogging;
        }
    }
}
