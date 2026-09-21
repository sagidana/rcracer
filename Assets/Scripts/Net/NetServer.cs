using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// The dedicated server: authoritative over every connected player's car. Each player gets a real
// instance of their chosen car prefab (the exact same CarController physics the client runs), fed by
// network input instead of a keyboard, so collisions - with the track and with each other - are
// resolved by one single simulation that every client's local guess is checked against
// (see NetClient.Reconcile). Runs inside the normal game (RaceBootstrap adds this instead of a local
// player car when the build is a dedicated server - see Application.isBatchMode there).
public class NetServer : MonoBehaviour
{
    public Transform spawnPoint;

    class Player
    {
        public byte id;
        public byte carIndex;
        public IPEndPoint endpoint;
        public GameObject car;
        public CarInput input;
        public CarController controller;
        public Rigidbody rb;
        public float lastSeen;
        public uint lastAppliedSeq;   // the newest input tick already applied: older/duplicate ones are ignored
        // input ticks received but not yet stepped, oldest first. Kept in tick order so the car is
        // driven through exactly the sequence the player actually held, one tick per physics step.
        public readonly SortedDictionary<uint, NetProtocol.InputSample> pending = new SortedDictionary<uint, NetProtocol.InputSample>();
        public int minPending = int.MaxValue;   // shallowest the queue got since the last drain check
        public float nextDrainCheck;
    }

    // How deep the pending queue should sit. Some backlog is required, not merely tolerated: an input
    // is only in hand about half a round trip after it was pressed, so the server must always be
    // consuming ticks slightly old or it would starve between packets. Depth beyond that is pure added
    // lag, and the depth the queue happens to settle at is an accident of when the first packet landed,
    // so it is actively drained back down (see DrainBacklog) instead of being left wherever it started.
    const int TargetPendingTicks = 2;
    const float DrainCheckInterval = 0.5f;
    // A hard ceiling for the pathological case (a client whose clock runs fast, a long stall): past this
    // the oldest ticks are dropped outright rather than letting the server fall ever further behind.
    const int MaxPendingTicks = 12;

    UdpClient socket;
    readonly Dictionary<string, Player> byEndpoint = new Dictionary<string, Player>();   // key = endpoint.ToString()
    readonly Queue<KeyValuePair<IPEndPoint, byte[]>> inbox = new Queue<KeyValuePair<IPEndPoint, byte[]>>();
    readonly object inboxLock = new object();
    int nextId = 1;
    int nextSlot;
    float lastSnapshot = -99f;

    void Start()
    {
        try
        {
            socket = new UdpClient(NetConfig.ServerPort);
            socket.BeginReceive(OnReceive, null);
            Debug.Log("NetServer: listening on UDP " + NetConfig.ServerPort);
        }
        catch (Exception e)
        {
            Debug.LogError("NetServer: could not bind UDP " + NetConfig.ServerPort + ": " + e.Message);
            enabled = false;
        }
    }

