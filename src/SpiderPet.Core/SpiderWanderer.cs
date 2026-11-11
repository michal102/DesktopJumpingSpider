namespace SpiderPet.Core;

/// <summary>
/// The spider walks around on its own, with a purpose: it looks for the nearest edge (of the screen, or of an open
/// window) and goes there, and once it is at an edge it walks along it, round the corners, with a pause now and then.
/// Every step is a short walk (<see cref="MinStepUnits"/>..<see cref="MaxStepUnits"/>) that turns at most
/// <see cref="MaxTurnDeg"/> from where it already faces; only when that cannot work (a wall ahead, an edge behind)
/// it makes one deliberate bigger turn. It does not look about while it rests, so it does not spin on the spot.
/// A spider standing on a window stays on that window and uses its edges.
///
/// The host calls <see cref="Update"/> every frame while the brain is in <see cref="SpiderState.Wandering"/>, and
/// <see cref="Interrupt"/> every frame it is not.
/// </summary>
public sealed class SpiderWanderer
{
    private enum Phase { Resting, Walking }

    private readonly Random _rng;
    private readonly SpiderWalker _walker;
    private Phase _phase = Phase.Resting;
    private double _t, _restDur = 0.5, _walkT;
    private double _hold = 8.0;
    private Vec2 _goal;
    private long _surface;
    private bool _called;           // walking to a place somebody asked for, not wandering
    private double _calledPace = 0.9, _calledArrive = 40.0;
    private Vec2? _restInterest;    // something it glanced at when it stopped

    public SpiderWanderer(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _walker = new SpiderWalker(_rng)
        {
            GlanceScale = 0.3,   // hardly any looking about: that is what made it spin
            JitterScale = 0.5,
        };
        _walker.Path.SegmentLength = 110.0;
        _walker.Path.MaxSideOffset = 12.0;
    }

    /// <summary>The largest turn (degrees, either side) between where it faces and where it goes next.</summary>
    public double MaxTurnDeg { get; set; } = 35.0;

    /// <summary>The bigger turn it may make when the normal ones lead nowhere (a wall ahead, the edge behind it).</summary>
    public double CornerTurnDeg { get; set; } = 110.0;

    /// <summary>Shortest and longest walk, in body units.</summary>
    public double MinStepUnits { get; set; } = 40.0;
    public double MaxStepUnits { get; set; } = 90.0;

    /// <summary>Farther than this (px) from every edge it is "looking for an edge"; closer, it follows the edge.</summary>
    public double FollowGap { get; set; } = 70.0;

    /// <summary>How quickly the pull towards the nearest edge fades with distance, in px.</summary>
    public double EdgeFalloff { get; set; } = 60.0;

    /// <summary>Keeps the goal this far (px) from the screen edge and from the edge of the window it stands on.</summary>
    public double EdgeMargin { get; set; } = 48.0;

    /// <summary>On the bare desktop it keeps this far (px) from windows, so that a detour cannot carry it onto one.</summary>
    public double WindowClearance { get; set; } = 44.0;

    /// <summary>Extra room at the bottom of the screen for the taskbar, in px.</summary>
    public double BottomInset { get; set; } = 56.0;

    /// <summary>Walk round the desktop icons instead of over them (only while there are few of them).</summary>
    public bool AvoidIcons { get; set; } = true;

    /// <summary>With more icons than this the desktop is too crowded to keep clear of them.</summary>
    public int AvoidIconsMaxCount { get; set; } = 30;

    /// <summary>It keeps this far (px) from the edge of an icon.</summary>
    public double IconClearance { get; set; } = 26.0;

    /// <summary>Walking speed as a fraction of the spider's speed.</summary>
    public double Pace { get; set; } = 0.65;

    /// <summary>A rest between two walks lasts a random time between these, in seconds (sometimes much longer).</summary>
    public double RestMinSeconds { get; set; } = 0.5;
    public double RestMaxSeconds { get; set; } = 2.2;

