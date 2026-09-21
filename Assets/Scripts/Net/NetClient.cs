using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// Talks to the dedicated server over UDP: sends this player's input, receives everyone's authoritative
// transform, and reconciles the local car if it drifts too far from what the server says. Other players
// are purely visual (RemoteCarView) - the server is the only place player-vs-player collisions are real.
//
// Connecting is best-effort: if NetConfig.ServerIP never answers within ConnectTimeout, this gives up
// quietly and the game keeps playing offline (single player, no reconciliation, no remote cars).
public class NetClient : MonoBehaviour
{
    GameObject localCar;
    CarInput localInput;
    Rigidbody localRb;
    byte localCarIndex;

    UdpClient socket;
    IPEndPoint serverEndPoint;
    readonly Queue<byte[]> inbox = new Queue<byte[]>();
    readonly object inboxLock = new object();

    bool welcomed;
    bool gaveUp;   // tried and failed to reach the server; OnGUI shows this so "offline" is never just a guess
    byte playerId;
    float connectStart;
    float lastHelloSent = -99f;
    float lastInputSent = -99f;
    uint inputSeq;
    bool resetLatched;   // so one press of R becomes one reset tick, not one per tick until it is released

    // Every input tick this client has produced that the server has not acknowledged yet, oldest first.
    // This is the replay buffer: after a correction, these are exactly the ticks the server had not
    // seen when it took that snapshot, so re-simulating them on top of it reproduces "now".
    readonly List<NetProtocol.InputSample> unconfirmed = new List<NetProtocol.InputSample>();
    uint lastAckedSeq;

    // Each packet repeats the last few ticks so one lost packet does not leave a hole in the server's
    // input stream. Physics runs at 100Hz and packets go out at SendRate (30Hz), so ~4 ticks are new
    // each time; 12 covers roughly two consecutive lost packets.
    const int TicksPerPacket = 12;
    // If the server stops acknowledging entirely (a dropout), stop growing the buffer: beyond this the
    // connection is broken badly enough that a replay of it would be meaningless anyway.
    const int MaxUnconfirmed = 300;

    // latency, measured (not assumed): a ping carries our own clock reading, the server echoes it back
    // unchanged, so (now - thatValue) on the reply is a real round trip time over this connection
    float lastPingSent = -99f;
    float smoothedRttMs = -1f;
    public float LatencyMs { get { return smoothedRttMs; } }

    // the most recent snapshot naming this player, used by ReconcileTick every frame rather than
    // correcting once on arrival - see ReconcileTick for why
    bool haveServerState;
    Vector3 lastServerPos;
    Quaternion lastServerRot = Quaternion.identity;
    Vector3 lastServerVel;
    Vector3 lastServerAngVel;
    float lastServerSnapshotTime;

    readonly Dictionary<byte, RemoteCarView> remotes = new Dictionary<byte, RemoteCarView>();

    // the isolated physics world the unconfirmed ticks are re-simulated in (see NetPredictor)
    NetPredictor predictor;
    bool snapshotPending;   // a new server state arrived this frame and has not been reconciled against yet

    public bool Connected { get { return welcomed; } }
    public int RemoteCount { get { return remotes.Count; } }

    public void Init(GameObject car, int carIndex) { Init(car, carIndex, NetConfig.ServerIP); }

    // The 3-arg overload exists for testing (e.g. pointing at 127.0.0.1 in the editor) without
    // touching NetConfig.ServerIP, which every real client uses by way of the 2-arg overload above.
    public void Init(GameObject car, int carIndex, string serverIp)
    {
        localCar = car;
        localInput = car.GetComponent<CarInput>();
        localRb = car.GetComponent<Rigidbody>();
        localCarIndex = (byte)carIndex;

        int ci = Mathf.Clamp(carIndex, 0, GameSelection.Cars.Length - 1);
        GameObject carPrefab = Resources.Load<GameObject>(GameSelection.CarResourceFolder + "/" + GameSelection.Cars[ci]);
        if (carPrefab != null)
        {
            predictor = gameObject.AddComponent<NetPredictor>();
            predictor.Setup(carPrefab);
        }

        try
        {
            socket = new UdpClient();
            socket.Connect(serverIp, NetConfig.ServerPort);   // UDP "connect": just fixes the default destination
            serverEndPoint = (IPEndPoint)socket.Client.RemoteEndPoint;
            socket.BeginReceive(OnReceive, null);
            connectStart = Time.time;
            Debug.Log("NetClient: trying " + serverIp + ":" + NetConfig.ServerPort + " ...");
        }
        catch (Exception e)
        {
            Debug.LogWarning("NetClient: could not start (playing offline): " + e.Message);
            if (socket != null) { socket.Close(); socket = null; }
            gaveUp = true;
        }
    }

