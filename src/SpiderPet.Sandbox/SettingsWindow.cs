using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace SpiderPet.Sandbox;

/// <summary>Regular window with every toggle of <see cref="AppSettings"/> plus a quit button.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CheckBox _outlines = new() { Content = "Show window outlines (Ctrl+Alt+F9)" };
    private readonly CheckBox _info = new() { Content = "Show info panel" };
    private readonly CheckBox _glow = new() { Content = "Light glow under the spider" };
    private readonly CheckBox _pilot = new() { Content = "Keyboard control - pilot mode (Ctrl+Alt+F8)" };
    private readonly Slider _speed = new() { Minimum = 20, Maximum = 1200, Width = 300 };
    private readonly TextBlock _speedLabel = new();
    private bool _updating;
    private readonly CheckBox _spider = new() { Content = "Show spider (Ctrl+Alt+F10)" };
    private readonly CheckBox _iconMarks = new() { Content = "Mark desktop icons" };
    private readonly CheckBox _lookIcons = new() { Content = "Look at nearby desktop icons" };

    public SettingsWindow(AppSettings settings, Action quit)
    {
        _settings = settings;

        Title = "Spider settings";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpiderPet.Sandbox/Assets/app-icon.ico")));
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,        // Windows 11
            WindowTransparencyLevel.AcrylicBlur, // fallback
            WindowTransparencyLevel.Blur,
        };

        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        var quitButton = new Button { Content = "Quit spider" };
        quitButton.Click += (_, _) => quit();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { close, quitButton },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                _spider,
                _outlines, _iconMarks, _info, _glow, _pilot, _lookIcons,
                new TextBlock { Text = "Walking speed", Margin = new Thickness(0, 10, 0, 0) },
                _speed, _speedLabel,
                buttons,
            },
        };

        _outlines.IsCheckedChanged += (_, _) => { if (!_updating) _settings.ShowWindowOutlines = _outlines.IsChecked == true; };
        _info.IsCheckedChanged += (_, _) => { if (!_updating) _settings.ShowInfoPanel = _info.IsChecked == true; };
        _glow.IsCheckedChanged += (_, _) => { if (!_updating) _settings.ShowGlow = _glow.IsChecked == true; };
        _pilot.IsCheckedChanged += (_, _) => { if (!_updating) _settings.PilotMode = _pilot.IsChecked == true; };
        _speed.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            _speedLabel.Text = $"{_speed.Value:0} px/s";
            if (!_updating) _settings.BaseSpeed = _speed.Value;
        };
        _iconMarks.IsCheckedChanged += (_, _) => { if (!_updating) _settings.ShowIconMarks = _iconMarks.IsChecked == true; };
        _lookIcons.IsCheckedChanged += (_, _) => { if (!_updating) _settings.LookAtIcons = _lookIcons.IsChecked == true; };
        _spider.IsCheckedChanged += (_, _) => { if (!_updating) _settings.ShowSpider = _spider.IsChecked == true; };

        _settings.Changed += Refresh;
        Closed += (_, _) => _settings.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        _outlines.IsChecked = _settings.ShowWindowOutlines;
        _iconMarks.IsChecked = _settings.ShowIconMarks;
        _lookIcons.IsChecked = _settings.LookAtIcons;
        _info.IsChecked = _settings.ShowInfoPanel;
        _glow.IsChecked = _settings.ShowGlow;
        _pilot.IsChecked = _settings.PilotMode;
        _speed.Value = _settings.BaseSpeed;
        _speedLabel.Text = $"{_settings.BaseSpeed:0} px/s";
        _updating = false;
        _spider.IsChecked = _settings.ShowSpider;
    }
}
