using Earshot.Voice;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Zeigt die aktuell berechneten Stimmwerte an. Ohne diese Anzeige ist jeder
    /// Klangfehler blindes Raten: Man hoert "zu dumpf" und weiss nicht, ob Distanz,
    /// Wand oder Tuer die Ursache ist.
    /// </summary>
    [AddComponentMenu("Earshot/Voice Debug Overlay")]
    public class VoiceDebugOverlay : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Overlay anzeigen. Standard aus, damit es im fertigen Spiel nicht stoert.")]
        private bool visible;

        [SerializeField]
        [Tooltip("Taste, die das Overlay ein- und ausblendet.")]
        private KeyCode toggleKey = KeyCode.F3;

        [SerializeField]
        [Tooltip("Position am Bildschirmrand in Pixeln.")]
        private Vector2 margin = new Vector2(16f, 220f);

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }
#endif
        }

        private void Awake()
        {
            // In Builds startet die Anzeige aus. F3 schaltet sie ein - auch mit einem Freund.
            if (!Application.isEditor) visible = false;
        }

        private void OnGUI()
        {
            if (!visible) return;

            const float width = 520f;
            const float height = 420f;
            GUILayout.BeginArea(new Rect(margin.x, margin.y, width, height), GUI.skin.box);
            GUILayout.Label("<b>Earshot Voice</b>", RichLabel);

            GUILayout.Label(
                $"Sitzung: {Coop.State}    " +
                $"Sprachkanal: {(CoopVoice.IsConnected ? "aktiv" : "aus")}    " +
                $"Mikrofon: {(CoopVoice.MicrophoneMuted ? "stumm" : "an")}",
                RichLabel);

            var runtime = VoiceRuntime.Instance;
            if (runtime == null || runtime.Emitters.Count == 0)
            {
                GUILayout.Label("Keine empfangenen Stimmen.", RichLabel);
            }
            else
            {
                for (int i = 0; i < runtime.Emitters.Count; i++)
                {
                    var emitter = runtime.Emitters[i];
                    if (emitter == null) continue;

                    string name = emitter.PlayerId;
                    if (PlayerRegistry.TryGetByUgsId(emitter.PlayerId, out var player) && player != null)
                    {
                        name = player.DisplayName;
                    }

                    var sample = emitter.Current;
                    var context = emitter.LastContext;

                    GUILayout.Space(6f);
                    GUILayout.Label($"<b>{name}</b>", RichLabel);
                    GUILayout.Label(
                        $"  Distanz {context.Distance:0.0} m    " +
                        $"Lautstaerke {sample.Volume:0.00}    " +
                        $"Tiefpass {sample.LowPassHz:0} Hz",
                        RichLabel);
                    GUILayout.Label(
                        $"  Hall {sample.ReverbMix:0.00}    " +
                        $"Portal {(context.HasPortal ? $"ja ({context.PortalOpenness:0.00})" : "nein")}    " +
                        $"Wand {context.OcclusionAmount:0.00}",
                        RichLabel);
                    GUILayout.Label(
                        $"  Zuhoerer {(context.ListenerZone != null ? context.ListenerZone.ZoneName : "kein Raum")}    " +
                        $"Sprecher {(context.SpeakerZone != null ? context.SpeakerZone.ZoneName : "kein Raum")}",
                        RichLabel);
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("<b>Log (dieser Rechner = wen ICH hoere)</b>", RichLabel);
            if (!string.IsNullOrEmpty(VoiceSessionLog.FilePath))
            {
                GUILayout.Label(VoiceSessionLog.FilePath, RichLabel);
            }

            var lines = VoiceSessionLog.Recent;
            int start = lines.Count > 12 ? lines.Count - 12 : 0;
            for (int i = start; i < lines.Count; i++)
            {
                GUILayout.Label(lines[i], RichLabel);
            }

            GUILayout.EndArea();
        }

        private static GUIStyle richLabel;

        private static GUIStyle RichLabel =>
            richLabel ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
    }
}
