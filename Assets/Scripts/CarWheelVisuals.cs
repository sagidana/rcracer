using System;
using System.Text;
using UnityEngine;

// Moves the visible wheels of the 3D model so they follow the REAL physics:
//  - up/down with the real suspension,
//  - spinning with the real ground speed (forward = forward, reverse = backward),
//  - the front wheels steer left/right.
// Every wheel sits in a small "pivot" object that is centered on the wheel:
//   pivot = position + steering (+ tilt), the wheel mesh inside the pivot = spinning only.
// It only reads from CarController - it never changes the physics.
public class CarWheelVisuals : MonoBehaviour
{
    [Serializable]
    public class Binding
    {
        [Tooltip("מרכז הגלגל: אובייקט ריק שמזיז ומפנה את הגלגל.")]
        public Transform pivot;          // small object centered on the wheel (moves + steers)
        [Tooltip("הגלגל עצמו (התמונה). הוא רק מסתובב.")]
        public Transform obj;            // the wheel mesh (spins)
        [Tooltip("איזה גלגלים פיזיקליים הוא עוקב: 0=קדמי שמאל 1=קדמי ימין 2=אחורי שמאל 3=אחורי ימין. שניים = ציר שלם.")]
        public int[] wheelIndices;
        public bool spin = true;         // wheels spin
        public bool steer;               // front wheels steer

        // ---- runtime (set in Start) ----
        [NonSerialized] public Vector3 restCarPos;        // pivot position, car space
        [NonSerialized] public Quaternion restCarRot;     // pivot rotation, car space
        [NonSerialized] public Vector3 restLocalPos;      // wheel mesh position inside the pivot
        [NonSerialized] public Quaternion restLocalRot;   // wheel mesh rotation inside the pivot
        [NonSerialized] public Vector3 pivotToCenter;     // (only when there is no pivot)
        [NonSerialized] public Vector3 smoothPos;
        [NonSerialized] public float steerVisual;         // degrees shown right now
        [NonSerialized] public float lastSpinDeg;
        [NonSerialized] public float spinSpeedDegPerSec;
        [NonSerialized] public bool ready;
    }

    public CarController car;
    public Binding[] bindings = new Binding[0];

    [Header("Steering look - איך ההיגוי נראה")]
    [Tooltip("זווית ההיגוי המקסימלית של הגלגלים הנראים (מעלות) במהירות נמוכה.")]
    public float maxVisualSteer = 30f;

    [Tooltip("כמה מהזווית נשארת במהירות גבוהה (0.5 = חצי). ככה הגלגלים פונים פחות כשנוסעים מהר.")]
    [Range(0f, 1f)] public float highSpeedFraction = 0.5f;

    [Tooltip("המהירות (מטר בשנייה) שבה הזווית מגיעה לערך של מהירות גבוהה.")]
    public float highSpeedReference = 26f;

    [Tooltip("כמה מהר הגלגלים פונים ליעד (גבוה = מהר). נמוך = תנועה רכה. הגלגלים חוזרים ישר באותה מהירות.")]
    public float steerSmoothing = 12f;

    [Header("Suspension look")]
    [Tooltip("החלקה קלה של תנועת הגלגלים למעלה ולמטה (0 = בלי).")]
    public float smoothing = 40f;

    [Header("Debug")]
    [Tooltip("אם מסומן, ב-Console מודפסים מהירות הסיבוב וזווית ההיגוי של כל גלגל, פעמיים בשנייה.")]
    public bool debugLog = false;
    public float debugInterval = 0.5f;

    float nextLog;

    void Start()
    {
        if (car == null) car = GetComponent<CarController>();
        Transform ct = transform;
        StringBuilder sb = new StringBuilder("CarWheelVisuals: " + bindings.Length + " visible wheel objects.");
        foreach (Binding b in bindings)
        {
            if (b == null || b.obj == null) continue;
            if (b.pivot != null)
            {
                b.restCarPos = ct.InverseTransformPoint(b.pivot.position);
                b.restCarRot = Quaternion.Inverse(ct.rotation) * b.pivot.rotation;
                b.restLocalPos = b.obj.localPosition;
                b.restLocalRot = b.obj.localRotation;
                b.smoothPos = b.restCarPos;
            }
            else
            {
                // old style (no pivot): rotate the object itself around the center of its picture
                Bounds bounds = BoundsOf(b.obj);
                b.restCarPos = ct.InverseTransformPoint(bounds.center);
                b.restCarRot = Quaternion.Inverse(ct.rotation) * b.obj.rotation;
                b.pivotToCenter = b.restCarPos - ct.InverseTransformPoint(b.obj.position);
                b.smoothPos = ct.InverseTransformPoint(b.obj.position);
            }
            b.ready = true;
            sb.Append("\n  - ").Append(b.obj.name).Append(b.pivot != null ? " (pivot " + b.pivot.name + ")" : " (no pivot)")
              .Append(" follows wheels ").Append(string.Join(",", b.wheelIndices ?? new int[0])).Append(b.steer ? " [steers]" : "").Append(b.spin ? " [spins]" : "");
        }
        if (bindings.Length == 0) sb.Append(" NONE - run Tools > Apply New Car Physics.");
        if (debugLog) Debug.Log(sb.ToString());
    }

