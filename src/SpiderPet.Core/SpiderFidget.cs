namespace SpiderPet.Core;

/// <summary>
/// Small idle behaviours that make the spider feel alive when nothing is going on:
/// <list type="bullet">
/// <item>it cleans itself (<see cref="Spider.Grooming"/>) in its longer pauses, and now and then also while it
/// watches something (the cursor, text, an icon, a video);</item>
/// <item>when the user has done nothing at all for a long time it gets drowsy (<see cref="Drowsy"/>: the host walks
/// it to a corner), and once it is there (<see cref="FallAsleep"/>) it sleeps (<see cref="Spider.Sleeping"/>). It wakes
/// the moment the user does something, and it never dozes off while a video or game is on.</item>
/// </list>
/// It only decides; the brain gets <see cref="Sleeping"/> through <see cref="BrainContext.Sleeping"/> and the spider
/// shows the poses. Call <see cref="Update"/> every frame.
/// </summary>
public sealed class SpiderFidget
{
    private readonly Random _rng;
    private double _still;          // seconds the spider has been standing still
    private double _groomLeft;
    private double _groomCooldown;
    private double _wake;           // seconds since it woke up (a short grace period before it may doze again)
    private double _drowsyFor;

    public SpiderFidget(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _groomCooldown = 3.0 + 4.0 * _rng.NextDouble();
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Groom this often per second of stillness (it is doubled in a longer pause).</summary>
    public double GroomChancePerSecond { get; set; } = 0.12;

    /// <summary>It only cleans itself after standing still this long, seconds.</summary>
    public double MinStillSeconds { get; set; } = 1.2;

    /// <summary>A longer pause (more chances to clean itself) starts after this many seconds of standing still.</summary>
    public double LongPauseSeconds { get; set; } = 2.5;

    /// <summary>Falls asleep after the user was idle this long, seconds.</summary>
    public double SleepAfterSeconds { get; set; } = 45.0;

    public bool Grooming { get; private set; }

    /// <summary>It wants to sleep: it is going to a place to do it, or is asleep already. The brain gets this as <see cref="BrainContext.Sleeping"/>.</summary>
    public bool Drowsy { get; private set; }

    /// <summary>It is asleep.</summary>
    public bool Sleeping { get; private set; }

    /// <summary>Raised when it first gets drowsy (a yawn).</summary>
    public event Action? Yawned;

    /// <summary>The host calls this once the spider has reached the place where it wants to sleep.</summary>
    public void FallAsleep()
    {
        if (Drowsy)
            SetSleeping(true);
    }

    /// <summary>Raised when it falls asleep (true) or wakes up (false).</summary>
    public event Action<bool>? SleepChanged;

    /// <param name="idleSeconds">Seconds since the user last did anything (moved the mouse, typed, clicked).</param>
    /// <param name="allowSleep">False while something keeps it busy (watching a video with you, say).</param>
    public void Update(double dt, Spider spider, SpiderState state, double idleSeconds, bool allowSleep = true)
    {
        bool standing = spider.Velocity.Length < 3.0 && spider.MoveInput.Length < 0.01 && !spider.IsLeaping && !spider.IsGrabbed;
        _still = standing ? _still + dt : 0.0;
        _wake += dt;

        if (!Enabled)
        {
            Drowsy = false;
            SetSleeping(false);
            Grooming = false;
            _groomLeft = 0.0;
            return;
        }

        // ---- getting drowsy, and sleeping
        if (Drowsy)
        {
            _drowsyFor += dt;
            // any input wakes it, and so does anything that takes the brain away from calm states
            if (idleSeconds < 1.0 || !allowSleep || !Calm(state))
            {
                Drowsy = false;
                SetSleeping(false);
                _wake = 0.0;
            }
            else if (!Sleeping && _drowsyFor > 25.0)
            {
                SetSleeping(true);   // it could not get to its place: it sleeps where it is
            }
        }
        else if (allowSleep && idleSeconds >= SleepAfterSeconds && _wake > 5.0 && Calm(state))
        {
            Drowsy = true;
            _drowsyFor = 0.0;
            Yawned?.Invoke();
        }

        if (Drowsy)
        {
            Grooming = false;
            _groomLeft = 0.0;
            return;
        }

        // ---- grooming
        if (_groomCooldown > 0.0)
            _groomCooldown -= dt;

        if (Grooming)
        {
            _groomLeft -= dt;
            if (_groomLeft <= 0.0 || !standing || !Calm(state))
            {
                Grooming = false;
                _groomCooldown = 3.0 + 5.0 * _rng.NextDouble();
            }
        }
        else if (_groomCooldown <= 0.0 && _still >= MinStillSeconds && Calm(state))
        {
            double chance = GroomChancePerSecond * (_still >= LongPauseSeconds ? 2.0 : 1.0);
            if (_rng.NextDouble() < chance * dt)
            {
                Grooming = true;
                _groomLeft = 1.6 + 1.4 * _rng.NextDouble();
            }
        }
    }

    // States in which nothing needs the spider's full attention.
    private static bool Calm(SpiderState state) => state is SpiderState.Watching or SpiderState.Wandering
        or SpiderState.LookingAtCursor or SpiderState.LookingAtText or SpiderState.LookingAtIcon
        or SpiderState.LookingAtWindow or SpiderState.WatchingMedia or SpiderState.Sleeping;

    private void SetSleeping(bool value)
    {
        if (Sleeping == value) return;
        Sleeping = value;
        SleepChanged?.Invoke(value);
    }
}
