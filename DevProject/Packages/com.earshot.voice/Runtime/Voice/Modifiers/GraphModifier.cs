using UnityEngine;

namespace Earshot.Voice.Modifiers
{
    /// <summary>
    /// Wendet den Raum-Portal-Graph an: geschlossene Tueren auf dem Umweg machen
    /// dumpf und leiser. Die Weglaenge selbst steckt schon in
    /// <see cref="VoiceContext.HearingDistance"/> — die rechnet das Distanz-Modul.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Voice/Voice Modifiers/Graph",
        fileName = "Graph")]
    public class GraphModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Lautstaerke bei vollstaendig geschlossenem Graph-Weg uebrig bleibt.")]
        private float closedVolume = 0.18f;

        [SerializeField, Range(100f, 22000f)]
        [Tooltip("Tiefpass, wenn alle Tueren auf dem Weg zu sind.")]
        private float closedCutoffHz = 700f;

        public override int Order => VoiceModifierOrder.Graph;

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (!context.UsedGraph) return;

            float closed = Mathf.Clamp01(context.GraphClosedness);
            if (closed <= 0f) return;

            sample.Volume *= Mathf.Lerp(1f, closedVolume, closed);

            float cutoff = Mathf.Lerp(VoiceSample.NoLowPass, closedCutoffHz, closed);
            sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
        }
    }
}
