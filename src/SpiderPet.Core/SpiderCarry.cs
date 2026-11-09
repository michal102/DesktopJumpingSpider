namespace SpiderPet.Core;

public enum CarryPhase
{
    /// <summary>Nothing is being carried and no item lies around.</summary>
    Idle,
    /// <summary>The spider snatches the item from where it was dropped.</summary>
    Grab,
    /// <summary>The spider walks to the destination with the item in its mandibles.</summary>
    Carry,
    /// <summary>The spider sets the item down (or flicks it away, when it disapproves).</summary>
    Place,
    /// <summary>The item lies on the ground for a few seconds, then fades away.</summary>
    Rest,
}

/// <summary>The spider's own corner of the screen, where it keeps the things it likes.</summary>
public enum SpiderCorner { TopRight, TopLeft, BottomRight, BottomLeft }

/// <summary>How a carry should go.</summary>
/// <param name="Disdain">The spider holds the item at arm's length and flicks it away at the end.</param>
/// <param name="Quip">What the spider says.</param>
/// <param name="BringToCursor">Gift mode: follow the mouse cursor and set the item down next to it.</param>
public readonly record struct CarryOptions(bool Disdain = false, string Quip = "", bool BringToCursor = false);

/// <summary>
/// "Something was dropped on the spider": it grabs the item, carries it to a destination and sets it down.
///
/// While <see cref="Busy"/> this class takes over the spider's <see cref="Spider.MoveInput"/> and
/// <see cref="Spider.LookTarget"/>, so call <see cref="Update"/> after the normal input handling and before
/// <see cref="Spider.Update"/>. The renderer reads <see cref="ItemPosition"/>, <see cref="ItemAngleDeg"/>,
/// <see cref="ItemScale"/> and <see cref="ItemAlpha"/> whenever <see cref="HasItem"/> is true.
/// <see cref="Placed"/> fires when the item touches the ground, which is the moment to move the real thing.
/// </summary>
public sealed class SpiderCarry
{
    private Vec2 _drop;
    private Vec2 _destination;
    private Vec2 _ground;
    private Vec2 _placeFrom;
    private double _placeAngle;
    private double _t;
    private bool _disdain;
    private bool _bringToCursor;
    private bool _cursorLeft;
    private double _quipLeft;
    private readonly SkitterPath _skitter;
    private readonly Random _rng;

    /// <param name="rng">Source of the randomness of the walk; pass a seeded one for repeatable tests.</param>
    public SpiderCarry(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _skitter = new SkitterPath(_rng);
    }

    /// <summary>Walk in short legs with sideways detours (skittering) instead of in a straight line.</summary>
    public bool Skitter { get; set; } = true;

    /// <summary>
    /// Walk like a real spider: short fast dashes, each followed by a still moment in which it looks around
    /// ("thinks"), now and then a longer one. Needs no <see cref="Skitter"/>, but they go well together.
    /// </summary>
    public bool Jerky { get; set; } = true;

    /// <summary>True while the spider stands still between two dashes of a walk.</summary>
    public bool Pausing => Phase == CarryPhase.Carry && _walk == WalkPhase.Pause;

    /// <summary>The path being walked, for debug drawing.</summary>
    public SkitterPath Path => _skitter;

    /// <summary>The screen area; the detours are kept inside it.</summary>
    public RectD? Bounds
    {
        get => _skitter.Bounds;
        set => _skitter.Bounds = value;
    }

    public double GrabSeconds { get; set; } = 0.35;
    public double PlaceSeconds { get; set; } = 0.45;
    public double RestSeconds { get; set; } = 6.0;
    public double FadeSeconds { get; set; } = 1.0;

    /// <summary>Walking stops when the body is within this many pixels of the destination (or the held item is within 10).</summary>
    public double ArriveDistance { get; set; } = 14.0;

    /// <summary>Give up and set the item down wherever the spider is after this many seconds.</summary>
    public double MaxCarrySeconds { get; set; } = 25.0;

    /// <summary>Gift mode: how long the spider waits holding the item for the mouse to leave before it gives up.</summary>
    public double WaitForCursorSeconds { get; set; } = 6.0;

    /// <summary>Gift mode: the mouse must get this far (px) from where the item was dropped before the spider sets off.</summary>
    public double CursorLeaveDistance { get; set; } = 160.0;

    /// <summary>Gift mode: the spider stops this many pixels short of the cursor.</summary>
    public double GiftGap { get; set; } = 64.0;

    /// <summary>How far in front of the body centre the item is held, in body units.</summary>
    public double MouthUnits { get; set; } = 6.5;

