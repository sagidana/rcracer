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
        string previousVersion = StampVersion();

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = EnabledScenes();
        options.locationPathName = exe;
        options.target = BuildTarget.StandaloneWindows64;
        options.targetGroup = BuildTargetGroup.Standalone;
        // must be explicit: Player (0) is also BuildPlayerOptions.subtarget's un-set default, so if this
        // is left off, a build can silently inherit whatever subtarget the editor last built with (e.g.
        // Server, from BuildLinuxServer) instead of always producing a normal player here.
        options.subtarget = (int)StandaloneBuildSubtarget.Player;
        options.options = BuildOptions.None;

        Debug.Log("BuildScript: building " + options.scenes.Length + " scene(s) to " + exe);
        try
        {
            return BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            RestoreVersion(previousVersion);
        }
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
        string previousVersion = StampVersion();

        BuildPlayerOptions options = new BuildPlayerOptions();
        options.scenes = ServerScenes;
        options.locationPathName = exe;
        options.target = BuildTarget.StandaloneLinux64;
        options.targetGroup = BuildTargetGroup.Standalone;
        options.subtarget = (int)StandaloneBuildSubtarget.Server;
        options.options = BuildOptions.None;

        Debug.Log("BuildScript: building dedicated server, " + options.scenes.Length + " scene(s), to " + exe);
        try
        {
            return BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            RestoreVersion(previousVersion);
        }
    }

    // Stamps the build with the commit it came from, so "are we on the same build?" is something two
    // players read off their screens (see GameVersion) instead of trying to remember. Taken from git
    // rather than typed into ProjectSettings by hand: a version number somebody has to remember to
    // bump is the one that silently stays wrong - which is exactly how two builds end up on different
    // wire formats, both reporting "Online", unable to see each other.
    //
    // Returns the version to put back afterwards: the stamp belongs to the build, not to the checkout,
    // and leaving it in ProjectSettings.asset would mean a modified file after every single build.
    static string StampVersion()
    {
        string previous = PlayerSettings.bundleVersion;
        string commit = Git("rev-parse --short HEAD");
        string version;
        if (string.IsNullOrEmpty(commit))
        {
            // no git on PATH, or not a checkout: the build time at least still differs between two
            // people's builds, instead of claiming they match
            version = "nogit-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            Debug.LogWarning("BuildScript: git not available here, stamping " + version + " instead of the commit.");
        }
        else
        {
            version = Git("log -1 --format=%cd --date=format:%Y-%m-%d") + "." + commit;
            // uncommitted edits are the other way two builds differ while claiming the same commit
            if (!string.IsNullOrEmpty(Git("status --porcelain"))) version = version + "+edits";
        }
        PlayerSettings.bundleVersion = version;
        Debug.Log("BuildScript: version " + version + ", net protocol " + NetProtocol.Version);
        return previous;
    }

    static void RestoreVersion(string previous)
    {
        PlayerSettings.bundleVersion = previous;
        AssetDatabase.SaveAssets();
    }

    static string Git(string args)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("git", args);
            psi.WorkingDirectory = ProjectRoot();
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (System.Diagnostics.Process git = System.Diagnostics.Process.Start(psi))
            {
                string output = git.StandardOutput.ReadToEnd();
                git.StandardError.ReadToEnd();
                git.WaitForExit(10000);
                if (git.ExitCode != 0) return "";
                return output.Trim();
            }
        }
        catch (Exception)
        {
            return "";
        }
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
