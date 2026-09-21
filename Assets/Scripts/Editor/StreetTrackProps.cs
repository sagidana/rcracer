using UnityEngine;

// Huge props: trash cans, mailboxes, parked cars, traffic cones.
public static partial class StreetTrackBuilder
{
    // Small props are separate objects (so they can fly away). Decoration without a collider stays in the merged meshes.
    static void TrashCan(Vector3 basePos, string key, bool collider)
    {
        if (collider)
        {
            SpawnProp("TrashCan", basePos, Quaternion.identity, 3f, new Vector3(0f, 2.2f, 0f), new Vector3(2.6f, 4.4f, 2.6f),
                new PropPart(key, m => m.Cylinder(Vector3.zero, 1.3f, 4f, 14)),
                new PropPart("trashLid", m => m.Cylinder(new Vector3(0f, 4f, 0f), 1.45f, 0.4f, 14)));
            return;
        }
        batch[key].Cylinder(basePos, 1.3f, 4f, 14);
        batch["trashLid"].Cylinder(basePos + Vector3.up * 4f, 1.45f, 0.4f, 14);
    }

    static void Mailbox(Vector3 basePos, Quaternion rot, bool collider)
    {
        if (collider)
        {
            SpawnProp("Mailbox", basePos, rot, 4f, new Vector3(0f, 2.2f, 0f), new Vector3(1.6f, 4.4f, 2.7f),
                new PropPart("post", m => m.Box(Vector3.zero, Quaternion.identity, new Vector3(0f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 0.4f))),
                new PropPart("mailBlue", m => m.Box(Vector3.zero, Quaternion.identity, new Vector3(0f, 3.8f, 0f), new Vector3(1.5f, 1.3f, 2.6f))),
                new PropPart("mailRed", m => m.Box(Vector3.zero, Quaternion.identity, new Vector3(0.9f, 4.2f, 0.4f), new Vector3(0.12f, 1f, 0.5f))));
            return;
        }
        batch["post"].Box(basePos, rot, new Vector3(0f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 0.4f));
        batch["mailBlue"].Box(basePos, rot, new Vector3(0f, 3.8f, 0f), new Vector3(1.5f, 1.3f, 2.6f));
        batch["mailRed"].Box(basePos, rot, new Vector3(0.9f, 4.2f, 0.4f), new Vector3(0.12f, 1f, 0.5f));
    }

    static void Cone(Vector3 basePos)
    {
        SpawnProp("Cone", basePos, Quaternion.identity, 0.6f, new Vector3(0f, 1.3f, 0f), new Vector3(1.6f, 2.6f, 1.6f),
            new PropPart("coneBase", m => m.Box(Vector3.zero, Quaternion.identity, new Vector3(0f, 0.1f, 0f), new Vector3(2f, 0.2f, 2f))),
            new PropPart("cone", m => m.Cone(new Vector3(0f, 0.2f, 0f), 0.85f, 2.6f, 10)));
    }

    static void ParkedCar(Vector3 center, Quaternion rot)
    {
        string[] colors = { "carRed", "carBlue", "carYellow", "carGreen" };
        string body = colors[rng.Next(colors.Length)];
        Vector3 o = center + Vector3.up * RoadY;
        batch[body].Box(o, rot, new Vector3(0f, 1.7f, 0f), new Vector3(6f, 2f, 14f));
        batch[body].Box(o, rot, new Vector3(0f, 3.55f, -1f), new Vector3(5.2f, 1.7f, 7f));
        batch["carGlass"].Box(o, rot, new Vector3(0f, 3.7f, -1f), new Vector3(5.35f, 0.9f, 6.4f));
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
                batch["tire"].Box(o, rot, new Vector3(sx * 2.8f, 0.9f, sz * 4.2f), new Vector3(0.9f, 1.8f, 1.8f));
            batch["lightY"].Box(o, rot, new Vector3(sx * 2.1f, 1.9f, 7.02f), new Vector3(1.2f, 0.7f, 0.1f));
            batch["lightR"].Box(o, rot, new Vector3(sx * 2.1f, 1.9f, -7.02f), new Vector3(1.2f, 0.7f, 0.1f));
        }
        AddBoxCollider("ParkedCar", o, rot, new Vector3(0f, 2.2f, 0f), new Vector3(6f, 4.4f, 14f));
    }

    // Puts parked cars, pairs of trash cans and cone lines along straight parts of the road edge.
    static void BuildRoadProps(bool[] cross, bool[][] gap, int spawn)
    {
        int m = main.Count;
        int i = 12;
        while (i < m - 12)
        {
            bool ok = SuitableForProp(i, cross, gap, spawn);
            if (!ok) { i++; continue; }

            float s = rng.Next(0, 2) == 0 ? -1f : 1f;
            Vector3 p = main.pos[i], f = main.fwd[i], r = main.right[i] * s;
            Quaternion rot = Quaternion.LookRotation(f, Vector3.up);
            double kind = rng.NextDouble();
            if (kind < 0.35)
            {
                ParkedCar(p + r * (hw - 3.5f), rot);
            }
            else if (kind < 0.65)
            {
                TrashCan(p + r * (hw - 1.9f) + f * 2.4f + Vector3.up * RoadY, rng.NextDouble() < 0.5 ? "trashGreen" : "trashGrey", true);
                TrashCan(p + r * (hw - 1.9f) - f * 2.4f + Vector3.up * RoadY, rng.NextDouble() < 0.5 ? "trashGreen" : "trashGrey", true);
            }
            else
            {
                for (int q = 0; q < 4; q++)
                    Cone(p + r * (hw - 1f - q * 0.7f) + f * (q * 3f - 4.5f) + Vector3.up * RoadY);
            }
            i += Mathf.CeilToInt(R(30f, 60f) / SampleStep);
        }
    }

    static bool SuitableForProp(int i, bool[] cross, bool[][] gap, int spawn)
    {
        int m = main.Count;
        for (int k = -10; k <= 10; k++)
        {
            int j = (i + k + m) % m;
            if (cross[j] || gap[0][j] || gap[1][j]) return false;
        }
        int fromStart = Mathf.Min(i, m - i);
        if (fromStart < 25) return false;
        if (Mathf.Abs(i - spawn) < 25) return false;
        Vector3 a = main.fwd[(i - 8 + m) % m], b = main.fwd[(i + 8) % m];
        if (Vector3.Angle(a, b) > 3f) return false;
        if (DistToMouth(main.pos[i]) < 30f) return false;
        return true;
    }
}
