using UnityEditor;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    [CustomEditor(typeof(VoiceSourceColor))]
    public sealed class VoiceSourceColorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Klangfarbe nur dieser Quelle. Kommt zu Distanz und Waenden dazu. " +
                "Auf Testlautsprecher, Player oder jede AudioSource setzen.",
                MessageType.Info);

            var preset = serializedObject.FindProperty("preset");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(preset, new GUIContent("Voreinstellung"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                var color = (VoiceSourceColor)target;
                color.ApplyPreset((VoiceSourcePreset)preset.enumValueIndex);
                serializedObject.Update();
            }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("muffle"), new GUIContent("Dumpf"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("muffleCutoffHz"), new GUIContent("Tiefpass bei voll dumpf"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("reverb"), new GUIContent("Hall"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("thinness"), new GUIContent("Duenner / blechern"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("thinCutoffHz"), new GUIContent("Hochpass bei voll duenn"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("volume"), new GUIContent("Lautstaerke"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.FindProperty("preset").enumValueIndex = (int)VoiceSourcePreset.Custom;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
