// Shared constants for the client <-> server UDP protocol.
public static class NetConfig
{
    // The dedicated server's fixed address (deploy/deploy.sh installs it here).
    public const string ServerIP = "142.132.187.130";
    public const int ServerPort = 7777;

    public const float SendRate = 30f;             // client input packets per second
    public const float SnapshotRate = 20f;         // server snapshot broadcasts per second
    public const float ConnectTimeout = 4f;        // give up reaching the server after this long (offline play continues)
    public const float PlayerTimeout = 8f;         // server drops a client that has gone silent this long

    // How far the client's own simulation may drift from what the server says before it is corrected.
    // Both sides run the identical CarController physics from the same inputs, so small differences
    // (float rounding, timing) are normal; only a real divergence (a collision the server saw
    // differently, for example) should ever produce a visible snap.
    public const float ReconcileDistance = 2.5f;   // meters
    public const float ReconcileAngle = 20f;       // degrees
}
