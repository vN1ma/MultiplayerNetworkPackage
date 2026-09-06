using UnityEngine;

namespace Earshot.Voice.Modifiers
{
    /// <summary>
    /// Die Grundlage jedes Proximity-Chats: Je weiter weg, desto leiser.
    /// <para>
    /// Die Kurve selbst steht im <see cref="VoiceProfile"/>, weil auch andere Teile des
    /// Systems die Hoerweite kennen muessen. Dieses Modul entscheidet nur, was daraus
    /// klanglich folgt - und fuegt die Beobachtung hinzu, dass entfernte Stimmen nicht nur
    /// leiser, sondern auch dumpfer werden, weil Luft hohe Frequenzen staerker schluckt.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Voice/Voice Modifiers/Distance Falloff",
        fileName = "DistanceFalloff")]
    public class DistanceFalloffModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie stark entfernte Stimmen zusaetzlich dumpfer werden. 0 schaltet den Effekt ab.")]
        private float airAbsorption = 0.35f;

        [SerializeField, Range(1000f, 22000f)]
        [Tooltip("Tiefpass an der Hoergrenze, wenn Luftabsorption voll wirkt.")]
        private float distantCutoffHz = 4000f;

        [SerializeField, Range(10f, 2000f)]
        [Tooltip("Hochpass in der Ferne. 10 = aus.")]
        private float distantHighPassHz = 10f;

        public override int Order => VoiceModifierOrder.Distance;

        public void Configure(VoiceHearingTuning tuning)
        {
            if (tuning == null) return;
            airAbsorption = tuning.airAbsorption;
            distantCutoffHz = tuning.distantCutoffHz;
            distantHighPassHz = tuning.distantHighPassHz;
        }

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (context.Profile == null) return;

            float range = context.HearingDistance > 0f ? context.HearingDistance : context.Distance;
            sample.Volume *= context.Profile.EvaluateDistanceFalloff(range);

            float t = context.Profile.MaxHearingDistance > 0f
                ? Mathf.Clamp01(range / context.Profile.MaxHearingDistance)
                : 1f;

            if (airAbsorption > 0f)
            {
                float cutoff = Mathf.Lerp(VoiceSample.NoLowPass, distantCutoffHz, t * airAbsorption);
                sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
            }

            if (distantHighPassHz > VoiceSample.NoHighPass)
            {
                float high = Mathf.Lerp(VoiceSample.NoHighPass, distantHighPassHz, t);
                sample.HighPassHz = Mathf.Max(sample.HighPassHz, high);
            }
        }
    }
}
