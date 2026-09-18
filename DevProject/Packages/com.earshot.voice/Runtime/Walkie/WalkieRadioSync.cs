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
        private bool syncRequested;
        private int requestedRevision;
        private int appliedRevision;

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
            syncRequested = true;
            requestedRevision++;
            if (syncRunning) return;
            _ = SyncAsync();
        }

        private async Task SyncAsync()
        {
            if (syncRunning) return;
            syncRunning = true;

            try
            {
                do
                {
                    int revision = requestedRevision;
                    syncRequested = false;
                    var timer = System.Diagnostics.Stopwatch.StartNew();

                    // Kurze Pause, damit mehrere Inspector-/Input-Aenderungen zusammenfallen.
                    await Task.Yield();

                    var backend = VoiceRuntime.Instance != null
                        ? VoiceRuntime.Instance.Backend as IVoiceRadioBackend
                        : null;
                    if (backend == null)
                    {
                        VoiceSessionLog.Note($"WALKIE SYNC r{revision}: Backend noch nicht bereit.");
                        continue;
                    }

                    WalkieTalkieRegistry.CollectPoweredChannelIds(wantedChannels);

                    joinedScratch.Clear();
                    backend.CopyJoinedRadioChannels(joinedScratch);
                    bool transmitting = WalkieTalkieRegistry.LocalIsTransmitting;
                    string transmitChannel = WalkieTalkieRegistry.LocalTransmitChannelId;
                    VoiceSessionLog.Note(
                        $"WALKIE SYNC start r{revision}: poweredChannels={wantedChannels.Count}, " +
                        $"joinedChannels={joinedScratch.Count}, tx={transmitting}, " +
                        $"txChannel='{transmitChannel}'");

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

                    // leak-hunt-v13: Funkkanal-Teilnehmer offengelegen. Ein stiller
                    // zweiter Client im Funkkanal ist im restlichen Log unsichtbar
                    // (er redet nicht, ist nicht im Proximity-Roster), spielt die
                    // eigene Sendung aber an SEINEN Walkies ab - aus Sicht dieses
                    // Spielers waere das eine Stimme mit Walkie-Effekt und konstant
                    // bleibender Lautstaerke, unabhaengig vom eigenen Standort.
                    var participantScratch = new List<string>(8);
                    for (int i = 0; i < wantedChannels.Count; i++)
                    {
                        participantScratch.Clear();
                        backend.CopyRadioChannelParticipantIds(wantedChannels[i], participantScratch);

                        if (participantScratch.Count > 1)
                        {
                            VoiceSessionLog.Alert(
                                $"WALKIE RADIO KANAL '{wantedChannels[i]}': {participantScratch.Count} Teilnehmer " +
                                $"[{string.Join(", ", participantScratch)}] - ZWEITER CLIENT IM KANAL! " +
                                "Dessen Walkies spielen die eigene Sendung ab: Hauptverdacht fuer " +
                                "'Stimme ueberall gleich laut' ohne 3D-Rolloff.");
                        }
                        else
                        {
                            VoiceSessionLog.Note(
                                $"WALKIE RADIO KANAL '{wantedChannels[i]}': {participantScratch.Count} Teilnehmer " +
                                $"[{string.Join(", ", participantScratch)}] - nur ich hier. Eine jetzt " +
                                "hoerbare zweite Stimme mit Funk-Effekt kommt NICHT von einem anderen Client.");
                        }
                    }

                    appliedRevision = revision;
                    timer.Stop();
                    VoiceSessionLog.Note(
                        $"WALKIE SYNC fertig r{revision}: {timer.ElapsedMilliseconds} ms, " +
                        $"nachlauf={requestedRevision != revision}, applied={appliedRevision}");
                } while (syncRequested && isActiveAndEnabled);
            }
            catch (System.Exception ex)
            {
                EarshotVoiceLog.Exception("Walkie-Funkkanal-Sync fehlgeschlagen", ex);
            }
            finally
            {
                syncRunning = false;
                if (syncRequested && isActiveAndEnabled) _ = SyncAsync();
            }
        }
    }
}
