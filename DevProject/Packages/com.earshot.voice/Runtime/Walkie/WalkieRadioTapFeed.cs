using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Speist Vivox-Funk-Samples in den Bus und macht den Tap selbst stumm
    /// (Wiedergabe laeuft nur an den Walkie-Geraeten).
    /// <para>
    /// Der Bus transportiert ausschliesslich MONO-Frames (ein Float pro Sample).
    /// Mehrkanaliges Tap-Signal wird hier auf Mono heruntergemischt, damit alle
    /// Konsumenten (WalkieDeviceOutput) unabhaengig von Unity's Ausgabe-Kanalzahl
    /// dieselbe Zeitbasis nutzen.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(100)]
    internal sealed class WalkieRadioTapFeed : MonoBehaviour
    {
        internal VoiceEmitter Emitter;

        private float[] monoBuffer = new float[2048];

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0 || Emitter == null) return;
            if (Emitter.PathKind != VoicePathKind.Radio) return;

            int ch = channels > 0 ? channels : 1;
            int frames = data.Length / ch;
            if (frames <= 0)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }

            if (monoBuffer.Length < frames) monoBuffer = new float[frames];

            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                int baseIdx = f * ch;
                for (int c = 0; c < ch; c++) sum += data[baseIdx + c];
                monoBuffer[f] = sum / ch;
            }

            WalkieRadioBus.Write(Emitter.ChannelId, Emitter.PlayerId, monoBuffer, 0, frames);

            // Tap selbst nicht abspielen — sonst Doppelton + nur eine Position.
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
        }
    }
}
