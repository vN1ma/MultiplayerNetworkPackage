using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Die oeffentliche Schnittstelle von Earshot. Fuer ein normales Koop-Spiel ist das
    /// alles, was man kennen muss.
    /// <code>
    /// string code = await Coop.HostAsync();   // Sitzung eroeffnen
    /// await Coop.JoinAsync("ABC123");         // Sitzung betreten
    /// await Coop.LeaveAsync();                // Sitzung verlassen
    /// </code>
    /// <para>
    /// Die Voice-Schicht taucht hier bewusst nicht auf. Sie haengt sich stattdessen an
    /// <see cref="StateChanged"/>, wodurch Netzwerk und Sprache unabhaengig voneinander
    /// bleiben und man eines von beidem austauschen kann, ohne das andere anzufassen.
    /// </para>
    /// </summary>
    public static class Coop
    {
        private static readonly SessionController session =
            new SessionController(new RelayTransportProvider());

        private static CoopState state = CoopState.Offline;
        private static Task pendingOperation;
        private static bool localSession;

        private const ushort LocalPort = 7777;
        private const string LocalSessionId = "local-playtest";
        private const string LocalJoinCode = "LOCAL";

        /// <summary>Aktueller Verbindungszustand.</summary>
        public static CoopState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                state = value;
                CoopLog.Info($"Zustand: {value}");
                StateChanged?.Invoke(value);
            }
        }

        /// <summary>Wird bei jeder Zustandsaenderung ausgeloest.</summary>
        public static event Action<CoopState> StateChanged;

        /// <summary>Ein Spieler ist der Welt beigetreten.</summary>
        public static event Action<CoopPlayer> PlayerJoined
        {
            add => PlayerRegistry.PlayerAdded += value;
            remove => PlayerRegistry.PlayerAdded -= value;
        }

        /// <summary>Ein Spieler hat die Welt verlassen.</summary>
        public static event Action<CoopPlayer> PlayerLeft
        {
            add => PlayerRegistry.PlayerRemoved += value;
            remove => PlayerRegistry.PlayerRemoved -= value;
        }

        /// <summary>
        /// Der Code, den Mitspieler zum Beitreten brauchen. Leer, solange keine
        /// Sitzung laeuft. Bei einem lokalen Test steht hier LOCAL.
        /// </summary>
        public static string JoinCode => localSession ? LocalJoinCode : session.JoinCode;

        /// <summary>Eindeutige Kennung der Sitzung. Wird als Name des Voice-Kanals verwendet.</summary>
        public static string SessionId =>
            localSession ? LocalSessionId : (session.Current?.Id ?? string.Empty);

        /// <summary>
        /// Wahr, wenn die Sitzung nur auf diesem Rechner laeuft (kein Relay, kein Unity-Account).
        /// Vivox bleibt dann aus; die Teststimme in der Szene prueft Tueren und Waende.
        /// </summary>
        public static bool IsLocalSession => localSession;

        /// <summary>Wahr, wenn diese Instanz die Sitzung hostet.</summary>
        public static bool IsHost => State == CoopState.Hosting;

        /// <summary>Wahr, wenn eine Sitzung laeuft, egal ob als Host oder Client.</summary>
        public static bool IsInSession => State == CoopState.Hosting || State == CoopState.Connected;

        /// <summary>Alle Spieler in der Welt, einschliesslich des eigenen.</summary>
        public static IReadOnlyList<CoopPlayer> Players => PlayerRegistry.Players;

        /// <summary>Der eigene Avatar, oder null solange er nicht gespawnt ist.</summary>
        public static CoopPlayer LocalPlayer => PlayerRegistry.LocalPlayer;

        /// <summary>
        /// Name, unter dem der lokale Spieler bei den anderen erscheint. Vor dem Verbinden
        /// setzen; spaeter wirkt er sich erst beim naechsten Spawn aus.
        /// </summary>
        public static string LocalPlayerName { get; set; } = string.Empty;

        /// <summary>
        /// Eroeffnet eine Sitzung und macht diese Instanz zum Host.
        /// </summary>
        /// <returns>Der Join-Code fuer die Mitspieler.</returns>
        public static async Task<string> HostAsync()
        {
            if (IsInSession)
            {
                CoopLog.Warn("Es laeuft bereits eine Sitzung. HostAsync wurde ignoriert.");
                return JoinCode;
            }

            await RunExclusive(async () =>
            {
                State = CoopState.Connecting;
                await CoopServices.EnsureReadyAsync();

                AttachNetworkCallbacks();
                await session.CreateAsync(CoopSettings.Instance);

                State = CoopState.Hosting;
            });

            return JoinCode;
        }

        /// <summary>
        /// Startet eine Sitzung nur auf diesem Rechner. Kein Unity-Account, kein Relay.
        /// Zum Ausprobieren von Bewegung, Tuer und Proximity mit der Teststimme.
        /// Eine zweite Instanz auf demselben PC tritt mit <see cref="JoinLocalAsync"/> bei.
        /// </summary>
        public static Task HostLocalAsync()
        {
            if (IsInSession)
            {
                CoopLog.Warn("Es laeuft bereits eine Sitzung. HostLocalAsync wurde ignoriert.");
                return Task.CompletedTask;
            }

            return RunExclusive(() =>
            {
                State = CoopState.Connecting;
                localSession = true;
                AttachNetworkCallbacks();
                ConfigureLocalTransport("127.0.0.1", listenOnAllAddresses: true);

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.StartHost())
                {
                    localSession = false;
                    throw new InvalidOperationException(
                        "Lokaler Host konnte nicht starten. Liegt ein NetworkManager in der Szene?");
                }

                CoopLog.Info(
                    "Lokale Sitzung laeuft. Zweite Instanz: Button 'Lokal beitreten'. " +
                    "Internet-Host braucht weiterhin ein verknuepftes Unity-Projekt.");
                State = CoopState.Hosting;
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Tritt einer lokalen Sitzung auf diesem Rechner bei. Standard ist 127.0.0.1.
        /// </summary>
        public static Task JoinLocalAsync(string address = "127.0.0.1")
        {
            if (IsInSession)
            {
                CoopLog.Warn("Es laeuft bereits eine Sitzung. JoinLocalAsync wurde ignoriert.");
                return Task.CompletedTask;
            }

            return RunExclusive(() =>
            {
                State = CoopState.Connecting;
                localSession = true;
                AttachNetworkCallbacks();
                ConfigureLocalTransport(
                    string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim(),
                    listenOnAllAddresses: false);

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.StartClient())
                {
                    localSession = false;
                    throw new InvalidOperationException(
                        "Lokaler Client konnte nicht starten.");
                }

                CoopLog.Info("Lokaler Client startet.");
                State = CoopState.Connected;
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Betritt eine bestehende Sitzung. Gross- und Kleinschreibung sowie Leerzeichen
        /// im Code spielen keine Rolle.
        /// </summary>
        public static async Task JoinAsync(string joinCode)
        {
            if (IsInSession)
            {
                CoopLog.Warn("Es laeuft bereits eine Sitzung. JoinAsync wurde ignoriert.");
                return;
            }

            await RunExclusive(async () =>
            {
                State = CoopState.Connecting;
                await CoopServices.EnsureReadyAsync();

                AttachNetworkCallbacks();
                await session.JoinByCodeAsync(joinCode);

                State = CoopState.Connected;
            });
        }

        /// <summary>
        /// Verlaesst die laufende Sitzung. Beim Host beendet das die Runde fuer alle.
        /// </summary>
        public static async Task LeaveAsync()
        {
            if (State == CoopState.Offline) return;

            await RunExclusive(async () =>
            {
                State = CoopState.Disconnecting;

                if (localSession)
                {
                    ShutdownLocal();
                }
                else
                {
                    await session.LeaveAsync();
                }

                DetachNetworkCallbacks();
                PlayerRegistry.Clear();
                localSession = false;

                State = CoopState.Offline;
            });
        }

        /// <summary>
        /// Fuehrt Verbindungsvorgaenge nacheinander aus. Ohne diese Sperre koennte ein
        /// hektischer Doppelklick auf "Host" zwei Sitzungen gleichzeitig anstossen.
        /// </summary>
        private static async Task RunExclusive(Func<Task> operation)
        {
            while (pendingOperation != null && !pendingOperation.IsCompleted)
            {
                await pendingOperation;
            }

            Task task;
            try
            {
                task = operation();
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Verbindungsvorgang fehlgeschlagen", ex);
                await SafeResetAsync();
                throw;
            }

            pendingOperation = task;

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Verbindungsvorgang fehlgeschlagen", ex);
                await SafeResetAsync();
                throw;
            }
            finally
            {
                pendingOperation = null;
            }
        }

        private static async Task SafeResetAsync()
        {
            try
            {
                if (localSession) ShutdownLocal();
                else await session.LeaveAsync();
            }
            catch
            {
                // Beim Aufraeumen nach einem Fehler ist ein zweiter Fehler nicht hilfreich.
            }

            DetachNetworkCallbacks();
            PlayerRegistry.Clear();
            localSession = false;
            State = CoopState.Offline;
        }

        private static void ConfigureLocalTransport(string address, bool listenOnAllAddresses)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                throw new InvalidOperationException(
                    "Kein NetworkManager in der Szene. Tools > Earshot > Testraum bauen.");
            }

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                throw new InvalidOperationException(
                    "Am NetworkManager fehlt UnityTransport.");
            }

            string listen = listenOnAllAddresses ? "0.0.0.0" : address;
            transport.SetConnectionData(address, LocalPort, listen);
        }

        private static void ShutdownLocal()
        {
            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                manager.Shutdown();
            }
        }

        private static void AttachNetworkCallbacks()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                CoopLog.Error(
                    "Kein NetworkManager in der Szene. Ohne ihn kann keine Verbindung " +
                    "aufgebaut werden. Ueber Tools > Earshot > Setup laesst sich einer anlegen.");
                return;
            }

            SceneCoordinator.Hook(manager);

            manager.OnClientStopped -= OnClientStopped;
            manager.OnClientStopped += OnClientStopped;
        }

        private static void DetachNetworkCallbacks()
        {
            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.OnClientStopped -= OnClientStopped;
            }

            SceneCoordinator.Unhook();
        }

        /// <summary>
        /// Faengt Verbindungsabbrueche ab, die nicht von uns ausgingen - etwa weil der Host
        /// das Spiel geschlossen hat oder das WLAN weg war.
        /// </summary>
        private static void OnClientStopped(bool wasHost)
        {
            if (State == CoopState.Disconnecting || State == CoopState.Offline) return;

            CoopLog.Warn(wasHost
                ? "Die Sitzung wurde beendet."
                : "Die Verbindung zum Host ist abgebrochen.");

            _ = SafeResetAsync();
        }
    }
}
