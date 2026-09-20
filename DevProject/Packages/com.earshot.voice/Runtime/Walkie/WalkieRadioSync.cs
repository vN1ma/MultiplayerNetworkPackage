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

                    // v18: Vivox-Funkkanal-Roster entfaellt (kein Join mehr,
                    // siehe VivoxVoiceBackend.EnsureRadioChannelAsync). Die
                    // Diagnose gegen 'Stimme ueberall gleich laut' uebernimmt
                    // der REMOTE FEED im Session-Log: Er zeigt, welcher Spieler
                    // mit welchem Peak auf dem Bus liegt.
                    VoiceSessionLog.Note(
                        $"WALKIE SYNC r{revision}: keine Vivox-Funkkanal-Roster mehr (v18).");
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
                // remote-hunt-v16.9: Frueher NUR Konsole — die Session-Log-Datei
                // blieb stumm, obwohl genau hier ein gescheiterter TX-Wechsel
                // vorbeikommen kann (Beweis-Kette Freund-Session 20260920-005940).
                VoiceSessionLog.Alert(
                    $"WALKIE SYNC FEHLGESCHLAGEN: {ex.GetType().Name}: {ex.Message} " +
                    "(Kanal-Join/Leave oder TX-Wechsel abgebrochen — Sendung/Empfang kann leiden).");
            }
            finally
            {
                syncRunning = false;
                if (syncRequested && isActiveAndEnabled) _ = SyncAsync();
            }
        }
    }
}
