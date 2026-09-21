using UnityEngine;

// One wheel of the car: where it is mounted, and what it touches right now.
// (No MonoBehaviour: CarController owns 4 of these and updates them.)
public class CarWheel
{
    // --- fixed data ---
    public Transform marker;            // Wheel_FL / FR / BL / BR object of the car
    public bool isFront;
    public bool isLeft;
    public float radius;                // real wheel radius (meters)
    public Vector3 restCenterLocal;     // where the wheel center is when the car stands still (car space)
    public Vector3 mountLocal;          // top of the suspension (car space)

    // --- state, updated every physics step ---
    public bool grounded;
    public float length;                // distance from mount to wheel center (0 = fully compressed)
    public float compression;           // travel - length
    public Vector3 contactPoint;
    public Vector3 contactNormal = Vector3.up;
    public Collider groundCollider;
    public float suspensionForce;       // last spring force (used as the wheel load)
    public float steerAngle;            // degrees (front wheels)
    public float spinDegrees;           // accumulated rolling angle for the visual wheel
    public float spinRate;              // degrees per second
    public Vector3 centerLocal;         // current wheel center (car space) - used by CarWheelVisuals
    public bool debugSlipping;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    // Sweeps the wheel sphere down from the mount and finds what it touches.
    // lift: the sweep starts a bit higher than the mount so it never starts inside the ground.
    // physicsScene: cast against THIS scene's geometry specifically, never the global/default one -
    // a car living in an isolated PhysicsScene (see NetPredictor) must never sense the main scene.
    public void Cast(Transform car, Rigidbody body, float travel, float radiusScale, float lift, float lightMass, PhysicsScene physicsScene)
    {
        Vector3 up = car.up;
        Vector3 origin = car.TransformPoint(mountLocal) + up * lift;
        float r = radius * radiusScale;
        int n = physicsScene.SphereCast(origin, r, -up, hitBuffer, travel + lift, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider == null) continue;
            if (h.distance <= 0.0001f && h.point == Vector3.zero) continue;   // sphere started inside something: ignore
            Rigidbody other = h.collider.attachedRigidbody;
            if (other == body) continue;                                      // our own colliders
            if (other != null && other.mass <= lightMass) continue;           // cones, cans: the wheel does not ride on them
            if (Vector3.Dot(h.normal, up) < 0.2f) continue;                  // a wall, not a floor: the wheel cannot stand on it
            if (h.distance < best) { best = h.distance; bestIndex = i; }
        }

        if (bestIndex >= 0)
        {
            RaycastHit h = hitBuffer[bestIndex];
            grounded = true;
            length = Mathf.Clamp(h.distance - lift, 0f, travel);
            contactPoint = h.point;
            contactNormal = h.normal;
            groundCollider = h.collider;
        }
        else
        {
            grounded = false;
            length = travel;                 // hangs down at full length
            groundCollider = null;
            contactNormal = up;
        }
        compression = travel - length;
        centerLocal = mountLocal - Vector3.up * length;
    }
}
