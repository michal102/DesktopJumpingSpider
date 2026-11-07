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

    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.Parse("#E6E8EB"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#D9DCE1")), 1);
    private static readonly IBrush WindowBodyBrush = new SolidColorBrush(Color.Parse("#FAFBFC"));
    private static readonly IBrush TitleBarBrush = new SolidColorBrush(Color.Parse("#CDD2DA"));
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#98A0AD"));
    private static readonly IPen WindowPen = new Pen(new SolidColorBrush(Color.Parse("#AEB5C0")), 1);
    private static readonly IPen AimPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 40, 40, 40)), 1);
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#D32F2F")), 1.5);

    private readonly HashSet<Key> _keys = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private double _lastTime;
    private double _baseSpeed = 180.0;
    private double _fps;
    
    private bool _isDragging;
    private bool _showCursorOverlay = true;

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

        if (distance > 55)
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

            _isPointerOverSpider = (dx * dx + dy * dy) <= (55 * 55);
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

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnTick);
    }

    public Spider Spider { get; }

    public MockDesktop Desktop { get; } = new();

    public double Fps => _fps;

    /// <summary>Raised once per simulation frame, after the spider has been updated.</summary>
    public event Action? Frame;

    public void SetMouse(Vec2 position) => Desktop.Cursor = position;

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

        ApplyInput();
        Spider.Update(dt);
        KeepInside();

        Frame?.Invoke();
        InvalidateVisual();
    }

    private void ApplyInput()
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

        Spider.LookTarget = Desktop.GetSnapshot().Cursor;
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
        context.FillRectangle(BackdropBrush, area);

        for (double x = 0; x < area.Width; x += 80)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, area.Height));
        for (double y = 0; y < area.Height; y += 80)
            context.DrawLine(GridPen, new Point(0, y), new Point(area.Width, y));

        foreach (DesktopWindow w in Desktop.Windows)
            DrawMockWindow(context, w);

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

        SpiderRenderer.Draw(context, Spider);

        if (ShowCursorOverlay)
        {
            DrawCursor(context, cursor);
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
