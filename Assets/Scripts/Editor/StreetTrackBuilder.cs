using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Build Street Track : builds a giant-scale suburban street track.
// Everything is created under one object called "StreetTrack". Running it again deletes the old one first.
// (The class is split into several files: StreetTrack*.cs - all of them are part of this tool.)
public static partial class StreetTrackBuilder
{
    // =====================================================================
    //   EDIT HERE - כאן משנים את המסלול
    // =====================================================================

    // מקום המסלול במפה. רחוק ממסלול הבדיקה כדי שלא יתערבבו.
    public static Vector3 WorldOrigin = new Vector3(1000f, 0f, 0f);

    // רוחב הכביש במטרים (הרכב הוא בערך 1 מטר רוחב)
    public static float RoadWidth = 14f;

    // גובה המדרכה/אבן השפה במטרים. גלגלי הבאגי (רדיוס ~0.36) מטפסים עד בערך 0.35.
    // 0.3 = אפשר לעלות על המדרכה. 0.7 = קיר שאי אפשר לעלות עליו.
    public static float CurbHeight = 0.3f;

    // רוחב המדרכה מאחורי אבן השפה
    public static float SidewalkWidth = 6f;

    // נקודת ציון: x, z = המקום (במטרים), radius = רדיוס העיגול של הפינה בנקודה הזו (0 = בלי עיגול)
    public class WP
    {
        public float x, z, radius;
        public WP(float x, float z, float radius) { this.x = x; this.z = z; this.radius = radius; }
    }

    // הכביש עובר בין הנקודות לפי הסדר וחוזר לנקודה הראשונה (הנקודה הראשונה = קו הזינוק, חייבת radius 0).
    // צורת "שמונה": שני הלולאות נפגשות בצומת ב-(0,0) - שם יש מעבר חצייה.
    public static WP[] Waypoints =
    {
        new WP( -30,    0,  0),   // 0: קו זינוק, נוסעים מזרחה
        new WP(  30,    0, 60),   // 1: תחילת קטע S עדין
        new WP(  70,  -20, 60),   // 2: סוף קטע S
        new WP( 150,  -20, 50),   // 3: פנייה רחבה ארוכה (רדיוס 50)
        new WP( 150,  145, 16),   // 4: פנייה חדה של 90 מעלות (רדיוס 16)
        new WP(   0,  145, 25),   // 5: פינה צפון-מערב
        new WP(   0,  -80, 20),   // 6: יורדים דרומה דרך הצומת
        new WP( -90,  -80, 20),   // 7: פינה
        new WP( -90,    0, 25),   // 8: חוזרים לקו הזינוק
    };

    // הקיצור דרך החצר: מתחיל על הכביש ומסתיים על הכביש (בלי פינות)
    public static WP[] ShortcutWaypoints =
    {
        new WP(150,  75, 0),
        new WP( 80, 145, 0),
    };
    public static float ShortcutWidth = 8f;

    // מספר קבוע שקובע איך הבתים והחפצים מפוזרים (אותו מספר = אותו מסלול)
    public static int RandomSeed = 7;

    // =====================================================================

    const string RootName = "StreetTrack";
    internal const float SampleStep = 2f;

    static PathData main, cut;
    static Batch batch;
    static Transform root;
    static System.Random rng;
    static List<Obb> occupied;
    static Vector3[] mouth;
    static float hw;

    [MenuItem("Tools/Build Street Track")]
    public static void Build()
    {
        if (GameObject.Find("Car") == null) RaceSceneSetup.Build();

        // remove the previous street track (no duplicates)
        GameObject old = GameObject.Find(RootName);
        while (old != null)
        {
            Undo.DestroyObjectImmediate(old);
            old = GameObject.Find(RootName);
        }

        hw = RoadWidth * 0.5f;
        rng = new System.Random(RandomSeed);
        occupied = new List<Obb>();
        batch = new Batch();
        main = BuildPath(Waypoints, true);
        cut = BuildPath(ShortcutWaypoints, false);
        int m = main.Count;

        GameObject rootGo = new GameObject(RootName);
        rootGo.transform.position = WorldOrigin;
        Undo.RegisterCreatedObjectUndo(rootGo, "Build Street Track");
        root = rootGo.transform;
        BeginProps();

        // shortcut mouths = the first and last part of the shortcut (curbs are opened there)
        List<Vector3> mouthList = new List<Vector3>();
        for (int i = 0; i < cut.Count; i++)
        {
            float along = i * cut.step, fromEnd = (cut.Count - 1 - i) * cut.step;
            if (along < 32f || fromEnd < 32f) mouthList.Add(cut.pos[i]);
        }
        mouth = mouthList.ToArray();

        // where do roads cross, and where must curbs / lines stop?
        float[] otherD = DistToOtherParts();
        bool[] cross = new bool[m];
        bool[][] gap = { new bool[m], new bool[m] };
        float[] lateral = { hw - 0.7f, hw, hw + SidewalkWidth * 0.5f, hw + SidewalkWidth };
        for (int i = 0; i < m; i++)
        {
            cross[i] = otherD[i] < hw + 1f;
            for (int si = 0; si < 2; si++)
            {
                bool near = cross[i];
                float s = si == 0 ? -1f : 1f;
                for (int k = 0; k < lateral.Length && !near; k++)
                    if (NearMouth(main.pos[i] + main.right[i] * (s * lateral[k]))) near = true;
                gap[si][i] = near;
            }
        }

        int spawn = (m - 6) % m; // 12 m before the start line

        BuildRoad(cross, gap);
        BuildCurbs(gap);
        BuildCrosswalks(otherD);
        BuildStartLine(spawn);
        BuildShortcut();
        BuildHouses();
        BuildRoadProps(cross, gap, spawn);
        BuildGroundAndBoundary();
        batch.Emit(root);

        SetupSunAndSky();
        PlaceCar(spawn);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Street track ready: " + m + " samples, " + occupied.Count + " houses. Press Play. Space = handbrake, R = reset.");
    }

