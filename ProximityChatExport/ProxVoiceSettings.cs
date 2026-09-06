using Earshot.Proximity.Modifiers;
using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Einstellungen nur fuer den Proximity-Chat. Als Asset unter
    /// <c>Assets/Resources/ProxVoiceSettings.asset</c> ablegen, damit es zur Laufzeit
    /// ohne Szenenreferenz gefunden wird.
    /// </summary>
    [CreateAssetMenu(menuName = "Earshot Proximity/Settings", fileName = "ProxVoiceSettings")]
    public class ProxVoiceSettings : ScriptableObject
    {
        public const string ResourcePath = "ProxVoiceSettings";

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

        [Header("Entwicklung")]
        [SerializeField]
        [Tooltip("Ausfuehrliche Meldungen zum Verbindungsablauf in der Konsole.")]
        private bool verboseLogging = true;

        public bool VoiceEnabled => voiceEnabled;
        public bool MicrophoneMutedOnJoin => microphoneMutedOnJoin;
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

        private static ProxVoiceSettings cached;
        private static bool warnedAboutMissingAsset;
        private bool defaultsInjected;

        public static ProxVoiceSettings Instance
        {
            get
            {
                if (cached != null)
                {
                    ProxLog.Verbose = cached.verboseLogging;
                    return cached;
                }

                cached = Resources.Load<ProxVoiceSettings>(ResourcePath);
                if (cached == null)
                {
                    cached = CreateInstance<ProxVoiceSettings>();
                    cached.name = "ProxVoiceSettings (Standardwerte)";

                    if (!warnedAboutMissingAsset)
                    {
                        warnedAboutMissingAsset = true;
                        ProxLog.Warn(
                            "Kein Asset unter Resources/" + ResourcePath + " gefunden. " +
                            "Proximity Chat laeuft mit Standardwerten. Siehe ANLEITUNG.md.");
                    }
                }

                ProxLog.Verbose = cached.verboseLogging;
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
                ProxLog.Warn(
                    "Kein Voice Profile zugewiesen. Es gilt 25 m Hoerweite. " +
                    "Lege ein Asset per Create > Earshot Proximity > Voice Profile an.");
            }

            if (defaultsInjected) return;
            if (voiceProfile.SortedModifiers.Count > 0) return;

            voiceProfile.AddRuntimeModifier(CreateInstance<DistanceFalloffModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<OcclusionModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<PortalModifier>());
            voiceProfile.AddRuntimeModifier(CreateInstance<ZoneModifier>());
            defaultsInjected = true;
        }

        private void OnValidate()
        {
            ProxLog.Verbose = verboseLogging;
        }
    }
}
