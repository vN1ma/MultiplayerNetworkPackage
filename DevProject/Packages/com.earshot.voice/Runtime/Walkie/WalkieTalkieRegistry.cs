using System;
using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Zentrale Liste aller Walkies und des lokalen Sendezustands.
    /// <para>
    /// Das Spiel steuert einzelne Geraete; Runtime und Backend lesen hier, welcher
    /// Funkkanal noetig ist und wo die Stimme abgespielt werden soll.
    /// </para>
    /// </summary>
    public static class WalkieTalkieRegistry
    {
        private static readonly List<EarshotWalkieTalkie> devices = new List<EarshotWalkieTalkie>();
        private static readonly HashSet<string> radioSpeakers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<EarshotWalkieTalkie> Devices => devices;

        public static bool LocalIsTransmitting { get; private set; }

        public static string LocalTransmitChannelId { get; private set; }

        public static float ActiveMouthDampening { get; private set; } = 0.12f;

        public static float ActiveTransmissionDelaySeconds { get; private set; } = 0.2f;

        /// <summary>Extra-Daempfung nur fuer lokales Sidetone (Feedback-Bremse).</summary>
        public static float ActiveSidetoneWorldVolume { get; private set; } = 0.4f;

        public static event Action StateChanged;

        internal static void Register(EarshotWalkieTalkie device)
        {
            if (device == null || devices.Contains(device)) return;
            devices.Add(device);
            RaiseChanged();
        }

        internal static void Unregister(EarshotWalkieTalkie device)
        {
            if (device == null) return;
            if (!devices.Remove(device)) return;

            if (LocalIsTransmitting && ReferenceEquals(device, FindTransmittingDevice()))
            {
                ClearLocalTransmit();
            }

            RaiseChanged();
        }

        internal static void NotifyChanged() => RaiseChanged();

        internal static void SetLocalTransmit(EarshotWalkieTalkie device, bool transmitting)
        {
            if (transmitting)
            {
                if (device == null || !WalkieRules.CanTransmit(device.PoweredOn, device.CanTransmit))
                {
                    return;
                }

                LocalIsTransmitting = true;
                LocalTransmitChannelId = device.ChannelId;
                ActiveMouthDampening = device.MouthVolumeWhileTransmitting;
                ActiveTransmissionDelaySeconds = device.TransmissionDelaySeconds;
                ActiveSidetoneWorldVolume = device.SidetoneWorldVolume;
            }
            else if (!transmitting && (device == null || IsSameTransmitDevice(device)))
            {
                ClearLocalTransmit();
            }

            RaiseChanged();
        }

        internal static void MarkRadioSpeaker(string playerId, bool present)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            bool changed = present
                ? radioSpeakers.Add(playerId)
                : radioSpeakers.Remove(playerId);

            if (changed) RaiseChanged();
        }

        public static bool IsRadioSpeakerPresent(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && radioSpeakers.Contains(playerId);
        }

        public static bool HasPoweredDeviceOnChannel(string channelId)
        {
            string wanted = WalkieRules.SanitizeChannelId(channelId);
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d != null && d.PoweredOn &&
                    string.Equals(d.ChannelId, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void CollectPoweredChannelIds(List<string> into)
        {
            if (into == null) return;
            into.Clear();

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || !d.PoweredOn) continue;

                string id = d.ChannelId;
                bool exists = false;
                for (int j = 0; j < into.Count; j++)
                {
                    if (string.Equals(into[j], id, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists) into.Add(id);
            }
        }

        /// <summary>
        /// Naechstes eingeschaltetes Geraet auf dem Kanal — dort kommt die Funkstimme her
        /// (Hand, Handgelenk oder Boden). Leak entsteht ueber Unity-3D-Audio.
        /// </summary>
        public static Transform GetBestReceiveAnchor(string channelId, Vector3 listenerPosition)
        {
            string wanted = WalkieRules.SanitizeChannelId(channelId);
            Transform best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || !d.PoweredOn) continue;
                if (!string.Equals(d.ChannelId, wanted, StringComparison.OrdinalIgnoreCase)) continue;

                Transform anchor = d.AudioAnchor;
                if (anchor == null) continue;

                float dist = Vector3.Distance(listenerPosition, anchor.position);
                // In der Hand leicht bevorzugen, damit das Geraet am Koerper gewinnt.
                float score = dist - (d.CanTransmit ? 0.35f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = anchor;
                }
            }

            return best;
        }

        public static EarshotWalkieTalkie GetSettingsDevice(string channelId)
        {
            string wanted = WalkieRules.SanitizeChannelId(channelId);
            EarshotWalkieTalkie held = null;
            EarshotWalkieTalkie any = null;

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || !d.PoweredOn) continue;
                if (!string.Equals(d.ChannelId, wanted, StringComparison.OrdinalIgnoreCase)) continue;

                any ??= d;
                if (d.CanTransmit)
                {
                    held = d;
                    break;
                }
            }

            return held ?? any;
        }

        /// <summary>
        /// Wenn das Spiel beim Ablegen PTT nicht sauber beendet, hier nachziehen.
        /// </summary>
        internal static void EnsureLocalTransmitStillValid()
        {
            if (!LocalIsTransmitting) return;

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d != null &&
                    d.IsTransmitting &&
                    d.PoweredOn &&
                    d.CanTransmit &&
                    string.Equals(d.ChannelId, LocalTransmitChannelId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d != null && d.IsTransmitting)
                {
                    d.SetTransmitting(false);
                }
            }

            if (LocalIsTransmitting)
            {
                ClearLocalTransmit();
                RaiseChanged();
            }
        }

        private static bool IsSameTransmitDevice(EarshotWalkieTalkie device)
        {
            return device != null &&
                   string.Equals(device.ChannelId, LocalTransmitChannelId, StringComparison.OrdinalIgnoreCase) &&
                   device.IsTransmitting;
        }

        private static EarshotWalkieTalkie FindTransmittingDevice()
        {
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d != null && d.IsTransmitting) return d;
            }

            return null;
        }

        private static void ClearLocalTransmit()
        {
            LocalIsTransmitting = false;
            LocalTransmitChannelId = null;
        }

        private static void RaiseChanged() => StateChanged?.Invoke();

        internal static void ClearForTests()
        {
            devices.Clear();
            radioSpeakers.Clear();
            ClearLocalTransmit();
            ActiveMouthDampening = 0.12f;
            ActiveTransmissionDelaySeconds = 0.2f;
            ActiveSidetoneWorldVolume = 0.35f;
        }
    }
}
