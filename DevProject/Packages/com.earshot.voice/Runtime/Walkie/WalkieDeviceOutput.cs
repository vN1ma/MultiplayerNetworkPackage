using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lautsprecher an einem Walkie: Remote-Gewinner oder lokales Sidetone, mit Delay.
    /// v16.7 (Phase 1c, E3): Der Geraeteton laeuft durch denselben Welt-Messweg wie die
    /// Stimme - Zonen, Graph-Pfad, Tueren, Waende. Ein liegendes Walkie klingt damit wie
    /// eine Quelle an diesem Ort: dumpf hinter der geschlossenen Tuer, leiser um den
    /// Umweg, nicht mehr nach reiner Luftlinie.
    /// v16.4: Die effektive Lautstaerke wird IM OnAudioFilterRead multipliziert.
    /// Unity zieht AudioSource.volume und AudioListener.volume VOR OnAudioFilterRead
    /// vom Datenstrom ab - wer dort data komplett UEBERSCHREIBT, umgeht saemtliche
    /// Lautstaerkeregeln (Leak-Beweis Log 20260919-090425: Sidetone in vollem Pegel
    /// im Master bei listenerVol=0 und allen device-vol=0, abhaengig allein vom Feed).
    /// Deshalb bleibt source.volume konstant 1 und smoothedVolume + gespiegelter
    /// Listener-Master werden im Filter durchgesetzt.
    /// <para>
    /// Wichtig: <see cref="OnAudioFilterRead"/> laeuft auf dem Audio-Thread —
    /// dort kein <c>AudioSettings</c>, keine Allokationen, keine Unity-API.
    /// Der Delay-Ring arbeitet in MONO-Frames (siehe <see cref="WalkieRadioBus"/>);
    /// am Ende wird jeder Mono-Frame auf alle Ausgabe-Kanaele verteilt. So bleibt
    /// die Zeitbasis unabhaengig von Unity's Kanalzahl korrekt (kein Chipmunk-Sound).
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(AudioSource))]
    internal sealed class WalkieDeviceOutput : MonoBehaviour
    {
        private const int MaxDelaySeconds = 2;
        private const float VolumeSmoothPerSecond = 6f;

        // Wie schnell der Welt-Tiefpass (Tuer faellt zu, Wand dazwischen) nachzieht.
        // Logarithmisch (wie die Stimmen-Filter im Profil), damit nichts knackt.
        private const float WorldFilterSmoothPerSecond = 10f;
        private const float ActiveDiagnosticIntervalSeconds = 5f;

        // Nie naeher als das rechnen — sonst kann ein Geraet direkt am Ohr (z.B. Hand-Modell
        // sehr nah am Kopf) auf maximale Lautstaerke kommen und mit dem Mikro echte akustische
        // Rueckkopplung ("Heulen") ausloesen, die von Aufnahme zu Aufnahme lauter wird.
        private const float MinPerceivedDistance = 0.9f;

        // Schutz gegen Duplikate/Desync (z.B. ein lokales Sicht-/Handmodell-Walkie, dessen
        // eigenes IsTransmitting-Flag nie gesetzt wird): ein Geraet direkt an der eigenen
        // Hoerposition darf niemals die eigene Stimme (Sidetone) abspielen, egal was sein
        // eigener Sende-Status sagt — der ganze Sinn von Sidetone ist, sich AN EINEM ANDEREN
        // Geraet zu hoeren, nicht am eigenen.
        private const float MinSidetoneSelfDistance = 0.5f;

        private EarshotWalkieTalkie walkie;
        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;

        private readonly WalkieAudioRing inbox = new WalkieAudioRing(48000);

        // Delay-Ring in MONO-Frames (nicht mit Kanalzahl multipliziert).
        private float[] delayRing;
        private int delayWrite;
        private bool delayPrimed;
        private float delaySeconds = 0.2f;
        private int outputChannels = 1;
        private int sampleRate = 48000;
        private string lastStreamId;

        private volatile int pendingChannels;
        private volatile bool delayReady;

        private float[] monoPullBuffer = new float[2048];
        private float smoothedVolume;

        // v16.7 (Phase 1c): Welt-Auswertung fuer den Geraeteton. Laeuft in LateUpdate
        // pro aktivem Geraet einmal durch die VoicePipeline (ohne Distanz-Modul) und
        // liefert Welt-Daempfung + Tiefpass, die in die Geraete-Regelung gemischt werden.
        private readonly VoicePipeline worldPipeline = new VoicePipeline();
        private float worldVolume = 1f;
        private float worldLowPassHz = VoiceSample.NoLowPass;
        private float smoothedWorldLowPassHz = VoiceSample.NoLowPass;

        // v16.4: AudioListener.volume wird von Unity VOR OnAudioFilterRead
        // abgezogen und kann durch das Ueberschreiben von data umgangen werden
        // (Leak-Beweis Log 20260919-090425: Sprache im Master bei Master=0).
        // Der Master-Pegel wird deshalb im Main-Thread gespiegelt und im
        // OnAudioFilterRead multiplikativ durchgesetzt - so greift die
        // F12-Diagnose (und jede kuenftige globale Stummschaltung) auch fuer
        // die Walkie-Lautsprecher.
        internal static volatile float GlobalListenerVolume = 1f;
        private float radioCrunch;
        private string lastDiagnosticState;
        private float nextActiveDiagnostic;

        // Sample-and-Hold-Zustand fuer den Alter-Funk-Effekt (nur Audio-Thread).
        private int crunchHoldCounter;
        private float crunchHeldValue;

        internal void Bind(EarshotWalkieTalkie owner)
        {
            walkie = owner;
            CacheSampleRate();
            EnsureAudio();
            EnsureDelayCapacity();
            ApplyEq();
            WalkieRadioBus.Register(this);
        }

        private void OnEnable()
        {
            CacheSampleRate();
            EnsureDelayCapacity();
            WalkieRadioBus.Register(this);
        }

        private void OnDisable()
        {
            WalkieRadioBus.Unregister(this);
            delayReady = false;
        }

        internal void PushSamples(
            string channelId,
            string streamId,
            float[] data,
            int offset,
            int length)
        {
            if (walkie == null || !walkie.PoweredOn || walkie.IsTransmitting) return;
            if (!string.Equals(walkie.ChannelId, channelId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string wanted = ResolveStreamId();
            if (string.IsNullOrEmpty(wanted) ||
                !string.Equals(wanted, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(lastStreamId, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                inbox.Clear();
                lastStreamId = streamId;
            }

            inbox.Write(data, offset, length);
        }

        internal void ClearInbox(string channelId, string streamId)
        {
            if (walkie == null) return;
            if (!string.Equals(walkie.ChannelId, channelId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(lastStreamId, streamId, System.StringComparison.OrdinalIgnoreCase))
            {
                inbox.Clear();
            }
        }

        internal void ClearAllInbox()
        {
            inbox.Clear();
            lastStreamId = null;
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
                // Scene-Load / falscher Thread — Default behalten.
            }
        }

        private void CacheListenerVolume()
        {
            try
            {
                float v = AudioListener.volume;
                if (v >= 0f && v <= 1f) GlobalListenerVolume = v;
            }
            catch
            {
                // Kein Listener in der Szene - letzter Spiegelwert bleibt.
            }
        }

        private void EnsureAudio()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
                if (source == null) source = gameObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            // Entfernung nur ueber unser Volume — Unity-Rolloff wuerde sonst doppelt daempfen.
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            source.minDistance = 0.4f;
            source.maxDistance = 50f;
            source.mute = false;
            // v16.4: konstant 1 - die Regelung laeuft autoritativ im
            // OnAudioFilterRead (smoothedVolume * GlobalListenerVolume).
            source.volume = 1f;

            if (lowPass == null)
            {
                lowPass = GetComponent<AudioLowPassFilter>();
                if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            }

            if (highPass == null)
            {
                highPass = GetComponent<AudioHighPassFilter>();
                if (highPass == null) highPass = gameObject.AddComponent<AudioHighPassFilter>();
            }

            // Filter bleiben immer aktiv — Ein/Ausschalten der Komponente selbst
            // verursacht Klicks. Stille kommt allein ueber Lautstaerke/Samples.
            lowPass.enabled = true;
            highPass.enabled = true;

            if (source.clip == null)
            {
                source.clip = AudioClip.Create("EarshotWalkieOut", 256, 1, sampleRate, false);
                var zeros = new float[256];
                source.clip.SetData(zeros, 0);
            }

            if (!source.isPlaying) source.Play();
        }

        private void EnsureDelayCapacity()
        {
            int needed = Mathf.Max(1, MaxDelaySeconds * sampleRate);
            if (delayRing != null && delayRing.Length >= needed)
            {
                delayReady = true;
                return;
            }

            delayRing = new float[needed];
            delayWrite = 0;
            delayPrimed = false;
            delayReady = true;
        }

        private void LateUpdate()
        {
            if (walkie == null) return;

            CacheSampleRate();
            EnsureAudio();

            if (pendingChannels > 0 && pendingChannels != outputChannels)
            {
                outputChannels = pendingChannels;
            }

            EnsureDelayCapacity();
            ApplyEq();

            Transform anchor = walkie.AudioAnchor;
            if (anchor != null) transform.position = anchor.position;

            source.maxDistance = walkie.MaxHearingDistance;
            delaySeconds = walkie.TransmissionDelaySeconds;
            radioCrunch = walkie.RadioCrunch;

            bool active = ShouldOutput();
            bool sidetone = IsSidetoneMode();

            float falloff = EvaluateWorldFalloff(
                active,
                out float listenerDistance,
                out float pathDistance,
                out bool usedGraph,
                out Vector3 listenerPosition);

            float targetVolume = 0f;
            if (active)
            {
                float baseVol = sidetone
                    ? WalkieTalkieRegistry.ActiveSidetoneWorldVolume
                    : walkie.RadioVolume;
                targetVolume = Mathf.Clamp01(
                    baseVol * falloff * worldVolume * EarshotVoice.HeardVoiceVolume);
            }

            SmoothWorldLowPass();

            smoothedVolume = Mathf.MoveTowards(
                smoothedVolume,
                targetVolume,
                Time.unscaledDeltaTime * VolumeSmoothPerSecond);

            // v16.4: Unity-Volume bleibt konstant 1 (Regelung im Filter,
            // keine Doppel-Daempfung); zusaetzlich den Listener-Master
            // fuer den Filter spiegeln (F12-Diagnose).
            source.volume = 1f;
            CacheListenerVolume();

            ReportOutputDiagnostic(
                active,
                targetVolume,
                falloff,
                listenerDistance,
                pathDistance,
                usedGraph,
                listenerPosition);
        }

        private bool IsSidetoneMode()
        {
            return WalkieTalkieRegistry.LocalIsTransmitting &&
                   string.Equals(
                       WalkieTalkieRegistry.LocalTransmitChannelId,
                       walkie.ChannelId,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldOutput()
        {
            if (walkie == null || !walkie.PoweredOn) return false;
            if (walkie.IsTransmitting) return false;

            if (IsSidetoneMode())
            {
                // Steht dieses Geraet praktisch an der eigenen Hoerposition (z.B. ein
                // Sicht-/Handmodell-Duplikat ohne synchronisierten Sendezustand), niemals
                // die eigene Stimme dort ausgeben — das waere kein Sidetone, sondern ein
                // Feedback-Kandidat direkt am eigenen Ohr.
                if (TryListenerPosition(out Vector3 selfListener) &&
                    Vector3.Distance(selfListener, transform.position) < MinSidetoneSelfDistance)
                {
                    return false;
                }

                return true;
            }

            if (WalkieTalkieRegistry.LocalIsTransmitting) return false;

            string winner = WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
            return !string.IsNullOrEmpty(winner);
        }

        /// <summary>
        /// Reichweiten-Falloff des Geraets (1 - d/max)^2 - aber auf dem akustischen WEG,
        /// nicht mehr auf der Luftlinie (E3): Steht eine Wand oder geschlossene Tuer
        /// zwischen Geraet und Zuhoerer, verlaengert sich der hoerbare Weg. Zusaetzlich
        /// liefert die Welt-Auswertung worldVolume (Tuer/Wand/Zonen-Daempfung) und
        /// worldLowPassHz (dumpf), die LateUpdate in die Geraete-Regelung einmischt.
        /// </summary>
        private float EvaluateWorldFalloff(
            bool active,
            out float listenerDistance,
            out float pathDistance,
            out bool usedGraph,
            out Vector3 listener)
        {
            listenerDistance = -1f;
            pathDistance = -1f;
            usedGraph = false;
            worldVolume = 1f;
            worldLowPassHz = VoiceSample.NoLowPass;
            listener = default;

            // Kein bekannter Zuhoerer-Ort: lieber still als versehentlich auf voller
            // Lautstaerke senden (frueher wurde hier faelschlich 1f/volle Lautstaerke
            // zurueckgegeben). Gilt auch fuer inaktive Geraete - die Diagnose-Begruendung
            // (NO_LISTENER vs. NO_REMOTE_WINNER/HALF_DUPLEX) braucht die Distanz immer.
            if (!TryListenerPosition(out Vector3 listenerPos))
            {
                return 0f;
            }

            listener = listenerPos;
            listenerDistance = Vector3.Distance(listenerPos, transform.position);

            // Ohne aktiven Pfad gilt weiter die Luftlinie - die Welt-Auswertung
            // (Raycasts, Graph) lohnt sich nur fuer Geraete, die tatsaechlich Ton geben.
            pathDistance = listenerDistance;

            if (!active)
            {
                return 0f;
            }

            var settings = EarshotVoiceSettings.Instance;
            var profile = settings != null ? settings.VoiceProfile : null;
            var world = worldPipeline.EvaluateWorldAttenuation(
                profile,
                listenerPos,
                transform.position,
                Time.unscaledDeltaTime,
                out var context);

            worldVolume = world.Volume;
            worldLowPassHz = world.LowPassHz;
            usedGraph = context.UsedGraph;
            pathDistance = context.HearingDistance > 0f
                ? context.HearingDistance
                : listenerDistance;

            float max = Mathf.Max(1f, walkie.MaxHearingDistance);
            float d = Mathf.Max(pathDistance, MinPerceivedDistance);
            float t = 1f - Mathf.Clamp01(d / max);
            return t * t;
        }

        private void SmoothWorldLowPass()
        {
            // Logarithmisch nachziehen (Tonhoehe wird so wahrgenommen), damit eine
            // zufallende Tuer nicht im Filter knackt.
            float from = Mathf.Max(1f, smoothedWorldLowPassHz);
            float to = Mathf.Max(1f, worldLowPassHz);
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * WorldFilterSmoothPerSecond);
            smoothedWorldLowPassHz = Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), t));
        }

        private void ReportOutputDiagnostic(
            bool pathActive,
            float targetVolume,
            float falloff,
            float listenerDistance,
            float pathDistance,
            bool usedGraph,
            Vector3 listenerPosition)
        {
            bool sidetone = IsSidetoneMode();
            bool audible = pathActive && targetVolume > 0.0001f;
            string mode = sidetone ? "SIDETONE" : "REMOTE";
            string stream = sidetone
                ? WalkieRadioBus.LocalSidetoneStreamId
                : WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
            string reason = ResolveDiagnosticReason(pathActive, listenerDistance, pathDistance);
            string state = $"{mode}|{audible}|{reason}|{stream}|{usedGraph}";
            float now = Time.unscaledTime;

            bool changed = !string.Equals(
                state,
                lastDiagnosticState,
                System.StringComparison.Ordinal);
            bool periodic = audible && now >= nextActiveDiagnostic;
            if (!changed && !periodic) return;

            lastDiagnosticState = state;
            nextActiveDiagnostic = now + ActiveDiagnosticIntervalSeconds;

            int listenerCount = CountEnabledAudioListeners();
            string distanceText = listenerDistance >= 0f
                ? listenerDistance.ToString("0.00") + "m"
                : "unbekannt";
            string pathText = pathDistance >= 0f
                ? pathDistance.ToString("0.00") + "m"
                : "unbekannt";

            VoiceSessionLog.Note(
                $"WALKIE OUTPUT {(audible ? "AN" : "AUS")}: device='{walkie.gameObject.name}', " +
                $"channel='{walkie.ChannelId}', mode={mode}, stream='{stream}', reason={reason}, " +
                $"distance={distanceText}/{walkie.MaxHearingDistance:0.00}m, path={pathText}, " +
                $"graph={usedGraph}, worldVol={worldVolume:0.000}, falloff={falloff:0.000}, " +
                $"target={targetVolume:0.000}, actual={smoothedVolume:0.000}, master={GlobalListenerVolume:0.000}, " +
                $"listener={listenerPosition.ToString("F2")}, speaker={transform.position.ToString("F2")}, " +
                $"listeners={listenerCount}, HP={walkie.HighPassHz:0}Hz, " +
                $"LP={Mathf.Min(walkie.LowPassHz, smoothedWorldLowPassHz):0}Hz, " +
                $"crunch={walkie.RadioCrunch:0.00}, delay={walkie.TransmissionDelaySeconds:0.000}s");
        }

        private string ResolveDiagnosticReason(bool pathActive, float listenerDistance, float pathDistance)
        {
            if (!walkie.PoweredOn) return "POWER_OFF";
            if (walkie.IsTransmitting) return "OWN_DEVICE_TX";
            if (listenerDistance < 0f) return "NO_LISTENER";
            if (pathDistance >= walkie.MaxHearingDistance) return "OUT_OF_RANGE";
            if (IsSidetoneMode() && listenerDistance < MinSidetoneSelfDistance)
            {
                return "SELF_DISTANCE_GUARD";
            }

            if (!pathActive)
            {
                if (WalkieTalkieRegistry.LocalIsTransmitting) return "HALF_DUPLEX";
                return "NO_REMOTE_WINNER";
            }

            return "AUDIBLE";
        }

        private static int CountEnabledAudioListeners()
        {
            var listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].enabled) count++;
            }

            return count;
        }

        private static bool TryListenerPosition(out Vector3 position)
        {
            // Fuer die tatsaechliche Wiedergabe ist der aktive Unity-Listener die
            // verlaessliche Hoerposition. Ein falsch/zu spaet als lokal markierter
            // VoiceRoster-Avatar darf Sidetone nicht aus beliebiger Entfernung hoerbar machen.
            var listener = VoiceRoster.FindPreferredAudioListener();
            if (listener != null)
            {
                position = listener.transform.position;
                return true;
            }

            var local = VoiceRoster.LocalPlayer;
            if (local?.VoiceAnchor != null)
            {
                position = local.VoiceAnchor.position;
                return true;
            }

            position = default;
            return false;
        }

        private void ApplyEq()
        {
            if (walkie == null) return;
            if (highPass != null) highPass.cutoffFrequency = walkie.HighPassHz;
            if (lowPass != null)
            {
                // Der Geraeteton haengt in der Welt (v16.7): Dumpf durch geschlossene
                // Tueren und Waende unterbiegt das eigene Funk-EQ nie - es gilt der
                // dumpfere der beiden Werte. Geglattet, damit nichts knackt.
                float cutoff = Mathf.Min(walkie.LowPassHz, smoothedWorldLowPassHz);
                lowPass.cutoffFrequency = Mathf.Max(VoiceSample.NoHighPass, cutoff);
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                int ch = channels > 0 ? channels : 1;
                if (ch != outputChannels || !delayReady || delayRing == null)
                {
                    pendingChannels = ch;
                    Silence(data);
                    return;
                }

                int frames = data.Length / ch;
                if (frames <= 0)
                {
                    Silence(data);
                    return;
                }

                if (monoPullBuffer.Length < frames) monoPullBuffer = new float[frames];

                bool play = walkie != null && walkie.PoweredOn && !walkie.IsTransmitting;
                int got = play ? inbox.Read(monoPullBuffer, 0, frames) : 0;
                for (int i = got; i < frames; i++) monoPullBuffer[i] = 0f;

                float delaySec = delaySeconds;
                if (delaySec < 0f) delaySec = 0f;
                if (delaySec > 1.5f) delaySec = 1.5f;

                int rate = sampleRate > 0 ? sampleRate : 48000;
                int delaySamples = Mathf.CeilToInt(delaySec * rate);
                if (delaySamples < 1) delaySamples = 1;
                if (delaySamples > delayRing.Length) delaySamples = delayRing.Length;

                // v16.4 (ROOT-CAUSE-FIX): Die finale Lautstaerke hier im Filter
                // durchsetzen. Unity wendet AudioSource.volume und
                // AudioListener.volume VOR OnAudioFilterRead an; das komplette
                // Ueberschreiben von data mit den Ring-Samples umging beide -
                // dadurch spielte der Sidetone/Remote-Ton in VOLLEM Pegel von
                // jedem Geraet im Kanal, unabhaengig von Distanz, OWN_DEVICE_TX,
                // OUT_OF_RANGE und F12 (Leak-Beweis Log 20260919-090425).
                float outputVolume = smoothedVolume * GlobalListenerVolume;
                if (outputVolume < 0f) outputVolume = 0f;
                if (outputVolume > 1f) outputVolume = 1f;

                // Puffer kontinuierlich weiterschieben — nie fruehzeitig abbrechen,
                // sonst entsteht am naechsten Aufruf ein hoerbarer Sprung (Klacken).
                for (int f = 0; f < frames; f++)
                {
                    float incoming = monoPullBuffer[f];
                    int readIndex = delayWrite - delaySamples;
                    if (readIndex < 0) readIndex += delayRing.Length;

                    float outgoing = delayPrimed ? delayRing[readIndex] : 0f;
                    delayRing[delayWrite] = incoming;
                    delayWrite++;
                    if (delayWrite >= delayRing.Length)
                    {
                        delayWrite = 0;
                        delayPrimed = true;
                    }

                    outgoing = ApplyRadioCrunch(outgoing, radioCrunch) * outputVolume;

                    int baseIdx = f * ch;
                    for (int c = 0; c < ch; c++) data[baseIdx + c] = outgoing;
                }
            }
            catch
            {
                Silence(data);
            }
        }

        /// <summary>
        /// Bewusster "altes Funkgeraet"-Charakter: grobe Stufen (Sample-and-Hold), reduzierte
        /// Aufloesung (Bit-Crush) und leichte weiche Verzerrung. Deutlich hoerbar von der
        /// glatten Mund-Stimme unterscheidbar, aber deterministisch (kein Knacken/Glitch).
        /// </summary>
        private float ApplyRadioCrunch(float sample, float amount)
        {
            if (amount <= 0.001f) return sample;

            int hold = 1 + Mathf.RoundToInt(amount * 2f);
            if (crunchHoldCounter <= 0)
            {
                crunchHeldValue = sample;
                crunchHoldCounter = hold;
            }

            crunchHoldCounter--;

            float levels = Mathf.Lerp(48f, 10f, amount);
            float quantized = Mathf.Round(crunchHeldValue * levels) / levels;

            float drive = 1f + amount * 1.2f;
            float distorted = Mathf.Clamp(quantized * drive, -1f, 1f);

            return Mathf.Lerp(sample, distorted, amount);
        }

        private static void Silence(float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
        }

        private string ResolveStreamId()
        {
            if (walkie == null) return null;

            if (IsSidetoneMode()) return WalkieRadioBus.LocalSidetoneStreamId;

            if (WalkieTalkieRegistry.LocalIsTransmitting) return null;

            return WalkieRadioBus.GetAudibleRemote(walkie.ChannelId);
        }
    }
}
