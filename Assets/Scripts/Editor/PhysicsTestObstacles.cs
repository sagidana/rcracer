using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Add Physics Test Obstacles : bumps, steps and a wall in front of the car, to test the suspension and wheel climbing.
// Left side of the road: things the wheels CAN climb. Right side: things that must stop the car. Run again = rebuilt, no duplicates.
public static class PhysicsTestObstacles
{
    const string RootName = "PhysicsTestObstacles";

    [MenuItem("Tools/Add Physics Test Obstacles")]
    public static void Build()
    {
        GameObject car = GameObject.Find("Car");
        if (car == null)
        {
            Debug.LogWarning("No Car in the scene. Run Tools > Setup Race Scene first.");
            return;
        }

        GameObject old = GameObject.Find(RootName);
        while (old != null)
        {
            Undo.DestroyObjectImmediate(old);
            old = GameObject.Find(RootName);
        }

        Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float groundY = 0f;
        RaycastHit hit;
        if (Physics.Raycast(car.transform.position + Vector3.up * 5f, Vector3.down, out hit, 20f)) groundY = hit.point.y;
        Vector3 origin = new Vector3(car.transform.position.x, groundY, car.transform.position.z);

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Physics test obstacles");
        Material climbMat = RaceSceneSetup.MakeMaterial(new Color(1f, 0.6f, 0.1f));
        Material blockMat = RaceSceneSetup.MakeMaterial(new Color(0.85f, 0.1f, 0.1f));

        float lane = 3.5f;   // left lane = -, right lane = +
        Bump(root.transform, origin + fwd * 18f - right * lane, rot, 0.12f, climbMat);
        Bump(root.transform, origin + fwd * 23f - right * lane, rot, 0.25f, climbMat);
        Step(root.transform, origin + fwd * 29f - right * lane, rot, 0.3f, 3f, climbMat);
        Step(root.transform, origin + fwd * 29f + right * lane, rot, 0.6f, 3f, blockMat);
        Step(root.transform, origin + fwd * 34f + right * lane, rot, 1.5f, 0.6f, blockMat);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Test obstacles added ahead of the car. Left lane: bumps 0.12 and 0.25, step 0.3 (climb). Right lane: step 0.6 and wall 1.5 (blocked).");
    }

    static void Step(Transform parent, Vector3 basePos, Quaternion rot, float height, float length, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Step_" + height.ToString("0.00");
        go.transform.SetParent(parent, true);
        go.transform.localScale = new Vector3(6f, height, length);
        go.transform.SetPositionAndRotation(basePos + Vector3.up * (height * 0.5f), rot);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // a half-buried cylinder across the lane
    static void Bump(Transform parent, Vector3 basePos, Quaternion rot, float rise, Material mat)
    {
        const float diameter = 1.4f;
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Bump_" + rise.ToString("0.00");
        go.transform.SetParent(parent, true);
        go.transform.localScale = new Vector3(diameter, 3f, diameter);
        go.transform.SetPositionAndRotation(basePos + Vector3.up * (rise - diameter * 0.5f), rot * Quaternion.Euler(0f, 0f, 90f));
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
