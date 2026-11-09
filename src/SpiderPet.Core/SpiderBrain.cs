namespace SpiderPet.Core;

/// <summary>What the spider is currently paying attention to (the "looking" part of its state).</summary>
public enum FocusKind
{
    /// <summary>The mouse cursor (the default).</summary>
    Cursor,
    /// <summary>A desktop icon close to the spider.</summary>
    Icon,
    /// <summary>A window close to the spider.</summary>
    Window,
    /// <summary>A sudden change: a window opened or closed, an icon moved, appeared or was deleted.</summary>
    Change,
    /// <summary>The place where someone is typing.</summary>
    Text,
}

/// <summary>
/// What the spider is doing right now. Exactly one state is active at any time; the order below is also the
/// priority order (first = strongest). A state can only be taken over by one that is listed above it.
/// </summary>
public enum SpiderState
{
    /// <summary>The user holds the spider in the mouse. Overrides everything.</summary>
    Held,
    /// <summary>It was handed something and is carrying it (or setting it down). Overrides everything below.</summary>
    Carrying,
    /// <summary>An item is being dragged over the spider: it turns to it and reaches for it with its front legs.</summary>
    Reaching,
    /// <summary>It was poked (clicked) and is hopping away from the mouse.</summary>
    Leaping,
    /// <summary>It was just made to let go of an item (tap) and looks at what it dropped, for a moment.</summary>
    Dropping,
    /// <summary>Something suddenly changed on the desktop (window opened/closed, icon moved): it stares at it.</summary>
    Startled,
    /// <summary>Someone is typing close to the spider: it watches the text appear.</summary>
    LookingAtText,
    /// <summary>A desktop icon is very close: it looks at it.</summary>
    LookingAtIcon,
    /// <summary>A window is very close: it looks at it.</summary>
    LookingAtWindow,
    /// <summary>The default: it watches the mouse cursor.</summary>
    Watching,
}

/// <summary>What the host tells the brain about things the brain cannot see itself. Pass one every frame.</summary>
/// <param name="Held">The user is holding the spider with the mouse.</param>
/// <param name="Carrying">The carry task is busy (<see cref="SpiderCarry.Busy"/>).</param>
/// <param name="HoverPoint">Where an item that is dragged over the spider currently is, or null when there is none.</param>
public readonly record struct BrainContext(bool Held = false, bool Carrying = false, Vec2? HoverPoint = null);

/// <summary>
/// The spider's decision layer, a small state machine (<see cref="SpiderState"/>).
///
/// Every frame <see cref="Think"/> chooses the one state that wins by priority:
///
///  1. <see cref="SpiderState.Held"/> (mouse holds it)
///  2. <see cref="SpiderState.Carrying"/> (the carry task owns movement and looking)
///  3. <see cref="SpiderState.Reaching"/> (an item hovers over it)
///  4. <see cref="SpiderState.Leaping"/> (it was clicked and hops away)
///  5. <see cref="SpiderState.Dropping"/> (it was just told to let go)
///  6. <see cref="SpiderState.Startled"/> (a sudden change, for <see cref="ChangeSeconds"/>)
///  7. <see cref="SpiderState.LookingAtText"/> (someone types close by)
///  8. <see cref="SpiderState.LookingAtIcon"/> / <see cref="SpiderState.LookingAtWindow"/> (whichever is nearer and close enough)
///  9. <see cref="SpiderState.Watching"/> (the cursor)
///
/// A window that comes fast at the spider makes it hop back a little (<see cref="DodgeWindows"/>); that is a
/// <see cref="SpiderState.Leaping"/> like any other hop.
///
/// and returns where it should look, or null when something else (the mouse, the carry task) is in control.
/// Entering a state raises <see cref="StateChanged"/>. States never need to know about each other: only this
/// priority list decides who overrides whom.
/// </summary>
public sealed class SpiderBrain
{
    private enum ChangeKind { WindowOpened, WindowClosed, IconMoved, IconAdded, IconRemoved }

