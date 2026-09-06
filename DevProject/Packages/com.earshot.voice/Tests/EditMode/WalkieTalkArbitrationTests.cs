using NUnit.Framework;

namespace Earshot.Voice.Tests
{
    public sealed class WalkieTalkArbitrationTests
    {
        [Test]
        public void FirstSpeakerWins_UntilTheyStop()
        {
            var arb = new WalkieTalkArbitration();

            arb.SetSpeaking("default", "alice", true, 1f);
            arb.SetSpeaking("default", "bob", true, 1.5f);

            Assert.AreEqual("alice", arb.GetWinnerPlayerId("default"));
            Assert.IsTrue(arb.IsWinner("default", "alice"));
            Assert.IsFalse(arb.IsWinner("default", "bob"));

            arb.SetSpeaking("default", "alice", false, 3f);
            Assert.AreEqual("bob", arb.GetWinnerPlayerId("default"));

            arb.SetSpeaking("default", "bob", false, 4f);
            Assert.IsNull(arb.GetWinnerPlayerId("default"));
        }

        [Test]
        public void ChannelsAreIndependent()
        {
            var arb = new WalkieTalkArbitration();
            arb.SetSpeaking("a", "alice", true, 1f);
            arb.SetSpeaking("b", "bob", true, 0.5f);

            Assert.AreEqual("alice", arb.GetWinnerPlayerId("a"));
            Assert.AreEqual("bob", arb.GetWinnerPlayerId("b"));
        }

        [Test]
        public void LateJoinerDoesNotStealWhileFirstStillSpeaking()
        {
            var arb = new WalkieTalkArbitration();
            arb.SetSpeaking("ops", "first", true, 10f);
            arb.SetSpeaking("ops", "second", true, 11f);
            arb.SetSpeaking("ops", "second", true, 12f); // refresh does not change start

            Assert.AreEqual("first", arb.GetWinnerPlayerId("ops"));
        }
    }
}
