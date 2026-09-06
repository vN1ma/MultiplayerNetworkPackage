using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class NetworkOwnershipProbeTests
    {
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("ownership-probe-host");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void Read_NoNetwork_ReturnsNone()
        {
            var result = NetworkOwnershipProbe.Read(host);

            Assert.AreEqual(NetworkOwnershipProbe.Status.None, result.Status);
        }

        [Test]
        public void Read_OwnerAfterSpawn_IsLocal()
        {
            var network = host.AddComponent<FakeNetworkObject>();
            network.IsSpawned = true;
            network.IsOwner = true;

            var result = NetworkOwnershipProbe.Read(host);

            Assert.AreEqual(NetworkOwnershipProbe.Status.Ready, result.Status);
            Assert.IsTrue(result.IsLocal);
        }

        [Test]
        public void Read_RemoteAfterSpawn_IsNotLocal()
        {
            var network = host.AddComponent<FakeNetworkObject>();
            network.IsSpawned = true;
            network.IsOwner = false;

            var result = NetworkOwnershipProbe.Read(host);

            Assert.AreEqual(NetworkOwnershipProbe.Status.Ready, result.Status);
            Assert.IsFalse(result.IsLocal);
        }

        [Test]
        public void Read_NotSpawned_ReturnsNotReady()
        {
            var network = host.AddComponent<FakeNetworkObject>();
            network.IsSpawned = false;
            network.IsOwner = true;

            var result = NetworkOwnershipProbe.Read(host);

            Assert.AreEqual(NetworkOwnershipProbe.Status.NotReady, result.Status);
        }

        [Test]
        public void Read_FindsNetworkOnParent()
        {
            var child = new GameObject("child");
            child.transform.SetParent(host.transform);
            var network = host.AddComponent<FakeNetworkObject>();
            network.IsSpawned = true;
            network.IsOwner = false;

            var result = NetworkOwnershipProbe.Read(child);

            Assert.AreEqual(NetworkOwnershipProbe.Status.Ready, result.Status);
            Assert.IsFalse(result.IsLocal);
        }

        [Test]
        public void Read_PhotonMineFalse_WithoutSpawnFlag_IsUnconfirmed()
        {
            var view = host.AddComponent<FakePhotonView>();
            view.IsMine = false;

            var result = NetworkOwnershipProbe.Read(host);

            Assert.AreEqual(NetworkOwnershipProbe.Status.NotReady, result.Status);
            Assert.IsFalse(result.WaitingForSpawn);
        }

        [Test]
        public void TryReadSyncedPlayerId_FromSiblingField()
        {
            var state = host.AddComponent<FakePlayerState>();
            state.PlayerId = "ugs-remote-1";

            Assert.IsTrue(NetworkOwnershipProbe.TryReadSyncedPlayerId(host, out string id));
            Assert.AreEqual("ugs-remote-1", id);
        }
    }

    public sealed class FakeNetworkObject : MonoBehaviour
    {
        public bool IsSpawned;
        public bool IsOwner;
    }

    public sealed class FakePlayerState : MonoBehaviour
    {
        public string PlayerId;
    }

    public sealed class FakePhotonView : MonoBehaviour
    {
        public bool IsMine;
    }
}
