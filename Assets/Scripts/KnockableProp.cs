using UnityEngine;

// A small prop (cone, trash can, mailbox) that the car can knock away.
// It is light, bouncy and slippery, falls asleep when calm, and goes back home when it flies too far.
[RequireComponent(typeof(Rigidbody))]
public class KnockableProp : MonoBehaviour
{
    [Tooltip("משקל החפץ בק\"ג. חרוט קל מאוד, פח ותיבת דואר קצת כבדים יותר. הרכב שוקל 20.")]
    public float mass = 1f;

    [Tooltip("כמה החפץ קופץ (0 עד 1).")]
    [Range(0f, 1f)] public float bounciness = 0.4f;

    [Tooltip("חיכוך של החפץ עם הקרקע. נמוך = מחליק רחוק.")]
    public float friction = 0.25f;

    [Header("Cleanup - ניקוי")]
    [Tooltip("אם מסומן, חפץ שעף רחוק או נפל מהמפה חוזר למקום המקורי שלו.")]
    public bool returnHome = true;

    [Tooltip("מרחק מהבית (במטרים) שמעבר לו החפץ נחשב \"רחוק\".")]
    public float farDistance = 40f;

    [Tooltip("גובה שמתחתיו החפץ נחשב שנפל מהמפה.")]
    public float fallHeight = -20f;

    [Tooltip("כמה שניות החפץ צריך להיות רחוק לפני שהוא חוזר הביתה.")]
    public float returnDelay = 8f;

    [Tooltip("החפץ חוזר הביתה רק אם הרכב רחוק ממנו לפחות כמה מטרים (כדי שלא יקפוץ מול העיניים).")]
    public float playerDistance = 40f;

    Rigidbody rb;
    Vector3 homePosition;
    Quaternion homeRotation;
    float awayTime;
    Transform player;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        homePosition = transform.position;
        homeRotation = transform.rotation;

        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.3f;
        rb.sleepThreshold = 0.05f;
        rb.maxDepenetrationVelocity = 3f;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        PhysicsMaterial mat = new PhysicsMaterial("Prop");
        mat.dynamicFriction = friction;
        mat.staticFriction = friction;
        mat.bounciness = bounciness;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounceCombine = PhysicsMaterialCombine.Maximum;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger) c.sharedMaterial = mat;
    }

    void Start()
    {
        CarController car = FindFirstObjectByType<CarController>();
        if (car != null) player = car.transform;
        // check once a second, at a random moment so all props do not check together
        InvokeRepeating(nameof(Check), Random.Range(0.5f, 1.5f), 1f);
    }

    void Check()
    {
        if (!returnHome) return;

        if (transform.position.y < fallHeight)
        {
            GoHome();
            return;
        }

        bool far = (transform.position - homePosition).sqrMagnitude > farDistance * farDistance;
        if (!far) { awayTime = 0f; return; }

        awayTime += 1f;
        if (awayTime < returnDelay) return;
        if (player != null && (player.position - transform.position).sqrMagnitude < playerDistance * playerDistance) return;
        GoHome();
    }

    void GoHome()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(homePosition, homeRotation);
        awayTime = 0f;
        rb.Sleep();
    }
}
