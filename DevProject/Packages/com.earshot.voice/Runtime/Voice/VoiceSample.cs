using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Das Klangergebnis fuer einen Sprecher. Jeder Modifier bekommt dieses Objekt
    /// und darf es veraendern; am Ende der Kette wird es auf die AudioSource und deren
    /// Filter angewendet.
    /// </summary>
    public struct VoiceSample
    {
        /// <summary>Obergrenze der Unity-Filter. Dieser Wert bedeutet "kein Tiefpass".</summary>
        public const float NoLowPass = 22000f;

        /// <summary>Untergrenze der Unity-Filter. Dieser Wert bedeutet "kein Hochpass".</summary>
        public const float NoHighPass = 10f;

        /// <summary>Lautstaerke von 0 bis 1.</summary>
        public float Volume;

        /// <summary>
        /// Tiefpass-Grenzfrequenz in Hertz. Niedrig heisst dumpf.
        /// 22000 = unveraendert, ~1000 = hinter einer Tuer, ~500 = sehr dumpf.
        /// </summary>
        public float LowPassHz;

        /// <summary>
        /// Hochpass-Grenzfrequenz in Hertz. Hoch heisst duenn und blechern.
        /// Nuetzlich fuer Funkgeraet- und Telefoneffekte.
        /// </summary>
        public float HighPassHz;

        /// <summary>Hallanteil von 0 bis 1.</summary>
        public float ReverbMix;

        /// <summary>0 = im Kopf (2D), 1 = voll raeumlich (3D).</summary>
        public float SpatialBlend;

        /// <summary>Harter Cutoff. Uebersteuert alles andere.</summary>
        public bool Muted;

        /// <summary>
        /// Neutraler Ausgangszustand: volle Lautstaerke, keine Filter, voll raeumlich.
        /// Jede Auswertung startet hier, danach duerfen die Modifier daempfen.
        /// </summary>
        public static VoiceSample Default => new VoiceSample
        {
            Volume = 1f,
            LowPassHz = NoLowPass,
            HighPassHz = NoHighPass,
            ReverbMix = 0f,
            SpatialBlend = 1f,
            Muted = false
        };

        /// <summary>
        /// Haelt alle Werte in ihren gueltigen Bereichen. Wird nach der Modifier-Kette
        /// aufgerufen, damit ein fehlerhaftes Modul nicht die Audio-Engine stoert.
        /// </summary>
        public void Clamp()
        {
            Volume = Mathf.Clamp01(Volume);
            LowPassHz = Mathf.Clamp(LowPassHz, NoHighPass, NoLowPass);
            HighPassHz = Mathf.Clamp(HighPassHz, NoHighPass, NoLowPass);
            ReverbMix = Mathf.Clamp01(ReverbMix);
            SpatialBlend = Mathf.Clamp01(SpatialBlend);

            // Ein Hochpass oberhalb des Tiefpasses wuerde das Signal ausloeschen.
            if (HighPassHz > LowPassHz) HighPassHz = LowPassHz;
        }

        /// <summary>
        /// Bewegt die Werte weich in Richtung eines Ziels. Ohne diese Glaettung knackt
        /// es hoerbar, sobald sich eine Filterfrequenz sprunghaft aendert.
        /// Frequenzen werden logarithmisch interpoliert, weil Tonhoehe so wahrgenommen
        /// wird: der Weg von 500 auf 1000 Hz klingt so gross wie der von 1000 auf 2000.
        /// </summary>
        public void MoveTowards(in VoiceSample target, float t)
        {
            MoveTowards(target, t, t);
        }

        /// <summary>
        /// Wie <see cref="MoveTowards(in VoiceSample, float)"/>, aber Lautstaerke
        /// und Filter (Dumpf/Hall) koennen unterschiedlich schnell nachziehen.
        /// </summary>
        public void MoveTowards(in VoiceSample target, float volumeT, float filterT)
        {
            volumeT = Mathf.Clamp01(volumeT);
            filterT = Mathf.Clamp01(filterT);
            Volume = Mathf.Lerp(Volume, target.Volume, volumeT);
            ReverbMix = Mathf.Lerp(ReverbMix, target.ReverbMix, filterT);
            SpatialBlend = Mathf.Lerp(SpatialBlend, target.SpatialBlend, filterT);
            LowPassHz = LerpFrequency(LowPassHz, target.LowPassHz, filterT);
            HighPassHz = LerpFrequency(HighPassHz, target.HighPassHz, filterT);
            Muted = target.Muted;
        }

        private static float LerpFrequency(float from, float to, float t)
        {
            from = Mathf.Max(from, 1f);
            to = Mathf.Max(to, 1f);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), t));
        }
    }
}
