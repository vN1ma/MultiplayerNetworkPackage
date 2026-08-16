using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Ein sofort einsatzbereites Overlay zum Hosten und Beitreten.
    /// <para>
    /// Bewusst mit Unitys direkt gezeichneter Oberflaeche gebaut und nicht mit einem Canvas:
    /// So funktioniert es in jedem Projekt ohne Prefabs, ohne EventSystem und ohne
    /// Abhaengigkeit auf ein bestimmtes UI-Paket. Fuer das fertige Spiel baut man eine
    /// eigene Oberflaeche und ruft dieselben drei Methoden von <see cref="Coop"/> auf.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Coop Quick Menu")]
    public class CoopQuickMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Overlay anzeigen. Fuer Aufnahmen oder das fertige Spiel abschalten.")]
        private bool visible = true;

        [SerializeField]
        [Tooltip("Taste, die das Overlay ein- und ausblendet. Im Join-Bildschirm ohne Wirkung.")]
        private KeyCode toggleKey = KeyCode.F1;

        [SerializeField]
        [Tooltip("Position am Bildschirmrand in Pixeln, sobald eine Sitzung laeuft.")]
        private Vector2 margin = new Vector2(16f, 16f);

        private string codeInput = string.Empty;
        private string statusMessage = string.Empty;
        private bool busy;

        private static GUIStyle richLabel;
        private static GUIStyle titleLabel;
        private static GUIStyle bigButton;

        private void Update()
        {
            if (Coop.State == CoopState.Offline)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                visible = true;
                return;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
                if (visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
#endif
        }

        private void OnGUI()
        {
            if (CoopPauseMenu.IsOpen) return;
            if (!visible) return;

            EnsureStyles();

            if (Coop.State == CoopState.Offline)
            {
                DrawJoinScreen();
                return;
            }

            var area = new Rect(margin.x, margin.y, 340f, 340f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Earshot", titleLabel);
            GUILayout.Label($"Zustand: {Coop.State}", richLabel);

            GUI.enabled = !busy;

            switch (Coop.State)
            {
                case CoopState.Hosting:
                case CoopState.Connected:
                    DrawSessionControls();
                    break;

                default:
                    GUILayout.Label("Bitte warten...", richLabel);
                    break;
            }

            GUI.enabled = true;
            DrawStatus();
            GUILayout.Label($"{toggleKey} blendet dieses Fenster aus", richLabel);
            GUILayout.EndArea();
        }

        private void DrawJoinScreen()
        {
            float width = 460f;
            float height = 500f;
            var area = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Space(12f);
            GUILayout.Label("Earshot Testraum", titleLabel);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Freund im Internet: unten hosten, Code schicken, er tritt bei.\n" +
                "Zweiter Spieler auf diesem PC: Beitreten (dieser PC).",
                richLabel);

            GUI.enabled = !busy;
            GUILayout.Space(12f);

            if (GUILayout.Button("Einstellungen (Mikro, Lautstaerke)", GUILayout.Height(32f)))
            {
                CoopPauseMenu.Open();
            }

            GUILayout.Space(12f);
            if (GUILayout.Button("Beitreten (dieser PC)", bigButton))
            {
                _ = JoinLocalAsync();
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Selbst hosten (dieser PC)", GUILayout.Height(36f)))
            {
                _ = HostLocalAsync();
            }

            GUILayout.Space(18f);
            GUILayout.Label("Internet (Unity Cloud / Relay, funktioniert auch DE zu NO)", richLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spiel hosten (Internet)", GUILayout.Height(36f)))
            {
                _ = HostAsync();
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            codeInput = GUILayout.TextField(codeInput, 8, GUILayout.Height(32f), GUILayout.Width(140f));
            if (GUILayout.Button("Beitreten (Internet)", GUILayout.Height(32f)))
            {
                _ = JoinAsync(codeInput);
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;

            DrawStatus();
            GUILayout.EndArea();
        }

        private void DrawSessionControls()
        {
            if (Coop.IsHost && !string.IsNullOrEmpty(Coop.JoinCode))
            {
                GUILayout.Label(Coop.JoinCode, titleLabel);

                if (Coop.IsLocalSession)
                {
                    GUILayout.Label(
                        "Zweite Instanz: in der EXE auf 'Beitreten (dieser PC)' klicken. " +
                        "Fuer einen Freund im Internet: Verlassen, dann Spiel hosten (Internet).",
                        richLabel);
                }

                if (GUILayout.Button("Code kopieren"))
                {
                    GUIUtility.systemCopyBuffer = Coop.JoinCode;
                    statusMessage = "Code in der Zwischenablage.";
                }
            }

            GUILayout.Label($"Spieler: {Coop.Players.Count}", richLabel);

            GUILayout.Space(6f);
            if (GUILayout.Button("Verlassen", GUILayout.Height(32f)))
            {
                _ = LeaveAsync();
            }
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(statusMessage)) return;
            GUILayout.Space(8f);
            GUILayout.Label(statusMessage, richLabel);
        }

        private async System.Threading.Tasks.Task HostLocalAsync()
        {
            busy = true;
            statusMessage = string.Empty;

            try
            {
                await Coop.HostLocalAsync();
                statusMessage = "Lokal unterwegs. WASD, E an der Tuer, Esc gibt die Maus frei.";
            }
            catch (System.Exception ex)
            {
                statusMessage = "Fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                busy = false;
            }
        }

        private async System.Threading.Tasks.Task JoinLocalAsync()
        {
            busy = true;
            statusMessage = "Verbinde mit dem Host auf diesem PC...";

            try
            {
                await Coop.JoinLocalAsync();
                statusMessage = "Beigetreten. Du bist die orangene Figur.";
            }
            catch (System.Exception ex)
            {
                statusMessage =
                    "Kein Host gefunden. Im Unity-Editor Play druecken, dann hier nochmal Beitreten. " +
                    ex.Message;
            }
            finally
            {
                busy = false;
            }
        }

        private async System.Threading.Tasks.Task HostAsync()
        {
            busy = true;
            statusMessage = string.Empty;

            try
            {
                string code = await Coop.HostAsync();
                statusMessage = "Sitzung laeuft. Code: " + code;
            }
            catch (System.Exception ex)
            {
                statusMessage = "Fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                busy = false;
            }
        }

        private async System.Threading.Tasks.Task JoinAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                statusMessage = "Bitte einen Code eingeben.";
                return;
            }

            busy = true;
            statusMessage = string.Empty;

            try
            {
                await Coop.JoinAsync(code);
                statusMessage = "Beigetreten.";
            }
            catch (System.Exception ex)
            {
                statusMessage = "Fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                busy = false;
            }
        }

        private async System.Threading.Tasks.Task LeaveAsync()
        {
            busy = true;

            try
            {
                await Coop.LeaveAsync();
                statusMessage = string.Empty;
            }
            finally
            {
                busy = false;
            }
        }

        private static void EnsureStyles()
        {
            if (richLabel == null)
            {
                richLabel = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = 14
                };
            }

            if (titleLabel == null)
            {
                titleLabel = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (bigButton == null)
            {
                bigButton = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 56f
                };
            }
        }
    }
}
