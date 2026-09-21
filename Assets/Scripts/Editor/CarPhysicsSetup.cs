using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Apply New Car Physics : sets up the wheel-based physics on the existing Car. Safe to run many times.
public static class CarPhysicsSetup
{
    static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };
    const string CollidersName = "BodyColliders";
    const string ModelName = "CarModel";

    [MenuItem("Tools/Apply New Car Physics")]
    public static void Apply()
    {
        GameObject car = GameObject.Find("Car");
        if (car == null)
        {
            Debug.LogWarning("No Car in the scene. Run Tools > Setup Race Scene first.");
            return;
        }
        ApplyToCar(car);
        ApplyProjectSettings();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = car;
        Debug.Log("New car physics applied. Press Play. (Old controller is kept, but disabled.)");
    }

    public static void ApplyToCar(GameObject car)
    {
        // 1) the old controller stays as a backup but is switched off
        CarController_Old old = car.GetComponent<CarController_Old>();
        if (old != null && old.enabled)
        {
            Undo.RecordObject(old, "Disable old controller");
            old.enabled = false;
        }

        // 2) the new components
        CarController cc = car.GetComponent<CarController>();
        if (cc == null) cc = Undo.AddComponent<CarController>(car);
        CarWheelVisuals vis = car.GetComponent<CarWheelVisuals>();
        if (vis == null) vis = Undo.AddComponent<CarWheelVisuals>(car);
        if (car.GetComponent<CarInput>() == null) Undo.AddComponent<CarInput>(car);

        // 3) wheels are done with sphere casts now: remove the old wheel / body colliders
        Transform oldCols = car.transform.Find(CollidersName);
        if (oldCols != null) Undo.DestroyObjectImmediate(oldCols.gameObject);
        foreach (Collider c in car.GetComponentsInChildren<Collider>(true))
            Undo.DestroyObjectImmediate(c);

        // 4) a few box colliders that follow the shape of the car
        BuildBodyColliders(car.transform);

        // 5) visible wheels follow the suspension
        BuildWheelBindings(car.transform, vis, cc);

        Rigidbody rb = car.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.mass = cc.body.mass;
            rb.useGravity = true;
        }
    }

    // ------------------------------------------------------------------
    static bool Measure(Transform car, List<Renderer> list, out Vector3 min, out Vector3 max)
    {
        min = Vector3.one * float.MaxValue;
        max = Vector3.one * float.MinValue;
        bool any = false;
        foreach (Renderer r in list)
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 l = car.InverseTransformPoint(c);
                min = Vector3.Min(min, l);
                max = Vector3.Max(max, l);
                any = true;
            }
        }
        return any;
    }

    static bool IsWheelPart(string n)
    {
        n = n.ToLower();
        return n.Contains("wheel") || n.Contains("axle");
    }

    static void BuildBodyColliders(Transform car)
    {
        // wheel layout from the markers
        Transform[] m = new Transform[4];
        for (int i = 0; i < 4; i++) m[i] = car.Find(WheelNames[i]);
        if (m[0] == null || m[1] == null || m[2] == null || m[3] == null)
        {
            Debug.LogWarning("Wheel_FL/FR/BL/BR markers not found on the Car. Run Tools > Setup Race Scene first.");
            return;
        }
        float diameter = (m[0].localScale.x + m[1].localScale.x + m[2].localScale.x + m[3].localScale.x) * 0.25f;
        float half = Mathf.Abs(m[1].localPosition.x);

        // the size of the body (everything that is not a wheel or axle)
        Vector3 bmin, bmax;
        Transform holder = car.Find(ModelName);
        List<Renderer> parts = new List<Renderer>();
        if (holder != null)
        {
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>())
                if (!IsWheelPart(r.gameObject.name)) parts.Add(r);
        }
        if (parts.Count == 0 || !Measure(car, parts, out bmin, out bmax))
        {
            // simple box car: use the Body and Cabin boxes
            Transform body = car.Find("Body"), cabin = car.Find("Cabin");
            bmin = new Vector3(-0.5f, -0.05f, -1f);
            bmax = new Vector3(0.5f, 0.65f, 1f);
            if (body != null)
            {
                bmin = body.localPosition - body.localScale * 0.5f;
                bmax = body.localPosition + body.localScale * 0.5f;
            }
            if (cabin != null) bmax.y = Mathf.Max(bmax.y, cabin.localPosition.y + cabin.localScale.y * 0.5f);
        }

        float bottom = Mathf.Max(bmin.y, diameter * 0.15f);
        float top = Mathf.Min(Mathf.Max(bmax.y, bottom + 0.3f), bottom + 1.4f);
        float width = Mathf.Max(0.4f, Mathf.Min(bmax.x - bmin.x, 2f * half - diameter * 0.3f));
        float length = Mathf.Max(0.5f, (bmax.z - bmin.z) * 0.95f);
        float cz = (bmin.z + bmax.z) * 0.5f;
        float chassisTop = bottom + (top - bottom) * 0.5f;

        GameObject root = new GameObject(CollidersName);
        Undo.RegisterCreatedObjectUndo(root, "Body colliders");
        root.transform.SetParent(car, false);

        AddBox(root.transform, "Col_Chassis", new Vector3(0f, (bottom + chassisTop) * 0.5f, cz), new Vector3(width, chassisTop - bottom, length));
        AddBox(root.transform, "Col_Cabin", new Vector3(0f, (chassisTop + top) * 0.5f, cz - length * 0.05f), new Vector3(width * 0.75f, top - chassisTop, length * 0.55f));

        // skirt: one low box around all four wheels (bumper height). It stops the car against walls with one flat
        // face per side, so nothing catches on a corner, and the contact stays low so wall hits do not roll the car.
        // It sits above the height a wheel can climb, so curbs still work.
        float r0 = diameter * 0.5f;
        float thick = Mathf.Max(0.15f, diameter * 0.3f);
        float frontZ = (m[0].localPosition.z + m[1].localPosition.z) * 0.5f;
        float rearZ = (m[2].localPosition.z + m[3].localPosition.z) * 0.5f;
        float wheelY = (m[0].localPosition.y + m[1].localPosition.y + m[2].localPosition.y + m[3].localPosition.y) * 0.25f;
        AddBox(root.transform, "Col_Skirt",
            new Vector3(0f, wheelY + r0 * 0.55f, (frontZ + rearZ) * 0.5f),
            new Vector3(2f * half + thick, r0 * 0.8f, Mathf.Abs(frontZ - rearZ) + r0 * 1.6f));
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = center;
        bc.size = size;
    }

    // ------------------------------------------------------------------
    const string PivotPrefix = "WheelPivot_";

    // Every visible wheel gets a small pivot object centered on the wheel:
    // the pivot moves + steers, the wheel mesh inside only spins. Forks and axles ride along inside the same pivot.
    static void BuildWheelBindings(Transform car, CarWheelVisuals vis, CarController cc)
    {
        Undo.RecordObject(vis, "Wheel visuals");
        vis.car = cc;
        List<CarWheelVisuals.Binding> list = new List<CarWheelVisuals.Binding>();

        Vector3[] rest = new Vector3[4];
        Transform[] markers = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            markers[i] = car.Find(WheelNames[i]);
            rest[i] = markers[i] != null ? markers[i].localPosition : Vector3.zero;
        }
        float half = Mathf.Abs(rest[1].x);

        Transform holder = car.Find(ModelName);
        if (holder != null)
        {
            List<Renderer> wheelMeshes = new List<Renderer>();
            List<Renderer> attachments = new List<Renderer>();
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLower();
                if (n.Contains("wheel")) wheelMeshes.Add(r);
                else if (n.Contains("axle") || n.Contains("fork")) attachments.Add(r);
            }

            List<float> pivotZ = new List<float>();
            List<Transform> pivots = new List<Transform>();

            foreach (Renderer r in wheelMeshes)
            {
                Vector3 mn, mx;
                Measure(car, new List<Renderer> { r }, out mn, out mx);
                Vector3 c = (mn + mx) * 0.5f, s = mx - mn;
                bool pair = s.x > Mathf.Max(s.y, s.z) * 1.3f || Mathf.Abs(c.x) < half * 0.25f;

                // a small object centered on the wheel
                Transform pivot = r.transform.parent;
                if (pivot == null || !pivot.name.StartsWith(PivotPrefix))
                {
                    GameObject pg = new GameObject(PivotPrefix + r.gameObject.name);
                    pivot = pg.transform;
                    pivot.SetParent(holder, false);
                    pivot.SetPositionAndRotation(car.TransformPoint(c), car.rotation);   // axes = the car's axes
                    r.transform.SetParent(pivot, true);                                    // the wheel keeps its place
                }
                pivots.Add(pivot);
                pivotZ.Add(c.z);

                CarWheelVisuals.Binding b = new CarWheelVisuals.Binding();
                b.pivot = pivot;
                b.obj = r.transform;
                b.spin = true;
                if (pair)
                {
                    bool front = c.z > (rest[0].z + rest[2].z) * 0.5f;
                    b.wheelIndices = front ? new[] { 0, 1 } : new[] { 2, 3 };
                    b.steer = front;
                }
                else
                {
                    int best = 0;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < 4; i++)
                    {
                        float d = new Vector2(rest[i].x - c.x, rest[i].z - c.z).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    b.wheelIndices = new[] { best };
                    b.steer = best < 2;
                }
                list.Add(b);
            }

            // forks and axles ride along inside the pivot of the nearest wheel (same end of the car)
            foreach (Renderer r in attachments)
            {
                if (r.transform.parent != null && r.transform.parent.name.StartsWith(PivotPrefix)) continue;
                if (pivots.Count == 0) break;
                Vector3 mn, mx;
                Measure(car, new List<Renderer> { r }, out mn, out mx);
                float z = (mn.z + mx.z) * 0.5f;
                int nearest = 0;
                for (int i = 1; i < pivots.Count; i++)
                    if (Mathf.Abs(pivotZ[i] - z) < Mathf.Abs(pivotZ[nearest] - z)) nearest = i;
                r.transform.SetParent(pivots[nearest], true);
            }
        }
        else
        {
            // simple box car: the four wheel cylinders are the visible wheels (their origin is already their center)
            for (int i = 0; i < 4; i++)
            {
                if (markers[i] == null) continue;
                Renderer r = markers[i].GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                CarWheelVisuals.Binding b = new CarWheelVisuals.Binding();
                b.obj = markers[i];
                b.wheelIndices = new[] { i };
                b.spin = true;
                b.steer = i < 2;
                list.Add(b);
            }
        }
        vis.bindings = list.ToArray();
        EditorUtility.SetDirty(vis);
        Debug.Log("Wheel visuals: " + list.Count + " visible wheel object(s) connected to the physics wheels.");
    }

    // ------------------------------------------------------------------
    // Fixed Timestep 0.01 and more solver iterations (saved in Project Settings).
    static void ApplyProjectSettings()
    {
        try
        {
            Object[] tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (tm != null && tm.Length > 0)
            {
                SerializedObject so = new SerializedObject(tm[0]);
                SerializedProperty p = so.FindProperty("Fixed Timestep");
                if (p != null && p.floatValue > 0.0101f)
                {
                    p.floatValue = 0.01f;
                    so.ApplyModifiedProperties();
                    Debug.Log("Project Settings: Fixed Timestep set to 0.01.");
                }
            }
            Object[] dm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (dm != null && dm.Length > 0)
            {
                SerializedObject so = new SerializedObject(dm[0]);
                SerializedProperty it = so.FindProperty("m_DefaultSolverIterations");
                SerializedProperty vit = so.FindProperty("m_DefaultSolverVelocityIterations");
                bool changed = false;
                if (it != null && it.intValue < 8) { it.intValue = 8; changed = true; }
                if (vit != null && vit.intValue < 2) { vit.intValue = 2; changed = true; }
                if (changed)
                {
                    so.ApplyModifiedProperties();
                    Debug.Log("Project Settings: solver iterations raised (8 / 2).");
                }
            }
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Could not change Project Settings automatically (" + e.Message + "). Set Edit > Project Settings > Time > Fixed Timestep = 0.01 by hand. The game also sets it when it starts.");
        }
    }
}
