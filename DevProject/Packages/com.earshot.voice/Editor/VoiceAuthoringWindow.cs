using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    /// <summary>
    /// Optional: Zonen aus Collidern, Tueren erkennen, Graph backen, Preflight.
    /// Nicht noetig, damit Earshot laeuft — nur zum Einrichten der Raeume.
    /// </summary>
    public sealed class VoiceAuthoringWindow : EditorWindow
    {
        private readonly List<string> warnings = new List<string>();
        private Vector2 scroll;
        private string bakeSummary = "Noch nicht gebacken.";

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

        [MenuItem("Earshot Voice/Create Zone From Selection")]
        public static void CreateZonesFromSelection()
        {
            int created = 0;
            foreach (var transform in Selection.transforms)
            {
                if (CreateZoneOn(transform.gameObject)) created++;
            }

            if (created == 0)
            {
                EditorUtility.DisplayDialog(
                    "Earshot Voice",
                    "Auswahl braucht einen Collider. Der wird zur Zone (Trigger).",
                    "OK");
                return;
            }

            VoiceGraph.MarkDirty();
        }

        [MenuItem("Earshot Voice/Detect Portals In Scene")]
        public static void DetectPortalsInScene()
        {
            int added = DetectPortals();
            VoiceGraph.MarkDirty();
            EditorUtility.DisplayDialog(
                "Earshot Voice",
                added + " Portal(e) an Tuer-/Treppen-Objekten ergaenzt.",
                "OK");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Zonen und Portale sind optional. Ohne sie gilt weiter die Sichtlinie.",
                MessageType.Info);

            if (GUILayout.Button("Hoertest-Szene erzeugen (Flur, Tuer, Treppe)"))
            {
                HearingTestSceneBuilder.Create();
            }

            if (GUILayout.Button("Zonen aus Auswahl erzeugen"))
            {
                CreateZonesFromSelection();
            }

            if (GUILayout.Button("Portale an Tuer-/Treppen-Objekten erkennen"))
            {
                DetectPortalsInScene();
            }

            if (GUILayout.Button("Graph backen"))
            {
                Bake();
            }

            EditorGUILayout.LabelField(bakeSummary);

            if (GUILayout.Button("Preflight"))
            {
                VoiceGraph.CollectPreflight(warnings);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (warnings.Count == 0)
            {
                EditorGUILayout.LabelField("Keine Warnungen.");
            }
            else
            {
                for (int i = 0; i < warnings.Count; i++)
                {
                    EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void Bake()
        {
            VoiceGraph.Rebuild();
            bakeSummary = VoiceGraph.NodeCount + " Raeume, " +
                          VoiceGraph.ConnectionCount + " Kanten.";
            VoiceGraph.CollectPreflight(warnings);
        }

        private static bool CreateZoneOn(GameObject target)
        {
            if (target == null) return false;
            var collider = target.GetComponent<Collider>();
            if (collider == null) return false;
            if (target.GetComponent<VoiceZone>() != null) return false;

            Undo.AddComponent<VoiceZone>(target);
            collider.isTrigger = true;
            return true;
        }

        private static int DetectPortals()
        {
#if UNITY_6000_5_OR_NEWER
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
#else
            var colliders = Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            int added = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null) continue;
                if (!LooksLikePortalHost(collider.gameObject.name)) continue;
                if (collider.GetComponentInParent<VoicePortal>() != null) continue;

                var portal = Undo.AddComponent<VoicePortal>(collider.gameObject);
                if (LooksLikeStair(collider.gameObject.name))
                {
                    portal.SetKind(VoicePortalKind.Stair);
                }

                added++;
            }

            return added;
        }

        private static bool LooksLikePortalHost(string name)
        {
            return Contains(name, "door")
                || Contains(name, "tuer")
                || Contains(name, "tür")
                || Contains(name, "gate")
                || Contains(name, "stair")
                || Contains(name, "treppe")
                || Contains(name, "portal");
        }

        private static bool LooksLikeStair(string name)
        {
            return Contains(name, "stair") || Contains(name, "treppe");
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
