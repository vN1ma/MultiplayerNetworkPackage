using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lokales Sidetone: waehrend PTT Mikrofon → Bus (mit Noise-Gate gegen Feedback/Rauschen).
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieSidetoneCapture : MonoBehaviour
    {
        private const float GateOpen = 0.045f;
        private const float GateClose = 0.025f;
        private const float SidetoneGain = 0.55f;

        private string micDevice;
        private AudioClip micClip;
        private int lastMicPos = -1;
        private float[] readBuffer = new float[4096];
        private bool running;
        private bool gateOpen;
        private float envelope;

        internal static WalkieSidetoneCapture EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var c = runtime.GetComponent<WalkieSidetoneCapture>();
            if (c == null) c = runtime.gameObject.AddComponent<WalkieSidetoneCapture>();
            return c;
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
            gateOpen = false;
            envelope = 0f;
            WalkieRadioBus.ClearStream(
                WalkieTalkieRegistry.LocalTransmitChannelId,
                WalkieRadioBus.LocalSidetoneStreamId);
            VoiceSessionLog.Note("WALKIE Sidetone an (" + micDevice + ")");
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
            gateOpen = false;
            envelope = 0f;

            if (!string.IsNullOrEmpty(channel))
            {
                WalkieRadioBus.ClearStream(channel, WalkieRadioBus.LocalSidetoneStreamId);
            }
        }

        private void PumpMic()
        {
            if (micClip == null || string.IsNullOrEmpty(micDevice)) return;
            if (string.IsNullOrEmpty(WalkieTalkieRegistry.LocalTransmitChannelId)) return;

            int pos = Microphone.GetPosition(micDevice);
            if (pos < 0 || pos == lastMicPos) return;

            int samples = micClip.samples;
            int channels = Mathf.Max(1, micClip.channels);
            int frameCount = pos > lastMicPos
                ? pos - lastMicPos
                : samples - lastMicPos + pos;

            if (frameCount <= 0) return;

            int startFrame = lastMicPos;
            string channel = WalkieTalkieRegistry.LocalTransmitChannelId;

            if (startFrame + frameCount <= samples)
            {
                EnsureReadBuffer(frameCount * channels);
                micClip.GetData(readBuffer, startFrame);
                ProcessAndMaybeWrite(channel, readBuffer.Length);
            }
            else
            {
                int firstFrames = samples - startFrame;
                EnsureReadBuffer(firstFrames * channels);
                micClip.GetData(readBuffer, startFrame);
                ProcessAndMaybeWrite(channel, readBuffer.Length);

                int secondFrames = frameCount - firstFrames;
                if (secondFrames > 0)
                {
                    EnsureReadBuffer(secondFrames * channels);
                    micClip.GetData(readBuffer, 0);
                    ProcessAndMaybeWrite(channel, readBuffer.Length);
                }
            }

            lastMicPos = pos;
        }

        private void ProcessAndMaybeWrite(string channel, int length)
        {
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                float a = readBuffer[i];
                if (a < 0f) a = -a;
                if (a > peak) peak = a;
            }

            envelope = Mathf.Lerp(envelope, peak, peak > envelope ? 0.45f : 0.12f);

            if (!gateOpen && envelope >= GateOpen) gateOpen = true;
            else if (gateOpen && envelope <= GateClose) gateOpen = false;

            if (!gateOpen)
            {
                // Kein Rauschen/Tacken in den Bus — sonst Delay-Klicken am Geraet.
                return;
            }

            for (int i = 0; i < length; i++)
            {
                readBuffer[i] *= SidetoneGain;
            }

            WalkieRadioBus.Write(
                channel,
                WalkieRadioBus.LocalSidetoneStreamId,
                readBuffer,
                0,
                length);
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