    /// <summary>Walking speed while carrying, as a fraction of the spider's speed.</summary>
    public double CarryPace { get; set; } = 0.8;

    /// <summary>How far a disapproved item is flicked past the destination, in px.</summary>
    public double FlickDistance { get; set; } = 70.0;

    public CarryPhase Phase { get; private set; }

    /// <summary>True while the carry task controls the spider (grab, carry, place).</summary>
    public bool Busy => Phase is CarryPhase.Grab or CarryPhase.Carry or CarryPhase.Place;

    /// <summary>True while there is an item to draw.</summary>
    public bool HasItem => Phase != CarryPhase.Idle;

    /// <summary>True once the user pointed at a spot with a double click; the item is then set down exactly there.</summary>
    public bool Pinned { get; private set; }

    public Vec2 Destination => _destination;
    public Vec2 ItemPosition { get; private set; }
    public double ItemAngleDeg { get; private set; }
    public double ItemScale { get; private set; } = 1.0;
    public double ItemAlpha { get; private set; } = 1.0;

    /// <summary>What the spider is saying right now (empty when silent).</summary>
    public string Quip => _quipLeft > 0.0 ? _quip : "";
    private string _quip = "";

    /// <summary>Raised when the item reaches the ground. The argument is where it was put down.</summary>
    public event Action<Vec2>? Placed;

    /// <summary>Starts carrying an item that was dropped at <paramref name="dropPoint"/> to <paramref name="destination"/>. Replaces any earlier item.</summary>
    public void Start(Vec2 dropPoint, Vec2 destination, CarryOptions options = default)
    {
        _drop = dropPoint;
        _destination = destination;
        _ground = destination;
        _disdain = options.Disdain;
        _bringToCursor = options.BringToCursor;
        _cursorLeft = false;
        _quip = options.Quip ?? "";
        _quipLeft = _quip.Length > 0 ? 3.2 : 0.0;
        Pinned = false;
        _skitter.Reset();
        _t = 0.0;
        Phase = CarryPhase.Grab;
        ItemPosition = dropPoint;
        ItemAngleDeg = 0.0;
        ItemScale = 1.0;
        ItemAlpha = 1.0;
    }

    /// <summary>The user pointed at a spot: take the item exactly there instead. Only works before the item is set down.</summary>
    public bool Redirect(Vec2 destination)
    {
        if (Phase is not (CarryPhase.Grab or CarryPhase.Carry)) return false;
        _destination = destination;
        _ground = destination;
        _bringToCursor = false;
        _disdain = false;
        Pinned = true;
        _skitter.Reset();
        _t = 0.0;
        return true;
    }

    /// <summary>The user picked the spider up: put the item down where it is.</summary>
    public void Cancel()
    {
        if (!Busy) return;
        _ground = ItemPosition;
        _t = 0.0;
        Phase = CarryPhase.Rest;
        Placed?.Invoke(_ground);   // the item is on the ground now, so a real desktop icon moves there too
    }

    /// <summary>The user tapped the spider while it holds something: it lets go right where it stands. False if it held nothing.</summary>
    public bool Drop()
    {
        if (!Busy) return false;
        Cancel();
        return true;
    }

    /// <summary>Makes the spider say something for a moment.</summary>
    public void Say(string text, double seconds = 2.2)
    {
        _quip = text ?? "";
        _quipLeft = _quip.Length > 0 ? seconds : 0.0;
    }

    /// <summary>The item has been turned into something real (an icon was moved): stop drawing the ghost.</summary>
    public void EndItem()
    {
        Phase = CarryPhase.Idle;
        ItemAlpha = 0.0;
    }

