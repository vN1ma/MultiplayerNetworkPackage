using Earshot.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Earshot.Samples
{
    /// <summary>
    /// Baut die kleinste Szene, in der man Proximity Voice und Occlusion hoeren kann:
    /// zwei Raeume, eine Wand, eine Tuer.
    /// <para>
    /// Im Inspector dieses Objekts rechtsklicken und "Build Quick Start Layout" waehlen,
    /// danach die Szene speichern. Nicht im Play-Modus bauen: Die Tuer braucht ein
    /// NetworkObject, das schon in der Szene liegt, bevor die Sitzung startet.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Samples/Quick Start Layout")]
    public class QuickStartLayout : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Vorhandene Kinder dieses Objekts vor dem Aufbau entfernen.")]
        private bool replaceExisting = true;

        [ContextMenu("Build Quick Start Layout")]
        public void Build()
        {
            if (Application.isPlaying)
            {
                CoopLog.Warn(
                    "Quick Start Layout nicht im Play-Modus bauen. Zuerst im Editor aufbauen " +
                    "und die Szene speichern, sonst ist die Tuer kein gueltiges NetworkObject.");
                return;
            }

            if (replaceExisting)
            {
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(transform.GetChild(i).gameObject);
                }
            }

            Cube("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(16f, 0.2f, 10f));

            WallsForRoom("RoomA", new Vector3(-4.5f, 1.5f, 0f), 7f, 6f, -1f);
            WallsForRoom("RoomB", new Vector3(4.5f, 1.5f, 0f), 7f, 6f, 1f);

            // Die Trennwand hat in der Mitte eine Luecke fuer die Tuer.
            Cube("DividerNorth", new Vector3(0f, 1.5f, 2.25f), new Vector3(0.3f, 3f, 2.5f));
            Cube("DividerSouth", new Vector3(0f, 1.5f, -2.25f), new Vector3(0.3f, 3f, 2.5f));

            CreateDoor();
            CreateZone("ZoneA", new Vector3(-4.5f, 1.5f, 0f), new Vector3(7f, 3f, 6f));
            CreateZone("ZoneB", new Vector3(4.5f, 1.5f, 0f), new Vector3(7f, 3f, 6f));

            var spawnA = new GameObject("SpawnA");
            spawnA.transform.SetParent(transform, false);
            spawnA.transform.position = new Vector3(-4.5f, 0.1f, 0f);

            var spawnB = new GameObject("SpawnB");
            spawnB.transform.SetParent(transform, false);
            spawnB.transform.position = new Vector3(4.5f, 0.1f, 0f);

            var spawnerGo = new GameObject("PlayerSpawner");
            spawnerGo.transform.SetParent(transform, false);
            var spawner = spawnerGo.AddComponent<PlayerSpawner>();
            spawner.SetSpawnPoints(spawnA.transform, spawnB.transform);

            var menu = new GameObject("CoopQuickMenu");
            menu.transform.SetParent(transform, false);
            menu.AddComponent<CoopQuickMenu>();
            menu.AddComponent<VoiceDebugOverlay>();

            CoopLog.Info(
                "Quick Start Layout gebaut. Szene speichern, dann Tools > Earshot > Setup. " +
                "Ein Spieler startet links, der andere rechts der Tuer.");
        }

        private void WallsForRoom(string prefix, Vector3 center, float width, float depth, float outerSign)
        {
            const float wall = 0.3f;
            const float height = 3f;

            Cube(prefix + "_Back", center + new Vector3(0f, height * 0.5f, depth * 0.5f), new Vector3(width, height, wall));
            Cube(prefix + "_Front", center + new Vector3(0f, height * 0.5f, -depth * 0.5f), new Vector3(width, height, wall));
            Cube(
                prefix + "_Outer",
                center + new Vector3(outerSign * width * 0.5f, height * 0.5f, 0f),
                new Vector3(wall, height, depth));
        }

        private void CreateDoor()
        {
            // Als Wurzelobjekt, weil ein NetworkObject nicht unter einem normalen
            // Transform haengen darf, ohne dass Netcode sich beschwert.
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Door";
            door.transform.position = new Vector3(0f, 1.2f, 0f);
            door.transform.localScale = new Vector3(0.2f, 2.4f, 1.2f);
            door.AddComponent<VoicePortal>();
            door.AddComponent<NetworkObject>();
            door.AddComponent<NetworkDoor>();
        }

        private void CreateZone(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = center;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.AddComponent<VoiceZone>();
        }

        private GameObject Cube(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }
    }
}
