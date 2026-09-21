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

    // Reconciliation. The client re-simulates every input tick the server has not acknowledged yet on
    // top of the server's last verified state, in an identical physics world (see NetPredictor), which
    // gives where the car genuinely should be rather than an estimate of it - so there is no gap to
    // converge and nothing to tune for how fast to close it.
    //
    // ReplayDeadZone is only a floor for noise the server never simulates in the first place (props,
    // float drift): below it the local simulation is left alone rather than nudged every snapshot.
    public const float ReplayDeadZone = 0.05f;     // meters
    public const float ReplayDeadZoneAngle = 1f;   // degrees

    // A correction is right the moment it is computed, but applying all of it in one frame is exactly
    // what a player reads as a jump. The corrected velocity and heading are taken immediately, so the
    // car is driving correctly from that instant, while the position difference is fed in over this
    // long. Measured at 250ms with 30ms jitter, this keeps even a 3.9m correction under 0.34m in any
    // single frame - inside the range ordinary offline driving already produces.
    public const float CorrectionSmoothing = 0.08f;   // seconds
    // Beyond this the car is somewhere else entirely (a missed collision, a respawn, a long dropout)
    // and gliding across the gap would be its own spectacle: take it at once.
    public const float HardSnapDistance = 6f;      // meters

    public const float PingInterval = 1f;          // seconds between latency probes
    public const float LatencySmoothing = 0.25f;   // 0..1, higher = the on-screen ms reacts faster
}
