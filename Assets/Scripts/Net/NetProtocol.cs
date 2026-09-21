using System.IO;
using System.Net;
using UnityEngine;

// The wire format. Every UDP packet starts with one tag byte. Everything is "last value wins" -
// there is no ordering/ack layer, which is fine here because every message is either idempotent
// (Input, Snapshot: only the latest matters) or safely re-sendable (Hello).
public static class NetProtocol
{
    public const byte MsgHello = 1;        // client -> server: "I want to join"
    public const byte MsgWelcome = 2;      // server -> client: "you are player N"
    public const byte MsgInput = 3;        // client -> server: current control state
    public const byte MsgSnapshot = 4;     // server -> client: every player's transform
    public const byte MsgPlayerLeft = 5;   // server -> client: that player disconnected
    public const byte MsgPing = 6;         // client -> server: "what's my round trip time?" (echo this back unchanged)
    public const byte MsgPong = 7;         // server -> client: echo of a Ping's payload
    public const byte MsgVersionMismatch = 8;   // server -> client: "your build does not speak my wire format"
    public const byte MsgTrackMismatch = 9;     // server -> client: "I am not hosting the track you are on"

    // The wire format's own version. BUMP IT in the same commit as any change to what the messages
    // below contain - a field added to a snapshot shifts every field after it, so a client one
    // version behind does not read slightly stale data, it reads a different packet entirely (a
    // serverTick byte as a player count, a throttle byte as a player id). That failure is silent
    // and looks exactly like a netcode bug: both players connect, neither can see the other. The
    // server checks this at Hello and turns such a client away instead of letting it join blind.
    public const byte Version = 2;

