using System;
using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Meldet vorhandene Charaktere an die Sprachschicht, unabhaengig davon, wie ihre
    /// Identitaet entsteht (Netcode-Adapter, eigene Loesung, oder gar keine).
    /// <para>
    /// Ersetzt sowohl das fruehere <c>ProxVoiceRoster</c> (netzwerkfrei) als auch das
    /// coop-eigene <c>PlayerRegistry</c> (Netcode-gebunden): beide taten dasselbe, nur mit
    /// unterschiedlichem Spielertyp. Hier ist der Spielertyp <see cref="IProximityVoicePlayer"/>.
    /// </para>
    /// </summary>
    public static class VoiceRoster
    {
        private static readonly List<IProximityVoicePlayer> players = new List<IProximityVoicePlayer>();
        private static readonly Dictionary<string, IProximityVoicePlayer> byPlayerId =
            new Dictionary<string, IProximityVoicePlayer>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<IProximityVoicePlayer> Players => players;

        public static IProximityVoicePlayer LocalPlayer { get; private set; }

        /// <summary>
        /// Wird ausgeloest, sobald ein Charakter eine Spieler-ID hat, mit der Vivox
        /// den Tap dem Avatar zuordnen kann.
        /// </summary>
        public static event Action<IProximityVoicePlayer> IdentityReady;

        /// <summary>Ein Spielerobjekt ist erschienen. Die Identitaet kann noch fehlen.</summary>
        public static event Action<IProximityVoicePlayer> PlayerAdded;

        /// <summary>Ein Spielerobjekt ist verschwunden.</summary>
        public static event Action<IProximityVoicePlayer> PlayerRemoved;

        public static void Register(IProximityVoicePlayer player)
        {
            if (player == null) return;
            if (!players.Contains(player))
            {
                players.Add(player);
                PlayerAdded?.Invoke(player);
            }

            if (player.IsLocalPlayer) LocalPlayer = player;
            else if (LocalPlayer == player) LocalPlayer = null;

            if (player.HasIdentity) NotifyIdentityReady(player);
        }

        public static void Unregister(IProximityVoicePlayer player)
        {
            if (player == null) return;
            if (!players.Remove(player)) return;

            if (player.HasIdentity && byPlayerId.TryGetValue(player.PlayerId, out var mapped) && ReferenceEquals(mapped, player))
            {
                byPlayerId.Remove(player.PlayerId);
            }

            if (LocalPlayer == player) LocalPlayer = null;
            PlayerRemoved?.Invoke(player);
        }

        public static bool TryGetByPlayerId(string playerId, out IProximityVoicePlayer player)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                player = null;
                return false;
            }

            return byPlayerId.TryGetValue(playerId, out player) && player != null;
        }

        public static void NotifyIdentityReady(IProximityVoicePlayer player)
        {
            if (player == null || !player.HasIdentity) return;

            byPlayerId[player.PlayerId] = player;
            if (player.IsLocalPlayer) LocalPlayer = player;
            IdentityReady?.Invoke(player);
        }

        /// <summary>
        /// Robuste Listener-Suche fuer Szenen mit MEHR ALS EINEM aktiven AudioListener
        /// (Unity erlaubt das, warnt aber und das Verhalten ist sonst undefiniert — z.B.
        /// eine Lobby-/Verbindungs-UI, die ihre eigene Kamera+Listener nicht abschaltet).
        /// Bevorzugt einen Listener, der Kind des eigenen Spieler-Ankers ist; sonst den
        /// ersten aktiven ueberhaupt. Nie null zurueckgeben, wenn irgendein Listener existiert.
        /// </summary>
        internal static AudioListener FindPreferredAudioListener()
        {
#if UNITY_6000_5_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#else
            var found = UnityEngine.Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            var local = LocalPlayer;
            if (local != null && local.VoiceAnchor != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    var candidate = found[i];
                    if (candidate != null && candidate.isActiveAndEnabled &&
                        candidate.transform.IsChildOf(local.VoiceAnchor))
                    {
                        return candidate;
                    }
                }
            }

            for (int i = 0; i < found.Length; i++)
            {
                var candidate = found[i];
                if (candidate != null && candidate.isActiveAndEnabled) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Leert das Register. Fuer den Wechsel zwischen Sitzungen, damit eine neue Runde
        /// nicht mit Karteileichen der vorherigen startet.
        /// </summary>
        public static void Clear()
        {
            players.Clear();
            byPlayerId.Clear();
            LocalPlayer = null;
        }
    }
}
