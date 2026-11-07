namespace SpiderPet.Core;

public static class MathUtil
{
    public const double DegToRad = Math.PI / 180.0;
    public const double RadToDeg = 180.0 / Math.PI;

    /// <summary>Wraps an angle in degrees to (-180, 180].</summary>
    public static double Wrap(double a)
    {
        a %= 360.0;
        if (a <= -180.0) a += 360.0;
        else if (a > 180.0) a -= 360.0;
        return a;
    }

    /// <summary>Maps any angle in degrees to [0, 360).</summary>
    public static double To360(double a)
    {
        a %= 360.0;
        if (a < 0.0) a += 360.0;
        return a;
    }

    public static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    public static double MoveToward(double current, double target, double maxDelta)
    {
        double d = target - current;
        if (Math.Abs(d) <= maxDelta) return target;
        return current + Math.CopySign(maxDelta, d);
    }

    /// <summary>
    /// Angle in degrees from <paramref name="from"/> to <paramref name="to"/>:
    /// up = 0, right = 90, down = 180, left = -90 (clockwise positive), range (-180, 180].
    /// </summary>
    public static double HeadingFromTo(Vec2 from, Vec2 to) =>
        Math.Atan2(to.X - from.X, -(to.Y - from.Y)) * RadToDeg;
}
