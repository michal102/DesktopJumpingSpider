namespace SpiderPet.Core;

/// <summary>
/// Builds one smooth, tapered polygon along a Catmull-Rom curve through a few joints.
/// This is what makes limbs look soft: the joints are rounded off by the curve instead of
/// being drawn as separate rigid segments with hard elbows.
/// </summary>
internal static class Stroke
{
    /// <param name="joints">Centre-line control points (hip, knee, ankle, tip ...).</param>
    /// <param name="widths">Width at each joint, interpolated linearly in between.</param>
    public static Vec2[] Build(Vec2[] joints, double[] widths, int sub = 6)
    {
        int n = joints.Length;
        var cl = new List<Vec2>((n - 1) * sub + 1);
        for (int i = 0; i < n - 1; i++)
        {
            Vec2 p0 = joints[Math.Max(i - 1, 0)];
            Vec2 p1 = joints[i];
            Vec2 p2 = joints[i + 1];
            Vec2 p3 = joints[Math.Min(i + 2, n - 1)];
            for (int k = 0; k < sub; k++)
            {
                double t = (double)k / sub;
                double t2 = t * t;
                double t3 = t2 * t;
                cl.Add(new Vec2(
                    Outline.CatmullRom(p0.X, p1.X, p2.X, p3.X, t, t2, t3),
                    Outline.CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t, t2, t3)));
            }
        }
        cl.Add(joints[n - 1]);

        int m = cl.Count;
        var ws = new double[m];
        int idx = 0;
        for (int i = 0; i < n - 1; i++)
        {
            for (int k = 0; k < sub; k++)
                ws[idx++] = widths[i] + (widths[i + 1] - widths[i]) * ((double)k / sub);
        }
        ws[idx] = widths[n - 1];

        var result = new Vec2[2 * m + 3];
        var right = new Vec2[m];
        var lastT = new Vec2(0.0, -1.0);
        for (int i = 0; i < m; i++)
        {
            Vec2 a = cl[Math.Max(i - 1, 0)];
            Vec2 b = cl[Math.Min(i + 1, m - 1)];
            Vec2 t = (b - a).Normalized();
            if (t.Length < 0.5) t = lastT;
            lastT = t;
            var nrm = new Vec2(-t.Y, t.X);
            Vec2 p = cl[i];
            result[i] = p + nrm * (ws[i] / 2.0);
            right[i] = p - nrm * (ws[i] / 2.0);
        }

        Vec2 pEnd = cl[m - 1];
        double capR = ws[m - 1] / 2.0;
        result[m] = pEnd + lastT.Rotated(45.0) * capR;
        result[m + 1] = pEnd + lastT.Rotated(0.0) * capR;
        result[m + 2] = pEnd + lastT.Rotated(-45.0) * capR;

        for (int i = 0; i < m; i++)
            result[m + 3 + i] = right[m - 1 - i];
        return result;
    }
}
