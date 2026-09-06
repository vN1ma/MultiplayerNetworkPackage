using System;
using System.Collections.Generic;

namespace Earshot.Voice
{
    /// <summary>
    /// Verteilt Funk-Samples an alle Walkie-Geraete (Fan-out, nicht ein gemeinsamer Read).
    /// </summary>
    internal static class WalkieRadioBus
    {
        public const string LocalSidetoneStreamId = "__local__";

        private static readonly List<WalkieDeviceOutput> devices = new List<WalkieDeviceOutput>(8);
        private static readonly Dictionary<string, string> audibleRemoteByChannel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object gate = new object();

        public static void Register(WalkieDeviceOutput device)
        {
            if (device == null) return;
            lock (gate)
            {
                if (!devices.Contains(device)) devices.Add(device);
            }
        }

        public static void Unregister(WalkieDeviceOutput device)
        {
            if (device == null) return;
            lock (gate)
            {
                devices.Remove(device);
            }
        }

        public static void Write(string channelId, string streamId, float[] data, int offset, int length)
        {
            if (data == null || length <= 0) return;
            string channel = WalkieRules.SanitizeChannelId(channelId);

            lock (gate)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i];
                    if (d == null) continue;
                    d.PushSamples(channel, streamId, data, offset, length);
                }
            }
        }

        public static void SetAudibleRemote(string channelId, string playerId)
        {
            string id = WalkieRules.SanitizeChannelId(channelId);
            lock (gate)
            {
                if (string.IsNullOrEmpty(playerId)) audibleRemoteByChannel.Remove(id);
                else audibleRemoteByChannel[id] = playerId;
            }
        }

        public static string GetAudibleRemote(string channelId)
        {
            string id = WalkieRules.SanitizeChannelId(channelId);
            lock (gate)
            {
                return audibleRemoteByChannel.TryGetValue(id, out string playerId) ? playerId : null;
            }
        }

        public static void ClearStream(string channelId, string streamId)
        {
            string channel = WalkieRules.SanitizeChannelId(channelId);
            lock (gate)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    devices[i]?.ClearInbox(channel, streamId);
                }
            }
        }

        public static void ClearAll()
        {
            lock (gate)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    devices[i]?.ClearAllInbox();
                }

                audibleRemoteByChannel.Clear();
            }
        }
    }
}
