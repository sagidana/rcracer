using UnityEngine;

// Camera that stays behind and above the car and follows smoothly.
public class FollowCamera : MonoBehaviour
{
    public Transform target;              // the car
    public float distance = 5f;           // distance behind the car
    public float height = 2.2f;           // height above the car
    public float lookHeight = 0.5f;       // point above the car that the camera looks at
    public float positionSmoothing = 6f;  // higher = follows tighter
    public float rotationSmoothing = 4f;  // higher = turns faster with the car

    public float shakeDecay = 6f;         // how fast a camera shake fades out

    Vector3 smoothForward = Vector3.forward;
    float shake;

    void Start()
    {
        if (target != null) smoothForward = FlatForward();
    }

    Vector3 FlatForward()
    {
        Vector3 f = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        return f.sqrMagnitude < 0.001f ? smoothForward : f.normalized;
    }

    // Called by the car on a hard hit. strength ~ 0.1 (small) .. 1 (big).
    public void Shake(float strength)
    {
        shake = Mathf.Max(shake, strength);
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        smoothForward = Vector3.Slerp(smoothForward, FlatForward(), 1f - Mathf.Exp(-rotationSmoothing * dt));

        Vector3 wanted = target.position - smoothForward * distance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-positionSmoothing * dt));

        Vector3 lookPoint = target.position + Vector3.up * lookHeight;
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);

        if (shake > 0.001f)
        {
            transform.position += Random.insideUnitSphere * (shake * 0.35f);
            shake = Mathf.Max(0f, shake - shake * shakeDecay * dt - 0.02f * dt);
        }
    }
}
