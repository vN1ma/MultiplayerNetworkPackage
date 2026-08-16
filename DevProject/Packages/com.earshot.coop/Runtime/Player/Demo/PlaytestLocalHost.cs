using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Startet im Testraum automatisch eine lokale Sitzung.
    /// <para>
    /// Die Haupt-Editorinstanz hostet. Ein zweiter virtueller Spieler
    /// (Multiplayer Play Mode, Player 2) tritt von selbst bei. Damit laesst sich
    /// Bewegung und Tuer mit zwei Figuren pruefen, ohne Build und ohne Account.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Playtest Local Host")]
    public class PlaytestLocalHost : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Beim Play im Editor automatisch lokal hosten. Ausschalten, wenn du stattdessen ueber das Internet hosten willst.")]
        private bool hostOnPlay = true;

        private void Start()
        {
            if (FindAnyObjectByType<CoopPauseMenu>() == null)
            {
                var pause = new GameObject("Earshot Pause");
                pause.AddComponent<CoopPauseMenu>();
            }

            if (Coop.IsInSession) return;

            if (IsClientInstance())
            {
                _ = JoinWhenReady();
                return;
            }

            if (!hostOnPlay) return;
            if (!Application.isEditor) return;

            _ = HostWhenReady();
        }

        private static async Task HostWhenReady()
        {
            try
            {
                await Coop.HostLocalAsync();
                CoopLog.Info(
                    "Lokaler Host. Zweiter Spieler: Build starten, F1, 'Lokal beitreten'.");
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Lokaler Testhost konnte nicht starten", ex);
            }
        }

        private static async Task JoinWhenReady()
        {
            // Der Host braucht einen Moment, bevor Port 7777 offen ist.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(400);
                if (Coop.IsInSession) return;

                try
                {
                    await Coop.JoinLocalAsync();
                    CoopLog.Info("Als zweiter lokaler Spieler beigetreten.");
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 7)
                    {
                        CoopLog.Exception("Zweiter Spieler konnte nicht beitreten", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Wahr fuer virtuelle MPPM-Spieler, Builds mit -client, und alles was nicht
        /// die Haupt-Editorinstanz ist.
        /// </summary>
        internal static bool IsClientInstance()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-client", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (args[i] == "-name" && i + 1 < args.Length)
                {
                    string name = args[i + 1];
                    if (name.StartsWith("Player", StringComparison.Ordinal) && name != "Player1")
                    {
                        return true;
                    }
                }
            }

            return IsMppmVirtualPlayer();
        }

        private static bool IsMppmVirtualPlayer()
        {
            var type =
                Type.GetType("Unity.Multiplayer.Playmode.CurrentPlayer, Unity.Multiplayer.Playmode") ??
                Type.GetType("Unity.Multiplayer.PlayMode.CurrentPlayer, Unity.Multiplayer.PlayMode");
            if (type == null) return false;

            var property = type.GetProperty("IsMainEditor", BindingFlags.Public | BindingFlags.Static);
            if (property == null || property.PropertyType != typeof(bool)) return false;

            try
            {
                return !(bool)property.GetValue(null);
            }
            catch
            {
                return false;
            }
        }
    }
}
