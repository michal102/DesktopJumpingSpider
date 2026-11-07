using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpiderPet.Core;
using Avalonia.Platform;

namespace SpiderPet.Sandbox;

public sealed class MainWindow : Window
{
    private readonly SandboxView _view = new();
    private readonly TextBlock _hud;
    private int _frames;

    public MainWindow()
    {
        Title = "Jumping spider - sandbox";
        Width = 1280;
        Height = 800;
        MinWidth = 640;
        MinHeight = 420;
        Icon = new WindowIcon(
            AssetLoader.Open(new Uri("avares://SpiderPet.Sandbox/Assets/app-icon.ico"))
        );

        _hud = new TextBlock
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            Foreground = new SolidColorBrush(Color.Parse("#2A2F36")),
        };

        var root = new Grid();
        root.Children.Add(_view);
        root.Children.Add(_hud);
        Content = root;

        _view.Frame += OnFrame;
        Deactivated += (_, _) => _view.ReleaseAllKeys();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        Key mapped = MapMovementKey(e.Key);
        _view.SetKey(mapped, true);
        if (IsArrow(mapped)) e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Key mapped = MapMovementKey(e.Key);
        _view.SetKey(mapped, false);
        if (IsArrow(mapped)) e.Handled = true;
        base.OnKeyUp(e);
    }

    private static Key MapMovementKey(Key key) => key switch
    {
        Key.W => Key.Up,
        Key.S => Key.Down,
        Key.A => Key.Left,
        Key.D => Key.Right,
        _ => key
    };

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(_view);
        _view.SetMouse(new Vec2(p.X, p.Y));
    }

    private static bool IsArrow(Key key) =>
        key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right;

    private void OnFrame()
    {
        if (++_frames % 6 != 0) return;

        Spider spider = _view.Spider;
        double mouseAngle = MathUtil.To360(MathUtil.HeadingFromTo(spider.Position, _view.Desktop.Cursor));
        double heading = MathUtil.To360(spider.HeadingDeg);
        double bend = MathUtil.Wrap(spider.AbdomenDeg - spider.HeadingDeg);

        _hud.Text =
            $"speed {spider.Speed,5:0} px/s   heading {heading,5:0} deg   mouse angle {mouseAngle,5:0} deg   body bend {bend,4:0} deg   {_view.Fps:0} fps\n" +
            "arrows/WASD: move   mouse: aim   shift: sprint   + / -: speed    C: toggle on/off mouse overlay";
    }
}