    /// <summary>Give up on a walk that takes longer than this.</summary>
    public double MaxWalkSeconds { get; set; } = 20.0;

    public bool Walking => _phase == Phase.Walking;

    /// <summary>True while it walks to a place it was called to (see <see cref="GoTo"/>).</summary>
    public bool Called => _called && _phase == Phase.Walking;
    public Vec2 Goal => _goal;
    public SpiderWalker Walker => _walker;

    /// <summary>Walk straight to <paramref name="target"/> (it was called there), without thinking first.</summary>
    public void GoTo(Spider spider, Vec2 target, double pace = 0.9, double arriveRadius = 40.0)
    {
        _calledPace = pace;
        _calledArrive = arriveRadius;
        _goal = target;
        _surface = 0;
        _called = true;
        _walkT = 0.0;
        _phase = Phase.Walking;
        _restInterest = null;
        _walker.Bounds = null;
        _walker.Begin(false);
        spider.MoveInput = Vec2.Zero;
    }

    /// <summary>Something else took over: stop, and rest a moment before wandering on.</summary>
    public void Interrupt()
    {
        _called = false;
        _restInterest = null;
        _phase = Phase.Resting;
        _t = 0.0;
        _restDur = 0.8 + 1.2 * _rng.NextDouble();
    }

    /// <param name="interest">Something close that is worth a look when it stops (an icon, a window), or null.</param>
    public void Update(double dt, Spider spider, DesktopSnapshot snapshot, RectD area, Vec2? interest = null)
    {
        if (_phase == Phase.Resting)
        {
            _t += dt;
            spider.MoveInput = Vec2.Zero;
            // it faces what caught its eye, or else keeps facing the way it faces (no looking about: that made it spin)
            Vec2 at = _restInterest ?? spider.Position + Dir(spider.HeadingDeg) * 100.0;
            spider.LookTarget = at;

            if (_t >= _restDur)
                TryStartWalk(spider, snapshot, area);
            return;
        }

        _walkT += dt;
        double arrive = _called ? _calledArrive : 14.0;
        bool lost = false;
        if (!_called)
        {
            long here = SurfaceAt(snapshot.Windows, spider.Position);
            lost = here != _surface || SurfaceAt(snapshot.Windows, _goal) != _surface;
        }
        if (lost || _walkT >= (_called ? 2.0 * MaxWalkSeconds : MaxWalkSeconds) || (_goal - spider.Position).Length <= arrive)
        {
            Rest(spider, interest);
            return;
        }

        if (!_called)
            _walker.Bounds = WalkArea(snapshot, area, _surface);
        _walker.Step(dt, spider, _goal, _called ? _calledPace : Pace);
    }

    private void Rest(Spider spider, Vec2? interest = null)
    {
        bool wasCalled = _called;
        _called = false;
        _phase = Phase.Resting;
        _t = 0.0;
        _restDur = RestMinSeconds + (RestMaxSeconds - RestMinSeconds) * _rng.NextDouble();
        if (_rng.NextDouble() < 0.15)
            _restDur += 2.5 + 3.0 * _rng.NextDouble();   // now and then it sits for a while
        _restInterest = wasCalled ? null : interest;
        if (_restInterest is not null)
            _restDur += 1.0 + 1.5 * _rng.NextDouble();    // it takes a good look
        spider.MoveInput = Vec2.Zero;
    }

