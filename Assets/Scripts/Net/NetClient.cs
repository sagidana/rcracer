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

        if (Time.time - lastInputSent >= 1f / NetConfig.SendRate)
        {
            lastInputSent = Time.time;
            Send(NetProtocol.WriteInput(
                localInput != null ? localInput.Throttle : 0f,
                localInput != null ? localInput.Steer : 0f,
                localInput != null && localInput.Handbrake,
                localInput != null && localInput.PeekReset()));
        }

        if (Time.time - lastPingSent >= NetConfig.PingInterval)
        {
            lastPingSent = Time.time;
            Send(NetProtocol.WritePing(Time.time));
        }

        ReconcileTick();
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

    void PruneStale(HashSet<byte> seen)
    {
        List<byte> gone = new List<byte>();
        foreach (KeyValuePair<byte, RemoteCarView> kv in remotes) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (byte id in gone) { if (remotes[id] != null) Destroy(remotes[id].gameObject); remotes.Remove(id); }
    }

    // Runs every frame rather than once per snapshot: a snapshot is a sample of where the car WAS at
    // the moment the server sent it, already up to one round trip stale by the time it gets here. At
    // speed that alone looks like meters of "disagreement" that is not a real divergence at all, so
    // this extrapolates the snapshot forward (using its own reported velocity and angular velocity,
    // how long ago it arrived, plus half the measured ping) before comparing. Position is extrapolated
    // ALONG THE CURVE the car is turning through (rotate the velocity by half the predicted turn, then
    // step forward), not a straight line - a car mid-corner does not travel straight, so straight-line
    // prediction is systematically wrong exactly while cornering at speed, which is when a jump was
    // most visible. What is left after that is a genuine gap, eased in smoothly; a hard snap is
    // reserved for something clearly wrong (a missed collision, a fresh join, a long dropout).
    void ReconcileTick()
    {
        if (!haveServerState || localRb == null) return;

        float oneWayLatency = smoothedRttMs > 0f ? smoothedRttMs * 0.0005f : 0f;   // ms -> s, /2 for one-way
        float age = (Time.time - lastServerSnapshotTime) + oneWayLatency;

        Quaternion turn = Quaternion.Euler(lastServerAngVel * Mathf.Rad2Deg * age);
        Quaternion targetRot = turn * lastServerRot;
        Vector3 midVel = Quaternion.Euler(lastServerAngVel * Mathf.Rad2Deg * age * 0.5f) * lastServerVel;
        Vector3 target = lastServerPos + midVel * age;

        float dist = Vector3.Distance(localRb.position, target);
        float angle = Quaternion.Angle(localRb.rotation, targetRot);
        if (dist <= NetConfig.ReconcileDistance && angle <= NetConfig.ReconcileAngle) return;

        if (dist > NetConfig.HardSnapDistance || angle > NetConfig.HardSnapAngle)
        {
            Debug.Log("NetClient: hard-correcting (dist=" + dist.ToString("0.0") + "m, angle=" + angle.ToString("0") + " deg)");
            localRb.position = target;
            localRb.rotation = targetRot;
            localRb.linearVelocity = midVel;
            localRb.angularVelocity = lastServerAngVel;
            return;
        }

        // a moderate, genuine gap: ease toward the server's (extrapolated) state instead of teleporting
        float k = 1f - Mathf.Exp(-Time.deltaTime / NetConfig.ReconcileEase);
        localRb.position = Vector3.Lerp(localRb.position, target, k);
        localRb.rotation = Quaternion.Slerp(localRb.rotation, targetRot, k);
        localRb.linearVelocity = Vector3.Lerp(localRb.linearVelocity, midVel, k);
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
        try { socket.Send(data, data.Length); }
        catch (Exception) { /* a dropped send: the next periodic send will retry with fresh state */ }
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
