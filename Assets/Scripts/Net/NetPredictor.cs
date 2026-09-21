using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// An isolated, hand-stepped physics world holding a duplicate of the current track's static geometry
// plus one "shadow" car. Used to correctly recompute where the local player's car SHOULD be after a
// server correction: reset the shadow car to the server's verified state, then re-simulate every input
// the client has sent since that the server has not confirmed yet (see NetClient). The result is what
// the player's own prediction already should have converged to, so applying it produces at most a tiny
// correction instead of a visible jump - unlike the extrapolate-and-ease approach it replaces, this
// never has to guess.
//
// Physics.Simulate() on this scene never touches the main scene's automatic physics loop, so nothing
// else in the game (props, other cars, the main scene's own car) is affected by a replay.
public class NetPredictor : MonoBehaviour
{
    Scene shadowScene;
    PhysicsScene shadowPhysics;
    GameObject shadowCar;
    CarController shadowController;
    CarInput shadowInput;
    Rigidbody shadowRb;
    public bool Ready { get; private set; }

    // Stand-ins for the other players, so a replay can see them. Without these the client predicts
    // driving straight through another car while the server bounces both of them apart, and the
    // correction that follows is a whole collision's worth of disagreement arriving at once.
    class Ghost
    {
        public GameObject go;
        public Rigidbody rb;
        public CarController controller;
        public CarInput input;
        public Vector3 pos, vel, angVel;
        public Quaternion rot;
        public float throttle, steer;
        public bool handbrake;
        public bool seenThisSnapshot;
    }
    readonly Dictionary<byte, Ghost> ghosts = new Dictionary<byte, Ghost>();