    void OnReceive(IAsyncResult ar)
    {
        try
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = socket.EndReceive(ar, ref from);
            lock (inboxLock) inbox.Enqueue(new KeyValuePair<IPEndPoint, byte[]>(from, data));
        }
        catch (ObjectDisposedException) { return; }
        catch (Exception) { /* malformed packet: drop it */ }
        if (socket != null) { try { socket.BeginReceive(OnReceive, null); } catch (ObjectDisposedException) { } }
    }

    void Update()
    {
        NetSim.Pump(Time.time);
        DrainInbox();
        SweepTimeouts();

        if (Time.time - lastSnapshot >= 1f / NetConfig.SnapshotRate)
        {
            lastSnapshot = Time.time;
            BroadcastSnapshot();
        }
    }

    // One physics tick = one input tick, per player. The cars' own CarController.FixedUpdate is
    // disabled (see Spawn) so that this ordering is guaranteed: apply the tick's input first, then run
    // that car's physics step with it. Left to Unity's own callback order, a car could just as easily
    // step before its input arrived, which would make the server's simulation something the client
    // could never reproduce by replaying the same ticks.
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        foreach (Player p in byEndpoint.Values)
        {
            if (p.controller == null) continue;
            ConsumeInput(p);
            p.controller.Tick(dt);
        }
    }

    void ConsumeInput(Player p)
    {
        while (p.pending.Count > MaxPendingTicks) DropOldest(p);
        DrainBacklog(p);

        uint next = 0;
        bool have = false;
        foreach (uint seq in p.pending.Keys) { next = seq; have = true; break; }
        // nothing queued: the next packet has not arrived yet, so hold the controls the player last
        // had. lastAppliedSeq deliberately does NOT advance - this tick was the server's guess, not
        // the player's input, so the client must keep it unconfirmed.
        if (!have) return;

        NetProtocol.InputSample s = p.pending[next];
        p.pending.Remove(next);
        p.lastAppliedSeq = next;
        p.input.SetNetworkInput(s.throttle, s.steer, s.handbrake, s.reset);
    }

    // The queue only ever needs to be as deep as the worst late packet: whatever depth it never drops
    // below is backlog nobody is waiting on, and every tick of it is a tick of extra lag between a key
    // being pressed and the server acting on it. Judged on the SHALLOWEST depth seen over an interval
    // (never the current one, which swings by a whole packet's worth of ticks), and drained a single
    // tick at a time, so a genuinely jittery connection keeps the depth it actually uses.
    void DrainBacklog(Player p)
    {
        if (p.pending.Count < p.minPending) p.minPending = p.pending.Count;
        if (Time.time < p.nextDrainCheck) return;

        p.nextDrainCheck = Time.time + DrainCheckInterval;
        if (p.minPending > TargetPendingTicks && p.pending.Count > TargetPendingTicks) DropOldest(p);
        p.minPending = int.MaxValue;
    }

    // Skips one input tick. It is acknowledged as though applied, so the client stops replaying it -
    // the alternative, silently never applying it, would leave the client re-simulating a tick the
    // server's own state can never reflect.
    void DropOldest(Player p)
    {
        uint oldest = 0;
        foreach (uint seq in p.pending.Keys) { oldest = seq; break; }
        p.pending.Remove(oldest);
        if (oldest > p.lastAppliedSeq) p.lastAppliedSeq = oldest;
    }

    void DrainInbox()
    {
        while (true)
        {
            KeyValuePair<IPEndPoint, byte[]> item;
            lock (inboxLock) { if (inbox.Count == 0) break; item = inbox.Dequeue(); }
            Handle(item.Key, item.Value);
        }
    }

    void Handle(IPEndPoint from, byte[] data)
    {
        if (data.Length == 0) return;
        try
        {
            HandleUnsafe(from, data);
        }
        catch (EndOfStreamException)
        {
            // a packet shaped differently than expected - a client on a different build of this same
            // protocol, most likely. Drop it rather than let one bad sender take the whole server down
            // for every connected player.
        }
    }

    void HandleUnsafe(IPEndPoint from, byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms))
        {
            byte tag = r.ReadByte();
            string key = from.ToString();
            switch (tag)
            {
                case NetProtocol.MsgHello:
                    byte carIndex = r.ReadByte();
                    Player p;
                    if (!byEndpoint.TryGetValue(key, out p)) p = Spawn(from, carIndex);
                    p.lastSeen = Time.time;
                    SendTo(from, NetProtocol.WriteWelcome(p.id));
                    break;
                case NetProtocol.MsgInput:
                    NetProtocol.InputSample[] history = NetProtocol.ReadInput(r);
                    Player pl;
                    if (byEndpoint.TryGetValue(key, out pl))
                    {
                        pl.lastSeen = Time.time;
                        // the packet overlaps the previous one (see NetClient), so most of these ticks
                        // are already known: queue only the ones still ahead of what has been applied
                        foreach (NetProtocol.InputSample s in history)
                        {
                            if (s.seq <= pl.lastAppliedSeq) continue;
                            pl.pending[s.seq] = s;
                        }
                    }
                    break;
                case NetProtocol.MsgPing:
                    // a stateless echo - answers even before Hello/Welcome, so it also works as a
                    // quick "is the server up" probe from outside the game
                    float echoed = NetProtocol.ReadPingPong(r);
                    SendTo(from, NetProtocol.WritePong(echoed));
                    break;
            }
        }
    }

    Player Spawn(IPEndPoint from, byte carIndex)
    {
        int i = Mathf.Clamp((int)carIndex, 0, GameSelection.Cars.Length - 1);
        GameObject prefab = Resources.Load<GameObject>(GameSelection.CarResourceFolder + "/" + GameSelection.Cars[i]);
        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        if (spawnPoint != null)
        {
            // spread players out sideways so they don't spawn stacked on top of each other
            float side = (nextSlot % 2 == 0 ? 1f : -1f) * (3.5f * ((nextSlot / 2) + 1));
            pos += spawnPoint.right * side;
        }
        nextSlot++;

        GameObject car = prefab != null ? Instantiate(prefab, pos, rot) : new GameObject("Car(missing prefab)");
        car.name = "Player" + nextId;
        CarInput input = car.GetComponent<CarInput>();
        if (input == null) input = car.AddComponent<CarInput>();
        input.NetworkControlled = true;
        CarController controller = car.GetComponent<CarController>();
        if (controller != null) controller.enabled = false;   // stepped by FixedUpdate above, one input tick at a time

        Player p = new Player
        {
            id = (byte)(nextId++ & 0xFF),
            carIndex = (byte)i,
            endpoint = from,
            car = car,
            input = input,
            controller = controller,
            rb = car.GetComponent<Rigidbody>(),
        };
        byEndpoint[from.ToString()] = p;
        Debug.Log("NetServer: player " + p.id + " joined (" + GameSelection.Cars[i] + ") from " + from);
        return p;
    }

    void SweepTimeouts()
    {
        List<string> gone = null;
        foreach (KeyValuePair<string, Player> kv in byEndpoint)
        {
            if (Time.time - kv.Value.lastSeen <= NetConfig.PlayerTimeout) continue;
            if (gone == null) gone = new List<string>();
            gone.Add(kv.Key);
        }
        if (gone == null) return;
        foreach (string key in gone)
        {
            Player p = byEndpoint[key];
            Debug.Log("NetServer: player " + p.id + " timed out");
            byEndpoint.Remove(key);
            if (p.car != null) Destroy(p.car);
            byte[] msg = NetProtocol.WritePlayerLeft(p.id);
            foreach (Player other in byEndpoint.Values) SendTo(other.endpoint, msg);
        }
    }

    void BroadcastSnapshot()
    {
        if (byEndpoint.Count == 0) return;
        NetProtocol.PlayerState[] states = new NetProtocol.PlayerState[byEndpoint.Count];
        int i = 0;
        foreach (Player p in byEndpoint.Values)
        {
            if (p.rb == null) continue;
            states[i++] = new NetProtocol.PlayerState
            {
                playerId = p.id,
                carIndex = p.carIndex,
                pos = p.rb.position,
                rot = p.rb.rotation,
                vel = p.rb.linearVelocity,
                angVel = p.rb.angularVelocity,
                lastAppliedSeq = p.lastAppliedSeq,
            };
        }
        byte[] data = NetProtocol.WriteSnapshot(states);
        foreach (Player p in byEndpoint.Values) SendTo(p.endpoint, data);
    }

    void SendTo(IPEndPoint ep, byte[] data)
    {
        NetSim.SendTo(socket, data, ep, Time.time);   // no-op passthrough unless NetSim.OneWayDelayMs is set (test-only)
    }

    void OnDestroy()
    {
        if (socket != null) { socket.Close(); socket = null; }
    }
}
