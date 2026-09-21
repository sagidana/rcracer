using UnityEngine;

// BACKUP of the old "sliding box" car physics (steps 1-3). Disabled by Tools > Apply New Car Physics. Do not delete.
// Arcade-style car physics. Does not read the keyboard - takes values from CarInput.
[RequireComponent(typeof(Rigidbody))]
public class CarController_Old : MonoBehaviour
{
    [Header("Speed")]
    public float maxSpeed = 25f;          // top speed forward
    public float maxReverseSpeed = 10f;   // top speed backward
    public float acceleration = 35f;      // how strongly the car accelerates
    public float brakeStrength = 60f;     // how strongly it brakes when pressing the opposite way
    public float coastDrag = 4f;          // natural slow-down when no key is pressed

    [Header("Steering")]
    public float turnSpeed = 130f;        // degrees per second at full steering
    public float steerResponse = 10f;     // how fast the car reacts to steering
    public float minSpeedToSteer = 4f;    // speed at which steering becomes full

    [Header("Grip")]
    [Range(0f, 20f)] public float grip = 10f;  // higher = less sideways sliding
    public float downForce = 15f;         // pushes the car down, keeps it stable
    [Range(0f, 1f)] public float airControl = 0.2f; // steering strength in the air

    [Header("Drift - בלם יד (רווח)")]
    // אחיזה בזמן שמחזיקים רווח. מספר נמוך = החלקה גדולה יותר. (אחיזה רגילה היא Grip למעלה)
    public float driftGrip = 4f;
    // כמה מהר האחיזה חוזרת אחרי שעוזבים את הרווח. נמוך = ההחלקה נמשכת יותר זמן.
    public float gripRecovery = 4f;
    // כמה מהר האחיזה נעלמת ברגע שלוחצים רווח. גבוה = ההחלקה מתחילה מיד.
    public float gripLossSpeed = 15f;
    // כמה הרכב מאט בזמן בלם יד (0 = לא מאט בכלל).
    public float handbrakeStrength = 12f;
    // כמה חד הרכב פונה בזמן בלם יד. 1 = כרגיל, 1.3 = חד ב-30% יותר.
    public float driftTurnBoost = 1.3f;

    [Header("החלקה טבעית בפניות")]
    // המהירות (מטר בשנייה) שממנה פנייה חדה מתחילה לגרום להחלקה קטנה.
    public float cornerSlideSpeed = 15f;
    // כמה אחיזה הולכת לאיבוד בפנייה חדה במהירות מלאה. 0 = אין החלקה, 0.3 = 30%.
    [Range(0f, 0.9f)] public float cornerSlideAmount = 0.3f;

    Rigidbody rb;
    CarInput input;
    bool grounded;
    float currentGrip;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();
        currentGrip = grip;
        rb.centerOfMass = new Vector3(0f, -0.1f, 0f); // low = hard to flip
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    void OnCollisionStay(Collision c)
    {
        for (int i = 0; i < c.contactCount; i++)
        {
            if (c.GetContact(i).normal.y > 0.5f) { grounded = true; return; }
        }
    }

    void FixedUpdate()
    {
        float throttle = input != null ? input.Throttle : 0f;
        float steer = input != null ? input.Steer : 0f;
        bool handbrake = input != null && input.Handbrake;
        if (input != null && input.ConsumeReset()) ResetUpright();

        float dt = Time.fixedDeltaTime;
        Vector3 vel = rb.linearVelocity;
        Vector3 planarVel = Vector3.ProjectOnPlane(vel, transform.up);
        float speed = planarVel.magnitude;
        float forwardSpeed = Vector3.Dot(vel, transform.forward);

        // --- how much grip do we want right now? ---
        float targetGrip = grip;
        if (handbrake)
        {
            targetGrip = driftGrip;
        }
        else
        {
            // hard cornering at high speed = small natural slide
            float slide = Mathf.InverseLerp(cornerSlideSpeed, maxSpeed, speed) * Mathf.Abs(steer);
            targetGrip = grip * (1f - cornerSlideAmount * slide);
        }
        // grip drops fast, comes back smoothly
        float rate = targetGrip < currentGrip ? gripLossSpeed : gripRecovery;
        currentGrip = Mathf.Lerp(currentGrip, targetGrip, 1f - Mathf.Exp(-rate * dt));

        if (grounded)
        {
            // --- throttle / brake / reverse ---
            if (Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = throttle * forwardSpeed < -0.5f;
                float limit = throttle > 0f ? maxSpeed : maxReverseSpeed;
                bool underLimit = throttle > 0f ? forwardSpeed < limit : forwardSpeed > -limit;
                if (braking)
                    rb.AddForce(transform.forward * throttle * brakeStrength, ForceMode.Acceleration);
                else if (underLimit)
                    rb.AddForce(transform.forward * throttle * acceleration, ForceMode.Acceleration);
            }
            else
            {
                rb.AddForce(-transform.forward * Mathf.Clamp(forwardSpeed, -1f, 1f) * coastDrag, ForceMode.Acceleration);
            }

            // --- handbrake: gentle slow-down while held ---
            if (handbrake && speed > 1.5f)
                rb.AddForce(-planarVel.normalized * handbrakeStrength, ForceMode.Acceleration);

            // --- grip: cancel sideways sliding ---
            float sideSpeed = Vector3.Dot(vel, transform.right);
            rb.AddForce(-transform.right * sideSpeed * Mathf.Clamp01(currentGrip * dt), ForceMode.VelocityChange);
        }

        // --- steering ---
        float control = grounded ? 1f : airControl;
        float speedFactor = Mathf.Clamp01(speed / minSpeedToSteer);
        float direction = forwardSpeed >= -0.1f ? 1f : -1f; // steering flips in reverse
        float boost = handbrake ? driftTurnBoost : 1f;
        float targetYaw = steer * turnSpeed * Mathf.Deg2Rad * speedFactor * direction * control * boost;
        Vector3 av = rb.angularVelocity;
        float currentYaw = Vector3.Dot(av, transform.up);
        av += transform.up * (targetYaw - currentYaw) * Mathf.Clamp01(steerResponse * dt);
        rb.angularVelocity = av;

        // --- down force ---
        rb.AddForce(-transform.up * downForce, ForceMode.Acceleration);

        grounded = false; // set again by OnCollisionStay
    }

    // Puts the car back on its wheels, slightly above the same spot, facing the same way.
    void ResetUpright()
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position += Vector3.up * 1f;
        rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        currentGrip = grip;
    }
}
