using UnityEngine;

namespace Earshot.Voice.Modifiers
{
    /// <summary>
    /// Daempfung durch feste Geometrie: Waende, Boeden, Container.
    /// <para>
    /// Eine Wand macht eine Stimme nicht einfach leiser, sie macht sie vor allem dumpf.
    /// Hohe Frequenzen bleiben im Mauerwerk haengen, tiefe kommen durch. Genau deshalb
    /// klingt jemand hinter einer Wand nach Brummen und nicht nach leisem Fluestern -
    /// und deshalb greift dieses Modul staerker am Tiefpass als an der Lautstaerke.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Voice/Voice Modifiers/Occlusion",
        fileName = "Occlusion")]
    public class OcclusionModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Lautstaerke bei vollstaendiger Verdeckung uebrig bleibt.")]
        private float occludedVolume = 0.22f;

        [SerializeField, Range(100f, 22000f)]
        [Tooltip("Tiefpass bei vollstaendiger Verdeckung. Etwa 500 Hz klingt nach massiver Wand.")]
        private float occludedCutoffHz = 500f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Hall eine Verdeckung hinzufuegt. Verdeckter Schall kommt ueber Umwege und klingt dadurch diffuser.")]
        private float occludedReverb = 0.2f;

        public override int Order => VoiceModifierOrder.Occlusion;

        public void Configure(VoiceHearingTuning tuning)
        {
            if (tuning == null) return;
            SetEnabled(tuning.enableWallMuffle);
            occludedVolume = tuning.occludedVolume;
            occludedCutoffHz = tuning.occludedCutoffHz;
            occludedReverb = tuning.occludedReverb;
        }

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (context.UsedGraph) return;

            float amount = Mathf.Clamp01(context.OcclusionAmount);
            if (amount <= 0f) return;

            sample.Volume *= Mathf.Lerp(1f, occludedVolume, amount);

            float cutoff = Mathf.Lerp(VoiceSample.NoLowPass, occludedCutoffHz, amount);
            sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);

            sample.ReverbMix = Mathf.Max(sample.ReverbMix, occludedReverb * amount);
        }
    }
}
