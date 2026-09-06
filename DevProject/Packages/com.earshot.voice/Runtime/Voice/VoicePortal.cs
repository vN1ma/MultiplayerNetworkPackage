using UnityEngine;

namespace Earshot.Voice
{
    public enum VoicePortalKind
    {
        Opening = 0,
        Stair = 1
    }

    /// <summary>
    /// Eine Oeffnung im Schallschutz: Tuer, Fenster, Luke, Durchreiche, Treppe.
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
        [SerializeField]
        [Tooltip("Opening = Tuer/Fenster. Stair = Treppenlauf zwischen zwei Etagen.")]
        private VoicePortalKind kind = VoicePortalKind.Opening;

        [SerializeField, Min(0f)]
        [Tooltip("Akustische Laenge in Metern. 0 = automatisch aus den Raumzentren. Bei Treppen die Lauflaenge eintragen.")]
        private float travelLength;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie weit die Oeffnung akustisch offen ist. 1 = voellig offen, 0 = dicht verschlossen.")]
        private float openness = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie dumpf es klingt, wenn die Oeffnung geschlossen ist. Eine Stahltuer hoeher als ein Vorhang.")]
        private float closedMuffle = 0.8f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Wie viel Lautstaerke bei vollstaendig geschlossener Oeffnung noch durchdringt. 0 ergibt einen harten Cutoff.")]
        private float closedVolume = 0.15f;

        public VoicePortalKind Kind => kind;
        public float TravelLength => travelLength;
        public bool HasTravelLength => travelLength > 0.01f;

        public void SetKind(VoicePortalKind value)
        {
            if (kind == value) return;
            kind = value;
            VoiceGraph.MarkDirty();
        }

        public void SetTravelLength(float meters)
        {
            float clamped = Mathf.Max(0f, meters);
            if (Mathf.Abs(clamped - travelLength) < 0.001f) return;
            travelLength = clamped;
            VoiceGraph.MarkDirty();
        }

        /// <summary>Akustischer Oeffnungsgrad von 0 bis 1.</summary>
        public float Openness
        {
            get => openness;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Abs(clamped - openness) < 0.001f) return;
                openness = clamped;
                VoiceGraph.MarkDirty();
            }
        }

        private void OnValidate()
        {
            travelLength = Mathf.Max(0f, travelLength);
            VoiceGraph.MarkDirty();
        }

        private void OnEnable()
        {
            VoiceGraph.MarkDirty();
        }

        private void OnDisable()
        {
            VoiceGraph.MarkDirty();
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
            set => Openness = value ? 1f : 0f;
        }

        private void OnDrawGizmos()
        {
            var col = GetComponentInChildren<Collider>();
            if (col == null) return;

            Color closed = kind == VoicePortalKind.Stair
                ? new Color(0.2f, 0.45f, 0.85f, 0.35f)
                : new Color(0.85f, 0.25f, 0.25f, 0.35f);
            Color open = kind == VoicePortalKind.Stair
                ? new Color(0.35f, 0.75f, 0.95f, 0.4f)
                : new Color(0.3f, 0.8f, 0.4f, 0.35f);
            Gizmos.color = Color.Lerp(closed, open, openness);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
