using UnityEngine;
using Unity.Services.Vivox.AudioTaps;

namespace Earshot.Voice
{
    /// <summary>
    /// v18 (walkie-ueber-proximity): Speist die PROXIMITY-Stimme eines entfernten
    /// Spielers in den Walkie-Bus, solange dieser Spieler remote sendet (PTT).
    /// <para>
    /// Hintergrund: Das Audio-Medium des zweiten Vivox-Kanals ('earshot-radio-...')
    /// verbindet in keinem Sendemodus (Beweislage Laeufe 1-4, siehe
    /// docs/walkie-talkie-debug-history.md Abschnitt v18). Sendung UND Empfang
    /// laufen deshalb ueber den Proximity-Kanal, der nachweislich liefert: Die
    /// Mund-Stimme des Senders reist dort ohnehin mit, und Walkie-Empfaenger
    /// spielen genau diesen Strom mit Funk-Filter am Geraet ab.
    /// </para>
    /// <para>
    /// Der Feed liest den Participant-Tap des Proximity-Kanals mit derselben
    /// Reflektions-Mechanik wie der Sidetone-Feed (VivoxAudioProcessor.
    /// m_streamClip/m_writePointer, Main-Thread-Pull): Unity nullt
    /// OnAudioFilterRead-Samples bei volume=0 (Beweis Log 20260918-1318), die
    /// Mund-Wiedergabe derselben AudioSource (Entfernungsdaempfung ueber
    /// tap.volume) darf aber unangetastet bleiben.
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieParticipantTapFeed : MonoBehaviour
    {
        // VivoxAudioProcessor-Interna (Reflektion; Paket com.unity.services.vivox
        // ist gepinnt - Feldnamen aus VivoxAudioProcessor.cs, identisch zum
        // Sidetone-Feed in WalkieSidetoneCapture.cs).
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

        private string playerId;
        private bool bound;

        // Diagnose (Session-Log): Aktivierung, erstes echtes Signal, Herzschlag.
        private int diagnosticPulls;
        private int diagnosticFrames;
        private float diagnosticPeak;
        private bool activityLogged;
        private bool signalLogged;
        private float nextHeartbeat;
        private string lastLoggedChannel;

        /// <summary>
        /// Bindet den Feed an einen Proximity-Participant-Tap. Wird von der
        /// VoiceRuntime beim Erscheinen des Sprechers aufgerufen.
        /// </summary>
        internal void Bind(string remotePlayerId, VivoxAudioTap tap, AudioSource source)
        {
            playerId = remotePlayerId;
            tapSource = source;
            pullQuantumFrames = System.Math.Max(1, SafeOutputSampleRate() / 100); // 10 ms
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
            bound = !string.IsNullOrEmpty(playerId);
        }

        private void Update()
        {
            if (!bound || tapSource == null) return;
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

            bool active = WalkieTalkieRegistry.TryGetRemoteTransmitChannel(
                playerId, out string channel);

            if (!active || string.IsNullOrEmpty(channel))
            {
                // Cursor trotzdem mitziehen, damit beim Aktivieren keine alten
                // Samples in den Bus laufen (identisch zum Sidetone-Feed).
                lastWritePointer = write;
                LogDeactivated();
                return;
            }

            LogActivated(channel);

            // Feste Pull-Quanten (10 ms) halten die GetData-Buffergroesse stabil
            // (keine pro-Frame-Allokationen); ein Rest unter einer Quante wartet
            // auf den naechsten Frame. Nur der Seam-Chunk am Clip-Ende ist mal
            // kleiner - eine Allokation pro ~3 s ist vernachlaessigbar.
            int cursor = lastWritePointer;
            int remaining = delta;
            while (remaining >= pullQuantumFrames)
            {
                int chunk = System.Math.Min(pullQuantumFrames, total - cursor);
                ProcessSegment(cursor, chunk, channel);
                cursor = (cursor + chunk) % total;
                remaining -= chunk;
            }

            lastWritePointer = cursor;
            LogHeartbeat(channel);
        }

        private void ProcessSegment(int offset, int frames, string channel)
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

            diagnosticPulls++;
            diagnosticFrames += frames;
            TrackPeak(frames);
            LogFirstSignal();

            WalkieRadioBus.Write(channel, playerId, monoBuffer, 0, frames);
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

        private void TrackPeak(int frames)
        {
            float peak = 0f;
            for (int i = 0; i < frames; i++)
            {
                float absolute = monoBuffer[i] < 0f ? -monoBuffer[i] : monoBuffer[i];
                if (absolute > peak) peak = absolute;
            }

            if (peak > diagnosticPeak) diagnosticPeak = peak;
        }

        private void LogActivated(string channel)
        {
            if (activityLogged &&
                string.Equals(lastLoggedChannel, channel, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            activityLogged = true;
            lastLoggedChannel = channel;
            signalLogged = false;
            VoiceSessionLog.Note(
                $"WALKIE REMOTE FEED an: {playerId} @ '{channel}' " +
                "(Quelle: Proximity-Participant-Tap, v18 Funk-ueber-Proximity).");
        }

        private void LogDeactivated()
        {
            if (!activityLogged) return;

            activityLogged = false;
            lastLoggedChannel = null;
            VoiceSessionLog.Note($"WALKIE REMOTE FEED aus: {playerId}");
        }

        private void LogFirstSignal()
        {
            if (signalLogged || diagnosticPeak < 0.01f) return;

            signalLogged = true;
            VoiceSessionLog.Note(
                $"WALKIE REMOTE FEED Signal: {playerId} peak={diagnosticPeak:0.000} " +
                "(Funk-Strom liegt auf dem Bus).");
        }

        private void LogHeartbeat(string channel)
        {
            if (Time.unscaledTime < nextHeartbeat) return;

            nextHeartbeat = Time.unscaledTime + 5f;
            VoiceSessionLog.Note(
                $"WALKIE REMOTE FEED Herzschlag: {playerId} @ '{channel}', " +
                $"pulls={diagnosticPulls}, frames={diagnosticFrames}, peak={diagnosticPeak:0.000}");
            diagnosticPulls = 0;
            diagnosticFrames = 0;
            diagnosticPeak = 0f;
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
                "WALKIE REMOTE FEED: Reflektions-Zugriff auf VivoxAudioProcessor " +
                "(m_writePointer/m_streamClip) fehlgeschlagen - Funk-Empfang ueber " +
                "Proximity liegt still. Vivox-Paket-Version pruefen.");
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
}
