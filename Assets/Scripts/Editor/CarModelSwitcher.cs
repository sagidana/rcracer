using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Puts a 3D vehicle model on the player car and fits the physics to it.
// The car keeps working the same way (CarController, CarInput) - only the looks and the collider sizes change.
// Menu: Tools > Car Model > ...
public static class CarModelSwitcher
{
    // =====================================================================
    //   EDIT HERE - כאן בוחרים איזה רכב ברירת מחדל
    // =====================================================================

    // הדגם שנבחר כברירת מחדל (Vehicle19 = באגי עם גלגלים גדולים)
    public static string DefaultPrefabPath = "Assets/Free Adventure Vehicles/Prefabs/Vehicle19.prefab";

    // אורך הרכב במטרים (הרכב המקורי היה 2 מטר)
    public static float TargetLength = 2.4f;

    // =====================================================================

    const string PrefKey = "RCRACE.CarModelPath";
    const string ModelName = "CarModel";
    static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };

    [MenuItem("Tools/Car Model/Buggy (Vehicle19)")]
    static void UseVehicle19() { Use("Assets/Free Adventure Vehicles/Prefabs/Vehicle19.prefab"); }

    [MenuItem("Tools/Car Model/Roadster (Vehicle16)")]
    static void UseVehicle16() { Use("Assets/Free Adventure Vehicles/Prefabs/Vehicle16.prefab"); }

    [MenuItem("Tools/Car Model/Bike (Vehicle14)")]
    static void UseVehicle14() { Use("Assets/Free Adventure Vehicles/Prefabs/Vehicle14.prefab"); }

    [MenuItem("Tools/Car Model/Use Selected Prefab From Project")]
    static void UseSelected()
    {
        GameObject go = Selection.activeObject as GameObject;
        string path = go != null ? AssetDatabase.GetAssetPath(go) : "";
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("Select a vehicle prefab in the Project window first, then run this menu item.");
            return;
        }
        Use(path);
    }

    [MenuItem("Tools/Car Model/Simple Box Car (original)")]
    static void UseBoxCar()
    {
        GameObject car = GameObject.Find("Car");
        if (car == null) { Debug.LogWarning("No Car in the scene. Run Tools > Setup Race Scene first."); return; }
        RemoveModel(car);
        SetPrimitivesVisible(car, true);
        // original box car shape
        Transform body = car.transform.Find("Body");
        if (body != null) { body.localPosition = new Vector3(0f, 0.15f, 0f); body.localScale = new Vector3(1f, 0.4f, 2f); }
        float x = 0.55f, z = 0.65f;
        Vector3[] pos = { new Vector3(-x, 0f, z), new Vector3(x, 0f, z), new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z) };
        for (int i = 0; i < 4; i++)
        {
            Transform w = car.transform.Find(WheelNames[i]);
            if (w != null) { w.localPosition = pos[i]; w.localScale = new Vector3(0.4f, 0.1f, 0.4f); }
        }
        EditorPrefs.SetString(PrefKey, "");
        if (car.GetComponent<CarController>() != null) CarPhysicsSetup.ApplyToCar(car);
        Debug.Log("Car is back to the simple box car.");
    }

    static void Use(string path)
    {
        GameObject car = GameObject.Find("Car");
        if (car == null) { Debug.LogWarning("No Car in the scene. Run Tools > Setup Race Scene first."); return; }
        if (Apply(car, path))
        {
            EditorPrefs.SetString(PrefKey, path);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }

    // Called by "Setup Race Scene" after it builds the car: uses the last chosen model, or the default.
    public static void ApplyToCar(GameObject car)
    {
        string path = EditorPrefs.HasKey(PrefKey) ? EditorPrefs.GetString(PrefKey) : DefaultPrefabPath;
        if (string.IsNullOrEmpty(path)) return; // "simple box car" was chosen
        Apply(car, path);
    }

    // ---------------------------------------------------------------------

    static void RemoveModel(GameObject car)
    {
        Transform old = car.transform.Find(ModelName);
        while (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
            old = car.transform.Find(ModelName);
        }
    }

    static void SetPrimitivesVisible(GameObject car, bool visible)
    {
        string[] names = { "Body", "Cabin", "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };
        foreach (string n in names)
        {
            Transform t = car.transform.Find(n);
            if (t == null) continue;
            Renderer r = t.GetComponent<Renderer>();
            if (r != null) r.enabled = visible;
        }
    }

    // Bounding box of some renderers, in the car's own space.
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

    static Renderer FindWheel(List<Renderer> all, string side)
    {
        foreach (Renderer r in all)
        {
            string n = r.gameObject.name.ToLower();
            if (n.Contains("wheel") && n.Contains(side)) return r;
        }
        return null;
    }

    public static bool Apply(GameObject car, string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("Car model not found: " + path);
            return false;
        }

        RemoveModel(car);
        Transform ct = car.transform;

        GameObject holder = new GameObject(ModelName);
        Undo.RegisterCreatedObjectUndo(holder, "Set Car Model");
        holder.transform.SetParent(ct, false);
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // the physics of the car is done by the car itself - remove the model's own colliders
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        // URP versions of the materials (the pack uses old Standard-shader materials, which show pink in URP)
        Dictionary<Material, Material> cache = new Dictionary<Material, Material>();
        List<Renderer> all = new List<Renderer>(model.GetComponentsInChildren<Renderer>());
        foreach (Renderer r in all)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = ToUrp(mats[i], cache);
            r.sharedMaterials = mats;
        }

        Renderer front = FindWheel(all, "front"), rear = FindWheel(all, "rear");
        bool hasWheels = front != null && rear != null;
        Vector3 min, max;

        // 1) turn the model so that it faces the car's forward direction (+Z)
        if (hasWheels)
        {
            Vector3 dir = ct.InverseTransformPoint(front.bounds.center) - ct.InverseTransformPoint(rear.bounds.center);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                holder.transform.localRotation = Quaternion.FromToRotation(dir.normalized, Vector3.forward);
        }
        else if (Measure(ct, all, out min, out max) && (max.x - min.x) > (max.z - min.z) * 1.15f)
        {
            holder.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        }

        // 2) scale to the wanted length
        Measure(ct, all, out min, out max);
        float length = Mathf.Max(max.z - min.z, 0.01f);
        holder.transform.localScale = Vector3.one * (TargetLength / length);

        // 3) move so the wheel centers are at the car's center height and the model is centered
        Measure(ct, all, out min, out max);
        Vector3 offset;
        if (hasWheels)
        {
            Vector3 fmin, fmax, rmin, rmax;
            Measure(ct, new List<Renderer> { front }, out fmin, out fmax);
            Measure(ct, new List<Renderer> { rear }, out rmin, out rmax);
            Vector3 fc = (fmin + fmax) * 0.5f, rc = (rmin + rmax) * 0.5f;
            offset = new Vector3(-(min.x + max.x) * 0.5f, -(fc.y + rc.y) * 0.5f, -(fc.z + rc.z) * 0.5f);
        }
        else
        {
            offset = new Vector3(-(min.x + max.x) * 0.5f, -0.2f - min.y, -(min.z + max.z) * 0.5f);
        }
        holder.transform.localPosition += offset;

        // 4) fit the physics colliders (wheels + body) to the model
        SetPrimitivesVisible(car, false);
        if (hasWheels) FitPhysics(ct, all, front, rear);

        // keep the new wheel physics in step with the new model
        if (car.GetComponent<CarController>() != null) CarPhysicsSetup.ApplyToCar(car);

        string msg = "Car model set: " + prefab.name + (hasWheels ? " (wheels found, physics fitted)" : " (no separate wheels found, physics unchanged)");
        Debug.Log(msg);
        return true;
    }

    static void FitPhysics(Transform car, List<Renderer> all, Renderer front, Renderer rear)
    {
        Vector3 fmin, fmax, rmin, rmax;
        Measure(car, new List<Renderer> { front }, out fmin, out fmax);
        Measure(car, new List<Renderer> { rear }, out rmin, out rmax);
        Vector3 fs = fmax - fmin, rs = rmax - rmin;
        float diameter = (Mathf.Min(fs.y, fs.z) + Mathf.Min(rs.y, rs.z)) * 0.5f;
        float pairWidth = Mathf.Max(fs.x, rs.x);
        float frontZ = (fmin.z + fmax.z) * 0.5f, rearZ = (rmin.z + rmax.z) * 0.5f;

        // everything that is not a wheel = the body
        List<Renderer> bodyParts = new List<Renderer>();
        foreach (Renderer r in all)
            if (!r.gameObject.name.ToLower().Contains("wheel")) bodyParts.Add(r);
        Vector3 bmin = new Vector3(-0.5f, 0f, -1f), bmax = new Vector3(0.5f, 0.5f, 1f);
        if (bodyParts.Count > 0) Measure(car, bodyParts, out bmin, out bmax);

        // a wheel object that is much wider than a wheel = both wheels of one axle in one object
        bool pair = pairWidth > diameter * 1.3f;
        float half = pair ? pairWidth * 0.5f - diameter * 0.15f : Mathf.Max(0.35f, (bmax.x - bmin.x) * 0.4f);

        Vector3[] pos =
        {
            new Vector3(-half, 0f, frontZ), new Vector3(half, 0f, frontZ),
            new Vector3(-half, 0f, rearZ), new Vector3(half, 0f, rearZ)
        };
        for (int i = 0; i < 4; i++)
        {
            Transform w = car.Find(WheelNames[i]);
            if (w == null) continue;
            w.localPosition = pos[i];
            w.localScale = new Vector3(diameter, 0.1f, diameter); // sphere collider radius = diameter / 2
        }

        Transform body = car.Find("Body");
        if (body != null)
        {
            float bottom = Mathf.Max(bmin.y, diameter * 0.15f);
            float top = Mathf.Min(Mathf.Max(bmax.y, bottom + 0.3f), bottom + 1.4f);
            float width = Mathf.Min(bmax.x - bmin.x, half * 2f);
            float len = Mathf.Max((bmax.z - bmin.z) * 0.9f, 0.5f);
            body.localPosition = new Vector3(0f, (bottom + top) * 0.5f, (bmin.z + bmax.z) * 0.5f);
            body.localScale = new Vector3(Mathf.Max(width, 0.4f), top - bottom, len);
        }
    }

    // ---------- materials ----------

    static Material ToUrp(Material src, Dictionary<Material, Material> cache)
    {
        if (src == null) return null;
        if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;
        Material m;
        if (cache.TryGetValue(src, out m)) return m;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return src;

        m = new Material(lit);
        m.name = src.name + " (URP)";
        Texture tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
        Color col = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
        if (tex != null) m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", col);
        bool isGlass = (src.HasProperty("_Mode") && src.GetFloat("_Mode") >= 2f) || src.name.ToLower().Contains("glass");
        // glossy paint rather than flat plastic; glass gets close to a mirror finish
        m.SetFloat("_Smoothness", isGlass ? 0.92f : 0.45f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", isGlass ? 0f : 0.12f);
        if (isGlass)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        cache[src] = m;
        return m;
    }
}
