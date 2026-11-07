namespace SpiderPet.Core;

/// <summary>
/// Soft, coupled 3-segment leg solver (femur, patella+tibia, metatarsus+tarsus).
///
/// Real spider leg joints do not bend independently: the distal joints follow the proximal one.
/// The two bends are coupled (second = rho * first, same direction), so every leg is one
/// smooth arc described by a single angle that is found by bisection. A soft distance
/// falloff near full extension stops legs snapping straight when the foot is far away.
/// </summary>
internal static class LegIk
{
    public const double RestRatio = 0.82;   // Decreased from 0.88 to force a more bent resting stance // rest distance hip->foot as a fraction of full leg length
    public const double SoftStart = 0.93;   // fraction of full length where the soft falloff begins
    public const double ThetaMax = 100.0;   // largest first-joint bend (degrees)

    public readonly record struct Result(Vec2 Knee, Vec2 Ankle, Vec2 Tip);

    public static (Vec2 Q1, Vec2 Q2, Vec2 Q3) Chain(double a, double b, double c, double rho, double sign, double thetaDeg)
    {
        double p1 = sign * thetaDeg * MathUtil.DegToRad;
        double p2 = sign * thetaDeg * (1.0 + rho) * MathUtil.DegToRad;
        var q1 = new Vec2(a, 0.0);
        Vec2 q2 = q1 + new Vec2(Math.Cos(p1), Math.Sin(p1)) * b;
        Vec2 q3 = q2 + new Vec2(Math.Cos(p2), Math.Sin(p2)) * c;
        return (q1, q2, q3);
    }

    /// <summary>Shortest hip-&gt;tip distance the leg can fold to.</summary>
    public static double MinReach(double a, double b, double c, double rho) =>
        Chain(a, b, c, rho, 1.0, ThetaMax).Q3.Length;

    public static double SoftDistance(double d, double fullLength)
    {
        double t = SoftStart * fullLength;
        if (d <= t) return d;
        double r = fullLength - t;
        return t + r * (1.0 - Math.Exp(-(d - t) / r));
    }

    public static Result Solve(Vec2 hip, Vec2 target, double a, double b, double c, double rho, double sign, double minReach)
    {
        double full = a + b + c;
        Vec2 dv = target - hip;
        double d = dv.Length;
        double dirDeg;
        if (d < 1e-6)
        {
            dirDeg = -90.0;
            d = minReach;
        }
        else
        {
            dirDeg = Math.Atan2(dv.Y, dv.X) * MathUtil.RadToDeg;
        }

        double de = Math.Clamp(SoftDistance(d, full), minReach, full - 1e-3);

        double lo = 0.0;
        double hi = ThetaMax;
        for (int i = 0; i < 28; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Chain(a, b, c, rho, sign, mid).Q3.Length > de) lo = mid;
            else hi = mid;
        }
        double theta = 0.5 * (lo + hi);

        var (q1, q2, q3) = Chain(a, b, c, rho, sign, theta);
        double rot0 = dirDeg - Math.Atan2(q3.Y, q3.X) * MathUtil.RadToDeg;
        return new Result(hip + q1.Rotated(rot0), hip + q2.Rotated(rot0), hip + q3.Rotated(rot0));
    }
}
