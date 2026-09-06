using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Eine Oeffnung im Schallschutz: Tuer, Fenster, Luke, Durchreiche.
    /// <para>
    /// Auf das GameObject mit dem Collider setzen, der die Tuer darstellt. Sobald die
    /// Sichtlinie zwischen zwei Spielern durch diesen Collider fuehrt, entscheidet
    /// <see cref="Openness"/>, wie viel Schall durchkommt - unabhaengig davon, ob die Tuer
    /// physisch aufschwingt oder nur eine Zustandsvariable umgelegt wird.
    /// </para>
    /// <para>
    /// Genau hier entsteht der abrupte Cutoff beim Zuschlagen einer Tuer: Von 1 auf 0 in
    /// dem Moment, in dem sie ins Schloss faellt. Wie hart der Uebergang klingt, bestimmt
    /// die Glaettung im <see cref="VoiceProfile"/>.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Voice Portal")]
    public class VoicePortal : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie weit die Oeffnung akustisch offen ist. 1 = voellig offen, 0 = dicht verschlossen.")]
        private float openness = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie dumpf es klingt, wenn die Oeffnung geschlossen ist. Eine Stahltuer hoeher als ein Vorhang.")]
        private float closedMuffle = 0.8f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Lautstaerke bei vollstaendig geschlossener Oeffnung noch durchdringt. 0 ergibt einen harten Cutoff.")]
        private float closedVolume = 0.15f;

        /// <summary>Akustischer Oeffnungsgrad von 0 bis 1.</summary>
        public float Openness
        {
            get => openness;
            set => openness = Mathf.Clamp01(value);
        }

        public float ClosedMuffle => closedMuffle;
        public float ClosedVolume => closedVolume;

        /// <summary>
        /// Bequemer Schalter fuer Tueren, die nur auf oder zu kennen. Fuer weiche
        /// Uebergaenge waehrend einer Tueranimation stattdessen <see cref="Openness"/>
        /// direkt setzen.
        /// </summary>
        public bool IsOpen
        {
            get => openness > 0.5f;
            set => openness = value ? 1f : 0f;
        }

        private void OnDrawGizmos()
        {
            var col = GetComponentInChildren<Collider>();
            if (col == null) return;

            // Gruen offen, rot geschlossen - im Editor auf einen Blick erkennbar.
            Gizmos.color = Color.Lerp(
                new Color(0.85f, 0.25f, 0.25f, 0.35f),
                new Color(0.3f, 0.8f, 0.4f, 0.35f),
                openness);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
