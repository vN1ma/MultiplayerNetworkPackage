using System.Collections.Generic;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Das Klangverhalten des Proximity-Chats als Asset. Hier steht, wie weit man hoert,
    /// wie oft nachgerechnet wird und welche Module in welcher Reihenfolge mitmischen.
    /// <para>
    /// Ein Spiel kann mehrere Profile haben und zur Laufzeit umschalten, etwa ein
    /// normales und eines fuer eine Traumsequenz.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Earshot Voice/Voice Profile", fileName = "VoiceProfile")]
    public class VoiceProfile : ScriptableObject
    {
        [Header("Reichweite")]
        [SerializeField, Min(1f)]
        [Tooltip("Ab dieser Entfernung in Metern ist ein Sprecher gar nicht mehr zu hoeren.")]
        private float maxHearingDistance = 25f;

        [SerializeField]
        [Tooltip("Lautstaerke ueber die Entfernung. Links = direkt daneben, rechts = Maximalentfernung.")]
        private AnimationCurve distanceFalloff = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(0.35f, 0.75f),
            new Keyframe(1f, 0f, -1.2f, 0f));

        [Header("Verdeckung")]
        [SerializeField]
        [Tooltip("Welche Layer den Schall blockieren. Typischerweise Default und eine eigene Wand-Ebene. Darf nicht leer sein, sonst blockiert nichts.")]
        private LayerMask occlusionLayers = 1;

        [SerializeField]
        [Tooltip("Auf welchen Layern die VoiceZone-Trigger liegen.")]
        private LayerMask zoneLayers = ~0;

        [Header("Raumklang")]
        [SerializeField]
        [Tooltip("Hall kostet Rechenzeit fuer jeden Sprecher einzeln. Ausschalten, wenn das Projekt keine Raeume mit Hall verwendet.")]
        private bool enableReverb = true;

        [Header("Berechnung")]
        [SerializeField, Range(4f, 60f)]
        [Tooltip("Wie oft pro Sekunde die Hoersituation neu bestimmt wird. Raycasts sind teuer, 20 bis 30 reicht.")]
        private float evaluationsPerSecond = 30f;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Zeit in Sekunden, in der die Haelfte einer Klangaenderung erreicht ist. Kleiner = schneller. Unter 0.03 beginnt es zu knacken.")]
        private float smoothingHalfLife = 0.04f;

        [Header("Module")]
        [SerializeField]
        [Tooltip("Die Klangbausteine. Reihenfolge in der Liste ist egal, sortiert wird nach dem Order-Wert des Moduls.")]
        private List<VoiceModifierAsset> modifiers = new List<VoiceModifierAsset>();

        private readonly List<IVoiceModifier> sorted = new List<IVoiceModifier>();
        private bool sortedDirty = true;

        public float MaxHearingDistance => maxHearingDistance;
        public AnimationCurve DistanceFalloff => distanceFalloff;
        public LayerMask OcclusionLayers => occlusionLayers;
        public LayerMask ZoneLayers => zoneLayers;
        public bool EnableReverb => enableReverb;
        public float EvaluationInterval => 1f / Mathf.Max(1f, evaluationsPerSecond);
        public float SmoothingHalfLife => smoothingHalfLife;

        /// <summary>
        /// Die aktiven Module, aufsteigend nach <see cref="IVoiceModifier.Order"/>.
        /// Die Liste wird zwischengespeichert und nur bei Aenderungen neu sortiert.
        /// </summary>
        public IReadOnlyList<IVoiceModifier> SortedModifiers
        {
            get
            {
                if (sortedDirty) RebuildSorted();
                return sorted;
            }
        }

        /// <summary>
        /// Fuegt zur Laufzeit ein Modul hinzu, das nicht als Asset vorliegt. Nuetzlich fuer
        /// Module, die auf Spielzustand zugreifen muessen und deshalb kein Asset sein koennen.
        /// </summary>
        public void AddRuntimeModifier(IVoiceModifier modifier)
        {
            if (modifier == null) return;
            if (sortedDirty) RebuildSorted();
            sorted.Add(modifier);
            sorted.Sort(CompareOrder);
        }

        public void RemoveRuntimeModifier(IVoiceModifier modifier)
        {
            if (modifier == null) return;
            sorted.Remove(modifier);
        }

        /// <summary>
        /// Rechnet die Entfernung in den Bereich 0..1 der Falloff-Kurve um.
        /// </summary>
        public float EvaluateDistanceFalloff(float distance)
        {
            if (maxHearingDistance <= 0f) return 0f;
            float normalized = Mathf.Clamp01(distance / maxHearingDistance);
            return Mathf.Clamp01(distanceFalloff.Evaluate(normalized));
        }

        /// <summary>
        /// Glaettungsfaktor fuer diesen Frame. Haengt nur an der Halbwertszeit und der
        /// vergangenen Zeit, ist also unabhaengig von der Bildrate.
        /// </summary>
        public float GetSmoothingFactor(float deltaTime)
        {
            if (smoothingHalfLife <= 0f) return 1f;
            return 1f - Mathf.Exp(-deltaTime * 0.6931472f / smoothingHalfLife);
        }

        private void RebuildSorted()
        {
            sorted.Clear();
            for (int i = 0; i < modifiers.Count; i++)
            {
                var m = modifiers[i];
                if (m == null) continue;
                sorted.Add(m);
            }
            sorted.Sort(CompareOrder);
            sortedDirty = false;
        }

        private static int CompareOrder(IVoiceModifier a, IVoiceModifier b)
        {
            return a.Order.CompareTo(b.Order);
        }

        private void OnValidate()
        {
            sortedDirty = true;

            if (occlusionLayers == 0)
            {
                EarshotVoiceLog.Warn(
                    $"VoiceProfile '{name}': Occlusion Layers ist leer. Damit blockiert keine " +
                    "einzige Wand den Schall. Mindestens den Default-Layer auswaehlen.");
            }
        }
    }
}
