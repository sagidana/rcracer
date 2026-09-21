using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Build Test Track : builds a closed test track, and puts the car on the start line.
// Running it again deletes the old track ("TestTrack" object) and builds a fresh one.
public static class TestTrackBuilder
{
    const string RootName = "TestTrack";

    const float RoadWidth = 10f;
    const float SampleStep = 1.5f;     // distance between track samples (meters)
    const float RoadY = 0.03f;
    const float CurbY = 0.05f;
    const float BarY = 0.04f;
    const float WallHeight = 1.2f;
    const float WallThickness = 0.6f;
    const float CurbWidth = 0.8f;

    // Middle line of the road (x, z). The track is a smooth curve through these points.
    // Straight parts, gentle curves, tight curves and one hairpin (the U-turn near x=120, z=-10).
    static readonly Vector2[] ControlPoints =
    {
        new Vector2(0, 0), new Vector2(0, 45), new Vector2(0, 85), new Vector2(12, 112),
        new Vector2(40, 128), new Vector2(85, 130), new Vector2(118, 124), new Vector2(135, 100),
        new Vector2(137, 60), new Vector2(135, 35), new Vector2(134, 15), new Vector2(134, 0),
        new Vector2(134, -10), new Vector2(132.3f, -16.5f), new Vector2(127.5f, -21.3f), new Vector2(121, -23),
        new Vector2(114.5f, -21.3f), new Vector2(109.7f, -16.5f), new Vector2(108, -10), new Vector2(108, 0),
        new Vector2(108, 15), new Vector2(106, 32), new Vector2(102, 48), new Vector2(85, 58),
        new Vector2(60, 62), new Vector2(42, 45), new Vector2(38, 10), new Vector2(40, -32),
        new Vector2(34, -46), new Vector2(20, -52), new Vector2(6, -46), new Vector2(0, -32),
    };

    [MenuItem("Tools/Build Test Track")]
    public static void Build()
    {
        // the track needs the ground and the car from "Setup Race Scene"
        if (GameObject.Find("Ground") == null || GameObject.Find("Car") == null)
            RaceSceneSetup.Build();

        // remove the previous track (no duplicates)
        GameObject old = GameObject.Find(RootName);
        while (old != null)
        {
            Undo.DestroyObjectImmediate(old);
            old = GameObject.Find(RootName);
        }

        GameObject ground = GameObject.Find("Ground");
        if (ground != null)
            SetColor(ground.GetComponent<Renderer>().sharedMaterial, new Color(0.25f, 0.55f, 0.22f)); // grass

        BuildCenterline(out Vector3[] pos, out Vector3[] fwd, out Vector3[] right);
        int m = pos.Length;
        float hw = RoadWidth * 0.5f;

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Test Track");

        Material roadMat = RaceSceneSetup.MakeMaterial(new Color(0.16f, 0.16f, 0.18f));
        Material barMat = RaceSceneSetup.MakeMaterial(new Color(0.24f, 0.24f, 0.27f));
        Material whiteMat = RaceSceneSetup.MakeMaterial(new Color(0.95f, 0.95f, 0.95f));
        Material redMat = RaceSceneSetup.MakeMaterial(new Color(0.9f, 0.1f, 0.1f));
        Material blackMat = RaceSceneSetup.MakeMaterial(new Color(0.03f, 0.03f, 0.03f));
        Material wallMatA = RaceSceneSetup.MakeMaterial(new Color(1f, 0.55f, 0.1f));
        Material wallMatB = RaceSceneSetup.MakeMaterial(new Color(0.15f, 0.3f, 0.75f));
        Material rampMat = RaceSceneSetup.MakeMaterial(new Color(1f, 0.85f, 0.1f));

        MeshData road = new MeshData(), bars = new MeshData(), white = new MeshData();
        MeshData red = new MeshData(), black = new MeshData();
        MeshData wallA = new MeshData(), wallB = new MeshData();

        Vector3 up = Vector3.up;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            Vector3 pi = pos[i], pj = pos[j];
            Vector3 ri = right[i], rj = right[j];

            // road surface
            road.Quad(pi - ri * hw + up * RoadY, pi + ri * hw + up * RoadY,
                      pj + rj * hw + up * RoadY, pj - rj * hw + up * RoadY, up);

            // red / white curbs on both edges
            MeshData curb = ((i / 3) % 2 == 0) ? red : white;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 ei = pi + ri * (s * hw) + up * CurbY;
                Vector3 ej = pj + rj * (s * hw) + up * CurbY;
                Vector3 ii = ei - ri * (s * CurbWidth);
                Vector3 ij = ej - rj * (s * CurbWidth);
                curb.Quad(ei, ii, ij, ej, up);
            }

