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
    // Without a frame cap the headless loop spins as fast as the machine allows, and the server's own
    // clock goes with it: measured 155 physics steps per real second on an idle 16-core box and 121 on
    // the live server, against the 100 a client produces. The server then has no input left to apply on
    // a third of its steps, repeats the controls it last saw, and does not acknowledge those steps - so
    // the client's unconfirmed buffer never drains and every snapshot lands as a correction. That is
    // what "the server is lagging" was. Capped at the physics rate, simulated time tracks real time
    // (and the server stops burning a whole core doing it).
    const int ServerFrameRate = 100;   // = 1 / Time.fixedDeltaTime, see CarController.Awake

    void Awake()
    {
        QualitySettings.vSyncCount = 0;   // nothing to sync to without a display; it would ignore the cap below
        Application.targetFrameRate = ServerFrameRate;

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