    private void TryStartWalk(Spider spider, DesktopSnapshot snapshot, RectD area)
    {
        long surface = SurfaceAt(snapshot.Windows, spider.Position);
        RectD walk = WalkArea(snapshot, area, surface);
        double gapNow = Gap(snapshot, area, surface, spider.Position);
        bool seeking = gapNow > FollowGap;
        _hold = 4.0 + 26.0 * _rng.NextDouble();   // follows the edge at a slightly different distance each time

        // first the gentle turn; if it leads nowhere, one deliberate bigger turn; as a last resort any direction
        double[] turns = { MaxTurnDeg, CornerTurnDeg, 180.0 };
        Vec2? goal = null;
        for (int pass = 0; pass < turns.Length && goal is null; pass++)
        {
            Vec2? candidate = Pick(spider, snapshot, area, surface, walk, turns[pass], seeking, gapNow, out double gap);
            if (candidate is null)
                continue;
            // looking for an edge, a step that does not bring it closer is not good enough yet
            if (seeking && pass < turns.Length - 1 && gap > gapNow - 10.0)
                continue;
            goal = candidate;
        }

        if (goal is null)
        {
            Rest(spider);   // nowhere to go (a tiny window, say); try again later
            return;
        }

        _goal = goal.Value;
        _surface = surface;
        _called = false;
        _restInterest = null;
        _walkT = 0.0;
        _phase = Phase.Walking;
        _walker.Bounds = walk;
        _walker.Begin();
    }

    private Vec2? Pick(Spider spider, DesktopSnapshot snapshot, RectD area, long surface, RectD walk,
                       double maxTurn, bool seeking, double gapNow, out double pickedGap)
    {
        pickedGap = gapNow;
        double ppu = spider.PixelsPerUnit;
        Vec2 pos = spider.Position;

        // icons only exist on the bare desktop, and only matter when there are few of them
        IReadOnlyList<DesktopIcon>? avoid = AvoidIcons && surface == 0 && snapshot.Icons is { Count: > 0 } icons
                                            && icons.Count <= AvoidIconsMaxCount ? icons : null;

        Span<Vec2> points = stackalloc Vec2[16];
        Span<double> weights = stackalloc double[16];
        Span<double> gaps = stackalloc double[16];
        int count = 0;
        double total = 0.0;

        for (int i = 0; i < 16; i++)
        {
            double turn = (_rng.NextDouble() - _rng.NextDouble()) * maxTurn;   // mostly small turns
            double dist = (MinStepUnits + (MaxStepUnits - MinStepUnits) * _rng.NextDouble()) * ppu;
            Vec2 p = pos + Dir(spider.HeadingDeg + turn) * dist;

            if (p.X < walk.X || p.X > walk.Right || p.Y < walk.Y || p.Y > walk.Bottom)
                continue;
            if (!PathStaysOn(snapshot.Windows, pos, p, surface, WindowClearance))
                continue;
            if (avoid is not null && !IconPathClear(avoid, pos, p, IconClearance))
                continue;

            double gap = Gap(snapshot, area, surface, p);
            double straight = 1.0 - 0.5 * Math.Abs(turn) / Math.Max(1.0, maxTurn);   // prefers going on straight
            double w = seeking
                ? Math.Exp(-gap / EdgeFalloff)                    // the closer to an edge the better
                : Math.Exp(-Math.Abs(gap - _hold) / 16.0);        // stay at the same distance from the edge
            w = Math.Max(0.01, w * straight);

            points[count] = p;
            weights[count] = w;
            gaps[count] = gap;
            total += w;
            count++;
        }

        if (count == 0) return null;

        double roll = _rng.NextDouble() * total;
        for (int i = 0; i < count; i++)
        {
            roll -= weights[i];
            if (roll <= 0.0 || i == count - 1)
            {
                pickedGap = gaps[i];
                return points[i];
            }
        }
        return null;
    }

