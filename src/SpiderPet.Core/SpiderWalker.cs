namespace SpiderPet.Core;

/// <summary>
/// Walks a spider to a goal the way a real one does: along a skittering path, in short dashes with a still moment
/// (a "think") in between. The same stop-and-go rhythm as the carry walk, packaged for the wanderer.
/// Call <see cref="Step"/> every frame after the normal input handling and before <see cref="Spider.Update"/>.
/// </summary>
public sealed class SpiderWalker
{
    private enum Phase { Dash, Pause }

    private readonly Random _rng;
    private readonly SkitterPath _skitter;
    private Phase _phase = Phase.Pause;
    private double _t, _dur, _dashPace, _dashJitter, _glanceA, _glanceB;

    public SpiderWalker(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _skitter = new SkitterPath(_rng);
    }

    /// <summary>The path being walked, for debug drawing.</summary>
    public SkitterPath Path => _skitter;

    /// <summary>The area the detours are kept inside.</summary>
    public RectD? Bounds
    {
        get => _skitter.Bounds;
        set => _skitter.Bounds = value;
    }

    /// <summary>How much it glances about while it thinks (1 = like a carrying spider, 0 = not at all).</summary>
    public double GlanceScale { get; set; } = 1.0;

    /// <summary>How much each dash wanders off the straight line (1 = like a carrying spider).</summary>
    public double JitterScale { get; set; } = 1.0;

    /// <summary>True while it stands still between two dashes.</summary>
    public bool Pausing => _phase == Phase.Pause;

    /// <summary>Forget the current path and start with a short think, as if it just chose a new goal.</summary>
    public void Begin(bool thinkFirst = true)
    {
        _skitter.Reset();
        if (thinkFirst) BeginPause(true);
        else BeginDash();
    }

    /// <param name="pace">Fraction of the spider's speed (0..1).</param>
    public void Step(double dt, Spider spider, Vec2 goal, double pace)
    {
        int before = _skitter.NextIndex;
        Vec2 aim = _skitter.Next(spider.Position, goal);
        bool reached = _skitter.NextIndex != before;

        Vec2 to = aim - spider.Position;
        double distance = to.Length;
        Vec2 dir = distance > 1e-6 ? to * (1.0 / distance) : Vec2.Zero;
        _t += dt;

        if (_phase == Phase.Dash)
        {
            if (_t >= _dur || reached)
            {
                BeginPause(reached ? _rng.NextDouble() < 0.2 : _rng.NextDouble() < 0.05);
            }
            else
            {
                Vec2 heading = dir.Rotated(_dashJitter);
                spider.MoveInput = heading * (_dashPace * pace);
                spider.LookTarget = spider.Position + heading * 100.0;
                return;
            }
        }

        spider.MoveInput = Vec2.Zero;
        double k = _dur > 1e-6 ? _t / _dur : 1.0;
        double glance = k < 0.28 ? _glanceA : k < 0.55 ? _glanceB : 0.0;
        spider.LookTarget = spider.Position + dir.Rotated(glance) * 100.0;

        if (_t >= _dur)
            BeginDash();
    }

    private void BeginPause(bool thinking)
    {
        _phase = Phase.Pause;
        _t = 0.0;
        _dur = thinking ? 0.5 + 0.5 * _rng.NextDouble() : 0.10 + 0.25 * _rng.NextDouble();
        double side = _rng.Next(2) == 0 ? -1.0 : 1.0;
        _glanceA = thinking ? side * (10.0 + 12.0 * _rng.NextDouble()) * GlanceScale : 0.0;
        _glanceB = thinking ? -side * (3.0 + 5.0 * _rng.NextDouble()) * GlanceScale : 0.0;
    }

    private void BeginDash()
    {
        _phase = Phase.Dash;
        _t = 0.0;
        _dur = 0.35 + 0.5 * _rng.NextDouble();
        _dashPace = Math.Min(1.0, 0.85 + 0.4 * _rng.NextDouble());
        _dashJitter = (_rng.NextDouble() - 0.5) * 12.0 * JitterScale;
    }
}
