using System.Collections.Generic;
using Unity.Services.Vivox;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Lauscht der laufenden Sitzung und schreibt Rede-/Hoer-Ereignisse ins Voice-Log.
    /// Aendert den Klang nicht, zaehlt nur mit.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(50)]
    public sealed class VoiceSessionRecorder : MonoBehaviour
    {
        private const float HearSampleInterval = 1f;
        private const float HeardVolume = 0.05f;

        private readonly Dictionary<string, bool> speaking = new Dictionary<string, bool>();
        private readonly Dictionary<string, float> lastHearLog = new Dictionary<string, float>();
        private readonly Dictionary<string, string> lastHearKey = new Dictionary<string, string>();
        private readonly Dictionary<string, string> lastMode = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> lastTapAlive = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastTapObjectActive = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastTapSourcePlaying = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastInAudio = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastEnergyOn = new Dictionary<string, bool>();

        private CoopState lastState = CoopState.Offline;
        private bool toasted;
        private float toastUntil;
        private bool warnedLocalVoice;
        private int lastListenerCount = -1;
        private bool lastFocused = true;
        private float lastMasterVolume = -1f;
        private float lastHeardVolume = -1f;

        private void OnEnable()
        {
            Coop.StateChanged += OnStateChanged;
            Coop.PlayerJoined += OnPlayerJoined;
            Coop.PlayerLeft += OnPlayerLeft;
            OnStateChanged(Coop.State);
        }

        private void OnDisable()
        {
            Coop.StateChanged -= OnStateChanged;
            Coop.PlayerJoined -= OnPlayerJoined;
            Coop.PlayerLeft -= OnPlayerLeft;
            VoiceSessionLog.EndSession("Recorder aus.");
        }

        private void OnApplicationQuit()
        {
            VoiceSessionLog.EndSession("Spiel beendet.");
        }

        private void OnStateChanged(CoopState state)
        {
            if (state == lastState) return;
            lastState = state;

            if (state == CoopState.Hosting || state == CoopState.Connected)
            {
                VoiceSessionLog.BeginSession();
                toasted = false;
                toastUntil = Time.unscaledTime + 10f;
                speaking.Clear();
                lastHearLog.Clear();
                lastHearKey.Clear();
                lastMode.Clear();
                lastTapAlive.Clear();
                lastTapObjectActive.Clear();
                lastTapSourcePlaying.Clear();
                lastInAudio.Clear();
                lastEnergyOn.Clear();
                lastListenerCount = -1;
                lastMasterVolume = -1f;
                lastHeardVolume = -1f;
                warnedLocalVoice = false;
                VoiceSessionLog.Note(
                    $"SITZUNG {state}. Voice={(CoopVoice.IsConnected ? "Vivox an" : "noch nicht verbunden")} " +
                    $"Mikrofon={(CoopVoice.MicrophoneMuted ? "STUMM" : "an")}");
                LogExistingPlayers();
            }
            else if (state == CoopState.Offline)
            {
                VoiceSessionLog.EndSession("Offline.");
            }
            else
            {
                VoiceSessionLog.Note($"SITZUNG Zustand {state}.");
            }
        }

        private static void OnPlayerJoined(CoopPlayer player)
        {
            if (player == null) return;
            VoiceSessionLog.Note(
                $"SPIELER da: {player.DisplayName}  Client {player.OwnerClientId}  " +
                $"lokal={player.IsLocalPlayer}  id={player.UgsPlayerId}");
        }

        private static void OnPlayerLeft(CoopPlayer player)
        {
            if (player == null) return;
            VoiceSessionLog.Note($"SPIELER weg: {player.DisplayName}");
        }

        private void Update()
        {
            if (!Coop.IsInSession || !VoiceSessionLog.IsRecording) return;

            if (Coop.IsLocalSession)
            {
                if (!warnedLocalVoice)
                {
                    warnedLocalVoice = true;
                    VoiceSessionLog.Alert(
                        "PROBLEM: Lokales Spiel - Proximity-Chat mit einem Freund braucht " +
                        "'Spiel hosten (Internet)'.");
                }

                return;
            }

            if (CoopVoice.IsConnected && !toasted)
            {
                toasted = true;
                VoiceSessionLog.Note(
                    "VIVOX verbunden. Eigene Stimme geht raus, fremde Stimmen kommen als Tap an.");
                LogSnapshot("nach Vivox");
                toastUntil = Time.unscaledTime + 10f;
            }

            PollSpeech();
            PollHearing();
            PollMachine();
        }

        private static void LogExistingPlayers()
        {
            var players = PlayerRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                OnPlayerJoined(players[i]);
            }
        }

        private void PollSpeech()
        {
            var service = VivoxService.Instance;
            if (service == null || !service.IsLoggedIn) return;
            if (service.ActiveChannels == null) return;

            foreach (var pair in service.ActiveChannels)
            {
                var list = pair.Value;
                if (list == null) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var participant = list[i];
                    if (participant == null) continue;

                    string key = participant.IsSelf ? "self" : participant.PlayerId;
                    string name = DisplayName(participant.IsSelf, participant.PlayerId);

                    bool inAudio = participant.IsInAudio;
                    lastInAudio.TryGetValue(key, out bool wasInAudio);
                    if (!lastInAudio.ContainsKey(key) || wasInAudio != inAudio)
                    {
                        lastInAudio[key] = inAudio;
                        if (inAudio)
                        {
                            VoiceSessionLog.Note($"VIVOX KANAL: {name} ist im Audio");
                        }
                        else
                        {
                            VoiceSessionLog.Alert($"VIVOX KANAL: {name} NICHT im Audio - Stimme kommt nicht an");
                        }
                    }

                    bool energyOn = participant.AudioEnergy > (lastEnergyOn.ContainsKey(key) && lastEnergyOn[key] ? 0.005 : 0.02);
                    lastEnergyOn.TryGetValue(key, out bool wasEnergy);
                    if (!lastEnergyOn.ContainsKey(key))
                    {
                        lastEnergyOn[key] = energyOn;
                        if (energyOn)
                        {
                            VoiceSessionLog.Note($"PEGEL an: {name}  energy {participant.AudioEnergy:0.000}");
                        }
                    }
                    else if (wasEnergy != energyOn)
                    {
                        lastEnergyOn[key] = energyOn;
                        VoiceSessionLog.Note(
                            energyOn
                                ? $"PEGEL an: {name}  energy {participant.AudioEnergy:0.000}"
                                : $"PEGEL aus: {name}");
                    }

                    bool now = participant.SpeechDetected;
                    speaking.TryGetValue(key, out bool was);
                    if (now == was) continue;

                    speaking[key] = now;

                    if (now)
                    {
                        VoiceSessionLog.Note($"REDET: {name}");
                        if (!participant.IsSelf) LogHearingFor(participant.PlayerId, force: true);
                    }
                    else
                    {
                        VoiceSessionLog.Note($"STILL: {name}");
                    }
                }
            }
        }

        private void PollHearing()
        {
            var runtime = VoiceRuntime.Instance;
            if (runtime == null) return;

            var emitters = runtime.Emitters;
            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null || string.IsNullOrEmpty(emitter.PlayerId)) continue;
                if (emitter.PlayerId.StartsWith("debug:", System.StringComparison.Ordinal)) continue;

                LogHearingFor(emitter.PlayerId, force: false);
            }
        }

        private void LogHearingFor(string playerId, bool force)
        {
            var runtime = VoiceRuntime.Instance;
            if (runtime == null) return;

            VoiceEmitter emitter = null;
            var emitters = runtime.Emitters;
            for (int i = 0; i < emitters.Count; i++)
            {
                if (emitters[i] != null && emitters[i].PlayerId == playerId)
                {
                    emitter = emitters[i];
                    break;
                }
            }

            string name = DisplayName(false, playerId);

            if (emitter == null)
            {
                VoiceSessionLog.Alert($"PROBLEM: {name} redet, aber kein Audio-Tap auf diesem Rechner.");
                return;
            }

            var sample = emitter.Current;
            var context = emitter.LastContext;
            bool heard = !sample.Muted && sample.Volume >= HeardVolume;
            string mode = emitter.PlaybackMode;
            bool tapAlive = emitter.TapIsPlaying;
            bool sourcePlaying = emitter.TapAudioSourceIsPlaying;
            string place = emitter.UsesHeadSpeaker ? "Kopf" : "Tap";
            string verdict = heard
                ? (context.OcclusionAmount > 0.45f ? "DUMPF" : "KLAR")
                : "NICHT GEHOERT";

            lastMode.TryGetValue(playerId, out string previousMode);
            bool firstMode = !lastMode.ContainsKey(playerId);
            if (firstMode)
            {
                VoiceSessionLog.Note(
                    $"AUDIO START: {name}  {mode} am {place}  " +
                    $"Distanz {context.Distance:0.0} m  Laut {sample.Volume:0.00}  " +
                    $"Avatar {(emitter.Anchor != null ? "ja" : "NEIN")}");
            }
            else if (mode != previousMode)
            {
                if (mode == "stumm" && previousMode != "stumm")
                {
                    VoiceSessionLog.Alert(
                        $"AUDIO STIRBT: {name}  war {previousMode} am {place}  " +
                        $"jetzt stumm  Distanz {context.Distance:0.0} m  Laut {sample.Volume:0.00}  " +
                        $"Wand {context.OcclusionAmount:0.00}  " +
                        $"Avatar {(emitter.Anchor != null ? "ja" : "NEIN")}  " +
                        $"Tap {(tapAlive ? "lebt" : "tot")}  " +
                        $"AudioSource {(sourcePlaying ? "spielt" : "PAUSIERT")}");
                    LogSnapshot("bei AUDIO STIRBT");
                }
                else if (previousMode == "stumm" && mode != "stumm")
                {
                    VoiceSessionLog.Note(
                        $"AUDIO LEBT: {name}  jetzt {mode} am {place}  " +
                        $"Distanz {context.Distance:0.0} m  Laut {sample.Volume:0.00}");
                }
                else
                {
                    VoiceSessionLog.Note(
                        $"AUDIO WECHSEL: {name}  {previousMode} -> {mode} am {place}  " +
                        $"Distanz {context.Distance:0.0} m  Laut {sample.Volume:0.00}  " +
                        $"Avatar {(emitter.Anchor != null ? "ja" : "NEIN")}");
                }
            }

            lastMode[playerId] = mode;

            lastTapAlive.TryGetValue(playerId, out bool wasTapAlive);
            if (lastTapAlive.ContainsKey(playerId) && wasTapAlive != tapAlive)
            {
                if (tapAlive)
                {
                    VoiceSessionLog.Note($"TAP LEBT: {name}  Wiedergabe {mode} am {place}");
                }
                else
                {
                    VoiceSessionLog.Alert(
                        $"TAP STIRBT: {name}  Wiedergabe war {mode} am {place}  " +
                        $"Laut {sample.Volume:0.00}  Distanz {context.Distance:0.0} m  " +
                        $"AudioSource {(sourcePlaying ? "spielt noch" : "PAUSIERT (Vivox selbst)")}");
                }
            }

            lastTapAlive[playerId] = tapAlive;

            lastTapSourcePlaying.TryGetValue(playerId, out bool wasSourcePlaying);
            if (lastTapSourcePlaying.ContainsKey(playerId) && wasSourcePlaying != sourcePlaying)
            {
                if (sourcePlaying)
                {
                    VoiceSessionLog.Note($"VIVOX-PAUSE vorbei: {name}  AudioSource spielt wieder.");
                }
                else
                {
                    VoiceSessionLog.Alert(
                        $"VIVOX-PAUSE: {name}  Vivox hat die AudioSource selbst pausiert - " +
                        "ueber 400 ms kamen keine neuen Netzwerk-Audiodaten an. Das ist NICHT " +
                        "unsere Pipeline: Netzwerk-Aussetzer/Jitter, oder (im Multiplayer Play " +
                        "Mode mit zwei Editor-Instanzen auf einem Rechner) zu wenig CPU-Zeit " +
                        "fuer den Audio-Thread. Mit zwei echten Builds/Rechnern testen, um das " +
                        "auszuschliessen.");
                }
            }

            lastTapSourcePlaying[playerId] = sourcePlaying;

            bool tapObjectActive = emitter.TapObjectActive;
            lastTapObjectActive.TryGetValue(playerId, out bool wasTapObjectActive);
            if (lastTapObjectActive.ContainsKey(playerId) && wasTapObjectActive != tapObjectActive)
            {
                if (tapObjectActive)
                {
                    VoiceSessionLog.Note($"TAP-OBJEKT wieder aktiv: {name}");
                }
                else
                {
                    VoiceSessionLog.Alert(
                        $"PROBLEM: Tap-Objekt von {name} wurde deaktiviert. " +
                        "Nicht 'gerade still', sondern komplett abgeschaltet - Vivox hat das GameObject ausgeschaltet.");
                }
            }

            lastTapObjectActive[playerId] = tapObjectActive;

            string key =
                $"{verdict}|{mode}|{place}|{sample.Volume:0.00}|{context.Distance:0.0}|" +
                $"{context.OcclusionAmount:0.00}|{(emitter.Anchor != null ? "avatar" : "kein-avatar")}|" +
                $"{(tapAlive ? "tap" : "kein-tap")}|{(sourcePlaying ? "spielt" : "pausiert")}";

            lastHearKey.TryGetValue(playerId, out string previous);
            lastHearLog.TryGetValue(playerId, out float lastTime);
            bool due = Time.unscaledTime - lastTime >= HearSampleInterval;

            if (!force && key == previous && !due) return;

            lastHearKey[playerId] = key;
            lastHearLog[playerId] = Time.unscaledTime;

            VoiceSessionLog.Note(
                $"GEHOERT: ich <- {name}  {verdict}  " +
                $"Distanz {context.Distance:0.0} m  Laut {sample.Volume:0.00}  " +
                $"Wand {context.OcclusionAmount:0.00}  " +
                $"Avatar {(emitter.Anchor != null ? "ja" : "NEIN")}  " +
                $"{mode} am {place}  " +
                $"Tap {(tapAlive ? "lebt" : "tot")}");

            if (emitter.Anchor == null)
            {
                VoiceSessionLog.Alert($"PROBLEM: {name} hat noch keinen Avatar. Stimme liegt nicht am Kopf.");
            }
        }

        private void PollMachine()
        {
            int listeners = CountEnabledListeners();
            if (listeners != lastListenerCount)
            {
                lastListenerCount = listeners;
                if (listeners != 1)
                {
                    VoiceSessionLog.Alert(
                        $"PROBLEM: {listeners} AudioListener aktiv. Unity nutzt nur einen - " +
                        "oft das falsche Ohr, dann einseitig stumm.");
                }
                else
                {
                    VoiceSessionLog.Note("OHR: genau 1 AudioListener (" + ListenerPlace() + ")");
                }
            }

            float master = AudioListener.volume;
            if (lastMasterVolume < 0f || Mathf.Abs(master - lastMasterVolume) > 0.01f)
            {
                lastMasterVolume = master;
                if (master < 0.05f)
                {
                    VoiceSessionLog.Alert("PROBLEM: Unity-Masterlautstaerke ist " + master.ToString("0.00") + " - alles stumm.");
                }
                else
                {
                    VoiceSessionLog.Note("STAND Masterlautstaerke " + master.ToString("0.00"));
                }
            }

            float heard = CoopVoice.HeardVoiceVolume;
            if (lastHeardVolume < 0f || Mathf.Abs(heard - lastHeardVolume) > 0.01f)
            {
                lastHeardVolume = heard;
                if (heard < 0.05f)
                {
                    VoiceSessionLog.Alert("PROBLEM: Gehoerte-Stimmen-Regler ist " + heard.ToString("0.00") + " - fremde Stimmen aus.");
                }
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!VoiceSessionLog.IsRecording) return;
            if (focused == lastFocused) return;
            lastFocused = focused;
            VoiceSessionLog.Note(focused
                ? "FOKUS wieder da. Wenn Stimme jetzt tot ist: Windows hat oft das Geraet gewechselt."
                : "FOKUS weg (Alt-Tab). Vivox kann Mikrofon und Ausgabe kappen.");
            if (focused) LogSnapshot("nach Fokus");
        }

        private void OnApplicationPause(bool paused)
        {
            if (!VoiceSessionLog.IsRecording) return;
            VoiceSessionLog.Note(paused ? "PAUSE an" : "PAUSE aus");
        }

        private static void LogSnapshot(string when)
        {
            var profile = CoopSettings.Instance != null ? CoopSettings.Instance.VoiceProfile : null;
            string range = profile != null ? profile.MaxHearingDistance.ToString("0") + " m" : "?";
            var local = PlayerRegistry.LocalPlayer;
            string pos = local != null
                ? $"x{local.transform.position.x:0.0} y{local.transform.position.y:0.0} z{local.transform.position.z:0.0}"
                : "kein Avatar";

            VoiceSessionLog.Note(
                "STAND " + when +
                ": Listener " + CountEnabledListeners() + " (" + ListenerPlace() + ")" +
                "  Master " + AudioListener.volume.ToString("0.00") +
                "  Gehoert-Regler " + CoopVoice.HeardVoiceVolume.ToString("0.00") +
                "  Mic " + (CoopVoice.MicrophoneMuted ? "STUMM" : "an") +
                "  Fokus " + (Application.isFocused ? "ja" : "nein") +
                "  Profil " + range +
                "  In='" + CoopVoice.ActiveInputDeviceName + "'" +
                "  Out='" + CoopVoice.ActiveOutputDeviceName + "'" +
                "  Pos " + pos);
        }

        private static int CountEnabledListeners()
        {
#if UNITY_6000_5_OR_NEWER
            var found = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#else
            var found = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].isActiveAndEnabled) count++;
            }

            return count;
        }

        private static string ListenerPlace()
        {
            var local = PlayerRegistry.LocalPlayer;
            if (local == null) return "Lobby";
            var anchor = local.VoiceAnchor;
            if (anchor != null) return "Kopf";
            return "Avatar";
        }

        private static string DisplayName(bool self, string playerId)
        {
            if (self) return $"ich ({Coop.LocalPlayerName})";

            if (PlayerRegistry.TryGetByUgsId(playerId, out var player) && player != null)
            {
                return player.DisplayName;
            }

            return string.IsNullOrEmpty(playerId) ? "(unbekannt)" : playerId;
        }

        private void OnGUI()
        {
            if (Time.unscaledTime > toastUntil) return;
            if (!VoiceSessionLog.IsRecording) return;

            var box = new Rect(12f, 12f, 520f, 52f);
            GUI.Box(box, GUIContent.none);
            GUI.Label(
                new Rect(20f, 16f, 504f, 44f),
                "F3 = Live-Stimme   Esc = Pause (Log-Ordner)\n" +
                VoiceSessionLog.FilePath,
                GUI.skin.label);
        }
    }
}
