using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Schreibt ein lesbares Protokoll der Stimmsitzung auf die Festplatte.
    /// Jeder Rechner haelt fest, wen er empfangen und gehoert hat - nach dem
    /// Spielen mit einem Freund die Datei oeffnen, statt zu raten.
    /// </summary>
    public static class VoiceSessionLog
    {
        private const int RecentCapacity = 40;
        private const string FolderName = "EarshotLogs";

        private static readonly List<string> recent = new List<string>(RecentCapacity);
        private static StreamWriter writer;
        private static string filePath;

        public static string FilePath => filePath ?? string.Empty;

        public static string FolderPath
        {
            get
            {
                // Build: neben der EXE. Editor: Projektordner neben Assets.
                string root = Application.dataPath;
                try
                {
                    var parent = Directory.GetParent(root);
                    if (parent != null) root = parent.FullName;
                }
                catch
                {
                    root = Application.persistentDataPath;
                }

                return Path.Combine(root, FolderName);
            }
        }

        public static IReadOnlyList<string> Recent => recent;

        public static bool IsRecording => writer != null;

        internal static void BeginSession()
        {
            EndSession("Neue Sitzung startet.");

            try
            {
                Directory.CreateDirectory(FolderPath);
                WriteReadme(FolderPath);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");

                // Zwei Fenster auf demselben PC (Solo-Test) starten oft in derselben
                // Sekunde. Die Prozess-ID haengt jede Datei sicher auseinander, auch wenn
                // Millisekunden zufaellig gleich sein sollten.
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                filePath = Path.Combine(FolderPath, $"voice-{stamp}-pid{pid}.txt");
                writer = new StreamWriter(filePath, false, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                WriteRaw("# Earshot Voice-Log");
                WriteRaw($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteRaw($"# Datei: {filePath}");
                WriteRaw($"# Szene: {SafeSceneName()}");
                WriteRaw($"# Rolle: {(Coop.IsHost ? "Host" : "Client")}");
                WriteRaw($"# Name: {Coop.LocalPlayerName}");
                WriteRaw($"# Join-Code: {Coop.JoinCode}");
                WriteRaw($"# Lokal ohne Internet: {Coop.IsLocalSession}");
                WriteRaw("#");
                WriteRaw("# Jeder Rechner schreibt, WEN ER GEHOERT hat.");
                WriteRaw("# AUDIO STIRBT / AUDIO LEBT / AUDIO WECHSEL = 2D oder 3D kippt.");
                WriteRaw("# TAP STIRBT / TAP LEBT = Vivox-Stream kommt an oder nicht.");
                WriteRaw("# VIVOX-PAUSE = Vivox hat die AudioSource selbst pausiert (kein");
                WriteRaw("#   Nachschub >400ms) - Netzwerk/Jitter oder zu wenig CPU-Zeit, NICHT");
                WriteRaw("#   unsere Pipeline. Steht das hier, ist das der eigentliche Beweis.");
                WriteRaw("# SELBSTHEILUNG = Tap blieb pausiert, WAEHREND Vivox 'redet gerade'");
                WriteRaw("#   meldete - kein Fall, aus dem sich Vivox von selbst erholt. Wir");
                WriteRaw("#   bauen den Tap dann neu auf. Steht das hier oefter fuer denselben");
                WriteRaw("#   Spieler, ist die Zustellung fuer ihn dauerhaft gestoert (Netzwerk).");
                WriteRaw("# Den Log des Kumpels brauchst du fuer die Gegenrichtung.");
                WriteRaw("#");

                if (Coop.IsLocalSession)
                {
                    Note(
                        "HINWEIS: Lokale Sitzung - kein Vivox, kein echtes Mikrofon. " +
                        "Mit einem Freund im Internet hosten, nicht 'dieser PC'.");
                }

                CoopLog.Info($"Voice-Log: {filePath}");
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Voice-Log konnte nicht angelegt werden", ex);
                writer = null;
                filePath = string.Empty;
            }
        }

        internal static void EndSession(string reason)
        {
            if (writer == null) return;

            try
            {
                Note("SITZUNG Ende: " + reason);
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Voice-Log konnte nicht geschlossen werden", ex);
            }
            finally
            {
                writer = null;
            }
        }

        public static void Note(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            if (recent.Count >= RecentCapacity) recent.RemoveAt(0);
            recent.Add(line);
            WriteRaw(line);
        }

        /// <summary>
        /// Wie <see cref="Note"/>, zusaetzlich sofort sichtbar in der Unity-Konsole.
        /// <para>
        /// Fuer alles, was beim Testen mit einem Freund SOFORT auffallen soll - "hoert
        /// mich nicht", "Tap tot", falsche Zahl AudioListener - statt erst nach dem
        /// Spielen in der Log-Datei entdeckt zu werden.
        /// </para>
        /// </summary>
        public static void Alert(string message)
        {
            Note(message);
            CoopLog.Warn(message);
        }

        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Voice-Log-Ordner fehlt", ex);
                return;
            }

            Application.OpenURL("file:///" + FolderPath.Replace('\\', '/'));
        }

        private static void WriteReadme(string folder)
        {
            string readme = Path.Combine(folder, "LIESMICH.txt");
            if (File.Exists(readme)) return;

            File.WriteAllText(
                readme,
                "Earshot Voice-Logs\n" +
                "\n" +
                "Nach dem Spielen die voice-....txt Dateien aus diesem Ordner schicken.\n" +
                "Jede Datei gehoert zu EINEM Rechner: wen ICH gehoert habe, welches Mikrofon,\n" +
                "welcher Lautsprecher, und wo etwas fehlgeschlagen ist.\n" +
                "Den Log des Kumpels brauchst du fuer die Gegenrichtung.\n",
                new UTF8Encoding(false));
        }

        private static void WriteRaw(string line)
        {
            if (writer == null) return;

            try
            {
                writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                CoopLog.Exception("Voice-Log Schreiben fehlgeschlagen", ex);
            }
        }

        private static string SafeSceneName()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : "(unbekannt)";
        }
    }
}
