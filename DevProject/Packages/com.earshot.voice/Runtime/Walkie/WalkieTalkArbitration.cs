using System;
using System.Collections.Generic;

namespace Earshot.Voice
{
    /// <summary>
    /// First-Speaker-Lock: wer zuerst angefangen hat zu funken, bleibt hoerbar,
    /// bis er aufhoert — danach der naechste Wartende (fruehester Start).
    /// </summary>
    public sealed class WalkieTalkArbitration
    {
        private readonly Dictionary<string, float> speakingSince =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> scratchKeys = new List<string>(8);

        /// <summary>Kanal-scoped Key: "channelId|playerId".</summary>
        public static string Key(string channelId, string playerId)
        {
            return WalkieRules.SanitizeChannelId(channelId) + "|" + (playerId ?? string.Empty);
        }

        public void SetSpeaking(string channelId, string playerId, bool speaking, float nowSeconds)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            string key = Key(channelId, playerId);

            if (speaking)
            {
                if (!speakingSince.ContainsKey(key))
                {
                    speakingSince[key] = nowSeconds;
                }
            }
            else
            {
                speakingSince.Remove(key);
            }
        }

        public void ClearChannel(string channelId)
        {
            string prefix = WalkieRules.SanitizeChannelId(channelId) + "|";
            scratchKeys.Clear();
            foreach (var pair in speakingSince)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    scratchKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < scratchKeys.Count; i++)
            {
                speakingSince.Remove(scratchKeys[i]);
            }
        }

        public void ClearAll() => speakingSince.Clear();

        /// <summary>
        /// Gewinner auf dem Kanal, oder null wenn niemand spricht.
        /// </summary>
        public string GetWinnerPlayerId(string channelId)
        {
            string prefix = WalkieRules.SanitizeChannelId(channelId) + "|";
            string bestPlayer = null;
            float bestSince = float.MaxValue;

            foreach (var pair in speakingSince)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (pair.Value >= bestSince) continue;

                bestSince = pair.Value;
                int sep = pair.Key.IndexOf('|');
                bestPlayer = sep >= 0 && sep + 1 < pair.Key.Length
                    ? pair.Key.Substring(sep + 1)
                    : null;
            }

            return bestPlayer;
        }

        public bool IsWinner(string channelId, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            string winner = GetWinnerPlayerId(channelId);
            return string.Equals(winner, playerId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
