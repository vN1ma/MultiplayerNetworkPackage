using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Die oeffentliche Schnittstelle von Earshot Voice.
    /// <para>
    /// Diese Schicht haengt sich an KEINE Sitzung von selbst an - anders als ein volles
    /// Multiplayer-Framework kennt dieses Paket keinen eigenen Sitzungsbegriff. Das eigene
    /// Spiel ruft <see cref="ConnectAsync"/> auf, sobald alle im selben Match sind, und
    /// <see cref="DisconnectAsync"/> beim Verlassen.
    /// </para>
    /// </summary>
    public static class EarshotVoice
    {
        private static Func<IVoiceBackend> backendFactory = () => new VivoxVoiceBackend();
        private static IVoiceBackend active;
        private static Task pendingOperation;
        private static bool desiredMuteState;
        private static string pendingInputDevice;
        private static string pendingOutputDevice;
        private static Transform pendingListenerOverride;
        private static float heardVoiceVolume = 1f;

        public static bool IsConnected => active != null && active.IsConnected;

        /// <summary>
        /// Unity Authentication PlayerId des lokalen Spielers, sobald er angemeldet ist.
        /// Genau diese Zeichenkette muss beim eigenen <see cref="IProximityVoicePlayer"/>
        /// als <see cref="IProximityVoicePlayer.PlayerId"/> ankommen.
        /// </summary>
        public static string LocalPlayerId
        {
            get
            {
                try
                {
                    return AuthenticationService.Instance != null &&
                           AuthenticationService.Instance.IsSignedIn
                        ? AuthenticationService.Instance.PlayerId
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

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

        public static void ToggleMicrophone() => MicrophoneMuted = !MicrophoneMuted;

        public static float GameVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }

        /// <summary>Nur fremde Stimmen, unabhaengig von der Spiel-Lautstaerke.</summary>
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

        public static async Task SetInputDeviceAsync(string deviceName)
        {
            if (IsUnusableAudioDevice(deviceName))
            {
                VoiceSessionLog.Note("GERAET Mikrofon ignoriert (virtuell/kein Geraet): " + deviceName);
                return;
            }

            pendingInputDevice = deviceName;
            if (string.IsNullOrEmpty(deviceName)) return;
            if (!VivoxReady) return;

            try
            {
                var device = FindInputDevice(deviceName);
                if (device == null) return;

                string before = ActiveInputDeviceName;
                if (string.Equals(before, device.DeviceName, StringComparison.Ordinal)) return;

                await VivoxService.Instance.SetActiveInputDeviceAsync(device);
                VoiceSessionLog.Note("GERAET Mikrofon gesetzt: '" + device.DeviceName + "'");
            }
            catch (Exception ex)
            {
                VoiceSessionLog.Note("PROBLEM: Mikrofon wechseln fehlgeschlagen: " + ex.Message);
                EarshotVoiceLog.Exception("Mikrofon wechseln fehlgeschlagen", ex);
            }
        }

        public static async Task SetOutputDeviceAsync(string deviceName)
        {
            if (IsUnusableAudioDevice(deviceName))
            {
                VoiceSessionLog.Note("GERAET Lautsprecher ignoriert (virtuell/kein Geraet): " + deviceName);
                return;
            }

            pendingOutputDevice = deviceName;
            if (string.IsNullOrEmpty(deviceName)) return;
            if (!VivoxReady) return;

            try
            {
                var device = FindOutputDevice(deviceName);
                if (device == null) return;

                string before = ActiveOutputDeviceName;
                if (string.Equals(before, device.DeviceName, StringComparison.Ordinal)) return;

                await VivoxService.Instance.SetActiveOutputDeviceAsync(device);
                VoiceSessionLog.Note("GERAET Lautsprecher gesetzt: '" + device.DeviceName + "'");
            }
            catch (Exception ex)
            {
                VoiceSessionLog.Note("PROBLEM: Lautsprecher wechseln fehlgeschlagen: " + ex.Message);
                EarshotVoiceLog.Exception("Lautsprecher wechseln fehlgeschlagen", ex);
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

        /// <summary>
        /// Hoerposition abweichend vom eigenen Kopf, z.B. Zuschauerkamera.
        /// Null = Kopf des lokalen Spielers, sonst aktiver AudioListener.
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

        public static void UseBackend(Func<IVoiceBackend> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            backendFactory = factory;
        }

        /// <summary>
        /// Meldet den lokalen Spieler bei Unity Services an, falls das Host-Spiel das
        /// noch nicht getan hat. Bereits angemeldete Spieler bleiben unangetastet.
        /// Vivox braucht diese Anmeldung zwingend.
        /// </summary>
        public static async Task EnsureSignedInAsync(bool signInAnonymouslyIfNeeded = true)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn) return;

            if (!signInAnonymouslyIfNeeded)
            {
                throw new InvalidOperationException(
                    "Unity Authentication ist nicht angemeldet. Vor EarshotVoice.ConnectAsync " +
                    "selbst anmelden, oder signInAnonymouslyIfNeeded auf true lassen.");
            }

            // Das eigene Spiel loggt oft parallel ein. Zwei SignIn gleichzeitig werfen
            // "already signing in" - dann warten wir, statt ein zweites Mal anzustossen.
            try
            {
                await auth.SignInAnonymouslyAsync();
                EarshotVoiceLog.Info("Anonym bei Unity Authentication angemeldet. PlayerId=" + LocalPlayerId);
            }
            catch (Exception)
            {
                int waitedMs = 0;
                while (!auth.IsSignedIn && waitedMs < 15000)
                {
                    await Task.Delay(50);
                    waitedMs += 50;
                }

                if (auth.IsSignedIn) return;
                throw;
            }
        }

        /// <summary>
        /// Tritt dem gemeinsamen Sprachkanal bei. <paramref name="channelName"/> muss bei
        /// allen Spielern derselben Runde identisch sein (Lobby-ID, Room-Name, Match-ID).
        /// </summary>
        public static Task ConnectAsync(string channelName, string displayName = null)
        {
            return ConnectAsync(channelName, displayName, signInAnonymouslyIfNeeded: true);
        }

        public static Task ConnectAsync(string channelName, string displayName, bool signInAnonymouslyIfNeeded)
        {
            return Enqueue(() => ConnectInternalAsync(channelName, displayName, signInAnonymouslyIfNeeded));
        }

        public static Task DisconnectAsync()
        {
            return Enqueue(DisconnectInternalAsync);
        }

        private static async Task Enqueue(Func<Task> operation)
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
                EarshotVoiceLog.Exception(
                    "Sprachverbindung fehlgeschlagen. Das Spiel laeuft ohne Stimme weiter", ex);
                VoiceSessionLog.Note("PROBLEM: Sprachverbindung fehlgeschlagen: " + ex.Message);
            }
            finally
            {
                pendingOperation = null;
            }
        }

        private static async Task ConnectInternalAsync(
            string channelName,
            string displayName,
            bool signInAnonymouslyIfNeeded)
        {
            var settings = EarshotVoiceSettings.Instance;
            if (!settings.VoiceEnabled)
            {
                EarshotVoiceLog.Info("Voice ist in den Einstellungen deaktiviert.");
                return;
            }

            if (string.IsNullOrWhiteSpace(channelName))
            {
                EarshotVoiceLog.Warn("Kein Kanalname. Alle Spieler brauchen denselben Namen fuer denselben Match.");
                return;
            }

            if (active != null) return;

            await EnsureSignedInAsync(signInAnonymouslyIfNeeded);

            VoiceSessionLog.BeginSession(channelName, displayName);

            active = backendFactory();

            var runtime = VoiceRuntime.EnsureExists();
            runtime.ListenerOverride = pendingListenerOverride;
            runtime.AttachBackend(active);

            string name = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
            await active.ConnectAsync(channelName.Trim(), name);

            active.MicrophoneMuted = desiredMuteState || settings.MicrophoneMutedOnJoin;

            if (!string.IsNullOrEmpty(pendingInputDevice))
            {
                await SetInputDeviceAsync(pendingInputDevice);
            }

            if (!string.IsNullOrEmpty(pendingOutputDevice))
            {
                await SetOutputDeviceAsync(pendingOutputDevice);
            }

            EarshotVoiceLog.Info(
                "Earshot Voice verbunden. Kanal='" + channelName +
                "'  lokale PlayerId='" + LocalPlayerId + "'");
        }

        private static async Task DisconnectInternalAsync()
        {
            if (active == null) return;

            var backend = active;
            active = null;

            VoiceRuntime.Instance?.DetachBackend();
            VoiceSessionLog.EndSession("Kanal verlassen.");

            await backend.DisconnectAsync();
        }

        private static VivoxInputDevice FindInputDevice(string deviceName)
        {
            return FindByName(VivoxService.Instance.AvailableInputDevices, deviceName, d => d.DeviceName);
        }

        private static VivoxOutputDevice FindOutputDevice(string deviceName)
        {
            return FindByName(VivoxService.Instance.AvailableOutputDevices, deviceName, d => d.DeviceName);
        }

        private static T FindByName<T>(
            System.Collections.Generic.IReadOnlyList<T> list,
            string wanted,
            Func<T, string> nameOf)
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

            return null;
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
    }
}
