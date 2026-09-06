using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Verzoegert den Funkton lokal. Sample-Rate nur auf dem Main-Thread lesen.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(50)]
    internal sealed class WalkieAudioDelay : MonoBehaviour
    {
        private float delaySeconds = 0.2f;
        private float[] ring;
        private int writeIndex;
        private int channels = 1;
        private int sampleRate = 48000;
        private bool primed;
        private volatile int pendingChannels;
        private volatile bool ready;

        public void SetDelaySeconds(float seconds)
        {
            float clamped = WalkieRules.ClampDelaySeconds(seconds);
            if (Mathf.Abs(clamped - delaySeconds) < 0.001f) return;
            delaySeconds = clamped;
            RebuildBuffer(channels);
        }

        private void OnEnable()
        {
            CacheSampleRate();
            RebuildBuffer(channels);
        }

        private void Update()
        {
            CacheSampleRate();
            int want = pendingChannels > 0 ? pendingChannels : channels;
            if (!ready || want != channels || ring == null)
            {
                RebuildBuffer(want);
            }
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
                // ignorieren
            }
        }

        private void RebuildBuffer(int channelCount)
        {
            channels = Mathf.Max(1, channelCount);
            int length = Mathf.Max(channels, Mathf.CeilToInt(1.5f * sampleRate) * channels);
            ring = new float[length];
            writeIndex = 0;
            primed = delaySeconds <= 0.0001f;
            ready = true;
        }

        private void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                int ch = channelCount > 0 ? channelCount : 1;
                if (!ready || ring == null || ch != channels)
                {
                    pendingChannels = ch;
                    return;
                }

                if (delaySeconds <= 0.0001f || ring.Length == 0) return;

                int rate = sampleRate > 0 ? sampleRate : 48000;
                int delaySamples = Mathf.CeilToInt(delaySeconds * rate) * channels;
                if (delaySamples < channels) delaySamples = channels;
                if (delaySamples > ring.Length) delaySamples = ring.Length;

                for (int i = 0; i < data.Length; i++)
                {
                    float incoming = data[i];
                    int readIndex = writeIndex - delaySamples;
                    if (readIndex < 0) readIndex += ring.Length;

                    float outgoing = primed ? ring[readIndex] : 0f;
                    ring[writeIndex] = incoming;
                    writeIndex++;
                    if (writeIndex >= ring.Length)
                    {
                        writeIndex = 0;
                        primed = true;
                    }

                    data[i] = outgoing;
                }
            }
            catch
            {
                // Audio-Thread darf nicht crashen.
            }
        }
    }
}