    // big grass ground + invisible walls so the car can never leave the map
    static void BuildGroundAndBoundary()
    {
        Vector3 min = main.pos[0], max = main.pos[0];
        foreach (Vector3 p in main.pos)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Vector3 center = (min + max) * 0.5f;
        float sx = max.x - min.x, sz = max.z - min.z;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "StreetGround";
        ground.transform.SetParent(root, false);
        ground.transform.localScale = new Vector3(sx + 1600f, 1f, sz + 1600f);
        ground.transform.localPosition = new Vector3(center.x, -0.5f, center.z);
        ground.GetComponent<Renderer>().sharedMaterial = MakeMat("grass", Palette["grass"]);

        float margin = 140f, t = 4f, hgt = 60f;
        float wx = sx + margin * 2f + t * 2f, wz = sz + margin * 2f + t * 2f;
        Vector3 e = new Vector3(sx * 0.5f + margin + t * 0.5f, 0f, sz * 0.5f + margin + t * 0.5f);
        Quaternion id = Quaternion.identity;
        AddBoxCollider("Boundary_N", new Vector3(center.x, hgt * 0.5f, center.z + e.z), id, Vector3.zero, new Vector3(wx, hgt, t));
        AddBoxCollider("Boundary_S", new Vector3(center.x, hgt * 0.5f, center.z - e.z), id, Vector3.zero, new Vector3(wx, hgt, t));
        AddBoxCollider("Boundary_E", new Vector3(center.x + e.x, hgt * 0.5f, center.z), id, Vector3.zero, new Vector3(t, hgt, wz));
        AddBoxCollider("Boundary_W", new Vector3(center.x - e.x, hgt * 0.5f, center.z), id, Vector3.zero, new Vector3(t, hgt, wz));
    }

    // warm sunny day
    static void SetupSunAndSky()
    {
        Light sun = null;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null)
        {
            GameObject go = new GameObject("Directional Light");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            Undo.RegisterCreatedObjectUndo(go, "Create Light");
        }
        sun.color = new Color(1f, 0.93f, 0.78f);
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        RenderSettings.sun = sun;

        Shader sky = Shader.Find("Skybox/Procedural");
        if (sky != null)
        {
            Material skyMat = new Material(sky);
            skyMat.SetColor("_SkyTint", new Color(0.5f, 0.62f, 0.92f));
            skyMat.SetColor("_GroundColor", new Color(0.7f, 0.68f, 0.6f));
            skyMat.SetFloat("_Exposure", 1.3f);
            skyMat.SetFloat("_SunSize", 0.06f);
            skyMat.SetFloat("_AtmosphereThickness", 1.0f);
            RenderSettings.skybox = skyMat;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
    }

    // put the car on the grid, facing along the road, and move the camera behind it
    static void PlaceCar(int spawn)
    {
        GameObject car = GameObject.Find("Car");
        if (car == null) return;
        if (car.GetComponent<SpeedDisplay>() == null) Undo.AddComponent<SpeedDisplay>(car);

        Vector3 pos = root.position + main.pos[spawn] + Vector3.up * 0.4f;
        car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(main.fwd[spawn], Vector3.up));
        Rigidbody rb = car.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            FollowCamera follow = cam.GetComponent<FollowCamera>();
            float dist = follow != null ? follow.distance : 5f;
            float hgt = follow != null ? follow.height : 2.2f;
            cam.farClipPlane = 1500f;
            cam.transform.position = pos - main.fwd[spawn] * dist + Vector3.up * hgt;
            cam.transform.LookAt(pos + Vector3.up * 0.5f);
        }
        Selection.activeGameObject = car;
    }
}
