using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private static readonly IBrush GlowBrush = new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(55, 235, 240, 255), 0.0),
            new GradientStop(Color.FromArgb(25, 235, 240, 255), 0.1),
            new GradientStop(Color.FromArgb(0, 235, 240, 255), 0.5),
        },
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

        _isDragging = true;
        Spider.IsGrabbed = true;

// Stop the spider from turning towards the cursor while held.
        Spider.LookTarget = null;

        _dragOffset = new Vec2(
            Spider.Position.X - mouse.X,
            Spider.Position.Y - mouse.Y
        );

        _lastMousePosition = mouse;

        Spider.Velocity = new Vec2(0, 0);
        Spider.MoveInput = new Vec2(0, 0);

        e.Pointer.Capture(this);

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);
        Vec2 mouse = new(point.X, point.Y);

        SetMouse(mouse);

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
        AddHandler(DragDrop.DropEvent, OnDrop);

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

    /// <summary>Bring dropped items to the cursor like a gift instead of laying them down near the spider.</summary>
    public bool CarryToCursor { get; set; }

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

        DesktopSnapshot snapshot = _watcher.Poll();
        Vec2 carry = _rider.Update(dt, Spider.Position, snapshot.Windows, _isDragging);
        if (carry != Vec2.Zero)
            Spider.Translate(carry);

        ApplyInput(dt);

        if (_isDragging && _carry.Busy)
            _carry.Cancel();
        _carry.Update(dt, Spider);

        Spider.Update(dt);
        KeepInside();

        Frame?.Invoke();
        InvalidateVisual();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        Point p = e.GetPosition(this);
        e.DragEffects = _spiderVisible && IsOverSpider(new Vec2(p.X, p.Y)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Copy, never Move: the source must not think we took the file, so nothing gets deleted or moved on disk.
    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        Point p = e.GetPosition(this);
        var at = new Vec2(p.X, p.Y);
        if (!_spiderVisible || !IsOverSpider(at))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;

        Vec2 destination = SpiderCarry.PickDestination(
            Spider, _watcher.Current, new RectD(0, 0, Bounds.Width, Bounds.Height), _rng, CarryToCursor);
        _carry.Start(at, destination);
    }

    private void ApplyInput(double dt)
    {
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

        Spider.LookTarget = _brain.Update(dt, Spider, _watcher.Current);
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

            if (_spiderVisible) { DrawGlow(context); SpiderRenderer.Draw(context, Spider); }
            DrawCarriedItem(context);
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

        DrawGlow(context);
        SpiderRenderer.Draw(context, Spider);
        DrawCarriedItem(context);

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

    // The thing the spider was handed: a generic sheet of paper, drawn upright and rotated with the carry angle.
    private void DrawCarriedItem(DrawingContext context)
    {
        if (!_carry.HasItem || _carry.ItemAlpha <= 0.0) return;

        double u = Spider.PixelsPerUnit;
        double w = 8.0 * u * _carry.ItemScale;
        double h = 10.0 * u * _carry.ItemScale;
        double fold = 2.6 * u * _carry.ItemScale;
        Vec2 at = _carry.ItemPosition;

        using (context.PushOpacity(_carry.ItemAlpha))
        using (context.PushTransform(
                   Matrix.CreateRotation(_carry.ItemAngleDeg * Math.PI / 180.0) *
                   Matrix.CreateTranslation(at.X, at.Y)))
        {
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
    }

    private void DrawGlow(DrawingContext context)
    {
        if (!ShowGlow) return;
        double r = 10.0 * Spider.PixelsPerUnit;
        context.DrawEllipse(GlowBrush, null, new Point(Spider.Position.X, Spider.Position.Y), r, r);
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
