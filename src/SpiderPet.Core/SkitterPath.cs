namespace SpiderPet.Core;

/// <summary>
/// Breaks a walk towards a goal into short legs with small sideways detours, so the spider skitters left and
/// right of the straight line instead of marching along it. The last waypoint is always the real goal.
/// </summary>
public sealed class SkitterPath
{
    private readonly Random _rng;
    private readonly List<Vec2> _points = new();
    private int _next;
    private Vec2 _goal;

    public SkitterPath(Random? rng = null) => _rng = rng ?? new Random();

    /// <summary>Roughly how far apart the waypoints are, px.</summary>
    public double SegmentLength { get; set; } = 80.0;

    /// <summary>Largest sideways detour from the straight line, px (it fades to zero near the start and the goal).</summary>
    public double MaxSideOffset { get; set; } = 34.0;

    /// <summary>A waypoint counts as reached within this many px.</summary>
    public double ArriveRadius { get; set; } = 14.0;

    /// <summary>If the goal moves further than this from where the path was planned, a new path is planned.</summary>
    public double ReplanDistance { get; set; } = 120.0;

    /// <summary>Waypoints are kept inside this area (with <see cref="EdgeMargin"/>), when set.</summary>
    public RectD? Bounds { get; set; }

    public double EdgeMargin { get; set; } = 40.0;

    public IReadOnlyList<Vec2> Points => _points;

    /// <summary>Index of the waypoint being walked to.</summary>
    public int NextIndex => _next;

    public void Reset()
    {
        _points.Clear();
        _next = 0;
    }

    /// <summary>Plans a fresh path from <paramref name="from"/> to <paramref name="goal"/>.</summary>
    public void Plan(Vec2 from, Vec2 goal)
    {
        _points.Clear();
        _next = 0;
        _goal = goal;

        Vec2 line = goal - from;
        double distance = line.Length;
        int segments = (int)Math.Floor(distance / Math.Max(1.0, SegmentLength));

        if (segments >= 2)
        {
            Vec2 dir = line * (1.0 / distance);
            var perp = new Vec2(-dir.Y, dir.X);
            double side = _rng.Next(2) == 0 ? -1.0 : 1.0;

            for (int i = 1; i < segments; i++)
            {
                double t = (double)i / segments;
                double amplitude = MaxSideOffset * (0.45 + 0.55 * _rng.NextDouble()) * Math.Sin(Math.PI * t);
                double along = (_rng.NextDouble() - 0.5) * 0.3 * SegmentLength;
                _points.Add(Keep(from + line * t + dir * along + perp * (side * amplitude)));

                // mostly alternate sides, now and then keep the same one: it reads as skittering, not as a sine wave
                if (_rng.NextDouble() < 0.8)
                    side = -side;
            }
        }

        _points.Add(goal);
    }

    /// <summary>
    /// The point to walk to right now. Plans a path on first use (or when the goal jumped), moves on to the next
    /// waypoint once the current one is reached, and keeps the last waypoint on the live goal.
    /// </summary>
    public Vec2 Next(Vec2 position, Vec2 goal)
    {
        if (_points.Count == 0 || (goal - _goal).Length > ReplanDistance)
            Plan(position, goal);
        else
        {
            _goal = goal;
            _points[^1] = goal;
        }

        while (_next < _points.Count - 1 && (_points[_next] - position).Length <= ArriveRadius)
            _next++;

        return _points[_next];
    }

    private Vec2 Keep(Vec2 p)
    {
        if (Bounds is not RectD b) return p;
        double m = EdgeMargin;
        double minX = b.X + m, maxX = Math.Max(minX, b.Right - m);
        double minY = b.Y + m, maxY = Math.Max(minY, b.Bottom - m);
        return new Vec2(Math.Clamp(p.X, minX, maxX), Math.Clamp(p.Y, minY, maxY));
    }
}