    private sealed record Change(ChangeKind Kind, long Id, Vec2 LastKnown);

    private readonly Dictionary<long, RectD> _windowsPrev = new();
    private Dictionary<long, RectD> _windowsNow = new();
    private readonly List<Vec2> _iconsPrev = new();   // centres of the icons of the previous list
    private readonly WindowMotion _motion = new();
    private double _clock;
    private double _dodgeCooldown;
    private IReadOnlyList<DesktopWindow>? _windowsRef;
    private IReadOnlyList<DesktopIcon>? _iconsRef;
    private bool _windowsPrimed;
    private bool _iconsPrimed;

    private Change? _change;
    private double _changeLeft;

    private double _dropLeft;
    private Vec2 _dropAt;
    
    private int _ownIcon = -1;      // an icon the spider itself is moving: that is no surprise
    private double _ownIconLeft;

    // ------------------------------------------------------------------ tuning

    /// <summary>An icon this close (in spider body units, see <see cref="Spider.PixelsPerUnit"/>) catches the spider's attention.</summary>
    public double AttentionRadiusUnits { get; set; } = 28.0;

    /// <summary>A window whose nearest edge is this close (body units) catches the spider's attention.</summary>
    public double WindowAttentionRadiusUnits { get; set; } = 22.0;

    /// <summary>Once looking at something, the spider only loses interest beyond radius * this factor (prevents flicker at the edge).</summary>
    public double ReleaseFactor { get; set; } = 1.3;

    /// <summary>Targets closer than this many pixels are ignored: looking at a point under the body is meaningless.</summary>
    public double MinDistance { get; set; } = 8.0;

    /// <summary>How long a sudden change keeps the spider's attention, in seconds.</summary>
    public double ChangeSeconds { get; set; } = 1.0;

    /// <summary>How long the spider stares at an item it was just made to drop, in seconds.</summary>
    public double DropSeconds { get; set; } = 0.7;

    /// <summary>An icon that moved at least this many pixels counts as "moved".</summary>
    public double IconMoveThreshold { get; set; } = 4.0;

    public bool LookAtIcons { get; set; } = true;
    public bool LookAtWindows { get; set; } = true;
    public bool ReactToChanges { get; set; } = true;

    /// <summary>Look at the place where someone is typing, when that is close to the spider.</summary>
    public bool LookAtText { get; set; } = true;

    /// <summary>Typing this close (body units) catches the spider's attention.</summary>
    public double TextAttentionRadiusUnits { get; set; } = 350.0;

    /// <summary>Hop back a little from a window that is coming fast at the spider.</summary>
    public bool DodgeWindows { get; set; } = true;

    /// <summary>A window must move at least this fast to be seen as coming at the spider, px/s.</summary>
    public double DodgeMinSpeed { get; set; } = 250.0;

    /// <summary>Only windows whose edge is within this distance (body units) matter.</summary>
    public double DodgeRangeUnits { get; set; } = 40.0;

    /// <summary>The window must be about to arrive within this many seconds.</summary>
    public double DodgeLookAheadSeconds { get; set; } = 0.4;

    /// <summary>Hop length per px/s of closing speed (it is kept between 18 and 55 px, so a fast window still gets there).</summary>
    public double DodgeShove { get; set; } = 0.08;

    /// <summary>Minimum time between two dodges, seconds.</summary>
    public double DodgeCooldownSeconds { get; set; } = 1.0;

    // ------------------------------------------------------------------ state, for the renderer and the HUD

    /// <summary>The current state.</summary>
    public SpiderState State { get; private set; } = SpiderState.Watching;

    /// <summary>Seconds spent in the current state.</summary>
    public double TimeInState { get; private set; }

    /// <summary>Raised when the state changes: (old, new).</summary>
    public event Action<SpiderState, SpiderState>? StateChanged;

    /// <summary>Raised when the spider hops back from an approaching window.</summary>
    public event Action? Dodged;

