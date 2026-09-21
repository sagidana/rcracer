using UnityEngine;

// What the player picked in the menu: which car and which track.
// Static so it survives scene loads; saved in PlayerPrefs so it survives restarts.
public static class GameSelection
{
    // car prefab names under Assets/Resources/Cars/ (built by Tools > Cars > Build Car Prefabs)
    public static readonly string[] Cars = { "Buggy", "Roadster", "Bike" };
    public static readonly string[] CarLabels = { "Buggy", "Roadster", "Bike" };

    // track scene names (built by Tools > Tracks > Build Track Scenes) and what the menu shows for them
    public static readonly string[] Tracks = { "Track_Street", "Track_Test" };
    public static readonly string[] TrackLabels = { "Street", "Test Track" };

    public const string MenuScene = "Menu";
    public const string CarResourceFolder = "Cars";

    const string CarKey = "RCRACE.Car";
    const string TrackKey = "RCRACE.Track";

    public static string Car
    {
        get { return PlayerPrefs.GetString(CarKey, Cars[0]); }
        set { PlayerPrefs.SetString(CarKey, value); }
    }

    public static string Track
    {
        get { return PlayerPrefs.GetString(TrackKey, Tracks[0]); }
        set { PlayerPrefs.SetString(TrackKey, value); }
    }

    public static int IndexOf(string[] list, string value)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] == value) return i;
        return 0;
    }
}
