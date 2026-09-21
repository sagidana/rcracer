using UnityEngine;

// A purely visual stand-in for another player's car. No physics, no local input: NetClient feeds it a
// target position/rotation/velocity from each server snapshot (~20 Hz) and this smoothly interpolates
// toward it every frame, dead-reckoning with the last known velocity so movement does not stall between
// snapshots. Attached to a spawned car prefab whose physics/input/wheel-visual components have been
// disabled by NetClient (only the body moves; wheels stay static - see NetClient.SpawnRemote).
public class RemoteCarView : MonoBehaviour
{
    Vector3 targetPos;
    Quaternion targetRot = Quaternion.identity;
    Vector3 targetVel;
    float lastSnapshotTime;
    bool ready;

    [Tooltip("כמה מהר הרכב נצמד למיקום שהשרת שלח (גבוה = תגובה מיידית יותר, נמוך = חלק יותר).")]
    public float followSpeed = 12f;

    public void SetTarget(Vector3 pos, Quaternion rot, Vector3 vel)
    {
        targetPos = pos;
        targetRot = rot;
        targetVel = vel;
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
        Vector3 predicted = targetPos + targetVel * (Time.time - lastSnapshotTime);
        float k = 1f - Mathf.Exp(-followSpeed * dt);
        transform.position = Vector3.Lerp(transform.position, predicted, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
    }
}
