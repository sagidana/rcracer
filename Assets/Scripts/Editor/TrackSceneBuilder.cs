using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Tracks > Build Track Scenes : turns SampleScene (the workbench with everything in it) into one
// playable scene per track: Assets/Scenes/Track_Street.unity and Track_Test.unity.
// A track scene = the track, ground, camera, light, volume, a SpawnPoint and a RaceBootstrap. No car:
// the car the player picked in the menu is spawned by RaceBootstrap when the scene starts.
// SampleScene itself is not changed. Also fills the build settings (Menu first, then the tracks).
public static class TrackSceneBuilder
{
    const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    const string MenuScenePath = "Assets/Scenes/Menu.unity";

    class Track
    {
        public string scene;
        public string[] remove;          // root objects that do not belong to this track
        public System.Action rebuild;    // the generator that (re)builds the track and puts the Car on its start line
        public Track(string scene, string[] remove, System.Action rebuild) { this.scene = scene; this.remove = remove; this.rebuild = rebuild; }
    }

    static readonly Track[] Tracks =
    {
        new Track("Track_Street", new[] { "TestTrack", "PhysicsTestObstacles" }, StreetTrackBuilder.Build),
        new Track("Track_Test", new[] { "StreetTrack", "PhysicsTestObstacles" }, TestTrackBuilder.Build),
    };

    [MenuItem("Tools/Tracks/Build Track Scenes")]
    public static void BuildAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (Track t in Tracks) BuildOne(t);
        EditorSceneManager.OpenScene(SampleScenePath);
        UpdateBuildSettings();
        Debug.Log("Track scenes built: " + string.Join(", ", System.Array.ConvertAll(Tracks, t => t.scene)) + ". Build settings updated.");
    }

    static void BuildOne(Track t)
    {
        Scene scene = EditorSceneManager.OpenScene(SampleScenePath);
        foreach (string name in t.remove)
        {
            GameObject go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }

        t.rebuild();   // fresh track geometry + the Car placed on the start line

        GameObject car = GameObject.Find("Car");
        if (car == null)
        {
            Debug.LogError("The track generator did not leave a 'Car' in the scene; cannot place the SpawnPoint.");
            return;
        }

        GameObject spawn = new GameObject("SpawnPoint");
        spawn.transform.SetPositionAndRotation(car.transform.position, car.transform.rotation);
        GameObject boot = new GameObject("RaceBootstrap");
        boot.AddComponent<RaceBootstrap>().spawnPoint = spawn.transform;
        Object.DestroyImmediate(car);

        string path = "Assets/Scenes/" + t.scene + ".unity";
        EditorSceneManager.SaveScene(scene, path);   // the open scene becomes the track scene; SampleScene.unity on disk is untouched
        Debug.Log("Track scene saved: " + path);
    }

    static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>();
        if (File.Exists(MenuScenePath)) list.Add(new EditorBuildSettingsScene(MenuScenePath, true));
        foreach (Track t in Tracks)
        {
            string path = "Assets/Scenes/" + t.scene + ".unity";
            if (File.Exists(path)) list.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
    }
}