    /// <summary>Where the front legs should reach to (hand this to <see cref="Spider.ReachTarget"/>), or null.</summary>
    public Vec2? ReachPoint { get; private set; }

    /// <summary>The icon the spider is currently looking at because it is near, or null.</summary>
    public DesktopIcon? Attention { get; private set; }

    /// <summary>The window the spider is currently looking at because it is near, or null.</summary>
    public DesktopWindow? AttentionWindow { get; private set; }

    /// <summary>True when no window covers the spider, i.e. it is standing on the desktop.</summary>
    public bool OnDesktop { get; private set; }

    /// <summary>The looking part of the state, for the debug drawing.</summary>
    public FocusKind Focus => State switch
    {
        SpiderState.LookingAtIcon => FocusKind.Icon,
        SpiderState.LookingAtWindow => FocusKind.Window,
        SpiderState.Startled => FocusKind.Change,
        SpiderState.LookingAtText => FocusKind.Text,
        _ => FocusKind.Cursor,
    };

    /// <summary>The point being looked at when <see cref="Focus"/> is not <see cref="FocusKind.Cursor"/>.</summary>
    public Vec2 FocusPoint { get; private set; }

    /// <summary>Short description of the current state for the HUD.</summary>
    public string FocusLabel => State switch
    {
        SpiderState.Held => "held",
        SpiderState.Carrying => "carrying something",
        SpiderState.Reaching => "reaching for an item",
        SpiderState.Leaping => "hopping away",
        SpiderState.Dropping => "dropped it",
        SpiderState.LookingAtText => "text being typed",
        SpiderState.LookingAtIcon => $"icon #{Attention?.Index}",
        SpiderState.LookingAtWindow => $"window \"{AttentionWindow?.Title}\"",
        SpiderState.Startled => _change?.Kind switch
        {
            ChangeKind.WindowOpened => "change: window opened",
            ChangeKind.WindowClosed => "change: window closed",
            ChangeKind.IconMoved => "change: icon moved",
            ChangeKind.IconAdded => "change: icon added",
            ChangeKind.IconRemoved => "change: icon removed",
            _ => "change",
        },
        _ => "cursor",
    };

    // ------------------------------------------------------------------ events from the host
    public void IgnoreIconMove(int index, double seconds = 1.5)
    {
        _ownIcon = index;
        _ownIconLeft = seconds;
    }
    