    static Bounds BoundsOf(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    void LateUpdate()
    {
        if (car == null || car.Wheels == null) return;
        Transform ct = transform;
        float dt = Time.deltaTime;
        float k = smoothing > 0f ? 1f - Mathf.Exp(-smoothing * dt) : 1f;
        float ks = steerSmoothing > 0f ? 1f - Mathf.Exp(-steerSmoothing * dt) : 1f;

        // how far the wheels may turn at this speed
        float speedT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, Mathf.Max(1f, highSpeedReference), car.PlanarSpeed));
        float maxAngle = Mathf.Lerp(maxVisualSteer, maxVisualSteer * highSpeedFraction, speedT);
        float steerTarget = car.SteerNormalized * maxAngle;

        StringBuilder log = debugLog && Time.time >= nextLog
            ? new StringBuilder("WHEELS  speed=" + car.PlanarSpeed.ToString("0.0") + " m/s  steerInput=" + car.SteerNormalized.ToString("0.00"))
            : null;

        foreach (Binding b in bindings)
        {
            if (b == null || !b.ready || b.obj == null || b.wheelIndices == null || b.wheelIndices.Length == 0) continue;

            // average movement of the physics wheels it follows (compared to their resting place)
            Vector3 delta = Vector3.zero;
            float spinDeg = 0f;
            foreach (int i in b.wheelIndices)
            {
                CarWheel w = car.Wheels[i];
                delta += w.centerLocal - w.restCenterLocal;
                spinDeg += w.spinDegrees;
            }
            float n = b.wheelIndices.Length;
            delta /= n; spinDeg /= n;

            // a rigid axle with two wheels: tilt it like the two wheels
            float roll = 0f;
            if (b.wheelIndices.Length == 2)
            {
                CarWheel a = car.Wheels[b.wheelIndices[0]], c = car.Wheels[b.wheelIndices[1]];
                float dx = c.restCenterLocal.x - a.restCenterLocal.x;
                float dy = (c.centerLocal.y - c.restCenterLocal.y) - (a.centerLocal.y - a.restCenterLocal.y);
                if (Mathf.Abs(dx) > 0.01f) roll = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                delta.x = 0f;
            }

            // steering: smooth, and back to center when the keys are released
            b.steerVisual = Mathf.Lerp(b.steerVisual, b.steer ? steerTarget : 0f, ks);

            b.spinSpeedDegPerSec = dt > 0f ? (spinDeg - b.lastSpinDeg) / dt : 0f;
            b.lastSpinDeg = spinDeg;

            Quaternion steerRot = Quaternion.AngleAxis(b.steerVisual, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);

            if (b.pivot != null)
            {
                b.smoothPos = Vector3.Lerp(b.smoothPos, b.restCarPos + delta, k);
                // the pivot moves and steers ...
                b.pivot.SetPositionAndRotation(ct.TransformPoint(b.smoothPos), ct.rotation * (steerRot * b.restCarRot));
                // ... the wheel inside only spins around the axle (the pivot's own X axis)
                if (b.spin)
                {
                    Quaternion s = Quaternion.AngleAxis(spinDeg, Vector3.right);
                    b.obj.localRotation = s * b.restLocalRot;
                    b.obj.localPosition = s * b.restLocalPos;    // a wheel with an off-center origin still turns around its own center
                }
            }
            else
            {
                Quaternion rot = steerRot * Quaternion.AngleAxis(b.spin ? spinDeg : 0f, Vector3.right) * b.restCarRot;
                Quaternion deltaRot = rot * Quaternion.Inverse(b.restCarRot);
                Vector3 pivotPos = b.restCarPos + delta - deltaRot * b.pivotToCenter;
                b.smoothPos = Vector3.Lerp(b.smoothPos, pivotPos, k);
                b.obj.SetPositionAndRotation(ct.TransformPoint(b.smoothPos), ct.rotation * rot);
            }

            if (log != null)
                log.Append("\n  ").Append(b.obj.name).Append(": spin=").Append(b.spinSpeedDegPerSec.ToString("0")).Append(" deg/s")
                   .Append("  steer=").Append(b.steerVisual.ToString("0.0")).Append(" deg");
        }

        if (log != null)
        {
            for (int i = 0; i < car.Wheels.Length; i++)
            {
                CarWheel w = car.Wheels[i];
                log.Append("\n  physics wheel ").Append(i).Append(w.grounded ? " (on ground)" : " (in air)")
                   .Append(": spin=").Append(w.spinRate.ToString("0")).Append(" deg/s  steer=").Append(w.steerAngle.ToString("0.0")).Append(" deg");
            }
            Debug.Log(log.ToString());
            nextLog = Time.time + Mathf.Max(0.1f, debugInterval);
        }
    }
}
