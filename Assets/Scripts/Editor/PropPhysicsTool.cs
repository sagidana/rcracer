using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Make Props Knockable : cones, trash cans and mailboxes of the StreetTrack become light physics objects.
// (The street track generator also calls MakeKnockable for every new prop.)
public static class PropPhysicsTool
{
    const string RootName = "StreetTrack";

    static float DefaultMass(string name)
    {
        if (name == "Cone") return 0.6f;
        if (name == "TrashCan") return 3f;
        return 4f; // Mailbox
    }

    static bool IsSmallProp(string name)
    {
        return name == "Cone" || name == "TrashCan" || name == "Mailbox";
    }

    // Adds a Rigidbody + KnockableProp (or updates them). Running it twice changes nothing.
    public static void MakeKnockable(GameObject go, float mass)
    {
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.3f;
        rb.sleepThreshold = 0.05f;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.interpolation = RigidbodyInterpolation.None;

        KnockableProp kp = go.GetComponent<KnockableProp>();
        if (kp == null) kp = go.AddComponent<KnockableProp>();
        kp.mass = mass;
    }

    [MenuItem("Tools/Make Props Knockable")]
    public static void Run()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning("No StreetTrack in the scene. Run Tools > Build Street Track first.");
            return;
        }

        int made = 0, old = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!IsSmallProp(t.gameObject.name) || t.GetComponent<BoxCollider>() == null) continue;
            if (t.GetComponentInChildren<Renderer>() == null) { old++; continue; } // old build: the picture is part of a merged mesh
            MakeKnockable(t.gameObject, DefaultMass(t.gameObject.name));
            made++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Knockable props: " + made + " ready, " + old + " old-style props found.");

        if (old > 0)
        {
            bool rebuild = EditorUtility.DisplayDialog("Make Props Knockable",
                "The street track was built with an older version, where the small props are part of the road picture and cannot fly.\n\n" +
                "Build the street track again? (The new build makes them knockable automatically. The car goes back to the start.)",
                "Build again", "Cancel");
            if (rebuild) StreetTrackBuilder.Build();
        }
    }
}
