using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>
/// Owns the spider and the mock desktop, runs the simulation loop and paints everything.
/// Input arrives through <see cref="SetKey"/> and <see cref="SetMouse"/> (called by the window).
/// </summary>
public sealed class SandboxView : Control
{
    private const double SprintFactor = 2.5;
    private const double WallMargin = 40.0;
    private const double MinSpeed = 20.0;
    private const double MaxSpeed = 1200.0;
    private const double GrabRadius = 55.0;

    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.Parse("#E6E8EB"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#D9DCE1")), 1);
    private static readonly IBrush WindowBodyBrush = new SolidColorBrush(Color.Parse("#FAFBFC"));
    private static readonly IBrush TitleBarBrush = new SolidColorBrush(Color.Parse("#CDD2DA"));
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#98A0AD"));
    private static readonly IPen WindowPen = new Pen(new SolidColorBrush(Color.Parse("#AEB5C0")), 1);
    private static readonly IPen AimPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 40, 40, 40)), 1);
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#D32F2F")), 1.5);
    // Rim light: wide, faint strokes of a cool tint around the spider's silhouette, widest first.
    private static readonly IPen[] RimPens =
    {
        /*new Pen(new SolidColorBrush(Color.FromArgb(10, 150, 180, 255)), 26) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
        new Pen(new SolidColorBrush(Color.FromArgb(14, 160, 190, 255)), 16) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
        new Pen(new SolidColorBrush(Color.FromArgb(20, 175, 200, 255)), 8) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
        new Pen(new SolidColorBrush(Color.FromArgb(26, 190, 210, 255)), 3) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },*/
        new Pen(new SolidColorBrush(Color.FromArgb(3,  160, 190, 255)), 5) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
        new Pen(new SolidColorBrush(Color.FromArgb(5,  170, 198, 255)), 3)  { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
        new Pen(new SolidColorBrush(Color.FromArgb(9, 185, 208, 255)), 2)  { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round },
    };
    private static readonly IPen DebugWindowPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 80, 80)), 1.5);
    private static readonly IPen IconMarkPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 0, 200, 255)), 1.5);
    private static readonly IPen IconAttentionPen = new Pen(new SolidColorBrush(Color.FromArgb(240, 255, 190, 0)), 2.5);
    private static readonly IPen ChangeRingPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 60, 60)), 3);
    private static readonly IPen IconLinkPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 190, 0)), 1);
    private static readonly IPen DebugPerchPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 190, 0)), 3);

    private readonly HashSet<Key> _keys = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private double _lastTime;
    private double _baseSpeed = 180.0;
    private double _fps;
    
    private bool _isDragging;
    private bool _showCursorOverlay = true;
    private bool _overlay;
    private DesktopWatcher _watcher;
    private readonly WindowRider _rider = new();
    private readonly SpiderBrain _brain = new();
    private readonly SpiderCarry _carry = new();
    private readonly Random _rng = new();

    private Vec2 _dragOffset;
    private Vec2 _lastMousePosition;

    public bool ShowCursorOverlay
    {
        get => _showCursorOverlay;
        set
        {
            _showCursorOverlay = value;
            Cursor = value
                ? new Cursor(StandardCursorType.None)
                : new Cursor(StandardCursorType.Arrow);

            InvalidateVisual();
        }
    }
    
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            var at = new Vec2(point.Position.X, point.Position.Y);
            if (!_isDragging && IsOverSpider(at))
            {
                SpiderMenuRequested?.Invoke(at);
                e.Handled = true;
            }
            return;
        }

        // Double click somewhere while the spider is carrying something: take it exactly there.
        if (point.Properties.IsLeftButtonPressed && e.ClickCount >= 2 && _carry.Busy)
        {
            var target = new Vec2(point.Position.X, point.Position.Y);
            if (!IsOverSpider(target) && _carry.Redirect(target))
            {
                e.Handled = true;
                return;
            }
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        Vec2 mouse = new(
            point.Position.X,
            point.Position.Y
        );

        // Check whether the mouse is over the spider.
        double dx = mouse.X - Spider.Position.X;
        double dy = mouse.Y - Spider.Position.Y;

        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > GrabRadius)
            return;

        // A press starts out as a tap. Released without moving it pokes the spider (it hops away from the mouse,
        // or lets go of what it holds); moved further than TapSlop it picks the spider up instead.
        _tapDown = true;
        _tapStart = mouse;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private int _pokes;
    private double _lastPoke = -10.0;

    // The spider was clicked: it hops away from the mouse and reacts. Poking it again and again makes it cross.
    private void Poke(Vec2 mouse)
    {
        if (!_spiderVisible) return;

        double now = _clock.Elapsed.TotalSeconds;
        _pokes = now - _lastPoke < 3.0 ? _pokes + 1 : 1;
        _lastPoke = now;

        double distance = 50.0 * Spider.PixelsPerUnit;
        Spider.Leap(PickLeapDirection(mouse, distance), distance);
        ShowEmote(_pokes >= 3 ? "💢" : _pokes == 2 ? "💦" : "❗", 1.2);
    }

    // Away from the mouse, with a little randomness, turned aside if the landing would be off the screen.
    private Vec2 PickLeapDirection(Vec2 mouse, double distance)
    {
        Vec2 from = Spider.Position;
        Vec2 away = from - mouse;
        away = away.Length < 2.0
            ? new Vec2(0.0, -1.0).Rotated(_rng.NextDouble() * 360.0)
            : away.Normalized().Rotated((_rng.NextDouble() - 0.5) * 24.0);

        double sign = _rng.Next(2) == 0 ? 1.0 : -1.0;
        const double margin = 40.0;
        foreach (double turn in new[] { 0.0, 30.0, -30.0, 60.0, -60.0, 90.0, -90.0, 135.0, -135.0, 180.0 })
        {
            Vec2 dir = away.Rotated(turn * sign);
            Vec2 land = from + dir * distance;
            if (land.X >= margin && land.X <= Bounds.Width - margin && land.Y >= margin && land.Y <= Bounds.Height - margin)
                return dir;
        }

        var centre = new Vec2(Bounds.Width / 2.0, Bounds.Height / 2.0);
        return (centre - from).Length < 1.0 ? away : (centre - from).Normalized();
    }

    private Vec2 _lastCursor;
    private double _lastActivity;
    private bool _tapDown;
    private Vec2 _tapStart;
    private const double TapSlop = 7.0;

    private void BeginDrag(Vec2 mouse)
    {
        _isDragging = true;
        Spider.IsGrabbed = true;
        Spider.CancelLeap();

// Stop the spider from turning towards the cursor while held.
        Spider.LookTarget = null;

        _dragOffset = new Vec2(
            Spider.Position.X - mouse.X,
            Spider.Position.Y - mouse.Y
        );

        _lastMousePosition = mouse;

        Spider.Velocity = new Vec2(0, 0);
        Spider.MoveInput = new Vec2(0, 0);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        Vec2 mouse = new(point.X, point.Y);

        SetMouse(mouse);

        if (_tapDown && (mouse - _tapStart).Length > TapSlop)
        {
            // it was a drag after all: pick the spider up (the item it holds is dropped by the tick)
            _tapDown = false;
            BeginDrag(mouse);
        }

        if (_isDragging)
        {
            // Move the spider with the mouse, preserving the grab position.
            Spider.Position = new Vec2(
                mouse.X + _dragOffset.X,
                mouse.Y + _dragOffset.Y
            );

            // Prevent unwanted movement while being held.
            Spider.Velocity = new Vec2(0, 0);
            Spider.MoveInput = new Vec2(0, 0);

            _lastMousePosition = mouse;
            InvalidateVisual();

            e.Handled = true;
        }
        else
        {
            // Check whether the mouse is hovering over the spider.
            double dx = mouse.X - Spider.Position.X;
            double dy = mouse.Y - Spider.Position.Y;

            _isPointerOverSpider = (dx * dx + dy * dy) <= (GrabRadius * GrabRadius);
        }

        // Choose the appropriate cursor:
        // grabbing, hovering over spider, or normal arrow.
        UpdatePointerCursor();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_tapDown)
        {
            _tapDown = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (_carry.Drop())
            {
                _brain.NotifyDropped(_carry.ItemPosition);
                ShowEmote("💦", 1.4);
            }
            else
            {
                Poke(_tapStart);
            }
            return;
        }

        if (!_isDragging)
            return;

        _isDragging = false;
        Spider.IsGrabbed = false;

        Spider.MoveInput = new Vec2(0, 0);

        e.Pointer.Capture(null);

        e.Handled = true;
    }

    public SandboxView()
    {
        Spider = new Spider(new Vec2(640, 640), 0.0);
        Spider.Speed = _baseSpeed;

        // the OS cursor is hidden; the mock cursor below is drawn instead
        Cursor = new Cursor(StandardCursorType.None);
        ClipToBounds = true;

        _watcher = new DesktopWatcher(Desktop);
        HookWatcher();

        // Files (or anything else) dragged onto the spider. Only the spider itself accepts a drop.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _carry.Placed += OnCarryPlaced;
        _brain.StateChanged += (_, now) =>
        {
            if (now == SpiderState.Startled) ShowEmote("❗", 1.0);
            else if (now == SpiderState.Reaching) ShowEmote("✨", 1.4);
        };
        _brain.Dodged += () => ShowEmote("💦", 1.0);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnTick);
    }

    public Spider Spider { get; }

    public MockDesktop Desktop { get; } = new();

    public double Fps => _fps;

    /// <summary>Window and cursor tracking. In the sandbox it watches <see cref="Desktop"/>; in overlay mode the real desktop.</summary>
    public DesktopWatcher Watcher => _watcher;

    /// <summary>The window the spider is currently standing on (it is carried along when that window moves), or null.</summary>
    public DesktopWindow? Perch => _rider.Riding;

    /// <summary>Human-readable description of the last window event, for the HUD.</summary>
    public string LastEvent { get; private set; } = "-";

    /// <summary>Draw outlines of the tracked windows (overlay mode only).</summary>
    public bool ShowDebug { get; set; } = true;

    public bool IsDragging => _isDragging;

    /// <summary>Decides where the spider looks (cursor, or a nearby desktop icon).</summary>
    public SpiderBrain Brain => _brain;

    /// <summary>The "something was dropped on me" behaviour: grab it, carry it, set it down.</summary>
    public SpiderCarry Carry => _carry;

    /// <summary>What happens to a dropped file the spider likes (a disliked one is always dumped in the far corner).</summary>
    public DropMode DropStyle { get; set; } = DropMode.Nearby;

    /// <summary>The spider's own corner; rubbish goes to the opposite one.</summary>
    public SpiderCorner HoardCorner { get; set; } = SpiderCorner.TopRight;

    /// <summary>Lets the spider find and move real desktop icons (set by the overlay).</summary>
    public IDesktopIconControl? IconControl { get; set; }

    /// <summary>Outline every desktop icon the source reports.</summary>
    public bool ShowIconMarks { get; set; } = true;

    /// <summary>Soft light glow drawn under the spider.</summary>
    public bool ShowGlow { get; set; }
    
    private bool _spiderVisible = true;
    public bool SpiderVisible
    {
        get => _spiderVisible;
        set { _spiderVisible = value; InvalidateVisual(); }
    }

    /// <summary>Walking speed in px/s before sprint, clamped to the allowed range.</summary>
    public double BaseSpeed
    {
        get => _baseSpeed;
        set => _baseSpeed = Math.Clamp(value, MinSpeed, MaxSpeed);
    }

    /// <summary>Right button pressed on the spider; argument is the position in view coordinates.</summary>
    public event Action<Vec2>? SpiderMenuRequested;

    /// <summary>True when <paramref name="p"/> (view coordinates) is within grabbing distance of the spider.</summary>
    public bool IsOverSpider(Vec2 p)
    {
        if (!_spiderVisible) return false;
        
        double dx = p.X - Spider.Position.X;
        double dy = p.Y - Spider.Position.Y;
        return dx * dx + dy * dy <= GrabRadius * GrabRadius;
    }

    /// <summary>Raised at the start of every simulation frame, before the desktop is polled.</summary>
    public event Action? BeforeUpdate;

    /// <summary>
    /// Switches the view to transparent-overlay mode driven by a real desktop: no backdrop, no mock windows or
    /// mock cursor, the OS cursor is used, and coordinates come from <paramref name="source"/>
    /// (which must already be expressed in this view's coordinate space).
    /// </summary>
    public void UseOverlayDesktop(IDesktopSource source)
    {
        _overlay = true;
        _watcher = new DesktopWatcher(source);
        HookWatcher();
        ShowCursorOverlay = false;
    }

    private void HookWatcher()
    {
        _watcher.WindowAppeared += w => LastEvent = $"opened: {w.Title}";
        _watcher.WindowClosed += w => LastEvent = $"closed: {w.Title}";
        _watcher.WindowMoved += (before, now) =>
            LastEvent = (before.Bounds.Width == now.Bounds.Width && before.Bounds.Height == now.Bounds.Height
                ? "moved: " : "resized: ") + now.Title;
    }

    /// <summary>Raised once per simulation frame, after the spider has been updated.</summary>
    public event Action? Frame;

    public void SetMouse(Vec2 position)
    {
        if (!_overlay)
            Desktop.Cursor = position;
    }

    public void SetKey(Key key, bool down)
    {
        if (!down)
        {
            _keys.Remove(key);
            return;
        }

        _keys.Add(key);
        switch (key)
        {
            case Key.OemPlus:
            case Key.Add:
                _baseSpeed = Math.Min(MaxSpeed, _baseSpeed * 1.15);
                break;
            case Key.OemMinus:
            case Key.Subtract:
                _baseSpeed = Math.Max(MinSpeed, _baseSpeed / 1.15);
                break;
            case Key.C:
                ShowCursorOverlay = !ShowCursorOverlay;
                break;
        }
    }

    public void ReleaseAllKeys() => _keys.Clear();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastTime = _clock.Elapsed.TotalSeconds;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;
        if (dt <= 0.0) return;

        _fps = _fps <= 0.0 ? 1.0 / dt : _fps * 0.92 + (1.0 / dt) * 0.08;

        BeforeUpdate?.Invoke();
        if (!_spiderVisible) { Frame?.Invoke(); return; }

        // DragOver keeps coming while an item hovers; if it stops, the drag is over
        if (_hover is not null && now - _hoverStamp > 0.5)
            EndHover();

        DesktopSnapshot snapshot = _watcher.Poll();

        if (_emoteLeft > 0.0)
            _emoteLeft -= dt;
        if ((snapshot.Cursor - _lastCursor).Length > 3.0 || _carry.Busy || _isDragging || _keys.Count > 0)
        {
            _lastCursor = snapshot.Cursor;
            _lastActivity = now;
        }
        else if (now - _lastActivity > 40.0 && _emoteLeft <= 0.0)
        {
            ShowEmote("💤", 3.2);   // nobody around for a while: dozes off
        }
        Vec2 carry = _rider.Update(dt, Spider.Position, snapshot.Windows, _isDragging);
        if (carry != Vec2.Zero)
            Spider.Translate(carry);

        ApplyInput(dt);

        if (_isDragging && _carry.Busy)
            _carry.Cancel();
        _carry.Bounds = new RectD(0, 0, Bounds.Width, Bounds.Height);
        _carry.Update(dt, Spider, _watcher.Current.Cursor);

        Spider.Update(dt);
        KeepInside();

        Frame?.Invoke();
        InvalidateVisual();
    }

    // ---- an item dragged over the spider
    private Vec2? _hover;               // where the dragged item is, while it is over the spider
    private double _hoverStamp;         // clock time of the last DragOver
    private Bitmap? _hoverIcon;
    private bool _hoverRead;            // the item's picture was read for this drag

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        Point p = e.GetPosition(this);
        var at = new Vec2(p.X, p.Y);
        SetMouse(at);

        // Not while it holds something else: its mouth is full.
        bool accept = _spiderVisible && IsOverSpider(at) && !_carry.Busy;
        if (_spiderVisible && IsOverSpider(at) && _carry.Busy && !_isDragging && (_emote != "💢" || _emoteLeft < 0.3))
            ShowEmote("💢", 0.9);   // hands full

        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!accept)
        {
            EndHover();
            return;
        }

        if (!_hoverRead)
        {
            _hoverRead = true;
            (_, string? path) = ReadDroppedFile(e);
            _hoverIcon = path is not null ? FileIcons.TryLoad(path) : null;
        }

        _hover = at;
        _hoverStamp = _clock.Elapsed.TotalSeconds;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => EndHover();

    private void EndHover()
    {
        _hover = null;
        _hoverRead = false;
        _hoverIcon = null;
    }

    // Copy, never Move: the source must not think we took the file, so nothing gets deleted or moved on disk.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        Point p = e.GetPosition(this);
        var at = new Vec2(p.X, p.Y);
        EndHover();
        if (!_spiderVisible || !IsOverSpider(at) || _carry.Busy)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        (string name, string? path) = ReadDroppedFile(e);
        StartCarry(name, path, at);
    }

    private static (string Name, string? Path) ReadDroppedFile(DragEventArgs e)
    {
        try
        {
            IStorageItem[]? files = e.DataTransfer.TryGetFiles();
            if (files is { Length: > 0 })
            {
                IStorageItem item = files[0];
                string? path = item.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null;
                return (item.Name ?? "", path);
            }
        }
        catch (Exception)
        {
            // not a file drop, or the data could not be read: the spider simply takes "something"
        }

        return ("", null);
    }

    // The item the spider is handling right now: its picture and, when it is a desktop icon, which one.
    private Bitmap? _itemIcon;
    private int _itemIconIndex = -1;
    private bool _itemJunk;

    private void StartCarry(string name, string? path, Vec2 at)
    {
        FileJudgement judgement = name.Length > 0
            ? FilePolicy.Judge(name)
            : new FileJudgement(FileVerdict.Worthy, "Ooh, a mystery.");

        _itemIcon = path is not null ? FileIcons.TryLoad(path) : null;

        _itemIconIndex = -1;
        if (IconControl is not null && name.Length > 0)
        {
            string bare = System.IO.Path.GetFileNameWithoutExtension(name);
            _itemIconIndex = IconControl.FindIcon(bare.Length > 0 && bare != name ? new[] { name, bare } : new[] { name });
        }

        var area = new RectD(0, 0, Bounds.Width, Bounds.Height);
        IReadOnlyList<DesktopIcon>? icons = _watcher.Current.Icons;
        var options = new CarryOptions();
        Vec2 destination;

        if (judgement.Verdict == FileVerdict.Junk)
        {
            // far away from the spider's own corner, and held at arm's length
            destination = SpiderCarry.FindCornerSlot(area, SpiderCarry.Opposite(HoardCorner), icons, _itemIconIndex);
            options = options with { Disdain = true };
        }
        else if (DropStyle == DropMode.Hoard)
        {
            destination = SpiderCarry.FindCornerSlot(area, HoardCorner, icons, _itemIconIndex);
        }
        else if (DropStyle == DropMode.Cursor)
        {
            destination = _watcher.Current.Cursor;
            options = options with { BringToCursor = true };
        }
        else
        {
            destination = SpiderCarry.PickDestination(Spider, _watcher.Current, area, _rng, false);
        }

        _itemJunk = judgement.Verdict == FileVerdict.Junk;
        ShowEmote(_itemJunk ? "💢" : name.Length == 0 ? "❓" : "✨", 2.0);
        _carry.Start(at, destination, options);
    }

    // The item touched the ground: if it is a real desktop icon, move the real thing now.
    private void OnCarryPlaced(Vec2 where)
    {
        if (!_itemJunk)
            ShowEmote("💖", 2.0);

        int index = _itemIconIndex;
        _itemIconIndex = -1;
        if (index < 0 || IconControl is null)
            return;

        var target = new Vec2(
            Math.Clamp(where.X, 40.0, Math.Max(40.0, Bounds.Width - 40.0)),
            Math.Clamp(where.Y, 40.0, Math.Max(40.0, Bounds.Height - 100.0)));

        _brain.IgnoreIconMove(index);   // its own doing, so no "!"
        
        if (IconControl.MoveIcon(index, target))
            _carry.EndItem();   // the real icon is there now, so the ghost can go
    }

    private void ApplyInput(double dt)
    {
        // The brain (a state machine) decides who is in charge this frame.
        Vec2? look = _brain.Think(dt, Spider, _watcher.Current,
            new BrainContext(_isDragging, _carry.Busy, _hover));
        Spider.ReachTarget = _brain.ReachPoint;

        if (_isDragging)
        {
            Spider.MoveInput = new Vec2(0, 0);
            Spider.Velocity = new Vec2(0, 0);

            return;
        }

        double dx =
            (_keys.Contains(Key.Right) ? 1.0 : 0.0) -
            (_keys.Contains(Key.Left) ? 1.0 : 0.0);

        double dy =
            (_keys.Contains(Key.Down) ? 1.0 : 0.0) -
            (_keys.Contains(Key.Up) ? 1.0 : 0.0);

        bool sprint =
            _keys.Contains(Key.LeftShift) ||
            _keys.Contains(Key.RightShift);

        Spider.MoveInput = new Vec2(dx, dy);

        Spider.Speed = _baseSpeed *
                       (sprint ? SprintFactor : 1.0);

        // while carrying, the carry task sets the look target itself (after this, in the same tick)
        if (look is Vec2 l)
            Spider.LookTarget = l;
    }

    private void KeepInside()
    {
        if (Bounds.Width < 2 * WallMargin + 10 || Bounds.Height < 2 * WallMargin + 10) return;

        Vec2 p = Spider.Position;
        Vec2 v = Spider.Velocity;
        double maxX = Bounds.Width - WallMargin;
        double maxY = Bounds.Height - WallMargin;

        if (p.X < WallMargin) { p = new Vec2(WallMargin, p.Y); if (v.X < 0) v = new Vec2(0, v.Y); }
        if (p.X > maxX) { p = new Vec2(maxX, p.Y); if (v.X > 0) v = new Vec2(0, v.Y); }
        if (p.Y < WallMargin) { p = new Vec2(p.X, WallMargin); if (v.Y < 0) v = new Vec2(v.X, 0); }
        if (p.Y > maxY) { p = new Vec2(p.X, maxY); if (v.Y > 0) v = new Vec2(v.X, 0); }

        Spider.Position = p;
        Spider.Velocity = v;
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);

        if (_overlay)
        {
            // Fully transparent, but it must be drawn: Avalonia hit-tests custom controls against what they
            // draw, so without this the spider could only be grabbed by touching its exact outline.
            context.DrawRectangle(Brushes.Transparent, null, area);

            if (ShowDebug)
                DrawDebugWindows(context);
            if (ShowIconMarks)
                DrawIconMarks(context);
            if (ShowIconMarks || ShowDebug)
                DrawFocus(context);

            if (_spiderVisible) DrawSpider(context);
            DrawCarriedItem(context);
            DrawHoverItem(context);
            return;
        }

        context.FillRectangle(BackdropBrush, area);

        for (double x = 0; x < area.Width; x += 80)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, area.Height));
        for (double y = 0; y < area.Height; y += 80)
            context.DrawLine(GridPen, new Point(0, y), new Point(area.Width, y));

        foreach (DesktopWindow w in Desktop.Windows)
            DrawMockWindow(context, w);

        foreach (DesktopIcon icon in Desktop.Icons)
            context.DrawRectangle(DotBrush, null, new Rect(icon.Bounds.X, icon.Bounds.Y, icon.Bounds.Width, icon.Bounds.Height), 6, 6);
        if (ShowIconMarks)
            DrawIconMarks(context);
        if (ShowIconMarks || ShowDebug)
            DrawFocus(context);

        Vec2 cursor = Desktop.Cursor;
        Vec2 pos = Spider.Position;

        if (ShowCursorOverlay)
        {
            context.DrawLine(
                AimPen,
                new Point(pos.X, pos.Y),
                new Point(cursor.X, cursor.Y)
            );
        }

        DrawSpider(context);
        DrawCarriedItem(context);
        DrawHoverItem(context);

        if (ShowCursorOverlay)
        {
            DrawCursor(context, cursor);
        }
    }

    private static readonly IBrush ItemShadowBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
    private static readonly IBrush ItemPaperBrush = new SolidColorBrush(Color.Parse("#F3F5F9"));
    private static readonly IBrush ItemFoldBrush = new SolidColorBrush(Color.Parse("#C9CFDA"));
    private static readonly IBrush ItemLineBrush = new SolidColorBrush(Color.Parse("#9AA3B2"));
    private static readonly IPen ItemPen = new Pen(new SolidColorBrush(Color.Parse("#5A6270")), 1.2);

    // The thing the spider was handed: the file's own icon, or a plain sheet of paper when there is none.
    private void DrawCarriedItem(DrawingContext context)
    {
        DrawEmote(context);

        if (!_carry.HasItem || _carry.ItemAlpha <= 0.0) return;

        Vec2 at = _carry.ItemPosition;

        using (context.PushOpacity(_carry.ItemAlpha))
        using (context.PushTransform(
                   Matrix.CreateRotation(_carry.ItemAngleDeg * Math.PI / 180.0) *
                   Matrix.CreateTranslation(at.X, at.Y)))
        {
            DrawItemGlyph(context, _itemIcon, _carry.ItemScale);
        }
    }

    // The picture of an item around the origin of the current transform: its own icon, or a sheet of paper.
    private void DrawItemGlyph(DrawingContext context, Bitmap? icon, double scale)
    {
        double u = Spider.PixelsPerUnit;
        if (icon is not null)
        {
            double side = 12.0 * u * scale;
            context.DrawEllipse(ItemShadowBrush, null, new Point(2, side * 0.42), side * 0.5, side * 0.14);
            context.DrawImage(
                icon,
                new Rect(0, 0, icon.PixelSize.Width, icon.PixelSize.Height),
                new Rect(-side / 2, -side / 2, side, side));
            return;
        }

        double w = 8.0 * u * scale;
        double h = 10.0 * u * scale;
        double fold = 2.6 * u * scale;

        context.DrawEllipse(ItemShadowBrush, null, new Point(2, h * 0.45), w * 0.6, h * 0.18);

        var sheet = new StreamGeometry();
        using (StreamGeometryContext g = sheet.Open())
        {
            g.BeginFigure(new Point(-w / 2, -h / 2), true);
            g.LineTo(new Point(w / 2 - fold, -h / 2));
            g.LineTo(new Point(w / 2, -h / 2 + fold));
            g.LineTo(new Point(w / 2, h / 2));
            g.LineTo(new Point(-w / 2, h / 2));
            g.EndFigure(true);
        }
        context.DrawGeometry(ItemPaperBrush, ItemPen, sheet);

        var corner = new StreamGeometry();
        using (StreamGeometryContext g = corner.Open())
        {
            g.BeginFigure(new Point(w / 2 - fold, -h / 2), true);
            g.LineTo(new Point(w / 2 - fold, -h / 2 + fold));
            g.LineTo(new Point(w / 2, -h / 2 + fold));
            g.EndFigure(true);
        }
        context.DrawGeometry(ItemFoldBrush, ItemPen, corner);

        for (int i = 0; i < 3; i++)
        {
            double y = -h / 2 + fold + 0.9 * u + i * 1.5 * u;
            context.DrawLine(new Pen(ItemLineBrush, 1.4), new Point(-w / 2 + 1.2 * u, y), new Point(w / 2 - 1.2 * u - (i == 2 ? 1.5 * u : 0), y));
        }
    }

    // The item the user is dragging over the spider, drawn at the pointer (the spider reaches for it).
    private void DrawHoverItem(DrawingContext context)
    {
        if (_hover is not Vec2 at || !_spiderVisible) return;
        using (context.PushOpacity(0.95))
        using (context.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
            DrawItemGlyph(context, _hoverIcon, 1.0);
    }

    // ------------------------------------------------------------------ emotes (no speech, just a little icon above the spider)

    private string _emote = "";
    private double _emoteLeft;
    private double _emoteTotal = 1.0;
    private FormattedText? _emoteText;
    private string _emoteCached = "";

    private static readonly Typeface EmojiFace = new("Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, sans-serif");

    /// <summary>Makes the spider show an emotion: a small emoji that pops up above it and fades away.</summary>
    public void ShowEmote(string glyph, double seconds = 1.6)
    {
        _emote = glyph;
        _emoteTotal = Math.Max(0.3, seconds);
        _emoteLeft = _emoteTotal;
    }

    private void DrawEmote(DrawingContext context)
    {
        if (_emoteLeft <= 0.0 || _emote.Length == 0 || !_spiderVisible) return;

        if (_emoteText is null || _emote != _emoteCached)
        {
            _emoteCached = _emote;
            _emoteText = new FormattedText(
                _emote, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, EmojiFace, 30, Brushes.White);
        }

        double age = _emoteTotal - _emoteLeft;
        double life = age / _emoteTotal;

        // pops in with a little overshoot, fades out at the end
        double pop = 1.0;
        if (age < 0.25)
        {
            double t = age / 0.25 - 1.0;                       // ease-out-back: grows past 1 and settles
            pop = Math.Max(0.0, 1.0 + 2.70158 * t * t * t + 1.70158 * t * t);
        }
        double alpha = Math.Clamp(_emoteLeft / 0.35, 0.0, 1.0);

        double dx = 0.0, dy = 0.0;
        switch (_emote)
        {
            case "💢": dx = Math.Sin(age * 55.0) * 2.5; break;                         // seething
            case "💦": dy = age * 16.0; break;                                         // dripping
            case "💤": dy = -age * 14.0; dx = Math.Sin(age * 3.0) * 5.0; break;        // drifting up
            case "💖": dy = -age * 12.0 - Math.Abs(Math.Sin(age * 6.0)) * 3.0; break;  // floating hearts
            case "✨": dy = -Math.Sin(age * 5.0) * 3.0; dx = Math.Cos(age * 4.0) * 2.0; break;
            case "❗": dy = -Math.Abs(Math.Sin(age * 12.0)) * 7.0 * (1.0 - life); break; // jumps
            default: dy = Math.Sin(age * 5.0) * 2.5; break;
        }

        double u = Spider.PixelsPerUnit;
        Vec2 at = Spider.Position + new Vec2(7.0 * u + dx, -9.0 * u + dy);
        double w = _emoteText.Width, h = _emoteText.Height;

        using (context.PushOpacity(alpha))
        using (context.PushTransform(Matrix.CreateScale(pop, pop) * Matrix.CreateTranslation(at.X, at.Y)))
            context.DrawText(_emoteText, new Point(-w / 2.0, -h / 2.0));
    }

    // The spider itself. While it leaps it is "in the air": it swells, rises a little and leaves a shadow on the ground.
    private void DrawSpider(DrawingContext context)
    {
        double arc = Spider.LeapArc;
        if (arc <= 0.001)
        {
            DrawGlow(context);
            SpiderRenderer.Draw(context, Spider);
            return;
        }

        Vec2 p = Spider.Position;
        double u = Spider.PixelsPerUnit;
        using (context.PushOpacity(0.35 * (1.0 - 0.4 * arc)))
            context.DrawEllipse(Brushes.Black, null, new Point(p.X, p.Y + u), 7.0 * u * (1.0 - 0.2 * arc), 4.5 * u * (1.0 - 0.2 * arc));

        double s = 1.0 + 0.28 * arc;
        using (context.PushTransform(
                   Matrix.CreateTranslation(-p.X, -p.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(p.X, p.Y - 6.0 * u * arc)))
            SpiderRenderer.Draw(context, Spider);
    }

    private void DrawGlow(DrawingContext context)
    {
        if (!ShowGlow) return;

        // legs: from the IK outline polygons; body: from the real art, so the abdomen is lit too
        var geo = new StreamGeometry();
        using (StreamGeometryContext g = geo.Open())
        {
            foreach (SpiderLeg leg in Spider.Legs)
                AddPolygon(g, leg.Outline);
        }

        foreach (Pen pen in RimPens)
        {
            context.DrawGeometry(null, pen, geo);
            SpiderRenderer.DrawBodyRim(context, Spider, pen);
        }
    }

    private static void AddPolygon(StreamGeometryContext g, Vec2[] pts)
    {
        if (pts.Length < 3) return;
        g.BeginFigure(new Point(pts[0].X, pts[0].Y), false);
        for (int i = 1; i < pts.Length; i++)
            g.LineTo(new Point(pts[i].X, pts[i].Y));
        g.EndFigure(true);
    }

    private void DrawIconMarks(DrawingContext context)
    {
        IReadOnlyList<DesktopIcon>? icons = _watcher.Current.Icons;
        if (icons is null) return;

        DesktopIcon? attention = _brain.Attention;
        foreach (DesktopIcon icon in icons)
        {
            var r = new Rect(icon.Bounds.X, icon.Bounds.Y, icon.Bounds.Width, icon.Bounds.Height);
            bool looked = attention is not null && attention.Index == icon.Index;
            context.DrawRectangle(null, looked ? IconAttentionPen : IconMarkPen, r, 6, 6);
        }
    }

    // A line from the spider to whatever it is looking at when that is not the cursor; a ring for sudden changes.
    private void DrawFocus(DrawingContext context)
    {
        if (!_spiderVisible || _brain.Focus == FocusKind.Cursor) return;

        Vec2 f = _brain.FocusPoint;
        context.DrawLine(IconLinkPen, new Point(Spider.Position.X, Spider.Position.Y), new Point(f.X, f.Y));
        if (_brain.Focus == FocusKind.Change)
            context.DrawEllipse(null, ChangeRingPen, new Point(f.X, f.Y), 26, 26);
    }

    private void DrawDebugWindows(DrawingContext context)
    {
        long? perch = _rider.RidingId;
        foreach (DesktopWindow w in _watcher.Current.Windows)
        {
            var r = new Rect(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height);
            context.DrawRectangle(null, perch == w.Id ? DebugPerchPen : DebugWindowPen, r);
        }
    }

    private static void DrawMockWindow(DrawingContext ctx, DesktopWindow w)
    {
        var body = new Rect(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height);
        ctx.DrawRectangle(WindowBodyBrush, WindowPen, body);

        var title = new Rect(body.X, body.Y, body.Width, 28);
        ctx.DrawRectangle(TitleBarBrush, WindowPen, title);
        for (int i = 0; i < 3; i++)
            ctx.DrawEllipse(DotBrush, null, new Point(body.X + 16 + i * 18, body.Y + 14), 5, 5);
    }

    private static void DrawCursor(DrawingContext ctx, Vec2 at)
    {
        (double X, double Y)[] shape =
        {
            (0, 0), (0, 16), (4, 12.5), (7, 19), (9.5, 18), (6.5, 11.5), (11.5, 11.5),
        };

        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(at.X + shape[0].X, at.Y + shape[0].Y), true);
            for (int i = 1; i < shape.Length; i++)
                g.LineTo(new Point(at.X + shape[i].X, at.Y + shape[i].Y));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Brushes.White, CursorPen, geometry);
    }
    
    private static readonly Cursor ArrowCursor =
        new(StandardCursorType.Arrow);

    private static readonly Cursor HoverSpiderCursor =
        new(StandardCursorType.Hand);

    private static readonly Cursor GrabbedSpiderCursor =
        new(StandardCursorType.SizeAll);

    private bool _isPointerOverSpider;
    
    private void UpdatePointerCursor()
    {
        Cursor = _isDragging
            ? GrabbedSpiderCursor
            : _isPointerOverSpider
                ? HoverSpiderCursor
                : ArrowCursor;
    }
}
