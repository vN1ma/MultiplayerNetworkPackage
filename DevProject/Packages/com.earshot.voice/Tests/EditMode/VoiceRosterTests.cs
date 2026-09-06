using System.Collections.Generic;
using NUnit.Framework;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceRosterTests
    {
        private readonly List<IProximityVoicePlayer> added = new List<IProximityVoicePlayer>();
        private readonly List<IProximityVoicePlayer> removed = new List<IProximityVoicePlayer>();
        private readonly List<IProximityVoicePlayer> ready = new List<IProximityVoicePlayer>();

        [SetUp]
        public void SetUp()
        {
            VoiceRoster.Clear();
            added.Clear();
            removed.Clear();
            ready.Clear();
            VoiceRoster.PlayerAdded += OnAdded;
            VoiceRoster.PlayerRemoved += OnRemoved;
            VoiceRoster.IdentityReady += OnReady;
        }

        [TearDown]
        public void TearDown()
        {
            VoiceRoster.PlayerAdded -= OnAdded;
            VoiceRoster.PlayerRemoved -= OnRemoved;
            VoiceRoster.IdentityReady -= OnReady;
            VoiceRoster.Clear();
        }

        private void OnAdded(IProximityVoicePlayer player) => added.Add(player);
        private void OnRemoved(IProximityVoicePlayer player) => removed.Add(player);
        private void OnReady(IProximityVoicePlayer player) => ready.Add(player);

        [Test]
        public void Register_AddsPlayer_AndRaisesPlayerAdded()
        {
            var player = new FakeVoicePlayer { DisplayName = "anna" };

            VoiceRoster.Register(player);

            Assert.AreEqual(1, VoiceRoster.Players.Count);
            Assert.AreSame(player, VoiceRoster.Players[0]);
            Assert.AreEqual(1, added.Count);
            Assert.AreSame(player, added[0]);
        }

        [Test]
        public void Register_SamePlayerTwice_DoesNotDuplicate()
        {
            var player = new FakeVoicePlayer();

            VoiceRoster.Register(player);
            VoiceRoster.Register(player);

            Assert.AreEqual(1, VoiceRoster.Players.Count);
            Assert.AreEqual(1, added.Count);
        }

        [Test]
        public void Register_Null_IsIgnored()
        {
            VoiceRoster.Register(null);

            Assert.AreEqual(0, VoiceRoster.Players.Count);
            Assert.AreEqual(0, added.Count);
        }

        [Test]
        public void Register_LocalPlayer_SetsLocalPlayer()
        {
            var local = new FakeVoicePlayer { IsLocalPlayer = true, PlayerId = "me" };
            var remote = new FakeVoicePlayer { IsLocalPlayer = false, PlayerId = "other" };

            VoiceRoster.Register(remote);
            VoiceRoster.Register(local);

            Assert.AreSame(local, VoiceRoster.LocalPlayer);
        }

        [Test]
        public void Register_WithoutIdentity_DoesNotMapById()
        {
            var player = new FakeVoicePlayer { PlayerId = "" };

            VoiceRoster.Register(player);

            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("anything", out _));
            Assert.AreEqual(0, ready.Count);
        }

        [Test]
        public void Register_WithIdentity_MapsId_AndRaisesIdentityReady()
        {
            var player = new FakeVoicePlayer { PlayerId = "ugs-abc" };

            VoiceRoster.Register(player);

            Assert.IsTrue(VoiceRoster.TryGetByPlayerId("ugs-abc", out var found));
            Assert.AreSame(player, found);
            Assert.AreEqual(1, ready.Count);
            Assert.AreSame(player, ready[0]);
        }

        [Test]
        public void TryGetByPlayerId_IsCaseInsensitive()
        {
            var player = new FakeVoicePlayer { PlayerId = "AbC-Id" };
            VoiceRoster.Register(player);

            Assert.IsTrue(VoiceRoster.TryGetByPlayerId("abc-id", out var found));
            Assert.AreSame(player, found);
        }

        [Test]
        public void TryGetByPlayerId_Empty_ReturnsFalse()
        {
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("", out var player));
            Assert.IsNull(player);
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId(null, out player));
            Assert.IsNull(player);
        }

        [Test]
        public void NotifyIdentityReady_WithoutIdentity_DoesNothing()
        {
            var player = new FakeVoicePlayer { PlayerId = "" };

            VoiceRoster.NotifyIdentityReady(player);

            Assert.AreEqual(0, ready.Count);
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("x", out _));
        }

        [Test]
        public void NotifyIdentityReady_AfterLateBind_MapsPreviouslyUnknownPlayer()
        {
            var player = new FakeVoicePlayer();
            VoiceRoster.Register(player);
            Assert.AreEqual(0, ready.Count);

            player.PlayerId = "late-id";
            VoiceRoster.NotifyIdentityReady(player);

            Assert.IsTrue(VoiceRoster.TryGetByPlayerId("late-id", out var found));
            Assert.AreSame(player, found);
            Assert.AreEqual(1, ready.Count);
        }

        [Test]
        public void Unregister_RemovesPlayer_AndRaisesPlayerRemoved()
        {
            var player = new FakeVoicePlayer { PlayerId = "gone" };
            VoiceRoster.Register(player);

            VoiceRoster.Unregister(player);

            Assert.AreEqual(0, VoiceRoster.Players.Count);
            Assert.AreEqual(1, removed.Count);
            Assert.AreSame(player, removed[0]);
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("gone", out _));
        }

        [Test]
        public void Unregister_Unknown_IsIgnored()
        {
            var player = new FakeVoicePlayer();

            VoiceRoster.Unregister(player);

            Assert.AreEqual(0, removed.Count);
        }

        [Test]
        public void Unregister_Local_ClearsLocalPlayer()
        {
            var local = new FakeVoicePlayer { IsLocalPlayer = true, PlayerId = "me" };
            VoiceRoster.Register(local);

            VoiceRoster.Unregister(local);

            Assert.IsNull(VoiceRoster.LocalPlayer);
        }

        [Test]
        public void Clear_EmptiesPlayers_AndLookup()
        {
            VoiceRoster.Register(new FakeVoicePlayer { IsLocalPlayer = true, PlayerId = "a" });
            VoiceRoster.Register(new FakeVoicePlayer { PlayerId = "b" });

            VoiceRoster.Clear();

            Assert.AreEqual(0, VoiceRoster.Players.Count);
            Assert.IsNull(VoiceRoster.LocalPlayer);
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("a", out _));
            Assert.IsFalse(VoiceRoster.TryGetByPlayerId("b", out _));
        }
    }
}
