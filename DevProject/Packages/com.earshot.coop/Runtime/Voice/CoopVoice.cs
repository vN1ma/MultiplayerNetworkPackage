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
                desiredMuteState = value;
                if (active != null) active.MicrophoneMuted = value;
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

        /// <summary>Vivox-Mikrofonlautstaerke, -50 bis 50. Wirkt erst nach Internet-Join.</summary>
        public static int MicrophoneVolume
        {
            get => VivoxReady ? VivoxService.Instance.InputDeviceVolume : 0;
            set
            {
                if (!VivoxReady) return;
                VivoxService.Instance.SetInputDeviceVolume(Mathf.Clamp(value, -50, 50));
            }
        }

        /// <summary>Vivox-Ausgabelautstaerke, -50 bis 50. Wirkt erst nach Internet-Join.</summary>
        public static int VoiceOutputVolume
        {
            get => VivoxReady ? VivoxService.Instance.OutputDeviceVolume : 0;
            set
            {
                if (!VivoxReady) return;
                VivoxService.Instance.SetOutputDeviceVolume(Mathf.Clamp(value, -50, 50));
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
            pendingInputDevice = deviceName;
            if (!VivoxReady || string.IsNullOrEmpty(deviceName)) return;

            var list = VivoxService.Instance.AvailableInputDevices;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].DeviceName != deviceName) continue;
                await VivoxService.Instance.SetActiveInputDeviceAsync(list[i]);
                return;
            }
        }

        /// <summary>Lautsprecher waehlen. Vor Vivox gemerkt, danach sofort gesetzt.</summary>
        public static async Task SetOutputDeviceAsync(string deviceName)
        {
            pendingOutputDevice = deviceName;
            if (!VivoxReady || string.IsNullOrEmpty(deviceName)) return;

            var list = VivoxService.Instance.AvailableOutputDevices;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].DeviceName != deviceName) continue;
                await VivoxService.Instance.SetActiveOutputDeviceAsync(list[i]);
                return;
            }
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

            if (!string.IsNullOrEmpty(pendingInputDevice))
            {
                await SetInputDeviceAsync(pendingInputDevice);
            }

            if (!string.IsNullOrEmpty(pendingOutputDevice))
            {
                await SetOutputDeviceAsync(pendingOutputDevice);
            }
        }

        private static async Task DisconnectAsync()
        {
            if (active == null) return;

            var backend = active;
            active = null;

            VoiceRuntime.Instance?.DetachBackend();

            await backend.DisconnectAsync();
        }
    }
}
