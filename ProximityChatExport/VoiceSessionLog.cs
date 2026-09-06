using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Schreibt ein lesbares Protokoll der Stimmsitzung auf die Festplatte.
    /// Nach einem Test mit einem Freund die Datei oeffnen, statt zu raten.
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

        internal static void BeginSession(string channelName, string displayName)
        {
            EndSession("Neue Sitzung startet.");

            try
            {
                Directory.CreateDirectory(FolderPath);
                WriteReadme(FolderPath);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                filePath = Path.Combine(FolderPath, $"voice-{stamp}-pid{pid}.txt");
                writer = new StreamWriter(filePath, false, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                WriteRaw("# Earshot Proximity Voice-Log");
                WriteRaw($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteRaw($"# Datei: {filePath}");
                WriteRaw($"# Szene: {SafeSceneName()}");
                WriteRaw($"# Name: {displayName}");
                WriteRaw($"# Kanal: {channelName}");
                WriteRaw($"# Lokale PlayerId: {ProxVoice.LocalPlayerId}");
                WriteRaw("#");
                ProxLog.Info("Voice-Log: " + filePath);
            }
            catch (Exception ex)
            {
                ProxLog.Exception("Voice-Log konnte nicht angelegt werden", ex);
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
                ProxLog.Exception("Voice-Log konnte nicht geschlossen werden", ex);
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

        public static void Alert(string message)
        {
            Note(message);
            ProxLog.Warn(message);
        }

        private static void WriteReadme(string folder)
        {
            string readme = Path.Combine(folder, "LIESMICH.txt");
            if (File.Exists(readme)) return;

            File.WriteAllText(
                readme,
                "Proximity Voice-Logs\n\n" +
                "Nach dem Spielen die voice-....txt Dateien aus diesem Ordner schicken.\n",
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
                ProxLog.Exception("Voice-Log Schreiben fehlgeschlagen", ex);
            }
        }

        private static string SafeSceneName()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : "(unbekannt)";
        }
    }
}