    public void Setup(GameObject carPrefab)
    {
        shadowScene = SceneManager.CreateScene("NetPredictorShadow_" + GetEntityId(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        shadowPhysics = shadowScene.GetPhysicsScene();

        CopyStaticTrackColliders();

        shadowCar = Instantiate(carPrefab);
        SceneManager.MoveGameObjectToScene(shadowCar, shadowScene);
        shadowCar.name = "ShadowCar";

        // visual/side-effect components must never run for a car that exists purely to be
        // reset-and-replayed many times a second: no rendering, no camera shake, no HUD, no wheel
        // mesh animation, and no real input source (it is always fed by SetNetworkInput during replay)
        foreach (Renderer r in shadowCar.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (Behaviour b in shadowCar.GetComponentsInChildren<Behaviour>(true))
            if (b is CarWheelVisuals || b is SpeedDisplay) b.enabled = false;

        shadowController = shadowCar.GetComponent<CarController>();
        shadowInput = shadowCar.GetComponent<CarInput>();
        shadowRb = shadowCar.GetComponent<Rigidbody>();
        shadowController.RefreshPhysicsScene();   // Awake() already ran in the main scene before the move above
        shadowController.InitializePhysics();     // Start() has not fired yet - a replay cannot wait for it
        shadowController.collision.cameraShake = 0f;   // never shake the real player's camera from a replay
        shadowInput.NetworkControlled = true;

        // The shadow car must move ONLY when Replay() drives it. Left enabled, Unity would keep calling
        // its FixedUpdate every frame, piling drive/suspension forces onto a Rigidbody whose scene only
        // steps during a replay - nothing consumes them in between, so the next replay would apply the
        // whole accumulated pile at once (measured: a shadow car idling ~4s launched 3.4km upward).
        // Replay() calls Tick() directly instead, which works fine on a disabled component.
        shadowController.enabled = false;

        Ready = true;
    }

    // Every static (non-Rigidbody) collider becomes a same-shaped copy in the shadow scene: cheap
    // (MeshColliders reuse the same Mesh asset, nothing is deep-copied) and safe to do once, since
    // track geometry never moves at runtime. Dynamic objects (props, cars) are deliberately excluded -
    // the shadow car only needs to agree with the server about the STATIC world; prop collisions are
    // not network-authoritative at all today, so replaying them would not be more correct, only slower.
    void CopyStaticTrackColliders()
    {
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<CarController>() != null) continue;

            GameObject copy = new GameObject("Shadow_" + c.name);
            SceneManager.MoveGameObjectToScene(copy, shadowScene);
            copy.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
            copy.transform.localScale = c.transform.lossyScale;

            if (c is MeshCollider mc)
            {
                copy.AddComponent<MeshCollider>().sharedMesh = mc.sharedMesh;
            }
            else if (c is BoxCollider bc)
            {
                BoxCollider nc = copy.AddComponent<BoxCollider>();
                nc.center = bc.center;
                nc.size = bc.size;
            }
            else if (c is SphereCollider sc)
            {
                SphereCollider nc = copy.AddComponent<SphereCollider>();
                nc.center = sc.center;
                nc.radius = sc.radius;
            }
            else if (c is CapsuleCollider cc)
            {
                CapsuleCollider nc = copy.AddComponent<CapsuleCollider>();
                nc.center = cc.center;
                nc.radius = cc.radius;
                nc.height = cc.height;
                nc.direction = cc.direction;
            }
            else
            {
                Destroy(copy);   // an unsupported collider shape: skip it rather than guess
            }
        }
    }

    // Tells the replay where another player was as of the snapshot being reconciled against. Their
    // state comes from the same snapshot as our own baseline, so everyone starts the replay consistent
    // with everyone else - which is the whole point: it is the same moment the server simulated from.
    public void SetRemote(byte playerId, GameObject carPrefab, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel,
        float throttle, float steer, bool handbrake)
    {
        Ghost g;
        if (!ghosts.TryGetValue(playerId, out g))
        {
            if (carPrefab == null) return;
            g = new Ghost();
            g.go = Instantiate(carPrefab);
            SceneManager.MoveGameObjectToScene(g.go, shadowScene);
            g.go.name = "ShadowRemote" + playerId;
            foreach (Renderer r in g.go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Behaviour b in g.go.GetComponentsInChildren<Behaviour>(true))
                if (b is CarWheelVisuals || b is SpeedDisplay) b.enabled = false;
            g.rb = g.go.GetComponent<Rigidbody>();
            g.controller = g.go.GetComponent<CarController>();
            g.input = g.go.GetComponent<CarInput>();
            if (g.controller != null)
            {
                g.controller.RefreshPhysicsScene();
                g.controller.InitializePhysics();
                g.controller.collision.cameraShake = 0f;
                g.controller.enabled = false;   // driven by Replay, exactly like the shadow car
            }
            if (g.input != null) g.input.NetworkControlled = true;
            ghosts[playerId] = g;
        }
        g.pos = pos;
        g.rot = rot;
        g.vel = vel;
        g.angVel = angVel;
        g.throttle = throttle;
        g.steer = steer;
        g.handbrake = handbrake;
        g.seenThisSnapshot = true;
    }

    // Drops stand-ins for players who were not in the latest snapshot. Called once per snapshot, after
    // every SetRemote for it, so "not seen" means they are genuinely no longer in the race.
    public void DropUnseenRemotes()
    {
        List<byte> gone = null;
        foreach (KeyValuePair<byte, Ghost> kv in ghosts)
        {
            if (kv.Value.seenThisSnapshot) { kv.Value.seenThisSnapshot = false; continue; }
            if (gone == null) gone = new List<byte>();
            gone.Add(kv.Key);
        }
        if (gone == null) return;
        foreach (byte id in gone)
        {
            if (ghosts[id].go != null) Destroy(ghosts[id].go);
            ghosts.Remove(id);
        }
    }

    // Resets the shadow car to (pos, rot, vel, angVel) - the server's last verified state - then steps
    // it forward once per entry in `inputs` (oldest first), applying that entry's controls before each
    // step. Returns what the car's state should be right now, given that baseline plus everything
    // replayed since.
    public void Replay(
        Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel,
        IEnumerable<NetProtocol.InputSample> inputs, float fixedDt,
        out Vector3 outPos, out Quaternion outRot, out Vector3 outVel, out Vector3 outAngVel)
    {
        shadowRb.position = pos;
        shadowRb.rotation = rot;
        shadowRb.linearVelocity = vel;
        shadowRb.angularVelocity = angVel;
        // Physics.SyncTransforms() only reaches the DEFAULT physics scene, never a local one like this
        // shadow scene - and this project runs with AutoSyncTransforms off (see
        // ProjectSettings/DynamicsManager.asset) for performance, so without this the Transform above
        // would stay stale until physics itself steps. Tick() reads transform.forward/up directly, so
        // it needs the correct pose right now, or the very first replayed step drives in whatever
        // direction the shadow car last faced instead of the direction it was just reset to.
        shadowCar.transform.SetPositionAndRotation(pos, rot);

        foreach (KeyValuePair<byte, Ghost> kv in ghosts)
        {
            Ghost g = kv.Value;
            if (g.rb == null) continue;
            g.rb.position = g.pos;
            g.rb.rotation = g.rot;
            g.rb.linearVelocity = g.vel;
            g.rb.angularVelocity = g.angVel;
            g.go.transform.SetPositionAndRotation(g.pos, g.rot);
            // held at whatever the server last applied for them. Their inputs for the replay window
            // itself have not reached the server yet, let alone us, so holding the last known controls
            // is the best available guess - and a far better one than assuming they let go of
            // everything, which turns any near miss into a collision that never happened.
            if (g.input != null) g.input.SetNetworkInput(g.throttle, g.steer, g.handbrake, false);
        }

        foreach (NetProtocol.InputSample s in inputs)
        {
            shadowInput.SetNetworkInput(s.throttle, s.steer, s.handbrake, s.reset);
            // manually stepping this scene never triggers Unity's automatic FixedUpdate loop, so the
            // car's own physics step (wheel forces, suspension, ...) has to be invoked by hand too
            shadowController.Tick(fixedDt);
            foreach (KeyValuePair<byte, Ghost> kv in ghosts)
                if (kv.Value.controller != null) kv.Value.controller.Tick(fixedDt);
            shadowPhysics.Simulate(fixedDt);
        }

        outPos = shadowRb.position;
        outRot = shadowRb.rotation;
        outVel = shadowRb.linearVelocity;
        outAngVel = shadowRb.angularVelocity;
    }

    void OnDestroy()
    {
        foreach (KeyValuePair<byte, Ghost> kv in ghosts) if (kv.Value.go != null) Destroy(kv.Value.go);
        ghosts.Clear();
        if (shadowCar != null) Destroy(shadowCar);
        if (shadowScene.IsValid()) SceneManager.UnloadSceneAsync(shadowScene);
    }
}
