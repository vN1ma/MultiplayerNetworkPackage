using Earshot.Voice.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceHearingTuningTests
    {
        [Test]
        public void DistanceFalloff_InsideNear_IsFullVolume()
        {
            var profile = ScriptableObject.CreateInstance<VoiceProfile>();
            var tuning = new VoiceHearingTuning
            {
                nearDistance = 4f,
                maxHearingDistance = 20f,
                distanceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f)
            };

            profile.ApplyTuning(tuning);

            Assert.AreEqual(1f, profile.EvaluateDistanceFalloff(0f), 0.0001f);
            Assert.AreEqual(1f, profile.EvaluateDistanceFalloff(4f), 0.0001f);
            Assert.AreEqual(0.5f, profile.EvaluateDistanceFalloff(12f), 0.02f);
            Assert.AreEqual(0f, profile.EvaluateDistanceFalloff(20f), 0.0001f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void ApplyHearingLimits_CapsReverb_AndFloorsLowPass()
        {
            var profile = ScriptableObject.CreateInstance<VoiceProfile>();
            var tuning = new VoiceHearingTuning
            {
                maxReverbMix = 0.3f,
                minLowPassHz = 400f,
                spatialBlend = 1f,
                farSpatialBlend = 0.2f,
                nearDistance = 0f,
                maxHearingDistance = 10f
            };
            profile.ApplyTuning(tuning);

            var sample = VoiceSample.Default;
            sample.ReverbMix = 0.9f;
            sample.LowPassHz = 120f;

            profile.ApplyHearingLimits(10f, ref sample);

            Assert.AreEqual(0.3f, sample.ReverbMix, 0.0001f);
            Assert.AreEqual(400f, sample.LowPassHz, 0.0001f);
            Assert.AreEqual(0.2f, sample.SpatialBlend, 0.0001f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void ApplyHearingTuning_WritesModifierKnobs()
        {
            var settings = ScriptableObject.CreateInstance<EarshotVoiceSettings>();
            var profile = ScriptableObject.CreateInstance<VoiceProfile>();
            var distance = ScriptableObject.CreateInstance<DistanceFalloffModifier>();
            var occlusion = ScriptableObject.CreateInstance<OcclusionModifier>();
            profile.AddRuntimeModifier(distance);
            profile.AddRuntimeModifier(occlusion);
            settings.SetVoiceProfile(profile);

            var tuning = new VoiceHearingTuning
            {
                airAbsorption = 0.8f,
                distantCutoffHz = 2500f,
                occludedVolume = 0.05f,
                occludedCutoffHz = 220f,
                heardVolume = 0.4f,
                enableWallMuffle = false
            };

            settings.ApplyHearingTuning(tuning);

            var sample = VoiceSample.Default;
            var context = new VoiceContext
            {
                Profile = profile,
                Distance = 10f,
                HearingDistance = 10f
            };
            distance.Apply(in context, ref sample);
            Assert.Less(sample.LowPassHz, VoiceSample.NoLowPass);

            Assert.IsFalse(occlusion.Enabled);
            Assert.AreEqual(0.4f, EarshotVoice.HeardVoiceVolume, 0.0001f);

            EarshotVoice.HeardVoiceVolume = 1f;
            Object.DestroyImmediate(distance);
            Object.DestroyImmediate(occlusion);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void MoveTowards_UsesSeparateRatesForVolumeAndFilter()
        {
            var current = VoiceSample.Default;
            current.Volume = 0f;
            current.LowPassHz = 500f;

            var target = VoiceSample.Default;
            target.Volume = 1f;
            target.LowPassHz = 2000f;

            current.MoveTowards(target, 1f, 0f);

            Assert.AreEqual(1f, current.Volume, 0.0001f);
            Assert.AreEqual(500f, current.LowPassHz, 0.5f);
        }
    }
}
