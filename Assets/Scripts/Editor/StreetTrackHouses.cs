using UnityEngine;

// Giant houses with roofs, doors, windows, lawns and fences.
public static partial class StreetTrackBuilder
{
    // true if the point is far enough from every road (so a house never blocks or overlaps a road)
    static bool FarFromRoads(Vector3 pt, float margin)
    {
        float minMain = hw + SidewalkWidth + margin;
        float minCut = ShortcutWidth * 0.5f + 4f;
        float a = minMain * minMain, b = minCut * minCut;
        for (int i = 0; i < main.pos.Length; i++)
        {
            Vector3 d = main.pos[i] - pt; d.y = 0f;
            if (d.sqrMagnitude < a) return false;
        }
        for (int i = 0; i < cut.pos.Length; i++)
        {
            Vector3 d = cut.pos[i] - pt; d.y = 0f;
            if (d.sqrMagnitude < b) return false;
        }
        return true;
    }

    static void BuildHouses()
    {
        int m = main.Count;
        float sw = SidewalkWidth;

        // row 1: tidy rows right next to the road, both sides
        for (int side = -1; side <= 1; side += 2)
        {
            int i = rng.Next(0, 6);
            while (i < m)
            {
                float w = R(22f, 36f), d = R(18f, 26f);
                int floors = rng.NextDouble() < 0.55 ? 1 : 2;
                float yard = 7f;
                Vector3 outN = main.right[i] * side;
                Vector3 c = main.pos[i] + outN * (hw + sw + yard + d * 0.5f);
                bool ok = TryHouse(c, -outN, w, d, floors, yard, true);
                i += ok ? Mathf.CeilToInt((w + R(3f, 7f)) / SampleStep) : 3;
            }
        }

        // row 2: more houses further back, filling the block (no fences)
        for (int attempt = 0; attempt < 600; attempt++)
        {
            int i = rng.Next(0, m);
            int side = rng.Next(0, 2) == 0 ? -1 : 1;
            float w = R(22f, 36f), d = R(18f, 26f);
            int floors = rng.NextDouble() < 0.5 ? 1 : 2;
            Vector3 outN = main.right[i] * side;
            Vector3 c = main.pos[i] + outN * (hw + sw + 7f + d * 0.5f + R(20f, 75f));
            TryHouse(c, -outN, w, d, floors, 7f, false);
        }
    }

    static bool TryHouse(Vector3 c, Vector3 front, float w, float d, int floors, float yard, bool fenced)
    {
        front.y = 0f;
        front.Normalize();
        Vector3 rt = Vector3.Cross(Vector3.up, front);
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
                if (!FarFromRoads(c + rt * (ix * w * 0.5f) + front * (iz * d * 0.5f), 2f)) return false;
        if (fenced)
        {
            float fz = d * 0.5f + yard - 1.5f;
            if (!FarFromRoads(c + rt * (w * 0.5f + 3f) + front * fz, 0.5f)) return false;
            if (!FarFromRoads(c - rt * (w * 0.5f + 3f) + front * fz, 0.5f)) return false;
        }

        Obb box = MakeObb(c, front, w * 0.5f + 3.5f, d * 0.5f + 3.5f);
        foreach (Obb o in occupied)
            if (Overlap(box, o)) return false;
        occupied.Add(box);

        BuildHouse(c, front, w, d, floors, yard, fenced);
        return true;
    }