    /// <summary>Where goals may lie: the screen without margins, or the window the spider stands on.</summary>
    private RectD WalkArea(DesktopSnapshot snapshot, RectD area, long surface)
    {
        double m = EdgeMargin;
        double left = area.X + m, top = area.Y + m, right = area.Right - m, bottom = area.Bottom - m - BottomInset;
        if (surface != 0)
        {
            foreach (DesktopWindow w in snapshot.Windows)
            {
                if (w.Id != surface) continue;
                left = Math.Max(left, w.Bounds.X + m);
                top = Math.Max(top, w.Bounds.Y + m);
                right = Math.Min(right, w.Bounds.Right - m);
                bottom = Math.Min(bottom, w.Bounds.Bottom - m);
                break;
            }
        }
        return new RectD(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    /// <summary>
    /// How far <paramref name="p"/> is from the nearest edge it could walk along, counted from the closest the spider
    /// may get to it (so 0 means "right on the edge").
    /// </summary>
    private double Gap(DesktopSnapshot snapshot, RectD area, long surface, Vec2 p)
    {
        if (surface != 0)
        {
            foreach (DesktopWindow w in snapshot.Windows)
            {
                if (w.Id != surface) continue;
                RectD b = w.Bounds;
                double inside = Math.Min(Math.Min(p.X - b.X, b.Right - p.X), Math.Min(p.Y - b.Y, b.Bottom - p.Y));
                return Math.Max(0.0, inside - EdgeMargin);
            }
        }

        double screen = Math.Min(
            Math.Min(p.X - area.X, area.Right - p.X),
            Math.Min(p.Y - area.Y, area.Bottom - p.Y - BottomInset)) - EdgeMargin;
        double gap = Math.Min(screen, WindowDistance(snapshot.Windows, p) - WindowClearance);
        return Math.Max(0.0, gap);
    }

    // None of the sampled points may lie on an icon. A spider that already stands on one may leave it, so then
    // only the goal is checked.
    private static bool IconPathClear(IReadOnlyList<DesktopIcon> icons, Vec2 a, Vec2 b, double clearance)
    {
        bool startBlocked = OnIcon(icons, a, clearance);
        for (int i = startBlocked ? 8 : 1; i <= 8; i++)
        {
            if (OnIcon(icons, a.Lerp(b, i / 8.0), clearance))
                return false;
        }
        return true;
    }

    private static bool OnIcon(IReadOnlyList<DesktopIcon> icons, Vec2 p, double clearance)
    {
        foreach (DesktopIcon icon in icons)
        {
            RectD r = icon.Bounds;
            if (p.X >= r.X - clearance && p.X <= r.Right + clearance && p.Y >= r.Y - clearance && p.Y <= r.Bottom + clearance)
                return true;
        }
        return false;
    }

    // Every step of the way must be on the same surface; on the bare desktop it must also keep clear of windows.
    private static bool PathStaysOn(IReadOnlyList<DesktopWindow> windows, Vec2 a, Vec2 b, long surface, double clearance)
    {
        // a spider that already stands closer than the clearance may still walk away, just not closer
        double need = surface == 0 ? Math.Min(clearance, WindowDistance(windows, a)) - 0.5 : 0.0;

        for (int i = 1; i <= 8; i++)
        {
            Vec2 p = a.Lerp(b, i / 8.0);
            if (SurfaceAt(windows, p) != surface)
                return false;
            if (surface == 0 && WindowDistance(windows, p) < need)
                return false;
        }
        return true;
    }

    /// <summary>Distance from <paramref name="p"/> to the nearest window (infinity when there is none).</summary>
    private static double WindowDistance(IReadOnlyList<DesktopWindow> windows, Vec2 p)
    {
        double best = double.PositiveInfinity;
        foreach (DesktopWindow w in windows)
        {
            RectD b = w.Bounds;
            double dx = Math.Max(Math.Max(b.X - p.X, 0.0), p.X - b.Right);
            double dy = Math.Max(Math.Max(b.Y - p.Y, 0.0), p.Y - b.Bottom);
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    private static long SurfaceAt(IReadOnlyList<DesktopWindow> windows, Vec2 p)
    {
        foreach (DesktopWindow w in windows)
            if (w.Bounds.Contains(p))
                return w.Id;
        return 0;
    }

    // heading: 0 = up, clockwise
    private static Vec2 Dir(double headingDeg)
    {
        double h = headingDeg * MathUtil.DegToRad;
        return new Vec2(Math.Sin(h), -Math.Cos(h));
    }
}
