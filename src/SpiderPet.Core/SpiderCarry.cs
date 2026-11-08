namespace SpiderPet.Core;

public enum CarryPhase
{
    /// <summary>Nothing is being carried and no item lies around.</summary>
    Idle,
    /// <summary>The spider snatches the item from where it was dropped.</summary>
    Grab,
    /// <summary>The spider walks to the destination with the item in its mandibles.</summary>
    Carry,
    /// <summary>The spider sets the item down.</summary>
    Place,
    /// <summary>The item lies on the ground for a few seconds, then fades away.</summary>
    Rest,
}

/// <summary>
/// "Something was dropped on the spider": it grabs the item, carries it to a destination and sets it down.
/// Purely a visual game - the item is a ghost, nothing on disk is touched.
///
/// While <see cref="Busy"/> this class takes over the spider's <see cref="Spider.MoveInput"/> and
/// <see cref="Spider.LookTarget"/>, so call <see cref="Update"/> after the normal input handling and before
/// <see cref="Spider.Update"/>. The renderer reads <see cref="ItemPosition"/>, <see cref="ItemAngleDeg"/>,
/// <see cref="ItemScale"/> and <see cref="ItemAlpha"/> whenever <see cref="HasItem"/> is true.
/// </summary>
public sealed class SpiderCarry
{
    private Vec2 _drop;
    private Vec2 _destination;
    private Vec2 _ground;
    private Vec2 _placeFrom;
    private double _t;

    public double GrabSeconds { get; set; } = 0.35;
    public double PlaceSeconds { get; set; } = 0.45;
    public double RestSeconds { get; set; } = 6.0;
    public double FadeSeconds { get; set; } = 1.0;

    /// <summary>Walking stops within this many pixels of the destination.</summary>
    public double ArriveDistance { get; set; } = 14.0;

    /// <summary>Give up and set the item down wherever the spider is after this many seconds.</summary>
    public double MaxCarrySeconds { get; set; } = 12.0;

    /// <summary>How far in front of the body centre the item is held, in body units.</summary>
    public double MouthUnits { get; set; } = 6.5;

    /// <summary>Walking speed while carrying, as a fraction of the spider's speed.</summary>
    public double CarryPace { get; set; } = 0.8;

    public CarryPhase Phase { get; private set; }

    /// <summary>True while the carry task controls the spider (grab, carry, place).</summary>
    public bool Busy => Phase is CarryPhase.Grab or CarryPhase.Carry or CarryPhase.Place;

    /// <summary>True while there is an item to draw.</summary>
    public bool HasItem => Phase != CarryPhase.Idle;

    public Vec2 Destination => _destination;
    public Vec2 ItemPosition { get; private set; }
    public double ItemAngleDeg { get; private set; }
    public double ItemScale { get; private set; } = 1.0;
    public double ItemAlpha { get; private set; } = 1.0;

    /// <summary>Starts carrying an item that was dropped at <paramref name="dropPoint"/> to <paramref name="destination"/>. Replaces any earlier item.</summary>
    public void Start(Vec2 dropPoint, Vec2 destination)
    {
        _drop = dropPoint;
        _destination = destination;
        _t = 0.0;
        Phase = CarryPhase.Grab;
        ItemPosition = dropPoint;
        ItemAngleDeg = 0.0;
        ItemScale = 1.0;
        ItemAlpha = 1.0;
    }

    /// <summary>The user picked the spider up: put the item down where it is.</summary>
    public void Cancel()
    {
        if (!Busy) return;
        _ground = ItemPosition;
        _t = 0.0;
        Phase = CarryPhase.Rest;
    }

