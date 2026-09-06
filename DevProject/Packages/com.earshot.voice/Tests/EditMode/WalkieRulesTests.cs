using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class WalkieRulesTests
    {
        [TearDown]
        public void TearDown()
        {
            WalkieTalkieRegistry.ClearForTests();
        }

        [Test]
        public void ToVivoxRadioChannel_PrefixesAndSanitizes()
        {
            Assert.AreEqual("earshot.radio.default", WalkieRules.ToVivoxRadioChannel(" "));
            Assert.AreEqual("earshot.radio.team_a", WalkieRules.ToVivoxRadioChannel("Team A"));
            Assert.AreEqual("earshot.radio.ops-1", WalkieRules.ToVivoxRadioChannel("ops-1"));
        }

        [Test]
        public void TryParseLogicalChannel_RoundTrip()
        {
            string vivox = WalkieRules.ToVivoxRadioChannel("alpha");
            Assert.IsTrue(WalkieRules.TryParseLogicalChannel(vivox, out string logical));
            Assert.AreEqual("alpha", logical);
            Assert.IsFalse(WalkieRules.TryParseLogicalChannel("match-lobby", out _));
        }

        [Test]
        public void HalfDuplex_ReceiveOnlyWhenPoweredAndNotTransmitting()
        {
            Assert.IsTrue(WalkieRules.ShouldPlayReceivedRadio(true, false));
            Assert.IsFalse(WalkieRules.ShouldPlayReceivedRadio(true, true));
            Assert.IsFalse(WalkieRules.ShouldPlayReceivedRadio(false, false));
            Assert.IsFalse(WalkieRules.ShouldPlayReceivedRadio(false, true));
        }

        [Test]
        public void CanTransmit_RequiresPowerAndHeld()
        {
            Assert.IsTrue(WalkieRules.CanTransmit(true, true));
            Assert.IsFalse(WalkieRules.CanTransmit(true, false));
            Assert.IsFalse(WalkieRules.CanTransmit(false, true));
        }

        [Test]
        public void MouthVolumeScale_DampensOnlyWhileOnRadio()
        {
            Assert.AreEqual(1f, WalkieRules.MouthVolumeScale(false, 0.1f));
            Assert.AreEqual(0.1f, WalkieRules.MouthVolumeScale(true, 0.1f), 0.0001f);
            Assert.AreEqual(0f, WalkieRules.MouthVolumeScale(true, 0f), 0.0001f);
        }

        [Test]
        public void ClampDelaySeconds_LimitsRange()
        {
            Assert.AreEqual(0f, WalkieRules.ClampDelaySeconds(-1f));
            Assert.AreEqual(0.2f, WalkieRules.ClampDelaySeconds(0.2f), 0.0001f);
            Assert.AreEqual(1.5f, WalkieRules.ClampDelaySeconds(9f));
        }

        [Test]
        public void Registry_HasPoweredDeviceOnChannel()
        {
            var go = new GameObject("walkie-test");
            var walkie = go.AddComponent<EarshotWalkieTalkie>();
            walkie.SetChannelId("ops");
            walkie.SetPowered(false);

            Assert.IsFalse(WalkieTalkieRegistry.HasPoweredDeviceOnChannel("ops"));

            walkie.SetPowered(true);
            Assert.IsTrue(WalkieTalkieRegistry.HasPoweredDeviceOnChannel("ops"));
            Assert.IsFalse(WalkieTalkieRegistry.HasPoweredDeviceOnChannel("other"));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Registry_LocalTransmit_SetsHalfDuplexFlag()
        {
            var go = new GameObject("walkie-tx");
            var walkie = go.AddComponent<EarshotWalkieTalkie>();
            walkie.SetChannelId("a");
            walkie.SetPowered(true);
            walkie.SetCanTransmit(true);

            Assert.IsFalse(WalkieTalkieRegistry.LocalIsTransmitting);

            walkie.SetTransmitting(true);
            Assert.IsTrue(WalkieTalkieRegistry.LocalIsTransmitting);
            Assert.AreEqual("a", WalkieTalkieRegistry.LocalTransmitChannelId);
            Assert.IsFalse(WalkieRules.ShouldPlayReceivedRadio(true, WalkieTalkieRegistry.LocalIsTransmitting));

            walkie.SetTransmitting(false);
            Assert.IsFalse(WalkieTalkieRegistry.LocalIsTransmitting);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetTransmitting_IgnoredWhenNotHeld()
        {
            var go = new GameObject("walkie-nohold");
            var walkie = go.AddComponent<EarshotWalkieTalkie>();
            walkie.SetPowered(true);
            walkie.SetCanTransmit(false);

            walkie.SetTransmitting(true);
            Assert.IsFalse(walkie.IsTransmitting);
            Assert.IsFalse(WalkieTalkieRegistry.LocalIsTransmitting);

            Object.DestroyImmediate(go);
        }
    }
}
