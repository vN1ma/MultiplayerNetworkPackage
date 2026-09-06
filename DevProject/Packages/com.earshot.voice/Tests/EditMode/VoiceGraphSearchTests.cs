using System.Collections.Generic;
using NUnit.Framework;

namespace Earshot.Voice.Tests
{
    public sealed class VoiceGraphSearchTests
    {
        [Test]
        public void SameNode_IsFreePath()
        {
            var search = new VoiceGraphSearch();
            search.AddNode(1);
            var portals = new List<int>();

            bool found = search.TryFindPath(1, 1, portals, out float cost);

            Assert.IsTrue(found);
            Assert.AreEqual(0f, cost);
            Assert.AreEqual(0, portals.Count);
        }

        [Test]
        public void DisconnectedNodes_ReturnsFalse_DoesNotThrow()
        {
            var search = new VoiceGraphSearch();
            search.AddNode(1);
            search.AddNode(-475822);

            Assert.DoesNotThrow(() =>
            {
                bool found = search.TryFindPath(1, -475822, new List<int>(), out float cost);
                Assert.IsFalse(found);
                Assert.AreEqual(0f, cost);
            });
        }

        [Test]
        public void MissingNode_Fails()
        {
            var search = new VoiceGraphSearch();
            search.AddNode(1);

            Assert.IsFalse(search.TryFindPath(1, 2, new List<int>(), out _));
        }

        [Test]
        public void OpenCorridor_UsesShorterDoor()
        {
            var search = new VoiceGraphSearch();
            search.AddUndirectedEdge(1, 2, 4f, 10);
            search.AddUndirectedEdge(1, 2, 20f, 11);
            var portals = new List<int>();

            bool found = search.TryFindPath(1, 2, portals, out float cost);

            Assert.IsTrue(found);
            Assert.AreEqual(4f, cost);
            Assert.AreEqual(1, portals.Count);
            Assert.AreEqual(10, portals[0]);
        }

        [Test]
        public void ClosedDoor_IsExpensive_DetourWins()
        {
            var search = new VoiceGraphSearch();
            search.AddUndirectedEdge(1, 2, 3f + VoiceGraph.ClosedLengthPenalty, 10);
            search.AddUndirectedEdge(1, 3, 5f, 11);
            search.AddUndirectedEdge(3, 2, 5f, 12);
            var portals = new List<int>();

            bool found = search.TryFindPath(1, 2, portals, out float cost);

            Assert.IsTrue(found);
            Assert.AreEqual(10f, cost);
            Assert.AreEqual(2, portals.Count);
            Assert.AreEqual(11, portals[0]);
            Assert.AreEqual(12, portals[1]);
        }

        [Test]
        public void TwoDoorsInSeries_AddsWeights()
        {
            var search = new VoiceGraphSearch();
            search.AddUndirectedEdge(1, 2, 6f, 10);
            search.AddUndirectedEdge(2, 3, 7f, 11);
            var portals = new List<int>();

            bool found = search.TryFindPath(1, 3, portals, out float cost);

            Assert.IsTrue(found);
            Assert.AreEqual(13f, cost);
            Assert.AreEqual(2, portals.Count);
            Assert.AreEqual(10, portals[0]);
            Assert.AreEqual(11, portals[1]);
        }
    }
}
