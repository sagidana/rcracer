using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Build Desert Track : a fast desert canyon loop - hills, banked sweepers, a hairpin, an S-bend and
// a tabletop jump on the start straight (ramp, table, landing slope), with sand shoulders, berms, rocks and cacti.
// Everything is created under one object called "DesertTrack"; running it again deletes the old one first.
// Uses the path and mesh helpers of StreetTrackBuilder.
public static class DesertTrackBuilder
{
    // =====================================================================
    //   EDIT HERE - כאן משנים את המסלול
    // =====================================================================

    // מקום המסלול במפה (רחוק מהמסלולים האחרים)
    public static Vector3 WorldOrigin = new Vector3(-1500f, 0f, 0f);

    // רוחב הכביש (מטרים) ורוחב שולי החול (נסיעים, אבל בלי קווים)
    public static float RoadWidth = 16f;
    public static float ShoulderWidth = 5f;

    // גובה הגבעות (מטרים) - הכביש עולה ויורד לאורך הסיבוב
    public static float HillHeight = 5f;

    // הטיה מקסימלית של הכביש בפניות (מעלות). הצד החיצוני גבוה יותר.
    public static float MaxBank = 12f;

    // הקפיצה: איפה על הישורת הראשונה (0 עד 1) וכמה גבוהה (מטרים)
    public static float JumpAt = 0.4f;
    public static float JumpHeight = 1.8f;

    // מספר קבוע שקובע איך הסלעים והקקטוסים מפוזרים
    public static int RandomSeed = 11;

    // נקודות ציון: x, z (מטרים), radius = רדיוס הפינה. הכביש סגור: חוזר מהנקודה האחרונה לראשונה.
    public static StreetTrackBuilder.WP[] Waypoints =
    {
        new StreetTrackBuilder.WP(-100,    0, 30),   // 0: קו זינוק על הישורת שאחרי, נוסעים מזרחה
        new StreetTrackBuilder.WP( 150,    0, 55),   // 1: פנייה רחבה ימינה (דרומה)
        new StreetTrackBuilder.WP( 170, -150, 20),   // 2: סיכת ראש
        new StreetTrackBuilder.WP(  60, -100, 25),   // 3: S
        new StreetTrackBuilder.WP( -10, -180, 30),   // 4: S
        new StreetTrackBuilder.WP(-170, -140, 45),   // 5: פנייה מערבה-צפונה
        new StreetTrackBuilder.WP(-230,  -10, 60),   // 6: פנייה ארוכה ומוטה
        new StreetTrackBuilder.WP(-170,  110, 50),   // 7: ממשיכים מזרחה
        new StreetTrackBuilder.WP( -30,  130, 40),   // 8: פנייה חדה חזרה לישורת
    };

    // =====================================================================

    const string RootName = "DesertTrack";
    const float RoadY = 0.05f;
    const float LineY = 0.07f;

    static StreetTrackBuilder.PathData path;
    static StreetTrackBuilder.Batch batch;
    static Transform root;
    static System.Random rng;
    static float hw;
    static float[] bank;         // signed degrees per sample (+ = right turn)
    static Vector3[] rightFlat;  // horizontal right vector per sample (the banked one lives in path.right)

