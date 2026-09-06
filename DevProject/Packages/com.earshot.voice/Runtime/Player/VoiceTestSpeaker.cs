using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Simuliert einen zweiten Sprecher im Raum, ohne Vivox und ohne zweite Spielinstanz.
    /// <para>
    /// Ein Testton laeuft durch genau dieselbe <see cref="VoicePipeline"/> wie eine echte
    /// Stimme - Distanz, Waende, Tueren und Raeume wirken exakt so, wie sie spaeter bei
    /// einem echten Mitspieler wirken wuerden. Damit laesst sich das komplette raeumliche
    /// Klangverhalten in einem einzigen Spielstart pruefen, allein am eigenen Rechner.
    /// </para>
    /// <para>
    /// Was das bewusst NICHT prueft: die echte Vivox-Verbindung selbst (Mikrofon, Login,
    /// Netzwerk-Tap). Dafuer braucht es weiterhin zwei echte Teilnehmer.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Voice Test Speaker")]
    public class VoiceTestSpeaker : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Aus, sobald eine echte Vivox-Verbindung laeuft. Verhindert, dass ein echter Mitspieler den Testton hoert.")]
        private bool onlyWithoutRealConnection = true;

        [SerializeField]
        [Tooltip("Eindeutiger Name, falls mehrere Testlautsprecher in derselben Szene stehen.")]
        private string speakerId = "testsprecher";

        private const float SafetyRecheckSeconds = 0.5f;

        private GameObject host;
        private VoiceEmitter emitter;
        private AudioClip clip;
        private float nextSafetyRecheck;

        public bool IsPlaying => emitter != null && isActiveAndEnabled;

        public void Toggle()
        {
            enabled = !enabled;
        }

        public void SetPlaying(bool playing)
        {
            enabled = playing;
        }

        public void SetSpeakerId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            speakerId = id;
        }

        private void OnEnable()
        {
            // Ausserhalb des Play-Modus (z.B. waehrend ein Editor-Skript die Szene baut)
            // darf hier nichts passieren - VoiceRuntime.EnsureExists() ruft unter anderem
            // DontDestroyOnLoad auf, was im Editier-Modus nicht erlaubt ist.
            if (!Application.isPlaying) return;

            Refresh();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;

            Remove();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSafetyRecheck) return;
            nextSafetyRecheck = Time.unscaledTime + SafetyRecheckSeconds;
            Refresh();
        }

        private void Refresh()
        {
            if (!Application.isPlaying) return;

            bool shouldRun = !onlyWithoutRealConnection || !EarshotVoice.IsConnected;
            if (shouldRun) Ensure();
            else Remove();
        }

        private void Ensure()
        {
            if (emitter != null) return;

            var runtime = VoiceRuntime.EnsureExists();

            // Eigenes Kind-Objekt statt Komponenten direkt an diesem GameObject: so raeumt
            // ein einziges Destroy alles wieder auf (AudioSource, Filter, Capture-Helfer),
            // egal wie oft Ensure/Remove beim Wechseln des Verbindungszustands laufen.
            host = new GameObject("Voice Test Speaker (laufend)");
            host.transform.SetParent(transform, false);

            var source = host.AddComponent<AudioSource>();
            emitter = host.AddComponent<VoiceEmitter>();
            emitter.Initialize("debug:" + speakerId, source, EarshotVoiceSettings.Instance.VoiceProfile);
            emitter.Anchor = transform;

            clip = BuildTestClip();
            emitter.PlayLoop(clip);

            runtime.RegisterDebugEmitter(emitter);
            EarshotVoiceLog.Info($"Voice-Testton '{speakerId}' laeuft an {name}.");
        }

        private void Remove()
        {
            if (emitter != null)
            {
                VoiceRuntime.Instance?.UnregisterDebugEmitter(emitter);
                emitter = null;
            }

            if (host != null)
            {
                Destroy(host);
                host = null;
            }

            if (clip != null)
            {
                Destroy(clip);
                clip = null;
            }
        }

        /// <summary>
        /// Baut eine kurze, in sich geschlossene Tonschleife im Sprechrhythmus - Ton, Pause,
        /// Ton - mit zwei Obertoenen, damit Tiefpass/Hochpass-Aenderungen (Waende, Tueren)
        /// deutlich hoerbar sind. Kein Asset noetig, funktioniert sofort in jeder Szene.
        /// </summary>
        private static AudioClip BuildTestClip()
        {
            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate < 8000) sampleRate = 48000;

            const float lengthSeconds = 3.2f;
            int totalSamples = Mathf.CeilToInt(sampleRate * lengthSeconds);
            var data = new float[totalSamples];

            float[] burstStart = { 0.05f, 0.55f, 1.05f, 1.75f, 2.25f, 2.75f };
            float[] burstLength = { 0.35f, 0.30f, 0.45f, 0.30f, 0.35f, 0.30f };

            float dt = 1f / sampleRate;
            float t = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float sample = 0f;

                for (int b = 0; b < burstStart.Length; b++)
                {
                    float start = burstStart[b];
                    float end = start + burstLength[b];
                    if (t < start || t >= end) continue;

                    float local = t - start;
                    float envelope = Mathf.Sin(Mathf.PI * local / burstLength[b]);
                    float wobble = 1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 5f * t);
                    float tone =
                        Mathf.Sin(2f * Mathf.PI * 180f * wobble * t) * 0.6f +
                        Mathf.Sin(2f * Mathf.PI * 360f * wobble * t) * 0.3f;
                    sample = tone * envelope * 0.5f;
                    break;
                }

                data[i] = sample;
                t += dt;
            }

            var result = AudioClip.Create("Earshot Testton", totalSamples, 1, sampleRate, false);
            result.SetData(data, 0);
            return result;
        }
    }
}
