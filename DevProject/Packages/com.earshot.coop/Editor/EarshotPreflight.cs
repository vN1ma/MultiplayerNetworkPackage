using Earshot.Voice;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Earshot.EditorTools
{
    /// <summary>
    /// Reine Diagnose. Aendert nichts am Projekt, damit man sie jederzeit gefahrlos
    /// ausfuehren kann.
    /// </summary>
    public sealed class EarshotPreflight : EditorWindow
    {
        private readonly System.Collections.Generic.List<Check> checks =
            new System.Collections.Generic.List<Check>();

        private Vector2 scroll;

        [MenuItem("Tools/Earshot/Pruefen")]
        public static void Open()
        {
            var window = GetWindow<EarshotPreflight>("Earshot Pruefen");
            window.minSize = new Vector2(460f, 360f);
            window.Run();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Erneut pruefen", GUILayout.Height(28f)))
            {
                Run();
            }

            EditorGUILayout.Space(6f);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            int failed = 0;
            for (int i = 0; i < checks.Count; i++)
            {
                if (!checks[i].Passed) failed++;
            }

            EditorGUILayout.LabelField(
                failed == 0
                    ? "Alles in Ordnung."
                    : failed + " Hinweis(e). Nichts wurde veraendert.",
                EditorStyles.boldLabel);

            for (int i = 0; i < checks.Count; i++)
            {
                var check = checks[i];
                var type = check.Passed ? MessageType.Info : MessageType.Warning;
                EditorGUILayout.HelpBox((check.Passed ? "OK  " : "Fehlt  ") + check.Title + "\n" + check.Why, type);
            }

            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            checks.Clear();

            var settings = Resources.Load<CoopSettings>(CoopSettings.ResourcePath);
            bool hasSettings = settings != null && !settings.name.Contains("Standardwerte");
            Add(
                hasSettings,
                "Einstellungs-Asset",
                hasSettings
                    ? "Resources/EarshotSettings ist vorhanden."
                    : "Ohne dieses Asset laeuft Earshot mit Standardwerten. Anlegen ueber Tools > Earshot > Setup.");

            var profile = settings != null ? settings.VoiceProfile : null;
            Add(
                profile != null,
                "VoiceProfile zugewiesen",
                profile != null
                    ? profile.name
                    : "Ohne Profil gibt es nur Entfernungsdaempfung, keine Waende, Tueren oder Raeume.");

            bool hasModifiers = profile != null && profile.SortedModifiers.Count > 0;
            Add(
                hasModifiers,
                "Module im VoiceProfile",
                hasModifiers
                    ? profile.SortedModifiers.Count + " Modul(e) aktiv."
                    : "Ein leeres Profil wirkt nur ueber die Entfernung. Waende und Tueren bleiben stumm.");

            bool layersOk = profile == null || profile.OcclusionLayers != 0;
            Add(
                layersOk,
                "Occlusion Layers",
                layersOk
                    ? "Mindestens ein Layer ist gesetzt."
                    : "Ist die Maske leer, blockiert keine Wand den Schall. Im VoiceProfile mindestens Default ankreuzen.");

            var listeners = EarshotEditorFind.All<AudioListener>(false);
            Add(
                listeners.Length == 1,
                "Genau ein AudioListener",
                listeners.Length == 1
                    ? listeners[0].name
                    : listeners.Length == 0
                        ? "Ohne Listener hoert niemand Stimmen. Meist haengt er an der Spielerkamera."
                        : listeners.Length + " Listener gefunden. Unity nutzt nur einen, oft den falschen.");

            var manager = EarshotEditorFind.First<NetworkManager>(true);
            Add(
                manager != null,
                "NetworkManager",
                manager != null
                    ? manager.name
                    : "Ohne NetworkManager gibt es keine Sitzung. Anlegen ueber Tools > Earshot > Setup.");

            bool prefabOk = manager != null && manager.NetworkConfig.PlayerPrefab != null;
            Add(
                prefabOk,
                "Player-Prefab im NetworkManager",
                prefabOk
                    ? manager.NetworkConfig.PlayerPrefab.name
                    : "Netcode erzeugt dann keine Spielerfiguren.");

            bool coopPlayerOk = prefabOk &&
                                manager.NetworkConfig.PlayerPrefab.GetComponent<CoopPlayer>() != null;
            Add(
                coopPlayerOk,
                "CoopPlayer am Prefab",
                coopPlayerOk
                    ? "Die UGS-Spieler-ID kann synchronisiert werden."
                    : "Ohne CoopPlayer laesst sich eine Stimme keinem Avatar zuordnen.");

            string projectId = CloudProjectSettings.projectId;
            bool ugsOk = !string.IsNullOrEmpty(projectId);
            Add(
                ugsOk,
                "Unity Gaming Services verknuepft",
                ugsOk
                    ? "Projekt ist verknuepft. Mitspieler muessen dasselbe UGS-Projekt verwenden."
                    : "Ohne Verknuepfung gehen Relay und Vivox nicht. Der lokale Testraum (Play, ohne Account) funktioniert trotzdem. Internet-Host: Edit > Project Settings > Services.");

            var portals = EarshotEditorFind.All<VoicePortal>(true);
            int portalsWithoutCollider = 0;
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i] != null && portals[i].GetComponent<Collider>() == null)
                {
                    portalsWithoutCollider++;
                }
            }

            Add(
                portalsWithoutCollider == 0,
                "VoicePortal mit Collider",
                portalsWithoutCollider == 0
                    ? (portals.Length == 0
                        ? "Keine Portale in der Szene. In Ordnung, solange es keine Tueren gibt."
                        : portals.Length + " Portal(e) mit Collider.")
                    : portalsWithoutCollider + " Portal(e) ohne Collider. Die Sichtlinie trifft sie nie, die Tuer wirkt akustisch nicht.");

            var zones = EarshotEditorFind.All<VoiceZone>(true);
            int zonesNotTrigger = 0;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null) continue;
                var col = zones[i].GetComponent<Collider>();
                if (col != null && !col.isTrigger) zonesNotTrigger++;
            }

            Add(
                zonesNotTrigger == 0,
                "VoiceZone als Trigger",
                zonesNotTrigger == 0
                    ? (zones.Length == 0
                        ? "Keine Zonen in der Szene."
                        : zones.Length + " Zone(n) mit Trigger.")
                    : zonesNotTrigger + " Zone(n) ohne Is Trigger. Earshot erkennt dann nicht, wer im Raum steht.");

            Repaint();
        }

        private void Add(bool passed, string title, string why)
        {
            checks.Add(new Check { Passed = passed, Title = title, Why = why });
        }

        private struct Check
        {
            public bool Passed;
            public string Title;
            public string Why;
        }
    }
}
