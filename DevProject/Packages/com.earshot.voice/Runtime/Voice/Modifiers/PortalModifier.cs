using UnityEngine;

namespace Earshot.Voice.Modifiers
{
    /// <summary>
    /// Tueren, Fenster und Luken.
    /// <para>
    /// Dieses Modul erzeugt den Effekt, um den es urspruenglich ging: Eine Tuer faellt
    /// zu, und die Stimme dahinter bricht ab oder wird schlagartig dumpf. Wie hart dieser
    /// Uebergang ausfaellt, entscheiden zwei Werte an unterschiedlichen Stellen - was
    /// durchkommt, steht am <see cref="VoicePortal"/> selbst, wie schnell es umschaltet,
    /// steht als Glaettung im <see cref="VoiceProfile"/>.
    /// </para>
    /// <para>
    /// Steht eine Tuer offen, greift dieses Modul gar nicht ein. Das ist Absicht: Eine
    /// offene Tuer soll klingen, als waere dort nichts.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Voice/Voice Modifiers/Portal",
        fileName = "Portal")]
    public class PortalModifier : VoiceModifierAsset
    {
        [SerializeField]
        [Tooltip("Kurve vom geschlossenen (links) zum offenen Zustand (rechts). Eine steile Kurve laesst eine Tuer schon beim Anlehnen dicht wirken.")]
        private AnimationCurve opennessResponse = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1.6f),
            new Keyframe(1f, 1f, 0.4f, 0f));

        public override int Order => VoiceModifierOrder.Portal;

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            if (!context.HasPortal || context.Portal == null) return;

            float openness = Mathf.Clamp01(opennessResponse.Evaluate(
                Mathf.Clamp01(context.PortalOpenness)));

            if (openness >= 1f) return;

            var portal = context.Portal;

            // Bei openness = 1 bleibt alles unveraendert, bei 0 gelten die Werte des
            // Portals. Dazwischen wird linear ueberblendet.
            sample.Volume *= Mathf.Lerp(portal.ClosedVolume, 1f, openness);

            float muffle = portal.ClosedMuffle * (1f - openness);
            if (muffle <= 0f) return;

            // Der Muffle-Regler von 0 bis 1 wird logarithmisch auf eine Frequenz
            // abgebildet, damit die obere Haelfte des Reglers noch hoerbar etwas tut.
            float cutoff = Mathf.Exp(Mathf.Lerp(
                Mathf.Log(VoiceSample.NoLowPass), Mathf.Log(300f), muffle));

            sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
        }
    }
}
