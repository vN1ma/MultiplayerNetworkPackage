using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Verzoegert den Funkton lokal (Walkie-Delay), ohne das Vivox-Signal zu aendern.
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

        public void SetDelaySeconds(float seconds)
        {
            float clamped = WalkieRules.ClampDelaySeconds(seconds);
            if (Mathf.Abs(clamped - delaySeconds) < 0.001f) return;
            delaySeconds = clamped;
            RebuildBuffer();
        }

        private void OnEnable()
        {
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            RebuildBuffer();
        }

        private void RebuildBuffer()
        {
            int length = Mathf.Max(channels, Mathf.CeilToInt(delaySeconds * sampleRate) * channels);
            if (length < channels) length = channels;
            ring = new float[length];
            writeIndex = 0;
            primed = delaySeconds <= 0.0001f;
        }

        private void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (data == null || data.Length == 0) return;

            if (channelCount != channels || ring == null)
            {
                channels = Mathf.Max(1, channelCount);
                sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : sampleRate;
                RebuildBuffer();
            }

            if (delaySeconds <= 0.0001f || ring == null || ring.Length == 0)
            {
                return;
            }

            int delaySamples = Mathf.Clamp(
                Mathf.CeilToInt(delaySeconds * sampleRate) * channels,
                channels,
                ring.Length);

            for (int i = 0; i < data.Length; i++)
            {
                float incoming = data[i];
                int readIndex = writeIndex - delaySamples;
                while (readIndex < 0) readIndex += ring.Length;

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
    }
}
