using System.Collections.Generic;
using UnityEngine;

// Helpers for StreetTrackBuilder: mesh building, color palette, batching.
public static partial class StreetTrackBuilder
{
    static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        {"asphalt", new Color(0.13f, 0.13f, 0.15f)},
        {"lineYellow", new Color(1f, 0.82f, 0.1f)},
        {"lineWhite", new Color(0.95f, 0.95f, 0.95f)},
        {"black", new Color(0.03f, 0.03f, 0.03f)},
        {"curb", new Color(0.58f, 0.58f, 0.6f)},
        {"sidewalk", new Color(0.82f, 0.8f, 0.76f)},
        {"grass", new Color(0.3f, 0.6f, 0.22f)},
        {"lawn", new Color(0.42f, 0.74f, 0.3f)},
        {"pathway", new Color(0.72f, 0.7f, 0.66f)},
        {"shortcut", new Color(0.62f, 0.55f, 0.42f)},
        {"hedge", new Color(0.12f, 0.42f, 0.14f)},
        {"ramp", new Color(1f, 0.8f, 0.1f)},
        {"fence", new Color(0.97f, 0.97f, 0.94f)},
        {"post", new Color(0.85f, 0.85f, 0.8f)},
        {"glass", new Color(0.6f, 0.8f, 0.95f)},
        {"frame", new Color(0.97f, 0.97f, 0.97f)},
        {"chimney", new Color(0.6f, 0.25f, 0.2f)},
        {"wall0", new Color(0.95f, 0.88f, 0.7f)},
        {"wall1", new Color(0.62f, 0.78f, 0.9f)},
        {"wall2", new Color(0.95f, 0.62f, 0.55f)},
        {"wall3", new Color(0.7f, 0.85f, 0.65f)},
        {"wall4", new Color(0.95f, 0.95f, 0.92f)},
        {"wall5", new Color(1f, 0.85f, 0.4f)},
        {"roof0", new Color(0.45f, 0.22f, 0.15f)},
        {"roof1", new Color(0.3f, 0.3f, 0.35f)},
        {"roof2", new Color(0.75f, 0.35f, 0.2f)},
        {"roof3", new Color(0.2f, 0.25f, 0.4f)},
        {"door0", new Color(0.7f, 0.15f, 0.15f)},
        {"door1", new Color(0.15f, 0.3f, 0.6f)},
        {"door2", new Color(0.35f, 0.22f, 0.12f)},
        {"trashGreen", new Color(0.15f, 0.5f, 0.25f)},
        {"trashGrey", new Color(0.45f, 0.47f, 0.5f)},
        {"trashLid", new Color(0.2f, 0.2f, 0.22f)},
        {"mailBlue", new Color(0.15f, 0.3f, 0.75f)},
        {"mailRed", new Color(0.85f, 0.1f, 0.1f)},
        {"cone", new Color(1f, 0.4f, 0.05f)},
        {"coneBase", new Color(0.15f, 0.15f, 0.15f)},
        {"tire", new Color(0.05f, 0.05f, 0.05f)},
        {"carGlass", new Color(0.15f, 0.22f, 0.3f)},
        {"carRed", new Color(0.85f, 0.12f, 0.12f)},
        {"carBlue", new Color(0.15f, 0.35f, 0.85f)},
        {"carYellow", new Color(0.95f, 0.8f, 0.15f)},
        {"carGreen", new Color(0.2f, 0.6f, 0.3f)},
        {"lightY", new Color(1f, 0.95f, 0.6f)},
        {"lightR", new Color(0.9f, 0.05f, 0.05f)},
        {"bannerRed", new Color(0.85f, 0.1f, 0.1f)},
    };

    static Material MakeMat(Color c)
    {
        Material m = RaceSceneSetup.MakeMaterial(c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    static float R(float min, float max)
    {
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    // ---------- mesh data ----------

    class MeshData
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<int> tris = new List<int>();

        // Winding is fixed automatically so the quad faces the direction we ask for.
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

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
            {
                Vector3 tmp = b; b = c; c = tmp;
            }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        // Box in a local frame: position = origin + rot * (localCenter +- size/2)
        public void Box(Vector3 origin, Quaternion rot, Vector3 localCenter, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float sz = (i & 4) == 0 ? -1f : 1f;
                c[i] = origin + rot * (localCenter + new Vector3(sx * h.x, sy * h.y, sz * h.z));
            }
            Quad(c[0], c[2], c[6], c[4], rot * Vector3.left);
            Quad(c[1], c[3], c[7], c[5], rot * Vector3.right);
            Quad(c[0], c[1], c[5], c[4], rot * Vector3.down);
            Quad(c[2], c[3], c[7], c[6], rot * Vector3.up);
            Quad(c[0], c[1], c[3], c[2], rot * Vector3.back);
            Quad(c[4], c[5], c[7], c[6], rot * Vector3.forward);
        }

        public void Cylinder(Vector3 basePos, float r, float h, int segs)
        {
            Vector3 top = Vector3.up * h;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 p0 = basePos + d0 * r, p1 = basePos + d1 * r;
                Quad(p0, p1, p1 + top, p0 + top, d0 + d1);
                Tri(basePos + top, p0 + top, p1 + top, Vector3.up);
            }
        }

        public void Cone(Vector3 basePos, float r, float h, int segs)
        {
            Vector3 apex = basePos + Vector3.up * h;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Tri(basePos + d0 * r, basePos + d1 * r, apex, (d0 + d1) * 0.5f * h + Vector3.up * r);
            }
        }

        // Gable roof, ridge along local x. Local +z is the front of the house.
        public void Roof(Vector3 origin, Quaternion rot, float w, float d, float h, float overhang, float rise)
        {
            float hx = w * 0.5f + overhang, hz = d * 0.5f + overhang;
            Vector3 a = origin + rot * new Vector3(-hx, h, hz);
            Vector3 b = origin + rot * new Vector3(hx, h, hz);
            Vector3 c = origin + rot * new Vector3(hx, h + rise, 0f);
            Vector3 e = origin + rot * new Vector3(-hx, h + rise, 0f);
            Quad(a, b, c, e, rot * new Vector3(0f, hz, rise));
            Vector3 a2 = origin + rot * new Vector3(-hx, h, -hz);
            Vector3 b2 = origin + rot * new Vector3(hx, h, -hz);
            Quad(a2, b2, c, e, rot * new Vector3(0f, hz, -rise));
            Tri(a, a2, e, rot * Vector3.left);
            Tri(b, b2, c, rot * Vector3.right);
            Quad(a, b, b2, a2, rot * Vector3.down);
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

    // One mesh per material key: keeps draw calls low.
    class Batch
    {
        readonly Dictionary<string, MeshData> data = new Dictionary<string, MeshData>();

        public MeshData this[string key]
        {
            get
            {
                MeshData d;
                if (!data.TryGetValue(key, out d)) { d = new MeshData(); data[key] = d; }
                return d;
            }
        }

        public void Emit(Transform parent)
        {
            foreach (KeyValuePair<string, MeshData> kv in data)
            {
                if (kv.Value.verts.Count == 0) continue;
                Color c;
                if (!Palette.TryGetValue(kv.Key, out c)) c = Color.magenta;
                MakeMeshObject("Batch_" + kv.Key, parent, kv.Value, MakeMat(c), false);
            }
        }
    }

    static GameObject MakeMeshObject(string name, Transform parent, MeshData data, Material mat, bool collider)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Mesh mesh = data.ToMesh(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // Invisible object that only has a mesh collider.
    static void MakeColliderObject(string name, Transform parent, MeshData data)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshCollider>().sharedMesh = data.ToMesh(name);
    }

    // Invisible box collider (position is local to the StreetTrack object).
    static void AddBoxCollider(string name, Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = localCenter;
        bc.size = size;
    }

    // ---------- 2D oriented boxes (for placing houses without overlaps) ----------

    struct Obb
    {
        public Vector2 c, u, v;
        public float hu, hv;
    }

    static Obb MakeObb(Vector3 center, Vector3 front, float hu, float hv)
    {
        Vector3 r = Vector3.Cross(Vector3.up, front);
        Obb o = new Obb();
        o.c = new Vector2(center.x, center.z);
        o.u = new Vector2(r.x, r.z);
        o.v = new Vector2(front.x, front.z);
        o.hu = hu;
        o.hv = hv;
        return o;
    }

    static bool Overlap(Obb a, Obb b)
    {
        Vector2[] axes = { a.u, a.v, b.u, b.v };
        Vector2 diff = b.c - a.c;
        for (int i = 0; i < 4; i++)
        {
            Vector2 ax = axes[i];
            float ra = a.hu * Mathf.Abs(Vector2.Dot(ax, a.u)) + a.hv * Mathf.Abs(Vector2.Dot(ax, a.v));
            float rb = b.hu * Mathf.Abs(Vector2.Dot(ax, b.u)) + b.hv * Mathf.Abs(Vector2.Dot(ax, b.v));
            if (Mathf.Abs(Vector2.Dot(ax, diff)) > ra + rb) return false;
        }
        return true;
    }
}
