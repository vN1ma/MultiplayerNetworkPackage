using Unity.Services.Vivox.AudioTaps;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Verwaltet lokales Sidetone aus Vivox' bestehendem Capture-Stream.
    /// Oeffnet bewusst kein zweites Unity-Mikrofon.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieSidetoneCapture : MonoBehaviour
    {
        private const string DiagnosticRevision = "capture-pin-proximity-v3";

        private VivoxCaptureSourceTap captureTap;
        private WalkieVivoxCaptureFeed feed;
        private AudioSource tapSource;
        private GameObject tapObject;
        private readonly float[] outputDiagnosticBuffer = new float[256];
        private int lastTapId = int.MinValue;
        private float nextCreateAttempt;
        private float nextFlowDiagnostic;
        private bool deviceInventoryLogged;
        private bool lastWanted;
        private string lastChannel;

        internal static WalkieSidetoneCapture EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var capture = runtime.GetComponent<WalkieSidetoneCapture>();
            if (capture == null)
            {
                capture = runtime.gameObject.AddComponent<WalkieSidetoneCapture>();
            }

            return capture;
        }

        private void Update()
        {
            WalkieTalkieRegistry.EnsureLocalTransmitStillValid();
            EnsureCaptureTap();
            TryPinTapToProximityChannel(false);
            EnforceDirectOutputMute();

            bool ready = captureTap != null && captureTap.TapId >= 0 && feed != null;
            bool wanted = ready &&
                          WalkieTalkieRegistry.LocalIsTransmitting &&
                          !string.IsNullOrEmpty(WalkieTalkieRegistry.LocalTransmitChannelId);
            string channel = wanted
                ? WalkieTalkieRegistry.LocalTransmitChannelId
                : null;

            if (wanted != lastWanted ||
                !string.Equals(channel, lastChannel, System.StringComparison.OrdinalIgnoreCase))
            {
                bool wasWanted = lastWanted;
                feed?.SetCaptureState(wanted, channel);
                lastWanted = wanted;
                lastChannel = channel;

                if (wanted)
                {
                    VoiceSessionLog.Note(
                        $"WALKIE Sidetone an (Vivox Capture Tap {captureTap.TapId}, " +
                        $"input='{EarshotVoice.ActiveInputDeviceName}')");
                }
                else if (wasWanted)
                {
                    VoiceSessionLog.Note("WALKIE Sidetone aus (Vivox Capture Tap)");
                }
            }

            if (captureTap != null && captureTap.TapId != lastTapId)
            {
                lastTapId = captureTap.TapId;
                VoiceSessionLog.Note(
                    $"WALKIE Vivox-Capture-Tap Status: TapId={lastTapId}, " +
                    $"input='{EarshotVoice.ActiveInputDeviceName}', " +
                    $"outputRate={SafeOutputSampleRate()} Hz");
            }

            if (wanted && Time.unscaledTime >= nextFlowDiagnostic)
            {
                nextFlowDiagnostic = Time.unscaledTime + 2f;
                feed.ConsumeDiagnostics(out int callbacks, out int signalBlocks, out float peak);
                float directOutputPeak = ReadDirectOutputPeak();
                VoiceSessionLog.Note(
                    $"WALKIE CAPTURE FLOW: TapId={captureTap.TapId}, " +
                    $"tapChannel='{captureTap.ChannelName}', autoAcquire={captureTap.AutoAcquireChannel}, " +
                    $"callbacks={callbacks}, " +
                    $"signalBlocks={signalBlocks}, inputPeak={peak:0.0000}, " +
                    $"directOutputPeak={directOutputPeak:0.000000}, sourcePlaying={tapSource.isPlaying}, " +
                    $"sourceMute={tapSource.mute}, sourceVolume={tapSource.volume:0.000}, " +
                    $"channel='{channel}', revision='{DiagnosticRevision}'");
            }
        }

        private void OnDisable()
        {
            feed?.SetCaptureState(false, null);
            lastWanted = false;
            lastChannel = null;
        }

        private void EnsureCaptureTap()
        {
            if (captureTap != null || !EarshotVoice.IsConnected) return;
            if (Time.unscaledTime < nextCreateAttempt) return;
            nextCreateAttempt = Time.unscaledTime + 2f;

            try
            {
                if (tapObject != null) Destroy(tapObject);
                tapObject = new GameObject("Earshot Vivox Sidetone Tap");
                tapObject.transform.SetParent(transform, false);

                tapSource = tapObject.AddComponent<AudioSource>();
                tapSource.playOnAwake = false;
                tapSource.loop = false;
                tapSource.spatialBlend = 0f;
                tapSource.dopplerLevel = 0f;
                tapSource.volume = 1f;
                // Unity verarbeitet eine spielende, gemutete AudioSource weiterhin im
                // DSP-Graph. Damit bleibt der Capture-Callback aktiv, waehrend der
                // Source-Ausgang unabhaengig vom Filterpuffer hart stumm ist.
                tapSource.mute = true;

                // Reihenfolge ist wichtig: Vivox speist zuerst die AudioSource,
                // danach liest der Feed die Samples und nullt den direkten Mix.
                captureTap = tapObject.AddComponent<VivoxCaptureSourceTap>();
                feed = tapObject.AddComponent<WalkieVivoxCaptureFeed>();
                feed.Configure(SafeOutputSampleRate());

                // WICHTIG: Tap fest auf den Proximity-Kanal pinnen, BEVOR Funkkanaele
                // gejoint werden. Vivox' Auto-Acquire umregistriert den Tap sonst bei
                // JEDEM Kanal-Join auf den zuletzt gejointen Kanal - also auf den
                // Funkkanal, sobald ein Walkie an ist. Der native Capture-Tap liefert
                // in diesem Zustand waehrend der Funk-Transmission dauerhaft
                // NoMoreData und der Sidetone bleibt stumm (Log-Beweis 20260918-0612).
                TryPinTapToProximityChannel(true);

                VoiceSessionLog.Note(
                    $"WALKIE Vivox-Capture-Tap erstellt: TapId={captureTap.TapId}, " +
                    $"channel='{captureTap.ChannelName}', autoAcquire={captureTap.AutoAcquireChannel}, " +
                    $"input='{EarshotVoice.ActiveInputDeviceName}', hardMute={tapSource.mute}, " +
                    $"revision='{DiagnosticRevision}'");
                LogAudioDeviceInventory();
            }
            catch (System.Exception ex)
            {
                if (tapObject != null) Destroy(tapObject);
                tapObject = null;
                captureTap = null;
                feed = null;
                tapSource = null;
                VoiceSessionLog.Alert(
                    "WALKIE Vivox-Capture-Tap konnte nicht erstellt werden: " + ex.Message);
            }
        }

        /// <summary>
        /// Pinnt den Capture-Tap fest auf den Proximity-Kanal. Der ChannelName-Setter
        /// deaktiviert AutoAcquireChannel und registriert den Tap sofort auf genau
        /// diesen Kanal neu. Ohne Pinning wuerde Vivox den Tap bei jedem Kanal-Join
        /// (z.B. Walkie an) auf den Funkkanal umregistrieren - der Sidetone verstummt
        /// dann fuer die gesamte PTT-Phase. Der Pin bleibt auch bei spaeteren
        /// Funk-Joins/Leaves stabil, weil Vivox ohne Auto-Acquire nur beim Treffer
        /// des gepinnten Kanals neu registriert.
        /// </summary>
        private void TryPinTapToProximityChannel(bool reportMissingChannel)
        {
            if (captureTap == null) return;

            var vivoxBackend = VoiceRuntime.Instance != null
                ? VoiceRuntime.Instance.Backend as VivoxVoiceBackend
                : null;
            string proximityChannel = vivoxBackend != null
                ? vivoxBackend.ProximityChannelName
                : null;
            if (string.IsNullOrEmpty(proximityChannel))
            {
                if (reportMissingChannel)
                {
                    VoiceSessionLog.Alert(
                        "WALKIE Sidetone-Tap konnte nicht gepinnt werden: Proximity-Kanalname unbekannt.");
                }
                return;
            }

            if (!captureTap.AutoAcquireChannel &&
                string.Equals(captureTap.ChannelName, proximityChannel, System.StringComparison.OrdinalIgnoreCase))
            {
                return; // bereits gepinnt
            }

            captureTap.ChannelName = proximityChannel;

            VoiceSessionLog.Note(
                $"WALKIE Sidetone-Tap auf Proximity-Kanal '{proximityChannel}' gepinnt: " +
                $"TapId={captureTap.TapId}, autoAcquire={captureTap.AutoAcquireChannel}, " +
                $"revision='{DiagnosticRevision}'");

            if (captureTap.TapId < 0)
            {
                VoiceSessionLog.Alert(
                    $"WALKIE Sidetone-Tap-Pinning fehlgeschlagen: TapId={captureTap.TapId} " +
                    "(negativ = Vivox-Fehlercode, Details in der Unity-Konsole).");
            }
        }

        private void EnforceDirectOutputMute()
        {
            if (tapSource == null || tapSource.mute) return;

            tapSource.mute = true;
            VoiceSessionLog.Alert(
                "WALKIE Capture-Source war unerwartet ungemutet und wurde sofort stummgeschaltet.");
        }

        private float ReadDirectOutputPeak()
        {
            if (tapSource == null) return -1f;

            try
            {
                tapSource.GetOutputData(outputDiagnosticBuffer, 0);
                float peak = 0f;
                for (int i = 0; i < outputDiagnosticBuffer.Length; i++)
                {
                    float sample = outputDiagnosticBuffer[i];
                    float absolute = sample < 0f ? -sample : sample;
                    if (absolute > peak) peak = absolute;
                }

                return peak;
            }
            catch
            {
                return -1f;
            }
        }

        private void LogAudioDeviceInventory()
        {
            if (deviceInventoryLogged) return;
            deviceInventoryLogged = true;

            string[] inputs = EarshotVoice.InputDeviceNames;
            string[] outputs = EarshotVoice.OutputDeviceNames;
            VoiceSessionLog.Note(
                $"AUDIO DEVICES: activeInput='{EarshotVoice.ActiveInputDeviceName}', " +
                $"activeOutput='{EarshotVoice.ActiveOutputDeviceName}', " +
                $"inputs=[{string.Join(" | ", inputs)}], outputs=[{string.Join(" | ", outputs)}]");
        }

        private static int SafeOutputSampleRate()
        {
            try
            {
                int rate = AudioSettings.outputSampleRate;
                return rate > 0 ? rate : 48000;
            }
            catch
            {
                return 48000;
            }
        }
    }

    /// <summary>
    /// Audio-Thread-Feed hinter dem VivoxCaptureSourceTap. Das Tap-Signal wird nur
    /// in den Walkie-Bus geschrieben und danach aus dem direkten Unity-Mix entfernt.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieVivoxCaptureFeed : MonoBehaviour
    {
        private const float GateOpenThreshold = 0.05f;
        private const float GateCloseThreshold = 0.025f;
        private const float SustainSecondsToOpen = 0.09f;
        private const float GainAttackPerSecond = 14f;
        private const float GainReleasePerSecond = 6f;
        private const float SidetoneGain = 0.55f;

        private float[] monoBuffer = new float[4096];
        private volatile bool captureActive;
        private volatile string captureChannel;
        private int sampleRate = 48000;
        private float envelope;
        private float aboveThresholdSeconds;
        private float gain;
        private int diagnosticCallbacks;
        private int diagnosticSignalBlocks;
        private volatile float diagnosticPeak;

        internal void Configure(int outputSampleRate)
        {
            if (outputSampleRate > 0) sampleRate = outputSampleRate;
        }

        internal void SetCaptureState(bool active, string channel)
        {
            string previousChannel = captureChannel;
            captureActive = active;
            captureChannel = active ? channel : null;
            ResetGate();

            if (!string.IsNullOrEmpty(previousChannel))
            {
                WalkieRadioBus.ClearStream(
                    previousChannel,
                    WalkieRadioBus.LocalSidetoneStreamId);
            }
        }

        internal void ConsumeDiagnostics(out int callbacks, out int signalBlocks, out float peak)
        {
            callbacks = System.Threading.Interlocked.Exchange(ref diagnosticCallbacks, 0);
            signalBlocks = System.Threading.Interlocked.Exchange(ref diagnosticSignalBlocks, 0);
            peak = diagnosticPeak;
            diagnosticPeak = 0f;
        }

        private void OnDisable()
        {
            SetCaptureState(false, null);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0) return;

            int channelCount = channels > 0 ? channels : 1;
            int frames = data.Length / channelCount;
            if (frames > 0 && captureActive && !string.IsNullOrEmpty(captureChannel))
            {
                System.Threading.Interlocked.Increment(ref diagnosticCallbacks);
                EnsureCapacity(frames);
                Downmix(data, channelCount, frames);
                ApplySustainGateAndGain(frames);
                WalkieRadioBus.Write(
                    captureChannel,
                    WalkieRadioBus.LocalSidetoneStreamId,
                    monoBuffer,
                    0,
                    frames);
            }

            // Niemals direkt abspielen: hoerbar nur ueber WalkieDeviceOutput.
            System.Array.Clear(data, 0, data.Length);
        }

        private void Downmix(float[] data, int channels, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                int baseIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += data[baseIndex + channel];
                }

                monoBuffer[frame] = sum / channels;
            }
        }

        private void ApplySustainGateAndGain(int frameCount)
        {
            float peak = 0f;
            for (int i = 0; i < frameCount; i++)
            {
                float value = monoBuffer[i] < 0f ? -monoBuffer[i] : monoBuffer[i];
                if (value > peak) peak = value;
            }

            envelope += (peak - envelope) * (peak > envelope ? 0.5f : 0.15f);
            diagnosticPeak = peak;
            if (peak >= GateCloseThreshold)
            {
                System.Threading.Interlocked.Increment(ref diagnosticSignalBlocks);
            }
            float dt = frameCount / (float)(sampleRate > 0 ? sampleRate : 48000);

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
            float step = rate * dt;
            if (gain < targetGain)
            {
                gain += step;
                if (gain > targetGain) gain = targetGain;
            }
            else
            {
                gain -= step;
                if (gain < targetGain) gain = targetGain;
            }

            float finalGain = gain * SidetoneGain;
            for (int i = 0; i < frameCount; i++)
            {
                monoBuffer[i] *= finalGain;
            }
        }

        private void ResetGate()
        {
            envelope = 0f;
            aboveThresholdSeconds = 0f;
            gain = 0f;
        }

        private void EnsureCapacity(int frames)
        {
            if (monoBuffer.Length < frames) monoBuffer = new float[frames];
        }
    }
}
