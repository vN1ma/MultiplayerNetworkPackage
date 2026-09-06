using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lautsprecher an einem Walkie: Remote-Gewinner oder lokales Sidetone, mit Delay.
    /// <para>
    /// Wichtig: <see cref="OnAudioFilterRead"/> laeuft auf dem Audio-Thread —
    /// dort kein <c>AudioSettings</c>, keine Allokationen, keine Unity-API.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(AudioSource))]
    internal sealed class WalkieDeviceOutput : MonoBehaviour
    {
        private const int MaxDelaySeconds = 2;

        private EarshotWalkieTalkie walkie;
        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;

        private readonly WalkieAudioRing inbox = new WalkieAudioRing(48000);
        private float[] delayRing;
        private int delayWrite;
        private bool delayPrimed;
        private float delaySeconds = 0.2f;
        private int outputChannels = 1;
        private int sampleRate = 48000;
        private string lastStreamId;

        // Audio-Thread setzt nur Flags; Main-Thread baut den Delay-Puffer.
        private volatile int pendingChannels;
        private volatile bool delayReady;

        private readonly float[] pullBuffer = new float[4096];

        internal void Bind(EarshotWalkieTalkie owner)
        {
            walkie = owner;
            CacheSampleRate();
            EnsureAudio();
            EnsureDelayCapacity(outputChannels);
            ApplyEq();
            WalkieRadioBus.Register(this);
        }

        private void OnEnable()
        {
            CacheSampleRate();
            EnsureDelayCapacity(outputChannels);
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
            // Entfernung nur ueber unser Volume — Unity-Rolloff wuerde sonst doppelt/komisch daempfen.
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            source.minDistance = 0.4f;
            source.maxDistance = 50f;
            source.mute = true;
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

            // EQ erst aktiv, wenn wirklich Funkton laeuft — sonst faerbt der HighPass
            // Stille/Rauschen und stoert die Szene.
            lowPass.enabled = false;
            highPass.enabled = false;

            if (source.clip == null)
            {
                source.clip = AudioClip.Create("EarshotWalkieOut", 256, 1, sampleRate, false);
                var zeros = new float[256];
                source.clip.SetData(zeros, 0);
            }

            if (!source.isPlaying) source.Play();
        }

        private void EnsureDelayCapacity(int channels)
        {
            channels = Mathf.Max(1, channels);
            int needed = Mathf.Max(channels, MaxDelaySeconds * sampleRate * channels);
            if (delayRing != null && delayRing.Length >= needed && outputChannels == channels)
            {
                delayReady = true;
                return;
            }

            outputChannels = channels;
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

            int want = pendingChannels > 0 ? pendingChannels : outputChannels;
            EnsureDelayCapacity(want);
            ApplyEq();

            Transform anchor = walkie.AudioAnchor;
            if (anchor != null) transform.position = anchor.position;

            source.maxDistance = walkie.MaxHearingDistance;
            delaySeconds = walkie.TransmissionDelaySeconds;

            bool active = ShouldOutput();
            bool sidetone = IsSidetoneMode();
            float volume = 0f;
            if (active)
            {
                float baseVol = sidetone
                    ? WalkieTalkieRegistry.ActiveSidetoneWorldVolume
                    : walkie.RadioVolume;
                volume = baseVol * DistanceFalloff() * EarshotVoice.HeardVoiceVolume;

                // Nah am Lautsprecher + offenes Mikro = Feedback. Sidetone in der Naehe leiser.
                if (sidetone && TryListenerPosition(out Vector3 listener))
                {
                    float d = Vector3.Distance(listener, transform.position);
                    float nearDuck = Mathf.Clamp01(d / 1.8f);
                    volume *= Mathf.Lerp(0.25f, 1f, nearDuck);
                }
            }

            bool audible = volume > 0.0001f;
            source.volume = Mathf.Clamp01(volume);
            source.mute = !audible;
            if (lowPass != null) lowPass.enabled = audible;
            if (highPass != null) highPass.enabled = audible;
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

            if (IsSidetoneMode()) return true;

            if (WalkieTalkieRegistry.LocalIsTransmitting) return false;

            string winner = WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
            return !string.IsNullOrEmpty(winner);
        }

        private float DistanceFalloff()
        {
            if (!TryListenerPosition(out Vector3 listener)) return 1f;
            float max = Mathf.Max(1f, walkie.MaxHearingDistance);
            float d = Vector3.Distance(listener, transform.position);
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

            var listener = Object.FindFirstObjectByType<AudioListener>();
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

                if (walkie == null || !walkie.PoweredOn || walkie.IsTransmitting)
                {
                    Silence(data);
                    return;
                }

                if (inbox.Available < outputChannels)
                {
                    // Kein Nachschub: Delay nicht mit Nullen fuettern (Klicken/Tacken).
                    Silence(data);
                    return;
                }

                int n = data.Length < pullBuffer.Length ? data.Length : pullBuffer.Length;
                inbox.Read(pullBuffer, 0, n);

                float delaySec = delaySeconds;
                if (delaySec < 0f) delaySec = 0f;
                if (delaySec > 1.5f) delaySec = 1.5f;

                if (delaySec <= 0.0001f)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = i < n ? pullBuffer[i] : 0f;
                    }

                    return;
                }

                int rate = sampleRate > 0 ? sampleRate : 48000;
                int delaySamples = Mathf.CeilToInt(delaySec * rate) * outputChannels;
                if (delaySamples < outputChannels) delaySamples = outputChannels;
                if (delaySamples > delayRing.Length) delaySamples = delayRing.Length;

                for (int i = 0; i < data.Length; i++)
                {
                    float incoming = i < n ? pullBuffer[i] : 0f;
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

                    data[i] = outgoing;
                }
            }
            catch
            {
                Silence(data);
            }
        }

        private static void Silence(float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
        }

        private string ResolveStreamId()
        {
            if (walkie == null) return null;

            if (WalkieTalkieRegistry.LocalIsTransmitting &&
                string.Equals(
                    WalkieTalkieRegistry.LocalTransmitChannelId,
                    walkie.ChannelId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return WalkieRadioBus.LocalSidetoneStreamId;
            }

            if (WalkieTalkieRegistry.LocalIsTransmitting) return null;

            return WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
        }
    }
}
