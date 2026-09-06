using System;
using Earshot.Voice;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Pause- und Einstellungsmenue: Esc im Spiel, oder der Button auf dem Startbildschirm.
    /// Mikrofonliste kommt zuerst von Unity, Lautsprecher erst wenn Vivox nach einem
    /// Internet-Join laeuft.
    /// </summary>
    [AddComponentMenu("Earshot/Coop Pause Menu")]
    public class CoopPauseMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private Vector2 scroll;
        private string status = string.Empty;

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (IsOpen)
            {
                SetOpen(false);
            }
            else if (Coop.IsInSession)
            {
                SetOpen(true);
            }
#endif
        }

        private void OnDisable()
        {
            if (IsOpen) SetOpen(false);
        }

        public static void Toggle() => SetOpen(!IsOpen);

        public static void Open() => SetOpen(true);

        private static void SetOpen(bool open)
        {
            IsOpen = open;
            Cursor.lockState = open || !Coop.IsInSession
                ? CursorLockMode.None
                : CursorLockMode.Locked;
            Cursor.visible = open || !Coop.IsInSession;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;

            float width = 480f;
            float height = Mathf.Min(620f, Screen.height - 40f);
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label(Coop.IsInSession ? "Pause" : "Einstellungen", Title);
            GUILayout.Label(
                Coop.IsInSession
                    ? "Esc schliesst dieses Fenster wieder."
                    : "Hier vor dem Start Mikrofon und Lautstaerke waehlen.",
                Body);

            GUILayout.Space(10f);
            if (GUILayout.Button(Coop.IsInSession ? "Weiterspielen" : "Zurueck", GUILayout.Height(40f)))
            {
                SetOpen(false);
            }

            GUILayout.Space(12f);
            GUILayout.Label("Audio", Title);

            GUILayout.Label($"Spiel-Lautstaerke: {Mathf.RoundToInt(CoopVoice.GameVolume * 100f)} %", Body);
            float gameVol = GUILayout.HorizontalSlider(CoopVoice.GameVolume, 0f, 1f);
            if (Mathf.Abs(gameVol - CoopVoice.GameVolume) > 0.001f)
            {
                CoopVoice.GameVolume = gameVol;
            }

            bool muted = CoopVoice.MicrophoneMuted;
            if (GUILayout.Button(
                    muted ? "Mikrofon: STUMM (einschalten)" : "Mikrofon: AN (stummschalten)",
                    GUILayout.Height(32f)))
            {
                CoopVoice.ToggleMicrophone();
                status = CoopVoice.MicrophoneMuted ? "Mikrofon stumm" : "Mikrofon an";
            }

            DrawDeviceRow("Mikrofon", CoopVoice.InputDeviceNames, CoopVoice.ActiveInputDeviceName, name =>
            {
                _ = CoopVoice.SetInputDeviceAsync(name);
                status = "Mikrofon: " + name;
            });

            if (CoopVoice.IsConnected)
            {
                GUILayout.Label($"Mikrofon-Pegel (Aufnahme): {CoopVoice.MicrophoneVolume}", Body);
                int micPeg = Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(CoopVoice.MicrophoneVolume, -50f, 50f));
                if (micPeg != CoopVoice.MicrophoneVolume)
                {
                    CoopVoice.MicrophoneVolume = micPeg;
                }

                GUILayout.Label(
                    $"Gehoerte Stimmen: {Mathf.RoundToInt(CoopVoice.HeardVoiceVolume * 100f)} %",
                    Body);
                float heard = GUILayout.HorizontalSlider(CoopVoice.HeardVoiceVolume, 0f, 1f);
                if (Mathf.Abs(heard - CoopVoice.HeardVoiceVolume) > 0.001f)
                {
                    CoopVoice.HeardVoiceVolume = heard;
                }

                DrawDeviceRow("Lautsprecher", CoopVoice.OutputDeviceNames, CoopVoice.ActiveOutputDeviceName, name =>
                {
                    _ = CoopVoice.SetOutputDeviceAsync(name);
                    status = "Lautsprecher: " + name;
                });

                GUILayout.Label(
                    "Mikrofon-Wechsel steht im Voice-Log (GERAET ...). " +
                    "Gehoerte Stimmen kommen ueber Unity, also den Windows-Standard-Lautsprecher. " +
                    "Wenn die Stimme nicht auf ein anderes Geraet wandert: in Windows das " +
                    "Standard-Wiedergabegeraet aendern.",
                    Body);
            }
            else
            {
                GUILayout.Label(
                    "Lautsprecherwahl erscheint, sobald eine Internet-Sitzung mit Vivox laeuft. " +
                    "Lokal gibt es nur den Testton, kein echtes Mikrofon.",
                    Body);
            }

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Label(status, Body);
            }

            if (Coop.IsInSession)
            {
                GUILayout.Space(12f);
                GUILayout.Label("Voice-Log", Title);
                GUILayout.Label(
                    "Nach der Runde: Ordner EarshotLogs neben der EXE (nicht in AppData). " +
                    "Dort steht, wer geredet hat, wen dieser Rechner gehoert hat, " +
                    "und ob Mikrofon/Lautsprecher wirklich gewechselt haben. " +
                    "Den Ordner des Kumpels brauchst du fuer die Gegenrichtung.",
                    Body);
                if (!string.IsNullOrEmpty(VoiceSessionLog.FilePath))
                {
                    GUILayout.Label(VoiceSessionLog.FilePath, Body);
                }

                if (GUILayout.Button("Voice-Log-Ordner oeffnen", GUILayout.Height(32f)))
                {
                    VoiceSessionLog.OpenFolder();
                }

                GUILayout.Space(12f);
                if (GUILayout.Button("Sitzung verlassen", GUILayout.Height(36f)))
                {
                    SetOpen(false);
                    _ = Coop.LeaveAsync();
                }
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Spiel beenden", GUILayout.Height(36f)))
            {
                Application.Quit();
#if UNITY_EDITOR
                var editorApp = Type.GetType("UnityEditor.EditorApplication, UnityEditor");
                editorApp?.GetProperty("isPlaying")?.SetValue(null, false);
#endif
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawDeviceRow(string label, string[] devices, string active, Action<string> onPick)
        {
            GUILayout.Space(6f);
            GUILayout.Label(label + ": " + (string.IsNullOrEmpty(active) ? "(Standard)" : active), Body);

            if (devices == null || devices.Length == 0)
            {
                GUILayout.Label("Keine Geraete gefunden.", Body);
                return;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                string name = devices[i];
                if (CoopVoice.IsUnusableAudioDevice(name)) continue;
                bool selected = name == active;
                if (GUILayout.Button((selected ? "> " : "  ") + name, GUILayout.Height(26f)))
                {
                    onPick(name);
                }
            }
        }

        private static GUIStyle title;
        private static GUIStyle body;

        private static GUIStyle Title =>
            title ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

        private static GUIStyle Body =>
            body ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
    }
}
