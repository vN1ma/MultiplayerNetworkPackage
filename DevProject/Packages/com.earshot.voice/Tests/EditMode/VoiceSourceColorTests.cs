using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceSourceColorTests
    {
        private GameObject host;
        private VoiceSourceColor color;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("SourceColorHost");
            color = host.AddComponent<VoiceSourceColor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void Apply_Muffle_LowersLowPass()
        {
            color.SetColor(1f, 0f, 0f, 1f, 400f);
            var sample = VoiceSample.Default;
            color.Apply(ref sample);

            Assert.Less(sample.LowPassHz, 500f);
            Assert.AreEqual(1f, sample.Volume, 0.0001f);
        }

        [Test]
        public void Apply_Reverb_RaisesMix()
        {
            color.SetColor(0f, 0.6f);
            var sample = VoiceSample.Default;
            sample.ReverbMix = 0.1f;
            color.Apply(ref sample);

            Assert.AreEqual(0.6f, sample.ReverbMix, 0.0001f);
        }

        [Test]
        public void Apply_Volume_ScalesExisting()
        {
            color.SetColor(0f, 0f, 0f, 0.5f);
            var sample = VoiceSample.Default;
            sample.Volume = 0.8f;
            color.Apply(ref sample);

            Assert.AreEqual(0.4f, sample.Volume, 0.0001f);
        }

        [Test]
        public void Apply_Thinness_RaisesHighPass()
        {
            color.SetColor(0f, 0f, 1f, 1f, 500f, 600f);
            var sample = VoiceSample.Default;
            color.Apply(ref sample);

            Assert.AreEqual(600f, sample.HighPassHz, 0.5f);
        }

        [Test]
        public void Find_WalksToParent()
        {
            var child = new GameObject("Child");
            child.transform.SetParent(host.transform, false);

            Assert.AreSame(color, VoiceSourceColor.Find(child.transform));
            Object.DestroyImmediate(child);
        }

        [Test]
        public void HallPreset_SetsReverb()
        {
            color.ApplyPreset(VoiceSourcePreset.Hall);
            Assert.Greater(color.Reverb, 0.5f);
            Assert.AreEqual(VoiceSourcePreset.Hall, color.Preset);
        }
    }
}
