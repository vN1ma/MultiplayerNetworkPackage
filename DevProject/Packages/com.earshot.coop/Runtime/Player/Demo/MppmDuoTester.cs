using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Testraum eigens fuer den Multiplayer Play Mode (zwei Spieler an einem PC, ein
    /// einziger Play-Knopf, kein Build).
    /// <para>
    /// Die Haupt-Editorinstanz hostet von selbst ueber das Internet (Relay/Vivox, also
    /// eine echte Sitzung) und zeigt links oben nur den Code. Der virtuelle Spieler
    /// (Player 2) bekommt nur ein Eingabefeld fuer den Code und einen Beitreten-Knopf.
    /// Kein Testton, kein Menue mit zehn Knoepfen - genau die zwei Schritte, um mit
    /// echtem Vivox zwischen zwei Fenstern auf einem Rechner zu testen.
    /// </para>
    /// <para>
    /// Die Rollen-Erkennung ist dieselbe wie bei <see cref="PlaytestLocalHost"/>
    /// (MPPM-Player-Erkennung per Reflection, '-client', '-name PlayerN').
    /// </para>
    /// </summary>
    [AddComponentMenu("")]
    public class MppmDuoTester : MonoBehaviour
    {
        private string codeInput = string.Empty;
        private string status = string.Empty;
        private bool busy;
        private bool isClientRole;
        private bool autoHostStarted;

        private static GUIStyle title;
        private static GUIStyle codeLabel;
        private static GUIStyle body;

        private void Awake()
        {
            isClientRole = PlaytestLocalHost.IsClientInstance();
        }

        private void Start()
        {
            if (!isClientRole) _ = AutoHostAsync();
        }

        private async Task AutoHostAsync()
        {
            if (autoHostStarted) return;
            autoHostStarted = true;
            busy = true;
            status = "Hoste ueber das Internet...";

            try
            {
                await Coop.HostAsync();
                status = string.Empty;
            }
            catch (Exception ex)
            {
                status = "Hosten fehlgeschlagen: " + ex.Message;
                CoopLog.Exception("MPPM-Duo: Hosten fehlgeschlagen", ex);
            }
            finally
            {
                busy = false;
            }
        }

        private async Task JoinAsync()
        {
            if (string.IsNullOrWhiteSpace(codeInput))
            {
                status = "Bitte den Code von der ersten Instanz eingeben.";
                return;
            }

            busy = true;
            status = "Verbinde...";

            try
            {
                await Coop.JoinAsync(codeInput.Trim());
                status = string.Empty;
            }
            catch (Exception ex)
            {
                status = "Beitreten fehlgeschlagen: " + ex.Message;
                CoopLog.Exception("MPPM-Duo: Beitreten fehlgeschlagen", ex);
            }
            finally
            {
                busy = false;
            }
        }

        private async Task LeaveAsync()
        {
            busy = true;
            try
            {
                await Coop.LeaveAsync();
            }
            finally
            {
                busy = false;
                status = string.Empty;
            }
        }

        private void OnGUI()
        {
            if (CoopPauseMenu.IsOpen) return;
            EnsureStyles();

            var area = new Rect(12f, 12f, 380f, 190f);
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.Label(isClientRole ? "MPPM Spieler 2 (Client)" : "MPPM Spieler 1 (Host)", title);

            if (!isClientRole) DrawHost();
            else DrawClient();

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(4f);
                GUILayout.Label(status, body);
            }

            GUILayout.Label("Esc = Pause (Mikro/Lautstaerke), F3 = Live-Stimme", body);
            GUILayout.EndArea();
        }

        private void DrawHost()
        {
            if (Coop.IsHost && !string.IsNullOrEmpty(Coop.JoinCode))
            {
                GUILayout.Label(Coop.JoinCode, codeLabel);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Code kopieren", GUILayout.Height(28f)))
                {
                    GUIUtility.systemCopyBuffer = Coop.JoinCode;
                    status = "In der Zwischenablage.";
                }

                if (GUILayout.Button("Verlassen", GUILayout.Height(28f)))
                {
                    _ = LeaveAsync();
                }

                GUILayout.EndHorizontal();
                GUILayout.Label($"Spieler: {Coop.Players.Count}", body);
            }
            else
            {
                GUILayout.Label(busy ? "Hoste ueber das Internet..." : "Warte...", body);
            }
        }

        private void DrawClient()
        {
            if (Coop.IsInSession)
            {
                GUILayout.Label($"Verbunden. Spieler: {Coop.Players.Count}", body);
                if (GUILayout.Button("Verlassen", GUILayout.Height(28f)))
                {
                    _ = LeaveAsync();
                }

                return;
            }

            GUI.enabled = !busy;
            GUILayout.BeginHorizontal();
            codeInput = GUILayout.TextField(codeInput, 8, GUILayout.Height(28f), GUILayout.Width(160f));
            if (GUILayout.Button("Beitreten", GUILayout.Height(28f)))
            {
                _ = JoinAsync();
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private static void EnsureStyles()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold
                };
            }

            if (codeLabel == null)
            {
                codeLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold
                };
            }

            if (body == null)
            {
                body = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 12
                };
            }
        }
    }
}
