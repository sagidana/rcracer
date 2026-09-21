using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Server > Build Server Scene : creates Assets/Scenes/Server.unity, an empty scene holding only
// a ServerBoot object. Nothing else needed - no camera, no light, no UI: the dedicated server never
// renders anything (built with -nographics -batchmode). Safe to run again.
public static class ServerSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Server.unity";

    [MenuItem("Tools/Server/Build Server Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject boot = new GameObject("ServerBoot");
        boot.AddComponent<ServerBoot>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Server scene saved: " + ScenePath);
    }
}