    public static byte[] WriteHello(byte carIndex, string track)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgHello);
            w.Write(carIndex);
            w.Write(Version);
            w.Write(track);
            return ms.ToArray();
        }
    }

    // track is the scene the player is driving in, e.g. "Track_Street". A server instance hosts exactly
    // one of them, and the snapshots it sends are positions in THAT track: a client on another one is
    // not a player it can place, it is a car about to be dragged a couple of kilometres sideways into
    // empty space (measured: 2495m, from the Desert spawn to the Street one).
    public struct Hello { public byte carIndex; public byte version; public string track; }

    // Hello is the only message an out-of-date client is allowed to be wrong about, so the version byte
    // lives at the END of it: builds from before versioning existed send just the car index, and rather
    // than throwing on the missing byte (which would drop the packet and leave that player waiting on a
    // server that never answers) they read back as version 0, which matches nothing and is answered with
    // a VersionMismatch they can act on.
    public static Hello ReadHello(BinaryReader r)
    {
        Hello h;
        h.carIndex = r.ReadByte();
        h.version = 0;
        h.track = "";
        if (r.BaseStream.Position < r.BaseStream.Length) h.version = r.ReadByte();
        if (r.BaseStream.Position < r.BaseStream.Length) h.track = r.ReadString();
        return h;
    }

    public static byte[] WriteVersionMismatch(byte serverVersion)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgVersionMismatch);
            w.Write(serverVersion);
            return ms.ToArray();
        }
    }

    public static byte[] WriteWelcome(byte playerId)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgWelcome);
            w.Write(playerId);
            return ms.ToArray();
        }
    }

    // One physics tick's worth of controls. seq is the client's own tick counter: the client samples
    // its input once per FixedUpdate and the server applies exactly one sample per FixedUpdate, so a
    // sample means "this is what was held during tick N", not merely "this is the newest input".
    // That one-to-one correspondence is what lets the client re-simulate its unconfirmed ticks and
    // land on the same result the server did (see NetPredictor).
    public struct InputSample { public uint seq; public float throttle, steer; public bool handbrake, reset; }

    // Carries the newest tick PLUS the several ticks before it, so a lost or reordered UDP packet does
    // not punch a hole in the server's input stream - the next packet's overlap backfills it. Ticks
    // already applied are ignored by seq (see NetServer.ConsumeInput).
    public static byte[] WriteInput(InputSample[] history)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgInput);
            w.Write((byte)history.Length);
            foreach (InputSample s in history)
            {
                w.Write(s.seq);
                w.Write(s.throttle);
                w.Write(s.steer);
                byte flags = 0;
                if (s.handbrake) flags |= 1;
                if (s.reset) flags |= 2;
                w.Write(flags);
            }
            return ms.ToArray();
        }
    }

    public static InputSample[] ReadInput(BinaryReader r)
    {
        int n = r.ReadByte();
        InputSample[] result = new InputSample[n];
        for (int i = 0; i < n; i++)
        {
            InputSample s;
            s.seq = r.ReadUInt32();
            s.throttle = r.ReadSingle();
            s.steer = r.ReadSingle();
            byte flags = r.ReadByte();
            s.handbrake = (flags & 1) != 0;
            s.reset = (flags & 2) != 0;
            result[i] = s;
        }
        return result;
    }

    // angVel (radians/sec, Rigidbody.angularVelocity) lets the receiver extrapolate a snapshot along the
    // curve the car is actually turning through, instead of a straight line from linear velocity alone -
    // a car mid-corner does not travel straight, so straight-line extrapolation systematically predicts
    // the wrong spot exactly while turning at speed, which is the most common time a jump is visible.
    // lastAppliedSeq is the acknowledgement: the last input tick the server had actually applied to this
    // player's car when it took this snapshot. The owning client uses it to know exactly which of its
    // own inputs are still unconfirmed, and therefore which ones must be replayed on top of this state.
    // throttle/steer/handbrake are the controls the server last applied to THIS player. Every other
    // client needs them: its prediction has to carry that player forward through the replay window,
    // and a car assumed to be coasting straight while it is really steering turns a near miss into a
    // collision that never happened - a correction as violent as the one prediction exists to avoid.
    public struct PlayerState { public byte playerId; public byte carIndex; public Vector3 pos; public Quaternion rot; public Vector3 vel; public Vector3 angVel; public uint lastAppliedSeq; public float throttle, steer; public bool handbrake; }

    // serverTick is the server's own physics step counter, so a receiver can tell how two snapshots are
    // ordered. UDP does not preserve order and jitter reorders packets routinely, so without it a
    // snapshot that overtook a newer one would be treated as the current truth.
    public static byte[] WriteSnapshot(PlayerState[] players, uint serverTick)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgSnapshot);
            w.Write(serverTick);
            w.Write((byte)players.Length);
            foreach (PlayerState p in players)
            {
                w.Write(p.playerId);
                w.Write(p.carIndex);
                WriteVector3(w, p.pos);
                WriteQuaternion(w, p.rot);
                WriteVector3(w, p.vel);
                WriteVector3(w, p.angVel);
                w.Write(p.lastAppliedSeq);
                w.Write(p.throttle);
                w.Write(p.steer);
                w.Write(p.handbrake);
            }
            return ms.ToArray();
        }
    }

    public static PlayerState[] ReadSnapshot(BinaryReader r, out uint serverTick)
    {
        serverTick = r.ReadUInt32();
        int n = r.ReadByte();
        PlayerState[] result = new PlayerState[n];
        for (int i = 0; i < n; i++)
        {
            PlayerState p;
            p.playerId = r.ReadByte();
            p.carIndex = r.ReadByte();
            p.pos = ReadVector3(r);
            p.rot = ReadQuaternion(r);
            p.vel = ReadVector3(r);
            p.angVel = ReadVector3(r);
            p.lastAppliedSeq = r.ReadUInt32();
            p.throttle = r.ReadSingle();
            p.steer = r.ReadSingle();
            p.handbrake = r.ReadBoolean();
            result[i] = p;
        }
        return result;
    }

    public static byte[] WriteTrackMismatch(string serverTrack)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgTrackMismatch);
            w.Write(serverTrack);
            return ms.ToArray();
        }
    }

    // "Track_Street" -> "Street": what the menu calls it, which is what a player can act on
    public static string PrettyTrack(string sceneName)
    {
        if (sceneName != null && sceneName.StartsWith("Track_")) return sceneName.Substring("Track_".Length);
        return sceneName;
    }

    public static byte[] WritePlayerLeft(byte playerId)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(MsgPlayerLeft);
            w.Write(playerId);
            return ms.ToArray();
        }
    }

    // the payload is just the sender's own clock reading at send time; the receiver never interprets
    // it, only echoes it back so the original sender can compute (now - thatValue) = round trip time
    public static byte[] WritePing(float senderTime) { return WritePingPong(MsgPing, senderTime); }
    public static byte[] WritePong(float echoedTime) { return WritePingPong(MsgPong, echoedTime); }
    static byte[] WritePingPong(byte tag, float time)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(tag);
            w.Write(time);
            return ms.ToArray();
        }
    }
    public static float ReadPingPong(BinaryReader r) { return r.ReadSingle(); }

    static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static Vector3 ReadVector3(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
    static void WriteQuaternion(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    static Quaternion ReadQuaternion(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
}
