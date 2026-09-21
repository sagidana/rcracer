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

    // Reconciliation: how far the client's own simulation may drift from what the server says.
    // A raw comparison against a snapshot is misleading on its own - by the time a snapshot arrives
    // it is already up to one round trip old, and at speed that alone looks like meters of "error"
    // that is not a real disagreement at all. NetClient first extrapolates the snapshot forward by
    // the measured one-way latency (half the ping RTT) using its reported velocity, then compares -
    // what is left after that is a genuine difference, which is corrected smoothly:
    //   below ReconcileDistance/Angle           -> ignore, this is just noise
    //   between that and HardSnapDistance/Angle -> ease toward the server's state over ~ReconcileEase
    //   above HardSnapDistance/Angle             -> snap instantly (a real desync: a missed collision,
    //                                               a fresh join, a long dropout)
    public const float ReconcileDistance = 0.6f;   // meters
    public const float ReconcileAngle = 6f;        // degrees
    public const float ReconcileEase = 0.15f;      // seconds to close a moderate gap
    public const float HardSnapDistance = 6f;      // meters
    public const float HardSnapAngle = 45f;        // degrees

    public const float PingInterval = 1f;          // seconds between latency probes
    public const float LatencySmoothing = 0.25f;   // 0..1, higher = the on-screen ms reacts faster
}
