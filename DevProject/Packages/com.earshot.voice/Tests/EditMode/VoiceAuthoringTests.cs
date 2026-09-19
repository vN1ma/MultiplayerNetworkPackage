using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Earshot.Voice.Tests
{
    /// <summary>
    /// Phase 2: Autoring-Factory und Raum-Sondierung. Raeume werden aus simplen
    /// Box-Collidern aufgebaut; EditMode-Tests koennen dafuer die Physik nutzen.
    /// </summary>
    public sealed class VoiceAuthoringTests
    {
        private readonly List<Object> created = new List<Object>();

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

        private GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            created.Add(go);
            return go;
        }

        [Test]
        public void ClosedRoom_ProbesExactBounds()
        {
            // Innenraum 4 x 6 m Grundflaeche (x/z), 6 m hoch (y), Marker mittig.
            Box("Boden", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
            Box("Decke", new Vector3(0f, 6.5f, 0f), new Vector3(10f, 1f, 10f));
            Box("WandPX", new Vector3(2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));
            Box("WandMX", new Vector3(-2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));
            Box("WandPZ", new Vector3(0f, 3f, 3.5f), new Vector3(10f, 10f, 1f));
            Box("WandMZ", new Vector3(0f, 3f, -3.5f), new Vector3(10f, 10f, 1f));

            var issues = new List<string>();
            bool ok = VoiceGraphFactory.ProbeRoomBounds(
                new Vector3(0f, 2f, 0f), ~0, 30f, 0.1f, out var bounds, issues);

            Assert.IsTrue(ok, "geschlossener Raum muss sich sondieren lassen");
            Assert.AreEqual(0, issues.Count);
            Assert.AreEqual(3.8f, bounds.size.x, 0.001f, "4 m - 2x Wandabzug");
            Assert.AreEqual(5.8f, bounds.size.z, 0.001f, "6 m - 2x Wandabzug");
            Assert.AreEqual(6f, bounds.size.y, 0.001f, "Boden bis Decke");
            Assert.AreEqual(0f, bounds.center.x, 0.01f);
            Assert.AreEqual(0f, bounds.center.z, 0.01f);
            Assert.AreEqual(3f, bounds.center.y, 0.001f);
        }

        [Test]
        public void OpenSide_ReportsIssue_AndFallsBackToMaxDistance()
        {
            Box("Boden", new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
            Box("Decke", new Vector3(0f, 6.5f, 0f), new Vector3(10f, 1f, 10f));
            Box("WandPX", new Vector3(2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));
            Box("WandMX", new Vector3(-2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));
            Box("WandMZ", new Vector3(0f, 3f, -3.5f), new Vector3(10f, 10f, 1f));
            // +Z bleibt absichtlich offen (Nische/Durchgang).

            var issues = new List<string>();
            bool ok = VoiceGraphFactory.ProbeRoomBounds(
                new Vector3(0f, 2f, 0f), ~0, 30f, 0.1f, out var bounds, issues);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, issues.Count, "genau die offene Seite melden");
            StringAssert.Contains("+Z", issues[0]);
            Assert.AreEqual(30f + 3f - 0.2f, bounds.size.z, 0.001f,
                "offene Seite zaehlt Max-Distanz weiter");
        }

        [Test]
        public void MissingFloor_FailsWithIssue()
        {
            Box("Decke", new Vector3(0f, 6.5f, 0f), new Vector3(10f, 1f, 10f));
            Box("WandPX", new Vector3(2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));
            Box("WandMX", new Vector3(-2.5f, 3f, 0f), new Vector3(1f, 10f, 10f));

            var issues = new List<string>();
            bool ok = VoiceGraphFactory.ProbeRoomBounds(
                new Vector3(0f, 2f, 0f), ~0, 30f, 0.1f, out _, issues);

            Assert.IsFalse(ok, "ohne Boden kein brauchbarer Raum-Innenpunkt");
            Assert.GreaterOrEqual(issues.Count, 1);
            StringAssert.Contains("Boden", issues[0]);
        }

        [Test]
        public void CreateZone_BuildsNamedTriggerBox()
        {
            var bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));

            var zone = VoiceGraphFactory.CreateZone(bounds, "Lobby", null);
            created.Add(zone.gameObject);

            Assert.IsNotNull(zone);
            Assert.AreEqual("AudioZone_Lobby", zone.gameObject.name);
            Assert.AreEqual("Lobby", zone.ZoneName);

            var box = zone.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "Zone braucht einen Collider");
            Assert.IsTrue(box.isTrigger, "Zone-Collider muss Trigger sein");
            Assert.AreEqual(bounds.size, box.size);
            Assert.AreEqual(bounds.center, zone.transform.position);
        }

        [Test]
        public void LinkZones_ConnectsGraphAcrossDistance()
        {
            // Zwei Zonen, kilometerweit auseinander - nur eine Bruecke
            // (explizite Zuweisung) verbindet sie (Phase 1a / E4).
            var zoneA = VoiceGraphFactory.CreateZone(
                new Bounds(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f)), "Flur", null);
            var zoneB = VoiceGraphFactory.CreateZone(
                new Bounds(new Vector3(100f, 2f, 0f), new Vector3(4f, 4f, 4f)), "Themenraum", null);
            created.Add(zoneA.gameObject);
            created.Add(zoneB.gameObject);

            var host = new GameObject("Bruecke");
            host.transform.position = new Vector3(50f, 2f, 0f);
            created.Add(host);

            var portal = host.AddComponent<VoicePortal>();
            portal.SetTravelLength(4f);
            VoiceGraphFactory.LinkZones(portal, zoneA, zoneB);

            VoiceGraph.Rebuild();

            Assert.IsTrue(VoiceGraph.TryResolveSides(portal, out var a, out var b));
            Assert.AreSame(zoneA, a);
            Assert.AreSame(zoneB, b);
            Assert.GreaterOrEqual(VoiceGraph.ConnectionCount, 1);

            var path = new List<VoicePortal>();
            Assert.IsTrue(VoiceGraph.TryFindPath(zoneA, zoneB, path, out _),
                "Bruecke muss einen Graph-Weg liefern");
            Assert.AreEqual(1, path.Count);
            Assert.AreSame(portal, path[0]);
        }
    }
}