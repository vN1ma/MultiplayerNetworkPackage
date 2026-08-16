using System;
using System.Collections.Generic;
using System.IO;
using Earshot.Voice;
using Earshot.Voice.Modifiers;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace Earshot.EditorTools
{
    /// <summary>
    /// Richtet ein fremdes Projekt so weit wie moeglich ein, ohne vorhandene Arbeit
    /// zu ueberschreiben. Erst pruefen, dann anwenden.
    /// </summary>
    public sealed class EarshotSetupWindow : EditorWindow
    {
        private const string SettingsPath = "Assets/Resources/EarshotSettings.asset";
        private const string EarshotFolder = "Assets/Earshot";
        private const string ProfilePath = "Assets/Earshot/DefaultVoiceProfile.asset";

        private GameObject playerPrefab;
        private readonly List<PlannedStep> steps = new List<PlannedStep>();
        private readonly List<string> report = new List<string>();
        private Vector2 scroll;
        private bool inspected;

        [MenuItem("Tools/Earshot/Setup")]
        public static void Open()
        {
            var window = GetWindow<EarshotSetupWindow>("Earshot Setup");
            window.minSize = new Vector2(460f, 420f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Earshot einrichten", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Zuerst Testraum bauen, dann Play. Kein Unity-Account noetig: " +
                "du hoerst eine Teststimme hinter der Tuer. Pruefen/Anwenden legt nur " +
                "unsichtbare Technik an (Einstellungen, Voice-Profil, NetworkManager).",
                MessageType.Info);

            if (GUILayout.Button("Testraum bauen  (Haus + Tuer + Spieler)", GUILayout.Height(40f)))
            {
                EarshotPlaytestBuilder.Build();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Oder Schritt fuer Schritt", EditorStyles.boldLabel);

            EditorGUILayout.Space(6f);
            playerPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Player Prefab", "Das Prefab, das Netcode fuer jeden Spieler erzeugt."),
                playerPrefab,
                typeof(GameObject),
                false);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Pruefen", GUILayout.Height(28f)))
                {
                    report.Clear();
                    Inspect();
                }

                using (new EditorGUI.DisabledScope(!inspected || !HasWork()))
                {
                    if (GUILayout.Button("Anwenden", GUILayout.Height(28f)))
                    {
                        Apply();
                    }
                }
            }

            EditorGUILayout.Space(8f);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (inspected)
            {
                EditorGUILayout.LabelField("Geplante Aenderungen", EditorStyles.boldLabel);
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    var icon = step.AlreadyPresent ? "vorhanden, bleibt unveraendert" : "wird angelegt";
                    EditorGUILayout.LabelField(step.Title, icon);
                    if (!string.IsNullOrEmpty(step.Detail))
                    {
                        EditorGUILayout.LabelField(step.Detail, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }

            if (report.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Ergebnis", EditorStyles.boldLabel);
                for (int i = 0; i < report.Count; i++)
                {
                    EditorGUILayout.LabelField(report[i], EditorStyles.wordWrappedLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private bool HasWork()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].AlreadyPresent) return true;
            }

            return false;
        }

        private void Inspect()
        {
            steps.Clear();
            inspected = true;

            PlanSettings();
            PlanVoiceProfile();
            PlanNetworkManager();
            PlanPlayerPrefab();
            PlanPlayerPrefabAssignment();

            Repaint();
        }

        private void Apply()
        {
            report.Clear();

            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    if (step.AlreadyPresent)
                    {
                        report.Add("Unveraendert: " + step.Title);
                        continue;
                    }

                    step.Apply?.Invoke();
                    report.Add("Erledigt: " + step.Title);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                CoopSettings.ClearCache();
            }
            catch (Exception ex)
            {
                report.Add("Fehler: " + ex.Message);
                Debug.LogException(ex);
            }

            report.Add(string.Empty);
            report.Add("Noch von Hand:");
            report.Add("- Unity-Projekt unter Edit > Project Settings > Services mit Unity Gaming Services verknuepfen. Alle Mitspieler muessen dasselbe UGS-Projekt verwenden.");
            report.Add("- Vivox unter Project Settings > Services > Vivox aktivieren.");
            report.Add("- Occlusion-Layer im VoiceProfile so setzen, dass Waende den Schall treffen. Earshot aendert keine Layer oder Tags.");
            report.Add("- Interactor auf das Player-Prefab ziehen, wenn Tueren per Taste benutzt werden sollen.");
            report.Add("- Ueber Tools > Earshot > Pruefen das Ergebnis kontrollieren.");

            inspected = false;
            Inspect();
        }

        private void PlanSettings()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<CoopSettings>(SettingsPath) != null;
            steps.Add(new PlannedStep
            {
                Title = "EarshotSettings unter Resources",
                AlreadyPresent = exists,
                Detail = exists ? SettingsPath : "Wird unter " + SettingsPath + " angelegt.",
                Apply = CreateSettings
            });
        }

        private void PlanVoiceProfile()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<VoiceProfile>(ProfilePath) != null;
            steps.Add(new PlannedStep
            {
                Title = "Standard-VoiceProfile mit vier Modulen",
                AlreadyPresent = exists,
                Detail = exists
                    ? ProfilePath
                    : "Legt Distanz, Occlusion, Portal und Zone als Assets an und traegt sie ins Profil ein.",
                Apply = CreateVoiceProfile
            });
        }

        private void PlanNetworkManager()
        {
            var manager = EarshotEditorFind.First<NetworkManager>(true);
            bool exists = manager != null;
            bool hasTransport = manager != null && manager.GetComponent<UnityTransport>() != null;
            bool hasBootstrap = manager != null && manager.GetComponent<CoopBootstrap>() != null;

            if (exists && hasTransport && hasBootstrap)
            {
                steps.Add(new PlannedStep
                {
                    Title = "NetworkManager mit UnityTransport",
                    AlreadyPresent = true,
                    Detail = manager.gameObject.name
                });
                return;
            }

            if (exists && !hasTransport)
            {
                steps.Add(new PlannedStep
                {
                    Title = "UnityTransport am vorhandenen NetworkManager",
                    AlreadyPresent = false,
                    Apply = () =>
                    {
                        var nm = EarshotEditorFind.First<NetworkManager>(true);
                        if (nm == null) return;
                        var transport = Undo.AddComponent<UnityTransport>(nm.gameObject);
                        Undo.RecordObject(nm, "Earshot Transport");
                        nm.NetworkConfig.NetworkTransport = transport;
                    }
                });
            }
            else if (!exists)
            {
                steps.Add(new PlannedStep
                {
                    Title = "NetworkManager mit UnityTransport",
                    AlreadyPresent = false,
                    Detail = "Neues Objekt in der offenen Szene. Strg+Z nimmt es zurueck.",
                    Apply = CreateNetworkManager
                });
            }

            if (exists && !hasBootstrap)
            {
                steps.Add(new PlannedStep
                {
                    Title = "CoopBootstrap am NetworkManager",
                    AlreadyPresent = false,
                    Apply = () =>
                    {
                        var nm = EarshotEditorFind.First<NetworkManager>(true);
                        if (nm == null || nm.GetComponent<CoopBootstrap>() != null) return;
                        Undo.AddComponent<CoopBootstrap>(nm.gameObject);
                    }
                });
            }
        }

        private void PlanPlayerPrefab()
        {
            if (playerPrefab == null)
            {
                steps.Add(new PlannedStep
                {
                    Title = "Player-Prefab pruefen",
                    AlreadyPresent = true,
                    Detail = "Kein Prefab ausgewaehlt. Zuweisung wird uebersprungen."
                });
                return;
            }

            bool hasNet = playerPrefab.GetComponent<NetworkObject>() != null;
            bool hasPlayer = playerPrefab.GetComponent<CoopPlayer>() != null;

            if (hasNet && hasPlayer)
            {
                steps.Add(new PlannedStep
                {
                    Title = "Player-Prefab Komponenten",
                    AlreadyPresent = true,
                    Detail = playerPrefab.name + " hat NetworkObject und CoopPlayer."
                });
                return;
            }

            steps.Add(new PlannedStep
            {
                Title = "Fehlende Komponenten am Player-Prefab",
                AlreadyPresent = false,
                Detail = MissingComponentText(hasNet, hasPlayer),
                Apply = () => AddPlayerComponents(playerPrefab)
            });
        }

        private void PlanPlayerPrefabAssignment()
        {
            var manager = EarshotEditorFind.First<NetworkManager>(true);
            if (manager != null && manager.NetworkConfig.PlayerPrefab != null)
            {
                steps.Add(new PlannedStep
                {
                    Title = "Player-Prefab im NetworkManager",
                    AlreadyPresent = true,
                    Detail = manager.NetworkConfig.PlayerPrefab.name
                });
                return;
            }

            if (playerPrefab == null)
            {
                steps.Add(new PlannedStep
                {
                    Title = "Player-Prefab im NetworkManager",
                    AlreadyPresent = true,
                    Detail = "Kein Prefab ausgewaehlt und keines eingetragen. Nach dem Anlegen eines Prefabs Setup erneut ausfuehren."
                });
                return;
            }

            steps.Add(new PlannedStep
            {
                Title = "Player-Prefab im NetworkManager eintragen",
                AlreadyPresent = false,
                Apply = () =>
                {
                    var nm = EarshotEditorFind.First<NetworkManager>(true);
                    if (nm == null || playerPrefab == null) return;
                    Undo.RecordObject(nm, "Earshot Player Prefab");
                    nm.NetworkConfig.PlayerPrefab = playerPrefab;
                    EditorUtility.SetDirty(nm);
                }
            });
        }

        private static string MissingComponentText(bool hasNet, bool hasPlayer)
        {
            if (!hasNet && !hasPlayer) return "NetworkObject und CoopPlayer werden hinzugefuegt.";
            if (!hasNet) return "NetworkObject wird hinzugefuegt.";
            return "CoopPlayer wird hinzugefuegt.";
        }

        private static void CreateSettings()
        {
            EnsureFolder("Assets/Resources");
            var settings = CreateInstance<CoopSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        private static void CreateVoiceProfile()
        {
            EnsureFolder(EarshotFolder);

            var distance = CreateModifierAsset<DistanceFalloffModifier>(EarshotFolder + "/DistanceFalloff.asset");
            var occlusion = CreateModifierAsset<OcclusionModifier>(EarshotFolder + "/Occlusion.asset");
            var portal = CreateModifierAsset<PortalModifier>(EarshotFolder + "/Portal.asset");
            var zone = CreateModifierAsset<ZoneModifier>(EarshotFolder + "/Zone.asset");

            var profile = CreateInstance<VoiceProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var so = new SerializedObject(profile);
            var list = so.FindProperty("modifiers");
            list.arraySize = 4;
            list.GetArrayElementAtIndex(0).objectReferenceValue = distance;
            list.GetArrayElementAtIndex(1).objectReferenceValue = occlusion;
            list.GetArrayElementAtIndex(2).objectReferenceValue = portal;
            list.GetArrayElementAtIndex(3).objectReferenceValue = zone;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);

            var settings = AssetDatabase.LoadAssetAtPath<CoopSettings>(SettingsPath);
            if (settings != null && settings.VoiceProfile == null)
            {
                var settingsSo = new SerializedObject(settings);
                settingsSo.FindProperty("voiceProfile").objectReferenceValue = profile;
                settingsSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        private static T CreateModifierAsset<T>(string path) where T : VoiceModifierAsset
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void CreateNetworkManager()
        {
            var go = new GameObject("NetworkManager");
            Undo.RegisterCreatedObjectUndo(go, "Earshot NetworkManager");
            var manager = Undo.AddComponent<NetworkManager>(go);
            var transport = Undo.AddComponent<UnityTransport>(go);
            Undo.AddComponent<CoopBootstrap>(go);
            manager.NetworkConfig.NetworkTransport = transport;
        }

        private static void AddPlayerComponents(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                if (prefab.GetComponent<NetworkObject>() == null)
                {
                    Undo.AddComponent<NetworkObject>(prefab);
                }

                if (prefab.GetComponent<CoopPlayer>() == null)
                {
                    Undo.AddComponent<CoopPlayer>(prefab);
                }

                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (contents.GetComponent<NetworkObject>() == null)
                {
                    contents.AddComponent<NetworkObject>();
                }

                if (contents.GetComponent<CoopPlayer>() == null)
                {
                    contents.AddComponent<CoopPlayer>();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        private sealed class PlannedStep
        {
            public string Title;
            public string Detail;
            public bool AlreadyPresent;
            public Action Apply;
        }
    }
}
