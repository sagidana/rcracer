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
    byte playerId;
    float connectStart;
    float lastHelloSent = -99f;
    float lastInputSent = -99f;

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
            enabled = false;
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
            }
        }
    }

    void ApplySnapshot(NetProtocol.PlayerState[] players)
    {
        HashSet<byte> seen = new HashSet<byte>();
        foreach (NetProtocol.PlayerState p in players)
        {
            seen.Add(p.playerId);
            if (welcomed && p.playerId == playerId) { Reconcile(p); continue; }

            RemoteCarView view;
            if (!remotes.TryGetValue(p.playerId, out view) || view == null)
            {
                view = SpawnRemote(p.carIndex);
                remotes[p.playerId] = view;
            }
            if (view != null) view.SetTarget(p.pos, p.rot, p.vel);
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

    void Reconcile(NetProtocol.PlayerState server)
    {
        if (localRb == null) return;
        float dist = Vector3.Distance(localRb.position, server.pos);
        float angle = Quaternion.Angle(localRb.rotation, server.rot);
        if (dist <= NetConfig.ReconcileDistance && angle <= NetConfig.ReconcileAngle) return;

        // the server's physics disagreed with ours enough to matter (usually a collision it resolved
        // differently) - snap to its authoritative state rather than keep drifting
        localRb.position = server.pos;
        localRb.rotation = server.rot;
        localRb.linearVelocity = server.vel;
        localRb.angularVelocity = Vector3.zero;
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

    void Shutdown()
    {
        if (socket != null) { socket.Close(); socket = null; }
        enabled = false;
    }

    void OnDestroy() { Shutdown(); }

    void OnGUI()
    {
        string status = welcomed ? ("Online - " + (remotes.Count + 1) + " car(s)") : "Connecting...";
        GUI.Label(new Rect(10, 10, 400, 24), status);
    }
}
