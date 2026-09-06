using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Haelt Vivox-Funkkanaele synchron zu eingeschalteten Walkies und steuert Half-Duplex-Senden.
    /// Entsteht automatisch an der Voice-Runtime.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class WalkieRadioSync : MonoBehaviour
    {
        private readonly List<string> wantedChannels = new List<string>(4);
        private readonly List<string> joinedScratch = new List<string>(4);
        private bool syncRunning;

        internal static WalkieRadioSync EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var sync = runtime.GetComponent<WalkieRadioSync>();
            if (sync == null) sync = runtime.gameObject.AddComponent<WalkieRadioSync>();
            return sync;
        }

        private void OnEnable()
        {
            WalkieTalkieRegistry.StateChanged += OnWalkieStateChanged;
            QueueSync();
        }

        private void OnDisable()
        {
            WalkieTalkieRegistry.StateChanged -= OnWalkieStateChanged;
        }

        private void OnWalkieStateChanged() => QueueSync();

        private void QueueSync()
        {
            if (!isActiveAndEnabled) return;
            _ = SyncAsync();
        }

        private async Task SyncAsync()
        {
            if (syncRunning) return;
            syncRunning = true;

            try
            {
                // Kurze Pause, damit mehrere Inspector-/Input-Aenderungen zusammenfallen.
                await Task.Yield();

                var backend = VoiceRuntime.Instance != null
                    ? VoiceRuntime.Instance.Backend as IVoiceRadioBackend
                    : null;
                if (backend == null) return;

                WalkieTalkieRegistry.CollectPoweredChannelIds(wantedChannels);

                joinedScratch.Clear();
                backend.CopyJoinedRadioChannels(joinedScratch);

                for (int i = 0; i < wantedChannels.Count; i++)
                {
                    string id = wantedChannels[i];
                    if (!backend.IsRadioChannelJoined(id))
                    {
                        await backend.EnsureRadioChannelAsync(id);
                    }
                }

                for (int i = 0; i < joinedScratch.Count; i++)
                {
                    string id = joinedScratch[i];
                    bool stillWanted = false;
                    for (int j = 0; j < wantedChannels.Count; j++)
                    {
                        if (string.Equals(wantedChannels[j], id, System.StringComparison.OrdinalIgnoreCase))
                        {
                            stillWanted = true;
                            break;
                        }
                    }

                    if (!stillWanted)
                    {
                        await backend.LeaveRadioChannelAsync(id);
                    }
                }

                if (WalkieTalkieRegistry.LocalIsTransmitting &&
                    !string.IsNullOrEmpty(WalkieTalkieRegistry.LocalTransmitChannelId))
                {
                    await backend.SetRadioTransmittingAsync(
                        WalkieTalkieRegistry.LocalTransmitChannelId, true);
                }
                else
                {
                    await backend.SetRadioTransmittingAsync(null, false);
                }
            }
            catch (System.Exception ex)
            {
                EarshotVoiceLog.Exception("Walkie-Funkkanal-Sync fehlgeschlagen", ex);
            }
            finally
            {
                syncRunning = false;
            }
        }
    }
}
