using System;
using System.Threading.Tasks;
using Earshot.Voice;
using Unity.Services.Vivox;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Die oeffentliche Schnittstelle der Sprachschicht, das Gegenstueck zu <see cref="Coop"/>.
    /// <para>
    /// Fuer normalen Proximity-Chat muss hier nichts aufgerufen werden: Die Schicht haengt
    /// sich beim Programmstart selbst an <see cref="Coop.StateChanged"/> und begleitet jede
    /// Sitzung automatisch. Von aussen interessant sind vor allem die Stummschaltung und
    /// der Austausch des Sprachdienstes.
    /// </para>
    /// <code>
    /// CoopVoice.ToggleMicrophone();
    /// </code>
    /// </summary>
    public static class CoopVoice
    {
        private static Func<IVoiceBackend> backendFactory = () => new VivoxVoiceBackend();
        private static IVoiceBackend active;
        private static Task pendingOperation;
        private static bool desiredMuteState;
        private static string pendingInputDevice;
        private static string pendingOutputDevice;

        /// <summary>Wahr, wenn ein Sprachkanal aktiv ist.</summary>
        public static bool IsConnected => active != null && active.IsConnected;

        /// <summary>
        /// Eigenes Mikrofon stummschalten. Laesst sich auch setzen, bevor eine Sitzung
        /// laeuft; der Wunsch wird dann beim Beitritt uebernommen.
        /// </summary>
        public static bool MicrophoneMuted
        {
            get => active?.MicrophoneMuted ?? desiredMuteState;
            set
            {
                bool changed = desiredMuteState != value;
                desiredMuteState = value;
                if (active != null) active.MicrophoneMuted = value;
                if (changed)
                {
                    VoiceSessionLog.Note(value ? "GERAET Mikrofon: STUMM" : "GERAET Mikrofon: AN");
                }
            }
        }

        /// <summary>Kehrt die Stummschaltung um. Praktisch fuer eine Taste im Spiel.</summary>
        public static void ToggleMicrophone() => MicrophoneMuted = !MicrophoneMuted;

        /// <summary>Spiel-Lautstaerke 0 bis 1, inklusive Testton und Stimmen.</summary>
        public static float GameVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Extra-Regler nur fuer fremde Stimmen (Audio-Taps). Unabhaengig von der
        /// Spiel-Lautstaerke, damit man Mitspieler leiser machen kann ohne die Welt.
        /// </summary>
        public static float HeardVoiceVolume
        {
            get => heardVoiceVolume;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Abs(clamped - heardVoiceVolume) < 0.005f)
                {
                    heardVoiceVolume = clamped;
                    return;
                }

                heardVoiceVolume = clamped;
                VoiceSessionLog.Note(
                    "GERAET Gehoerte Stimmen: " + Mathf.RoundToInt(clamped * 100f) + " %");
            }
        }

        private static float heardVoiceVolume = 1f;

        /// <summary>Vivox-Mikrofonlautstaerke, -50 bis 50. Wirkt erst nach Internet-Join.</summary>
        public static int MicrophoneVolume
        {
            get => VivoxReady ? VivoxService.Instance.InputDeviceVolume : 0;
            set
            {
                if (!VivoxReady) return;
                int clamped = Mathf.Clamp(value, -50, 50);
                if (VivoxService.Instance.InputDeviceVolume == clamped) return;
                VivoxService.Instance.SetInputDeviceVolume(clamped);
                VoiceSessionLog.Note("GERAET Mikrofon-Pegel: " + clamped);
            }
        }

        /// <summary>Vivox-Ausgabelautstaerke, -50 bis 50. Wirkt erst nach Internet-Join.</summary>
        public static int VoiceOutputVolume
        {
            get => VivoxReady ? VivoxService.Instance.OutputDeviceVolume : 0;
            set
            {
                if (!VivoxReady) return;
                int clamped = Mathf.Clamp(value, -50, 50);
                if (VivoxService.Instance.OutputDeviceVolume == clamped) return;
                VivoxService.Instance.SetOutputDeviceVolume(clamped);
                VoiceSessionLog.Note("GERAET Vivox-Ausgabe-Pegel: " + clamped);
            }
        }

        /// <summary>Namen der Mikrofon-Geraete. Vor Vivox die von Unity, danach die von Vivox.</summary>
        public static string[] InputDeviceNames
        {
            get
            {
                if (VivoxReady)
                {
                    var list = VivoxService.Instance.AvailableInputDevices;
                    var names = new string[list.Count];
                    for (int i = 0; i < list.Count; i++) names[i] = list[i].DeviceName;
                    return names;
                }

                return Microphone.devices ?? Array.Empty<string>();
            }
        }

        /// <summary>Namen der Lautsprecher. Leer, bis Vivox laeuft.</summary>
        public static string[] OutputDeviceNames
        {
            get
            {
                if (!VivoxReady) return Array.Empty<string>();

                var list = VivoxService.Instance.AvailableOutputDevices;
                var names = new string[list.Count];
                for (int i = 0; i < list.Count; i++) names[i] = list[i].DeviceName;
                return names;
            }
        }

        public static string ActiveInputDeviceName =>
            VivoxReady
                ? VivoxService.Instance.EffectiveInputDevice?.DeviceName ?? string.Empty
                : pendingInputDevice ?? string.Empty;

        public static string ActiveOutputDeviceName =>
            VivoxReady
                ? VivoxService.Instance.EffectiveOutputDevice?.DeviceName ?? string.Empty
                : pendingOutputDevice ?? string.Empty;

        /// <summary>Mikrofon waehlen. Vor Vivox gemerkt, danach sofort gesetzt.</summary>
        public static async Task SetInputDeviceAsync(string deviceName)
        {
            if (IsUnusableAudioDevice(deviceName))
            {
                VoiceSessionLog.Note("GERAET Mikrofon ignoriert (virtuell/kein Geraet): " + deviceName);
                return;
            }
            pendingInputDevice = deviceName;
            if (string.IsNullOrEmpty(deviceName))
            {
                VoiceSessionLog.Note("GERAET Mikrofon: leerer Name, ignoriert.");
                return;
            }

            if (!VivoxReady)
            {
                VoiceSessionLog.Note("GERAET Mikrofon gemerkt (Vivox noch nicht bereit): " + deviceName);
                return;
            }

            try
            {
                var device = FindInputDevice(deviceName);
                if (device == null)
                {
                    VoiceSessionLog.Note(
                        "PROBLEM: Mikrofon '" + deviceName + "' nicht in der Vivox-Liste. " +
                        "Verfuegbar: " + JoinNames(InputDeviceNames));
                    return;
                }

                string before = ActiveInputDeviceName;

                // Ohne diese Pruefung wuerde jeder Connect das Geraet erneut setzen, auch
                // wenn es schon aktiv ist. Ein Geraetewechsel bei Vivox startet die native
                // Audiositzung neu - mitten in einem laufenden Gespraech reisst das kurz
                // alle Tap-Wiedergaben ab. Nicht noetig, wenn sich nichts geaendert hat.
                if (string.Equals(before, device.DeviceName, StringComparison.Ordinal))
                {
                    return;
                }

                await VivoxService.Instance.SetActiveInputDeviceAsync(device);
                string after = VivoxService.Instance.EffectiveInputDevice?.DeviceName ?? string.Empty;
                VoiceSessionLog.Note(
                    "GERAET Mikrofon gesetzt: '" + device.DeviceName + "'" +
                    "  vorher='" + before + "'  effektiv='" + after + "'");
            }
            catch (Exception ex)
            {
                VoiceSessionLog.Note("PROBLEM: Mikrofon wechseln fehlgeschlagen: " + ex.Message);
                CoopLog.Exception("Mikrofon wechseln fehlgeschlagen", ex);
            }
        }

        /// <summary>Lautsprecher waehlen. Vor Vivox gemerkt, danach sofort gesetzt.</summary>
        public static async Task SetOutputDeviceAsync(string deviceName)
        {
            if (IsUnusableAudioDevice(deviceName))
            {
                VoiceSessionLog.Note("GERAET Lautsprecher ignoriert (virtuell/kein Geraet): " + deviceName);
                return;
            }
            pendingOutputDevice = deviceName;
            if (string.IsNullOrEmpty(deviceName))
            {
                VoiceSessionLog.Note("GERAET Lautsprecher: leerer Name, ignoriert.");
                return;
            }

            if (!VivoxReady)
            {
                VoiceSessionLog.Note("GERAET Lautsprecher gemerkt (Vivox noch nicht bereit): " + deviceName);
                return;
            }

            try
            {
                var device = FindOutputDevice(deviceName);
                if (device == null)
                {
                    VoiceSessionLog.Note(
                        "PROBLEM: Lautsprecher '" + deviceName + "' nicht in der Vivox-Liste. " +
                        "Verfuegbar: " + JoinNames(OutputDeviceNames));
                    return;
                }

                string before = ActiveOutputDeviceName;

                // Gleicher Grund wie beim Mikrofon: ein Ausgabegeraet-Wechsel bei Vivox
                // reisst kurz alle Tap-Wiedergaben ab. Nicht wiederholen, wenn es schon
                // das aktive Geraet ist.
                if (string.Equals(before, device.DeviceName, StringComparison.Ordinal))
                {
                    return;
                }

                await VivoxService.Instance.SetActiveOutputDeviceAsync(device);
                string after = VivoxService.Instance.EffectiveOutputDevice?.DeviceName ?? string.Empty;
                VoiceSessionLog.Note(
                    "GERAET Lautsprecher gesetzt: '" + device.DeviceName + "'" +
                    "  vorher='" + before + "'  effektiv='" + after + "'" +
                    "  Hinweis: Gehoerte Stimmen laufen ueber Unity/Windows-Standard, nicht nur ueber Vivox.");
            }
            catch (Exception ex)
            {
                VoiceSessionLog.Note("PROBLEM: Lautsprecher wechseln fehlgeschlagen: " + ex.Message);
                CoopLog.Exception("Lautsprecher wechseln fehlgeschlagen", ex);
            }
        }

        public static bool IsUnusableAudioDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (string.Equals(name, "No Device", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.IndexOf("Voicemod", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("Voice Changer", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("Steam Streaming", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("Virtual Desktop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("Oculus Virtual", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static VivoxInputDevice FindInputDevice(string deviceName)
        {
            var list = VivoxService.Instance.AvailableInputDevices;
            return FindByName(list, deviceName, d => d.DeviceName);
        }

        private static VivoxOutputDevice FindOutputDevice(string deviceName)
        {
            var list = VivoxService.Instance.AvailableOutputDevices;
            return FindByName(list, deviceName, d => d.DeviceName);
        }

        private static T FindByName<T>(System.Collections.Generic.IReadOnlyList<T> list, string wanted, Func<T, string> nameOf)
            where T : class
        {
            if (list == null || string.IsNullOrEmpty(wanted)) return null;

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(nameOf(list[i]), wanted, StringComparison.Ordinal)) return list[i];
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(nameOf(list[i]), wanted, StringComparison.OrdinalIgnoreCase)) return list[i];
            }

            string needle = wanted.Trim();
            T contains = null;
            int hits = 0;
            for (int i = 0; i < list.Count; i++)
            {
                string name = nameOf(list[i]);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    needle.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    contains = list[i];
                    hits++;
                }
            }

            return hits == 1 ? contains : null;
        }

        private static string JoinNames(string[] names)
        {
            if (names == null || names.Length == 0) return "(keine)";
            return string.Join(" | ", names);
        }

        private static bool VivoxReady
        {
            get
            {
                try
                {
                    return VivoxService.Instance != null &&
                           VivoxService.Instance.InitializationState == VivoxInitializationState.Initialized;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Verlegt den Ort, an dem zugehoert wird, weg vom AudioListener.
        /// <para>
        /// Normalerweise hoert man dort, wo die eigene Kamera steht - das ist beim
        /// Zuschauen nach dem Tod meist genau richtig, weil die Kamera dem Mitspieler
        /// folgt. Es gibt aber Faelle, in denen das nicht passt: eine Uebersichtskamera
        /// unter der Decke, eine freie Kamera oder eine Schulterperspektive mit grossem
        /// Abstand. Dann setzt man hier den Kopf des beobachteten Spielers ein und hoert
        /// die Welt aus dessen Position.
        /// </para>
        /// <code>
        /// CoopVoice.ListenerOverride = beobachteterSpieler.VoiceAnchor;  // zuschauen
        /// CoopVoice.ListenerOverride = null;                             // zurueck zur Kamera
        /// </code>
        /// </summary>
        public static Transform ListenerOverride
        {
            get => VoiceRuntime.Instance != null ? VoiceRuntime.Instance.ListenerOverride : pendingListenerOverride;
            set
            {
                pendingListenerOverride = value;
                if (VoiceRuntime.Instance != null) VoiceRuntime.Instance.ListenerOverride = value;
            }
        }

        private static Transform pendingListenerOverride;

        /// <summary>
        /// Ersetzt den Sprachdienst. Vor dem Start einer Sitzung aufrufen.
        /// <code>
        /// CoopVoice.UseBackend(() => new MeinEigenesBackend());
        /// </code>
        /// Das gesamte Klangverhalten - Entfernung, Waende, Tueren, Raeume - bleibt dabei
        /// unveraendert, weil es oberhalb dieser Grenze liegt.
        /// </summary>
        public static void UseBackend(Func<IVoiceBackend> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (IsConnected)
            {
                CoopLog.Warn(
                    "Das Voice-Backend wurde waehrend einer laufenden Sitzung gewechselt. " +
                    "Der Wechsel greift erst bei der naechsten Sitzung.");
            }

            backendFactory = factory;
        }

        /// <summary>
        /// Haengt die Sprachschicht an den Sitzungszustand. Laeuft beim Programmstart von
        /// selbst, damit ein Spiel ohne eine einzige Zeile Voice-Code auskommt.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            Coop.StateChanged -= OnCoopStateChanged;
            Coop.StateChanged += OnCoopStateChanged;
        }

        private static void OnCoopStateChanged(CoopState state)
        {
            switch (state)
            {
                case CoopState.Hosting:
                case CoopState.Connected:
                    Queue(ConnectAsync);
                    break;

                case CoopState.Offline:
                    Queue(DisconnectAsync);
                    break;
            }
        }

        /// <summary>
        /// Fuehrt Verbindungsvorgaenge nacheinander aus. Ein schnelles Verlassen und
        /// sofortiges Neubeitreten wuerde sonst zwei ueberlappende Vivox-Vorgaenge ausloesen.
        /// </summary>
        private static async void Queue(Func<Task> operation)
        {
            while (pendingOperation != null && !pendingOperation.IsCompleted)
            {
                try { await pendingOperation; }
                catch { /* Der ausloesende Aufruf hat den Fehler bereits gemeldet. */ }
            }

            var task = operation();
            pendingOperation = task;

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // Eine gescheiterte Sprachverbindung darf die Spielsitzung nicht mitreissen.
                // Zusammen spielen geht auch ohne Stimme.
                CoopLog.Exception(
                    "Sprachverbindung fehlgeschlagen. Die Sitzung laeuft ohne Stimme weiter", ex);
                VoiceSessionLog.Note("PROBLEM: Sprachverbindung fehlgeschlagen: " + ex.Message);
            }
            finally
            {
                pendingOperation = null;
            }
        }

        private static async Task ConnectAsync()
        {
            var settings = CoopSettings.Instance;

            if (!settings.VoiceEnabled)
            {
                CoopLog.Info("Voice ist in den Einstellungen deaktiviert.");
                return;
            }

            if (Coop.IsLocalSession)
            {
                VoiceRuntime.EnsureExists();
                CoopLog.Info(
                    "Lokale Sitzung: Vivox bleibt aus. Die Teststimme in der Szene zeigt, " +
                    "ob Tueren und Waende den Klang aendern.");
                return;
            }

            if (active != null) return;

            // Die Sitzungskennung ist bei allen Teilnehmern gleich und aendert sich pro
            // Runde. Damit ist sie der natuerliche Name fuer den Sprachkanal.
            string channel = Coop.SessionId;

            if (string.IsNullOrEmpty(channel))
            {
                CoopLog.Warn(
                    "Die Sitzung hat keine Kennung. Ohne sie laesst sich kein Sprachkanal bilden.");
                return;
            }

            active = backendFactory();

            var runtime = VoiceRuntime.EnsureExists();
            runtime.ListenerOverride = pendingListenerOverride;
            runtime.AttachBackend(active);

            string displayName = string.IsNullOrWhiteSpace(Coop.LocalPlayerName)
                ? "Player"
                : Coop.LocalPlayerName;

            await active.ConnectAsync(channel, displayName);

            // Erst nach dem Beitritt setzen: Vorher gibt es kein Eingabegeraet, das sich
            // stummschalten liesse.
            active.MicrophoneMuted = desiredMuteState || settings.MicrophoneMutedOnJoin;

            HookDeviceEvents();
            LogCurrentDevices("nach Connect");

            // Geraetelisten kommen oft erst einen Tick spaeter. Kurz warten, dann den
            // Wunsch aus dem Menue anwenden - sonst bleibt der Klick vor dem Join wirkungslos.
            await Task.Delay(250);

            if (!string.IsNullOrEmpty(pendingInputDevice))
            {
                await SetInputDeviceAsync(pendingInputDevice);
            }

            if (!string.IsNullOrEmpty(pendingOutputDevice))
            {
                await SetOutputDeviceAsync(pendingOutputDevice);
            }

            LogCurrentDevices("nach Geraetewunsch");
        }

        private static void HookDeviceEvents()
        {
            if (!VivoxReady) return;

            var vivox = VivoxService.Instance;
            vivox.AvailableInputDevicesChanged -= OnAvailableInputDevicesChanged;
            vivox.AvailableInputDevicesChanged += OnAvailableInputDevicesChanged;
            vivox.AvailableOutputDevicesChanged -= OnAvailableOutputDevicesChanged;
            vivox.AvailableOutputDevicesChanged += OnAvailableOutputDevicesChanged;
        }

        private static void UnhookDeviceEvents()
        {
            try
            {
                if (!VivoxReady) return;
                var vivox = VivoxService.Instance;
                vivox.AvailableInputDevicesChanged -= OnAvailableInputDevicesChanged;
                vivox.AvailableOutputDevicesChanged -= OnAvailableOutputDevicesChanged;
            }
            catch
            {
                // Beim Herunterfahren darf das Ereignis schon weg sein.
            }
        }

        private static void OnAvailableInputDevicesChanged()
        {
            VoiceSessionLog.Note("GERAET Mikrofon-Liste: " + JoinNames(InputDeviceNames));
            if (string.IsNullOrEmpty(pendingInputDevice)) return;
            if (string.Equals(ActiveInputDeviceName, pendingInputDevice, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = SetInputDeviceAsync(pendingInputDevice);
        }

        private static void OnAvailableOutputDevicesChanged()
        {
            VoiceSessionLog.Note("GERAET Lautsprecher-Liste: " + JoinNames(OutputDeviceNames));
            if (string.IsNullOrEmpty(pendingOutputDevice)) return;
            if (string.Equals(ActiveOutputDeviceName, pendingOutputDevice, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = SetOutputDeviceAsync(pendingOutputDevice);
        }

        private static void LogCurrentDevices(string when)
        {
            VoiceSessionLog.Note(
                "GERAET Stand " + when +
                ": Mikrofon='" + ActiveInputDeviceName + "'" +
                "  Lautsprecher='" + ActiveOutputDeviceName + "'" +
                "  Mikros=[" + JoinNames(InputDeviceNames) + "]" +
                "  Boxen=[" + JoinNames(OutputDeviceNames) + "]");
        }

        private static async Task DisconnectAsync()
        {
            if (active == null) return;

            UnhookDeviceEvents();

            var backend = active;
            active = null;

            VoiceRuntime.Instance?.DetachBackend();

            await backend.DisconnectAsync();
        }
    }
}
