using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lautsprecher an einem Walkie: Remote-Gewinner oder lokales Sidetone, mit Delay.
    /// <para>
    /// Wichtig: <see cref="OnAudioFilterRead"/> laeuft auf dem Audio-Thread —
    /// dort kein <c>AudioSettings</c>, keine Allokationen, keine Unity-API.
    /// Der Delay-Ring arbeitet in MONO-Frames (siehe <see cref="WalkieRadioBus"/>);
    /// am Ende wird jeder Mono-Frame auf alle Ausgabe-Kanaele verteilt. So bleibt
    /// die Zeitbasis unabhaengig von Unity's Kanalzahl korrekt (kein Chipmunk-Sound).
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(AudioSource))]
    internal sealed class WalkieDeviceOutput : MonoBehaviour
    {
        private const int MaxDelaySeconds = 2;
        private const float VolumeSmoothPerSecond = 6f;

        // Nie naeher als das rechnen — sonst kann ein Geraet direkt am Ohr (z.B. Hand-Modell
        // sehr nah am Kopf) auf maximale Lautstaerke kommen und mit dem Mikro echte akustische
        // Rueckkopplung ("Heulen") ausloesen, die von Aufnahme zu Aufnahme lauter wird.
        private const float MinPerceivedDistance = 0.9f;

        // Schutz gegen Duplikate/Desync (z.B. ein lokales Sicht-/Handmodell-Walkie, dessen
        // eigenes IsTransmitting-Flag nie gesetzt wird): ein Geraet direkt an der eigenen
        // Hoerposition darf niemals die eigene Stimme (Sidetone) abspielen, egal was sein
        // eigener Sende-Status sagt — der ganze Sinn von Sidetone ist, sich AN EINEM ANDEREN
        // Geraet zu hoeren, nicht am eigenen.
        private const float MinSidetoneSelfDistance = 0.5f;

        private EarshotWalkieTalkie walkie;
        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;

        private readonly WalkieAudioRing inbox = new WalkieAudioRing(48000);

        // Delay-Ring in MONO-Frames (nicht mit Kanalzahl multipliziert).
        private float[] delayRing;
        private int delayWrite;
        private bool delayPrimed;
        private float delaySeconds = 0.2f;
        private int outputChannels = 1;
        private int sampleRate = 48000;
        private string lastStreamId;

        private volatile int pendingChannels;
        private volatile bool delayReady;

        private float[] monoPullBuffer = new float[2048];
        private float smoothedVolume;
        private float radioCrunch;

        // Sample-and-Hold-Zustand fuer den Alter-Funk-Effekt (nur Audio-Thread).
        private int crunchHoldCounter;
        private float crunchHeldValue;

        internal void Bind(EarshotWalkieTalkie owner)
        {
            walkie = owner;
            CacheSampleRate();
            EnsureAudio();
            EnsureDelayCapacity();
            ApplyEq();
            WalkieRadioBus.Register(this);
        }

        private void OnEnable()
        {
            CacheSampleRate();
            EnsureDelayCapacity();
            WalkieRadioBus.Register(this);
        }

        private void OnDisable()
        {
            WalkieRadioBus.Unregister(this);
            delayReady = false;
        }

        internal void PushSamples(
            string channelId,
            string streamId,
            float[] data,
            int offset,
            int length)
        {
            if (walkie == null || !walkie.PoweredOn || walkie.IsTransmitting) return;
            if (!string.Equals(walkie.ChannelId, channelId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string wanted = ResolveStreamId();
            if (string.IsNullOrEmpty(wanted) ||
                !string.Equals(wanted, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(lastStreamId, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                inbox.Clear();
                lastStreamId = streamId;
            }

            inbox.Write(data, offset, length);
        }

        internal void ClearInbox(string channelId, string streamId)
        {
            if (walkie == null) return;
            if (!string.Equals(walkie.ChannelId, channelId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(lastStreamId, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                inbox.Clear();
            }
        }

        internal void ClearAllInbox()
        {
            inbox.Clear();
            lastStreamId = null;
        }

        private void CacheSampleRate()
        {
            try
            {
                int rate = AudioSettings.outputSampleRate;
                if (rate > 0) sampleRate = rate;
            }
            catch
            {
                // Scene-Load / falscher Thread — Default behalten.
            }
        }

        private void EnsureAudio()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
                if (source == null) source = gameObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            // Entfernung nur ueber unser Volume — Unity-Rolloff wuerde sonst doppelt daempfen.
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            source.minDistance = 0.4f;
            source.maxDistance = 50f;
            source.mute = false;
            source.volume = 0f;

            if (lowPass == null)
            {
                lowPass = GetComponent<AudioLowPassFilter>();
                if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            }

            if (highPass == null)
            {
                highPass = GetComponent<AudioHighPassFilter>();
                if (highPass == null) highPass = gameObject.AddComponent<AudioHighPassFilter>();
            }

            // Filter bleiben immer aktiv — Ein/Ausschalten der Komponente selbst
            // verursacht Klicks. Stille kommt allein ueber Lautstaerke/Samples.
            lowPass.enabled = true;
            highPass.enabled = true;

            if (source.clip == null)
            {
                source.clip = AudioClip.Create("EarshotWalkieOut", 256, 1, sampleRate, false);
                var zeros = new float[256];
                source.clip.SetData(zeros, 0);
            }

            if (!source.isPlaying) source.Play();
        }

        private void EnsureDelayCapacity()
        {
            int needed = Mathf.Max(1, MaxDelaySeconds * sampleRate);
            if (delayRing != null && delayRing.Length >= needed)
            {
                delayReady = true;
                return;
            }

            delayRing = new float[needed];
            delayWrite = 0;
            delayPrimed = false;
            delayReady = true;
        }

        private void LateUpdate()
        {
            if (walkie == null) return;

            CacheSampleRate();
            EnsureAudio();

            if (pendingChannels > 0 && pendingChannels != outputChannels)
            {
                outputChannels = pendingChannels;
            }

            EnsureDelayCapacity();
            ApplyEq();

            Transform anchor = walkie.AudioAnchor;
            if (anchor != null) transform.position = anchor.position;

            source.maxDistance = walkie.MaxHearingDistance;
            delaySeconds = walkie.TransmissionDelaySeconds;
            radioCrunch = walkie.RadioCrunch;

            bool active = ShouldOutput();
            float targetVolume = 0f;
            if (active)
            {
                bool sidetone = IsSidetoneMode();
                float baseVol = sidetone
                    ? WalkieTalkieRegistry.ActiveSidetoneWorldVolume
                    : walkie.RadioVolume;
                targetVolume = Mathf.Clamp01(baseVol * DistanceFalloff() * EarshotVoice.HeardVoiceVolume);
            }

            smoothedVolume = Mathf.MoveTowards(
                smoothedVolume,
                targetVolume,
                Time.unscaledDeltaTime * VolumeSmoothPerSecond);
            source.volume = smoothedVolume;
        }

        private bool IsSidetoneMode()
        {
            return WalkieTalkieRegistry.LocalIsTransmitting &&
                   string.Equals(
                       WalkieTalkieRegistry.LocalTransmitChannelId,
                       walkie.ChannelId,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldOutput()
        {
            if (walkie == null || !walkie.PoweredOn) return false;
            if (walkie.IsTransmitting) return false;

            if (IsSidetoneMode())
            {
                // Steht dieses Geraet praktisch an der eigenen Hoerposition (z.B. ein
                // Sicht-/Handmodell-Duplikat ohne synchronisierten Sendezustand), niemals
                // die eigene Stimme dort ausgeben — das waere kein Sidetone, sondern ein
                // Feedback-Kandidat direkt am eigenen Ohr.
                if (TryListenerPosition(out Vector3 selfListener) &&
                    Vector3.Distance(selfListener, transform.position) < MinSidetoneSelfDistance)
                {
                    return false;
                }

                return true;
            }

            if (WalkieTalkieRegistry.LocalIsTransmitting) return false;

            string winner = WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
            return !string.IsNullOrEmpty(winner);
        }

        private float DistanceFalloff()
        {
            // Kein bekannter Zuhoerer-Ort: lieber still als versehentlich auf voller
            // Lautstaerke senden (frueher wurde hier faelschlich 1f/volle Lautstaerke
            // zurueckgegeben).
            if (!TryListenerPosition(out Vector3 listener)) return 0f;
            float max = Mathf.Max(1f, walkie.MaxHearingDistance);
            float d = Mathf.Max(Vector3.Distance(listener, transform.position), MinPerceivedDistance);
            float t = 1f - Mathf.Clamp01(d / max);
            return t * t;
        }

        private static bool TryListenerPosition(out Vector3 position)
        {
            var local = VoiceRoster.LocalPlayer;
            if (local?.VoiceAnchor != null)
            {
                position = local.VoiceAnchor.position;
                return true;
            }

            // Nicht den erstbesten Listener der Szene nehmen — bei mehr als einem aktiven
            // AudioListener (Unity warnt davor, verhindert es aber nicht) waere das
            // nichtdeterministisch und koennte z.B. eine Lobby-/Verbindungs-UI-Kamera treffen.
            var listener = VoiceRoster.FindPreferredAudioListener();
            if (listener != null)
            {
                position = listener.transform.position;
                return true;
            }

            position = default;
            return false;
        }

        private void ApplyEq()
        {
            if (walkie == null) return;
            if (highPass != null) highPass.cutoffFrequency = walkie.HighPassHz;
            if (lowPass != null) lowPass.cutoffFrequency = walkie.LowPassHz;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                int ch = channels > 0 ? channels : 1;
                if (ch != outputChannels || !delayReady || delayRing == null)
                {
                    pendingChannels = ch;
                    Silence(data);
                    return;
                }

                int frames = data.Length / ch;
                if (frames <= 0)
                {
                    Silence(data);
                    return;
                }

                if (monoPullBuffer.Length < frames) monoPullBuffer = new float[frames];

                bool play = walkie != null && walkie.PoweredOn && !walkie.IsTransmitting;
                int got = play ? inbox.Read(monoPullBuffer, 0, frames) : 0;
                for (int i = got; i < frames; i++) monoPullBuffer[i] = 0f;

                float delaySec = delaySeconds;
                if (delaySec < 0f) delaySec = 0f;
                if (delaySec > 1.5f) delaySec = 1.5f;

                int rate = sampleRate > 0 ? sampleRate : 48000;
                int delaySamples = Mathf.CeilToInt(delaySec * rate);
                if (delaySamples < 1) delaySamples = 1;
                if (delaySamples > delayRing.Length) delaySamples = delayRing.Length;

                // Puffer kontinuierlich weiterschieben — nie fruehzeitig abbrechen,
                // sonst entsteht am naechsten Aufruf ein hoerbarer Sprung (Klacken).
                for (int f = 0; f < frames; f++)
                {
                    float incoming = monoPullBuffer[f];
                    int readIndex = delayWrite - delaySamples;
                    if (readIndex < 0) readIndex += delayRing.Length;

                    float outgoing = delayPrimed ? delayRing[readIndex] : 0f;
                    delayRing[delayWrite] = incoming;
                    delayWrite++;
                    if (delayWrite >= delayRing.Length)
                    {
                        delayWrite = 0;
                        delayPrimed = true;
                    }

                    outgoing = ApplyRadioCrunch(outgoing, radioCrunch);

                    int baseIdx = f * ch;
                    for (int c = 0; c < ch; c++) data[baseIdx + c] = outgoing;
                }
            }
            catch
            {
                Silence(data);
            }
        }

        /// <summary>
        /// Bewusster "altes Funkgeraet"-Charakter: grobe Stufen (Sample-and-Hold), reduzierte
        /// Aufloesung (Bit-Crush) und leichte weiche Verzerrung. Deutlich hoerbar von der
        /// glatten Mund-Stimme unterscheidbar, aber deterministisch (kein Knacken/Glitch).
        /// </summary>
        private float ApplyRadioCrunch(float sample, float amount)
        {
            if (amount <= 0.001f) return sample;

            int hold = 1 + Mathf.RoundToInt(amount * 2f);
            if (crunchHoldCounter <= 0)
            {
                crunchHeldValue = sample;
                crunchHoldCounter = hold;
            }

            crunchHoldCounter--;

            float levels = Mathf.Lerp(48f, 10f, amount);
            float quantized = Mathf.Round(crunchHeldValue * levels) / levels;

            float drive = 1f + amount * 1.2f;
            float distorted = Mathf.Clamp(quantized * drive, -1f, 1f);

            return Mathf.Lerp(sample, distorted, amount);
        }

        private static void Silence(float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
        }

        private string ResolveStreamId()
        {
            if (walkie == null) return null;

            if (IsSidetoneMode()) return WalkieRadioBus.LocalSidetoneStreamId;

            if (WalkieTalkieRegistry.LocalIsTransmitting) return null;

            return WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
        }
    }
}
