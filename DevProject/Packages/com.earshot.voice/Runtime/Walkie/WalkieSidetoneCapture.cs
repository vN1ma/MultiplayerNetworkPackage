using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lokales Sidetone: waehrend PTT Mikrofon → Bus, damit andere Walkies
    /// die eigene Stimme versetzt abspielen.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieSidetoneCapture : MonoBehaviour
    {
        private string micDevice;
        private AudioClip micClip;
        private int lastMicPos = -1;
        private float[] readBuffer = new float[4096];
        private bool running;

        internal static WalkieSidetoneCapture EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var c = runtime.GetComponent<WalkieSidetoneCapture>();
            if (c == null) c = runtime.gameObject.AddComponent<WalkieSidetoneCapture>();
            return c;
        }

        private void Update()
        {
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

            micClip = Microphone.Start(micDevice, true, 1, 16000);
            lastMicPos = 0;
            running = true;
            WalkieRadioBus.ClearStream(
                WalkieTalkieRegistry.LocalTransmitChannelId,
                WalkieRadioBus.LocalSidetoneStreamId);
            VoiceSessionLog.Note("WALKIE Sidetone an (" + micDevice + ")");
        }

        private void StopMic()
        {
            if (!string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice))
            {
                Microphone.End(micDevice);
            }

            micClip = null;
            micDevice = null;
            lastMicPos = -1;
            running = false;
        }

        private void PumpMic()
        {
            if (micClip == null || string.IsNullOrEmpty(micDevice)) return;

            int pos = Microphone.GetPosition(micDevice);
            if (pos < 0 || pos == lastMicPos) return;

            int samples = micClip.samples;
            int channels = Mathf.Max(1, micClip.channels);
            int frameCount;

            if (pos > lastMicPos) frameCount = pos - lastMicPos;
            else frameCount = samples - lastMicPos + pos;

            if (frameCount <= 0) return;

            int startFrame = lastMicPos;

            if (startFrame + frameCount <= samples)
            {
                EnsureReadBuffer(frameCount * channels);
                micClip.GetData(readBuffer, startFrame);
                WalkieRadioBus.Write(
                    WalkieTalkieRegistry.LocalTransmitChannelId,
                    WalkieRadioBus.LocalSidetoneStreamId,
                    readBuffer,
                    0,
                    readBuffer.Length);
            }
            else
            {
                int firstFrames = samples - startFrame;
                EnsureReadBuffer(firstFrames * channels);
                micClip.GetData(readBuffer, startFrame);
                WalkieRadioBus.Write(
                    WalkieTalkieRegistry.LocalTransmitChannelId,
                    WalkieRadioBus.LocalSidetoneStreamId,
                    readBuffer,
                    0,
                    readBuffer.Length);

                int secondFrames = frameCount - firstFrames;
                if (secondFrames > 0)
                {
                    EnsureReadBuffer(secondFrames * channels);
                    micClip.GetData(readBuffer, 0);
                    WalkieRadioBus.Write(
                        WalkieTalkieRegistry.LocalTransmitChannelId,
                        WalkieRadioBus.LocalSidetoneStreamId,
                        readBuffer,
                        0,
                        readBuffer.Length);
                }
            }

            lastMicPos = pos;
        }

        private void EnsureReadBuffer(int floats)
        {
            if (readBuffer == null || readBuffer.Length != floats)
            {
                readBuffer = new float[Mathf.Max(1, floats)];
            }
        }
    }
}
