using UnityEngine;

// A purely visual stand-in for another player's car. No physics, no local input: NetClient feeds it a
// target position/rotation/velocity/angular velocity from each server snapshot (~20 Hz) and this
// smoothly interpolates toward it every frame, dead-reckoning ALONG THE CURVE the car is turning
// through (not a straight line - see NetClient.ReconcileTick for why that matters at speed) so movement
// does not stall or cut corners between snapshots. Attached to a spawned car prefab whose
// physics/input/wheel-visual components have been disabled by NetClient (only the body moves - see
// NetClient.SpawnRemote).
public class RemoteCarView : MonoBehaviour
{
    Vector3 targetPos;
    Quaternion targetRot = Quaternion.identity;
    Vector3 targetVel;
    Vector3 targetAngVel;
    float lastSnapshotTime;
    bool ready;

    [Tooltip("כמה מהר הרכב נצמד למיקום שהשרת שלח (גבוה = תגובה מיידית יותר, נמוך = חלק יותר).")]
    public float followSpeed = 12f;

    public void SetTarget(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
    {
        targetPos = pos;
        targetRot = rot;
        targetVel = vel;
        targetAngVel = angVel;
        lastSnapshotTime = Time.time;
        if (!ready)
        {
            // first snapshot for this car: appear exactly there instead of sliding in from the origin
            transform.SetPositionAndRotation(pos, rot);
            ready = true;
        }
    }

    void Update()
    {
        if (!ready) return;
        float dt = Time.deltaTime;
        float age = Time.time - lastSnapshotTime;

        Quaternion turn = Quaternion.Euler(targetAngVel * Mathf.Rad2Deg * age);
        Quaternion predictedRot = turn * targetRot;
        Vector3 midVel = Quaternion.Euler(targetAngVel * Mathf.Rad2Deg * age * 0.5f) * targetVel;
        Vector3 predicted = targetPos + midVel * age;

        float k = 1f - Mathf.Exp(-followSpeed * dt);
        transform.position = Vector3.Lerp(transform.position, predicted, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, predictedRot, k);
    }
}