    [MenuItem("Tools/Build Desert Track")]
    public static void Build()
    {
        if (GameObject.Find("Car") == null) RaceSceneSetup.Build();

        GameObject old = GameObject.Find(RootName);
        while (old != null)
        {
            Undo.DestroyObjectImmediate(old);
            old = GameObject.Find(RootName);
        }

        var pal = StreetTrackBuilder.Palette;
        pal["sand"] = new Color(0.86f, 0.74f, 0.5f);
        pal["dirt"] = new Color(0.36f, 0.27f, 0.2f);
        pal["shoulder"] = new Color(0.72f, 0.58f, 0.38f);
        pal["berm"] = new Color(0.62f, 0.47f, 0.3f);
        pal["rock"] = new Color(0.55f, 0.42f, 0.33f);
        pal["rock2"] = new Color(0.68f, 0.55f, 0.42f);
        pal["cactus"] = new Color(0.25f, 0.5f, 0.22f);
        pal["rumbleRed"] = new Color(0.85f, 0.15f, 0.12f);

        hw = RoadWidth * 0.5f;
        rng = new System.Random(RandomSeed);
        batch = new StreetTrackBuilder.Batch();
        path = StreetTrackBuilder.BuildPath(Waypoints, true);
        int m = path.Count;

        GameObject rootGo = new GameObject(RootName);
        rootGo.transform.position = WorldOrigin;
        Undo.RegisterCreatedObjectUndo(rootGo, "Build Desert Track");
        root = rootGo.transform;

        int startIndex = NearestSample(Lerp01(0, 1, 0.3f));
        int jumpIndex = NearestSample(Lerp01(0, 1, JumpAt));
        int spawn = (startIndex - 6 + m) % m;

        ApplyHillsAndJump(jumpIndex);
        ApplyBanking();

        StreetTrackBuilder.MeshData col = new StreetTrackBuilder.MeshData();
        BuildRoad(col);
        BuildLinesAndRumble();
        BuildStartLine(startIndex);
        StreetTrackBuilder.MakeColliderObject("RoadCollider", root, col);
        BuildProps();
        BuildGroundAndBoundary();
        batch.Emit(root);

        SetupSunAndSky();
        PlaceCar(spawn);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Desert track ready: " + m + " samples (" + (m * path.step).ToString("0") + " m). Press Play.");
    }

    // ---------- shape ----------

    static Vector3 Lerp01(int a, int b, float t)
    {
        Vector3 pa = new Vector3(Waypoints[a].x, 0f, Waypoints[a].z), pb = new Vector3(Waypoints[b].x, 0f, Waypoints[b].z);
        return Vector3.Lerp(pa, pb, t);
    }

