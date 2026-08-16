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
        menuName = "Earshot/Voice Modifiers/Distance Falloff",
        fileName = "DistanceFalloff")]
    public class DistanceFalloffModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie stark entfernte Stimmen zusaetzlich dumpfer werden. 0 schaltet den Effekt ab.")]
        private float airAbsorption = 0.35f;

        [SerializeField, Range(1000f, 22000f)]
        [Tooltip("Tiefpass an der Hoergrenze, wenn Luftabsorption voll wirkt.")]
        private float distantCutoffHz = 4000f;

        public override int Order => VoiceModifierOrder.Distance;

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (context.Profile == null) return;

            sample.Volume *= context.Profile.EvaluateDistanceFalloff(context.Distance);

            if (airAbsorption <= 0f) return;

            float t = Mathf.Clamp01(context.Distance / context.Profile.MaxHearingDistance);
            float cutoff = Mathf.Lerp(VoiceSample.NoLowPass, distantCutoffHz, t * airAbsorption);
            sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
        }
    }
}
