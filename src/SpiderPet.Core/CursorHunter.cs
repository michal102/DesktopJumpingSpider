namespace SpiderPet.Core;

/// <summary>
/// Now and then the spider takes an interest in the mouse cursor like in prey: when the cursor is close and moves
/// slowly (a good chance), it may stalk it (creeping up in short rushes, always facing it), crouch, and pounce.
/// If the cursor bolts, it gives up (and is cross about it). It is not a state machine of its own in the brain's
/// sense: the brain only gets <see cref="Active"/> (see <see cref="BrainContext.Hunting"/>) and this class drives
/// the spider while it is hunting.
///
/// Call <see cref="Observe"/> every frame while the spider is free to start a hunt, and <see cref="Update"/> every
/// frame while <see cref="Active"/> and the brain is in a hunting (or leaping) state; call <see cref="Abort"/>
/// when something else takes over.
/// </summary>
public sealed class CursorHunter
{
    public enum HuntPhase { Idle, Stalk, Crouch, Pounce }

    private readonly Random _rng;
    private HuntPhase _phase = HuntPhase.Idle;
    private double _t, _interest, _cooldown;
    private Vec2 _prevCursor;
    private bool _primed;
    private double _speed;          // smoothed cursor speed, px/s
    private double _rollTimer;

    public CursorHunter(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _cooldown = 10.0;           // not right after start-up
    }

    public bool Enabled { get; set; } = true;

    /// <summary>The cursor must be this close (body units) to be interesting.</summary>
    public double InterestRangeUnits { get; set; } = 85.0;

    /// <summary>It pounces from this far (body units) or closer.</summary>
    public double PounceRangeUnits { get; set; } = 48.0;

    /// <summary>
    /// A cursor must move at least this fast (px/s) to be prey. 0 means a cursor that sits perfectly still is
    /// hunted too (like a bug that froze), as well as one that moves slowly, up to <see cref="MaxPreySpeed"/>.
    /// </summary>
    public double MinPreySpeed { get; set; } = 0.0;

    /// <summary>A cursor faster than this (px/s) is gone before it can be caught.</summary>
    public double MaxPreySpeed { get; set; } = 320.0;

    /// <summary>The cursor must have been interesting this long (seconds) before it may start.</summary>
    public double InterestSeconds { get; set; } = 0.9;

    /// <summary>Chance per second, once interested, that it starts hunting.</summary>
    public double StartChancePerSecond { get; set; } = 0.3;

    /// <summary>Rest between two hunts, seconds.</summary>
    public double CooldownSeconds { get; set; } = 25.0;

    /// <summary>It gives up a stalk after this long, seconds.</summary>
    public double StalkTimeoutSeconds { get; set; } = 7.0;

    /// <summary>How long it crouches before the pounce, seconds.</summary>
    public double CrouchSeconds { get; set; } = 0.5;

    public bool Active => _phase != HuntPhase.Idle;
    public HuntPhase Phase => _phase;

    /// <summary>Raised when a hunt starts.</summary>
    public event Action? Started;

    /// <summary>Raised when a hunt ends: true = the pounce landed close to the cursor, false = it got away or the spider gave up.</summary>
    public event Action<bool>? Finished;

    /// <summary>Forget the hunt (something more important happened).</summary>
    public void Abort()
    {
        if (_phase == HuntPhase.Idle) return;
        _phase = HuntPhase.Idle;
        _interest = 0.0;
        _cooldown = Math.Max(_cooldown, 8.0);
    }

    /// <summary>Watches the cursor and decides whether to start a hunt. Call it while the spider is free to.</summary>
    public void Observe(double dt, Spider spider, Vec2 cursor)
    {
        TrackSpeed(dt, cursor);
        if (_cooldown > 0.0) _cooldown -= dt;
        if (!Enabled || Active) { _interest = 0.0; return; }

        double distance = (cursor - spider.Position).Length;
        double range = InterestRangeUnits * spider.PixelsPerUnit;
        bool prey = distance <= range && distance >= 40.0 && _speed >= MinPreySpeed && _speed <= MaxPreySpeed;

        if (!prey)
        {
            _interest = Math.Max(0.0, _interest - dt * 2.0);
            return;
        }

        _interest += dt;
        _rollTimer += dt;
        if (_interest >= InterestSeconds && _cooldown <= 0.0 && _rollTimer >= 0.25)
        {
            _rollTimer = 0.0;
            if (_rng.NextDouble() < StartChancePerSecond * 0.25)
                Begin();
        }
    }

    private void Begin()
    {
        _phase = HuntPhase.Stalk;
        _t = 0.0;
        Started?.Invoke();
    }

    /// <summary>Drives the spider while a hunt is on.</summary>
    public void Update(double dt, Spider spider, Vec2 cursor)
    {
        TrackSpeed(dt, cursor);
        if (_phase == HuntPhase.Idle) return;

        _t += dt;
        double ppu = spider.PixelsPerUnit;
        Vec2 to = cursor - spider.Position;
        double distance = to.Length;
        Vec2 dir = distance > 1e-6 ? to * (1.0 / distance) : Vec2.Zero;

        if (_phase != HuntPhase.Pounce && spider.IsLeaping)
        {
            Abort();   // something else made it jump
            return;
        }

        switch (_phase)
        {
            case HuntPhase.Stalk:
            {
                if (_speed > MaxPreySpeed * 1.8 || distance > InterestRangeUnits * ppu * 1.6 || _t > StalkTimeoutSeconds)
                {
                    End(false);
                    return;
                }

                spider.LookTarget = cursor;
                // creeping: short rushes with a freeze between them
                double cycle = _t % 0.7;
                bool rush = cycle < 0.42;
                spider.MoveInput = rush ? dir * 0.32 : Vec2.Zero;

                if (distance <= PounceRangeUnits * ppu)
                {
                    spider.MoveInput = Vec2.Zero;
                    _phase = HuntPhase.Crouch;
                    _t = 0.0;
                }
                break;
            }

            case HuntPhase.Crouch:
            {
                spider.MoveInput = Vec2.Zero;
                spider.LookTarget = cursor;
                if (_speed > MaxPreySpeed * 1.8 || distance > PounceRangeUnits * ppu * 1.8)
                {
                    End(false);
                    return;
                }
                if (_t >= CrouchSeconds)
                {
                    // a good chance: the cursor must be close and not racing away
                    if (distance >= 30.0 && distance <= PounceRangeUnits * ppu * 1.4 && _speed <= MaxPreySpeed)
                    {
                        spider.Leap(dir, Math.Max(20.0, distance - 14.0));
                        _phase = HuntPhase.Pounce;
                        _t = 0.0;
                    }
                    else
                    {
                        End(false);
                    }
                }
                break;
            }

            case HuntPhase.Pounce:
            {
                spider.MoveInput = Vec2.Zero;
                if (!spider.IsLeaping && _t > 0.05)
                    End(distance <= 110.0);
                else if (_t > 2.0)
                    End(false);
                break;
            }
        }
    }

    private void End(bool caught)
    {
        _phase = HuntPhase.Idle;
        _interest = 0.0;
        _cooldown = CooldownSeconds * (caught ? 1.4 : 1.0);
        Finished?.Invoke(caught);
    }

    private void TrackSpeed(double dt, Vec2 cursor)
    {
        if (dt <= 0.0) return;
        if (!_primed)
        {
            _primed = true;
            _prevCursor = cursor;
            return;
        }
        double v = (cursor - _prevCursor).Length / dt;
        _prevCursor = cursor;
        _speed += (v - _speed) * Math.Min(1.0, dt * 8.0);
    }
}
