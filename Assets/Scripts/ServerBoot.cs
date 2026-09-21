using UnityEngine;
using UnityEngine.SceneManagement;

// Scene 0 of the dedicated server build only (see BuildScript.BuildLinuxServer). Reads which track to
// host from the command line and loads that track scene, where RaceBootstrap notices
// Application.isBatchMode and starts a NetServer instead of spawning a local player.
//
//   RCRACE-server -track=Street   (default if -track is omitted)
//   RCRACE-server -track=Desert
//   RCRACE-server -track=Test
public class ServerBoot : MonoBehaviour
{
    void Awake()
    {
        string track = "Street";
        foreach (string arg in System.Environment.GetCommandLineArgs())
            if (arg.StartsWith("-track=")) track = arg.Substring("-track=".Length);

        string sceneName = "Track_" + track;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("ServerBoot: unknown track '" + track + "' (scene '" + sceneName + "' is not in the build). Falling back to Track_Street.");
            sceneName = "Track_Street";
        }
        Debug.Log("ServerBoot: hosting " + sceneName);
        SceneManager.LoadScene(sceneName);
    }
}
