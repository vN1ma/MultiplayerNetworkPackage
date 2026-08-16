using System;
using System.Collections.Generic;

namespace Earshot
{
    /// <summary>
    /// Fuehrt Buch ueber alle Spieler in der Sitzung und stellt die Verbindung zwischen
    /// Netzwerk-Identitaet und Unity-Gaming-Services-Spieler-ID her.
    /// <para>
    /// Der Grund fuer diese eigene Ebene: Netcode und Vivox kennen einander nicht. Netcode
    /// denkt in Client-IDs, Vivox in Spieler-IDs. Erst die gemeinsame UGS-ID verbindet
    /// beide Welten - und weil Spawn und Vivox-Beitritt in beliebiger Reihenfolge eintreffen
    /// koennen, braucht es eine Stelle, die beide Seiten zusammenfuehrt, sobald sie da sind.
    /// </para>
    /// </summary>
    public static class PlayerRegistry
    {
        private static readonly List<CoopPlayer> players = new List<CoopPlayer>();
        private static readonly Dictionary<string, CoopPlayer> byUgsId =
            new Dictionary<string, CoopPlayer>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ein Spielerobjekt ist erschienen. Die Identitaet kann noch fehlen.</summary>
        public static event Action<CoopPlayer> PlayerAdded;

        /// <summary>Ein Spielerobjekt ist verschwunden.</summary>
        public static event Action<CoopPlayer> PlayerRemoved;

        /// <summary>
        /// Die UGS-Spieler-ID eines Spielers ist eingetroffen. Erst ab hier laesst sich eine
        /// Stimme zuordnen, weshalb die Voice-Schicht auf genau dieses Ereignis wartet.
        /// </summary>
        public static event Action<CoopPlayer> IdentityReady;

        /// <summary>Alle bekannten Spieler, einschliesslich des lokalen.</summary>
        public static IReadOnlyList<CoopPlayer> Players => players;

        /// <summary>Der Avatar des lokalen Spielers, oder null.</summary>
        public static CoopPlayer LocalPlayer
        {
            get
            {
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null && players[i].IsLocalPlayer) return players[i];
                }
                return null;
            }
        }

        internal static void Register(CoopPlayer player)
        {
            if (player == null || players.Contains(player)) return;

            players.Add(player);
            CoopLog.Info($"Spieler betritt die Welt: {player.DisplayName} (Client {player.OwnerClientId}).");
            PlayerAdded?.Invoke(player);
        }

        internal static void Unregister(CoopPlayer player)
        {
            if (player == null) return;
            if (!players.Remove(player)) return;

            string id = player.UgsPlayerId;
            if (!string.IsNullOrEmpty(id) && byUgsId.TryGetValue(id, out var mapped) && mapped == player)
            {
                byUgsId.Remove(id);
            }

            CoopLog.Info($"Spieler verlaesst die Welt: {player.DisplayName}.");
            PlayerRemoved?.Invoke(player);
        }

        internal static void NotifyIdentityReady(CoopPlayer player)
        {
            if (player == null || !player.HasIdentity) return;

            byUgsId[player.UgsPlayerId] = player;
            IdentityReady?.Invoke(player);
        }

        /// <summary>
        /// Sucht den Avatar zu einer UGS-Spieler-ID. Das ist der Aufruf, mit dem die
        /// Voice-Schicht eine eingehende Stimme ihrem Sprecher zuordnet.
        /// </summary>
        public static bool TryGetByUgsId(string ugsPlayerId, out CoopPlayer player)
        {
            if (string.IsNullOrEmpty(ugsPlayerId))
            {
                player = null;
                return false;
            }

            return byUgsId.TryGetValue(ugsPlayerId, out player) && player != null;
        }

        /// <summary>
        /// Leert die Registry. Wird beim Verlassen einer Sitzung aufgerufen, damit eine
        /// neue Runde nicht mit Karteileichen der vorherigen startet.
        /// </summary>
        internal static void Clear()
        {
            players.Clear();
            byUgsId.Clear();
        }
    }
}
