using System;
using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Meldet vorhandene Charaktere an die Sprachschicht. Ersetzt in diesem Export
    /// das Coop-Spielerregister: das Host-Spiel traegt seine Avatare selbst ein.
    /// </summary>
    public static class ProxVoiceRoster
    {
        private static readonly List<ProxVoicePlayer> players = new List<ProxVoicePlayer>();

        public static IReadOnlyList<ProxVoicePlayer> Players => players;

        public static ProxVoicePlayer LocalPlayer { get; private set; }

        /// <summary>
        /// Wird ausgeloest, sobald ein Charakter eine Spieler-ID hat, mit der Vivox
        /// den Tap dem Avatar zuordnen kann.
        /// </summary>
        public static event Action<ProxVoicePlayer> IdentityReady;

        public static void Register(ProxVoicePlayer player)
        {
            if (player == null) return;
            if (!players.Contains(player)) players.Add(player);

            if (player.IsLocalPlayer) LocalPlayer = player;
            else if (LocalPlayer == player) LocalPlayer = null;

            if (player.HasIdentity) IdentityReady?.Invoke(player);
        }

        public static void Unregister(ProxVoicePlayer player)
        {
            if (player == null) return;
            players.Remove(player);
            if (LocalPlayer == player) LocalPlayer = null;
        }

        public static bool TryGetByPlayerId(string playerId, out ProxVoicePlayer player)
        {
            player = null;
            if (string.IsNullOrEmpty(playerId)) return false;

            for (int i = 0; i < players.Count; i++)
            {
                var candidate = players[i];
                if (candidate == null || !candidate.HasIdentity) continue;
                if (string.Equals(candidate.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    player = candidate;
                    return true;
                }
            }

            return false;
        }

        public static void NotifyIdentityReady(ProxVoicePlayer player)
        {
            if (player == null || !player.HasIdentity) return;
            if (player.IsLocalPlayer) LocalPlayer = player;
            IdentityReady?.Invoke(player);
        }
    }
}
