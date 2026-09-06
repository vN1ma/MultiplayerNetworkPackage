using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Baut eine geschlossene Hoertest-Szene: Flur, Raum rechts mit Tuer,
    /// Treppe nach oben, oben dasselbe. Waende ueberlappen an jeder Ecke.
    /// </summary>
    [AddComponentMenu("Earshot Voice/Hearing Test Level")]
    public sealed class HearingTestLevel : MonoBehaviour
    {
        private const float T = 0.3f;
        private const float H = 3f;

        private Material wallMat;
        private Material floorMat;
        private Material ceilingMat;
        private Material doorMat;
        private Material stairMat;
        private Material speakerMat;
        private Transform geometry;

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ClearChildren();
            Build();
        }

        private void Start()
        {
            if (transform.childCount == 0) Build();
        }

        public void Build()
        {
            if (geometry != null) ClearChildren();
            CreateMaterials();
            geometry = new GameObject("Geometry").transform;
            geometry.SetParent(transform, false);

            BuildGroundHall();
            BuildGroundRoom();
            BuildStairs();
            BuildUpperLanding();
            BuildUpperRoom();
            BuildZonesAndPortals();
            BuildPlayer();
            BuildLight();
        }

        private void BuildGroundHall()
        {
            // Innen: x -1.5..1.5, y 0..3, z 0..16
            float x0 = -1.5f, x1 = 1.5f, y0 = 0f, y1 = H, z0 = 0f, z1 = 16f;
            float doorZ = 4.5f;

            Floor("HallFloor", x0, x1, z0, z1, y0, -1);
            // Decke nur bis zum Treppenschacht
            Floor("HallCeiling", x0, x1, z0, 11.5f, y1, 1);

            WallX("HallWallLeft", x0, y0, y1, z0, z1, -1, null);
            WallX("HallWallRight", x1, y0, y1, z0, z1, 1, DoorOpening(doorZ));
            WallZ("HallWallStart", z0, x0, x1, y0, y1, -1, null);
            // Nur der Sockel: oben muss man vom letzten Tritt aufs Podest laufen.
            Box(
                "HallWallEndLow",
                new Vector3(0f, 1.35f, z1 + T * 0.5f),
                new Vector3((x1 - x0) + 2f * T, 2.7f, T),
                wallMat,
                geometry);
        }

        private void BuildGroundRoom()
        {
            float x0 = 1.5f, x1 = 8.5f, y0 = 0f, y1 = H, z0 = 2f, z1 = 8f;
            float doorZ = 4.5f;

            Floor("Room1Floor", x0, x1, z0, z1, y0, -1);
            Floor("Room1Ceiling", x0, x1, z0, z1, y1, 1);
            WallX("Room1WallLeft", x0, y0, y1, z0, z1, -1, DoorOpening(doorZ));
            WallX("Room1WallRight", x1, y0, y1, z0, z1, 1, null);
            WallZ("Room1WallSouth", z0, x0, x1, y0, y1, -1, null);
            WallZ("Room1WallNorth", z1, x0, x1, y0, y1, 1, null);
        }

        private void BuildStairs()
        {
            const int steps = 10;
            const float rise = 0.3f;
            const float run = 0.45f;
            float z = 11.5f;
            var parent = new GameObject("Stairs").transform;
            parent.SetParent(geometry, false);

            for (int i = 0; i < steps; i++)
            {
                float top = (i + 1) * rise;
                Box(
                    "Step" + i,
                    new Vector3(0f, top - rise * 0.5f, z + run * 0.5f),
                    new Vector3(3f, rise, run + 0.02f),
                    stairMat,
                    parent);
                z += run;
            }

            // Seitliche Schachtwaende, damit der Schacht oben nicht seitlich offen ist
            Box("WellLeft", new Vector3(-1.5f - T * 0.5f, 4.5f, 13.75f), new Vector3(T, 3f, 4.6f), wallMat, geometry);
            Box("WellRight", new Vector3(1.5f + T * 0.5f, 4.5f, 13.75f), new Vector3(T, 3f, 4.6f), wallMat, geometry);
        }

        private void BuildUpperLanding()
        {
            float x0 = -1.5f, x1 = 1.5f, y0 = H, y1 = H + H, z0 = 16f, z1 = 18.5f;
            float doorZ = 17f;

            Floor("LandingFloor", x0, x1, z0 - 0.15f, z1, y0, -1);
            Floor("LandingCeiling", x0, x1, z0, z1, y1, 1);
            WallX("LandingWallLeft", x0, y0, y1, z0, z1, -1, null);
            WallX("LandingWallRight", x1, y0, y1, z0, z1, 1, DoorOpening(doorZ));
            WallZ("LandingWallBack", z1, x0, x1, y0, y1, 1, null);
            // Zur Treppe hin offen; Geländer-Sturz über dem Schacht
            Box(
                "LandingRail",
                new Vector3(0f, y0 + 1.1f, 11.5f + T * 0.5f),
                new Vector3(3f + 2f * T, 2.2f, T),
                wallMat,
                geometry);
        }

        private void BuildUpperRoom()
        {
            float x0 = 1.5f, x1 = 8.5f, y0 = H, y1 = H + H, z0 = 15f, z1 = 19.5f;
            float doorZ = 17f;

            Floor("Room2Floor", x0, x1, z0, z1, y0, -1);
            Floor("Room2Ceiling", x0, x1, z0, z1, y1, 1);
            WallX("Room2WallLeft", x0, y0, y1, z0, z1, -1, DoorOpening(doorZ));
            WallX("Room2WallRight", x1, y0, y1, z0, z1, 1, null);
            WallZ("Room2WallSouth", z0, x0, x1, y0, y1, -1, null);
            WallZ("Room2WallNorth", z1, x0, x1, y0, y1, 1, null);
        }

        private void BuildZonesAndPortals()
        {
            Zone("Zone_Flur", new Vector3(0f, 1.5f, 8f), new Vector3(2.9f, 2.8f, 15.6f));
            Zone("Zone_RaumUnten", new Vector3(5f, 1.5f, 5f), new Vector3(6.8f, 2.8f, 5.8f));
            Zone("Zone_Podest", new Vector3(0f, 4.5f, 17.25f), new Vector3(2.9f, 2.8f, 2.3f));
            Zone("Zone_RaumOben", new Vector3(5f, 4.5f, 17.25f), new Vector3(6.8f, 2.8f, 4.3f));

            var door1 = MakeDoor("TuerUnten", new Vector3(1.5f, 1.1f, 4.5f), Vector3.right);
            var door2 = MakeDoor("TuerOben", new Vector3(1.5f, 4.1f, 17f), Vector3.right);

            var stair = Box(
                "TreppenPortal",
                new Vector3(0f, 1.6f, 13.75f),
                new Vector3(2.8f, 3.1f, 4.4f),
                stairMat,
                geometry,
                trigger: true);
            var stairMesh = stair.GetComponent<MeshRenderer>();
            if (stairMesh != null) stairMesh.enabled = false;
            var stairPortal = stair.AddComponent<VoicePortal>();
            stairPortal.SetKind(VoicePortalKind.Stair);
            stairPortal.SetTravelLength(6f);
            stairPortal.Openness = 1f;

            Speaker("TonUnten", new Vector3(6.4f, 1.35f, 5f), "raum-unten");
            Speaker("TonOben", new Vector3(6.4f, 4.35f, 17.2f), "raum-oben");

            door1.name = "TuerUnten";
            door2.name = "TuerOben";
        }

        private void BuildPlayer()
        {
            var player = new GameObject("HoertestSpieler");
            player.SetActive(false);
            player.transform.SetParent(transform, false);
            player.transform.position = new Vector3(0f, 0.1f, 1.6f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.38f;
            controller.slopeLimit = 50f;

            var head = new GameObject("Kopf");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var camera = head.AddComponent<Camera>();
            camera.nearClipPlane = 0.08f;
            head.AddComponent<AudioListener>();

            var voice = player.AddComponent<EarshotProximityVoice>();
            voice.SetJoinVoiceChannel(false);
            voice.SetVoiceAnchor(head.transform);

            player.AddComponent<HearingTestWalker>();
            player.AddComponent<HearingTestInteract>();
            player.AddComponent<HearingTestHud>();
            player.SetActive(true);
        }

        private void BuildLight()
        {
            var lightObject = new GameObject("Licht");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
        }

        private static Rect DoorOpening(float doorZ)
        {
            return new Rect(doorZ - 0.6f, 0f, 1.2f, 2.2f);
        }

        private HearingTestDoor MakeDoor(string name, Vector3 center, Vector3 outward)
        {
            var root = new GameObject(name);
            root.transform.SetParent(geometry, false);
            root.transform.position = center;
            root.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);

            var frame = Box(
                "Oeffnung",
                center,
                new Vector3(0.22f, 2.2f, 1.2f),
                doorMat,
                root.transform,
                trigger: true);
            frame.transform.rotation = root.transform.rotation;
            var portal = frame.AddComponent<VoicePortal>();
            portal.Openness = 1f;

            var leaf = Box(
                "Blatt",
                center,
                new Vector3(0.12f, 2.15f, 1.16f),
                doorMat,
                root.transform);
            leaf.transform.rotation = root.transform.rotation;
            var door = root.AddComponent<HearingTestDoor>();
            door.Bind(portal, leaf.transform, leaf.GetComponent<Collider>());
            return door;
        }

        private void Speaker(string name, Vector3 position, string id)
        {
            var go = Box(name, position, new Vector3(0.45f, 0.7f, 0.45f), speakerMat, transform);
            go.SetActive(false);
            var speaker = go.AddComponent<VoiceTestSpeaker>();
            speaker.SetSpeakerId(id);
            go.SetActive(true);
            go.name = name;
        }

        private VoiceZone Zone(string name, Vector3 center, Vector3 size)
        {
            var go = Box(name, center, size, wallMat, geometry, trigger: true);
            go.GetComponent<MeshRenderer>().enabled = false;
            var zone = go.AddComponent<VoiceZone>();
            return zone;
        }

        private void Floor(string name, float x0, float x1, float z0, float z1, float yInner, float outY)
        {
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            float cy = yInner + outY * T * 0.5f;
            var mat = outY < 0f ? floorMat : ceilingMat;
            Box(name, new Vector3(cx, cy, cz), new Vector3((x1 - x0) + 2f * T, T, (z1 - z0) + 2f * T), mat, geometry);
        }

        private void WallX(
            string name,
            float xInner,
            float y0,
            float y1,
            float z0,
            float z1,
            float outX,
            Rect? opening)
        {
            float x = xInner + outX * T * 0.5f;
            if (!opening.HasValue)
            {
                Box(
                    name,
                    new Vector3(x, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                    new Vector3(T, (y1 - y0) + 2f * T, (z1 - z0) + 2f * T),
                    wallMat,
                    geometry);
                return;
            }

            Rect hole = opening.Value;
            float holeZ0 = hole.x;
            float holeZ1 = hole.x + hole.width;
            float holeY1 = y0 + hole.height;

            Box(
                name + "_A",
                new Vector3(x, (y0 + y1) * 0.5f, (z0 - T + holeZ0) * 0.5f),
                new Vector3(T, (y1 - y0) + 2f * T, (holeZ0 - (z0 - T))),
                wallMat,
                geometry);
            Box(
                name + "_B",
                new Vector3(x, (y0 + y1) * 0.5f, (holeZ1 + z1 + T) * 0.5f),
                new Vector3(T, (y1 - y0) + 2f * T, ((z1 + T) - holeZ1)),
                wallMat,
                geometry);
            Box(
                name + "_Sturz",
                new Vector3(x, (holeY1 + y1 + T) * 0.5f, (holeZ0 + holeZ1) * 0.5f),
                new Vector3(T, (y1 + T) - holeY1, hole.width),
                wallMat,
                geometry);
        }

        private void WallZ(
            string name,
            float zInner,
            float x0,
            float x1,
            float y0,
            float y1,
            float outZ,
            Rect? opening)
        {
            float z = zInner + outZ * T * 0.5f;
            if (!opening.HasValue)
            {
                Box(
                    name,
                    new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z),
                    new Vector3((x1 - x0) + 2f * T, (y1 - y0) + 2f * T, T),
                    wallMat,
                    geometry);
                return;
            }

            Rect hole = opening.Value;
            float holeX0 = hole.x;
            float holeX1 = hole.x + hole.width;
            float holeY1 = y0 + hole.height;

            Box(
                name + "_A",
                new Vector3((x0 - T + holeX0) * 0.5f, (y0 + y1) * 0.5f, z),
                new Vector3(holeX0 - (x0 - T), (y1 - y0) + 2f * T, T),
                wallMat,
                geometry);
            Box(
                name + "_B",
                new Vector3((holeX1 + x1 + T) * 0.5f, (y0 + y1) * 0.5f, z),
                new Vector3((x1 + T) - holeX1, (y1 - y0) + 2f * T, T),
                wallMat,
                geometry);
            Box(
                name + "_Sturz",
                new Vector3((holeX0 + holeX1) * 0.5f, (holeY1 + y1 + T) * 0.5f, z),
                new Vector3(hole.width, (y1 + T) - holeY1, T),
                wallMat,
                geometry);
        }

        private GameObject Box(
            string name,
            Vector3 center,
            Vector3 size,
            Material material,
            Transform parent,
            bool trigger = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            var collider = go.GetComponent<BoxCollider>();
            if (collider != null) collider.isTrigger = trigger;
            return go;
        }

        private void CreateMaterials()
        {
            wallMat = MakeMat(new Color(0.62f, 0.6f, 0.56f));
            floorMat = MakeMat(new Color(0.28f, 0.27f, 0.25f));
            ceilingMat = MakeMat(new Color(0.78f, 0.76f, 0.72f));
            doorMat = MakeMat(new Color(0.45f, 0.28f, 0.16f));
            stairMat = MakeMat(new Color(0.5f, 0.38f, 0.24f));
            speakerMat = MakeMat(new Color(0.85f, 0.2f, 0.18f));
        }

        private static Material MakeMat(Color color)
        {
            var shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            geometry = null;
        }
    }
}
