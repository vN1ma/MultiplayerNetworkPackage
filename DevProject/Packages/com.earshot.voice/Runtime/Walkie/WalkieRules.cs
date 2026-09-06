using System;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Reine Walkie-Regeln ohne Vivox/Unity-Audio — testbar und vom Spiel aufrufbar.
    /// </summary>
    public static class WalkieRules
    {
        public const string RadioChannelPrefix = "earshot.radio.";

        /// <summary>
        /// Vivox-Kanalname aus der logischen Funkkanal-ID (z.B. "a" → "earshot.radio.a").
        /// </summary>
        public static string ToVivoxRadioChannel(string logicalChannelId)
        {
            string id = SanitizeChannelId(logicalChannelId);
            return RadioChannelPrefix + id;
        }

        /// <summary>
        /// Logische ID aus einem Vivox-Funkkanal, oder leer wenn es keiner ist.
        /// </summary>
        public static bool TryParseLogicalChannel(string vivoxChannelName, out string logicalChannelId)
        {
            logicalChannelId = string.Empty;
            if (string.IsNullOrEmpty(vivoxChannelName)) return false;
            if (!vivoxChannelName.StartsWith(RadioChannelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            logicalChannelId = vivoxChannelName.Substring(RadioChannelPrefix.Length);
            return !string.IsNullOrEmpty(logicalChannelId);
        }

        public static string SanitizeChannelId(string logicalChannelId)
        {
            if (string.IsNullOrWhiteSpace(logicalChannelId)) return "default";

            var chars = logicalChannelId.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) chars[i] = '_';
            }

            return new string(chars);
        }

        /// <summary>
        /// Empfang: Gerät an und lokal nicht am Senden (Half-Duplex).
        /// </summary>
        public static bool ShouldPlayReceivedRadio(bool devicePowered, bool localTransmitting)
        {
            return devicePowered && !localTransmitting;
        }

        /// <summary>
        /// Senden nur mit Strom und wenn das Spiel „in der Hand“ freigibt.
        /// </summary>
        public static bool CanTransmit(bool powered, bool canTransmit)
        {
            return powered && canTransmit;
        }

        /// <summary>
        /// Mund-Stimme leiser, solange derselbe Sprecher übers Funkgerät sendet.
        /// </summary>
        public static float MouthVolumeScale(bool speakerTransmittingOnRadio, float dampeningWhileRadio)
        {
            if (!speakerTransmittingOnRadio) return 1f;
            return Mathf.Clamp01(dampeningWhileRadio);
        }

        /// <summary>
        /// Funk-Delay in Sekunden, begrenzt auf einen hoerbaren, stabilen Bereich.
        /// </summary>
        public static float ClampDelaySeconds(float seconds)
        {
            return Mathf.Clamp(seconds, 0f, 1.5f);
        }
    }
}