    static int NearestSample(Vector3 p)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            float d = (path.pos[i] - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // rolling hills from a few sine waves along the loop, lifted so the lowest point sits just above the ground,
    // plus a tabletop jump: a ramp up, a short flat top and a drop
    static void ApplyHillsAndJump(int jumpIndex)
    {
        int m = path.Count;
        float total = m * path.step;
        float[] h = new float[m];
        float min = float.MaxValue;
        for (int i = 0; i < m; i++)
        {
            float s = (float)i / m;
            h[i] = HillHeight * Mathf.Sin(s * Mathf.PI * 2f)
                 + HillHeight * 0.7f * Mathf.Sin(s * Mathf.PI * 4f + 1.3f)
                 + HillHeight * 0.35f * Mathf.Sin(s * Mathf.PI * 10f + 0.7f);
            min = Mathf.Min(min, h[i]);
        }
        for (int i = 0; i < m; i++)
        {
            float y = h[i] - min + 0.4f;
            float d = (i - jumpIndex) * path.step;         // meters past the start of the ramp
            if (d >= 0f && d < 10f) y += JumpHeight * Mathf.SmoothStep(0f, 1f, d / 10f);        // up
            else if (d >= 10f && d < 16f) y += JumpHeight;                                       // table
            else if (d >= 16f && d < 28f) y += JumpHeight * (1f - Mathf.SmoothStep(0f, 1f, (d - 16f) / 12f)); // landing slope
            path.pos[i] = new Vector3(path.pos[i].x, y, path.pos[i].z);
        }
        // forward vectors follow the slopes
        for (int i = 0; i < m; i++)
        {
            Vector3 a = path.pos[(i - 1 + m) % m], b = path.pos[(i + 1) % m];
            path.fwd[i] = (b - a).normalized;
        }
        Debug.Log("Desert hills: loop " + total.ToString("0") + " m, height range " + (h.Length > 0 ? (MaxOf(h) - min).ToString("0.0") : "0") + " m, jump at sample " + jumpIndex);
    }

    static float MaxOf(float[] v)
    {
        float r = float.MinValue;
        foreach (float x in v) r = Mathf.Max(r, x);
        return r;
    }

    // banked corners: tilt the right vector around the forward vector, outer edge up, smoothed along the road
    static void ApplyBanking()
    {
        int m = path.Count;
        rightFlat = new Vector3[m];
        float[] raw = new float[m];
        for (int i = 0; i < m; i++)
        {
            Vector3 f0 = path.fwd[(i - 2 + m) % m], f1 = path.fwd[(i + 2) % m];
            f0.y = 0f; f1.y = 0f;
            float turn = Vector3.SignedAngle(f0, f1, Vector3.up);          // degrees over 4 samples, + = right turn
            float curvature = turn * Mathf.Deg2Rad / (4f * path.step);       // 1/m
            raw[i] = Mathf.Clamp(curvature * 600f, -MaxBank, MaxBank);       // r = 50 m -> 12 degrees
        }
        bank = new float[m];
        for (int i = 0; i < m; i++)
        {
            float sum = 0f; int n = 0;
            for (int k = -6; k <= 6; k++) { sum += raw[(i + k + m) % m]; n++; }
            bank[i] = sum / n;
        }
        for (int i = 0; i < m; i++)
        {
            Vector3 fwdFlat = path.fwd[i]; fwdFlat.y = 0f; fwdFlat.Normalize();
            rightFlat[i] = Vector3.Cross(Vector3.up, fwdFlat).normalized;
            // a right turn (bank > 0): the right edge goes down, the left (outer) edge up
            path.right[i] = Quaternion.AngleAxis(-bank[i], path.fwd[i]) * rightFlat[i];
        }
    }

    // ---------- road ----------

    static void BuildRoad(StreetTrackBuilder.MeshData col)
    {
        int m = path.Count;
        Vector3 up = Vector3.up;
        StreetTrackBuilder.MeshData dirt = batch["dirt"], shoulder = batch["shoulder"], berm = batch["berm"];
        float sw = ShoulderWidth;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            Vector3 pi = path.pos[i] + up * RoadY, pj = path.pos[j] + up * RoadY, ri = path.right[i], rj = path.right[j];

            // road
            Quad2(dirt, col, pi - ri * hw, pi + ri * hw, pj + rj * hw, pj - rj * hw, up);
            // sand shoulders (same tilt as the road)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = pi + ri * (s * hw), b = pi + ri * (s * (hw + sw));
                Vector3 c = pj + rj * (s * (hw + sw)), d = pj + rj * (s * hw);
                Quad2(shoulder, col, a, b, c, d, up);

                // berm: from the shoulder edge down to the ground, sloping outward
                Vector3 e0 = b, e1 = c;
                Vector3 g0 = new Vector3(e0.x, 0f, e0.z) + rightFlat[i] * (s * Mathf.Max(1f, e0.y * 1.6f));
                Vector3 g1 = new Vector3(e1.x, 0f, e1.z) + rightFlat[j] * (s * Mathf.Max(1f, e1.y * 1.6f));
                Quad2(berm, col, e0, g0, g1, e1, up);
            }
        }
    }

    static void Quad2(StreetTrackBuilder.MeshData vis, StreetTrackBuilder.MeshData col, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
    {
        vis.Quad(a, b, c, d, facing);
        col.Quad(a, b, c, d, facing);
    }

    // white edge lines everywhere, red/white rumble strips on the inside of banked corners
    static void BuildLinesAndRumble()
    {
        int m = path.Count;
        Vector3 up = Vector3.up;
        StreetTrackBuilder.MeshData white = batch["lineWhite"], red = batch["rumbleRed"];
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            Vector3 pi = path.pos[i] + up * LineY, pj = path.pos[j] + up * LineY, ri = path.right[i], rj = path.right[j];
            for (int s = -1; s <= 1; s += 2)
            {
                bool inner = (s > 0 && bank[i] > 4f) || (s < 0 && bank[i] < -4f);
                if (inner)
                {
                    StreetTrackBuilder.MeshData strip = (i / 2) % 2 == 0 ? red : white;
                    strip.Quad(pi + ri * (s * (hw - 1.4f)), pi + ri * (s * hw), pj + rj * (s * hw), pj + rj * (s * (hw - 1.4f)), up);
                }
                else
                {
                    white.Quad(pi + ri * (s * (hw - 0.8f)), pi + ri * (s * (hw - 0.45f)), pj + rj * (s * (hw - 0.45f)), pj + rj * (s * (hw - 0.8f)), up);
                }
            }
        }
    }

    static void BuildStartLine(int start)
    {
        Vector3 up = Vector3.up;
        Vector3 p = path.pos[start] + up * (LineY + 0.005f), f = path.fwd[start], r = path.right[start];
        StreetTrackBuilder.MeshData white = batch["lineWhite"], black = batch["black"];
        int cols = Mathf.FloorToInt(RoadWidth);
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < cols; c++)
            {
                float x0 = -cols * 0.5f + c, x1 = x0 + 1f, z0 = (row - 1) * 0.7f, z1 = z0 + 0.7f;
                (((row + c) % 2 == 0) ? white : black).Quad(p + r * x0 + f * z0, p + r * x1 + f * z0, p + r * x1 + f * z1, p + r * x0 + f * z1, up);
            }

        // two rock pillars and a beam over the line
        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(f, up).normalized, up);
        float side = hw + ShoulderWidth + 1.5f, postH = 11f;
        StreetTrackBuilder.MeshData rock = batch["rock"], banner = batch["bannerRed"];
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 pp = path.pos[start] + rightFlat[start] * (s * side);
            pp.y = path.pos[start].y;
            rock.Box(pp, rot, new Vector3(0f, postH * 0.5f - 0.5f, 0f), new Vector3(2f, postH, 2f));
            AddBox("StartPost", pp, rot, new Vector3(0f, postH * 0.5f - 0.5f, 0f), new Vector3(2f, postH, 2f));
        }
        Vector3 center = path.pos[start];
        banner.Box(center, rot, new Vector3(0f, postH - 0.5f, 0f), new Vector3(side * 2f + 2f, 1.4f, 1.4f));
    }

    // ---------- props: rocks and cacti off the road ----------

    static void BuildProps()
    {
        int m = path.Count;
        Vector3 min = path.pos[0], max = path.pos[0];
        foreach (Vector3 p in path.pos) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        float keepOut = hw + ShoulderWidth + 12f;
        StreetTrackBuilder.MeshData rock = batch["rock"], rock2 = batch["rock2"], cactus = batch["cactus"];

        int rocks = 0, cacti = 0, tries = 0;
        while ((rocks < 140 || cacti < 70) && tries < 6000)
        {
            tries++;
            Vector3 p = new Vector3(R(min.x - 90f, max.x + 90f), 0f, R(min.z - 90f, max.z + 90f));
            float d = DistToRoad(p);
            if (d < keepOut || d > 110f) continue;
            if (rocks < 140 && (cacti >= 70 || rng.NextDouble() < 0.66))
            {
                float sx = R(1.5f, 6f), sy = R(1f, 4f), sz = R(1.5f, 6f);
                Quaternion rot = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
                (rng.NextDouble() < 0.5 ? rock : rock2).Box(p, rot, new Vector3(0f, sy * 0.3f, 0f), new Vector3(sx, sy, sz));
                AddBox("Rock", p, rot, new Vector3(0f, sy * 0.3f, 0f), new Vector3(sx, sy, sz));
                rocks++;
            }
            else
            {
                float h = R(3.5f, 7f), r = R(0.45f, 0.7f);
                cactus.Cylinder(p, r, h, 8);
                Quaternion rot = Quaternion.Euler(0f, R(0f, 360f), 0f);
                cactus.Box(p, rot, new Vector3(r + 0.9f, h * 0.55f, 0f), new Vector3(2f, 0.8f, 0.8f));
                cactus.Box(p, rot, new Vector3(r + 1.5f, h * 0.75f, 0f), new Vector3(0.8f, h * 0.4f, 0.8f));
                AddBox("Cactus", p, rot, new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h, r * 2f));
                cacti++;
            }
        }
    }

    static float DistToRoad(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < path.Count; i += 2)
        {
            Vector3 q = path.pos[i];
            float d = (new Vector2(p.x, p.z) - new Vector2(q.x, q.z)).sqrMagnitude;
            if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }

    static float R(float a, float b) { return Mathf.Lerp(a, b, (float)rng.NextDouble()); }

    // ---------- ground, walls, sky, car ----------

    static void BuildGroundAndBoundary()
    {
        Vector3 min = path.pos[0], max = path.pos[0];
        foreach (Vector3 p in path.pos) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        Vector3 center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
        float sx = max.x - min.x, sz = max.z - min.z;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "DesertGround";
        ground.transform.SetParent(root, false);
        ground.transform.localScale = new Vector3(sx + 1600f, 1f, sz + 1600f);
        ground.transform.localPosition = new Vector3(center.x, -0.5f, center.z);
        ground.GetComponent<Renderer>().sharedMaterial = StreetTrackBuilder.MakeMat("sand", StreetTrackBuilder.Palette["sand"]);

        float margin = 150f, t = 4f, hgt = 80f;
        float wx = sx + margin * 2f + t * 2f, wz = sz + margin * 2f + t * 2f;
        Vector3 e = new Vector3(sx * 0.5f + margin + t * 0.5f, 0f, sz * 0.5f + margin + t * 0.5f);
        Quaternion id = Quaternion.identity;
        AddBox("Boundary_N", new Vector3(center.x, hgt * 0.5f, center.z + e.z), id, Vector3.zero, new Vector3(wx, hgt, t));
        AddBox("Boundary_S", new Vector3(center.x, hgt * 0.5f, center.z - e.z), id, Vector3.zero, new Vector3(wx, hgt, t));
        AddBox("Boundary_E", new Vector3(center.x + e.x, hgt * 0.5f, center.z), id, Vector3.zero, new Vector3(t, hgt, wz));
        AddBox("Boundary_W", new Vector3(center.x - e.x, hgt * 0.5f, center.z), id, Vector3.zero, new Vector3(t, hgt, wz));
    }

    static void AddBox(string name, Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = localCenter;
        bc.size = size;
    }

    // late afternoon in the desert: low warm sun, orange haze
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
        sun.color = new Color(1f, 0.85f, 0.65f);
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(28f, 60f, 0f);
        RenderSettings.sun = sun;

        Shader sky = Shader.Find("Skybox/Procedural");
        if (sky != null)
        {
            Material skyMat = new Material(sky);
            skyMat.SetColor("_SkyTint", new Color(0.65f, 0.6f, 0.8f));
            skyMat.SetColor("_GroundColor", new Color(0.75f, 0.6f, 0.45f));
            skyMat.SetFloat("_Exposure", 1.25f);
            skyMat.SetFloat("_SunSize", 0.05f);
            skyMat.SetFloat("_AtmosphereThickness", 1.35f);
            RenderSettings.skybox = skyMat;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
    }

    static void PlaceCar(int spawn)
    {
        GameObject car = GameObject.Find("Car");
        if (car == null) return;
        if (car.GetComponent<SpeedDisplay>() == null) Undo.AddComponent<SpeedDisplay>(car);

        Vector3 pos = root.position + path.pos[spawn] + Vector3.up * 0.4f;
        Vector3 fwd = Vector3.ProjectOnPlane(path.fwd[spawn], Vector3.up).normalized;
        car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
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
            cam.farClipPlane = 2000f;
            cam.transform.position = pos - fwd * dist + Vector3.up * hgt;
            cam.transform.LookAt(pos + Vector3.up * 0.5f);
        }
        Selection.activeGameObject = car;
    }
}
