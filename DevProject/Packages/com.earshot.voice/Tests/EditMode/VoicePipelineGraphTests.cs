using System.Collections.Generic;
using Earshot.Voice.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    public sealed class VoicePipelineGraphTests
    {
        private readonly List<Object> created = new List<Object>();
        private VoicePipeline pipeline;
        private VoiceProfile profile;

        [SetUp]
        public void SetUp()
        {
            pipeline = new VoicePipeline();
            profile = ScriptableObject.CreateInstance<VoiceProfile>();
            created.Add(profile);
            profile.AddRuntimeModifier(Track(ScriptableObject.CreateInstance<DistanceFalloffModifier>()));
            profile.AddRuntimeModifier(Track(ScriptableObject.CreateInstance<OcclusionModifier>()));
            profile.AddRuntimeModifier(Track(ScriptableObject.CreateInstance<GraphModifier>()));
            profile.AddRuntimeModifier(Track(ScriptableObject.CreateInstance<ZoneModifier>()));
            VoiceGraph.MarkDirty();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            VoiceGraph.MarkDirty();
        }

        [Test]
        public void LHall_OpenDoor_UsesGraph_QuieterThanClear_LouderThanWall()
        {
            BuildLHall(openDoor: true);
            var listener = new Vector3(-5f, 1f, 0f);
            var speaker = new Vector3(4f, 1f, 8f);

            var open = pipeline.Evaluate(profile, listener, speaker, 1f, out var openContext);
            float wallVolume = ThroughWallVolume(listener, speaker);
            float clearVolume = ClearLineVolume(listener, speaker);

            Assert.IsTrue(openContext.UsedGraph);
            Assert.Greater(openContext.HearingDistance, openContext.Distance);
            Assert.Greater(open.Volume, wallVolume);
            Assert.Less(open.Volume, clearVolume);
        }

        [Test]
        public void LHall_ClosedDoor_IsMuffled()
        {
            var portal = BuildLHall(openDoor: false);
            var listener = new Vector3(-5f, 1f, 0f);
            var speaker = new Vector3(4f, 1f, 8f);

            var closed = pipeline.Evaluate(profile, listener, speaker, 1f, out var context);

            Assert.IsTrue(context.UsedGraph);
            Assert.Greater(context.GraphClosedness, 0.9f);
            Assert.Less(closed.Volume, 0.25f);
            Assert.Less(closed.LowPassHz, 2000f);
            Assert.AreEqual(0f, portal.Openness);
        }

        [Test]
        public void TwoFloors_CeilingOnly_IsOccluded()
        {
            BuildFloors(withStair: false);
            var sample = pipeline.Evaluate(
                profile,
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 4.5f, 0f),
                1f,
                out var context);

            Assert.IsFalse(context.UsedGraph);
            Assert.Greater(context.OcclusionAmount, 0.9f);
            Assert.Less(sample.Volume, 0.3f);
        }

        [Test]
        public void TwoFloors_WithStair_UsesStairLength()
        {
            BuildFloors(withStair: true);
            var sample = pipeline.Evaluate(
                profile,
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 4.5f, 0f),
                1f,
                out var context);

            Assert.IsTrue(context.UsedGraph, "Treppe muss den Decken-Cut ersetzen");
            Assert.Greater(context.HearingDistance, 7f);
            Assert.AreEqual(0f, context.OcclusionAmount);
            Assert.Greater(sample.Volume, 0.2f);
        }

        [Test]
        public void DoorFrameGraze_IsNotFullyOccluded()
        {
            BuildGrazeDoor();
            pipeline.Evaluate(
                profile,
                new Vector3(-4f, 1f, 0.45f),
                new Vector3(4f, 1f, 0.45f),
                1f,
                out var context);

            Assert.IsTrue(context.HasPortal || context.OcclusionAmount < 1f);
            Assert.Less(context.OcclusionAmount, 1f);
        }

        [Test]
        public void Preflight_ReportsZoneWithoutPortal()
        {
            Zone("Alone", Vector3.zero, new Vector3(2f, 2f, 2f));
            Physics.SyncTransforms();
            VoiceGraph.MarkDirty();
            var warnings = new List<string>();
            VoiceGraph.CollectPreflight(warnings);
            Assert.IsTrue(warnings.Exists(w => w.Contains("Zone ohne Portal")));
        }

        [Test]
        public void HopLength_UsesTravelLengthWhenLonger()
        {
            Assert.AreEqual(3f, VoiceGraph.HopLength(Vector3.zero, Vector3.forward * 3f, null));
        }

        [Test]
        public void FurtherRoom_IsQuieterThanHall_AlongWalkPath()
        {
            BuildHallAndTwoRooms();
            var speaker = new Vector3(7f, 1f, 0f);
            var inHall = pipeline.Evaluate(profile, new Vector3(0f, 1f, 0f), speaker, 1f, out var hallContext);
            var inFarRoom = pipeline.Evaluate(profile, new Vector3(7f, 1f, 8f), speaker, 1f, out var farContext);

            Assert.IsTrue(hallContext.UsedGraph);
            Assert.IsTrue(farContext.UsedGraph);
            Assert.Greater(farContext.HearingDistance, hallContext.HearingDistance);
            Assert.Less(inFarRoom.Volume, inHall.Volume);
        }

        [Test]
        public void JustOutsideZone_SnapsToNearest_AndKeepsGraph()
        {
            BuildHallAndTwoRooms();
            pipeline.Evaluate(
                profile,
                new Vector3(0f, 3.2f, 0f),
                new Vector3(7f, 1f, 0f),
                1f,
                out var context);

            Assert.IsNotNull(context.ListenerZone);
            Assert.IsTrue(context.UsedGraph);
        }

        [Test]
        public void ApparentDirection_PointsAtFirstPortal_WhenGraphWins()
        {
            BuildLHall(openDoor: true);
            pipeline.Evaluate(
                profile,
                new Vector3(-5f, 1f, 0f),
                new Vector3(4f, 1f, 8f),
                1f,
                out var context);

            Assert.IsTrue(context.UsedGraph);
            Vector3 expected = (context.ApparentPosition - context.ListenerPosition).normalized;
            Assert.That(Vector3.Dot(context.ApparentDirection, expected), Is.GreaterThan(0.98f));
            Assert.That(context.ApparentPosition.x, Is.EqualTo(0f).Within(0.6f));
        }

        private VoicePortal BuildLHall(bool openDoor)
        {
            Zone("RoomA", new Vector3(-4.5f, 1.5f, 0f), new Vector3(7f, 3f, 6f));
            Zone("RoomB", new Vector3(4f, 1.5f, 4f), new Vector3(6f, 3f, 16f));
            Box("Wall", new Vector3(0f, 1.5f, 6f), new Vector3(0.4f, 3f, 10f), trigger: false);
            var portal = Portal(
                "Door",
                new Vector3(0f, 1.5f, 0f),
                new Vector3(0.4f, 2.4f, 1.6f),
                Vector3.right,
                openDoor ? 1f : 0f);
            Physics.SyncTransforms();
            VoiceGraph.MarkDirty();
            VoiceGraph.Rebuild();
            return portal;
        }

        private void BuildFloors(bool withStair)
        {
            Zone("Lower", new Vector3(0f, 1.5f, 0f), new Vector3(16f, 3f, 16f));
            Zone("Upper", new Vector3(0f, 4.5f, 0f), new Vector3(16f, 3f, 16f));
            Box("Ceiling", new Vector3(0f, 3f, 0f), new Vector3(20f, 0.3f, 20f), trigger: false);
            if (withStair)
            {
                var stair = Portal(
                    "Stair",
                    new Vector3(6f, 3f, 0f),
                    new Vector3(1.5f, 0.4f, 1.5f),
                    Vector3.up,
                    openness: 1f);
                stair.SetKind(VoicePortalKind.Stair);
                stair.SetTravelLength(8f);
            }

            Physics.SyncTransforms();
            VoiceGraph.MarkDirty();
            VoiceGraph.Rebuild();
        }

        private void BuildHallAndTwoRooms()
        {
            Zone("Hall", new Vector3(0f, 1.5f, 4f), new Vector3(4f, 3f, 12f));
            Zone("NearRoom", new Vector3(6f, 1.5f, 0f), new Vector3(6f, 3f, 6f));
            Zone("FarRoom", new Vector3(6f, 1.5f, 8f), new Vector3(6f, 3f, 6f));
            Box("Split", new Vector3(2f, 1.5f, 4f), new Vector3(0.4f, 3f, 12f), trigger: false);
            Portal("DoorNear", new Vector3(2f, 1.5f, 0f), new Vector3(0.4f, 2.4f, 1.6f), Vector3.right, 1f);
            Portal("DoorFar", new Vector3(2f, 1.5f, 8f), new Vector3(0.4f, 2.4f, 1.6f), Vector3.right, 1f);
            Physics.SyncTransforms();
            VoiceGraph.MarkDirty();
            VoiceGraph.Rebuild();
        }

        private void BuildGrazeDoor()
        {
            Box("Frame", new Vector3(0f, 1.5f, 0.55f), new Vector3(0.4f, 3f, 0.3f), trigger: false);
            Portal(
                "DoorGap",
                new Vector3(0f, 1.5f, 0f),
                new Vector3(0.3f, 2.2f, 0.9f),
                Vector3.right,
                openness: 1f);
            Physics.SyncTransforms();
        }

        private float ThroughWallVolume(Vector3 listener, Vector3 speaker)
        {
#if UNITY_6000_5_OR_NEWER
            var portals = Object.FindObjectsByType<VoicePortal>(FindObjectsInactive.Exclude);
#else
            var portals = Object.FindObjectsByType<VoicePortal>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i] != null) portals[i].gameObject.SetActive(false);
            }

            VoiceGraph.MarkDirty();
            VoiceGraph.Rebuild();
            var sample = pipeline.Evaluate(profile, listener, speaker, 1f);
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i] != null) portals[i].gameObject.SetActive(true);
            }

            VoiceGraph.MarkDirty();
            return sample.Volume;
        }

        private float ClearLineVolume(Vector3 listener, Vector3 speaker)
        {
#if UNITY_6000_5_OR_NEWER
            var walls = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude);
