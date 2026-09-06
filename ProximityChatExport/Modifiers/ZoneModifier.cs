using UnityEngine;

namespace Earshot.Proximity.Modifiers
{
    /// <summary>
    /// Raumakustik: Hall im Badezimmer, Daempfung im Teppichflur, gedaempfte Stimmen
    /// ueber Raumgrenzen hinweg.
    /// <para>
    /// Der Hall richtet sich nach dem Raum des <b>Sprechers</b>, nicht nach dem des
    /// Zuhoerers. Das entspricht dem Hoereindruck: Wer aus dem gefliesten Bad ruft, klingt
    /// hallig - auch fuer jemanden, der im Wohnzimmer steht.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Proximity/Voice Modifiers/Zone",
        fileName = "Zone")]
    public class ZoneModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Gesamtstaerke der Zonenwirkung. Zum Abschwaechen, ohne jede einzelne Zone anzufassen.")]
        private float intensity = 1f;

        [SerializeField, Range(100f, 22000f)]
        [Tooltip("Tiefpass bei maximaler Daempfung ueber eine Raumgrenze hinweg.")]
        private float crossZoneCutoffHz = 1200f;

        public override int Order => VoiceModifierOrder.Zone;

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (intensity <= 0f) return;

            var speakerZone = context.SpeakerZone;

            if (speakerZone != null)
            {
                sample.ReverbMix = Mathf.Max(
                    sample.ReverbMix, speakerZone.Reverb * intensity);

                sample.Volume *= Mathf.Lerp(1f, 1f - speakerZone.Absorption, intensity);
            }

            if (context.SameZone) return;

            // Ueber eine Raumgrenze hinweg zaehlt der Raum des Sprechers; hat er keinen,
            // greift ersatzweise der des Zuhoerers. So wirkt die Grenze auch dann, wenn
            // nur eine der beiden Seiten als Raum definiert wurde.
            var boundary = speakerZone != null ? speakerZone : context.ListenerZone;
            if (boundary == null) return;

            sample.Volume *= Mathf.Lerp(1f, boundary.CrossZoneVolume, intensity);

            float muffle = boundary.CrossZoneMuffle * intensity;
            if (muffle <= 0f) return;

            float cutoff = Mathf.Lerp(VoiceSample.NoLowPass, crossZoneCutoffHz, muffle);
            sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
        }
    }
}
