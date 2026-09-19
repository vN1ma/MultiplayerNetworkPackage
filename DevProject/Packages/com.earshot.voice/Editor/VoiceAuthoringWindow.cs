using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    /// <summary>
    /// Authoring-Fenster (Masterplan Phase 2): Zonen aus Hand-Markern erzeugen und
    /// aktualisieren, Graph backen, Pruefen, Tool-Ergebnisse entfernen.
    /// <para>
    /// Alles, was das Tool erzeugt, lebt unter EINEM Container
    /// (<see cref="ContainerName"/>) pro Szene - die Geometrie der Szene wird nie
    /// angefasst. Idempotent: erneut Ausfuehren aktualisiert vorhandene Zonen statt
    /// sie zu vervielfachen. Jede Aktion laeuft durch Unitys Undo; der Dry-Run
    /// zeigt vorher, was passieren wuerde.
    /// </para>
    /// <para>
    /// Workflow: pro Raum einmal ein Empty in die Raummitte legen und
    /// "ZoneMarker_&lt;Raumname&gt;" nennen (Button unten erledigt das fuer die
    /// Auswahl). Dann "Zonen aus Markern" - das Tool sondiert Boden, Decke und
    /// Waende per Raycast und baut die Trigger-Box. Tueren verkabelt das
    /// Hotelszenen-Tool (SimpleDoor) bzw. spaeter Phase 3 (Teleport-Bruecken).
    /// </para>
    /// </summary>
    public sealed class VoiceAuthoringWindow : EditorWindow
    {
        public const string ContainerName = "_EarshotAudioGraph";
        public const string MarkersFolderName = "Markers";
        public const string ZonesFolderName = "Zones";
        public const string MarkerPrefix = "ZoneMarker_";
        public const string ZonePrefix = "AudioZone_";

        [SerializeField]
        private LayerMask probeMask = ~0;

        [SerializeField, Min(1f)]
        private float maxProbeDistance = VoiceGraphFactory.DefaultMaxProbeDistance;

        [SerializeField, Min(0f)]
        private float wallInset = VoiceGraphFactory.DefaultWallInset;

        private bool dryRun = true;
        private readonly List<string> report = new List<string>();
        private Vector2 scroll;

        [MenuItem("Earshot Voice/Authoring")]
        public static void Open()
        {
            GetWindow<VoiceAuthoringWindow>("Earshot Authoring");
        }

        [MenuItem("Earshot Voice/Create Hearing Test Scene")]
        public static void CreateHearingTestScene()
        {
            HearingTestSceneBuilder.Create();
        }

        /// <summary>
        /// EditorGUILayout hat kein oeffentliches LayerMask-Feld. Wir bauen es
        /// aus MaskField: displayedOptions sind die belegten Layer-Namen, die
        /// Bits dort sind Indizes in diese Liste und werden auf die echten
        /// Layer-Nummern zurueckgerechnet. Nutzt nur oeffentliche API.
        /// </summary>
        private static LayerMask LayerMaskField(string label, LayerMask mask)
        {
            var names = new List<string>();
            var layerNumbers = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    names.Add(layerName);
                    layerNumbers.Add(i);
                }
            }

            int maskValue = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if (((mask.value >> layerNumbers[i]) & 1) != 0)
                {
                    maskValue |= 1 << i;
                }
            }

            int newMaskValue = EditorGUILayout.MaskField(
                label, maskValue, names.ToArray());

            int result = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if (((newMaskValue >> i) & 1) != 0)
                {
                    result |= 1 << layerNumbers[i];
                }
            }
            return result;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Pro Raum: Empty in die Raummitte, Name 'ZoneMarker_<Raum>' " +
                "(Button unten macht das fuer die Auswahl). Dann 'Zonen aus Markern'. " +
                "Alles Erzeugte liegt unter " + ContainerName + " - die Szene selbst " +
                "wird nicht angefasst. Tueren verkabelt das Hotelszenen-Tool.",
                MessageType.Info);

            probeMask = LayerMaskField("Sondierungs-Layer", probeMask);
            maxProbeDistance = EditorGUILayout.Slider(
                "Max-Raycast (m)", maxProbeDistance, 1f, 100f);
            wallInset = EditorGUILayout.Slider(
                "Wandabzug (m)", wallInset, 0f, 1f);
            dryRun = EditorGUILayout.ToggleLeft(
                "Dry-Run (nur Bericht, nichts schreiben)", dryRun);

            EditorGUILayout.Space();

            if (GUILayout.Button("Marker fuer Auswahl erstellen"))
            {
                CreateMarkersFromSelection();
            }

            if (GUILayout.Button("Zonen aus Markern generieren/aktualisieren"))
            {
                GenerateZonesFromMarkers();
            }

            if (GUILayout.Button("Graph backen"))
            {
                Bake();
            }

            if (GUILayout.Button("Pruefen (Preflight + Autoring)"))
            {
                Validate();
            }

            if (GUILayout.Button("Tool-Ergebnisse entfernen (Container loeschen)"))
            {
                RemoveAll();
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (report.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "Noch kein Bericht in dieser Sitzung.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < report.Count; i++)
                {
                    EditorGUILayout.LabelField(report[i], EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Bake()
        {
            VoiceGraph.Rebuild();
            report.Clear();
            report.Add(
                "Graph: " + VoiceGraph.NodeCount + " Raeume, " +
                VoiceGraph.ConnectionCount + " Kanten.");
            Debug.Log("[Earshot Authoring] " + report[0]);

            var warnings = new List<string>();
            VoiceGraph.CollectPreflight(warnings);
            AppendWarnings(warnings);
        }

        private void Validate()
        {
            var warnings = new List<string>();
            CollectValidation(warnings);
            report.Clear();
            AppendWarnings(warnings);
        }

        private void AppendWarnings(List<string> warnings)
        {
            if (warnings.Count == 0)
            {
                report.Add("Keine Warnungen.");
                Debug.Log("[Earshot Authoring] Pruefung bestanden.");
                return;
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                report.Add(warnings[i]);
                Debug.LogWarning("[Earshot Authoring] " + warnings[i]);
            }
        }

        /// <summary>
        /// Findet den Authoring-Container der Szene, ohne ihn anzulegen. Externe
        /// Tools (z.B. Hotelszenen-Authoring) nutzen dieselbe Wurzel.
        /// </summary>
        public static Transform FindContainer()
        {
            var existing = GameObject.Find(ContainerName);
            return existing != null ? existing.transform : null;
        }

        /// <summary>Legt den Container (und fehlende Unterordner) an. Idempotent.</summary>
        public static Transform EnsureContainer()
        {
            var container = FindContainer();
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                Undo.RegisterCreatedObjectUndo(go, "Earshot: Container anlegen");
                container = go.transform;
            }

            EnsureChildFolder(container, MarkersFolderName);
            EnsureChildFolder(container, ZonesFolderName);
            return container;
        }

        private static Transform EnsureChildFolder(Transform container, string folderName)
        {
            var child = container.Find(folderName);
            if (child != null) return child;

            var go = new GameObject(folderName);
            go.transform.SetParent(container, false);
            Undo.RegisterCreatedObjectUndo(go, "Earshot: Ordner anlegen");
            return go.transform;
        }

        private static Transform FindChildNamed(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
            }

            return null;
        }

        private void CreateMarkersFromSelection()
        {
            report.Clear();
            var markers = EnsureChildFolder(EnsureContainer(), MarkersFolderName);
            int created = 0;

            foreach (var target in Selection.transforms)
            {
                if (target == null) continue;

                string roomName = target.name;
                if (roomName.StartsWith(MarkerPrefix, System.StringComparison.Ordinal))
                {
                    roomName = roomName.Substring(MarkerPrefix.Length);
                }

                var marker = new GameObject(MarkerPrefix + roomName);
                marker.transform.position = target.position;
                marker.transform.SetParent(markers, true);
                Undo.RegisterCreatedObjectUndo(marker, "Earshot: Marker anlegen");
                created++;
                report.Add("Marker angelegt: " + marker.name + " bei " + target.name);
            }

            if (created == 0)
            {
                EditorUtility.DisplayDialog(
                    "Earshot Voice",
                    "Bitte zuerst ein Objekt in der Raummitte selektieren " +
                    "(z.B. ein Moebel-Stueck) und erneut druecken.",
                    "OK");
                return;
            }

            MarkSceneDirty();
        }

        private void GenerateZonesFromMarkers()
        {
            report.Clear();
            var container = FindContainer();
            var markersFolder = container != null
                ? container.Find(MarkersFolderName)
                : null;

            if (markersFolder == null)
            {
                report.Add("Keine Marker vorhanden: erst 'Marker fuer Auswahl erstellen' nutzen.");
                return;
            }

            var zonesFolder = EnsureChildFolder(EnsureContainer(), ZonesFolderName);
            int created = 0;
            int updated = 0;
            int failed = 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Earshot: Zonen aus Markern");

            for (int i = 0; i < markersFolder.childCount; i++)
            {
                var marker = markersFolder.GetChild(i);
                if (!marker.name.StartsWith(MarkerPrefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                string zoneName = marker.name.Substring(MarkerPrefix.Length);
                var issues = new List<string>();

                if (!VoiceGraphFactory.ProbeRoomBounds(
                        marker.position, probeMask, maxProbeDistance, wallInset,
                        out var bounds, issues))
                {
                    failed++;
                    report.Add("FEHLER " + marker.name + ": " + string.Join("; ", issues));
                    continue;
                }

                for (int j = 0; j < issues.Count; j++)
                {
                    report.Add("Hinweis " + marker.name + ": " + issues[j]);
                }

                var existing = FindChildNamed(zonesFolder, ZonePrefix + zoneName);
                string sizeText = bounds.size.x.ToString("0.0") + "x" +
                    bounds.size.y.ToString("0.0") + "x" +
                    bounds.size.z.ToString("0.0") + " m";

                if (existing != null)
                {
                    if (dryRun)
                    {
                        updated++;
                        report.Add("wuerde aktualisieren: " + existing.name + " -> " + sizeText);
                        continue;
                    }

                    var box = existing.GetComponent<BoxCollider>();
                    Undo.RecordObject(existing, "Earshot: Zone aktualisieren");
                    Undo.RecordObject(box, "Earshot: Zone aktualisieren");
                    existing.transform.position = bounds.center;
                    box.center = Vector3.zero;
                    box.size = bounds.size;
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(box);
                    updated++;
                    report.Add("aktualisiert: " + existing.name + " -> " + sizeText);
                }
                else
                {
                    if (dryRun)
                    {
                        created++;
                        report.Add("wuerde anlegen: " + ZonePrefix + zoneName +
                            " (" + sizeText + ")");
                        continue;
                    }

                    var zone = VoiceGraphFactory.CreateZone(bounds, zoneName, zonesFolder);
                    Undo.RegisterCreatedObjectUndo(
                        zone.gameObject, "Earshot: Zone anlegen");
                    created++;
                    report.Add("angelegt: " + zone.gameObject.name + " (" + sizeText + ")");
                }
            }

            if (!dryRun)
            {
                MarkSceneDirty();
                VoiceGraph.MarkDirty();
                VoiceGraph.Rebuild();
                report.Add("Graph: " + VoiceGraph.NodeCount + " Raeume, " +
                    VoiceGraph.ConnectionCount + " Kanten.");
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            report.Add("Fertig: " + created + " angelegt/geplant, " + updated +
                " aktualisiert/geplant, " + failed + " fehlgeschlagen" +
                (dryRun ? " (DRY-RUN, nichts geschrieben)" : "") + ".");
            Debug.Log("[Earshot Authoring] " + report[report.Count - 1]);
        }

        private static void MarkSceneDirty()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>
        /// Erweiterter Pruef-Lauf: Paket-Preflight (Zonen ohne Portal, Portale ohne
        /// zwei Seiten, Spieler ausserhalb jeder Zone) plus Autoring-Checks
        /// (Marker ohne Zone, Zone ohne Marker, verwaiste Marker ausserhalb des
        /// Containers, ueberlappende Zonen).
        /// </summary>
        public static void CollectValidation(List<string> warnings)
        {
            if (warnings == null) return;
            warnings.Clear();

            // Basis-Pruefung des Pakets (baut den Graphen bei Bedarf mit auf).
            VoiceGraph.CollectPreflight(warnings);

            var container = FindContainer();
            var markersFolder = container != null ? container.Find(MarkersFolderName) : null;
            var zonesFolder = container != null ? container.Find(ZonesFolderName) : null;

            if (container == null)
            {
                warnings.Add("Kein " + ContainerName + "-Container: " +
                    "Autoring noch nicht eingerichtet (Marker setzen, Zonen generieren).");
            }

            // Marker ohne zugehoerige Zone.
            if (markersFolder != null)
            {
                for (int i = 0; i < markersFolder.childCount; i++)
                {
                    var marker = markersFolder.GetChild(i);
                    if (!marker.name.StartsWith(MarkerPrefix, System.StringComparison.Ordinal))
                    {
                        warnings.Add("Objekt im Markers-Ordner ohne '" + MarkerPrefix +
                            "'-Praefix: " + marker.name);
                        continue;
                    }

                    string zoneName = marker.name.Substring(MarkerPrefix.Length);
                    if (zonesFolder == null ||
                        FindChildNamed(zonesFolder, ZonePrefix + zoneName) == null)
                    {
                        warnings.Add("Marker ohne Zone: " + marker.name +
                            " ('Zonen aus Markern' ausfuehren)");
                    }
                }
            }

            // Zone ohne zugehoerigen Marker.
            if (zonesFolder != null)
            {
                for (int i = 0; i < zonesFolder.childCount; i++)
                {
                    var zone = zonesFolder.GetChild(i);
                    if (!zone.name.StartsWith(ZonePrefix, System.StringComparison.Ordinal))
                    {
                        warnings.Add("Objekt im Zones-Ordner ohne '" + ZonePrefix +
                            "'-Praefix: " + zone.name);
                        continue;
                    }

                    string markerName = MarkerPrefix + zone.name.Substring(ZonePrefix.Length);
                    if (markersFolder == null ||
                        FindChildNamed(markersFolder, markerName) == null)
                    {
                        warnings.Add("Zone ohne Marker: " + zone.name +
                            " (nicht regenerierbar - Marker nachtragen)");
                    }
                }
            }

            // Ueberlappende Zonen (paarweise, Editor-Zahlen sind klein genug).
#if UNITY_6000_5_OR_NEWER
            var allZones = Object.FindObjectsByType<VoiceZone>(FindObjectsInactive.Exclude);
#else
            var allZones = Object.FindObjectsByType<VoiceZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            for (int i = 0; i < allZones.Length; i++)
            {
                var left = allZones[i];
                if (left == null) continue;
                var leftCol = left.GetComponent<Collider>();
                if (leftCol == null) continue;

                for (int j = i + 1; j < allZones.Length; j++)
                {
                    var right = allZones[j];
                    if (right == null) continue;
                    var rightCol = right.GetComponent<Collider>();
                    if (rightCol == null) continue;

                    if (leftCol.bounds.Intersects(rightCol.bounds))
                    {
                        warnings.Add("Zonen ueberlappen: " + left.ZoneName +
                            " <-> " + right.ZoneName);
                    }
                }
            }
        }

        private void RemoveAll()
        {
            var container = FindContainer();
            if (container == null)
            {
                report.Clear();
                report.Add("Nichts zu entfernen: kein " + ContainerName + "-Container vorhanden.");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "Earshot Voice",
                "'" + ContainerName + "' samt Markern und Zonen entfernen?\n" +
                "An Tueren angehaengte Tool-Komponenten entfernt das " +
                "Hotelszenen-Tool ('Tuer-Verkabelung entfernen').\n" +
                "Rueckgaengig per Strg+Z, Szene liegt im Git.",
                "Entfernen",
                "Abbrechen");
            if (!confirm) return;

            Undo.DestroyObjectImmediate(container.gameObject);
            VoiceGraph.MarkDirty();
            MarkSceneDirty();
            report.Clear();
            report.Add("Container entfernt. An Tueren angehaengte Komponenten " +
                "bitte ueber das Hotelszenen-Tool entfernen.");
        }
    }
}