    static void BuildHouse(Vector3 o, Vector3 front, float w, float d, int floors, float yard, bool fenced)
    {
        Quaternion rot = Quaternion.LookRotation(front, Vector3.up);
        float h = floors * 7f;
        int wall = rng.Next(6), roof = rng.Next(4), door = rng.Next(3);

        batch["wall" + wall].Box(o, rot, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        batch["roof" + roof].Roof(o, rot, w, d, h, 1.4f, 5.5f);
        batch["door" + door].Box(o, rot, new Vector3(0f, 2.6f, d * 0.5f + 0.1f), new Vector3(3.4f, 5.2f, 0.4f));
        batch["chimney"].Box(o, rot, new Vector3(w * 0.25f, h + 3.6f, -d * 0.2f), new Vector3(2.4f, 5f, 2.4f));
        AddBoxCollider("House", o, rot, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));

        // windows: front and both sides
        for (int f = 0; f < floors; f++)
        {
            float y = f * 7f + 4.2f;
            float[] xs = f == 0 ? new[] { -w * 0.27f, w * 0.27f } : new[] { -w * 0.3f, 0f, w * 0.3f };
            foreach (float x in xs)
            {
                batch["frame"].Box(o, rot, new Vector3(x, y, d * 0.5f + 0.05f), new Vector3(4f, 3.8f, 0.3f));
                batch["glass"].Box(o, rot, new Vector3(x, y, d * 0.5f + 0.08f), new Vector3(3.4f, 3.2f, 0.45f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                batch["frame"].Box(o, rot, new Vector3(s * (w * 0.5f + 0.05f), y, 0f), new Vector3(0.3f, 3.8f, 4f));
                batch["glass"].Box(o, rot, new Vector3(s * (w * 0.5f + 0.08f), y, 0f), new Vector3(0.45f, 3.2f, 3.4f));
            }
        }

        // grass yard and the path to the door
        float zFront = d * 0.5f + yard;
        MeshData lawn = batch["lawn"], path = batch["pathway"];
        lawn.Quad(o + rot * new Vector3(-w * 0.5f - 3f, 0.03f, -d * 0.5f - 3f), o + rot * new Vector3(w * 0.5f + 3f, 0.03f, -d * 0.5f - 3f),
                  o + rot * new Vector3(w * 0.5f + 3f, 0.03f, zFront), o + rot * new Vector3(-w * 0.5f - 3f, 0.03f, zFront), Vector3.up);
        path.Quad(o + rot * new Vector3(-1.6f, 0.05f, d * 0.5f), o + rot * new Vector3(1.6f, 0.05f, d * 0.5f),
                  o + rot * new Vector3(1.6f, 0.05f, zFront), o + rot * new Vector3(-1.6f, 0.05f, zFront), Vector3.up);

        if (!fenced) return;

        // fence: two front pieces (gate gap in the middle) and two side pieces
        float fz = zFront - 1.5f, fx = w * 0.5f + 3f;
        Fence(o, rot, new Vector3(-fx, 0f, fz), new Vector3(-2.5f, 0f, fz));
        Fence(o, rot, new Vector3(2.5f, 0f, fz), new Vector3(fx, 0f, fz));
        Fence(o, rot, new Vector3(-fx, 0f, -d * 0.5f - 3f), new Vector3(-fx, 0f, fz));
        Fence(o, rot, new Vector3(fx, 0f, -d * 0.5f - 3f), new Vector3(fx, 0f, fz));

        // little extras in the front yard
        if (rng.NextDouble() < 0.6)
            Mailbox(o + rot * new Vector3(4.5f, 0f, zFront - 0.8f), rot, true);
        if (rng.NextDouble() < 0.5)
            TrashCan(o + rot * new Vector3(-w * 0.5f + 2.5f, 0f, d * 0.5f + 3f), rng.NextDouble() < 0.5 ? "trashGreen" : "trashGrey", false);
    }

    // a fence piece between two local points (aligned with the house axes)
    static void Fence(Vector3 o, Quaternion rot, Vector3 a, Vector3 b)
    {
        Vector3 center = (a + b) * 0.5f + Vector3.up * 1.3f;
        bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
        float len = Vector3.Distance(a, b);
        Vector3 size = alongX ? new Vector3(len, 2.6f, 0.35f) : new Vector3(0.35f, 2.6f, len);
        batch["fence"].Box(o, rot, center, size);
        int posts = Mathf.CeilToInt(len / 4f) + 1;
        for (int i = 0; i < posts; i++)
        {
            Vector3 pp = Vector3.Lerp(a, b, posts > 1 ? i / (float)(posts - 1) : 0f) + Vector3.up * 1.55f;
            batch["post"].Box(o, rot, pp, new Vector3(0.7f, 3.1f, 0.7f));
        }
        AddBoxCollider("Fence", o, rot, center, size);
    }
}