    void OnReceive(IAsyncResult ar)
    {
        try
        {
            IPEndPoint from = null;
            byte[] data = socket.EndReceive(ar, ref from);
            lock (inboxLock) inbox.Enqueue(data);
        }
        catch (ObjectDisposedException) { return; }   // socket closed while a receive was pending
        catch (Exception) { /* a malformed/short packet: drop it, keep listening */ }
        if (socket != null) { try { socket.BeginReceive(OnReceive, null); } catch (ObjectDisposedException) { } }
    }

    void Update()
    {
        if (socket == null) return;

        NetSim.Pump(Time.time);
        DrainInbox();

        if (!welcomed)
        {
            if (Time.time - connectStart > NetConfig.ConnectTimeout)
            {
                Debug.LogWarning("NetClient: server did not answer within " + NetConfig.ConnectTimeout + "s - playing offline.");
                Shutdown();
                return;
            }
            if (Time.time - lastHelloSent > 0.5f)
            {
                lastHelloSent = Time.time;
                Send(NetProtocol.WriteHello(localCarIndex));
            }
            return;
        }

        if (Time.time - lastInputSent >= 1f / NetConfig.SendRate) SendInput();

        if (Time.time - lastPingSent >= NetConfig.PingInterval)
        {
            lastPingSent = Time.time;
            Send(NetProtocol.WritePing(Time.time));
        }

        ReconcileTick();
    }

    // Sampled here, not in Update, because one sample must mean exactly one physics tick: the local car
    // steps once per FixedUpdate with whatever CarInput holds, and the server will step its copy once
    // per sample. CarInput only changes in Update, so within a frame every fixed step (there may be
    // several) reads the same controls this does - the sample is what the car really used that tick.
    void FixedUpdate()
    {
        if (socket == null || !welcomed) return;

        bool reset = false;
        if (localInput != null && localInput.PeekReset())
        {
            reset = !resetLatched;
            resetLatched = true;
        }
        else
        {
            resetLatched = false;
        }

        NetProtocol.InputSample sample = new NetProtocol.InputSample
        {
            seq = ++inputSeq,
            throttle = localInput != null ? localInput.Throttle : 0f,
            steer = localInput != null ? localInput.Steer : 0f,
            handbrake = localInput != null && localInput.Handbrake,
            reset = reset,
        };
        unconfirmed.Add(sample);
        if (unconfirmed.Count > MaxUnconfirmed) unconfirmed.RemoveRange(0, unconfirmed.Count - MaxUnconfirmed);
    }

    void SendInput()
    {
        lastInputSent = Time.time;
        if (unconfirmed.Count == 0) return;

        int count = Mathf.Min(TicksPerPacket, unconfirmed.Count);
        NetProtocol.InputSample[] history = new NetProtocol.InputSample[count];
        unconfirmed.CopyTo(unconfirmed.Count - count, history, 0, count);
        Send(NetProtocol.WriteInput(history));
    }

    void DrainInbox()
    {
        while (true)
        {
            byte[] data;
            lock (inboxLock) { if (inbox.Count == 0) break; data = inbox.Dequeue(); }
            Handle(data);
        }
    }

    void Handle(byte[] data)
    {
        if (data.Length == 0) return;
        try
        {
            HandleUnsafe(data);
        }
        catch (EndOfStreamException)
        {
            // a packet that is a different shape than expected - almost always a server running an
            // older/newer build of this same protocol (e.g. mid-deploy). Drop it and keep going rather
            // than let a single stray or version-mismatched packet spam exceptions into every frame.
        }
    }

