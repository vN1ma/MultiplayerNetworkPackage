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
        [SerializeField, Min(0f)]
        [Tooltip("Bis hierher volle Lautstaerke.")]
        private float nearDistance = 1.5f;

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

        [SerializeField, Range(0.5f, 4f)]
        [Tooltip("Nach so vielen massiven Waenden gilt der Weg als voll blockiert.")]
        private float wallsUntilFullMuffle = 1f;

        [Header("Raumklang")]
        [SerializeField]
        [Tooltip("Hall kostet Rechenzeit fuer jeden Sprecher einzeln. Ausschalten, wenn das Projekt keine Raeume mit Hall verwendet.")]
        private bool enableReverb = true;

        [SerializeField, Range(0f, 1f)]
        private float spatialBlend = 1f;

        [SerializeField, Range(0f, 1f)]
        private float farSpatialBlend = 1f;

        [SerializeField, Range(0f, 1f)]
        private float maxReverbMix = 1f;

        [SerializeField, Range(80f, 2000f)]
        private float minLowPassHz = 80f;

        [Header("Berechnung")]
        [SerializeField, Range(4f, 60f)]
        [Tooltip("Wie oft pro Sekunde die Hoersituation neu bestimmt wird. Raycasts sind teuer, 20 bis 30 reicht.")]
        private float evaluationsPerSecond = 30f;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Zeit in Sekunden, in der die Haelfte einer Lautstaerke-Aenderung erreicht ist.")]
        private float smoothingHalfLife = 0.04f;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Zeit in Sekunden, in der die Haelfte einer Filter-Aenderung (Dumpf/Hall) erreicht ist.")]
        private float filterSmoothingHalfLife = 0.06f;

        [Header("Module")]
        [SerializeField]
        [Tooltip("Die Klangbausteine. Reihenfolge in der Liste ist egal, sortiert wird nach dem Order-Wert des Moduls.")]
        private List<VoiceModifierAsset> modifiers = new List<VoiceModifierAsset>();

        private readonly List<IVoiceModifier> sorted = new List<IVoiceModifier>();
        private bool sortedDirty = true;

        public float NearDistance => nearDistance;
        public float MaxHearingDistance => maxHearingDistance;
        public AnimationCurve DistanceFalloff => distanceFalloff;
        public LayerMask OcclusionLayers => occlusionLayers;
        public LayerMask ZoneLayers => zoneLayers;
        public bool EnableReverb => enableReverb;
        public float WallsUntilFullMuffle => wallsUntilFullMuffle;
        public float SpatialBlend => spatialBlend;
        public float FarSpatialBlend => farSpatialBlend;
        public float MaxReverbMix => maxReverbMix;
        public float MinLowPassHz => minLowPassHz;
        public float EvaluationInterval => 1f / Mathf.Max(1f, evaluationsPerSecond);
        public float SmoothingHalfLife => smoothingHalfLife;
        public float FilterSmoothingHalfLife => filterSmoothingHalfLife;

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
            if (distance <= nearDistance) return 1f;
            float span = Mathf.Max(0.01f, maxHearingDistance - nearDistance);
            float normalized = Mathf.Clamp01((distance - nearDistance) / span);
            return Mathf.Clamp01(distanceFalloff.Evaluate(normalized));
        }

        /// <summary>
        /// Glaettungsfaktor fuer diesen Frame. Haengt nur an der Halbwertszeit und der
        /// vergangenen Zeit, ist also unabhaengig von der Bildrate.
        /// </summary>
        public float GetSmoothingFactor(float deltaTime)
        {
            return HalfLifeFactor(deltaTime, smoothingHalfLife);
        }

        public float GetFilterSmoothingFactor(float deltaTime)
        {
            return HalfLifeFactor(deltaTime, filterSmoothingHalfLife);
        }

        public void ApplyTuning(VoiceHearingTuning tuning)
        {
            if (tuning == null) return;
            tuning.Clamp();
            nearDistance = tuning.nearDistance;
            maxHearingDistance = tuning.maxHearingDistance;
            if (tuning.distanceFalloff != null) distanceFalloff = tuning.distanceFalloff;
            occlusionLayers = tuning.occlusionLayers;
            zoneLayers = tuning.zoneLayers;
            enableReverb = tuning.enableReverb;
            wallsUntilFullMuffle = tuning.wallsUntilFullMuffle;
            spatialBlend = tuning.spatialBlend;
            farSpatialBlend = tuning.farSpatialBlend;
            maxReverbMix = tuning.maxReverbMix;
            minLowPassHz = tuning.minLowPassHz;
            evaluationsPerSecond = tuning.evaluationsPerSecond;
            smoothingHalfLife = tuning.volumeSmoothingHalfLife;
            filterSmoothingHalfLife = tuning.filterSmoothingHalfLife;
        }

        /// <summary>
        /// Raeumlichkeit und harte Grenzen nach der Modifier-Kette.
        /// </summary>
        public void ApplyHearingLimits(float distance, ref VoiceSample sample)
        {
            float t = 0f;
            if (maxHearingDistance > nearDistance && distance > nearDistance)
            {
                t = Mathf.Clamp01((distance - nearDistance) / (maxHearingDistance - nearDistance));
            }

            sample.SpatialBlend = Mathf.Lerp(spatialBlend, farSpatialBlend, t);
            sample.ReverbMix = Mathf.Min(sample.ReverbMix, maxReverbMix);
            if (sample.LowPassHz < minLowPassHz) sample.LowPassHz = minLowPassHz;
        }

        private static float HalfLifeFactor(float deltaTime, float halfLife)
        {
            if (halfLife <= 0f) return 1f;
            return 1f - Mathf.Exp(-deltaTime * 0.6931472f / halfLife);
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
