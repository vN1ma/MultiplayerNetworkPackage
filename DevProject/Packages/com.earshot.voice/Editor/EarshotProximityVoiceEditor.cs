using UnityEditor;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    [CustomEditor(typeof(EarshotProximityVoice))]
    public sealed class EarshotProximityVoiceEditor : UnityEditor.Editor
    {
        private const string FoldPrefix = "Earshot.Hearing.";

        private SerializedProperty hearing;
        private SerializedProperty isLocalPlayer;
        private SerializedProperty voiceAnchor;
        private SerializedProperty playerId;
        private SerializedProperty displayName;
        private SerializedProperty channelName;
        private SerializedProperty joinVoiceChannel;

        private void OnEnable()
        {
            hearing = serializedObject.FindProperty("hearing");
            isLocalPlayer = serializedObject.FindProperty("isLocalPlayer");
            voiceAnchor = serializedObject.FindProperty("voiceAnchor");
            playerId = serializedObject.FindProperty("playerId");
            displayName = serializedObject.FindProperty("displayName");
            channelName = serializedObject.FindProperty("channelName");
            joinVoiceChannel = serializedObject.FindProperty("joinVoiceChannel");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Eine Komponente reicht. Die Hoerregler gelten fuer dich lokal: " +
                "so klingen die anderen. Am Prefab einstellen, nicht pro Remote-Instanz.",
                MessageType.Info);

            DrawFoldout("distance", "Distanz und Lautstaerke", true, () =>
            {
                Prop("nearDistance", "Nah (volle Lautstaerke)");
                Prop("maxHearingDistance", "Hoergrenze (m)");
                Prop("distanceFalloff", "Falloff-Kurve");
                Prop("airAbsorption", "Luft macht dumpf");
                Prop("distantCutoffHz", "Tiefpass in der Ferne");
                Prop("distantHighPassHz", "Hochpass in der Ferne");
                Prop("heardVolume", "Gehoerte Stimmen");
                Prop("spatialBlend", "Raeumlichkeit nah");
                Prop("farSpatialBlend", "Raeumlichkeit fern");
            });

            DrawFoldout("walls", "Waende und Dumpf", true, () =>
            {
                Prop("enableWallMuffle", "Waende daempfen");
                Prop("occlusionLayers", "Wand-Layer");
                Prop("occludedVolume", "Lautstaerke hinter Wand");
                Prop("occludedCutoffHz", "Dumpf hinter Wand");
                Prop("occludedReverb", "Hall hinter Wand");
                Prop("wallsUntilFullMuffle", "Waende bis voll dumpf");
            });

            DrawFoldout("doors", "Tueren", true, () =>
            {
                Prop("enableDoors", "Tueren wirken");
                Prop("doorOpennessResponse", "Offenheits-Kurve");
                Prop("doorClosedCutoffHz", "Tiefpass bei Tuer zu");
            });

            DrawFoldout("graph", "Umweg durch Raeume", false, () =>
            {
                Prop("enableGraphPath", "Graph-Umweg");
                Prop("graphClosedVolume", "Lautstaerke bei Tueren zu");
                Prop("graphClosedCutoffHz", "Dumpf auf dem Umweg");
            });

            DrawFoldout("reverb", "Hall und Raeume", true, () =>
            {
                Prop("enableReverb", "Hallfilter an");
                Prop("enableZones", "Zonen wirken");
                Prop("zoneIntensity", "Zonen-Staerke");
                Prop("crossZoneCutoffHz", "Dumpf ueber Raumgrenze");
                Prop("zoneLayers", "Zonen-Layer");
                Prop("maxReverbMix", "Hall-Obergrenze");
                Prop("minLowPassHz", "Tiefpass nie tiefer als");
            });

            DrawFoldout("smooth", "Uebergaenge", true, () =>
            {
                Prop("volumeSmoothingHalfLife", "Lautstaerke-Glaettung");
                Prop("filterSmoothingHalfLife", "Dumpf/Hall-Glaettung");
                Prop("evaluationsPerSecond", "Messungen pro Sekunde");
                EditorGUILayout.HelpBox(
                    "Kleine Halbwertszeit = harter Cutoff (Tuer knallt zu). " +
                    "Groessere Filter-Glaettung = weiches Dumpfwerden ohne Knacken.",
                    MessageType.None);
            });

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Zero-Config", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(isLocalPlayer, new GUIContent(
                "Ist lokal (Fallback)",
                "Mit Netcode/Mirror/Photon setzt die Komponente das selbst."));

            DrawFoldout("advanced", "Advanced (optional)", false, () =>
            {
                EditorGUILayout.PropertyField(voiceAnchor, new GUIContent("Mund / Kopf"));
                EditorGUILayout.PropertyField(playerId, new GUIContent("Spieler-ID"));
                EditorGUILayout.PropertyField(displayName, new GUIContent("Anzeigename"));
                EditorGUILayout.PropertyField(channelName, new GUIContent("Kanal"));
                EditorGUILayout.PropertyField(joinVoiceChannel, new GUIContent("Sprachkanal beitreten"));
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFoldout(string key, string title, bool defaultOpen, System.Action draw)
        {
            string stateKey = FoldPrefix + key;
            bool open = SessionState.GetBool(stateKey, defaultOpen);
            open = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            SessionState.SetBool(stateKey, open);
            if (!open) return;

            EditorGUI.indentLevel++;
            draw();
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        private void Prop(string relative, string label)
        {
            var property = hearing.FindPropertyRelative(relative);
            if (property == null) return;
            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
