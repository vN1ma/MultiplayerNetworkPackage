using Earshot.Voice;
using Earshot.Voice.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace Earshot.Tests
{
    public class VoiceMathTests
    {
        private readonly System.Collections.Generic.List<Object> spawned =
            new System.Collections.Generic.List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            }

            spawned.Clear();
        }

        [Test]
        public void Clamp_KeepsValuesInRange_AndFixesInvertedPassband()
        {
            var sample = new VoiceSample
            {
                Volume = 4f,
                LowPassHz = 50f,
                HighPassHz = 8000f,
                ReverbMix = 2f,
                SpatialBlend = -1f
            };

            sample.Clamp();

            Assert.AreEqual(1f, sample.Volume);
            Assert.AreEqual(1f, sample.ReverbMix);
            Assert.AreEqual(0f, sample.SpatialBlend);
            Assert.GreaterOrEqual(sample.LowPassHz, VoiceSample.NoHighPass);
            Assert.LessOrEqual(sample.HighPassHz, sample.LowPassHz);
        }

        [Test]
        public void MoveTowards_ReachesTargetAtOne_AndLeavesAudioAtZero()
        {
            var current = VoiceSample.Default;
            var target = VoiceSample.Default;
            target.Volume = 0.2f;
            target.LowPassHz = 800f;
            target.ReverbMix = 0.5f;
            target.Muted = true;

            var frozen = current;
            current.MoveTowards(in target, 0f);

            Assert.AreEqual(frozen.Volume, current.Volume);
            Assert.AreEqual(frozen.LowPassHz, current.LowPassHz, 0.01f);
            Assert.AreEqual(frozen.ReverbMix, current.ReverbMix);

            current.MoveTowards(in target, 1f);

            Assert.AreEqual(target.Volume, current.Volume, 0.0001f);
            Assert.AreEqual(target.LowPassHz, current.LowPassHz, 0.01f);
            Assert.AreEqual(target.ReverbMix, current.ReverbMix, 0.0001f);
            Assert.IsTrue(current.Muted);
        }

        [Test]
        public void FrequencyInterpolation_IsLogarithmic()
        {
            var current = VoiceSample.Default;
            current.LowPassHz = 500f;

            var target = VoiceSample.Default;
            target.LowPassHz = 2000f;

            current.MoveTowards(in target, 0.5f);

            // Geometrisches Mittel von 500 und 2000 ist 1000, das arithmetische waere 1250.
            Assert.AreEqual(1000f, current.LowPassHz, 1f);
        }

        [Test]
        public void DistanceFalloff_IsFullNearby_AndSilentBeyondRange()
        {
            var profile = ScriptableObject.CreateInstance<VoiceProfile>();
            spawned.Add(profile);

            Assert.AreEqual(1f, profile.EvaluateDistanceFalloff(0f), 0.001f);
            Assert.AreEqual(0f, profile.EvaluateDistanceFalloff(profile.MaxHearingDistance), 0.001f);
            Assert.AreEqual(0f, profile.EvaluateDistanceFalloff(profile.MaxHearingDistance * 4f), 0.001f);
        }

        [Test]
        public void DistanceModifier_AttenuatesWithDistance()
        {
            var profile = ScriptableObject.CreateInstance<VoiceProfile>();
            spawned.Add(profile);

            var modifier = ScriptableObject.CreateInstance<DistanceFalloffModifier>();
            spawned.Add(modifier);

            var nearby = VoiceSample.Default;
            modifier.Apply(new VoiceContext { Profile = profile, Distance = 0f }, ref nearby);

            var far = VoiceSample.Default;
            modifier.Apply(
                new VoiceContext { Profile = profile, Distance = profile.MaxHearingDistance },
                ref far);

            Assert.AreEqual(1f, nearby.Volume, 0.001f);
            Assert.AreEqual(0f, far.Volume, 0.001f);
            Assert.Less(far.LowPassHz, nearby.LowPassHz);
        }

        [Test]
        public void OcclusionModifier_OnlyAffectsBlockedPaths()
        {
            var modifier = ScriptableObject.CreateInstance<OcclusionModifier>();
            spawned.Add(modifier);

            var clear = VoiceSample.Default;
            modifier.Apply(new VoiceContext { OcclusionAmount = 0f }, ref clear);

            var blocked = VoiceSample.Default;
            modifier.Apply(new VoiceContext { OcclusionAmount = 1f }, ref blocked);

            Assert.AreEqual(1f, clear.Volume, 0.001f);
            Assert.AreEqual(VoiceSample.NoLowPass, clear.LowPassHz, 0.1f);
            Assert.Less(blocked.Volume, 1f);
            Assert.Less(blocked.LowPassHz, VoiceSample.NoLowPass);
        }

        [Test]
        public void PortalModifier_ClosedReducesVolume_OpenLeavesIt()
        {
            var modifier = ScriptableObject.CreateInstance<PortalModifier>();
            spawned.Add(modifier);

            var portalObject = new GameObject("TestPortal");
            spawned.Add(portalObject);
            var portal = portalObject.AddComponent<VoicePortal>();

            portal.Openness = 1f;
            var open = VoiceSample.Default;
            modifier.Apply(
                new VoiceContext
                {
                    HasPortal = true,
                    Portal = portal,
                    PortalOpenness = 1f
                },
                ref open);

            portal.Openness = 0f;
            var closed = VoiceSample.Default;
            modifier.Apply(
                new VoiceContext
                {
                    HasPortal = true,
                    Portal = portal,
                    PortalOpenness = 0f
                },
                ref closed);

            Assert.AreEqual(1f, open.Volume, 0.001f);
            Assert.Less(closed.Volume, 1f);
            Assert.AreEqual(portal.ClosedVolume, closed.Volume, 0.001f);
            Assert.Less(closed.LowPassHz, open.LowPassHz);
        }

        [Test]
        public void ZoneModifier_CrossZoneIsQuieterThanSameZone()
        {
            var modifier = ScriptableObject.CreateInstance<ZoneModifier>();
            spawned.Add(modifier);

            var zoneObject = new GameObject("TestZone");
            spawned.Add(zoneObject);
            zoneObject.AddComponent<BoxCollider>().isTrigger = true;
            var zone = zoneObject.AddComponent<VoiceZone>();

            var same = VoiceSample.Default;
            modifier.Apply(
                new VoiceContext
                {
                    SpeakerZone = zone,
                    ListenerZone = zone,
                    SameZone = true
                },
                ref same);

            var other = VoiceSample.Default;
            modifier.Apply(
                new VoiceContext
                {
                    SpeakerZone = zone,
                    ListenerZone = null,
                    SameZone = false
                },
                ref other);

            Assert.Greater(same.Volume, other.Volume);
            Assert.Less(other.LowPassHz, same.LowPassHz);
        }
    }
}
