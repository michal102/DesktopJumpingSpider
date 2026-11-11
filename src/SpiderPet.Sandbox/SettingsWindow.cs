using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>
/// Settings in tabs (Behavior, Drops, Display), each scrollable, plus Close and Quit buttons that are always visible.
/// Follows the system theme and uses the Windows Mica/acrylic backdrop where available.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _updating;

    // Behavior
    private readonly CheckBox _lookIcons = new() { Content = "Look at nearby desktop icons" };
    private readonly CheckBox _lookWindows = new() { Content = "Look at nearby windows" };
    private readonly CheckBox _reactChanges = new() { Content = "React to sudden changes (new window, moved or deleted icon)" };
    private readonly CheckBox _lookText = new() { Content = "Watch the text when you type close to it" };
    private readonly CheckBox _dodge = new() { Content = "Hop back from windows that come fast at it" };
    private readonly CheckBox _stepped = new() { Content = "Stepped turning (big turns are split into a few steps)" };
    private readonly CheckBox _wander = new() { Content = "Walk around on its own (paused while pilot mode is on)" };
    private readonly CheckBox _avoidIcons = new() { Content = "Walk round desktop icons instead of over them (when there are few)" };
    private readonly CheckBox _cursorPlay = new() { Content = "Watch the mouse when you wave it near; double-click to call it over" };
    private readonly CheckBox _pilot = new() { Content = "Keyboard control - pilot mode (Ctrl+Alt+F8)" };
    private readonly Slider _speed = new() { Minimum = 20, Maximum = 1200 };
    private readonly TextBlock _speedLabel = new();
    private readonly CheckBox _autostart = new() { Content = "Start with Windows" };
    private readonly TextBlock _autostartNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, IsVisible = false };

    // Drops
    private readonly ComboBox _dropStyle = new()
    {
        ItemsSource = new[]
        {
            "Lay it down nearby",
            "Tuck it away in my corner",
            "Bring it to the mouse cursor",
        },
    };
    private readonly ComboBox _corner = new()
    {
        ItemsSource = new[] { "Top right", "Top left", "Bottom right", "Bottom left" },
    };

    // Display
    private readonly CheckBox _spider = new() { Content = "Show spider (Ctrl+Alt+F10)" };
    private readonly CheckBox _glow = new() { Content = "Soft rim light around the spider (for dark backgrounds)" };
    private readonly CheckBox _outlines = new() { Content = "Show window outlines (Ctrl+Alt+F9)" };
    private readonly CheckBox _iconMarks = new() { Content = "Mark desktop icons" };
    private readonly CheckBox _info = new() { Content = "Show info panel" };

    public SettingsWindow(AppSettings settings, Action quit)
    {
        _settings = settings;

        Title = "Spider settings";
        Width = 440;
        Height = 520;
        MinWidth = 380;
        MinHeight = 360;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpiderPet.Sandbox/Assets/spider.ico")));
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,        // Windows 11
            WindowTransparencyLevel.AcrylicBlur, // fallback
            WindowTransparencyLevel.Blur,
        };

        AddPage("Behavior", _lookIcons, _lookWindows, _lookText, _reactChanges, _dodge, _stepped, _wander, _avoidIcons, _cursorPlay, _pilot,
            Label("Walking speed"), _speed, _speedLabel);
        AddPage("Drops", Note("What happens when you drop a file onto the spider:"),
            _dropStyle,
            Label("The spider's own corner"), _corner,
            Note("Files it dislikes (Microsoft slop, temp files...) are always dumped in the opposite corner. " +
                 "While it is carrying something, double-click anywhere to make it put the item exactly there, " +
                 "or tap the spider to make it drop the item on the spot. " +
                 "Dropped desktop icons are really moved on your desktop (not with Auto arrange icons on)."));
        AddPage("Display", _spider, _glow, _outlines, _iconMarks, _info);
        AddPage("General", _autostart, _autostartNote);

        // JetBrains-style tab strip: flat labels, an accent underline under the selected one, a hairline below the strip.
        var strip = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
            Padding = new Thickness(10, 6, 10, 0),
            Child = _strip,
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
            Margin = new Thickness(16, 8, 16, 14),
            Children = { close, quitButton },
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(strip);
        Grid.SetRow(_pages, 1);
        root.Children.Add(_pages);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        SelectTab(0);
        ActualThemeVariantChanged += (_, _) => UpdateTabs();
        Content = root;

        Wire(_lookIcons, v => _settings.LookAtIcons = v);
        Wire(_lookWindows, v => _settings.LookAtWindows = v);
        Wire(_reactChanges, v => _settings.ReactToChanges = v);
        Wire(_lookText, v => _settings.LookAtText = v);
        Wire(_dodge, v => _settings.DodgeWindows = v);
        Wire(_stepped, v => _settings.SteppedTurning = v);
        Wire(_wander, v => _settings.WanderAround = v);
        Wire(_avoidIcons, v => _settings.AvoidIcons = v);
        Wire(_cursorPlay, v => _settings.CursorInteraction = v);
        Wire(_pilot, v => _settings.PilotMode = v);
        Wire(_spider, v => _settings.ShowSpider = v);
        Wire(_glow, v => _settings.ShowGlow = v);
        Wire(_outlines, v => _settings.ShowWindowOutlines = v);
        Wire(_iconMarks, v => _settings.ShowIconMarks = v);
        Wire(_info, v => _settings.ShowInfoPanel = v);
        Wire(_autostart, v =>
        {
            if (!Autostart.Set(v))
            {
                _autostartNote.Text = Autostart.IsSupported
                    ? "Could not change the startup entry."
                    : "Only works when running the published exe, not from the IDE or 'dotnet run'.";
                _autostartNote.IsVisible = true;
            }
            else _autostartNote.IsVisible = false;
            Refresh();
        });

        _speed.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _speedLabel.Text = $"{_speed.Value:0} px/s";
            if (!_updating) _settings.BaseSpeed = _speed.Value;
        };
        _dropStyle.SelectionChanged += (_, _) =>
        {
            if (!_updating && _dropStyle.SelectedIndex >= 0) _settings.DropStyle = (DropMode)_dropStyle.SelectedIndex;
        };
        _corner.SelectionChanged += (_, _) =>
        {
            if (!_updating && _corner.SelectedIndex >= 0) _settings.HoardCorner = (SpiderCorner)_corner.SelectedIndex;
        };

        _settings.Changed += Refresh;
        Closed += (_, _) => _settings.Changed -= Refresh;
        Refresh();
    }

    // ------------------------------------------------------------------ layout helpers

    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Grid _pages = new();
    private readonly List<(Border Header, TextBlock Text, Control Page)> _tabs = new();
    private int _tab;

    private void AddPage(string header, params Control[] children)
    {
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(18, 14, 18, 14) };
        foreach (Control child in children)
            stack.Children.Add(child);

        var page = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
            IsVisible = false,
        };
        _pages.Children.Add(page);

        var text = new TextBlock { Text = header, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var tab = new Border
        {
            Padding = new Thickness(14, 8, 14, 7),
            BorderThickness = new Thickness(0, 0, 0, 3),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text,
        };

        int index = _tabs.Count;
        tab.PointerPressed += (_, _) => SelectTab(index);
        tab.PointerEntered += (_, _) => { _hover = index; UpdateTabs(); };
        tab.PointerExited += (_, _) => { if (_hover == index) _hover = -1; UpdateTabs(); };

        _tabs.Add((tab, text, page));
        _strip.Children.Add(tab);
    }

    private int _hover = -1;

    private void SelectTab(int index)
    {
        _tab = index;
        UpdateTabs();
    }

    private void UpdateTabs()
    {
        IBrush accent = Brushes.DodgerBlue;
        if (this.TryFindResource("SystemAccentColor", ActualThemeVariant, out object? v) && v is Color c)
            accent = new SolidColorBrush(c);

        for (int i = 0; i < _tabs.Count; i++)
        {
            bool selected = i == _tab;
            _tabs[i].Page.IsVisible = selected;
            _tabs[i].Header.BorderBrush = selected ? accent : Brushes.Transparent;
            _tabs[i].Header.Background = !selected && i == _hover
                ? new SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
                : Brushes.Transparent;
            _tabs[i].Text.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            _tabs[i].Text.Opacity = selected ? 1.0 : 0.65;
        }
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };

    private void Wire(CheckBox box, Action<bool> apply) =>
        box.IsCheckedChanged += (_, _) =>
        {
            if (!_updating) apply(box.IsChecked == true);
        };

    private void Refresh()
    {
        _updating = true;
        _lookIcons.IsChecked = _settings.LookAtIcons;
        _lookWindows.IsChecked = _settings.LookAtWindows;
        _reactChanges.IsChecked = _settings.ReactToChanges;
        _lookText.IsChecked = _settings.LookAtText;
        _dodge.IsChecked = _settings.DodgeWindows;
        _stepped.IsChecked = _settings.SteppedTurning;
        _wander.IsChecked = _settings.WanderAround;
        _avoidIcons.IsChecked = _settings.AvoidIcons;
        _cursorPlay.IsChecked = _settings.CursorInteraction;
        _pilot.IsChecked = _settings.PilotMode;
        _speed.Value = _settings.BaseSpeed;
        _speedLabel.Text = $"{_settings.BaseSpeed:0} px/s";
        _dropStyle.SelectedIndex = (int)_settings.DropStyle;
        _corner.SelectedIndex = (int)_settings.HoardCorner;
        _spider.IsChecked = _settings.ShowSpider;
        _glow.IsChecked = _settings.ShowGlow;
        _outlines.IsChecked = _settings.ShowWindowOutlines;
        _iconMarks.IsChecked = _settings.ShowIconMarks;
        _info.IsChecked = _settings.ShowInfoPanel;
        _autostart.IsChecked = Autostart.IsEnabled;
        _autostart.IsEnabled = Autostart.IsSupported;
        if (!Autostart.IsSupported)
        {
            _autostartNote.Text = "Only works when running the published exe, not from the IDE or 'dotnet run'.";
            _autostartNote.IsVisible = true;
        }
        _updating = false;
    }
}
