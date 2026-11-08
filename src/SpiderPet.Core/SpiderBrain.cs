namespace SpiderPet.Core;

/// <summary>What the spider is currently paying attention to.</summary>
public enum FocusKind
{
    /// <summary>The mouse cursor (the default).</summary>
    Cursor,
    /// <summary>A desktop icon close to the spider.</summary>
    Icon,
    /// <summary>A window close to the spider.</summary>
    Window,
    /// <summary>A sudden change: a window opened or closed, an icon moved or appeared.</summary>
    Change,
}

/// <summary>
/// The spider's decision layer. For now it only decides <b>where to look</b>, in this order of priority:
///
///  1. a sudden change (window opened / closed, desktop icon moved or added) - attention for
///     <see cref="ChangeSeconds"/>, wherever on the screen it happened;
///  2. whatever is closest and near enough: a desktop icon (only while the spider stands on the bare desktop)
///     or a window the spider is not standing on;
///  3. the cursor.
///
/// Call <see cref="Update"/> once per frame and assign the result to <see cref="Spider.LookTarget"/>.
/// </summary>
public sealed class SpiderBrain
{
    private enum ChangeKind { WindowOpened, WindowClosed, IconMoved, IconAdded }

    private sealed record Change(ChangeKind Kind, long Id, Vec2 LastKnown);

    private readonly Dictionary<long, RectD> _windowsPrev = new();
    private Dictionary<long, RectD> _windowsNow = new();
    private readonly Dictionary<int, Vec2> _iconsPrev = new();
    private IReadOnlyList<DesktopWindow>? _windowsRef;
    private IReadOnlyList<DesktopIcon>? _iconsRef;
    private bool _windowsPrimed;
    private bool _iconsPrimed;

    private Change? _change;
    private double _changeLeft;

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

    /// <summary>An icon that moved at least this many pixels counts as "moved".</summary>
    public double IconMoveThreshold { get; set; } = 4.0;

    public bool LookAtIcons { get; set; } = true;
    public bool LookAtWindows { get; set; } = true;
    public bool ReactToChanges { get; set; } = true;

    // ------------------------------------------------------------------ state, for debug drawing and the HUD

    /// <summary>The icon the spider is currently looking at because it is near, or null.</summary>
    public DesktopIcon? Attention { get; private set; }

    /// <summary>The window the spider is currently looking at because it is near, or null.</summary>
    public DesktopWindow? AttentionWindow { get; private set; }

    /// <summary>True when no window covers the spider, i.e. it is standing on the desktop.</summary>
    public bool OnDesktop { get; private set; }

    public FocusKind Focus { get; private set; }

    /// <summary>The point being looked at when <see cref="Focus"/> is not <see cref="FocusKind.Cursor"/>.</summary>
    public Vec2 FocusPoint { get; private set; }

    /// <summary>Short description of the current focus for the HUD.</summary>
    public string FocusLabel => Focus switch
    {
        FocusKind.Icon => $"icon #{Attention?.Index}",
        FocusKind.Window => $"window \"{AttentionWindow?.Title}\"",
        FocusKind.Change => _change?.Kind switch
        {
            ChangeKind.WindowOpened => "change: window opened",
            ChangeKind.WindowClosed => "change: window closed",
            ChangeKind.IconMoved => "change: icon moved",
            ChangeKind.IconAdded => "change: icon added",
            _ => "change",
        },
        _ => "cursor",
    };

    // ------------------------------------------------------------------ per frame

    /// <summary>Returns the point the spider should look at.</summary>
    /// <param name="dt">Seconds since the previous call.</param>
    public Vec2 Update(double dt, Spider spider, DesktopSnapshot snapshot)
    {
        Vec2 pos = spider.Position;
        double ppu = spider.PixelsPerUnit;

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

        // 1) sudden change
        bool changeActive = _changeLeft > 0.0;
        if (changeActive)
            _changeLeft -= dt;

        if (changeActive && ReactToChanges && _change is not null)
        {
            Vec2 target = ChangeTarget(_change, snapshot);
            if ((target - pos).Length >= MinDistance)
            {
                Attention = null;
                AttentionWindow = null;
                Focus = FocusKind.Change;
                FocusPoint = target;
                return target;
            }
        }

        // 2) nearest of: icon (desktop only), window (any)
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
            Attention = icon;
            AttentionWindow = null;
            Focus = FocusKind.Icon;
            FocusPoint = icon.Center;
            return icon.Center;
        }

        if (window is not null)
        {
            Attention = null;
            AttentionWindow = window;
            Focus = FocusKind.Window;
            FocusPoint = windowTarget;
            return windowTarget;
        }

        // 3) default
        Attention = null;
        AttentionWindow = null;
        Focus = FocusKind.Cursor;
        FocusPoint = snapshot.Cursor;
        return snapshot.Cursor;
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
            {
                DesktopIcon? changed = null;
                ChangeKind kind = ChangeKind.IconMoved;
                double bestD = double.MaxValue;

                foreach (DesktopIcon icon in icons)
                {
                    bool isNew = !_iconsPrev.TryGetValue(icon.Index, out Vec2 old);
                    if (!isNew && (icon.Center - old).Length < IconMoveThreshold)
                        continue;

                    double d = (icon.Center - spider.Position).Length;
                    if (d < bestD)
                    {
                        bestD = d;
                        changed = icon;
                        kind = isNew ? ChangeKind.IconAdded : ChangeKind.IconMoved;
                    }
                }

                if (changed is not null)
                    StartChange(kind, changed.Index, changed.Center);
            }

            _iconsPrev.Clear();
            foreach (DesktopIcon icon in icons)
                _iconsPrev[icon.Index] = icon.Center;
            _iconsPrimed = true;
        }
    }
}
