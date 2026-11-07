namespace SpiderPet.Core;

/// <summary>2D vector in screen space: +X right, +Y down.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public Vec2 Normalized()
    {
        double l = Length;
        return l > 1e-9 ? new Vec2(X / l, Y / l) : Zero;
    }

    /// <summary>Rotates clockwise on screen by <paramref name="deg"/> degrees.</summary>
    public Vec2 Rotated(double deg)
    {
        double r = deg * MathUtil.DegToRad;
        double c = Math.Cos(r);
        double s = Math.Sin(r);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public Vec2 Lerp(Vec2 other, double t) =>
        new(X + (other.X - X) * t, Y + (other.Y - Y) * t);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
}
