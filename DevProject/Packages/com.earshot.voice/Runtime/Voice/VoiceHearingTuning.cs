using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Alle Hoer-Regler am Player. Wirkt lokal: so klingen die anderen fuer dich.
    /// </summary>
    [System.Serializable]
    public sealed class VoiceHearingTuning
    {
        [Header("Distanz")]
        [Min(0f)]
        [Tooltip("Bis hierher volle Lautstaerke, danach faellt die Kurve ab.")]
        public float nearDistance = 1.5f;

        [Min(1f)]
        [Tooltip("Ab dieser Entfernung in Metern ist niemand mehr zu hoeren.")]
        public float maxHearingDistance = 25f;

        [Tooltip("Lautstaerke von nah (links) nach weit (rechts).")]
        public AnimationCurve distanceFalloff = DefaultDistanceFalloff();

        [Range(0f, 1f)]
        [Tooltip("Ferne Stimmen werden zusaetzlich dumpfer (Luft).")]
        public float airAbsorption = 0.35f;

        [Range(200f, 22000f)]
        [Tooltip("Tiefpass an der Hoergrenze, wenn Luftabsorption voll wirkt.")]
        public float distantCutoffHz = 4000f;

        [Range(10f, 2000f)]
        [Tooltip("Hochpass in der Ferne. 10 = aus. Hoeher = duenner, blecherner.")]
        public float distantHighPassHz = 10f;

        [Range(0f, 1f)]
        [Tooltip("Gesamtlautstaerke der gehoerten Stimmen.")]
        public float heardVolume = 1f;

        [Range(0f, 1f)]
        [Tooltip("0 = Stimme im Kopf (2D), 1 = voll aus der Welt (3D).")]
        public float spatialBlend = 1f;

        [Range(0f, 1f)]
        [Tooltip("Raeumlichkeit an der Hoergrenze. Kleiner als Spatial Blend = ferne Stimmen ruecken ins Stereo.")]
        public float farSpatialBlend = 1f;

        [Header("Waende / Dumpf")]
        [Tooltip("Waende machen Stimmen dumpf und leiser.")]
        public bool enableWallMuffle = true;

        [Tooltip("Welche Layer als Wand zaehlen.")]
        public LayerMask occlusionLayers = 1;

        [Range(0f, 1f)]
        [Tooltip("Wieviel Lautstaerke durch eine volle Wand bleibt.")]
        public float occludedVolume = 0.22f;

        [Range(80f, 8000f)]
        [Tooltip("Tiefpass hinter einer Wand. Niedriger = dumpfer.")]
        public float occludedCutoffHz = 500f;

        [Range(0f, 1f)]
        [Tooltip("Etwas Hall dazu, wenn die Stimme durch die Wand kommt.")]
        public float occludedReverb = 0.2f;

        [Range(0.5f, 4f)]
        [Tooltip("Nach so vielen massiven Waenden gilt der Weg als voll blockiert.")]
        public float wallsUntilFullMuffle = 1f;

        [Header("Tueren")]
        [Tooltip("Tueren oeffnen den Weg wieder. Offen klingt wie nichts dazwischen.")]
        public bool enableDoors = true;

        [Tooltip("Wie Openness auf den Klang gemappt wird. Links = zu, rechts = offen.")]
        public AnimationCurve doorOpennessResponse = DefaultDoorOpenness();

        [Range(80f, 4000f)]
        [Tooltip("Tiefpass bei voll geschlossener Tuer (neben den Werten am Portal selbst).")]
        public float doorClosedCutoffHz = 300f;

        [Header("Umweg (Graph)")]
        [Tooltip("Umweg durch Raeume und Tueren, wenn die Sichtlinie blockiert ist.")]
        public bool enableGraphPath = true;

        [Range(0f, 1f)]
        [Tooltip("Lautstaerke, wenn der Graph-Weg durch geschlossene Tueren geht.")]
        public float graphClosedVolume = 0.18f;

        [Range(80f, 8000f)]
        [Tooltip("Tiefpass auf einem geschlossenen Graph-Weg.")]
        public float graphClosedCutoffHz = 700f;

        [Header("Hall und Raeume")]
        [Tooltip("Hallfilter an den Stimmen. Kostet etwas CPU.")]
        public bool enableReverb = true;

        [Tooltip("Raumzonen: Hall im Raum, Daempfung ueber die Grenze.")]
        public bool enableZones = true;

        [Range(0f, 1f)]
        [Tooltip("Staerke der Zonen (Hall im Raum, Daempfung ueber die Grenze).")]
        public float zoneIntensity = 1f;

        [Range(80f, 8000f)]
        [Tooltip("Tiefpass, wenn Sprecher und Zuhoerer in verschiedenen Zonen sind (ohne Graph).")]
        public float crossZoneCutoffHz = 1200f;

        [Tooltip("Layer der VoiceZone-Trigger.")]
        public LayerMask zoneLayers = ~0;

        [Range(0f, 1f)]
        [Tooltip("Obergrenze fuer Hall, egal was Raum oder Wand dazu addiert.")]
        public float maxReverbMix = 1f;

        [Range(80f, 2000f)]
        [Tooltip("Tiefpass wird nie tiefer als das. Verhindert voellig verstummte Dumpfheit.")]
        public float minLowPassHz = 80f;

        [Header("Uebergaenge")]
        [Range(0.01f, 1f)]
        [Tooltip("Wie schnell die Lautstaerke nachzieht. Kleiner = haerterer Cutoff.")]
        public float volumeSmoothingHalfLife = 0.04f;

        [Range(0.01f, 1f)]
        [Tooltip("Wie weich Tiefpass/Hall umschaltet (Tuer zu, hinter die Wand). Kleiner = knackiger.")]
        public float filterSmoothingHalfLife = 0.06f;

        [Range(4f, 60f)]
        [Tooltip("Wie oft pro Sekunde Distanz und Waende neu gemessen werden.")]
        public float evaluationsPerSecond = 30f;

        public static AnimationCurve DefaultDistanceFalloff()
        {
            return new AnimationCurve(
                new Keyframe(0f, 1f, 0f, 0f),
                new Keyframe(0.35f, 0.75f),
                new Keyframe(1f, 0f, -1.2f, 0f));
        }

        public static AnimationCurve DefaultDoorOpenness()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 1.6f),
                new Keyframe(1f, 1f, 0.4f, 0f));
        }

        public void Clamp()
        {
            nearDistance = Mathf.Max(0f, nearDistance);
            maxHearingDistance = Mathf.Max(nearDistance + 0.1f, maxHearingDistance);
            if (distanceFalloff == null) distanceFalloff = DefaultDistanceFalloff();
            if (doorOpennessResponse == null) doorOpennessResponse = DefaultDoorOpenness();
            airAbsorption = Mathf.Clamp01(airAbsorption);
            distantCutoffHz = Mathf.Clamp(distantCutoffHz, 200f, 22000f);
            distantHighPassHz = Mathf.Clamp(distantHighPassHz, 10f, 2000f);
            heardVolume = Mathf.Clamp01(heardVolume);
            spatialBlend = Mathf.Clamp01(spatialBlend);
            farSpatialBlend = Mathf.Clamp01(farSpatialBlend);
            occludedVolume = Mathf.Clamp01(occludedVolume);
            occludedCutoffHz = Mathf.Clamp(occludedCutoffHz, 80f, 8000f);
            occludedReverb = Mathf.Clamp01(occludedReverb);
            wallsUntilFullMuffle = Mathf.Clamp(wallsUntilFullMuffle, 0.5f, 4f);
            doorClosedCutoffHz = Mathf.Clamp(doorClosedCutoffHz, 80f, 4000f);
            graphClosedVolume = Mathf.Clamp01(graphClosedVolume);
            graphClosedCutoffHz = Mathf.Clamp(graphClosedCutoffHz, 80f, 8000f);
            zoneIntensity = Mathf.Clamp01(zoneIntensity);
            crossZoneCutoffHz = Mathf.Clamp(crossZoneCutoffHz, 80f, 8000f);
            maxReverbMix = Mathf.Clamp01(maxReverbMix);
            minLowPassHz = Mathf.Clamp(minLowPassHz, 80f, 2000f);
            volumeSmoothingHalfLife = Mathf.Clamp(volumeSmoothingHalfLife, 0.01f, 1f);
            filterSmoothingHalfLife = Mathf.Clamp(filterSmoothingHalfLife, 0.01f, 1f);
            evaluationsPerSecond = Mathf.Clamp(evaluationsPerSecond, 4f, 60f);
        }
    }
}
