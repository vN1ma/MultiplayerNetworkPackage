using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Einheitliche, abschaltbare Protokollierung. Alle Meldungen tragen das Praefix
    /// "[Earshot]", damit sie sich in einer vollen Konsole wiederfinden lassen.
    /// </summary>
    public static class CoopLog
    {
        private const string Prefix = "<color=#4EA8DE><b>[Earshot]</b></color> ";

        /// <summary>
        /// Ausfuehrliche Meldungen zum Verbindungsablauf. Standardmaessig an, weil beim
        /// Einrichten fast jede Frage mit "was passiert eigentlich gerade" beginnt.
        /// </summary>
        public static bool Verbose = true;

        public static void Info(string message)
        {
            if (Verbose) Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        public static void Exception(string context, System.Exception exception)
        {
            Debug.LogError($"{Prefix}{context}: {exception.Message}");
            Debug.LogException(exception);
        }
    }
}
