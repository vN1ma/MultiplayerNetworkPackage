using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lautsprecher an einem Walkie: Remote-Gewinner oder lokales Sidetone, mit Delay.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(AudioSource))]
    internal sealed class WalkieDeviceOutput : MonoBehaviour
    {
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

        private readonly float[] pullBuffer = new float[4096];

        internal void Bind(EarshotWalkieTalkie owner)
        {
            walkie = owner;
            EnsureAudio();
            ApplyEq();
            WalkieRadioBus.Register(this);
        }

        private void OnEnable()
        {
            WalkieRadioBus.Register(this);
        }

        private void OnDisable()
        {
            WalkieRadioBus.Unregister(this);
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

        private void EnsureAudio()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
                if (source == null) source = gameObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.4f;

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

            if (!source.isPlaying)
            {
                source.clip = AudioClip.Create("EarshotWalkieOut", 256, 1, 48000, false);
                var zeros = new float[256];
                source.clip.SetData(zeros, 0);
                source.loop = true;
                source.Play();
            }
        }

        private void LateUpdate()
        {
            if (walkie == null) return;
            EnsureAudio();
            ApplyEq();

            Transform anchor = walkie.AudioAnchor;
            if (anchor != null) transform.position = anchor.position;

            source.maxDistance = walkie.MaxHearingDistance;
            delaySeconds = walkie.TransmissionDelaySeconds;

            bool active = ShouldOutput();
            float volume = 0f;
            if (active)
            {
                volume = walkie.RadioVolume * DistanceFalloff() * EarshotVoice.HeardVoiceVolume;
            }

            source.volume = Mathf.Clamp01(volume);
            source.mute = volume <= 0.0001f;
        }

        private bool ShouldOutput()
        {
            if (walkie == null || !walkie.PoweredOn) return false;
            if (walkie.IsTransmitting) return false;

            if (WalkieTalkieRegistry.LocalIsTransmitting &&
                string.Equals(
                    WalkieTalkieRegistry.LocalTransmitChannelId,
                    walkie.ChannelId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

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

            if (channels != outputChannels || delayRing == null)
            {
                outputChannels = Mathf.Max(1, channels);
                sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                RebuildDelay();
            }

            if (walkie == null || !walkie.PoweredOn || walkie.IsTransmitting)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }

            int n = Mathf.Min(data.Length, pullBuffer.Length);
            inbox.Read(pullBuffer, 0, n);

            float delaySec = WalkieRules.ClampDelaySeconds(delaySeconds);
            if (delaySec <= 0.0001f || delayRing == null || delayRing.Length == 0)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = i < n ? pullBuffer[i] : 0f;
                }

                return;
            }

            int delaySamples = Mathf.Clamp(
                Mathf.CeilToInt(delaySec * sampleRate) * outputChannels,
                outputChannels,
                delayRing.Length);

            for (int i = 0; i < data.Length; i++)
            {
                float incoming = i < n ? pullBuffer[i] : 0f;
                int readIndex = delayWrite - delaySamples;
                while (readIndex < 0) readIndex += delayRing.Length;

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

        private void RebuildDelay()
        {
            int length = Mathf.Max(outputChannels, Mathf.CeilToInt(1.5f * sampleRate) * outputChannels);
            delayRing = new float[length];
            delayWrite = 0;
            delayPrimed = false;
        }
    }
}
