using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Setup Race Scene : builds the test scene automatically.
public static class RaceSceneSetup
{
    [MenuItem("Tools/Setup Race Scene")]
    public static void Build()
    {
        // clean up a previous run
        DestroyIfExists("Ground");
        DestroyIfExists("Car");

        // ---- ground ----
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(300f, 1f, 300f);
        ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.4f, 0.35f));
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");

        // ---- car ----
        GameObject car = new GameObject("Car");
        car.transform.position = new Vector3(0f, 0.3f, 0f);
        Rigidbody rb = car.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 2f;
        car.AddComponent<CarInput>();
        car.AddComponent<CarController>();
        car.AddComponent<SpeedDisplay>();

        Material bodyMat = MakeMaterial(new Color(1f, 0.15f, 0.1f));
        Material cabinMat = MakeMaterial(new Color(1f, 0.85f, 0.1f));
        Material wheelMat = MakeMaterial(new Color(0.08f, 0.08f, 0.08f));

        // body (slippery collider so walls do not grab the car)
        GameObject body = MakePart(PrimitiveType.Cube, "Body", car.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1f, 0.4f, 2f), bodyMat);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.AddComponent<BoxCollider>().sharedMaterial = SlipperyMaterial();
        // cabin (visual only)
        GameObject cabin = MakePart(PrimitiveType.Cube, "Cabin", car.transform, new Vector3(0f, 0.5f, -0.2f), new Vector3(0.8f, 0.3f, 0.8f), cabinMat);
        Object.DestroyImmediate(cabin.GetComponent<Collider>());

        // wheels
        float x = 0.55f, z = 0.65f;
        MakeWheel(car.transform, "Wheel_FL", new Vector3(-x, 0f, z), wheelMat);
        MakeWheel(car.transform, "Wheel_FR", new Vector3(x, 0f, z), wheelMat);
        MakeWheel(car.transform, "Wheel_BL", new Vector3(-x, 0f, -z), wheelMat);
        MakeWheel(car.transform, "Wheel_BR", new Vector3(x, 0f, -z), wheelMat);
        Undo.RegisterCreatedObjectUndo(car, "Create Car");
        CarModelSwitcher.ApplyToCar(car); // 3D model (Tools > Car Model to change it)
        CarPhysicsSetup.ApplyToCar(car);  // wheel-based physics (Tools > Apply New Car Physics)

        // ---- camera ----
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
        }
        FollowCamera follow = cam.GetComponent<FollowCamera>();
        if (follow == null) follow = Undo.AddComponent<FollowCamera>(cam.gameObject);
        follow.target = car.transform;
        cam.transform.position = new Vector3(0f, 2.5f, -5f);
        cam.transform.LookAt(car.transform.position);

        // ---- light ----
        bool hasDirectional = false;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) hasDirectional = true;
        if (!hasDirectional)
        {
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Undo.RegisterCreatedObjectUndo(lightGo, "Create Light");
        }

        Selection.activeGameObject = car;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Race scene ready. Press Play and drive with arrows / WASD. R = put the car back on its wheels.");
    }

    static void DestroyIfExists(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    internal static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return m;
    }

    static PhysicsMaterial SlipperyMaterial()
    {
        PhysicsMaterial pm = new PhysicsMaterial("Slippery");
        pm.dynamicFriction = 0f;
        pm.staticFriction = 0f;
        pm.frictionCombine = PhysicsMaterialCombine.Minimum;
        return pm;
    }

    static GameObject MakePart(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void MakeWheel(Transform parent, string name, Vector3 pos, Material mat)
    {
        GameObject w = MakePart(PrimitiveType.Cylinder, name, parent, pos, new Vector3(0.4f, 0.1f, 0.4f), mat);
        w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        // smooth sphere instead of the default cylinder collider - rolls without snagging
        Object.DestroyImmediate(w.GetComponent<Collider>());
        SphereCollider sc = w.AddComponent<SphereCollider>();
        sc.radius = 0.5f;
        sc.sharedMaterial = SlipperyMaterial();
    }
}
