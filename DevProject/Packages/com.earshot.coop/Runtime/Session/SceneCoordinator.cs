using System;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace Earshot
{
    /// <summary>
    /// Haelt Szenen zwischen Host und Clients synchron.
    /// <para>
    /// Wichtigste Aufgabe ist eine Weichenstellung gleich beim Start: Netcode entlaedt
    /// standardmaessig beim Beitritt alle Szenen des Clients und laedt stattdessen die des
    /// Hosts. In einem Projekt, in das Earshot nur hineingezogen wurde, ist das ein
    /// zerstoerischer Nebeneffekt - dauerhaft geladene Menue- oder Manager-Szenen des
    /// Nutzers verschwinden ohne Vorwarnung. Deshalb stellen wir auf additives
    /// Synchronisieren um: Clients behalten ihre Szenen, Netzwerkobjekte werden trotzdem
    /// korrekt abgeglichen.
    /// </para>
    /// </summary>
    public static class SceneCoordinator
    {
        /// <summary>
        /// Wird ausgeloest, sobald ein ueber das Netzwerk angestossener Ladevorgang bei
        /// allen Beteiligten abgeschlossen ist. Liefert den Szenennamen.
        /// </summary>
        public static event Action<string> SceneLoadCompleted;

        private static NetworkManager hooked;

        internal static void Hook(NetworkManager manager)
        {
            if (manager == null || hooked == manager) return;

            Unhook();
            hooked = manager;
            manager.OnServerStarted += OnServerStarted;
        }

        internal static void Unhook()
        {
            if (hooked == null) return;

            hooked.OnServerStarted -= OnServerStarted;

            if (hooked.SceneManager != null)
            {
                hooked.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            }

            hooked = null;
        }

        private static void OnServerStarted()
        {
            var manager = hooked;
            if (manager == null || manager.SceneManager == null) return;

            if (CoopSettings.Instance.KeepClientScenes)
            {
                manager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
                manager.SceneManager.PostSynchronizationSceneUnloading = false;

                CoopLog.Info(
                    "Szenensynchronisierung steht auf additiv. Beitretende Spieler behalten " +
                    "ihre eigenen Szenen.");
            }

            manager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        private static void OnLoadEventCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            System.Collections.Generic.List<ulong> clientsCompleted,
            System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                CoopLog.Warn(
                    $"Szene '{sceneName}': {clientsTimedOut.Count} Spieler haben nicht rechtzeitig " +
                    "geladen. Bei grossen Szenen auf langsamen Rechnern kann das vorkommen.");
            }

            CoopLog.Info($"Szene '{sceneName}' ist bei allen Spielern geladen.");
            SceneLoadCompleted?.Invoke(sceneName);
        }

        /// <summary>
        /// Laedt eine Szene fuer alle Spieler. Darf nur vom Host aufgerufen werden.
        /// <para>
        /// Die Szene muss in den Build Settings eingetragen sein, sonst schlaegt der
        /// Vorgang fehl - das ist mit Abstand die haeufigste Ursache, wenn nichts passiert.
        /// </para>
        /// </summary>
        /// <returns>Wahr, wenn der Ladevorgang gestartet wurde.</returns>
        public static bool LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var manager = NetworkManager.Singleton;

            if (manager == null || !manager.IsListening)
            {
                CoopLog.Warn($"Szene '{sceneName}' kann nicht geladen werden: Es laeuft keine Sitzung.");
                return false;
            }

            if (!manager.IsServer)
            {
                CoopLog.Warn(
                    $"Nur der Host darf Szenen laden. Der Aufruf fuer '{sceneName}' wurde ignoriert.");
                return false;
            }

            var status = manager.SceneManager.LoadScene(sceneName, mode);

            if (status != SceneEventProgressStatus.Started)
            {
                CoopLog.Error(
                    $"Szene '{sceneName}' konnte nicht geladen werden: {status}. " +
                    "Haeufigste Ursache: Die Szene fehlt in den Build Settings.");
                return false;
            }

            return true;
        }
    }
}
