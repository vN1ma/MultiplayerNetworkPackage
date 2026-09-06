using Earshot.Voice;
using UnityEngine;

namespace Earshot.Voice.Samples
{
    /// <summary>
    /// Beispiel fuer ein eigenes Voice-Modul: ein Funkgeraet.
    /// <para>
    /// Es laeuft als Letztes (<see cref="VoiceModifierOrder.Transmission"/>) und hebt deshalb
    /// Distanz, Waende und Tueren wieder auf. Genau das will man bei Funk: Die Stimme soll
    /// blechern und immer gleich laut sein, egal wo der andere steht.
    /// </para>
    /// <para>
    /// Verwendung: Asset anlegen ueber Create > Earshot Voice > Samples > Radio, dann in
    /// ein VoiceProfile ziehen. Zum Abschalten das Haekchen am Asset oder das Asset aus
    /// dem Profil nehmen - Paketcode aendern ist nicht noetig.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        menuName = "Earshot Voice/Samples/Radio",
        fileName = "RadioModifier")]
    public class RadioModifier : VoiceModifierAsset
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Feste Lautstaerke der Funkstimme. Unabhaengig von der Entfernung.")]
        private float radioVolume = 0.7f;

        [SerializeField, Range(100f, 4000f)]
        [Tooltip("Unteres Ende des Funkbands. Hoeher klingt duenner.")]
        private float highPassHz = 800f;

        [SerializeField, Range(1000f, 8000f)]
        [Tooltip("Oberes Ende des Funkbands. Niedriger klingt dumpfer und aelter.")]
        private float lowPassHz = 3500f;

        public override int Order => VoiceModifierOrder.Transmission;

        public override void Apply(in VoiceContext context, ref VoiceSample sample)
        {
            sample.Muted = false;
            sample.Volume = radioVolume;
            sample.SpatialBlend = 0f;
            sample.ReverbMix = 0f;
            sample.HighPassHz = highPassHz;
            sample.LowPassHz = lowPassHz;
        }
    }
}