    public void Update(double dt, Spider spider)
    {
        switch (Phase)
        {
            case CarryPhase.Idle:
                return;

            case CarryPhase.Grab:
            {
                _t += dt;
                double k = Smooth(_t / GrabSeconds);
                spider.MoveInput = Vec2.Zero;
                spider.LookTarget = _drop;
                ItemPosition = _drop.Lerp(Mouth(spider), k);
                ItemAngleDeg = spider.HeadingDeg * k;
                if (_t >= GrabSeconds)
                {
                    Phase = CarryPhase.Carry;
                    _t = 0.0;
                }
                return;
            }

            case CarryPhase.Carry:
            {
                _t += dt;
                Vec2 to = _destination - spider.Position;
                double distance = to.Length;
                if (distance <= ArriveDistance || _t >= MaxCarrySeconds)
                {
                    _placeFrom = Mouth(spider);
                    _ground = spider.Position + Forward(spider) * ((MouthUnits + 4.0) * spider.PixelsPerUnit);
                    Phase = CarryPhase.Place;
                    _t = 0.0;
                    spider.MoveInput = Vec2.Zero;
                    spider.LookTarget = _ground;
                    return;
                }

                spider.MoveInput = to * (CarryPace / distance);
                spider.LookTarget = _destination;
                ItemPosition = Mouth(spider);
                // the item sways a little as it is dragged along
                ItemAngleDeg = spider.HeadingDeg + 7.0 * Math.Sin(_t * 10.0);
                return;
            }

            case CarryPhase.Place:
            {
                _t += dt;
                double k = Smooth(_t / PlaceSeconds);
                spider.MoveInput = Vec2.Zero;
                spider.LookTarget = _ground;
                ItemPosition = _placeFrom.Lerp(_ground, k);
                if (_t >= PlaceSeconds)
                {
                    Phase = CarryPhase.Rest;
                    _t = 0.0;
                }
                return;
            }

            case CarryPhase.Rest:
            {
                _t += dt;
                ItemPosition = _ground;
                double fadeStart = RestSeconds - FadeSeconds;
                ItemAlpha = _t <= fadeStart ? 1.0 : Math.Max(0.0, 1.0 - (_t - fadeStart) / FadeSeconds);
                if (_t >= RestSeconds)
                {
                    Phase = CarryPhase.Idle;
                    ItemAlpha = 0.0;
                }
                return;
            }
        }
    }

    /// <summary>
    /// Picks where to take the item. Candidates are tried in order and the first "good" one wins: inside
    /// <paramref name="area"/>, on the same surface as the spider (bare desktop or the same window), and not on top
    /// of a desktop icon. If none is good, the last candidate is used.
    /// </summary>
    /// <param name="towardCursor">Gift mode: aim near the mouse cursor instead of near the spider.</param>
    public static Vec2 PickDestination(Spider spider, DesktopSnapshot snapshot, RectD area, Random rng, bool towardCursor)
    {
        const double margin = 60.0;
        double ppu = spider.PixelsPerUnit;
        Vec2 centre = towardCursor ? snapshot.Cursor : spider.Position;
        long surface = SurfaceAt(snapshot.Windows, spider.Position);

        Vec2 candidate = centre;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double angle = rng.NextDouble() * Math.PI * 2.0;
            double radius = towardCursor
                ? (22.0 + rng.NextDouble() * 12.0) * ppu     // just beside the cursor
                : (50.0 + rng.NextDouble() * 60.0) * ppu;    // a short walk away

            candidate = new Vec2(
                Math.Clamp(centre.X + Math.Cos(angle) * radius, area.X + margin, Math.Max(area.X + margin, area.Right - margin)),
                Math.Clamp(centre.Y + Math.Sin(angle) * radius, area.Y + margin, Math.Max(area.Y + margin, area.Bottom - margin)));

            if (SurfaceAt(snapshot.Windows, candidate) != surface)
                continue;
            if (OverIcon(snapshot.Icons, candidate, 48.0))
                continue;
            return candidate;
        }

        return candidate;
    }

    // ------------------------------------------------------------------ helpers

    private static long SurfaceAt(IReadOnlyList<DesktopWindow> windows, Vec2 p)
    {
        foreach (DesktopWindow w in windows)
            if (w.Bounds.Contains(p))
                return w.Id;
        return 0; // the bare desktop
    }

    private static bool OverIcon(IReadOnlyList<DesktopIcon>? icons, Vec2 p, double clearance)
    {
        if (icons is null) return false;
        foreach (DesktopIcon icon in icons)
            if ((icon.Center - p).Length < clearance)
                return true;
        return false;
    }

    private static Vec2 Forward(Spider spider)
    {
        double h = spider.HeadingDeg * MathUtil.DegToRad;
        return new Vec2(Math.Sin(h), -Math.Cos(h));
    }

    private Vec2 Mouth(Spider spider) =>
        spider.Position + Forward(spider) * (MouthUnits * spider.PixelsPerUnit);

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }
}
