using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Speist Vivox-Funk-Samples in den Bus und macht den Tap selbst stumm
    /// (Wiedergabe laeuft nur an den Walkie-Geraeten).
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(100)]
    internal sealed class WalkieRadioTapFeed : MonoBehaviour
    {
        internal VoiceEmitter Emitter;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0 || Emitter == null) return;
            if (Emitter.PathKind != VoicePathKind.Radio) return;

            WalkieRadioBus.Write(Emitter.ChannelId, Emitter.PlayerId, data, 0, data.Length);

            // Tap selbst nicht abspielen — sonst Doppelton + nur eine Position.
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
        }
    }
}
