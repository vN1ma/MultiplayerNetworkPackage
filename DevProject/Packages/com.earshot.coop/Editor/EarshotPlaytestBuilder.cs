using System.IO;
using Earshot.Voice;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Earshot.EditorTools
{
    /// <summary>
    /// Baut den kompletten Testraum: zwei Zimmer, eine Tuer, ein lauffaehiger Spieler.
    /// Genau das, was man braucht, um mit einem Freund Proximity Chat auszuprobieren,
    /// ohne selbst Level zu bauen.
    /// </summary>
    public static class EarshotPlaytestBuilder
    {
        public const string ScenePath = "Assets/Scenes/EarshotPlaytest.unity";
        public const string MppmScenePath = "Assets/Scenes/EarshotMppmDuo.unity";
        public const string PlayerPath = "Assets/Earshot/DemoPlayer.prefab";

        // Waende dicker als die Tuer-Oeffnung, damit der Schall-Strahl nirgends
        // durch eine Fuge rutscht. Earshot zaehlt nur, was der Raycast trifft.
        private const float Wall = 0.4f;
        private const float Height = 3f;
        private const float Room = 6f;
        private const float DoorWidth = 1f;
        private const float DoorHeight = 2.2f;

        [MenuItem("Tools/Earshot/Testraum bauen")]
        public static void BuildFromMenu()
        {
            Build();
        }

        [MenuItem("Tools/Earshot/Testraum bauen", true)]
        private static bool ValidateBuild()
        {
            return !EditorApplication.isPlaying;
        }

        [MenuItem("Tools/Earshot/MPPM-Testraum bauen (2 Spieler, 1 PC)")]
        public static void BuildMppmFromMenu()
        {
            BuildMppm();
        }

        [MenuItem("Tools/Earshot/MPPM-Testraum bauen (2 Spieler, 1 PC)", true)]
        private static bool ValidateBuildMppm()
        {
            return !EditorApplication.isPlaying;
        }

        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Play laeuft noch",
                    "Zuerst Play beenden, dann nochmal Tools > Earshot > Testraum bauen.",
                    "OK");
                return;
            }
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Earshot");

            var player = CreateOrLoadPlayerPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildWorld(player);

            SaveSceneAndBakeNetworkObjectHashes(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);

            EditorUtility.DisplayDialog(
                "Testraum steht",
                "Die Szene 'EarshotPlaytest' ist offen.\n\n" +
                "Allein testen (ein einziger Start): Play druecken. In Zimmer B laeuft ein " +
                "Testton durch dieselbe Klang-Pipeline wie eine echte Stimme - Tuer auf/zu " +
                "und naeher/weiter weg zeigen Distanz, Wand und Muffling, ganz ohne Account " +
                "und ohne zweiten Spieler.\n\n" +
                "Zwei Spieler auf diesem Rechner (ohne Account, ohne Vivox):\n" +
                "1. Play im Editor.\n" +
                "2. File > Build Profiles > Build, EXE starten.\n" +
                "3. In der EXE: 'Beitreten (dieser PC)'.\n\n" +
                "Echte Stimme mit zwei Fenstern auf diesem PC: EXE zweimal starten, beim " +
                "zweiten Start '-profile p2' anhaengen (Verknuepfung > Eigenschaften > Ziel), " +
                "dann einmal 'Spiel hosten (Internet)' und einmal 'Beitreten (Internet)' mit " +
                "dem Code.\n\n" +
                "Freund im Internet: Unity Cloud verknuepfen, neu bauen,\n" +
                "dann 'Spiel hosten (Internet)' und den Code schicken.\n\n" +
                "Steuerung: WASD, Maus, E, Esc (Pause), F1, F3.",
                "OK");
        }

        /// <summary>
        /// Eigene, aufgeraeumte Szene fuer den Multiplayer Play Mode: kein Testton, kein
        /// Menue mit vielen Knoepfen. Die Haupt-Editorinstanz hostet von selbst ueber das
        /// Internet, der virtuelle Spieler bekommt nur ein Feld fuer den Code. Damit laesst
        /// sich echtes Vivox mit zwei Fenstern an einem PC testen, ohne zu builden.
        /// </summary>
        public static void BuildMppm()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Play laeuft noch",
                    "Zuerst Play beenden, dann nochmal Tools > Earshot > MPPM-Testraum bauen.",
                    "OK");
                return;
            }

            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Earshot");

            var player = CreateOrLoadPlayerPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildMppmWorld(player);

            SaveSceneAndBakeNetworkObjectHashes(scene, MppmScenePath);
            AddToBuildSettings(MppmScenePath);

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(MppmScenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);

            EditorUtility.DisplayDialog(
                "MPPM-Testraum steht",
                "Die Szene 'EarshotMppmDuo' ist offen.\n\n" +
                "1. Window > Multiplayer Play Mode > mindestens einen virtuellen Spieler " +
                "anlegen und aktivieren.\n" +
                "2. Play druecken. Die Haupt-Instanz hostet von selbst ueber das Internet " +
                "und zeigt links oben den Code.\n" +
                "3. Im Game-View des virtuellen Spielers den Code eingeben, 'Beitreten'.\n\n" +
                "Kein Testton, kein Auswahlbildschirm - echtes Vivox zwischen zwei Fenstern " +
                "auf diesem PC.\n\n" +
                "Steuerung: WASD, Maus, E, Esc (Pause), F1, F3.",
                "OK");
        }

        private static GameObject CreateOrLoadPlayerPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (existing != null) return existing;

            var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "DemoPlayer";
            Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());
            Object.DestroyImmediate(root.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(root.GetComponent<MeshFilter>());

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(visual.GetComponent<CapsuleCollider>());

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var camera = head.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            head.AddComponent<AudioListener>();

            root.AddComponent<NetworkObject>();

            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;

            var coopPlayer = root.AddComponent<CoopPlayer>();
            var coopSo = new SerializedObject(coopPlayer);
            coopSo.FindProperty("voiceAnchor").objectReferenceValue = head.transform;
            coopSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<Interactor>();
            root.AddComponent<DemoFirstPerson>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildWorld(GameObject playerPrefab)
        {
            float roomCenterX = BuildSharedRoom();

            CreateVoiceTestSpeaker(roomCenterX);

            var menu = new GameObject("Earshot UI");
            menu.AddComponent<CoopQuickMenu>();
            menu.AddComponent<CoopPauseMenu>();
            var overlay = menu.AddComponent<VoiceDebugOverlay>();
            var overlaySo = new SerializedObject(overlay);
            overlaySo.FindProperty("visible").boolValue = true;
            overlaySo.ApplyModifiedPropertiesWithoutUndo();

            var managerGo = CreateNetworkManager(playerPrefab);
            managerGo.AddComponent<PlaytestLocalHost>();
        }

        /// <summary>
        /// Dieselbe Zwei-Zimmer-Welt wie im normalen Testraum, aber ohne Testton und ohne
        /// das Menue mit vielen Knoepfen - nur die Rollen-Anzeige oben links.
        /// </summary>
        private static void BuildMppmWorld(GameObject playerPrefab)
        {
            BuildSharedRoom();

            var ui = new GameObject("Earshot UI (MPPM)");
            ui.AddComponent<CoopPauseMenu>();
            ui.AddComponent<VoiceDebugOverlay>();
            ui.AddComponent<MppmDuoTester>();

            CreateNetworkManager(playerPrefab);
        }

        /// <summary>Haus, Tuer, Zonen und Spawner - identisch fuer alle Testraum-Varianten.</summary>
        private static float BuildSharedRoom()
        {
            var light = new GameObject("Directional Light");
            var sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var lobby = new GameObject("LobbyCamera");
            lobby.transform.position = new Vector3(0f, 8f, -12f);
            lobby.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            lobby.AddComponent<Camera>();
            lobby.AddComponent<AudioListener>();
            lobby.AddComponent<LobbyCamera>();

            BuildSealedHouse();
            CreateDoor();

            float roomCenterX = (Room + Wall) * 0.5f;
            CreateZone("ZoneA", "Zimmer A", new Vector3(-roomCenterX, Height * 0.5f, 0f), new Vector3(Room - 0.2f, Height - 0.2f, Room - 0.2f));
            CreateZone("ZoneB", "Zimmer B", new Vector3(roomCenterX, Height * 0.5f, 0f), new Vector3(Room - 0.2f, Height - 0.2f, Room - 0.2f));

            var spawnA = new GameObject("SpawnA");
            spawnA.transform.position = new Vector3(-roomCenterX, 0.1f, 0f);
            spawnA.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var spawnB = new GameObject("SpawnB");
            spawnB.transform.position = new Vector3(roomCenterX, 0.1f, 0f);
            spawnB.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            var spawnerGo = new GameObject("PlayerSpawner");
            var spawner = spawnerGo.AddComponent<PlayerSpawner>();
            spawner.SetSpawnPoints(spawnA.transform, spawnB.transform);

            return roomCenterX;
        }

        private static GameObject CreateNetworkManager(GameObject playerPrefab)
        {
            var managerGo = new GameObject("NetworkManager");
            var manager = managerGo.AddComponent<NetworkManager>();
            var transport = managerGo.AddComponent<UnityTransport>();
            managerGo.AddComponent<CoopBootstrap>();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = playerPrefab;

            RegisterPrefab(playerPrefab);
            return managerGo;
        }

        /// <summary>
        /// Ein Testton in Zimmer B, der durch dieselbe Pipeline wie eine echte Stimme
        /// laeuft. Damit laesst sich Distanz, Tuer und Wand mit einem einzigen Spielstart
        /// pruefen - ohne zweiten Spieler, ohne Vivox. Bleibt automatisch aus, sobald ein
        /// echter Mitspieler ueber's Internet beitritt.
        /// </summary>
        private static void CreateVoiceTestSpeaker(float roomCenterX)
        {
            var speaker = new GameObject("Voice Test Speaker (Zimmer B)");
            speaker.transform.position = new Vector3(roomCenterX, 1.6f, 0f);
            speaker.AddComponent<VoiceTestSpeaker>();
        }

        /// <summary>
        /// Zwei Zimmer, die sich an jeder Kante ueberlappen. Keine Fuge, durch die ein
        /// Raycast rutschen koennte - genau das war vorher der Grund, warum die Tuer
        /// akustisch nichts tat.
        /// </summary>
        private static void BuildSealedHouse()
        {
            float spanX = Room * 2f + Wall * 3f;
            float spanZ = Room + Wall * 2f;
            float wallCenterY = Height * 0.5f;
            float halfRoom = Room * 0.5f;

            Cube("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(spanX, 0.2f, spanZ));
            Cube("Ceiling", new Vector3(0f, Height + 0.1f, 0f), new Vector3(spanX, 0.2f, spanZ));

            Cube("WallWest", new Vector3(-(Room + Wall), wallCenterY, 0f), new Vector3(Wall, Height, spanZ));
            Cube("WallEast", new Vector3(Room + Wall, wallCenterY, 0f), new Vector3(Wall, Height, spanZ));
            Cube("WallNorth", new Vector3(0f, wallCenterY, halfRoom + Wall * 0.5f), new Vector3(spanX, Height, Wall));
            Cube("WallSouth", new Vector3(0f, wallCenterY, -(halfRoom + Wall * 0.5f)), new Vector3(spanX, Height, Wall));

            float openingHalf = DoorWidth * 0.5f;
            float dividerEnd = halfRoom + Wall;
            float dividerLength = dividerEnd - openingHalf + 0.05f;
            float dividerCenterZ = openingHalf + dividerLength * 0.5f - 0.05f;

            Cube("DividerNorth", new Vector3(0f, wallCenterY, dividerCenterZ), new Vector3(Wall, Height, dividerLength));
            Cube("DividerSouth", new Vector3(0f, wallCenterY, -dividerCenterZ), new Vector3(Wall, Height, dividerLength));

            float lintelHeight = Height - DoorHeight;
            Cube(
                "Lintel",
                new Vector3(0f, DoorHeight + lintelHeight * 0.5f, 0f),
                new Vector3(Wall, lintelHeight, DoorWidth + 0.2f));
        }

        private static void CreateDoor()
        {
            // Scharnier an der Suedkante der Oeffnung, Fluegel fuellt die Luecke und
            // ueberlappt den Rahmen ein paar Zentimeter.
            var hinge = new GameObject("Door");
            hinge.transform.position = new Vector3(0f, DoorHeight * 0.5f, -DoorWidth * 0.5f);

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(hinge.transform, false);
            leaf.transform.localPosition = new Vector3(0f, 0f, DoorWidth * 0.5f);
            leaf.transform.localScale = new Vector3(Wall, DoorHeight, DoorWidth + 0.2f);
            Tint(leaf, new Color(0.82f, 0.38f, 0.12f));

            var portal = hinge.AddComponent<VoicePortal>();
            var portalSo = new SerializedObject(portal);
            portalSo.FindProperty("openness").floatValue = 0f;
            portalSo.FindProperty("closedMuffle").floatValue = 1f;
            portalSo.FindProperty("closedVolume").floatValue = 0.05f;
            portalSo.ApplyModifiedPropertiesWithoutUndo();

            hinge.AddComponent<NetworkObject>();
            var door = hinge.AddComponent<NetworkDoor>();
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("hinge").objectReferenceValue = hinge.transform;
            doorSo.ApplyModifiedPropertiesWithoutUndo();

            RegisterSceneNetworkObject(hinge);
        }

        private static void RegisterSceneNetworkObject(GameObject go)
        {
            // Szene-NetworkObjects werden von Netcode automatisch gespawnt, sobald sie
            // in der geladenen Szene liegen. Kein Extra-Eintrag noetig.
            _ = go;
        }

        /// <summary>
        /// Speichert die Szene und erzwingt danach, dass jedes in der Szene platzierte
        /// NetworkObject (z.B. die Tuer) einen echten GlobalObjectIdHash bekommt.
        /// <para>
        /// <c>NetworkObject.OnValidate()</c> berechnet diesen Hash - aber Unity ruft
        /// OnValidate fuer per Skript per <c>AddComponent</c> erzeugte Komponenten nicht
        /// zuverlaessig auf. Ohne diesen Schritt landet in der gespeicherten Szene ein
        /// Hash von 0, und Netcode meldet beim Beitreten eines zweiten Spielers
        /// "NetworkPrefab hash was not found!" / "Failed to spawn NetworkObject!" fuer
        /// die Tuer. Ein Schliessen-und-wieder-Oeffnen der Szene loest bei Unity
        /// zuverlaessig OnValidate fuer alle Komponenten aus (Teil der normalen
        /// Deserialisierung im Editor), danach wird nochmal gespeichert.
        /// </para>
        /// </summary>
        private static void SaveSceneAndBakeNetworkObjectHashes(UnityEngine.SceneManagement.Scene scene, string scenePath)
        {
            EditorSceneManager.SaveScene(scene, scenePath);
            var reopened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(reopened, scenePath);
        }

        private static void CreateZone(string objectName, string zoneName, Vector3 center, Vector3 size)
        {
            var go = new GameObject(objectName);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<VoiceZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("zoneName").stringValue = zoneName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }

        private static void Tint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null) return;

            var mat = new Material(renderer.sharedMaterial);
            mat.color = color;
            renderer.sharedMaterial = mat;
        }

        private static void RegisterPrefab(GameObject prefab)
        {
            const string listPath = "Assets/DefaultNetworkPrefabs.asset";
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
            if (list == null) return;
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scenePath) return;
            }

            var next = new EditorBuildSettingsScene[scenes.Length + 1];
            for (int i = 0; i < scenes.Length; i++) next[i] = scenes[i];
            next[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = next;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
