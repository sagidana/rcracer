using UnityEngine;

// What build this is, for a human to compare with a friend's screen. Two different things, both shown:
//
//   build     Application.version, stamped at build time from the git commit (see BuildScript.StampVersion).
//             Two players with the same string are running the exact same code.
//   net       NetProtocol.Version, the wire format. This is the one that actually decides whether a
//             client and the server can talk at all - the server refuses a client that does not match
//             (see NetServer's Hello handling), instead of letting it join and misread every packet.
//
// They are separate because most commits change the build without touching the wire: friends on
// different builds of the same protocol still race together, just with slightly different physics
// tuning or menus.
public static class GameVersion
{
    public static string Build { get { return Application.version; } }

    // e.g. "v2026-09-21.0e3e12e (net 1)" - short enough for a status line, complete enough to compare
    public static string Line { get { return "v" + Application.version + " (net " + NetProtocol.Version + ")"; } }
}
