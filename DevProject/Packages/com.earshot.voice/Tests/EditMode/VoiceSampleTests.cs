using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceSampleTests
    {
        [Test]
        public void Clamp_KeepsValuesInRange_AndFixesCrossedFilters()
        {
            var sample = VoiceSample.Default;
            sample.Volume = 2.5f;
            sample.ReverbMix = -0.2f;
            sample.SpatialBlend = 4f;
            sample.LowPassHz = 400f;
            sample.HighPassHz = 800f;

            sample.Clamp();

            Assert.AreEqual(1f, sample.Volume);
            Assert.AreEqual(0f, sample.ReverbMix);
            Assert.AreEqual(1f, sample.SpatialBlend);
            Assert.AreEqual(400f, sample.LowPassHz);
            Assert.AreEqual(400f, sample.HighPassHz);
        }

        [Test]
        public void MoveTowards_WithT1_ReachesTarget()
        {
            var current = VoiceSample.Default;
            var target = VoiceSample.Default;
            target.Volume = 0.2f;
            target.SpatialBlend = 0f;
            target.Muted = true;

            current.MoveTowards(target, 1f);

            Assert.AreEqual(0.2f, current.Volume, 0.0001f);
            Assert.AreEqual(0f, current.SpatialBlend, 0.0001f);
            Assert.IsTrue(current.Muted);
        }

        [Test]
        public void MoveTowards_WithT0_DoesNotChange()
        {
            var current = VoiceSample.Default;
            current.Volume = 0.4f;
            var target = VoiceSample.Default;
            target.Volume = 1f;

            current.MoveTowards(target, 0f);

            Assert.AreEqual(0.4f, current.Volume, 0.0001f);
        }

        [Test]
        public void MoveTowards_InterpolatesFrequencyLogarithmically()
        {
            var current = VoiceSample.Default;
            current.LowPassHz = 500f;
            var target = VoiceSample.Default;
            target.LowPassHz = 2000f;

            current.MoveTowards(target, 0.5f);

            Assert.AreEqual(1000f, current.LowPassHz, 1f);
        }
    }
}
