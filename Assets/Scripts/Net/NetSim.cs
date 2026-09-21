using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

// Dev/test-only: artificially delays outgoing sends to simulate a real network's one-way latency,
// so netcode bugs that only show up over a real connection (not loopback, ~0ms) can be reproduced
// and measured locally instead of guessed at over an actual, slow, hard-to-instrument internet link.
// Zero effect on real play: OneWayDelayMs defaults to 0, which sends immediately as before.
//
// One shared queue serves every socket in the process (this project's own local server+client tests
// run several sockets in one process), so each pending entry carries its own socket/destination -
// Pump() must never assume "the socket I was just called with" is the right one for every entry.
public static class NetSim
{
    // set these from a test script before Init(); 0/0 = off (real players never touch this)
    public static float OneWayDelayMs = 0f;
    public static float JitterMs = 0f;   // +/- randomized on top of OneWayDelayMs, per packet
    static readonly System.Random rng = new System.Random();

    struct Pending { public float releaseTime; public byte[] data; public UdpClient socket; public bool toEndpoint; public IPEndPoint endpoint; }
    static readonly List<Pending> pending = new List<Pending>();

    static float DelaySeconds()
    {
        float jitter = JitterMs > 0f ? ((float)rng.NextDouble() * 2f - 1f) * JitterMs : 0f;
        return Mathf_Max0(OneWayDelayMs + jitter) / 1000f;
    }
    static float Mathf_Max0(float v) { return v < 0f ? 0f : v; }

    // NetClient's send path (always to its connected server endpoint)
    public static void Send(UdpClient socket, byte[] data, float now)
    {
        if (OneWayDelayMs <= 0f && JitterMs <= 0f) { SafeSend(socket, data); return; }
        pending.Add(new Pending { releaseTime = now + DelaySeconds(), data = data, socket = socket, toEndpoint = false });
    }

    // NetServer's send path (always to a specific client endpoint)
    public static void SendTo(UdpClient socket, byte[] data, IPEndPoint ep, float now)
    {
        if (OneWayDelayMs <= 0f && JitterMs <= 0f) { SafeSendTo(socket, data, ep); return; }
        pending.Add(new Pending { releaseTime = now + DelaySeconds(), data = data, socket = socket, toEndpoint = true, endpoint = ep });
    }

    // call once per Update(), from anywhere - it is fine (and expected, in a multi-socket test) for
    // more than one caller to pump the same shared queue every frame
    public static void Pump(float now)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].releaseTime > now) continue;
            Pending p = pending[i];
            pending.RemoveAt(i);
            if (p.toEndpoint) SafeSendTo(p.socket, p.data, p.endpoint);
            else SafeSend(p.socket, p.data);
        }
    }

    static void SafeSend(UdpClient socket, byte[] data)
    {
        try { socket.Send(data, data.Length); } catch (System.Exception) { }
    }

    static void SafeSendTo(UdpClient socket, byte[] data, IPEndPoint ep)
    {
        try { socket.Send(data, data.Length, ep); } catch (System.Exception) { }
    }
}