    /// <param name="cursor">Needed for gift mode; may be null otherwise.</param>
    public void Update(double dt, Spider spider, Vec2? cursor = null)
    {
        if (_quipLeft > 0.0)
            _quipLeft -= dt;

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
                    BeginPause(true);   // it sets off only after a moment of thought
                }
                return;
            }

            case CarryPhase.Carry:
            {
                _t += dt;
                double limit = _bringToCursor ? MaxCarrySeconds + WaitForCursorSeconds : MaxCarrySeconds;

                if (_bringToCursor && cursor is Vec2 c)
                {
                    if ((c - _drop).Length > CursorLeaveDistance)
                        _cursorLeft = true;

                    if (!_cursorLeft)
                    {
                        // The mouse is still on top of the spider (that is where the item was dropped):
                        // stand there holding it until the user moves away.
                        spider.MoveInput = Vec2.Zero;
                        spider.LookTarget = c;
                        ItemPosition = Mouth(spider);
                        ItemAngleDeg = spider.HeadingDeg + Sway(_t);
                        if (_t >= WaitForCursorSeconds)
                            BeginPlace(spider, Mouth(spider));
                        return;
                    }

                    Vec2 away = (spider.Position - c).Normalized();
                    _destination = c + away * GiftGap;
                    _ground = _destination;
                }

                Vec2 to = _destination - spider.Position;
                double distance = to.Length;
                double mouthDistance = (_destination - Mouth(spider)).Length;
                if (distance <= ArriveDistance || mouthDistance <= 10.0 || _t >= limit)
                {
                    BeginPlace(spider, _t >= limit ? Mouth(spider) : _destination);
                    return;
                }

                // the spider walks to the next waypoint of its skittering path (the last one is the destination)
                int waypointBefore = _skitter.NextIndex;
                Vec2 aim = Skitter ? _skitter.Next(spider.Position, _destination) : _destination;
                bool reached = Skitter && _skitter.NextIndex != waypointBefore;

                if (Jerky)
                {
                    WalkStep(dt, spider, aim, reached);
                }
                else
                {
                    Vec2 toAim = aim - spider.Position;
                    double aimDistance = toAim.Length;
                    spider.MoveInput = aimDistance > 1e-6 ? toAim * (CarryPace / aimDistance) : Vec2.Zero;
                    spider.LookTarget = aim;
                }
                ItemPosition = Mouth(spider);
                ItemAngleDeg = spider.HeadingDeg + Sway(_t);
                return;
            }

            case CarryPhase.Place:
            {
                _t += dt;
                double k = Smooth(_t / PlaceSeconds);
                spider.MoveInput = Vec2.Zero;
                spider.LookTarget = _ground;
                ItemPosition = _placeFrom.Lerp(_ground, k);
                if (_disdain)
                    ItemAngleDeg = _placeAngle + 540.0 * k;   // flicked away, spinning
                if (_t >= PlaceSeconds)
                {
                    Phase = CarryPhase.Rest;
                    _t = 0.0;
                    Placed?.Invoke(_ground);
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

    // ------------------------------------------------------------------ stop-and-go walking

    private enum WalkPhase { Dash, Pause }

    private WalkPhase _walk = WalkPhase.Pause;
    private double _walkT, _walkDur;
    private double _dashPace, _dashJitter;
    private double _glanceA, _glanceB;

    // Real spiders do not glide: they dash a few body lengths, freeze, twitch their head about as if deciding,
    // and dash again, each time with a slightly different heading and speed.
    private void WalkStep(double dt, Spider spider, Vec2 aim, bool reachedWaypoint)
    {
        Vec2 to = aim - spider.Position;
        double distance = to.Length;
        Vec2 dir = distance > 1e-6 ? to * (1.0 / distance) : Vec2.Zero;
        _walkT += dt;

        if (_walk == WalkPhase.Dash)
        {
            if (_walkT >= _walkDur || reachedWaypoint)
            {
                // arriving at a waypoint is a good moment to stop and think
                BeginPause(reachedWaypoint ? _rng.NextDouble() < 0.2 : _rng.NextDouble() < 0.05);
            }
            else
            {
                Vec2 heading = dir.Rotated(_dashJitter);
                spider.MoveInput = heading * _dashPace;
                spider.LookTarget = spider.Position + heading * 100.0;
                return;
            }
        }

        // standing still: first it glances to one side, then the other, and finally faces where it will go
        spider.MoveInput = Vec2.Zero;
        double k = _walkDur > 1e-6 ? _walkT / _walkDur : 1.0;
        double glance = k < 0.28 ? _glanceA : k < 0.55 ? _glanceB : 0.0;
        spider.LookTarget = spider.Position + dir.Rotated(glance) * 100.0;

        if (_walkT >= _walkDur)
            BeginDash();
    }

    private void BeginPause(bool thinking)
    {
        _walk = WalkPhase.Pause;
        _walkT = 0.0;
        _walkDur = thinking ? 0.5 + 0.5 * _rng.NextDouble() : 0.10 + 0.25 * _rng.NextDouble();
        double reach = thinking ? 25.0 + 35.0 * _rng.NextDouble() : 8.0 + 22.0 * _rng.NextDouble();
        // Short pauses are just a freeze. Only a long, thinking pause makes it glance aside, and only a little.
        double side = _rng.Next(2) == 0 ? -1.0 : 1.0;
        _glanceA = thinking ? side * (10.0 + 12.0 * _rng.NextDouble()) : 0.0;
        _glanceB = thinking ? -side * (3.0 + 5.0 * _rng.NextDouble()) : 0.0;
    }

    private void BeginDash()
    {
        _walk = WalkPhase.Dash;
        _walkT = 0.0;
        _walkDur = 0.35 + 0.5 * _rng.NextDouble();     // 0.35–0.85 s, about half as many stops
        _dashPace = Math.Min(1.0, CarryPace * (0.85 + 0.4 * _rng.NextDouble()));
        _dashJitter = (_rng.NextDouble() - 0.5) * 12.0;
    }

    private void BeginPlace(Spider spider, Vec2 where)
    {
        _placeFrom = Mouth(spider);
        _placeAngle = ItemAngleDeg;
        _ground = _disdain && !Pinned ? where + Forward(spider) * FlickDistance : where;
        Phase = CarryPhase.Place;
        _t = 0.0;
        spider.MoveInput = Vec2.Zero;
        spider.LookTarget = _ground;
    }

    // An item held in disgust swings wider and faster.
    private double Sway(double t) => _disdain ? 16.0 * Math.Sin(t * 14.0) : 7.0 * Math.Sin(t * 10.0);

    // ------------------------------------------------------------------ choosing where to go

    /// <summary>
    /// Picks where to take the item. Candidates are tried in order and the first "good" one wins: inside
    /// <paramref name="area"/>, on the same surface as the spider (bare desktop or the same window), and not on top
    /// of a desktop icon. If none is good, the last candidate is used.
    /// </summary>
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
                ? (22.0 + rng.NextDouble() * 12.0) * ppu
                : (50.0 + rng.NextDouble() * 60.0) * ppu;

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

    /// <summary>The corner diagonally across from <paramref name="corner"/>: where rubbish is dumped.</summary>
    public static SpiderCorner Opposite(SpiderCorner corner) => corner switch
    {
        SpiderCorner.TopRight => SpiderCorner.BottomLeft,
        SpiderCorner.TopLeft => SpiderCorner.BottomRight,
        SpiderCorner.BottomRight => SpiderCorner.TopLeft,
        _ => SpiderCorner.TopRight,
    };

    /// <summary>
    /// The first free spot (icon centre) in <paramref name="corner"/>: spots are laid out like desktop icons, a column
    /// at a time starting at the corner, and a spot is free when no existing icon sits in its grid cell.
    /// <paramref name="ignoreIndex"/> is the icon being moved (it must not block its own new home).
    /// </summary>
    public static Vec2 FindCornerSlot(RectD area, SpiderCorner corner, IReadOnlyList<DesktopIcon>? icons, int ignoreIndex)
    {
        double iw = 48.0, ih = 48.0;
        if (icons is { Count: > 0 })
        {
            iw = 0.0; ih = 0.0;
            foreach (DesktopIcon icon in icons) { iw += icon.Bounds.Width; ih += icon.Bounds.Height; }
            iw /= icons.Count; ih /= icons.Count;
        }

        double cellW = Math.Max(76.0, iw * 1.7);
        double cellH = Math.Max(88.0, ih * 2.0);
        bool right = corner is SpiderCorner.TopRight or SpiderCorner.BottomRight;
        bool bottom = corner is SpiderCorner.BottomLeft or SpiderCorner.BottomRight;

        double marginX = cellW * 0.55;
        double marginTop = cellH * 0.5;
        double marginBottom = cellH * 0.5 + 56.0;   // keep clear of the taskbar

        int rows = Math.Max(1, (int)((area.Height - marginTop - marginBottom) / cellH) + 1);
        int cols = Math.Max(1, (int)((area.Width - 2 * marginX) / cellW) + 1);

        Vec2 first = default;
        for (int k = 0; k < rows * cols; k++)
        {
            int col = k / rows, row = k % rows;
            var slot = new Vec2(
                right ? area.Right - marginX - col * cellW : area.X + marginX + col * cellW,
                bottom ? area.Bottom - marginBottom - row * cellH : area.Y + marginTop + row * cellH);
            if (k == 0) first = slot;

            bool occupied = false;
            if (icons is not null)
            {
                foreach (DesktopIcon icon in icons)
                {
                    if (icon.Index == ignoreIndex) continue;
                    Vec2 c = icon.Center;
                    if (Math.Abs(c.X - slot.X) < cellW * 0.5 && Math.Abs(c.Y - slot.Y) < cellH * 0.5)
                    {
                        occupied = true;
                        break;
                    }
                }
            }

            if (!occupied)
                return slot;
        }

        return first;
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
        spider.Position + Forward(spider) * ((MouthUnits + (_disdain ? 2.5 : 0.0)) * spider.PixelsPerUnit);

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }
}