    /// <summary>The spider was just made to let go of something at <paramref name="at"/>: it looks at it for <see cref="DropSeconds"/>.</summary>
    public void NotifyDropped(Vec2 at)
    {
        _dropAt = at;
        _dropLeft = DropSeconds;
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>Convenience for hosts without held / carried / dragged items: always returns a point to look at.</summary>
    public Vec2 Update(double dt, Spider spider, DesktopSnapshot snapshot) =>
        Think(dt, spider, snapshot, default) ?? snapshot.Cursor;

    /// <summary>
    /// Picks the state for this frame and returns the point the spider should look at, or null when the state
    /// leaves looking to somebody else (<see cref="SpiderState.Held"/>, <see cref="SpiderState.Carrying"/>).
    /// </summary>
    /// <param name="dt">Seconds since the previous call.</param>
    public Vec2? Think(double dt, Spider spider, DesktopSnapshot snapshot, BrainContext ctx)
    {
        Vec2 pos = spider.Position;
        double ppu = spider.PixelsPerUnit;

        _clock += dt;
        if (_dodgeCooldown > 0.0)
            _dodgeCooldown -= dt;
        _motion.Update(_clock, snapshot.Windows);

        DetectChanges(spider, snapshot);

        OnDesktop = true;
        foreach (DesktopWindow w in snapshot.Windows)
        {
            if (w.Bounds.Contains(pos))
            {
                OnDesktop = false;
                break;
            }
        }

        bool changeActive = _changeLeft > 0.0;
        if (changeActive)
            _changeLeft -= dt;

        if (_ownIconLeft > 0.0)
            _ownIconLeft -= dt;
        
        bool dropActive = _dropLeft > 0.0;
        if (dropActive)
            _dropLeft -= dt;

        // ---- the priority list: the first state that applies wins
        SpiderState next;
        Vec2? look = null;
        Vec2? reach = null;

        if (ctx.Held)
        {
            next = SpiderState.Held;
        }
        else if (ctx.Carrying)
        {
            next = SpiderState.Carrying;
        }
        else if (ctx.HoverPoint is Vec2 hover)
        {
            next = SpiderState.Reaching;
            look = hover;
            reach = hover;
        }
        else if (spider.IsLeaping || TryDodge(spider, snapshot))
        {
            next = SpiderState.Leaping;
            look = spider.LeapLanding;
        }
        else if (dropActive && (_dropAt - pos).Length >= MinDistance)
        {
            next = SpiderState.Dropping;
            look = _dropAt;
        }
        else if (changeActive && ReactToChanges && _change is not null && (ChangeTarget(_change, snapshot) - pos).Length >= MinDistance)
        {
            next = SpiderState.Startled;
            look = ChangeTarget(_change, snapshot);
        }
        else if (TextTarget(pos, ppu, snapshot) is Vec2 textAt)
        {
            next = SpiderState.LookingAtText;
            look = textAt;
        }
        else
        {
            // nearest of: icon (desktop only), window (any)
            DesktopIcon? icon = LookAtIcons && OnDesktop ? NearestIcon(pos, ppu, snapshot.Icons) : null;
            double iconD = icon is null ? double.MaxValue : (icon.Center - pos).Length;

            DesktopWindow? window = LookAtWindows ? NearestWindow(pos, ppu, snapshot.Windows) : null;
            double windowD = double.MaxValue;
            Vec2 windowTarget = default;
            if (window is not null)
            {
                windowTarget = ClosestPoint(window.Bounds, pos);
                windowD = (windowTarget - pos).Length;
            }

            if (icon is not null && iconD <= windowD)
            {
                next = SpiderState.LookingAtIcon;
                Attention = icon;
                AttentionWindow = null;
                look = icon.Center;
            }
            else if (window is not null)
            {
                next = SpiderState.LookingAtWindow;
                Attention = null;
                AttentionWindow = window;
                look = windowTarget;
            }
            else
            {
                next = SpiderState.Watching;
                look = snapshot.Cursor;
            }
        }

        // the "near" targets keep their hysteresis memory only while they are the active state
        if (next != SpiderState.LookingAtIcon) Attention = null;
        if (next != SpiderState.LookingAtWindow) AttentionWindow = null;

        Enter(next);
        TimeInState += dt;

        ReachPoint = reach;
        if (look is Vec2 l)
            FocusPoint = l;
        return look;
    }

    private void Enter(SpiderState next)
    {
        if (next == State) return;
        SpiderState old = State;
        State = next;
        TimeInState = 0.0;
        StateChanged?.Invoke(old, next);
    }

    // ------------------------------------------------------------------ typing and approaching windows

    private Vec2? TextTarget(Vec2 pos, double ppu, DesktopSnapshot snapshot)
    {
        if (!LookAtText || !snapshot.Typing || snapshot.Caret is not Vec2 caret)
            return null;

        double limit = TextAttentionRadiusUnits * ppu * (State == SpiderState.LookingAtText ? ReleaseFactor : 1.0);
        double d = (caret - pos).Length;
        return d >= MinDistance && d <= limit ? caret : null;
    }

    // A window that heads straight for the spider, fast, and is nearly there: it hops back a little. The hop is
    // short on purpose, so a window that really is quick still catches up with it.
    private bool TryDodge(Spider spider, DesktopSnapshot snapshot)
    {
        if (!DodgeWindows || _dodgeCooldown > 0.0)
            return false;

        Vec2 pos = spider.Position;
        double range = DodgeRangeUnits * spider.PixelsPerUnit;

        foreach (DesktopWindow w in snapshot.Windows)
        {
            if (w.Id == 0 || w.Bounds.Contains(pos))
                continue;   // not a window, or the spider is already on it

            Vec2 v = _motion.VelocityOf(w.Id, _clock);
            double speed = v.Length;
            if (speed < DodgeMinSpeed)
                continue;

            Vec2 away = pos - ClosestPoint(w.Bounds, pos);
            double d = away.Length;
            if (d < 1.0 || d > range)
                continue;

            Vec2 n = away * (1.0 / d);                  // from the window's nearest edge to the spider
            double closing = v.X * n.X + v.Y * n.Y;     // how fast that edge approaches
            if (closing < speed * 0.6 || d / closing > DodgeLookAheadSeconds)
                continue;

            spider.Leap(n, Math.Clamp(closing * DodgeShove, 18.0, 55.0), 0.22);
            _dodgeCooldown = DodgeCooldownSeconds;
            Dodged?.Invoke();
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ nearby things

    private DesktopIcon? NearestIcon(Vec2 pos, double ppu, IReadOnlyList<DesktopIcon>? icons)
    {
        if (icons is null) return null;

        double enter = AttentionRadiusUnits * ppu;
        double leave = enter * ReleaseFactor;
        int currentIndex = Attention?.Index ?? -1;

        DesktopIcon? best = null;
        double bestD = double.MaxValue;
        foreach (DesktopIcon icon in icons)
        {
            double d = (icon.Center - pos).Length;
            double limit = icon.Index == currentIndex ? leave : enter;
            if (d < MinDistance || d > limit || d >= bestD)
                continue;
            best = icon;
            bestD = d;
        }

        return best;
    }

    // Windows the spider is standing on (containing its position) are skipped: it is on them, not near them.
    private DesktopWindow? NearestWindow(Vec2 pos, double ppu, IReadOnlyList<DesktopWindow> windows)
    {
        double enter = WindowAttentionRadiusUnits * ppu;
        double leave = enter * ReleaseFactor;
        long currentId = AttentionWindow?.Id ?? -1;

        DesktopWindow? best = null;
        double bestD = double.MaxValue;
        foreach (DesktopWindow w in windows)
        {
            if (w.Bounds.Contains(pos))
                continue;

            Vec2 p = ClosestPoint(w.Bounds, pos);
            double d = (p - pos).Length;
            double limit = w.Id == currentId ? leave : enter;
            if (d < MinDistance || d > limit || d >= bestD)
                continue;
            best = w;
            bestD = d;
        }

        return best;
    }

    private static Vec2 ClosestPoint(RectD r, Vec2 p) =>
        new(Math.Clamp(p.X, r.X, r.Right), Math.Clamp(p.Y, r.Y, r.Bottom));

    // ------------------------------------------------------------------ sudden changes

    private static Vec2 CenterOf(RectD r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static Vec2 ChangeTarget(Change c, DesktopSnapshot snapshot)
    {
        switch (c.Kind)
        {
            case ChangeKind.WindowOpened:
                // follow the window while it settles into place (opening animations, snapping)
                foreach (DesktopWindow w in snapshot.Windows)
                    if (w.Id == c.Id)
                        return CenterOf(w.Bounds);
                break;
            case ChangeKind.IconMoved:
            case ChangeKind.IconAdded:
                if (snapshot.Icons is not null)
                    foreach (DesktopIcon icon in snapshot.Icons)
                        if (icon.Index == c.Id)
                            return icon.Center;
                break;
        }

        return c.LastKnown;
    }

    private void StartChange(ChangeKind kind, long id, Vec2 where)
    {
        if (!ReactToChanges) return;
        _change = new Change(kind, id, where);
        _changeLeft = ChangeSeconds;
    }

    // Compares this frame's lists with the previous ones. Lists are only diffed when the source handed over a new
    // instance, and the very first list is only remembered (otherwise everything would be "new" at startup).
    // Icons have no stable identity (the index shifts when one is deleted), so they are matched by position. What
    // stayed put is ignored. What is left over on only one side was removed or added; a leftover on both sides is
    // an icon that moved. The change closest to the spider is the one it reacts to.
    private void DetectIconChanges(Spider spider, IReadOnlyList<DesktopIcon> icons)
    {
        var gone = new List<Vec2>(_iconsPrev);
        var fresh = new List<DesktopIcon>();
        foreach (DesktopIcon icon in icons)
        {
            Vec2 centre = icon.Center;
            int match = gone.FindIndex(c => (c - centre).Length < IconMoveThreshold);
            if (match >= 0)
                gone.RemoveAt(match);
            else
                fresh.Add(icon);
        }

        bool found = false;
        ChangeKind bestKind = ChangeKind.IconMoved;
        long bestId = 0;
        Vec2 bestAt = default;
        double bestD = double.MaxValue;

        void Consider(ChangeKind kind, long id, Vec2 at)
        {
            double d = (at - spider.Position).Length;
            if (d >= bestD) return;
            found = true;
            bestKind = kind;
            bestId = id;
            bestAt = at;
            bestD = d;
        }

        foreach (DesktopIcon icon in fresh)
        {
            // the nearest leftover old position is where this icon came from
            int from = -1;
            double fromD = double.MaxValue;
            for (int i = 0; i < gone.Count; i++)
            {
                double d = (gone[i] - icon.Center).Length;
                if (d < fromD)
                {
                    fromD = d;
                    from = i;
                }
            }

            if (from >= 0)
                gone.RemoveAt(from);

            if (icon.Index == _ownIcon && _ownIconLeft > 0.0)
                continue;   // the spider moved this one itself

            Consider(from >= 0 ? ChangeKind.IconMoved : ChangeKind.IconAdded, icon.Index, icon.Center);
        }

        foreach (Vec2 spot in gone)
            Consider(ChangeKind.IconRemoved, 0, spot);

        if (found)
            StartChange(bestKind, bestId, bestAt);
    }

    private void DetectChanges(Spider spider, DesktopSnapshot snapshot)
    {
        if (!ReferenceEquals(snapshot.Windows, _windowsRef))
        {
            _windowsRef = snapshot.Windows;

            _windowsNow.Clear();
            foreach (DesktopWindow w in snapshot.Windows)
                if (w.Id != 0)
                    _windowsNow[w.Id] = w.Bounds;

            if (_windowsPrimed)
            {
                // closed first, so that when both happen in one frame "opened" wins
                foreach ((long id, RectD bounds) in _windowsPrev)
                    if (!_windowsNow.ContainsKey(id))
                        StartChange(ChangeKind.WindowClosed, id, CenterOf(bounds));

                foreach (DesktopWindow w in snapshot.Windows)
                    if (w.Id != 0 && !_windowsPrev.ContainsKey(w.Id))
                        StartChange(ChangeKind.WindowOpened, w.Id, CenterOf(w.Bounds));
            }

            _windowsPrev.Clear();
            foreach ((long id, RectD bounds) in _windowsNow)
                _windowsPrev[id] = bounds;
            _windowsPrimed = true;
        }

        IReadOnlyList<DesktopIcon>? icons = snapshot.Icons;
        if (icons is not null && !ReferenceEquals(icons, _iconsRef))
        {
            _iconsRef = icons;

            // An empty list means "icons hidden or explorer not reachable": remember it, but do not announce
            // every icon as new when they come back.
            if (_iconsPrimed && _iconsPrev.Count > 0 && icons.Count > 0)
                DetectIconChanges(spider, icons);

            _iconsPrev.Clear();
            foreach (DesktopIcon icon in icons)
                _iconsPrev.Add(icon.Center);
            _iconsPrimed = true;
        }
    }
}
