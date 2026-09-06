using NUnit.Framework;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceChannelResolverTests
    {
        [TearDown]
        public void TearDown()
        {
            EarshotVoiceSettings.ClearCache();
        }

        [Test]
        public void Resolve_ComponentOverride_Wins()
        {
            Assert.AreEqual("lobby-99", VoiceChannelResolver.Resolve(" lobby-99 "));
        }

        [Test]
        public void Resolve_EmptyOverride_UsesSettingsOrDefault()
        {
            string channel = VoiceChannelResolver.Resolve(null);

            Assert.IsFalse(string.IsNullOrWhiteSpace(channel));
            Assert.AreEqual(VoiceChannelResolver.DefaultChannel, channel);
        }
    }
}
