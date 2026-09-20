using System.Collections.Generic;
using Unity.Services.Vivox;
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
        private const string DiagnosticRevision = "walkie-over-proximity-v18";

        // v16.5: Leak-Hunt-Hotkeys (F7-F12) sind per Default AUS. Der Root-Cause
        // ist gefixt und im Spiel bestaetigt, und F7 wird jetzt vom
        // VoiceGraphDebugHUD benutzt. Zum Nachtesten der alten Diagnose die
        // Komponente am 'Earshot Voice Runtime'-Objekt im Inspector anklicken
        // und diese Checkbox waehrend Play setzen - laufende Diagnose-Zustaende
        // werden beim Deaktivieren automatisch zurueckgesetzt.
        // Belegung und Hintergruende: docs/debug-keys.md.
        [SerializeField]
        [Tooltip("Leak-Hunt-Diagnose-Hotkeys F7-F12 aktivieren (Default aus, siehe docs/debug-keys.md).")]
        private bool diagnosticHotkeysEnabled;

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
        private bool diagnosticLegacyVolume;
        private string diagnosticVivoxOutputBefore;
        private bool diagnosticSidetoneBlocked;
        private bool diagnosticMasterMuted;
        private float diagnosticMasterVolumeBefore = 1f;
        private readonly float[] masterMixBuffer = new float[1024];
        private readonly float[] inventoryPeakBuffer = new float[256];
        private float nextInventoryLog;
        private bool lastWanted;
        private string lastChannel;
        private string lastPinnedChannel;
        private bool reportedMissingPinTarget;
        private float nextPinFailureAlert;
        private float nextRegisterRetry;
        private string[] txChannelSnapshot = new string[0];
        private float nextTxSnapshotRefresh;

        // v16: Passive RX-Sonden (VivoxChannelAudioTap) fuer Funk- und
        // Proximity-Kanal. Sie messen, was die Vivox-Engine auf dem Kanal
        // EMPFAENGT — ihre AudioSource bleibt auf volume=0 und ist damit selbst
        // nie hoerbar. Steigt funkRx waehrend PTT+Sprechen, empfaengt die Engine
        // das ECHO der eigenen Sendung zurueck und spielt es nativ ab.
        private GameObject radioRxObject;
        private VivoxChannelAudioTap radioRxTap;
        private AudioSource radioRxSource;
        private string lastRadioRxChannel;
        private GameObject proxRxObject;
        private VivoxChannelAudioTap proxRxTap;
        private AudioSource proxRxSource;
        private string lastProxRxChannel;
        private float nextRxDiagnostic;
        // remote-hunt-v16.9 Log-Diaet: Flanken-Zustand + 30-s-Herzschlag fuer die
        // VIVOX-RX-Beweiszeile (statt identische Zeile alle 2 s zu schreiben).
        private string lastRxStateKey;
        private float nextRxHeartbeat;
        private readonly List<string> radioRxScratch = new List<string>(4);
        private readonly List<string> proxParticipantScratch = new List<string>(4);
        private readonly List<string> radioParticipantScratch = new List<string>(4);
        private readonly float[] rxRingBuffer = new float[9600];

        // v16.2: Sonden-Lesebuffer in Clip-Groesse - Fix fuer die Out-of-
        // Bounds-GetData-Lesung in ReadTapRingPeak (Details im Methoden-
        // Kommentar). Wird pro Clip-Geometrie einmal allokiert (~3 s x Kanaele).
        private float[] rxProbeBuffer;
        private float[] micProbeBuffer;

        // VivoxAudioProcessor-Interna fuer die RX-Sonden (Reflektion, identisch
        // zu WalkieVivoxCaptureFeed — Feldnamen aus VivoxAudioProcessor.cs).
        private static System.Reflection.FieldInfo rxProcessorField;
        private static System.Reflection.FieldInfo rxWritePointerField;
        private static System.Reflection.FieldInfo rxStreamClipField;
        private static bool rxReflectionResolved;

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
            EnsureRxTaps();
            EnforceSilentDirectOutput();

            // v16.5: Leak-Hunt-Hotkeys nur noch auf Wunsch (Default aus).
            // Beim Ausschalten werden aktive Diagnose-Zustaende (F8-Geraet,
            // F9-Mute, F10-Volume, F11-Feed, F12-Master) sicher zurueckgesetzt.
            if (diagnosticHotkeysEnabled)
            {
                HandleDiagnosticHotkeys();
            }
            else
            {
                RestoreDiagnosticsIfActive();
            }

            bool ready = captureTap != null && captureTap.TapId >= 0 && feed != null;
            // v16.3: pttActive trennt "PTT + Tap bereit" vom diagnostischen
            // Feed-Block (F11). FLOW-/Inventar-Logs laufen ab jetzt AUCH bei
            // geblocktem Feed weiter - sonst ist ein F11-Fenster nachtraeglich
            // nicht beweisbar (v16.2, Session 072827: waehrend beider F11-
            // Fenster keine FLOW-Zeilen, Sprachnachweis fehlte komplett).
            bool pttActive = ready &&
                             WalkieTalkieRegistry.LocalIsTransmitting &&
                             !string.IsNullOrEmpty(WalkieTalkieRegistry.LocalTransmitChannelId);
            bool wanted = pttActive && !diagnosticSidetoneBlocked;
            string channel = pttActive
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

            if (pttActive && Time.unscaledTime >= nextFlowDiagnostic)
            {
                nextFlowDiagnostic = Time.unscaledTime + 1f;
                feed.ConsumeDiagnostics(out int pulls, out int pulledFrames, out int signalBlocks, out float peak);
                float directOutputPeak = ReadDirectOutputPeak();
                ReadMasterMix(out float masterPeak, out float masterRms);
                float funkRxPeak = ReadTapRingPeak(radioRxTap, ref rxProbeBuffer);
                float proxRxPeak = ReadTapRingPeak(proxRxTap, ref rxProbeBuffer);
                float micPeak = ReadTapRingPeak(captureTap, ref micProbeBuffer);
                RefreshTxChannelSnapshot();
                bool tapInTx = ContainsChannel(txChannelSnapshot, captureTap.ChannelName);
                VoiceSessionLog.Note(
                    $"WALKIE CAPTURE FLOW: TapId={captureTap.TapId}, " +
                    $"tapChannel='{captureTap.ChannelName}', autoAcquire={captureTap.AutoAcquireChannel}, " +
                    $"txChannels=[{string.Join(" | ", txChannelSnapshot)}], tapInTx={tapInTx}, " +
                    $"pulls={pulls}, pulledFrames={pulledFrames}, " +
                    $"signalBlocks={signalBlocks}, clipPeak={peak:0.0000}, " +
                    $"micPeak={micPeak:0.000000}, feedBlocked={diagnosticSidetoneBlocked}, " +
                    $"directOutputPeak={directOutputPeak:0.000000}, " +
                    $"funkRx={funkRxPeak:0.000000}, proxRx={proxRxPeak:0.000000}, " +
                    $"masterPeak={masterPeak:0.000000}, masterRms={masterRms:0.000000}, " +
                    $"sourcePlaying={tapSource.isPlaying}, " +
                    $"sourceMute={tapSource.mute}, sourceVolume={tapSource.volume:0.000}, " +
                    $"channel='{channel}', revision='{DiagnosticRevision}'");
                LogAudioSourceInventory();
            }

            LogVivoxRxProbe();
        }

        /// <summary>
        /// v16: Legt passive RX-Sonden (VivoxChannelAudioTap) auf den Funk- und den
        /// Proximity-Kanal. Die Sonden messen ausschliesslich, was die Vivox-Engine
        /// auf dem Kanal empfaengt; ihre AudioSource bleibt auf volume=0 und ist
        /// damit selbst nie hoerbar. Steigt funkRx waehrend Sendetaste+Sprechen,
        /// empfaengt die Engine die eigene Sendung als Echo zurueck — abgespielt
        /// wird es dann ueber die native Engine-Ausgabe (Zustand: vivoxOutMuted in
        /// der VIVOX-RX-Zeile, Schalter: F7).
        /// </summary>
        private void EnsureRxTaps()
        {
            if (!EarshotVoice.IsConnected) return;

            var vivoxBackend = VoiceRuntime.Instance != null
                ? VoiceRuntime.Instance.Backend as VivoxVoiceBackend
                : null;
            if (vivoxBackend == null) return;

            string proximity = vivoxBackend.ProximityChannelName;
            if (!string.IsNullOrEmpty(proximity) &&
                !string.Equals(lastProxRxChannel, proximity, System.StringComparison.OrdinalIgnoreCase))
            {
                lastProxRxChannel = proximity;
                RebuildChannelTap(
                    ref proxRxObject, ref proxRxTap, ref proxRxSource, proximity, "Proximity-RX");
            }

            radioRxScratch.Clear();
            vivoxBackend.CopyJoinedRadioChannels(radioRxScratch);
            string radioVivox = radioRxScratch.Count > 0
                ? WalkieRules.ToVivoxRadioChannel(radioRxScratch[0])
                : null;
            if (!string.IsNullOrEmpty(radioVivox) &&
                !string.Equals(lastRadioRxChannel, radioVivox, System.StringComparison.OrdinalIgnoreCase))
            {
                lastRadioRxChannel = radioVivox;
                RebuildChannelTap(
                    ref radioRxObject, ref radioRxTap, ref radioRxSource, radioVivox, "Funk-RX");
            }
        }

        private void RebuildChannelTap(
            ref GameObject tapObjectRef,
            ref VivoxChannelAudioTap tapRef,
            ref AudioSource sourceRef,
            string vivoxChannelName,
            string label)
        {
            try
            {
                if (tapObjectRef != null) Destroy(tapObjectRef);

                tapObjectRef = new GameObject("Earshot Vivox " + label + " Sonde");
                tapObjectRef.transform.SetParent(transform, false);

                sourceRef = tapObjectRef.AddComponent<AudioSource>();
                sourceRef.playOnAwake = false;
                sourceRef.loop = false;
                sourceRef.spatialBlend = 0f;
                sourceRef.dopplerLevel = 0f;
                // Messsonde: niemals hoerbar. Der Ring-Buffer-Clip des Taps wird
                // davon unberuehrt weiter gefuellt (v12-Beweislage) und per
                // Reflektion ausgelesen.
                sourceRef.volume = 0f;
                sourceRef.mute = false;

                tapRef = tapObjectRef.AddComponent<VivoxChannelAudioTap>();
                // Der ChannelName-Setter deaktiviert Auto-Acquire und registriert
                // den Tap gezielt auf den benannten Kanal.
                tapRef.ChannelName = vivoxChannelName;

                VoiceSessionLog.Note(
                    $"WALKIE RX-SONDE '{label}' auf Kanal '{vivoxChannelName}': " +
                    $"TapId={tapRef.TapId} (misst EMPFANGENEN Kanal-Audio, Ausgabe " +
                    $"volume=0 — die Sonde selbst ist nie hoerbar)");
            }
            catch (System.Exception ex)
            {
                if (tapObjectRef != null) Destroy(tapObjectRef);
                tapObjectRef = null;
                tapRef = null;
                sourceRef = null;
                VoiceSessionLog.Alert(
                    $"WALKIE RX-SONDE '{label}' fehlgeschlagen: {ex.Message}");
            }
        }

        /// <summary>
        /// v16: Periodischer Beweis-Report (alle 2 s, unabhaengig von PTT):
        /// - funkRx/proxRx: Was die Engine auf Funk-/Proximity-Kanal empfaengt.
        /// - vivoxOutMuted/vivoxOutDev/vivoxOutVol: Zustand der nativen Ausgabe.
        /// - masterPeak/masterRms: Unity-Endmix (Sonden-Validierung: muss bei
        ///   hörbarem Spiel-Ton >0 sein, bei F12 exakt 0).
        /// - proxKanal/funkKanal: Teilnehmer-Listen (Proximity war blinder Fleck).
        /// v16.9 Log-Diaet: Messung bleibt im 2-s-Takt, die Ausgabe kommt nur
        /// noch bei Zustandswechsel (PTT/Empfang/Play-Status/Teilnehmer) plus
        /// 30-s-Herzschlag — siehe remote-hunt-Kommentar im Methodenkoerper.
        /// </summary>
        private void LogVivoxRxProbe()
        {
            if (Time.unscaledTime < nextRxDiagnostic) return;
            nextRxDiagnostic = Time.unscaledTime + 2f;
            if (!EarshotVoice.IsConnected) return;

            var vivoxBackend = VoiceRuntime.Instance != null
                ? VoiceRuntime.Instance.Backend as VivoxVoiceBackend
                : null;

            float funkRx = ReadTapRingPeak(radioRxTap, ref rxProbeBuffer);
            float proxRx = ReadTapRingPeak(proxRxTap, ref rxProbeBuffer);
            // v16.3: micPeak misst das LOKALE MIKROFON direkt am Capture-Tap -
            // Sprachnachweis auch waehrend F11-Block (feedBlocked=True), denn
            // der SDK-Pull fuellt den StreamClip unabhaengig von unserem Feed.
            float micPeak = ReadTapRingPeak(captureTap, ref micProbeBuffer);
            ReadMasterMix(out float masterPeak, out float masterRms);

            proxParticipantScratch.Clear();
            if (vivoxBackend != null)
            {
                vivoxBackend.CopyProximityChannelParticipantIds(proxParticipantScratch);
            }

            radioParticipantScratch.Clear();
            if (radioRxScratch.Count > 0 && vivoxBackend != null)
            {
                vivoxBackend.CopyRadioChannelParticipantIds(
                    radioRxScratch[0], radioParticipantScratch);
            }

            bool outMuted = VivoxService.Instance != null &&
                            VivoxService.Instance.IsOutputDeviceMuted;
            int outVol = VivoxService.Instance != null
                ? VivoxService.Instance.OutputDeviceVolume
                : -1;
            string outDev = EarshotVoice.ActiveOutputDeviceName;

            // remote-hunt-v16.9 Log-Diaet: Der 2-s-Ticker MISST weiter (die Flanken-
            // erkennung braucht die Abtastung), schreibt die volle Beweis-Zeile aber
            // nur noch bei Zustandswechsel (PTT, Funk-Empfangs-Pegel, Sonden-Play-
            // Status, Teilnehmerlisten) plus alle 30 s als Herzschlag. Beweis:
            // Freund-Session 20260920-005940 enthielt ~300 identische Zeilen;
            // wertvoll waren ausschliesslich die Flanken (funkRx=0 von Anfang bis
            // Ende, funkRxPlaying nie True — der entscheidende Remote-Befund).
            string proxKanal = string.Join(", ", proxParticipantScratch);
            string funkKanal = string.Join(", ", radioParticipantScratch);
            // v17 (radio-tx-probe): Vivox' EIGENE Sicht der Fernseher-Sprachaktivitaet
            // im Funkkanal - unabhaengig von unseren Taps. E>0/S=True bei funkRx=0
            // waere Tap-Schicht schuld; beides 0/False heisst: Sendung kommt nie im
            // Kanal an (Beweislage Lokaltest 20260920-071757: genau das pruefen).
            string funkSprecher = vivoxBackend != null && radioRxScratch.Count > 0
                ? vivoxBackend.DescribeRadioChannelSpeech(radioRxScratch[0])
                : string.Empty;
            bool funkPlaying = radioRxSource != null && radioRxSource.isPlaying;
            bool proxPlaying = proxRxSource != null && proxRxSource.isPlaying;
            bool ptt = WalkieTalkieRegistry.LocalIsTransmitting;
            bool funkSignal = funkRx > 0.0001f;
            string stateKey = ptt + "|" + funkPlaying + "|" + proxPlaying + "|" +
                funkSignal + "|" + proxKanal + "|" + funkKanal + "|" + funkSprecher;
            bool changed = !string.Equals(stateKey, lastRxStateKey, System.StringComparison.Ordinal);
            bool heartbeat = changed || Time.unscaledTime >= nextRxHeartbeat;
            if (!heartbeat) return;
            if (Time.unscaledTime >= nextRxHeartbeat)
            {
                nextRxHeartbeat = Time.unscaledTime + 30f;
            }
            lastRxStateKey = stateKey;

            VoiceSessionLog.Note(
                (changed ? "WALKIE VIVOX RX (Wechsel): " : "WALKIE VIVOX RX (Herzschlag): ") +
                $"funkRx={funkRx:0.000000}, proxRx={proxRx:0.000000}, " +
                $"micPeak={micPeak:0.000000}, feedBlocked={diagnosticSidetoneBlocked}, " +
                $"vivoxOutMuted={outMuted}, vivoxOutDev='{outDev}', vivoxOutVol={outVol}, " +
                $"funkRxPlaying={funkPlaying}, " +
                $"proxRxPlaying={proxPlaying}, " +
                $"listenerVol={AudioListener.volume:0.000}, " +
                $"masterPeak={masterPeak:0.000000}, masterRms={masterRms:0.000000}, " +
                $"proxKanal=[{proxKanal}], " +
                $"funkKanal=[{funkKanal}], " +
                $"funkSprecher=[{funkSprecher}], " +
                $"ptt={ptt}, " +
                $"revision='{DiagnosticRevision}'");
        }

        /// <summary>
        /// v16: Peak des Ring-Buffer-Clips eines Vivox-Taps (Reflektion auf
        /// VivoxAudioProcessor.m_streamClip/m_writePointer — identischer
        /// Mechanismus wie WalkieVivoxCaptureFeed, v12). Liefert -1, wenn
        /// Tap/Clip fehlen, 0 bei Stille und &gt;0 bei empfangenem Audio.
        /// </summary>
        private float ReadTapRingPeak(VivoxAudioTap tap, ref float[] probeBuffer)
        {
            if (tap == null || tap.TapId < 0) return -1f;

            try
            {
                ResolveRxReflection();

                object processor = rxProcessorField != null
                    ? rxProcessorField.GetValue(tap)
                    : null;
                if (processor == null) return -1f;

                AudioClip clip = rxStreamClipField != null
                    ? rxStreamClipField.GetValue(processor) as AudioClip
                    : null;
                if (clip == null) return -1f;

                int writePointer = rxWritePointerField != null
                    ? (int)rxWritePointerField.GetValue(processor)
                    : -1;
                int totalFrames = clip.samples;
                int channels = clip.channels > 0 ? clip.channels : 1;
                if (totalFrames <= 0 || writePointer < 0) return -1f;

                // v16.2-Fix: AudioClip.GetData fuellt IMMER den kompletten
                // Buffer (data.Length) - firstFrames begrenzte frueher nur
                // die Peak-Auswertung, nicht das Lesen selbst. Sobald das
                // 0,2-s-Fenster ueber das Clip-Ende wrappte (frischer Tap,
                // writePointer < Fenstergroesse), las GetData darueber hin-
                // aus: Unity-native "SoundManager.cpp(815) m_Sound->lock ...
                // invalid parameter" (Editor.log 2026-09-19, 37x) und unzu-
                // verlaessliche funkRx/proxRx-Werte (Sonden blind). Sicherer
                // Weg: EIN Lesevorgang ab Offset 0 mit Buffer in Clip-Groesse
                // (immer in-bounds), der Peak wird danach im Speicher ueber
                // die letzten 0,2 s vor dem Schreibcursor berechnet.
                // v16.3: Buffer pro Tap (ref) - Capture-Tap ist MONO, die
                // Kanal-Sonden STEREO; ein gemeinsamer Buffer wuerde bei
                // jedem Aufruf neu allokiert werden.
                int totalSamples = totalFrames * channels;
                if (probeBuffer == null || probeBuffer.Length != totalSamples)
                {
                    probeBuffer = new float[totalSamples];
                }

                clip.GetData(probeBuffer, 0);

                int windowSamples = System.Math.Min(rxRingBuffer.Length, totalSamples);
                int firstSample = writePointer * channels - windowSamples;
                float peak = 0f;
                for (int i = 0; i < windowSamples; i++)
                {
                    int index = firstSample + i;
                    if (index < 0) index += totalSamples;
                    float absolute = probeBuffer[index];
                    if (absolute < 0f) absolute = -absolute;
                    if (absolute > peak) peak = absolute;
                }

                return peak;
            }
            catch
            {
                return -1f;
            }
        }

        private static void ResolveRxReflection()
        {
            if (rxReflectionResolved) return;
            rxReflectionResolved = true;

            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance;
                rxProcessorField = typeof(VivoxAudioTap).GetField("m_AudioProcessor", flags);
                var processorType = rxProcessorField != null
                    ? rxProcessorField.FieldType
                    : null;
                rxWritePointerField = processorType?.GetField("m_writePointer", flags);
                rxStreamClipField = processorType?.GetField("m_streamClip", flags);
            }
            catch
            {
                // Felder bleiben null -> ReadTapRingPeak meldet -1.
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
                // v12 (Beweis Log 20260918-1318, F10-Test): volume=0 stummt die 2D-
                // Direktausgabe des Tap-Clips komplett. Der SDK-Coroutine-Pull
                // (VivoxAudioProcessor.ProcessAudio -> DoAudioFilterRead per P/Invoke)
                // fuellt den Ring-Buffer-Clip davon unberuehrt weiter - er laeuft
                // unabhaengig von volume/mute der AudioSource. Seit v12 liest der Feed
                // die Daten per Reflektion direkt aus dem Clip (ConfigurePull);
                // volume=0 ist daher Dauer-Fix, kein Diagnose-Zustand. F10 stellt den
                // Legacy-Zustand volume=1 (hoerbarer Leak) testweise wieder her.
                tapSource.volume = 0f;
                tapSource.mute = false;

                feed = tapObject.AddComponent<WalkieVivoxCaptureFeed>();
                captureTap = tapObject.AddComponent<VivoxCaptureSourceTap>();
                feed.ConfigurePull(captureTap, tapSource, SafeOutputSampleRate());

                // WICHTIG: Tap permanent auf den Proximity-Kanal pinnen —
                // Funkkanal-Pins liefern nachweislich keine native Daten
                // (Begruendung im Kommentar von TryPinTapToActiveChannel, v9).
                TryPinTapToActiveChannel();

                VoiceSessionLog.Note(
                    $"WALKIE Vivox-Capture-Tap erstellt: TapId={captureTap.TapId}, " +
                    $"channel='{captureTap.ChannelName}', autoAcquire={captureTap.AutoAcquireChannel}, " +
                    $"input='{EarshotVoice.ActiveInputDeviceName}', hardMute={tapSource.mute}, " +
                    $"tapVolume={tapSource.volume:0.00}, " +
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
        /// Diagnose-Hotkeys gegen das Symptom "eigene Stimme ueberall gleich laut":
        /// v12: Default ist volume=0 (Dauer-Fix, Beweis F10-Test Log 20260918-1318).
        /// F9 = zusaetzlicher Hard-Mute (Not-Killswitch; stummt nur die Direktausgabe,
        /// der Sidetone laeuft seit v12 ueber den Clip-Lese-Pfad weiter).
        /// F10 = Legacy-Modus volume=1: Direktausgabe wieder hoerbar (Gegenprobe,
        /// dass der Leak wirklich die Tap-Direktausgabe war).
        /// F11 = Sidetone-Datenfluss zu den Walkie-Geraeten kappen.
        /// v14: F8 = Vivox-Nativwiedergabe auf ein physisches Geraet umleiten -
        /// trennt den Unity-Mix vom Vivox-Empfangspfad (siehe Handler).
        /// </summary>
        /// <summary>
        /// v16.5: Rueckgaengig machen, was die Leak-Hunt-Hotkeys evtl. verstellt
        /// haben, wenn sie per Inspector deaktiviert wurden (z.B. F12-Master
        /// stumm, F8 anderes Ausgabegeraet, F11 geblockter Feed). Laeuft nur
        /// einmalig, weil die Flags dabei zurueckgesetzt werden.
        /// </summary>
        private void RestoreDiagnosticsIfActive()
        {
            bool restored = false;

            if (diagnosticMasterMuted)
            {
                AudioListener.volume = diagnosticMasterVolumeBefore;
                diagnosticMasterMuted = false;
                restored = true;
            }

            if (diagnosticVivoxOutputBefore != null)
            {
                string restore = diagnosticVivoxOutputBefore;
                diagnosticVivoxOutputBefore = null;
                _ = EarshotVoice.SetOutputDeviceAsync(restore);
                restored = true;
            }

            if (diagnosticSidetoneBlocked)
            {
                diagnosticSidetoneBlocked = false;
                restored = true;
            }

            if (diagnosticHardMute)
            {
                diagnosticHardMute = false;
                if (tapSource != null) tapSource.mute = false;
                restored = true;
            }

            if (diagnosticLegacyVolume)
            {
                diagnosticLegacyVolume = false;
                if (tapSource != null) tapSource.volume = 0f;
                restored = true;
            }

            if (restored)
            {
                VoiceSessionLog.Alert(
                    "WALKIE DIAGNOSE: Hotkeys deaktiviert - alle Diagnose-Zustaende " +
                    "(F8-Geraet, F9-Mute, F10-Volume, F11-Feed, F12-Master) zurueckgesetzt.");
            }
        }

        private void HandleDiagnosticHotkeys()
        {
            // v14: Vivox spielt EMPFANGENEN Kanal-Ton zusaetzlich nativ auf sein
            // Ausgabegeraet (ausserhalb des Unity-Mixes, laeuft aber im Unity-Prozess
            // und zeigt sich im Windows-Mixer daher als 'Hotel Game'). Fuer getappte
            // Teilnehmer gilt silenceInFinalMix=true - ein Teilnehmer OHNE Tap
            // (z.B. ein zweiter Client im Funkkanal, dessen Tap-Anlage fehlschlug)
            // wird jedoch NATIV und damit nicht-raeumlich abgespielt: 'ueberall gleich
            // laut'. F8 leitet nur die Vivox-Ausgabe auf ein physisches Geraet um -
            // Unitys eigener Ton (Meer, Musik, Sidetone) bleibt unberuehrt.
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (diagnosticVivoxOutputBefore == null)
                {
                    string alternate = null;
                    string current = EarshotVoice.ActiveOutputDeviceName;
                    string[] devices = EarshotVoice.OutputDeviceNames;
                    for (int i = 0; i < devices.Length; i++)
                    {
                        string name = devices[i];
                        if (string.IsNullOrEmpty(name)) continue;
                        if (EarshotVoice.IsUnusableAudioDevice(name)) continue;
                        if (name.IndexOf("VB-Audio", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (name.IndexOf("CABLE", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (string.Equals(name, current, System.StringComparison.Ordinal)) continue;
                        alternate = name;
                        break;
                    }

                    if (alternate == null)
                    {
                        VoiceSessionLog.Alert(
                            "WALKIE DIAGNOSE F8: Kein physisches Ausgabegeraet gefunden - " +
                            "Vivox-Umleitung nicht moeglich.");
                    }
                    else
                    {
                        diagnosticVivoxOutputBefore = current;
                        _ = EarshotVoice.SetOutputDeviceAsync(alternate);
                        VoiceSessionLog.Alert(
                            "WALKIE DIAGNOSE F8: Vivox-Ausgabe umgeleitet auf '" + alternate + "' " +
                            "(Unity-Ton bleibt unveraendert!). Verschwindet die konstante Walkie-" +
                            "Stimme jetzt, kam sie aus VIVOX' nativer Wiedergabe - dann empfaengst " +
                            "du Ton von einem Teilnehmer im Funkkanal (Echo-Loop / fehlender Tap). " +
                            "Nochmal F8 stellt das alte Geraet wieder her.");
                    }
                }
                else
                {
                    string restore = diagnosticVivoxOutputBefore;
                    diagnosticVivoxOutputBefore = null;
                    _ = EarshotVoice.SetOutputDeviceAsync(restore);
                    VoiceSessionLog.Alert(
                        "WALKIE DIAGNOSE F8: Vivox-Ausgabe zurueck auf '" + restore + "'.");
                }
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                diagnosticHardMute = !diagnosticHardMute;
                if (tapSource != null) tapSource.mute = diagnosticHardMute;
                VoiceSessionLog.Alert(diagnosticHardMute
                    ? "WALKIE DIAGNOSE F9: Tap HARD-MUTE AN (Not-Killswitch). Die " +
                      "direkte Tap-Ausgabe ist stumm; der Sidetone laeuft ueber den " +
                      "Clip-Lese-Pfad (v12) weiter. Stimme trotzdem hoerbar -> " +
                      "sie kommt NICHT aus dem Sidetone-Tap."
                    : "WALKIE DIAGNOSE F9: Tap Hard-Mute AUS.");
            }

            // v12: F10 stellt die LEGACY-Direktausgabe (volume=1) testweise wieder an.
            // Im Normalzustand (volume=0) MUSS die eigene Stimme nirgendwo direkt
            // hoerbar sein - nur raeumlich an den Walkie-Geraeten. Mit F10 muss der
            // Leak 'ueberall gleich laut' zurueckkehren: der Gegenbeweis.
            if (Input.GetKeyDown(KeyCode.F10))
            {
                diagnosticLegacyVolume = !diagnosticLegacyVolume;
                if (tapSource != null) tapSource.volume = diagnosticLegacyVolume ? 1f : 0f;
                VoiceSessionLog.Alert(diagnosticLegacyVolume
                    ? "WALKIE DIAGNOSE F10: Tap-Volume=1 (LEGACY-LEAK-MODE). Die eigene " +
                      "Stimme sollte JETZT wieder ueberall gleich laut zu hoeren sein - " +
                      "Gegenbeweis, dass der Leak die Tap-Direktausgabe war. Der " +
                      "Sidetone laeuft ueber den Clip-Lese-Pfad weiter."
                    : "WALKIE DIAGNOSE F10: Tap-Volume zurueck auf 0 (v12-Fix, " +
                      "Direktausgabe stumm).");
            }

            // v11: F11 kappt den Sidetone-Datenfluss zu den Walkie-Geraeten.
            // Bleibt die Stimme hoerbar, kommt sie garantiert NICHT aus den
            // Walkie-Lautsprechern (WalkieDeviceOutput).
            if (Input.GetKeyDown(KeyCode.F11))
            {
                diagnosticSidetoneBlocked = !diagnosticSidetoneBlocked;
                VoiceSessionLog.Alert(diagnosticSidetoneBlocked
                    ? "WALKIE DIAGNOSE F11: Sidetone-Datenfluss BLOCKIERT - der Feed " +
                      "liefert keine Samples mehr an die Walkie-Geraete (Mic-Uebertragung " +
                      "laeuft weiter, micPeak im Log beweist deine Sprache). WIRD ES STUMM, " +
                      "kommt der Ton ueber den Sidetone-Bus -> Walkie-Geraete (bei >20m " +
                      "Abstand waere das der Leak-Beweis, Gegenprobe: F12). BLEIBT ES " +
                      "HOERBAR, kommt der Ton garantiert NICHT aus den Walkie-Lautsprechern."
                    : "WALKIE DIAGNOSE F11: Sidetone-Datenfluss wieder freigegeben.");
            }

            // v16.4: F12 stellt den GESAMTEN Unity-Mix stumm (AudioListener-Master).
            // Seit dem Root-Cause-Fix wird der Master zusaetzlich in
            // WalkieDeviceOutput.OnAudioFilterRead multipliziert (Unity zieht
            // Volume VOR dem Filter ab; das Ueberschreiben von data umging ihn -
            // Leak-Beweis Log 20260919-090425). Bleibt die Stimme jetzt trotzdem
            // hoerbar, kommt sie wirklich ausserhalb von Unity (OS/Parsec/VB-Cable).
            if (Input.GetKeyDown(KeyCode.F12))
            {
                diagnosticMasterMuted = !diagnosticMasterMuted;

                if (diagnosticMasterMuted)
                {
                    diagnosticMasterVolumeBefore = AudioListener.volume;
                    AudioListener.volume = 0f;
                    VoiceSessionLog.Alert(
                        "WALKIE DIAGNOSE F12: Unity-GESAMTAUSGABE STUMM (AudioListener-Master=0, " +
                        "zuvor " + diagnosticMasterVolumeBefore.ToString("0.000") + "). Seit v16.4 " +
                        "greift das AUCH fuer die Walkie-Lautsprecher (Filter multipliziert den " +
                        "Master mit). Hoerst du deine Stimme bei gehaltener Sendetaste TROTZDEM, " +
                        "kommt sie ausserhalb von Unity (OS-Seite/Parsec/VB-Cable). Nochmal F12 " +
                        "stellt alles wieder her.");
                }
                else
                {
                    AudioListener.volume = diagnosticMasterVolumeBefore;
                    VoiceSessionLog.Alert(
                        "WALKIE DIAGNOSE F12: Unity-Gesamtausgabe wiederhergestellt (" +
                        diagnosticMasterVolumeBefore.ToString("0.000") + ").");
                }
            }

            // v16: F7 toggelt die VIVOX-NATIVE Wiedergabe (Engine-Ausgabegeraet).
            // Die These 'Vivox-native ist der Leak' gilt seit dem Meta-Review
            // 2026-09-19 als geschaechtert (Solo-Kanal kann nichts reflektieren;
            // sndvol-Ausschlaege haben eine einfachere Erklaerung — siehe
            // Debug-Historie, Abschnitt v16-Meta). F7 bleibt als Gegenproben-
            // Werkzeug: AN = native Ausgabe aktiv (kehrt die Selbsthoerung
            // zurueck, waere die These doch bewiesen), AUS = Engine-Mute.
            // Hinweis: Der optionale Auto-Mute nach Login ist default AUS
            // (VivoxVoiceBackend.DiagnosticMuteVivoxNativeOutputOnLogin).
            if (Input.GetKeyDown(KeyCode.F7))
            {
                bool muted = VivoxService.Instance != null &&
                             VivoxService.Instance.IsOutputDeviceMuted;
                if (muted)
                {
                    VivoxService.Instance.UnmuteOutputDevice();
                    VoiceSessionLog.Alert(
                        "WALKIE DIAGNOSE F7: Vivox-NATIVE Ausgabe WIEDER AN. Hoerst du " +
                        "deine Stimme bei gehaltener Sendetaste jetzt wieder, ist " +
                        "bewiesen: Die Engine spielt Empfangenes (eigenes Funk-Echo, " +
                        "funkRx>0) nativ ins Ausgabegeraet. Nochmal F7 = wieder stumm.");
                }
                else
                {
                    VivoxService.Instance.MuteOutputDevice();
                    VoiceSessionLog.Alert(
                        "WALKIE DIAGNOSE F7: Vivox-native Ausgabe STUMM (Engine-Mute). " +
                        "Alle Stimmen laufen weiterhin ueber den Unity-Mix.");
                }
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

                // remote-hunt-v16.9 Log-Diaet: Inventory-Note-Zeilen nur noch fuer
                // voice-relevante Quellen (Earshot/Walkie/StreamClip/Tap). Die
                // Leak-Warnung unten bleibt fuer ALLE spielenden Quellen aktiv.
                // Beweis: Freund-Session 20260920-005940 listete pro Snapshot 16
                // OceanSound_XX-Quellen auf — Rauschen ohne Diagnosewert.
                bool voiceRelevant = isTap ||
                    objectName.IndexOf("Earshot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Walkie", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clipName.StartsWith("StreamClip", System.StringComparison.OrdinalIgnoreCase);

                float outPeak = voiceRelevant ? ReadSourceOutputPeak(candidate) : 0f;
                if (voiceRelevant)
                {
                    VoiceSessionLog.Note(
                        $"WALKIE AUDIO-INVENTAR: '{objectName}'" +
                        (isTap ? " [SIDETONE-TAP]" : "") +
                        $" clip='{clipName}' spatial={candidate.spatialBlend:0.00} " +
                        $"vol={candidate.volume:0.00} mute={candidate.mute} " +
                        $"loop={candidate.loop} outPeak={outPeak:0.000} " +
                        $"pos={candidate.transform.position:0.0}");
                }

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

        /// <summary>
        /// v12: Die Tap-AudioSource bleibt dauerhaft auf volume=0 - die 2D-
        /// Direktausgabe ist damit garantiert stumm (Beweis F10-Test Log
        /// 20260918-1318), waehrend der Feed die Sidetone-Daten per Reflektion
        /// aus dem Ring-Buffer-Clip liest. F9 (Hard-Mute) und F10 (Legacy-
        /// Volume=1) duerfen den Zustand bewusst ueberschreiben.
        /// </summary>
        private void EnforceSilentDirectOutput()
        {
            if (tapSource == null) return;

            if (!diagnosticHardMute && tapSource.mute)
            {
                tapSource.mute = false;
                VoiceSessionLog.Alert(
                    "WALKIE Capture-Source war unerwartet gemutet und wurde entsperrt.");
            }

            if (!diagnosticLegacyVolume && tapSource.volume > 0.0001f)
            {
                tapSource.volume = 0f;
                VoiceSessionLog.Alert(
                    "WALKIE Capture-Source-Volume war unerwartet >0 (Leak-Pfad) und " +
                    "wurde auf 0 zurueckgesetzt (v12-Fix).");
            }
        }

        /// <summary>
        /// v11: Tatsaechlicher Output-Pegel einer AudioSource (GetOutputData).
        /// Zeigt im AUDIO-INVENTAR, welche Quelle wirklich Signal in den Mix gibt -
        /// der entscheidende Beweis, wenn eine vermeintlich stummgefilterte Quelle
        /// (wie der Sidetone-Tap) trotzdem hoerbar ist.
        /// </summary>
        private float ReadSourceOutputPeak(AudioSource source)
        {
            if (source == null) return -1f;
            try
            {
                source.GetOutputData(inventoryPeakBuffer, 0);
                float peak = 0f;
                for (int i = 0; i < inventoryPeakBuffer.Length; i++)
                {
                    float absolute = inventoryPeakBuffer[i] < 0f
                        ? -inventoryPeakBuffer[i]
                        : inventoryPeakBuffer[i];
                    if (absolute > peak) peak = absolute;
                }
                return peak;
            }
            catch
            {
                return -1f;
            }
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

        /// <summary>
        /// v15: Misst den ENDTLICHEN Unity-Mix am AudioListener - NACH allen
        /// Filtern und Injektionen. GetOutputData an AudioSources laeuft VOR
        /// OnAudioFilterRead und sieht injizierte Samples (WalkieOutput, Tap)
        /// prinzipbedingt nicht; dieser Wert ist die einzige verlaessliche
        /// Aussage darueber, was Unity insgesamt Richtung Ausgabegeraet
        /// schickt. Vergleiche masterPeak bei Sprache vs. Stille - nur ein
        /// Anstieg waehrend des Sprechens beweist eine Unity-Quelle.
        /// </summary>
        private void ReadMasterMix(out float peak, out float rms)
        {
            peak = -1f;
            rms = -1f;

            try
            {
                AudioListener.GetOutputData(masterMixBuffer, 0);

                float localPeak = 0f;
                double sum = 0;

                for (int i = 0; i < masterMixBuffer.Length; i++)
                {
                    float sample = masterMixBuffer[i];
                    float absolute = sample < 0f ? -sample : sample;
                    if (absolute > localPeak) localPeak = absolute;
                    sum += absolute;
                }

                peak = localPeak;
                rms = (float)(sum / masterMixBuffer.Length);
            }
            catch
            {
                // Kein aktiver AudioListener - Messwerte bleiben -1.
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

            // leak-hunt-v13: Vivox' native Wiedergabe geht an activeOutput. Ist das ein
            // virtuelles Kabel (VB-Audio/CABLE), kann jedes Monitoring-Tool (OBS,
            // Audacity, Windows-'Abhoeren dieses Geraets') den Kabel-Ton ausserhalb
            // von Unity zurueckspielen - eine dort entstandene Stimme haette in
            // Unitys Audio-Inventar UNSICHTBARE Peaks (GetOutputData laeuft vor Filter).
            string activeOutput = EarshotVoice.ActiveOutputDeviceName ?? string.Empty;
            if (activeOutput.IndexOf("VB-Audio", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                activeOutput.IndexOf("CABLE", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                VoiceSessionLog.Alert(
                    $"AUDIO DEVICES: Vivox gibt auf '{activeOutput}' (virtuelles Kabel) aus. " +
                    "Falls ein Tool (OBS/Audacity/Windows-'Abhoeren') dieses Kabel zurueckspielt, " +
                    "hoerst du Vivox-Ton AUSSERHALB von Unity. Pruefe mit dem Windows-Lautstaerkemixer " +
                    "bei gehaltener Sendetaste, welche App ausschlaegt.");
            }
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
    /// v12: Holt das Vivox-Capture-Signal per Reflektion direkt aus dem Ring-
    /// Buffer-Clip des VivoxAudioProcessor (m_streamClip + m_writePointer) und
    /// schreibt es in den Walkie-Bus. Die Tap-AudioSource bleibt dauerhaft auf
    /// volume=0 (Beweis Log 20260918-1318): Der SDK-Coroutine-Pull fuellt den
    /// Clip unabhaengig von volume/mute, Unity nullt aber bei beidem die
    /// OnAudioFilterRead-Samples - deshalb faellt OnAudioFilterRead weg und die
    /// Daten werden im Main-Thread aus dem Clip gelesen (read-only, keine
    /// Doppel-Pulls am nativen Tap).
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

        // VivoxAudioProcessor-Interna (Reflektion; Paket com.unity.services.vivox
        // ist auf 16.10.0 gepinnt - Feldnamen aus VivoxAudioProcessor.cs).
        private static System.Reflection.FieldInfo audioProcessorField;
        private static System.Reflection.FieldInfo writePointerField;
        private static System.Reflection.FieldInfo streamClipField;
        private static bool reflectionResolved;

        private AudioSource tapSource;
        private object audioProcessor;
        private AudioClip pullClip;
        private int clipTotalFrames;
        private int clipChannels = 1;
        private int lastWritePointer = -1;
        private int pullQuantumFrames = 480;
        private bool pullBrokenReported;

        private float[] monoBuffer = new float[4096];
        private float[] segmentBuffer = new float[2048];
        private volatile bool captureActive;
        private volatile string captureChannel;
        private int sampleRate = 48000;
        private float envelope;
        private float aboveThresholdSeconds;
        private float gain;
        private int diagnosticPulls;
        private int diagnosticFrames;
        private int diagnosticSignalBlocks;
        private volatile float diagnosticPeak;

        internal void ConfigurePull(VivoxAudioTap tap, AudioSource source, int outputSampleRate)
        {
            tapSource = source;
            if (outputSampleRate > 0) sampleRate = outputSampleRate;
            pullQuantumFrames = System.Math.Max(1, sampleRate / 100); // 10 ms
            ResolveReflection();

            try
            {
                audioProcessor = tap != null ? audioProcessorField?.GetValue(tap) : null;
            }
            catch
            {
                audioProcessor = null;
            }

            RefreshClip();
        }

        private static void ResolveReflection()
        {
            if (reflectionResolved) return;
            reflectionResolved = true;

            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance;
                audioProcessorField = typeof(VivoxAudioTap).GetField("m_AudioProcessor", flags);
                var processorType = audioProcessorField != null
                    ? audioProcessorField.FieldType
                    : null;
                writePointerField = processorType?.GetField("m_writePointer", flags);
                streamClipField = processorType?.GetField("m_streamClip", flags);
            }
            catch
            {
                // Felder bleiben null -> ReportPullBroken() meldet den Ausfall.
            }
        }

        private void RefreshClip()
        {
            AudioClip clip = null;
            try
            {
                clip = audioProcessor != null
                    ? streamClipField?.GetValue(audioProcessor) as AudioClip
                    : null;
            }
            catch
            {
                clip = null;
            }

            if (clip == null && tapSource != null) clip = tapSource.clip;

            pullClip = clip;
            if (clip == null)
            {
                lastWritePointer = -1;
                return;
            }

            clipTotalFrames = clip.samples;
            clipChannels = clip.channels > 0 ? clip.channels : 1;
            lastWritePointer = ReadWritePointer();
        }

        private int ReadWritePointer()
        {
            try
            {
                if (audioProcessor != null && writePointerField != null)
                {
                    return (int)writePointerField.GetValue(audioProcessor);
                }
            }
            catch
            {
                // Fallthrough.
            }

            return -1;
        }

        private void ReportPullBroken()
        {
            if (pullBrokenReported) return;
            pullBrokenReported = true;
            VoiceSessionLog.Alert(
                "WALKIE Sidetone-Feed: Reflektions-Zugriff auf VivoxAudioProcessor " +
                "(m_writePointer/m_streamClip) fehlgeschlagen - Sidetone-Datenfluss " +
                "liegt still. Vivox-Paket-Version pruefen (erwartet 16.10.0).");
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

        internal void ConsumeDiagnostics(out int pulls, out int frames, out int signalBlocks, out float peak)
        {
            pulls = System.Threading.Interlocked.Exchange(ref diagnosticPulls, 0);
            frames = System.Threading.Interlocked.Exchange(ref diagnosticFrames, 0);
            signalBlocks = System.Threading.Interlocked.Exchange(ref diagnosticSignalBlocks, 0);
            peak = diagnosticPeak;
            diagnosticPeak = 0f;
        }

        private void OnDisable()
        {
            SetCaptureState(false, null);
        }

        private void Update()
        {
            if (tapSource == null) return;
            if (pullClip == null || tapSource.clip != pullClip)
            {
                RefreshClip();
            }
            if (pullClip == null || lastWritePointer < 0) return;

            int write = ReadWritePointer();
            if (write < 0)
            {
                ReportPullBroken();
                return;
            }

            int total = clipTotalFrames;
            if (total <= 0) return;

            int delta = (write - lastWritePointer + total) % total;
            if (delta == 0) return;
            if (delta > total / 4)
            {
                // Sprung (Re-Initialisierung/Underrun-Bump): neu synchronisieren -
                // die uebersprungene Region ist vom SDK vorsilenced worden.
                lastWritePointer = write;
                return;
            }

            if (!captureActive || string.IsNullOrEmpty(captureChannel))
            {
                // Cursor trotzdem mitziehen, damit beim Aktivieren keine alten
                // Samples in den Bus laufen.
                lastWritePointer = write;
                return;
            }

            // Feste Pull-Quanten (10 ms) halten die GetData-Buffergroesse stabil
            // (keine pro-Frame-Allokationen); ein Rest unter einer Quante wartet
            // auf den naechsten Frame. Nur der Seam-Chunk am Clip-Ende ist mal
            // kleiner - eine Allokation pro ~3 s ist vernachlaessigbar.
            int cursor = lastWritePointer;
            int remaining = delta;
            while (remaining >= pullQuantumFrames)
            {
                int chunk = System.Math.Min(pullQuantumFrames, total - cursor);
                ProcessSegment(cursor, chunk);
                cursor = (cursor + chunk) % total;
                remaining -= chunk;
            }

            lastWritePointer = cursor;
        }

        private void ProcessSegment(int offset, int frames)
        {
            int samples = frames * clipChannels;
            if (segmentBuffer.Length != samples) segmentBuffer = new float[samples];

            try
            {
                pullClip.GetData(segmentBuffer, offset);
            }
            catch
            {
                return;
            }

            if (monoBuffer.Length < frames) monoBuffer = new float[frames];
            Downmix(segmentBuffer, clipChannels, frames);

            System.Threading.Interlocked.Increment(ref diagnosticPulls);
            System.Threading.Interlocked.Add(ref diagnosticFrames, frames);
            ApplySustainGateAndGain(frames);
            WalkieRadioBus.Write(
                captureChannel,
                WalkieRadioBus.LocalSidetoneStreamId,
                monoBuffer,
                0,
                frames);
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
    }
}
