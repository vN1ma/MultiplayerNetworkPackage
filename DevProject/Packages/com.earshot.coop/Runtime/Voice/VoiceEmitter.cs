using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Der Lautsprecher eines Sprechers in der Spielwelt.
    /// <para>
    /// Diese Komponente sitzt auf dem GameObject, das Vivox fuer den Audio Tap angelegt
    /// hat, und ist die einzige Stelle im Paket, die eine AudioSource tatsaechlich
    /// anfasst. Die Pipeline liefert ein Ziel, hier wird weich dorthin geblendet.
    /// </para>
    /// <para>
    /// Die Glaettung ist kein Feinschliff, sondern notwendig: Sprungartige Aenderungen an
    /// Lautstaerke oder Filterfrequenz erzeugen ein deutlich hoerbares Knacken, und die
    /// Pipeline laeuft bewusst nur etwa fuenfzehnmal pro Sekunde.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    public class VoiceEmitter : MonoBehaviour
    {
        private const float NoReverbRoomLevel = -10000f;
        private const float FullReverbRoomLevel = -1000f;
        private const float FallbackSmoothingHalfLife = 0.09f;
        private const float FallbackMaxDistance = 25f;

        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;
        private AudioReverbFilter reverb;

        private VoiceProfile profile;
        private VoiceSample current = VoiceSample.Default;
        private VoiceSample target = VoiceSample.Default;

        /// <summary>Unity-Gaming-Services-ID des Sprechers.</summary>
        public string PlayerId { get; private set; }

        /// <summary>Der Punkt, an dem die Stimme entsteht. Null, wenn noch kein Avatar bekannt ist.</summary>
        public Transform Anchor { get; set; }

        /// <summary>Der zuletzt angewendete Zustand. Nuetzlich fuer Debug-Anzeigen.</summary>
        public VoiceSample Current => current;

        /// <summary>
        /// Die Hoersituation der letzten Pipeline-Auswertung. Wird nicht neu berechnet,
        /// sondern nur abgelegt, damit das Debug-Overlay dieselben Zahlen zeigt.
        /// </summary>
        public VoiceContext LastContext { get; internal set; }

        internal void Initialize(string playerId, AudioSource audioSource, VoiceProfile voiceProfile)
        {
            PlayerId = playerId;
            source = audioSource;
            profile = voiceProfile;

            ConfigureSource();
            AttachFilters();

            current = VoiceSample.Default;
            target = current;
            Apply();
        }

        private void ConfigureSource()
        {
            if (source == null) return;

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.volume = 0f;

            // Unity soll nichts eigenmaechtig daempfen: Die gesamte Entfernungsabhaengigkeit
            // stammt aus der Pipeline. Waeren beide aktiv, wirkte die Kurve doppelt und die
            // im Profil eingestellte Hoerweite waere wirkungslos. Eine konstante Kurve auf
            // 1 schaltet Unitys eigene Abschwaechung praktisch ab.
            source.maxDistance = profile != null
                ? Mathf.Max(1f, profile.MaxHearingDistance)
                : FallbackMaxDistance;
            source.minDistance = 0f;
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                AnimationCurve.Constant(0f, 1f, 1f));
            source.rolloffMode = AudioRolloffMode.Custom;

            // Sprache darf ihre Tonhoehe nicht veraendern, wenn jemand rennt.
            source.dopplerLevel = 0f;

            // Reverb Zones der Szene wuerden unkontrolliert mit unserem eigenen Hall
            // konkurrieren. Raumklang kommt ausschliesslich aus den VoiceZones.
            source.bypassReverbZones = true;
        }

        private void AttachFilters()
        {
            // Die Reihenfolge der Komponenten ist die Reihenfolge der Signalverarbeitung.
            // Der Vivox-Tap liegt bereits auf dem GameObject, alles Folgende haengt sich
            // korrekt dahinter.
            lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = VoiceSample.NoLowPass;
            lowPass.lowpassResonanceQ = 1f;

            highPass = gameObject.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = VoiceSample.NoHighPass;
            highPass.highpassResonanceQ = 1f;

            // Hall kostet spuerbar Rechenzeit pro Sprecher und ist nur sinnvoll, wenn das
            // Projekt ueberhaupt Raeume definiert. Sonst bleibt der Filter weg.
            if (profile == null || !profile.EnableReverb) return;

            reverb = gameObject.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.dryLevel = 0f;
            reverb.room = NoReverbRoomLevel;
        }

        /// <summary>Setzt das Ziel, auf das ab jetzt hingeblendet wird.</summary>
        public void SetTarget(in VoiceSample value)
        {
            target = value;
            target.Clamp();
        }

        /// <summary>Bringt die Stimme sofort zum Schweigen, ohne Ausblenden.</summary>
        public void SilenceImmediately()
        {
            current.Volume = 0f;
            target.Volume = 0f;
            Apply();
        }

        /// <summary>
        /// Spielt eine Schleife ab. Nur fuer die Teststimme im Testraum, nicht fuer Vivox.
        /// </summary>
        internal void PlayLoop(AudioClip clip)
        {
            if (source == null || clip == null) return;
            source.clip = clip;
            source.loop = true;
            source.Play();
        }

        private void LateUpdate()
        {
            if (Anchor != null)
            {
                transform.position = Anchor.position;
            }

            // Unscaled, damit eine Pause oder Zeitlupe im Spiel die Stimmen nicht einfriert.
            float dt = Time.unscaledDeltaTime;
            float t = profile != null
                ? profile.GetSmoothingFactor(dt)
                : 1f - Mathf.Exp(-dt * 0.6931472f / FallbackSmoothingHalfLife);

            current.MoveTowards(target, t);
            Apply();
        }

        private void Apply()
        {
            if (source == null) return;

            source.volume = current.Muted ? 0f : current.Volume;
            source.spatialBlend = current.SpatialBlend;

            if (lowPass != null) lowPass.cutoffFrequency = current.LowPassHz;
            if (highPass != null) highPass.cutoffFrequency = current.HighPassHz;

            if (reverb != null)
            {
                // AudioReverbFilter rechnet in Dezibel. Der lineare Regler von 0 bis 1 wird
                // hier auf einen hoerbaren Bereich abgebildet: unten praktisch trocken,
                // oben ein deutlicher Raumanteil.
                reverb.room = Mathf.Lerp(NoReverbRoomLevel, FullReverbRoomLevel, current.ReverbMix);
            }
        }
    }
}