    void HandleUnsafe(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms))
        {
            byte tag = r.ReadByte();
            switch (tag)
            {
                case NetProtocol.MsgWelcome:
                    playerId = r.ReadByte();
                    welcomed = true;
                    Debug.Log("NetClient: connected as player " + playerId);
                    break;
                case NetProtocol.MsgSnapshot:
                    ApplySnapshot(NetProtocol.ReadSnapshot(r));
                    break;
                case NetProtocol.MsgPlayerLeft:
                    byte left = r.ReadByte();
                    RemoteCarView view;
                    if (remotes.TryGetValue(left, out view)) { if (view != null) Destroy(view.gameObject); remotes.Remove(left); }
                    break;
                case NetProtocol.MsgPong:
                    float sentAt = NetProtocol.ReadPingPong(r);
                    float rttMs = Mathf.Max(0f, Time.time - sentAt) * 1000f;
                    smoothedRttMs = smoothedRttMs < 0f ? rttMs : Mathf.Lerp(smoothedRttMs, rttMs, NetConfig.LatencySmoothing);
                    break;
            }
        }
    }

    void ApplySnapshot(NetProtocol.PlayerState[] players)
    {
        HashSet<byte> seen = new HashSet<byte>();
        foreach (NetProtocol.PlayerState p in players)
        {
            seen.Add(p.playerId);
            if (welcomed && p.playerId == playerId)
            {
                lastServerPos = p.pos;
                lastServerRot = p.rot;
                lastServerVel = p.vel;
                lastServerAngVel = p.angVel;
                lastServerSnapshotTime = Time.time;
                haveServerState = true;
                snapshotPending = true;
                AckInputs(p.lastAppliedSeq);
                continue;
            }

            RemoteCarView view;
            if (!remotes.TryGetValue(p.playerId, out view) || view == null)
            {
                view = SpawnRemote(p.carIndex);
                remotes[p.playerId] = view;
            }
            if (view != null) view.SetTarget(p.pos, p.rot, p.vel, p.angVel);
        }
        // players in the snapshot but not the world (a slow PlayerLeft, or we joined mid-game after
        // they were already gone) are pruned lazily here rather than needing a second message type
        if (remotes.Count > players.Length + 4) PruneStale(seen);
    }

    // Everything the server has now stepped is settled history: its result is baked into the snapshot
    // that carried this acknowledgement, so those ticks must not be replayed on top of it again.
    void AckInputs(uint ackedSeq)
    {
        if (ackedSeq <= lastAckedSeq) return;
        lastAckedSeq = ackedSeq;

        int drop = 0;
        while (drop < unconfirmed.Count && unconfirmed[drop].seq <= ackedSeq) drop++;
        if (drop > 0) unconfirmed.RemoveRange(0, drop);
    }

    void PruneStale(HashSet<byte> seen)
    {
        List<byte> gone = new List<byte>();
        foreach (KeyValuePair<byte, RemoteCarView> kv in remotes) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (byte id in gone) { if (remotes[id] != null) Destroy(remotes[id].gameObject); remotes.Remove(id); }
    }

    // Runs once per arriving snapshot rather than every frame. A snapshot says where the car was at a
    // tick the server had already finished, which is always in the past by about half a round trip -
    // but it also says which of this client's input ticks it had applied by then, and every tick after
    // that is still held here. So instead of guessing where that state has drifted to since (the old
    // approach: extrapolate along velocity, ease toward it, and hope), the unconfirmed ticks are simply
    // re-simulated on top of it in an identical physics world. The result is not an estimate of where
    // the car should be - it is where the car would be, computed the same way the server computed its
    // half. Applying it therefore needs no easing and leaves nothing to converge.
    //
    // When the local prediction was already right, the replay lands on what the car already has and
    // nothing moves at all. The dead zone below only stops a permanent tug-of-war over differences the
    // server never simulates in the first place (props, float drift).
    void ReconcileTick()
    {
        if (!snapshotPending) return;
        snapshotPending = false;
        if (!haveServerState || localRb == null) return;
        if (predictor == null || !predictor.Ready) return;

        Vector3 pos, vel, angVel;
        Quaternion rot;
        predictor.Replay(lastServerPos, lastServerRot, lastServerVel, lastServerAngVel,
            unconfirmed, Time.fixedDeltaTime, out pos, out rot, out vel, out angVel);

        float dist = Vector3.Distance(localRb.position, pos);
        float angle = Quaternion.Angle(localRb.rotation, rot);
        if (dist <= NetConfig.ReplayDeadZone && angle <= NetConfig.ReplayDeadZoneAngle) return;

        localRb.position = pos;
        localRb.rotation = rot;
        localRb.linearVelocity = vel;
        localRb.angularVelocity = angVel;
        localCar.transform.SetPositionAndRotation(pos, rot);   // AutoSyncTransforms is off in this project
    }

    RemoteCarView SpawnRemote(byte carIndex)
    {
        int i = Mathf.Clamp(carIndex, 0, GameSelection.Cars.Length - 1);
        GameObject prefab = Resources.Load<GameObject>(GameSelection.CarResourceFolder + "/" + GameSelection.Cars[i]);
        if (prefab == null) return null;

        GameObject go = Instantiate(prefab);
        go.name = "RemoteCar";
        // visual only: no physics, no input, no HUD - just a body that follows the server's snapshots
        foreach (Behaviour b in go.GetComponentsInChildren<Behaviour>(true))
            if (b is CarController || b is CarInput || b is SpeedDisplay || b is CarWheelVisuals) b.enabled = false;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
        go.AddComponent<RemoteCarView>();
        return go.GetComponent<RemoteCarView>();
    }

    void Send(byte[] data)
    {
        NetSim.Send(socket, data, Time.time);   // no-op passthrough unless NetSim.OneWayDelayMs is set (test-only)
    }

    // Closes the socket and stops trying. Does NOT disable this component: OnGUI must keep running so
    // "gave up, playing offline" is an actual message on screen rather than the status line just
    // vanishing (which used to happen here, indistinguishable from a UI bug).
    void Shutdown()
    {
        if (socket != null) { socket.Close(); socket = null; }
        gaveUp = true;
    }

    void OnDestroy() { Shutdown(); }

    void OnGUI()
    {
        string status;
        if (gaveUp) status = "Offline (solo) - could not reach the server";
        else if (welcomed) status = "Online - " + (remotes.Count + 1) + " car(s)" + (smoothedRttMs >= 0f ? " - " + smoothedRttMs.ToString("0") + " ms" : " - measuring ping...");
        else status = "Connecting...";
        GUI.Label(new Rect(10, 10, 500, 24), status);
    }
}
