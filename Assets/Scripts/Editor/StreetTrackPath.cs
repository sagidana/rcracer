using System.Collections.Generic;
using UnityEngine;

// Turns the waypoint list into a smooth path: straight lines with round corners.
public static partial class StreetTrackBuilder
{
    internal class PathData
    {
        public Vector3[] pos, fwd, right;
        public float step;
        public int Count { get { return pos.Length; } }
    }

    static Vector3 P(WP w) { return new Vector3(w.x, 0f, w.z); }

    static void AddDense(List<Vector3> list, Vector3 p)
    {
        if (list.Count == 0 || Vector3.Distance(list[list.Count - 1], p) > 0.0001f) list.Add(p);
    }

    internal static PathData BuildPath(WP[] w, bool closed)
    {
        int n = w.Length;
        float[] arcT = new float[n], arcR = new float[n], arcTheta = new float[n], k = new float[n];

        // 1) work out the corner arcs
        for (int i = 0; i < n; i++)
        {
            k[i] = 1f;
            if (w[i].radius <= 0.01f) continue;
            if (!closed && (i == 0 || i == n - 1)) continue;
            Vector3 p = P(w[i]), prev = P(w[(i - 1 + n) % n]), next = P(w[(i + 1) % n]);
            Vector3 d1 = (p - prev).normalized, d2 = (next - p).normalized;
            float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d1, d2), -1f, 1f));
            if (theta < 0.01f) continue;
            arcTheta[i] = theta;
            arcR[i] = w[i].radius;
            arcT[i] = arcR[i] * Mathf.Tan(theta * 0.5f);
        }

        // 2) if two corners are too close, make their radius smaller so they fit
        int segs = closed ? n : n - 1;
        for (int s = 0; s < segs; s++)
        {
            int a = s, b = (s + 1) % n;
            float len = Vector3.Distance(P(w[a]), P(w[b]));
            float need = arcT[a] + arcT[b];
            if (need > len * 0.98f)
            {
                float f = len * 0.98f / need;
                k[a] = Mathf.Min(k[a], f);
                k[b] = Mathf.Min(k[b], f);
                Debug.LogWarning("StreetTrack: waypoints " + a + " and " + b + " are too close for their corner radius - radius reduced.");
            }
        }

        // 3) dense list of points
        List<Vector3> dense = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            Vector3 p = P(w[i]);
            if (arcTheta[i] <= 0f) { AddDense(dense, p); continue; }
            Vector3 prev = P(w[(i - 1 + n) % n]), next = P(w[(i + 1) % n]);
            Vector3 d1 = (p - prev).normalized, d2 = (next - p).normalized;
            float r = arcR[i] * k[i], t = arcT[i] * k[i], theta = arcTheta[i];
            float sign = Vector3.Cross(d1, d2).y > 0f ? 1f : -1f; // +1 = right turn
            Vector3 start = p - d1 * t;
            Vector3 center = start + Vector3.Cross(Vector3.up, d1) * (sign * r);
            Vector3 v0 = start - center;
            int count = Mathf.Max(3, Mathf.CeilToInt(theta * r));
            for (int q = 0; q <= count; q++)
            {
                float ang = sign * theta * q / count * Mathf.Rad2Deg;
                AddDense(dense, center + Quaternion.AngleAxis(ang, Vector3.up) * v0);
            }
        }
        if (closed) dense.Add(dense[0]);

        // 4) resample so that samples are evenly spaced
        float[] cum = new float[dense.Count];
        for (int i = 1; i < dense.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
        float total = cum[cum.Length - 1];

        int m = closed ? Mathf.RoundToInt(total / SampleStep) : Mathf.RoundToInt(total / SampleStep) + 1;
        m = Mathf.Max(m, 3);
        float step = closed ? total / m : total / (m - 1);

        PathData pd = new PathData();
        pd.step = step;
        pd.pos = new Vector3[m];
        int idx = 0;
        for (int i = 0; i < m; i++)
        {
            float d = i * step;
            while (idx < cum.Length - 2 && cum[idx + 1] < d) idx++;
            float seg = cum[idx + 1] - cum[idx];
            float f = seg > 0f ? Mathf.Clamp01((d - cum[idx]) / seg) : 0f;
            pd.pos[i] = Vector3.Lerp(dense[idx], dense[idx + 1], f);
        }

        pd.fwd = new Vector3[m];
        pd.right = new Vector3[m];
        for (int i = 0; i < m; i++)
        {
            Vector3 a, b;
            if (closed) { a = pd.pos[(i - 1 + m) % m]; b = pd.pos[(i + 1) % m]; }
            else { a = pd.pos[Mathf.Max(i - 1, 0)]; b = pd.pos[Mathf.Min(i + 1, m - 1)]; }
            pd.fwd[i] = (b - a).normalized;
            pd.right[i] = Vector3.Cross(Vector3.up, pd.fwd[i]).normalized;
        }
        return pd;
    }

    // For every sample: distance to the nearest sample of a FAR-away part of the loop.
    // Small value = another road crosses here (an intersection).
    static float[] DistToOtherParts()
    {
        int m = main.Count;
        int skip = Mathf.CeilToInt(60f / SampleStep);
        float[] d = new float[m];
        for (int i = 0; i < m; i++)
        {
            float best = float.MaxValue;
            for (int j = 0; j < m; j++)
            {
                int gap = Mathf.Abs(i - j);
                gap = Mathf.Min(gap, m - gap);
                if (gap <= skip) continue;
                float sq = (main.pos[i] - main.pos[j]).sqrMagnitude;
                if (sq < best) best = sq;
            }
            d[i] = Mathf.Sqrt(best);
        }
        return d;
    }
}
