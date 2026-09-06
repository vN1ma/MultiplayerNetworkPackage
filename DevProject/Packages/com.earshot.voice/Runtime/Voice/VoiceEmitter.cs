using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Der Lautsprecher eines Sprechers in der Spielwelt.
    /// <para>
    /// Genau eine AudioSource - der Vivox-Tap selbst. Das ist der von Vivox vorgesehene
    /// Weg, raeumlichen Klang zu bekommen: die vom Tap gelieferte AudioSource an den
    /// Avatar haengen und ganz normal ueber Unity spatialisieren. Kein zweiter,
    /// kuenstlicher Lautsprecher, kein Ringpuffer, keine Ueberblendlogik - das waren
    /// selbst die Fehlerquelle, nicht die Loesung.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    public class VoiceEmitter : MonoBehaviour
    {
        private const float NoReverbRoomLevel = -10000f;
        private const float FullReverbRoomLevel = -1000f;
        private const float FallbackSmoothingHalfLife = 0.09f;
        private const float FallbackMaxDistance = 25f;

        /// <summary>
        /// Ab diesem Pegel im rohen Tap-Signal gilt "es kommt gerade wirklich etwas an".
        /// Nur fuer die Diagnose (Log), veraendert nie den Klang.
        /// </summary>
        private const float SignalPeak = 0.006f;

        /// <summary>Wie lange nach dem letzten erkannten Signal der Tap noch als "lebt" gilt.</summary>
        private const float SignalHoldSeconds = 0.6f;

        private AudioSource tap;
        private VoiceTapCapture capture;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;
        private AudioReverbFilter reverb;

        private VoiceProfile profile;
        private VoiceSample current = VoiceSample.Default;
        private VoiceSample target = VoiceSample.Default;

        private volatile bool sawSignalThisFrame;
        private float lastSignalTime = -10f;
        private bool debugLoop;

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

        /// <summary>2D (kein Avatar bekannt) oder 3D (am Kopf des Sprechers), oder stumm.</summary>
        public string PlaybackMode
        {
            get
            {
                // Nur wirklich stumm, wenn die Pipeline die Stimme abgeschaltet hat.
                // Lautstaerke nahe 0 durch Entfernung ist kein Sterben - sonst schreien
                // die Logs "AUDIO STIRBT", sobald jemand ein paar Meter weg steht.
                if (current.Muted) return "stumm";
                return current.SpatialBlend >= 0.5f ? "3D" : "2D";
            }
        }

        /// <summary>Wahr, sobald die Stimme raeumlich am Kopf des Sprechers liegt.</summary>
        public bool UsesHeadSpeaker => Anchor != null;

        /// <summary>Ob im rohen Tap-Signal gerade wirklich Audiodaten ankommen (Diagnose).</summary>
        public bool TapIsPlaying => Time.unscaledTime - lastSignalTime < SignalHoldSeconds;

        /// <summary>
        /// Ob das Vivox-Tap-Objekt selbst noch aktiv ist. Wenn nicht, laeuft
        /// OnAudioFilterRead ueberhaupt nicht mehr - ein anderer Fehlerfall als
        /// "Tap laeuft, liefert aber gerade kein Signal" (<see cref="TapIsPlaying"/>).
        /// </summary>
        public bool TapObjectActive => tap != null && tap.gameObject.activeInHierarchy;

        /// <summary>
        /// Der native Zustand der AudioSource selbst (<c>AudioSource.isPlaying</c>).
        /// <para>
        /// Vivox' eigener <c>VivoxAudioProcessor</c> pausiert diese AudioSource ganz
        /// von sich aus, wenn ueber rund 400 ms (20 Zyklen zu je 20 ms) keine neuen
        /// Netzwerk-Audiodaten fuer diesen Sprecher ankommen - unabhaengig von unserer
        /// eigenen Pipeline. Wird dieser Wert false, waehrend <see cref="TapIsPlaying"/>
        /// vorher "lebt" war, ist das der Beweis: nicht unser Code hat abgeschaltet,
        /// sondern Vivox selbst hat den Nachschub verloren (Netzwerk-Aussetzer, Jitter,
        /// oder - besonders im Multiplayer Play Mode mit zwei Editor-Instanzen auf einem
        /// Rechner - schlicht zu wenig CPU-Zeit fuer den Audio-Thread).
        /// </para>
        /// </summary>
        public bool TapAudioSourceIsPlaying => tap != null && tap.isPlaying;

        internal void Initialize(string playerId, AudioSource audioSource, VoiceProfile voiceProfile)
        {
            PlayerId = playerId;
            tap = audioSource;
            profile = voiceProfile;

            ConfigureTap();
            AttachFilters(gameObject);

            // Nur zur Diagnose: liest die rohen Samples mit, ruehrt sie aber nicht an.
            capture = gameObject.AddComponent<VoiceTapCapture>();
            capture.Emitter = this;

            current = VoiceSample.Default;
            current.Volume = 0.5f;
            target = current;
            Apply();
        }

        private void ConfigureTap()
        {
            if (tap == null) return;

            tap.playOnAwake = false;
            // Vivox schreibt die Stimme in einen rund 3-Sekunden-Clip und spielt den
            // als Endlosschleife. loop=false laesst genau diesen Clip einmal durchlaufen
            // und dann fuer immer verstummen - das war der "2 Sekunden und tot"-Fehler.
            tap.loop = true;
            tap.pitch = 1f;
            tap.volume = 0.5f;
            tap.dopplerLevel = 0f;
            tap.spatialize = false;
            tap.bypassReverbZones = true;
            tap.minDistance = 0f;
            tap.maxDistance = profile != null
                ? Mathf.Max(1f, profile.MaxHearingDistance)
                : FallbackMaxDistance;

            // Die Entfernungsdaempfung rechnet ausschliesslich die Pipeline. Unitys
            // eigene Rolloff-Kurve wuerde sonst ein zweites Mal daempfen.
            tap.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            tap.rolloffMode = AudioRolloffMode.Custom;
        }

        private void AttachFilters(GameObject host)
        {
            lowPass = host.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = VoiceSample.NoLowPass;
            lowPass.lowpassResonanceQ = 1f;

            highPass = host.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = VoiceSample.NoHighPass;
            highPass.highpassResonanceQ = 1f;

            if (profile == null || !profile.EnableReverb) return;

            reverb = host.AddComponent<AudioReverbFilter>();
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
            debugLoop = true;
            if (tap == null || clip == null) return;
            tap.clip = clip;
            tap.loop = true;
            tap.Play();
        }

        /// <summary>
        /// Liest nur mit, ob im rohen Tap-Signal ein Pegel ankommt. Aendert das Signal
        /// selbst nicht - reine Diagnose fuer die Logs, damit man sieht, ob Vivox
        /// ueberhaupt noch etwas liefert, unabhaengig davon, was die Pipeline daraus macht.
        /// </summary>
        internal void CaptureFromTap(float[] data, int channels)
        {
            if (debugLoop || data == null || data.Length == 0) return;

            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] >= 0f ? data[i] : -data[i];
                if (a > peak) peak = a;
            }

            if (peak >= SignalPeak) sawSignalThisFrame = true;
        }

        private void LateUpdate()
        {
            if (sawSignalThisFrame)
            {
                lastSignalTime = Time.unscaledTime;
                sawSignalThisFrame = false;
            }

            // Der Tap ist die einzige Quelle: er wird an den Kopf des Sprechers gehaengt,
            // damit Unitys eigenes Panning und Doppler-freies 3D-Audio direkt daraus
            // entstehen. So sah es Vivox' "Audio Tap" von Anfang an vor.
            if (!debugLoop && tap != null && Anchor != null)
            {
                tap.transform.position = Anchor.position;
            }

            KeepVivoxStreamAlive();

            float dt = Time.unscaledDeltaTime;
            float t = profile != null
                ? profile.GetSmoothingFactor(dt)
                : 1f - Mathf.Exp(-dt * 0.6931472f / FallbackSmoothingHalfLife);

            current.MoveTowards(target, t);
            Apply();
        }

        /// <summary>
        /// Stellt sicher, dass niemand den Vivox-Ringpuffer-Clip aus der Schleife nimmt.
        /// Wenn loop doch false geworden ist und der Clip deshalb zu Ende gelaufen ist,
        /// starten wir ihn neu - Vivox' Pause bei Stille lassen wir in Ruhe.
        /// </summary>
        private void KeepVivoxStreamAlive()
        {
            if (debugLoop || tap == null) return;

            if (tap.loop) return;

            tap.loop = true;
            VoiceSessionLog.Alert(
                "TAP-LOOP war aus - Vivox-Ringpuffer waere nach ~3s tot. Loop wieder an.");
            if (tap.clip != null && !tap.isPlaying)
            {
                tap.Play();
            }
        }

        private void Apply()
        {
            if (tap == null) return;

            float volume = current.Muted ? 0f : current.Volume * EarshotVoice.HeardVoiceVolume;

            tap.volume = volume;
            tap.spatialBlend = current.SpatialBlend;
            tap.pitch = 1f;
            tap.dopplerLevel = 0f;

            // Die Richtung liefert allein die Position (Tap sitzt am Kopf des Sprechers)
            // zusammen mit spatialBlend. Ein manuelles Stereo-Pan wuerde das nur verzerren.
            tap.panStereo = 0f;

            if (lowPass != null) lowPass.cutoffFrequency = current.LowPassHz;
            if (highPass != null) highPass.cutoffFrequency = current.HighPassHz;

            if (reverb != null)
            {
                reverb.room = Mathf.Lerp(NoReverbRoomLevel, FullReverbRoomLevel, current.ReverbMix);
            }
        }

        private void OnDestroy()
        {
            tap = null;
        }
    }

    [AddComponentMenu("")]
    internal sealed class VoiceTapCapture : MonoBehaviour
    {
        internal VoiceEmitter Emitter;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (Emitter == null) return;
            Emitter.CaptureFromTap(data, channels);
        }
    }
}
