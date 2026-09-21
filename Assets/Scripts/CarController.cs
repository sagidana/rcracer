using UnityEngine;

// Wheel-based arcade car physics (step 5). Replaces CarController_Old.
// - Reads only from CarInput (throttle, steer, handbrake, reset).
// - Runs only in FixedUpdate.
// - 4 wheels, each with a suspension (spring + damper) and a sphere cast that touches the real ground.
// - Drive, brake and side-grip forces are applied at each wheel's contact point.
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Body / Rigidbody - גוף הרכב")]
    public BodySettings body = new BodySettings();
    [Header("Suspension - קפיצים")]
    public SuspensionSettings suspension = new SuspensionSettings();
    [Header("Drive - הנעה")]
    public DriveSettings drive = new DriveSettings();
    [Header("Steering - היגוי")]
    public SteeringSettings steering = new SteeringSettings();
    [Header("Grip / Drift - אחיזה והחלקה")]
    public GripSettings grip = new GripSettings();
    [Header("Collision - התנגשויות")]
    public CollisionSettings collision = new CollisionSettings();
    [Header("Props - חפצים קלים")]
    public PropSettings props = new PropSettings();
    [Header("Debug")]
    public DebugSettings debug = new DebugSettings();

    public CarWheel[] Wheels { get; private set; }
    public bool IsGrounded { get; private set; }
    // -1 (full left) .. 1 (full right): how far the steering is turned compared to what is allowed at this speed.
    public float SteerNormalized { get; private set; }
    // speed in meters per second (used by the visual wheels)
    public float PlanarSpeed { get; private set; }

    static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };

    Rigidbody rb;
    CarInput input;
    FollowCamera follow;
    PhysicsMaterial bodyMaterial;

    float steerAngle;
    float gripFront = 1f, gripRear = 1f;
    Vector3 lastVelocity, lastAngularVelocity;

    // unstick + impact
    float stuckTimer;
    bool wallTouch;                       // the body is pressed against a wall this step (not the ground, not a light prop)
    Vector3 wallNormalSum;
    bool touching;
    Vector3 touchNormalSum;
    float lastImpactTime = -10f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();

        if (body.setFastPhysicsStep && Time.fixedDeltaTime > 0.0101f) Time.fixedDeltaTime = 0.01f;

        Wheels = new CarWheel[4];
        for (int i = 0; i < 4; i++)
        {
            Transform m = transform.Find(WheelNames[i]);
            CarWheel w = new CarWheel();
            w.marker = m;
            w.isFront = i < 2;
            w.isLeft = (i % 2) == 0;
            if (m != null)
            {
                w.radius = Mathf.Max(0.05f, m.localScale.x * 0.5f);
                w.restCenterLocal = m.localPosition;
            }
            else
            {
                // no marker found: a default toy-size wheel layout
                w.radius = 0.2f;
                w.restCenterLocal = new Vector3(w.isLeft ? -0.55f : 0.55f, 0f, w.isFront ? 0.65f : -0.65f);
            }
            Wheels[i] = w;
        }
    }

    void Start()
    {
        ApplyBodySettings();
        PlaceMounts();
        ApplyCollisionMaterial();
        Camera cam = Camera.main;
        if (cam != null) follow = cam.GetComponent<FollowCamera>();
    }

    void ApplyBodySettings()
    {
        rb.mass = body.mass;
        rb.useGravity = true;
        rb.linearDamping = body.linearDrag;
        rb.angularDamping = body.angularDrag;
        rb.maxAngularVelocity = body.maxAngularSpeed;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.solverIterations = body.solverIterations;
        rb.solverVelocityIterations = 4;
        rb.maxDepenetrationVelocity = collision.maxPushOut;   // never "pop" out of a wall
        rb.sleepThreshold = 0f;                     // never fall asleep: the wheels push all the time
        rb.centerOfMass = body.centerOfMass;
        rb.ResetInertiaTensor();
        rb.inertiaTensor = rb.inertiaTensor * body.inertiaMultiplier;
        Vector3 it = rb.inertiaTensor;   // harder to flip (x, z) than to spin (y)
        rb.inertiaTensor = new Vector3(it.x * body.pitchRollInertia, it.y, it.z * body.pitchRollInertia);
    }

    // The suspension top is above the wheel's resting position by (travel - sag).
    void PlaceMounts()
    {
        float gEff = Physics.gravity.magnitude * Mathf.Max(1f, body.gravityScale);
        float sag = Mathf.Clamp(gEff / Mathf.Max(1f, suspension.springStrength), 0.1f * suspension.travel, 0.9f * suspension.travel);
        float restLength = suspension.travel - sag;
        foreach (CarWheel w in Wheels)
        {
            w.mountLocal = w.restCenterLocal + Vector3.up * restLength;
            w.centerLocal = w.restCenterLocal;   // the visual wheels start at their normal place
        }
    }

    void ApplyCollisionMaterial()
    {
        // a slippery body: the wall's normal push turns the car (natural yaw) instead of wall friction
        // grabbing the nose and stopping it. Minimum = the car's values win against any wall or prop.
        bodyMaterial = new PhysicsMaterial("CarBody");
        bodyMaterial.dynamicFriction = collision.bodyFriction;
        bodyMaterial.staticFriction = collision.bodyFriction;
        bodyMaterial.bounciness = collision.bounciness;
        bodyMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
        bodyMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) c.sharedMaterial = bodyMaterial;
    }

    void OnValidate()
    {
        if (!Application.isPlaying || bodyMaterial == null) return;
        bodyMaterial.bounciness = collision.bounciness;
        bodyMaterial.dynamicFriction = collision.bodyFriction;
        bodyMaterial.staticFriction = collision.bodyFriction;
        if (rb != null) rb.maxDepenetrationVelocity = collision.maxPushOut;
    }

    // ------------------------------------------------------------------
    //  Physics step
    // ------------------------------------------------------------------
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        lastVelocity = rb.linearVelocity;
        lastAngularVelocity = rb.angularVelocity;

        float throttle = input != null ? input.Throttle : 0f;
        float steerInput = input != null ? input.Steer : 0f;
        bool handbrake = input != null && input.Handbrake;
        if (input != null && input.ConsumeReset()) { ResetUpright(); return; }

        Transform t = transform;
        Vector3 up = t.up, fwd = t.forward;
        Vector3 vel = rb.linearVelocity;
        float speed = vel.magnitude;
        PlanarSpeed = speed;
        float mass = rb.mass;
        float mPer = mass * 0.25f;
        float gEff = Physics.gravity.magnitude * Mathf.Max(1f, body.gravityScale);

        UpdateSteering(steerInput, handbrake, speed, dt);
        UpdateGrip(steerInput, handbrake, speed, dt);

        // 1) what does every wheel touch?
        float lift = 0.4f * Wheels[0].radius;
        int groundedCount = 0;
        foreach (CarWheel w in Wheels)
        {
            w.Cast(t, rb, suspension.travel, suspension.wheelRadiusScale, lift, props.lightPropMass);
            if (w.grounded) groundedCount++;
        }
        IsGrounded = groundedCount > 0;

        // 2) suspension: spring + damper at the mount, plus anti-roll bars
        ApplySuspension(t, up, mPer);

        // 3) tire forces at the contact points
        int driven = drive.driveMode == DriveMode.AllWheel ? 4 : 2;
        float staticLoad = mPer * gEff;
        foreach (CarWheel w in Wheels)
            ApplyTire(w, t, up, fwd, throttle, handbrake, mass, mPer, driven, staticLoad, dt);

        // pressed against a wall: keep the car on its wheels (the wall pushes high, the tires hold low = a rollover couple)
        if (wallTouch && grip.wallUprightAssist > 0f)
        {
            Vector3 lean = Vector3.Cross(up, Vector3.up);                        // axis to rotate around to stand upright
            Vector3 spin = Vector3.ProjectOnPlane(rb.angularVelocity, Vector3.up); // roll/pitch rate only, never yaw
            rb.AddTorque((lean * grip.wallUprightAssist - spin * (grip.wallUprightAssist / 15f)) * mass);
        }

        // 4) extra arcade forces
        if (body.gravityScale > 1f)
            rb.AddForce(Physics.gravity * (body.gravityScale - 1f) * mass);
        if (groundedCount > 0)
            rb.AddForce(Vector3.down * (drive.downforce * speed * mass));
        else
            rb.angularVelocity *= Mathf.Clamp01(1f - grip.airStability * dt);

        UpdateUnstick(throttle, speed, dt);
        touching = false;
        touchNormalSum = Vector3.zero;
        wallTouch = false;
        wallNormalSum = Vector3.zero;
    }

    void UpdateSteering(float steerInput, bool handbrake, float speed, float dt)
    {
        float t01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, steering.highSpeedReference, speed));
        float maxAngle = Mathf.Lerp(steering.maxSteerAngle, steering.highSpeedSteerAngle, t01);
        if (handbrake) maxAngle *= steering.driftSteerBoost;
        float target = steerInput * maxAngle;
        bool returning = Mathf.Abs(target) < Mathf.Abs(steerAngle) || target * steerAngle < 0f;
        float rate = returning ? steering.steerReturn : steering.steerResponse;
        steerAngle = Mathf.Lerp(steerAngle, target, 1f - Mathf.Exp(-rate * dt));
        SteerNormalized = maxAngle > 0.01f ? Mathf.Clamp(steerAngle / maxAngle, -1f, 1f) : 0f;
    }

    void UpdateGrip(float steerInput, bool handbrake, float speed, float dt)
    {
        float slide = Mathf.InverseLerp(grip.cornerSlideSpeed, drive.maxSpeed, speed) * Mathf.Abs(steerInput);
        float corner = 1f - grip.cornerSlideAmount * slide;
        float targetFront = handbrake ? grip.driftGripFront : corner;
        float targetRear = handbrake ? grip.driftGripRear : corner;
        gripFront = SmoothGrip(gripFront, targetFront, dt);
        gripRear = SmoothGrip(gripRear, targetRear, dt);
    }

    float SmoothGrip(float current, float target, float dt)
    {
        float rate = target < current ? grip.gripLossSpeed : grip.gripRecovery;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
    }

    void ApplySuspension(Transform t, Vector3 up, float mPer)
    {
        float k = suspension.springStrength * mPer;
        float c = suspension.damping * mPer;
        float bumpStart = suspension.travel * 0.8f;

        foreach (CarWheel w in Wheels)
        {
            w.suspensionForce = 0f;
            if (!w.grounded) continue;
            Vector3 mount = t.TransformPoint(w.mountLocal);
            float f = k * w.compression;
            if (w.compression > bumpStart) f += k * 6f * (w.compression - bumpStart);   // soft bump stop
            f -= c * Vector3.Dot(rb.GetPointVelocity(mount), up);
            f = Mathf.Max(0f, f);
            w.suspensionForce = f;
            float align = Mathf.Clamp(Vector3.Dot(w.contactNormal, up), 0.3f, 1f);
            rb.AddForceAtPosition(up * (f * align), mount);
        }

        AntiRoll(t, up, Wheels[0], Wheels[1], mPer);
        AntiRoll(t, up, Wheels[2], Wheels[3], mPer);
    }

    void AntiRoll(Transform t, Vector3 up, CarWheel a, CarWheel b, float mPer)
    {
        if (!a.grounded && !b.grounded) return;
        float f = suspension.antiRoll * mPer * (a.compression - b.compression);
        rb.AddForceAtPosition(up * f, t.TransformPoint(a.mountLocal));
        rb.AddForceAtPosition(-up * f, t.TransformPoint(b.mountLocal));
    }

    void ApplyTire(CarWheel w, Transform t, Vector3 up, Vector3 fwd, float throttle, bool handbrake,
                   float mass, float mPer, int driven, float staticLoad, float dt)
    {
        w.steerAngle = w.isFront ? steerAngle : 0f;
        if (!w.grounded)
        {
            w.spinRate *= Mathf.Exp(-0.6f * dt);
            w.spinDegrees += w.spinRate * dt;
            w.debugSlipping = false;
            return;
        }

        // the wheel's own directions on the ground surface
        Vector3 n = w.contactNormal;
        Vector3 f = Vector3.ProjectOnPlane(Quaternion.AngleAxis(w.steerAngle, up) * fwd, n);
        if (f.sqrMagnitude < 0.0001f) return;
        f.Normalize();
        Vector3 r = Vector3.Cross(n, f);

        Vector3 cv = rb.GetPointVelocity(w.contactPoint);
        Rigidbody gb = w.groundCollider != null ? w.groundCollider.attachedRigidbody : null;
        if (gb != null) cv -= gb.GetPointVelocity(w.contactPoint);
        float vF = Vector3.Dot(cv, f);
        float vL = Vector3.Dot(cv, r);

        // how much friction can this wheel use?
        float load = Mathf.Max(Mathf.Lerp(staticLoad, w.suspensionForce, 0.5f), staticLoad * 0.15f);
        float axleGrip = w.isFront ? gripFront : gripRear;
        // sliding too far sideways: grip comes back, so a drift never turns into a spin
        float slipAngle = Mathf.Atan2(Mathf.Abs(vL), Mathf.Abs(vF) + 0.5f) * Mathf.Rad2Deg;
        axleGrip = Mathf.Lerp(axleGrip, 1f, Mathf.InverseLerp(grip.catchAngleStart, grip.catchAngleEnd, slipAngle));
        float budget = grip.friction * load * axleGrip;
        // pressed against a wall: the tires give way sideways, so the wall can shove and turn the car
        // instead of the car freezing with its nose in the wall
        if (wallTouch) budget *= grip.wallGripScale;

        // side grip: cancel sideways sliding, up to the friction limit
        float fyWanted = -vL * mPer / dt * grip.lateralStiffness;
        float fy = Mathf.Clamp(fyWanted, -budget, budget);
        float demand = Mathf.Abs(fyWanted) / Mathf.Max(budget, 0.001f);
        w.debugSlipping = demand > 1f;
        if (demand > 1f) fy *= Mathf.Lerp(1f, grip.slidingFriction, Mathf.Clamp01(demand - 1f));

        // drive / brake / coast along the wheel
        float fx = 0f;
        bool isRear = !w.isFront;
        bool drivenWheel = drive.driveMode == DriveMode.AllWheel || isRear;
        float absVF = Mathf.Abs(vF);
        if (Mathf.Abs(throttle) > 0.01f)
        {
            if (throttle * vF < -0.6f)
            {
                fx = Mathf.Sign(throttle) * Mathf.Min(drive.brakeStrength * mPer, absVF * mPer / dt * 0.9f);
            }
            else if (drivenWheel)
            {
                float limit = throttle > 0f ? drive.maxSpeed : drive.maxReverseSpeed;
                float ratio = absVF / Mathf.Max(limit, 0.1f);
                fx = throttle * drive.acceleration * (mass / driven) * Mathf.Clamp01(1f - ratio * ratio);
            }
        }
        else
        {
            float coast = absVF < 1.2f ? absVF * mPer / dt * 0.6f : Mathf.Min(drive.coastDrag * mPer, absVF * mPer / dt * 0.9f);
            fx = -Mathf.Sign(vF) * coast;
        }
        if (handbrake && isRear && absVF > 0.2f)
            fx -= Mathf.Sign(vF) * Mathf.Min(drive.handbrakeStrength * mPer, absVF * mPer / dt * 0.9f);

        // whatever friction is left after the side grip can be used for driving
        float fxMax = Mathf.Sqrt(Mathf.Max(0f, budget * budget - fy * fy));
        fx = Mathf.Clamp(fx, -fxMax, fxMax);

        rb.AddForceAtPosition(f * fx + r * fy, w.contactPoint + up * drive.forceHeight);

        // the visual wheel spins with the real ground speed (locked rear wheels with the handbrake)
        w.spinRate = (handbrake && isRear) ? 0f : vF / Mathf.Max(w.radius, 0.05f) * Mathf.Rad2Deg;
        w.spinDegrees += w.spinRate * dt;
    }

    // ------------------------------------------------------------------
    //  Reset, unstick, collisions
    // ------------------------------------------------------------------
    void ResetUpright()
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position += Vector3.up * 1f;
        rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        steerAngle = 0f;
        gripFront = gripRear = 1f;
        stuckTimer = 0f;
    }

    void UpdateUnstick(float throttle, float speed, float dt)
    {
        if (!collision.unstick) { stuckTimer = 0f; return; }
        if (touching && speed < 0.8f && Mathf.Abs(throttle) > 0.1f) stuckTimer += dt;
        else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);

        if (stuckTimer > collision.unstickTime)
        {
            Vector3 away = touchNormalSum.sqrMagnitude > 0.001f ? touchNormalSum.normalized : -transform.forward * Mathf.Sign(throttle);
            Vector3 push = (away + Vector3.up * 0.3f).normalized;
            rb.AddForce(push * (collision.unstickPush * rb.mass), ForceMode.Impulse);
            stuckTimer = 0f;
        }
    }

    void OnCollisionStay(Collision c)
    {
        touching = true;
        NoteContacts(c);
    }

    void NoteContacts(Collision c)
    {
        Rigidbody other = c.rigidbody;
        bool lightProp = other != null && other.mass <= props.lightPropMass;
        for (int i = 0; i < c.contactCount; i++)
        {
            Vector3 n = c.GetContact(i).normal;
            touchNormalSum += n;
            if (lightProp || Mathf.Abs(n.y) > 0.5f) continue;   // ground / roof / cone: not a wall
            wallTouch = true;
            wallNormalSum += n;
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.contactCount == 0) return;
        NoteContacts(c);

        // a light object (cone, trash can): keep most of our speed, the object flies away
        Rigidbody other = c.rigidbody;
        if (other != null && other.mass <= props.lightPropMass)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, lastVelocity, props.lightPropSpeedKeep);
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, lastAngularVelocity, props.lightPropSpeedKeep);
            return;
        }

        if (Time.time - lastImpactTime < 0.1f) return;   // one impact effect per hit, not one per collider

        Vector3 normal = Vector3.zero, point = Vector3.zero;
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint cp = c.GetContact(i);
            normal += cp.normal;
            point += cp.point;
        }
        normal.Normalize();
        point /= c.contactCount;

        float hitSpeed = -Vector3.Dot(lastVelocity, normal);           // speed going INTO the surface
        if (hitSpeed < collision.hardHitSpeed || normal.y > 0.7f) return; // soft touch, or just landing on the ground
        lastImpactTime = Time.time;

        // by default the physics engine alone decides the reaction (contact point, friction, inertia);
        // the scripted extras below are an arcade option
        if (collision.scriptedImpact)
        {
            float preSpeed = Mathf.Max(lastVelocity.magnitude, 0.1f);
            float headOn = Mathf.Clamp01(hitSpeed / preSpeed);             // 1 = straight into the wall, 0 = scraping along it

            // a bit more speed loss on head-on hits (glancing hits keep their speed)
            rb.linearVelocity *= 1f - collision.speedLossOnImpact * headOn;

            // a twist that depends on where the wall touched the car
            Vector3 arm = point - rb.worldCenterOfMass;
            float side = Vector3.Dot(Vector3.Cross(arm, normal), Vector3.up);
            float spin = Mathf.Clamp(side * hitSpeed * collision.spinOnImpact, -collision.maxImpactSpin, collision.maxImpactSpin);
            rb.angularVelocity += Vector3.up * spin;
        }

        if (follow != null && collision.cameraShake > 0f)
            follow.Shake(collision.cameraShake * Mathf.Clamp01(hitSpeed / 15f));
    }

    // ------------------------------------------------------------------
    //  Debug drawing (Scene view)
    // ------------------------------------------------------------------
    void OnDrawGizmos()
    {
        if (!debug.drawDebug) return;

        if (Application.isPlaying && Wheels != null)
        {
            Transform t = transform;
            foreach (CarWheel w in Wheels)
            {
                Vector3 mount = t.TransformPoint(w.mountLocal);
                Vector3 center = t.TransformPoint(w.centerLocal);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(mount, 0.05f);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(mount, center);
                Gizmos.color = w.grounded ? (w.debugSlipping ? new Color(1f, 0.5f, 0f) : Color.green) : Color.red;
                Gizmos.DrawWireSphere(center, w.radius * suspension.wheelRadiusScale);
                if (w.grounded)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawSphere(w.contactPoint, 0.05f);
                    Gizmos.DrawLine(w.contactPoint, w.contactPoint + w.contactNormal * 0.5f);
                }
            }
            return;
        }

        // not playing: show where the wheels are
        foreach (string n in WheelNames)
        {
            Transform m = transform.Find(n);
            if (m == null) continue;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(m.position, m.localScale.x * 0.5f);
        }
    }
}
