using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Klangfarbe einer einzelnen Quelle. Optional auf Testlautsprecher, Player
    /// oder jede AudioSource. Kommt nach Distanz/Waenden dazu: diese Quelle klingt
    /// dumpfer, halliger oder duenner als der Rest.
    /// </summary>
    [AddComponentMenu("Earshot Voice/Voice Source Color")]
    [DisallowMultipleComponent]
    public class VoiceSourceColor : MonoBehaviour
    {
        private const float NoReverbRoomLevel = -10000f;
        private const float FullReverbRoomLevel = -1000f;

        [SerializeField]
        [Tooltip("Voreinstellung fuellt die Regler. Danach auf Custom, wenn du fein justierst.")]
        private VoiceSourcePreset preset = VoiceSourcePreset.Custom;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Dumpfheit dieser Quelle. 0 = klar, 1 = stark hinter Stoff/Maske.")]
        private float muffle = 0f;

        [SerializeField, Range(80f, 4000f)]
        [Tooltip("Tiefpass bei voller Dumpfheit.")]
        private float muffleCutoffHz = 500f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Hall, den diese Quelle immer mitbringt (Bad, Halle, Tropfstein).")]
        private float reverb = 0f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Duenner, blecherner Klang. 0 = aus.")]
        private float thinness = 0f;

        [SerializeField, Range(80f, 2000f)]
        [Tooltip("Hochpass bei voller Thinness.")]
        private float thinCutoffHz = 400f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Lautstaerke dieser Quelle.")]
        private float volume = 1f;

        private AudioSource standaloneSource;
        private AudioLowPassFilter standaloneLowPass;
        private AudioHighPassFilter standaloneHighPass;
        private AudioReverbFilter standaloneReverb;

        public VoiceSourcePreset Preset => preset;
        public float Muffle => muffle;
        public float Reverb => reverb;
        public float Thinness => thinness;
        public float Volume => volume;

        public static VoiceSourceColor Find(Transform from)
        {
            if (from == null) return null;
            return from.GetComponentInParent<VoiceSourceColor>();
        }

        public void Apply(ref VoiceSample sample)
        {
            if (!isActiveAndEnabled) return;

            sample.Volume *= Mathf.Max(0f, volume);

            if (muffle > 0f)
            {
                float floor = Mathf.Max(80f, muffleCutoffHz);
                float cutoff = Mathf.Exp(Mathf.Lerp(
                    Mathf.Log(VoiceSample.NoLowPass),
                    Mathf.Log(floor),
                    Mathf.Clamp01(muffle)));
                sample.LowPassHz = Mathf.Min(sample.LowPassHz, cutoff);
            }

            if (reverb > 0f)
            {
                sample.ReverbMix = Mathf.Max(sample.ReverbMix, Mathf.Clamp01(reverb));
            }

            if (thinness > 0f)
            {
                float high = Mathf.Lerp(VoiceSample.NoHighPass, thinCutoffHz, Mathf.Clamp01(thinness));
                sample.HighPassHz = Mathf.Max(sample.HighPassHz, high);
            }
        }

        public void SetColor(
            float muffleAmount,
            float reverbAmount,
            float thinAmount = 0f,
            float volumeAmount = 1f,
            float muffleCutoff = 500f,
            float thinCutoff = 400f)
        {
            preset = VoiceSourcePreset.Custom;
            muffle = Mathf.Clamp01(muffleAmount);
            reverb = Mathf.Clamp01(reverbAmount);
            thinness = Mathf.Clamp01(thinAmount);
            volume = Mathf.Max(0f, volumeAmount);
            muffleCutoffHz = Mathf.Clamp(muffleCutoff, 80f, 4000f);
            thinCutoffHz = Mathf.Clamp(thinCutoff, 80f, 2000f);
        }

        public void ApplyPreset(VoiceSourcePreset value)
        {
            preset = value;
            switch (value)
            {
                case VoiceSourcePreset.Clear:
                    SetColor(0f, 0f);
                    preset = VoiceSourcePreset.Clear;
                    break;
                case VoiceSourcePreset.SoftMuffle:
                    SetColor(0.45f, 0.05f, 0f, 0.9f, 900f);
                    preset = VoiceSourcePreset.SoftMuffle;
                    break;
                case VoiceSourcePreset.HeavyMuffle:
                    SetColor(0.85f, 0.15f, 0f, 0.75f, 350f);
                    preset = VoiceSourcePreset.HeavyMuffle;
                    break;
                case VoiceSourcePreset.SmallRoom:
                    SetColor(0.1f, 0.35f, 0f, 1f, 1800f);
                    preset = VoiceSourcePreset.SmallRoom;
                    break;
                case VoiceSourcePreset.Hall:
                    SetColor(0.05f, 0.7f, 0f, 1f, 2500f);
                    preset = VoiceSourcePreset.Hall;
                    break;
                case VoiceSourcePreset.Tinny:
                    SetColor(0.25f, 0f, 0.65f, 0.85f, 2800f, 500f);
                    preset = VoiceSourcePreset.Tinny;
                    break;
            }
        }

        private void Reset()
        {
            ApplyPreset(VoiceSourcePreset.Custom);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (GetComponentInChildren<VoiceEmitter>(true) != null) return;

            if (!TryGetComponent(out standaloneSource) || standaloneSource == null) return;

            EnsureStandaloneFilters();
            var sample = VoiceSample.Default;
            Apply(ref sample);
            sample.Clamp();

            if (standaloneLowPass != null) standaloneLowPass.cutoffFrequency = sample.LowPassHz;
            if (standaloneHighPass != null) standaloneHighPass.cutoffFrequency = sample.HighPassHz;
            if (standaloneReverb != null)
            {
                standaloneReverb.room = Mathf.Lerp(NoReverbRoomLevel, FullReverbRoomLevel, sample.ReverbMix);
            }
        }

        private void EnsureStandaloneFilters()
        {
            if (standaloneSource == null) return;

            if (standaloneLowPass == null)
            {
                standaloneLowPass = standaloneSource.GetComponent<AudioLowPassFilter>();
                if (standaloneLowPass == null)
                {
                    standaloneLowPass = standaloneSource.gameObject.AddComponent<AudioLowPassFilter>();
                    standaloneLowPass.lowpassResonanceQ = 1f;
                }
            }

            if (standaloneHighPass == null)
            {
                standaloneHighPass = standaloneSource.GetComponent<AudioHighPassFilter>();
                if (standaloneHighPass == null)
                {
                    standaloneHighPass = standaloneSource.gameObject.AddComponent<AudioHighPassFilter>();
                    standaloneHighPass.highpassResonanceQ = 1f;
                }
            }

            if (standaloneReverb == null && reverb > 0f)
            {
                standaloneReverb = standaloneSource.GetComponent<AudioReverbFilter>();
                if (standaloneReverb == null)
                {
                    standaloneReverb = standaloneSource.gameObject.AddComponent<AudioReverbFilter>();
                    standaloneReverb.reverbPreset = AudioReverbPreset.User;
                    standaloneReverb.dryLevel = 0f;
                }
            }
        }

    }

    public enum VoiceSourcePreset
    {
        Custom = 0,
        Clear = 1,
        SoftMuffle = 2,
        HeavyMuffle = 3,
        SmallRoom = 4,
        Hall = 5,
        Tinny = 6
    }
}
