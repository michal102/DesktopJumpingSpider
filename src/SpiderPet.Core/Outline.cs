namespace SpiderPet.Core;

/// <summary>Builds smooth, left/right symmetric body outlines from a half-width profile.</summary>
internal static class Outline
{
    /// <param name="profile">(s, w): s = 0 front .. 1 rear along the body, w = half-width fraction.</param>
    /// <returns>Closed polygon in local units (right side front-&gt;rear, then mirrored left side rear-&gt;front).</returns>
    public static Vec2[] Build((double S, double W)[] profile, double length, double halfWidth, double y0, int sub = 6)
    {
        int n = profile.Length;
        var right = new Vec2[n];
        for (int i = 0; i < n; i++)
            right[i] = new Vec2(profile[i].W * halfWidth, y0 + profile[i].S * length);

        var r = new List<Vec2>();
        for (int i = 0; i < n - 1; i++)
        {
            Vec2 p0 = right[Math.Max(i - 1, 0)];
            Vec2 p1 = right[i];
            Vec2 p2 = right[i + 1];
            Vec2 p3 = right[Math.Min(i + 2, n - 1)];
            for (int k = 0; k < sub; k++)
            {
                double t = (double)k / sub;
                double t2 = t * t;
                double t3 = t2 * t;
                r.Add(new Vec2(
                    CatmullRom(p0.X, p1.X, p2.X, p3.X, t, t2, t3),
                    CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t, t2, t3)));
            }
        }
        r.Add(right[n - 1]);

        int m = r.Count;
        var result = new Vec2[m * 2];
        for (int i = 0; i < m; i++)
            result[i] = r[i];
        for (int i = 0; i < m; i++)
        {
            Vec2 src = r[m - 1 - i];
            result[m + i] = new Vec2(-src.X, src.Y);
        }
        return result;
    }

    internal static double CatmullRom(double p0, double p1, double p2, double p3, double t, double t2, double t3) =>
        0.5 * ((2.0 * p1)
             + (-p0 + p2) * t
             + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
             + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
}
