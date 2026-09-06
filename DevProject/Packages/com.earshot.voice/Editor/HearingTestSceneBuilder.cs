using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    public static class HearingTestSceneBuilder
    {
        private const string ScenePath = "Assets/EarshotHearingTest.unity";

        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HearingTest");
            var level = root.AddComponent<HearingTestLevel>();
            level.Build();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                EditorUtility.DisplayDialog(
                    "Earshot Voice",
                    "Szene konnte nicht unter Assets gespeichert werden.",
                    "OK");
                return;
            }

            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            EditorUtility.DisplayDialog(
                "Earshot Voice",
                "Hoertest liegt in deinem Projekt:\n" + ScenePath +
                "\n\nIm Project-Fenster unter Assets. Dann Play.\n" +
                "WASD + Maus, E Ton, F Tuer.",
                "OK");
        }
    }
}
