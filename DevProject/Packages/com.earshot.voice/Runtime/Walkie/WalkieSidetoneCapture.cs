using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lokales Sidetone: waehrend PTT Mikrofon → Bus (mono, Sustain-Gate gegen
    /// Fussschritte/Klicks, kontinuierlicher Fluss gegen Aussetzer/Knacken).
    /// <para>
    /// Wichtig: Mikrofon nimmt mit derselben Rate auf wie Unity ausgibt
    /// (<see cref="AudioSettings.outputSampleRate"/>) — eine feste 16 kHz-Aufnahme,
    /// die 1:1 in eine 48 kHz-Ausgabe lief, war der Hauptgrund fuer den
    /// roboterhaften/zu schnellen Klang (Pitch-Fehler durch fehlendes Resampling).
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieSidetoneCapture : MonoBehaviour
    {
        private const float GateOpenThreshold = 0.05f;
        private const float GateCloseThreshold = 0.025f;
        private const float SustainSecondsToOpen = 0.09f;
        private const float GainAttackPerSecond = 14f;
        private const float GainReleasePerSecond = 6f;
        private const float SidetoneGain = 0.55f;

        private string micDevice;
        private AudioClip micClip;
        private int lastMicPos = -1;
        private int captureSampleRate = 48000;

        private float[] rawBuffer = new float[4096];
        private float[] monoBuffer = new float[4096];

        private bool running;
        private float envelope;
        private float aboveThresholdSeconds;
        private float gain;

        private bool prewarmed;

        internal static WalkieSidetoneCapture EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var c = runtime.GetComponent<WalkieSidetoneCapture>();
            if (c == null) c = runtime.gameObject.AddComponent<WalkieSidetoneCapture>();
            c.Prewarm();
            return c;
        }

        private void Awake()
        {
            CacheSampleRate();
        }

        /// <summary>
        /// Startet/stoppt das Mikrofon einmal ganz kurz beim Verbindungsaufbau statt beim
        /// ersten echten PTT-Druck. <c>Microphone.Start</c> kann in Unity beim allerersten
        /// Aufruf spuerbar rucken (Betriebssystem initialisiert das Geraet) — lieber jetzt,
        /// waehrend eh schon Login/Verbindung laeuft, als mitten im Spiel beim Reinsprechen.
        /// </summary>
        private void Prewarm()
        {
            if (prewarmed) return;
            prewarmed = true;

            if (Microphone.devices == null || Microphone.devices.Length == 0) return;

            try
            {
                CacheSampleRate();
                string device = Microphone.devices[0];
                var clip = Microphone.Start(device, false, 1, captureSampleRate);
                if (clip != null) Microphone.End(device);
            }
            catch (System.Exception ex)
            {
                EarshotVoiceLog.Warn("Mikrofon-Vorwaermen fehlgeschlagen: " + ex.Message);
            }
        }

        private void CacheSampleRate()
        {
            try
            {
                int rate = AudioSettings.outputSampleRate;
                if (rate > 0) captureSampleRate = rate;
            }
            catch
            {
                // Default behalten.
            }
        }

        private void Update()
        {
            WalkieTalkieRegistry.EnsureLocalTransmitStillValid();

            bool want = WalkieTalkieRegistry.LocalIsTransmitting &&
                        !string.IsNullOrEmpty(WalkieTalkieRegistry.LocalTransmitChannelId);

            if (want && !running) StartMic();
            if (!want && running) StopMic();
            if (!running) return;

            PumpMic();
        }

        private void OnDisable() => StopMic();

        private void StartMic()
        {
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                EarshotVoiceLog.Warn("Walkie-Sidetone: kein Mikrofon gefunden.");
                return;
            }

            CacheSampleRate();

            micDevice = null;
            string preferred = EarshotVoice.ActiveInputDeviceName;
            for (int i = 0; i < Microphone.devices.Length; i++)
            {
                string name = Microphone.devices[i];
                if (EarshotVoice.IsUnusableAudioDevice(name)) continue;
                if (!string.IsNullOrEmpty(preferred) &&
                    string.Equals(preferred, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    micDevice = name;
                    break;
                }

                micDevice ??= name;
            }

            if (string.IsNullOrEmpty(micDevice))
            {
                micDevice = Microphone.devices[0];
            }

            // Gleiche Rate wie die Ausgabe — sonst Pitch-/Geschwindigkeitsfehler
            // beim Abspielen am anderen Walkie (klingt roboterhaft/zu schnell).
            micClip = Microphone.Start(micDevice, true, 1, captureSampleRate);
            lastMicPos = 0;
            running = true;
            envelope = 0f;
            aboveThresholdSeconds = 0f;
            gain = 0f;

            string channel = WalkieTalkieRegistry.LocalTransmitChannelId;
            if (!string.IsNullOrEmpty(channel))
            {
                WalkieRadioBus.ClearStream(channel, WalkieRadioBus.LocalSidetoneStreamId);
            }

            VoiceSessionLog.Note("WALKIE Sidetone an (" + micDevice + ", " + captureSampleRate + " Hz)");
        }

        private void StopMic()
        {
            string channel = WalkieTalkieRegistry.LocalTransmitChannelId;

            if (!string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice))
            {
                Microphone.End(micDevice);
            }

            micClip = null;
            micDevice = null;
            lastMicPos = -1;
            running = false;
            envelope = 0f;
            aboveThresholdSeconds = 0f;
            gain = 0f;

            if (!string.IsNullOrEmpty(channel))
            {
                WalkieRadioBus.ClearStream(channel, WalkieRadioBus.LocalSidetoneStreamId);
            }
        }

        private void PumpMic()
        {
            if (micClip == null || string.IsNullOrEmpty(micDevice)) return;
            string channel = WalkieTalkieRegistry.LocalTransmitChannelId;
            if (string.IsNullOrEmpty(channel)) return;

            int pos = Microphone.GetPosition(micDevice);
            if (pos < 0 || pos == lastMicPos) return;

            int samples = micClip.samples;
            int channels = Mathf.Max(1, micClip.channels);
            int frameCount = pos > lastMicPos
                ? pos - lastMicPos
                : samples - lastMicPos + pos;

            if (frameCount <= 0) return;

            int startFrame = lastMicPos;

            if (startFrame + frameCount <= samples)
            {
                ReadAndProcess(channel, startFrame, frameCount, channels);
            }
            else
            {
                int firstFrames = samples - startFrame;
                ReadAndProcess(channel, startFrame, firstFrames, channels);

                int secondFrames = frameCount - firstFrames;
                if (secondFrames > 0)
                {
                    ReadAndProcess(channel, 0, secondFrames, channels);
                }
            }

            lastMicPos = pos;
        }

        private void ReadAndProcess(string channel, int startFrame, int frameCount, int channels)
        {
            int rawLength = frameCount * channels;
            EnsureCapacity(frameCount, rawLength);

            micClip.GetData(rawBuffer, startFrame);

            // Downmix auf Mono — der Bus transportiert ausschliesslich Mono-Frames.
            for (int f = 0; f < frameCount; f++)
            {
                float sum = 0f;
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++) sum += rawBuffer[baseIdx + c];
                monoBuffer[f] = sum / channels;
            }

            ApplySustainGateAndGain(frameCount);

            WalkieRadioBus.Write(channel, WalkieRadioBus.LocalSidetoneStreamId, monoBuffer, 0, frameCount);
        }

        /// <summary>
        /// Kein hartes An/Aus (das erzeugt Knacken) — stattdessen ein weich
        /// nachziehender Gain, der erst oeffnet, wenn der Pegel eine kurze Zeit
        /// (<see cref="SustainSecondsToOpen"/>) am Stueck ueber der Schwelle bleibt.
        /// Kurze Transienten wie Fussschritt-Klicks bleiben so meist unten der
        /// Schwelle bzw. zu kurz, um den Gate zu oeffnen.
        /// </summary>
        private void ApplySustainGateAndGain(int frameCount)
        {
            float dt = frameCount / (float)Mathf.Max(1, captureSampleRate);

            float peak = 0f;
            for (int i = 0; i < frameCount; i++)
            {
                float a = monoBuffer[i];
                if (a < 0f) a = -a;
                if (a > peak) peak = a;
            }

            envelope = Mathf.Lerp(envelope, peak, peak > envelope ? 0.5f : 0.15f);

            if (envelope >= GateOpenThreshold)
            {
                aboveThresholdSeconds += dt;
            }
            else if (envelope <= GateCloseThreshold)
            {
                aboveThresholdSeconds = 0f;
            }

            float targetGain = aboveThresholdSeconds >= SustainSecondsToOpen ? 1f : 0f;
            float rate = targetGain > gain ? GainAttackPerSecond : GainReleasePerSecond;
            gain = Mathf.MoveTowards(gain, targetGain, rate * dt);

            float finalGain = gain * SidetoneGain;
            for (int i = 0; i < frameCount; i++)
            {
                monoBuffer[i] *= finalGain;
            }
        }

        private void EnsureCapacity(int frameCount, int rawLength)
        {
            if (rawBuffer.Length < rawLength) rawBuffer = new float[rawLength];
            if (monoBuffer.Length < frameCount) monoBuffer = new float[frameCount];
        }
    }
}
