using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Command-line build entry point. Used by build.sh (WSL/Linux) and build.bat (Windows):
//   Unity -batchmode -quit -projectPath <repo> -executeMethod BuildScript.BuildWindows [-buildPath <dir>]
// and by deploy/deploy.sh for the dedicated Linux server:
//   Unity -batchmode -quit -projectPath <repo> -executeMethod BuildScript.BuildLinuxServer [-buildPath <dir>]
// Also available in the editor: Tools > Build > Windows (x64) / Tools > Build > Linux Server.
//
// Needs the matching platform module installed from Unity Hub: "Windows Build Support (Mono)" for the
// player, "Linux Dedicated Server Build Support" for the server.
public static class BuildScript
{
    const string DefaultOutputDir = "Build/Windows";
    const string DefaultServerOutputDir = "Build/LinuxServer";

    // the dedicated server's own scene list: no Menu (nothing to click), ServerBoot picks the track
    // from -track= at startup instead (see ServerBoot.cs)
    static readonly string[] ServerScenes =
    {
        "Assets/Scenes/Server.unity",
        "Assets/Scenes/Track_Street.unity",
        "Assets/Scenes/Track_Desert.unity",
        "Assets/Scenes/Track_Test.unity",
    };

    [MenuItem("Tools/Build/Windows (x64)")]
    public static void BuildWindowsFromMenu()
    {
        BuildReport report = Build(Path.Combine(ProjectRoot(), DefaultOutputDir));
        if (report.summary.result == BuildResult.Succeeded)
            EditorUtility.RevealInFinder(report.summary.outputPath);
    }

    // Called with -executeMethod. Exits the editor with 0 on success, 1 on failure.
    public static void BuildWindows()
    {
        string outputDir = ArgAfter("-buildPath") ?? Path.Combine(ProjectRoot(), DefaultOutputDir);
        BuildReport report = Build(outputDir);
        bool ok = report.summary.result == BuildResult.Succeeded;
        Console.WriteLine(ok
            ? "BUILD OK: " + report.summary.outputPath + " (" + (report.summary.totalSize / (1024 * 1024)) + " MB)"
            : "BUILD FAILED: " + report.summary.result + ", errors: " + report.summary.totalErrors);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static BuildReport Build(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        string exe = Path.Combine(outputDir, PlayerSettings.productName + ".exe");

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = EnabledScenes();
        options.locationPathName = exe;
        options.target = BuildTarget.StandaloneWindows64;
        options.targetGroup = BuildTargetGroup.Standalone;
        options.options = BuildOptions.None;

        Debug.Log("BuildScript: building " + options.scenes.Length + " scene(s) to " + exe);
        return BuildPipeline.BuildPlayer(options);
    }

    [MenuItem("Tools/Build/Linux Server")]
    public static void BuildLinuxServerFromMenu()
    {
        BuildServer(Path.Combine(ProjectRoot(), DefaultServerOutputDir));
    }

    // Called with -executeMethod (see deploy/deploy.sh). Exits the editor with 0 on success, 1 on failure.
    public static void BuildLinuxServer()
    {
        string outputDir = ArgAfter("-buildPath") ?? Path.Combine(ProjectRoot(), DefaultServerOutputDir);
        BuildReport report = BuildServer(outputDir);
        bool ok = report.summary.result == BuildResult.Succeeded;
        Console.WriteLine(ok
            ? "BUILD OK: " + report.summary.outputPath + " (" + (report.summary.totalSize / (1024 * 1024)) + " MB)"
            : "BUILD FAILED: " + report.summary.result + ", errors: " + report.summary.totalErrors);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static BuildReport BuildServer(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        string exe = Path.Combine(outputDir, "RCRACE-server");

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = ServerScenes;
        options.locationPathName = exe;
        options.target = BuildTarget.StandaloneLinux64;
        options.targetGroup = BuildTargetGroup.Standalone;
        options.subtarget = (int)StandaloneBuildSubtarget.Server;
        options.options = BuildOptions.None;

        Debug.Log("BuildScript: building dedicated server, " + options.scenes.Length + " scene(s), to " + exe);
        return BuildPipeline.BuildPlayer(options);
    }

    static string[] EnabledScenes()
    {
        List<string> scenes = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);
        if (scenes.Count == 0)
            throw new Exception("No scenes enabled in File > Build Profiles / Build Settings.");
        return scenes.ToArray();
    }

    static string ProjectRoot()
    {
        return Path.GetDirectoryName(Application.dataPath);
    }

    static string ArgAfter(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