            // dashed white line in the middle
            if ((i / 2) % 2 == 0)
            {
                white.Quad(pi - ri * 0.15f + up * CurbY, pi + ri * 0.15f + up * CurbY,
                           pj + rj * 0.15f + up * CurbY, pj - rj * 0.15f + up * CurbY, up);
            }

            // lighter bars across the road every ~10 m, easy to see when going fast
            if (i % 7 == 3)
            {
                float inner = hw - CurbWidth;
                Vector3 f = fwd[i] * 0.25f;
                Vector3 l = pi - ri * inner + up * BarY;
                Vector3 r = pi + ri * inner + up * BarY;
                bars.Quad(l - f, r - f, r + f, l + f, up);
            }

            // walls (alternating colors), both sides
            MeshData wall = ((i / 6) % 2 == 0) ? wallA : wallB;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 outI = ri * s, outJ = rj * s;
                Vector3 ei = pi + outI * hw, ej = pj + outJ * hw;
                Vector3 h = up * WallHeight;
                // inner face (toward the road)
                wall.Quad(ei, ej, ej + h, ei + h, -outI);
                // top
                wall.Quad(ei + h, ej + h, ej + h + outJ * WallThickness, ei + h + outI * WallThickness, up);
                // outer face
                wall.Quad(ei + outI * WallThickness, ej + outJ * WallThickness,
                          ej + outJ * WallThickness + h, ei + outI * WallThickness + h, outI);
            }
        }

        // start line: black and white checker across the road at sample 0
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < (int)RoadWidth; col++)
            {
                float x0 = -hw + col, x1 = x0 + 1f;
                float z0 = (row - 1) * 0.6f, z1 = z0 + 0.6f;
                Vector3 a = pos[0] + right[0] * x0 + fwd[0] * z0 + up * (CurbY + 0.005f);
                Vector3 b = pos[0] + right[0] * x1 + fwd[0] * z0 + up * (CurbY + 0.005f);
                Vector3 c = pos[0] + right[0] * x1 + fwd[0] * z1 + up * (CurbY + 0.005f);
                Vector3 d = pos[0] + right[0] * x0 + fwd[0] * z1 + up * (CurbY + 0.005f);
                (((row + col) % 2 == 0) ? white : black).Quad(a, b, c, d, up);
            }
        }

        MakeMeshObject("Road", root.transform, road, roadMat, false);
        MakeMeshObject("RoadBars", root.transform, bars, barMat, false);
        MakeMeshObject("WhiteMarks", root.transform, white, whiteMat, false);
        MakeMeshObject("RedCurbs", root.transform, red, redMat, false);
        MakeMeshObject("StartLineBlack", root.transform, black, blackMat, false);
        MakeMeshObject("Wall_A", root.transform, wallA, wallMatA, true);
        MakeMeshObject("Wall_B", root.transform, wallB, wallMatB, true);

        // ramps (yellow) and bumps (across the road) - all placed on straight parts
        MakeRamp(root.transform, pos[20], fwd[20], rampMat);
        MakeRamp(root.transform, pos[222], fwd[222], rampMat);
        foreach (int i in new[] { 115, 120, 125, 392, 397 })
            MakeBump(root.transform, pos[i], fwd[i], rampMat);

        // start gantry
        BuildGantry(root.transform, pos[0], fwd[0], right[0], redMat, whiteMat);

        // put the car on the road, a few meters before the start line, facing along the track
        int spawn = (m - 5) % m;
        GameObject car = GameObject.Find("Car");
        if (car != null)
        {
            if (car.GetComponent<SpeedDisplay>() == null) Undo.AddComponent<SpeedDisplay>(car);
            car.transform.SetPositionAndRotation(pos[spawn] + up * 0.35f, Quaternion.LookRotation(fwd[spawn], up));
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
                cam.transform.position = car.transform.position - fwd[spawn] * dist + up * hgt;
                cam.transform.LookAt(car.transform.position + up * 0.5f);
            }
            Selection.activeGameObject = car;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Test track ready (" + m + " samples). Press Play. Arrows/WASD = drive, Space = handbrake/drift, R = reset.");
    }

    // ---------- centerline ----------

    static void BuildCenterline(out Vector3[] pos, out Vector3[] fwd, out Vector3[] right)
    {
        int n = ControlPoints.Length;
        var dense = new List<Vector3>();
        const int perSegment = 40;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = ToV3(ControlPoints[(i - 1 + n) % n]);
            Vector3 p1 = ToV3(ControlPoints[i]);
            Vector3 p2 = ToV3(ControlPoints[(i + 1) % n]);
            Vector3 p3 = ToV3(ControlPoints[(i + 2) % n]);
            for (int s = 0; s < perSegment; s++)
                dense.Add(CatmullRom(p0, p1, p2, p3, s / (float)perSegment));
        }
        dense.Add(dense[0]);

        var cum = new float[dense.Count];
        for (int i = 1; i < dense.Count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
        float total = cum[cum.Length - 1];

        int m = Mathf.RoundToInt(total / SampleStep);
        float step = total / m;
        pos = new Vector3[m];
        int k = 0;
        for (int i = 0; i < m; i++)
        {
            float d = i * step;
            while (cum[k + 1] < d) k++;
            float f = (d - cum[k]) / (cum[k + 1] - cum[k]);
            pos[i] = Vector3.Lerp(dense[k], dense[k + 1], f);
        }

        fwd = new Vector3[m];
        right = new Vector3[m];
        for (int i = 0; i < m; i++)
        {
            fwd[i] = (pos[(i + 1) % m] - pos[(i - 1 + m) % m]).normalized;
            right[i] = Vector3.Cross(Vector3.up, fwd[i]).normalized;
        }
    }

    static Vector3 ToV3(Vector2 p) { return new Vector3(p.x, 0f, p.y); }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // ---------- helpers ----------

    // Collects quads and turns them into one mesh. Winding is fixed automatically
    // so every quad faces the direction we ask for.
    class MeshData
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<int> tris = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
            {
                Vector3 tmp = b; b = d; d = tmp;
            }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    static void MakeMeshObject(string name, Transform parent, MeshData data, Material mat, bool collider)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Mesh mesh = data.ToMesh(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }

    static void MakeRamp(Transform parent, Vector3 p, Vector3 fwd, Material mat)
    {
        const float length = 8f, thickness = 0.6f, angle = 10f;
        float rad = angle * Mathf.Deg2Rad;
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Ramp";
        ramp.transform.SetParent(parent, false);
        ramp.transform.localScale = new Vector3(RoadWidth - 1.5f, thickness, length);
        // the low end sits exactly at ground level, the high end is about 1.4 m up
        float y = length * 0.5f * Mathf.Sin(rad) - thickness * 0.5f * Mathf.Cos(rad);
        ramp.transform.position = new Vector3(p.x, y, p.z);
        ramp.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-angle, 0f, 0f);
        ramp.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void MakeBump(Transform parent, Vector3 p, Vector3 fwd, Material mat)
    {
        const float diameter = 1.2f, rise = 0.25f;
        GameObject bump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bump.name = "Bump";
        bump.transform.SetParent(parent, false);
        // a cylinder lying across the road, mostly buried: only the top 0.25 m sticks out
        bump.transform.localScale = new Vector3(diameter, (RoadWidth + 2f) * 0.5f, diameter);
        bump.transform.position = new Vector3(p.x, rise - diameter * 0.5f, p.z);
        bump.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
        bump.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void BuildGantry(Transform parent, Vector3 p, Vector3 fwd, Vector3 right, Material postMat, Material barMat)
    {
        float side = RoadWidth * 0.5f + WallThickness + 0.8f;
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "StartPost";
            post.transform.SetParent(parent, false);
            post.transform.localScale = new Vector3(0.5f, 2.6f, 0.5f);
            post.transform.position = p + right * (s * side) + Vector3.up * 2.6f;
            post.GetComponent<Renderer>().sharedMaterial = postMat;
        }
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "StartBanner";
        bar.transform.SetParent(parent, false);
        bar.transform.localScale = new Vector3(side * 2f + 0.5f, 0.9f, 0.4f);
        bar.transform.position = p + Vector3.up * 5.2f;
        bar.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        bar.GetComponent<Renderer>().sharedMaterial = barMat;
        Object.DestroyImmediate(bar.GetComponent<Collider>()); // too high to reach
    }
}
