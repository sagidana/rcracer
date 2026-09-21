using System.Collections.Generic;
using UnityEngine;

// Road surface, lines, curbs, sidewalks, crosswalks, start line and the shortcut.
public static partial class StreetTrackBuilder
{
    const float RoadY = 0.05f;
    const float LineY = 0.07f;
    const float XwalkY = 0.075f;

    static void AddBoth(MeshData vis, MeshData col, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
    {
        vis.Quad(a, b, c, d, facing);
        col.Quad(a, b, c, d, facing);
    }

    static bool NearMouth(Vector3 p)
    {
        float r2 = (ShortcutWidth * 0.5f + 0.8f) * (ShortcutWidth * 0.5f + 0.8f);
        for (int i = 0; i < mouth.Length; i++)
            if ((mouth[i] - p).sqrMagnitude < r2) return true;
        return false;
    }

    static float DistToMouth(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < mouth.Length; i++)
            best = Mathf.Min(best, (mouth[i] - p).magnitude);
        return best;
    }

    // ---------- asphalt + lines ----------
    static void BuildRoad(bool[] cross, bool[][] gap)
    {
        int m = main.Count;
        Vector3 up = Vector3.up;
        MeshData asphalt = batch["asphalt"], yellow = batch["lineYellow"], white = batch["lineWhite"];
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            Vector3 pi = main.pos[i], pj = main.pos[j], ri = main.right[i], rj = main.right[j];

            asphalt.Quad(pi - ri * hw + up * RoadY, pi + ri * hw + up * RoadY,
                         pj + rj * hw + up * RoadY, pj - rj * hw + up * RoadY, up);

            // dashed center line
            if ((i / 2) % 2 == 0 && !cross[i] && !cross[j])
                yellow.Quad(pi - ri * 0.18f + up * LineY, pi + ri * 0.18f + up * LineY,
                            pj + rj * 0.18f + up * LineY, pj - rj * 0.18f + up * LineY, up);

            // solid edge lines
            for (int si = 0; si < 2; si++)
            {
                if (gap[si][i] || gap[si][j]) continue;
                float s = si == 0 ? -1f : 1f;
                white.Quad(pi + ri * (s * (hw - 0.9f)) + up * LineY, pi + ri * (s * (hw - 0.5f)) + up * LineY,
                           pj + rj * (s * (hw - 0.5f)) + up * LineY, pj + rj * (s * (hw - 0.9f)) + up * LineY, up);
            }
        }
    }

    // ---------- tall curbs + sidewalks (solid) ----------
    static void BuildCurbs(bool[][] gap)
    {
        int m = main.Count;
        float ch = CurbHeight, sw = SidewalkWidth;
        Vector3 h = Vector3.up * ch;
        MeshData curbFace = batch["curb"], sidewalk = batch["sidewalk"], col = new MeshData();

        for (int si = 0; si < 2; si++)
        {
            float s = si == 0 ? -1f : 1f;
            bool[] on = new bool[m];
            for (int i = 0; i < m; i++) on[i] = !(gap[si][i] || gap[si][(i + 1) % m]);

            for (int i = 0; i < m; i++)
            {
                if (!on[i]) continue;
                int j = (i + 1) % m;
                Vector3 n = main.right[i] * s;
                Vector3 a0 = main.pos[i] + main.right[i] * (s * hw);
                Vector3 a1 = main.pos[j] + main.right[j] * (s * hw);
                Vector3 b0 = main.pos[i] + main.right[i] * (s * (hw + sw));
                Vector3 b1 = main.pos[j] + main.right[j] * (s * (hw + sw));

                AddBoth(curbFace, col, a0, a1, a1 + h, a0 + h, -n);          // face toward the road
                AddBoth(sidewalk, col, a0 + h, a1 + h, b1 + h, b0 + h, Vector3.up); // sidewalk top
                AddBoth(curbFace, col, b0 + h, b1 + h, b1, b0, n);           // back face

                // end caps where the curb stops (intersections, shortcut openings)
                if (!on[(i - 1 + m) % m])
                    AddBoth(curbFace, col, a0, a0 + h, b0 + h, b0, -main.fwd[i]);
                if (!on[j])
                    AddBoth(curbFace, col, a1, a1 + h, b1 + h, b1, main.fwd[j]);
            }
        }
        MakeColliderObject("CurbCollider", root, col);
    }

    // ---------- crosswalks at every intersection ----------
    static void BuildCrosswalks(float[] otherD)
    {
        int m = main.Count;
        List<int> centers = new List<int>();
        for (int i = 0; i < m; i++)
        {
            float d = otherD[i];
            if (d > 2.2f) continue;
            if (d > otherD[(i - 1 + m) % m] || d > otherD[(i + 1) % m]) continue;
            bool dup = false;
            foreach (int c in centers)
            {
                int gap = Mathf.Abs(c - i);
                if (Mathf.Min(gap, m - gap) < 6) dup = true;
            }
            if (!dup) centers.Add(i);
        }

        int off = Mathf.RoundToInt((hw + 3.5f) / SampleStep);
        foreach (int c in centers)
        {
            Crosswalk((c + off) % m);
            Crosswalk((c - off + m) % m);
        }
    }

    static void Crosswalk(int k)
    {
        MeshData white = batch["lineWhite"];
        Vector3 p = main.pos[k], f = main.fwd[k], r = main.right[k];
        int stripes = Mathf.FloorToInt((RoadWidth - 1.4f) / 1.6f);
        float start = -(stripes - 1) * 0.8f;
        for (int q = 0; q < stripes; q++)
        {
            Vector3 c = p + r * (start + q * 1.6f) + Vector3.up * XwalkY;
            white.Quad(c - r * 0.4f - f * 2f, c + r * 0.4f - f * 2f, c + r * 0.4f + f * 2f, c - r * 0.4f + f * 2f, Vector3.up);
        }
    }

    // ---------- start / finish line, grid marks and the banner ----------
    static void BuildStartLine(int spawn)
    {
        Vector3 p = main.pos[0], f = main.fwd[0], r = main.right[0];
        Vector3 up = Vector3.up;
        MeshData white = batch["lineWhite"], black = batch["black"];

        // checker line across the road
        int cols = Mathf.FloorToInt(RoadWidth);
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                float x0 = -cols * 0.5f + col, x1 = x0 + 1f;
                float z0 = (row - 1) * 0.7f, z1 = z0 + 0.7f;
                float y = XwalkY + 0.005f;
                Vector3 a = p + r * x0 + f * z0 + up * y, b = p + r * x1 + f * z0 + up * y;
                Vector3 c = p + r * x1 + f * z1 + up * y, d = p + r * x0 + f * z1 + up * y;
                (((row + col) % 2 == 0) ? white : black).Quad(a, b, c, d, up);
            }
        }

        // grid slot marks where the car starts
        Vector3 sp = main.pos[spawn], sf = main.fwd[spawn], sr = main.right[spawn];
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 c = sp + sr * (side * 1.6f) + up * LineY;
            white.Quad(c - sr * 0.12f - sf * 2.2f, c + sr * 0.12f - sf * 2.2f, c + sr * 0.12f + sf * 2.2f, c - sr * 0.12f + sf * 2.2f, up);
        }
        Vector3 cb = sp - sf * 2.2f + up * LineY;
        white.Quad(cb - sr * 1.6f - sf * 0.12f, cb + sr * 1.6f - sf * 0.12f, cb + sr * 1.6f + sf * 0.12f, cb - sr * 1.6f + sf * 0.12f, up);

        // banner arch: two posts on the sidewalks and a beam with a checker pattern
        Quaternion rot = Quaternion.LookRotation(f, up);
        float side2 = hw + 1.6f, postH = 12f, baseY = CurbHeight;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 pp = p + r * (side * side2);
            batch["bannerRed"].Box(pp, rot, new Vector3(0f, baseY + postH * 0.5f, 0f), new Vector3(1.2f, postH, 1.2f));
            AddBoxCollider("StartPost", pp, rot, new Vector3(0f, baseY + postH * 0.5f, 0f), new Vector3(1.2f, postH, 1.2f));
        }
        float beamW = side2 * 2f + 1.2f, beamY = baseY + postH + 1.2f;
        batch["bannerRed"].Box(p, rot, new Vector3(0f, beamY, 0f), new Vector3(beamW, 3.2f, 0.6f));
        int checkers = Mathf.FloorToInt(beamW / 1.6f);
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < checkers; col++)
            {
                MeshData md = ((row + col) % 2 == 0) ? white : black;
                float x = -checkers * 0.8f + 0.8f + col * 1.6f;
                float y = beamY + (row == 0 ? -0.8f : 0.8f);
                md.Box(p, rot, new Vector3(x, y, 0.32f), new Vector3(1.6f, 1.6f, 0.06f));
                md.Box(p, rot, new Vector3(x, y, -0.32f), new Vector3(1.6f, 1.6f, 0.06f));
            }
        }
    }

    // ---------- shortcut through a yard: narrower, hedges, a ramp and obstacles ----------
    static void BuildShortcut()
    {
        int n = cut.Count;
        float hw2 = ShortcutWidth * 0.5f;
        Vector3 up = Vector3.up;
        MeshData surface = batch["shortcut"];
        MeshData hedge = new MeshData();
        int skipEnds = Mathf.CeilToInt(15f / cut.step);

        for (int i = 0; i < n - 1; i++)
        {
            int j = i + 1;
            Vector3 pi = cut.pos[i], pj = cut.pos[j], ri = cut.right[i], rj = cut.right[j];
            surface.Quad(pi - ri * hw2 + up * (RoadY + 0.005f), pi + ri * hw2 + up * (RoadY + 0.005f),
                         pj + rj * hw2 + up * (RoadY + 0.005f), pj - rj * hw2 + up * (RoadY + 0.005f), up);

            if (i < skipEnds || j > n - 1 - skipEnds) continue;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 nI = ri * s, nJ = rj * s;
                Vector3 ei = pi + nI * hw2, ej = pj + nJ * hw2;
                Vector3 h = up * 1.6f;
                hedge.Quad(ei, ej, ej + h, ei + h, -nI);
                hedge.Quad(ei + h, ej + h, ej + h + nJ * 1.0f, ei + h + nI * 1.0f, up);
                hedge.Quad(ei + nI * 1.0f, ej + nJ * 1.0f, ej + nJ * 1.0f + h, ei + nI * 1.0f + h, nI);
                if (i == skipEnds)
                    hedge.Quad(ei, ei + h, ei + h + nI * 1.0f, ei + nI * 1.0f, -cut.fwd[i]);
                if (j == n - 1 - skipEnds)
                    hedge.Quad(ej, ej + h, ej + h + nJ * 1.0f, ej + nJ * 1.0f, cut.fwd[j]);
            }
        }
        MakeMeshObject("ShortcutHedges", root, hedge, MakeMat(Palette["hedge"]), true);

        // a ramp in the middle (yellow), a huge trash can and a mailbox make it risky
        int ir = Mathf.RoundToInt((n - 1) * 0.55f);
        MakeRamp(cut.pos[ir], cut.fwd[ir], ShortcutWidth - 2.5f);
        int it = Mathf.RoundToInt((n - 1) * 0.3f);
        TrashCan(cut.pos[it] + cut.right[it] * 1.8f, "trashGreen", true);
        int im = Mathf.RoundToInt((n - 1) * 0.78f);
        Mailbox(cut.pos[im] - cut.right[im] * 2.2f, Quaternion.LookRotation(cut.fwd[im], up), true);
    }

    static void MakeRamp(Vector3 p, Vector3 fwd, float width)
    {
        const float length = 7f, thickness = 0.6f, angle = 12f;
        float rad = angle * Mathf.Deg2Rad;
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "ShortcutRamp";
        ramp.transform.SetParent(root, false);
        ramp.transform.localScale = new Vector3(width, thickness, length);
        float y = length * 0.5f * Mathf.Sin(rad) - thickness * 0.5f * Mathf.Cos(rad);
        ramp.transform.localPosition = new Vector3(p.x, y + 0.05f, p.z);
        ramp.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-angle, 0f, 0f);
        ramp.GetComponent<Renderer>().sharedMaterial = MakeMat(Palette["ramp"]);
    }
}
