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
        private const string DiagnosticRevision = "leak-hunt-v10";

        private VivoxCaptureSourceTap captureTap;
        private WalkieVivoxCaptureFeed feed;
        private AudioSource tapSource;
        private GameObject tapObject;
        private readonly float[] outputDiagnosticBuffer = new float[256];
        private int lastTapId = int.MinValue;
        private float nextCreateAttempt;
        private float nextFlowDiagnostic;
        private bool deviceInventoryLogged;
        private bool diagnosticHardMute;
        private float nextInventoryLog;
        private bool lastWanted;
        private string lastChannel;
        private string lastPinnedChannel;
        private bool reportedMissingPinTarget;
        private float nextPinFailureAlert;
        private float nextRegisterRetry;
        private string[] txChannelSnapshot = new string[0];
        private float nextTxSnapshotRefresh;

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
            TryPinTapToActiveChannel();
            EnforceDirectOutputUnmuted();
            HandleDiagnosticHotkeys();

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
                nextFlowDiagnostic = Time.unscaledTime + 1f;
                feed.ConsumeDiagnostics(out int callbacks, out int signalBlocks, out float peak);
                float directOutputPeak = ReadDirectOutputPeak();
                RefreshTxChannelSnapshot();
                bool tapInTx = ContainsChannel(txChannelSnapshot, captureTap.ChannelName);
                VoiceSessionLog.Note(
                    $"WALKIE CAPTURE FLOW: TapId={captureTap.TapId}, " +
                    $"tapChannel='{captureTap.ChannelName}', autoAcquire={captureTap.AutoAcquireChannel}, " +
                    $"txChannels=[{string.Join(" | ", txChannelSnapshot)}], tapInTx={tapInTx}, " +
                    $"callbacks={callbacks}, " +
                    $"signalBlocks={signalBlocks}, inputPeak={peak:0.0000}, " +
                    $"directOutputPeak={directOutputPeak:0.000000}, sourcePlaying={tapSource.isPlaying}, " +
                    $"sourceMute={tapSource.mute}, sourceVolume={tapSource.volume:0.000}, " +
                    $"channel='{channel}', revision='{DiagnosticRevision}'");
                LogAudioSourceInventory();
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
                // v7 (Log 20260918-0735): AudioSource.mute NULLED die Samples, die
                // OnAudioFilterRead erreichen — der Callback lief weiter (callbacks>0),
                // lieferte aber exakt 0.0000 (signalBlocks=0), obwohl der native Tap
                // Daten hatte (sourcePlaying=True = keine NoMoreData-Pause). Das Mute
                // aus 'capture-hardmute-v2' war der Killer, nicht der Kanal-Pin.
                // Die Direktausgabe bleibt stattdessen ueber den Feed stumm: Der Feed
                // nullt den Puffer am Ende von OnAudioFilterRead (e46d900-Design,
                // funktioniert im einzigen guten Lauf 20260918-0551).
                tapSource.mute = false;

                // Reihenfolge ist wichtig: Vivox speist zuerst die AudioSource,
                // danach liest der Feed die Samples und nullt den direkten Mix.
                captureTap = tapObject.AddComponent<VivoxCaptureSourceTap>();
                feed = tapObject.AddComponent<WalkieVivoxCaptureFeed>();
                feed.Configure(SafeOutputSampleRate());

                // WICHTIG: Tap permanent auf den Proximity-Kanal pinnen —
                // Funkkanal-Pins liefern nachweislich keine native Daten
                // (Begruendung im Kommentar von TryPinTapToActiveChannel, v9).
                TryPinTapToActiveChannel();

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
        /// Pinnt den Capture-Tap PERMANENT auf den Proximity-Kanal — auch und
        /// gerade waehrend Funk-PTT (v9).
        /// Beweislage (Logs 20260918-093346 und -094023, beide v7):
        /// - Auf dem Funkkanal ('earshot-radio-...') gepinnte Taps liefern NIE
        ///   native Daten: Der VivoxAudioProcessor haelt die Quelle nach 20x
        ///   NoMoreData an (sourcePlaying=False), signalBlocks=0. Die
        ///   signalBlocks=5 direkt nach jedem Funk-Pin sind der ~100-ms-Rest-
        ///   puffer der vorherigen Proximity-Registrierung — genau der kurze
        ///   Sidetone-Blitz, den der Nutzer beim ersten Reinsprechen hoerte.
        /// - Auf dem echten Proximity-Kanal gepinnte Taps liefern Daten
        ///   (sourcePlaying=True, directOutputPeak bis 0.068). Auch der einzige
        ///   gute Lauf (20260918-0551) hatte Tap UND TX auf Proximity.
        /// Offene Frage des v9-Tests: Liefert der Proximity-Tap auch Daten,
        /// waehrend TX auf dem Funkkanal laeuft? Falls nein (signalBlocks=0 und
        /// inputPeak=0 trotz Sprechen, tapChannel=Proximity, tapInTx=False),
        /// ist Plan C noetig: lokales Mikrofon-Loopback statt Vivox-Capture-Tap.
        /// Nebenwirkung des Fix: keine Neu-Registrierung mehr bei jedem PTT —
        /// der Latenzpuffer bleibt erhalten, Sidetone startet sofort.
        /// Reihenfolge wichtig: AutoAcquireChannel ZUERST abschalten. Der
        /// ChannelName-Setter ist sonst ein No-Opt, wenn Vivox den Namen bereits
        /// automatisch gesetzt hat (Early-Return bei gleichem Namen — Log-Beweis:
        /// 529-faches Pin-Spam mit autoAcquire=True in 20260918-0652).
        /// </summary>
        private void TryPinTapToActiveChannel()
        {
            if (captureTap == null) return;

            var vivoxBackend = VoiceRuntime.Instance != null
                ? VoiceRuntime.Instance.Backend as VivoxVoiceBackend
                : null;
            string desired = vivoxBackend != null ? vivoxBackend.ProximityChannelName : null;

            if (string.IsNullOrEmpty(desired))
            {
                if (!reportedMissingPinTarget)
                {
                    reportedMissingPinTarget = true;
                    VoiceSessionLog.Alert(
                        "WALKIE Sidetone-Tap: kein Pin-Ziel bekannt (Proximity-Kanal-Name fehlt).");
                }
                return;
            }
            reportedMissingPinTarget = false;

            bool alreadyPinned =
                !captureTap.AutoAcquireChannel &&
                string.Equals(captureTap.ChannelName, desired, System.StringComparison.OrdinalIgnoreCase);

            if (alreadyPinned && captureTap.TapId >= 0)
            {
                return; // korrekt gepinnt und registriert
            }

            // 1) Auto-Acquire abschalten (registriert ggf. auf dem aktuellen Kanal
            //    neu und macht den Namen-Setter wirksam).
            if (captureTap.AutoAcquireChannel)
            {
                captureTap.AutoAcquireChannel = false;
            }

            // 2) Auf den Zielkanal pinnen (Neuregistrierung bei Namenswechsel).
            if (!string.Equals(captureTap.ChannelName, desired, System.StringComparison.OrdinalIgnoreCase))
            {
                captureTap.ChannelName = desired;
            }
            else if (captureTap.TapId < 0 && Time.unscaledTime >= nextRegisterRetry)
            {
                // Gleicher Kanal, aber Registration fehlgeschlagen oder verloren:
                // Component-Neustart erzwingt OnEnable => RegisterTapCore.
                // Gedrosselt auf max. alle 2 s, sonst spammt jeder Frame eine
                // Registration (und deren Fehler) in die Konsole.
                nextRegisterRetry = Time.unscaledTime + 2f;
                captureTap.enabled = false;
                captureTap.enabled = true;
            }

            if (!string.Equals(lastPinnedChannel, desired, System.StringComparison.OrdinalIgnoreCase))
            {
                lastPinnedChannel = desired;
                VoiceSessionLog.Note(
                    $"WALKIE Sidetone-Tap auf Kanal '{desired}' gepinnt: TapId={captureTap.TapId}, " +
                    $"autoAcquire={captureTap.AutoAcquireChannel}, revision='{DiagnosticRevision}'");
            }

            if (captureTap.TapId < 0 && Time.unscaledTime >= nextPinFailureAlert)
            {
                nextPinFailureAlert = Time.unscaledTime + 2f;
                VoiceSessionLog.Alert(
                    $"WALKIE Sidetone-Tap-Registration fehlgeschlagen: TapId={captureTap.TapId} " +
                    "(negativ = Vivox-Fehlercode, Details in der Unity-Konsole).");
            }
        }

        private static bool ContainsChannel(string[] channels, string channelName)
        {
            if (channels == null || string.IsNullOrEmpty(channelName)) return false;
            for (int i = 0; i < channels.Length; i++)
            {
                if (string.Equals(channels[i], channelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshTxChannelSnapshot()
        {
            if (Time.unscaledTime < nextTxSnapshotRefresh) return;
            nextTxSnapshotRefresh = Time.unscaledTime + 0.1f;
            txChannelSnapshot = ReadTransmittingChannels();
        }

        private static string[] ReadTransmittingChannels()
        {
            try
            {
                var service = Unity.Services.Vivox.VivoxService.Instance;
                var channels = service != null ? service.TransmittingChannels : null;
                if (channels == null || channels.Count == 0) return new string[0];

                var result = new string[channels.Count];
                for (int i = 0; i < channels.Count; i++)
                {
                    result[i] = channels[i] ?? string.Empty;
                }
                return result;
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>
        /// v10-Diagnose gegen das Symptom "eigene Stimme ueberall gleich laut, kein 3D":
        /// F9 schaltet die Tap-AudioSource hart stumm (mute=true). Mute nullt
        /// nachweislich die OnAudioFilterRead-Samples (Log 20260918-0735), ist also
        /// garantiert nicht hoerbar und stoppt gleichzeitig die Sidetone-Daten.
        /// Hoert man die eigene Stimme trotz F9 weiter, kommt sie NICHT aus dem
        /// Sidetone-Tap — dann ist der Leak woanders (Vivox-nativ, OS, zweiter Pfad).
        /// </summary>
        private void HandleDiagnosticHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                diagnosticHardMute = !diagnosticHardMute;
                if (tapSource != null) tapSource.mute = diagnosticHardMute;
                VoiceSessionLog.Alert(diagnosticHardMute
                    ? "WALKIE DIAGNOSE F9: Tap HARD-MUTE AN. Sidetone UND direkte " +
                      "Tap-Ausgabe sind jetzt garantiert stumm. Hoerst du deine " +
                      "Stimme trotzdem weiter, kommt sie NICHT aus dem Sidetone-Tap."
                    : "WALKIE DIAGNOSE F9: Tap Hard-Mute AUS - Normalzustand " +
                      "wiederhergestellt (Sidetone wieder aktiv).");
            }
        }

        /// <summary>
        /// v10-Diagnose: listet alle SPIELENDEN AudioSources der Szene auf
        /// (auch inaktive Objekte), damit jede 2D-Quelle — auch versteckte
        /// Vivox- oder Wiedergabe-Quellen — im Log identifizierbar ist.
        /// Laeuft gedrosselt alle 2 s waehrend des Sidetone-Flow-Logs.
        /// </summary>
        private void LogAudioSourceInventory()
        {
            if (Time.unscaledTime < nextInventoryLog) return;
            nextInventoryLog = Time.unscaledTime + 2f;

            AudioSource[] sources = FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource candidate = sources[i];
                if (candidate == null || !candidate.isPlaying) continue;

                bool isTap = candidate == tapSource;
                string clipName = candidate.clip != null ? candidate.clip.name : "-";
                string objectName = candidate.gameObject.name;

                VoiceSessionLog.Note(
                    $"WALKIE AUDIO-INVENTAR: '{objectName}'" +
                    (isTap ? " [SIDETONE-TAP]" : "") +
                    $" clip='{clipName}' spatial={candidate.spatialBlend:0.00} " +
                    $"vol={candidate.volume:0.00} mute={candidate.mute} " +
                    $"loop={candidate.loop} pos={candidate.transform.position:0.0}");

                if (!isTap &&
                    candidate.spatialBlend < 0.5f &&
                    !candidate.mute &&
                    candidate.volume > 0.01f)
                {
                    VoiceSessionLog.Alert(
                        $"WALKIE LEAK-VERDACHT: 2D-AudioSource '{objectName}' " +
                        $"(clip='{clipName}') spielt gerade in den Mix - " +
                        "Kandidat fuer 'Stimme ueberall gleich laut'.");
                }
            }
        }

        private void EnforceDirectOutputUnmuted()
        {
            // v7: Die Source darf NICHT gemutet sein — AudioSource.mute nullt die
            // Samples in OnAudioFilterRead (siehe Kommentar in EnsureCaptureTap).
            // Stumme Direktausgabe garantiert der Feed selbst (Array.Clear).
            // v10: F9-Diagnose-Mute darf nicht automatisch entfernt werden.
            if (diagnosticHardMute) return;
            if (tapSource == null || !tapSource.mute) return;

            tapSource.mute = false;
            VoiceSessionLog.Alert(
                "WALKIE Capture-Source war unerwartet gemutet (nullt OnAudioFilterRead) und wurde entsperrt.");
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
            // Fenster-Maximum statt Momentanwert: v7-Diagnose zeigte inputPeak=0
            // trotz signalBlocks=5, weil hier immer nur der letzte Buffer stand.
            if (peak > diagnosticPeak) diagnosticPeak = peak;
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