#else
            var walls = Object.FindObjectsByType<BoxCollider>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            var disabled = new List<GameObject>();
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null || walls[i].isTrigger) continue;
                if (walls[i].GetComponentInParent<VoicePortal>() != null) continue;
                walls[i].gameObject.SetActive(false);
                disabled.Add(walls[i].gameObject);
            }

            VoiceGraph.MarkDirty();
            var sample = pipeline.Evaluate(profile, listener, speaker, 1f);
            for (int i = 0; i < disabled.Count; i++)
            {
                if (disabled[i] != null) disabled[i].SetActive(true);
            }

            VoiceGraph.MarkDirty();
            return sample.Volume;
        }

        private VoiceZone Zone(string name, Vector3 position, Vector3 size)
        {
            var go = Box(name, position, size, trigger: true);
            return go.AddComponent<VoiceZone>();
        }

        private VoicePortal Portal(
            string name,
            Vector3 position,
            Vector3 size,
            Vector3 forward,
            float openness)
        {
            var go = Box(name, position, size, trigger: false);
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var portal = go.AddComponent<VoicePortal>();
            portal.Openness = openness;
            return portal;
        }

        private T Track<T>(T asset) where T : Object
        {
            created.Add(asset);
            return asset;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, bool trigger)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = trigger;
            created.Add(go);
            return go;
        }
    }
}
