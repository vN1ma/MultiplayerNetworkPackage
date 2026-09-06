using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Earshot.Voice.Editor
{
    public static class HearingTestSceneBuilder
    {
        private const string ScenePath =
            "Packages/com.earshot.voice/Scenes/HearingTest.unity";

        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HearingTest");
            var level = root.AddComponent<HearingTestLevel>();
            level.Build();

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(ScenePath) ?? "Packages/com.earshot.voice/Scenes");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            Selection.activeGameObject = root;
            EditorUtility.DisplayDialog(
                "Earshot Voice",
                "Hoertest-Szene liegt unter " + ScenePath +
                "\n\nPlay: WASD + Maus, E Ton, F Tuer.\n" +
                "Raeume sind geschlossen (Waende ueberlappen).",
                "OK");
        }
    }
}
