using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SpiderPet.Core;
using SpiderPet.Platform.Windows;

namespace SpiderPet.Sandbox;

/// <summary>
/// Transparent, borderless, topmost, click-through window covering the primary monitor that draws only the spider.
///
/// Click-through: the window carries WS_EX_TRANSPARENT so every click goes to whatever is underneath, except while
/// the mouse is over the spider (or the spider is being dragged), when the flag is removed so the spider can be grabbed.
///
/// Keyboard: the overlay never takes focus, so keys are polled globally. That only happens in "pilot mode"
/// (Ctrl+Alt+F8, settings or tray menu), because the keys also reach whatever app has focus.
/// Ctrl+Alt+F9 toggles the window outlines, Ctrl+Alt+F12 quits. Right-click the spider for its menu.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OverlayWindow : Window
{
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const int VkA = 0x41, VkD = 0x44, VkS = 0x53, VkW = 0x57;
    private const int VkF8 = 0x77, VkF9 = 0x78, VkF12 = 0x7B;
    private const int VkOemPlus = 0xBB, VkOemMinus = 0xBD, VkAdd = 0x6B, VkSubtract = 0x6D;

    private readonly SandboxView _view = new();
    private readonly WindowsDesktopSource _windows = new();
    private readonly OverlayDesktopSource _source;
    private readonly Border _hudBox;
    private readonly TextBlock _hud;
    private readonly Border _menuShield;
    private readonly Border _menu;
    private readonly bool _solid = LaunchOptions.Solid;
    private readonly AppSettings _settings = new() { ShowGlow = WindowsTheme.IsDark() };

    private TrayIcon? _tray;
    private NativeMenuItem? _pilotItem;
    private NativeMenuItem? _outlinesItem;
    private SettingsWindow? _settingsWindow;
    private bool _menuOpen;
    private bool _wasPilot;

    private IntPtr _hwnd;
    private bool _clickThrough = true;
    private bool _placed;
    private bool _hotkeyHeld;
    private bool _outlinesKeyHeld;
    private bool _quitHeld;
    private bool _plusHeld;
    private bool _minusHeld;
    private bool _up, _down, _left, _right, _sprint;
    private long _topmostAt;
    private IntPtr _lastForeground;
    private int _frames;
    
    private const int VkF10 = 0x79;
    private bool _spiderKeyHeld;
    private NativeMenuItem? _spiderItem;

    public OverlayWindow()
    {
        _source = new OverlayDesktopSource(_windows);

        Title = "Spider overlay";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        TransparencyBackgroundFallback = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _hud = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
        };
        _hudBox = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(170, 16, 18, 22)),
            Child = _hud,
        };

        // Right-click menu: drawn as plain controls (no popup/focus machinery, which a non-activating overlay handles badly).
        // The shield is an invisible full-screen layer behind the menu that closes it on any click elsewhere.
        _menuShield = new Border { Background = Brushes.Transparent, IsVisible = false };
        _menuShield.PointerPressed += (_, e) => { CloseMenu(); e.Handled = true; };

        _menu = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(245, 22, 25, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Width = 150,
                Children =
                {
                    MenuItemControl("\uE713", "Settings...", OpenSettings),
                    MenuItemControl("\uED1A", "Hide spider", () => _settings.ShowSpider = false),
                    MenuItemControl("\uE7E8", "Quit", Quit),
                },
            },
        };

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_view);
        root.Children.Add(_hudBox);
        root.Children.Add(_menuShield);
        root.Children.Add(_menu);
        Content = root;

        _view.UseOverlayDesktop(_source);
        _view.IconControl = _source;
        _view.BeforeUpdate += OnBeforeUpdate;
        _view.Frame += OnFrame;
        _view.SpiderMenuRequested += ShowMenu;

        _settings.Changed += OnSettingsChanged;
        OnSettingsChanged();

        CreateTray();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Screen? screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            PixelRect b = screen.Bounds;
            Position = b.Position;
            Width = b.Width / screen.Scaling;
            Height = b.Height / screen.Scaling;
        }

        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _windows.IgnoredWindow = _hwnd;
        if (_hwnd != IntPtr.Zero)
        {
            WindowsOverlay.Prepare(_hwnd, !_solid);
            _clickThrough = !_solid;
            if (screen is not null)
            {
                PixelRect b = screen.Bounds;
                WindowsOverlay.Place(_hwnd, b.X, b.Y, b.Width, b.Height);
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        _windows.Dispose();
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------ per-frame work

    private void OnBeforeUpdate()
    {
        _source.OriginX = Position.X;
        _source.OriginY = Position.Y;
        _source.Scale = RenderScaling;

        if (!_placed && _view.Bounds.Width > 100 && _view.Bounds.Height > 100)
        {
            var centre = new Vec2(_view.Bounds.Width / 2, _view.Bounds.Height / 2);
            _view.Spider.Translate(centre - _view.Spider.Position);
            _placed = true;
        }

        PollHotkeys();
        if (_settings.PilotMode) PollPilotKeys();

        UpdateClickThrough();

        // Whatever window just came to the front (the settings window, a menu, another topmost window) ends up above
        // the overlay, so put the overlay back on top at once when the focus moves, and now and then regardless.
        // (Counted in real time: a frame counter ran slow under load and left the spider hidden for seconds.)
        if (_hwnd != IntPtr.Zero)
        {
            IntPtr foreground = WindowsOverlay.GetForeground();
            long tick = Environment.TickCount64;
            if (foreground != _lastForeground || tick - _topmostAt >= 250)
            {
                _lastForeground = foreground;
                _topmostAt = tick;
                WindowsOverlay.KeepTopmost(_hwnd);
            }
        }
    }

    private void UpdateClickThrough()
    {
        if (_hwnd == IntPtr.Zero || _solid) return;

        // Uses the cursor from the previous poll, which is at most one frame old.
        // while the spider carries something the overlay also catches clicks, so a double click can tell it where to put the item
        bool wantSolid = _menuOpen || _view.IsDragging || _view.Carry.Busy
                         || _view.IsOverSpider(_view.Watcher.Current.Cursor);
        bool wantClickThrough = !wantSolid;
        if (wantClickThrough != _clickThrough)
        {
            WindowsOverlay.SetClickThrough(_hwnd, wantClickThrough);
            _clickThrough = wantClickThrough;
        }
    }

    private void PollHotkeys()
    {
        bool mods = WindowsOverlay.IsKeyDown(VkControl) && WindowsOverlay.IsKeyDown(VkMenu);

        bool toggle = mods && WindowsOverlay.IsKeyDown(VkF8);
        if (toggle && !_hotkeyHeld) _settings.PilotMode = !_settings.PilotMode;
        _hotkeyHeld = toggle;

        bool outlines = mods && WindowsOverlay.IsKeyDown(VkF9);
        if (outlines && !_outlinesKeyHeld) _settings.ShowWindowOutlines = !_settings.ShowWindowOutlines;
        _outlinesKeyHeld = outlines;

        bool quit = mods && WindowsOverlay.IsKeyDown(VkF12);
        if (quit && !_quitHeld) Quit();
        _quitHeld = quit;
        
        bool spiderKey = mods && WindowsOverlay.IsKeyDown(VkF10);
        if (spiderKey && !_spiderKeyHeld) _settings.ShowSpider = !_settings.ShowSpider;
        _spiderKeyHeld = spiderKey;
    }

    private void PollPilotKeys()
    {
        Apply(ref _up, WindowsOverlay.IsKeyDown(VkUp) || WindowsOverlay.IsKeyDown(VkW), Key.Up);
        Apply(ref _down, WindowsOverlay.IsKeyDown(VkDown) || WindowsOverlay.IsKeyDown(VkS), Key.Down);
        Apply(ref _left, WindowsOverlay.IsKeyDown(VkLeft) || WindowsOverlay.IsKeyDown(VkA), Key.Left);
        Apply(ref _right, WindowsOverlay.IsKeyDown(VkRight) || WindowsOverlay.IsKeyDown(VkD), Key.Right);
        Apply(ref _sprint, WindowsOverlay.IsKeyDown(VkShift), Key.LeftShift);
        Apply(ref _plusHeld, WindowsOverlay.IsKeyDown(VkOemPlus) || WindowsOverlay.IsKeyDown(VkAdd), Key.OemPlus);
        Apply(ref _minusHeld, WindowsOverlay.IsKeyDown(VkOemMinus) || WindowsOverlay.IsKeyDown(VkSubtract), Key.OemMinus);
    }

    // Forwards only changes, because SandboxView.SetKey(down) also performs one-shot actions (speed +/-).
    private void Apply(ref bool state, bool now, Key key)
    {
        if (now == state) return;
        state = now;
        _view.SetKey(key, now);
    }

    private string SpiderHeader() => _settings.ShowSpider ? "Hide spider (Ctrl+Alt+F10)" : "Show spider (Ctrl+Alt+F10)";
    
    private void OnSettingsChanged()
    {
        _view.ShowDebug = _settings.ShowWindowOutlines;
        _view.ShowGlow = _settings.ShowGlow;
        _view.BaseSpeed = _settings.BaseSpeed;
        _hudBox.IsVisible = _settings.ShowInfoPanel;
        _view.SpiderVisible = _settings.ShowSpider;
        _view.ShowIconMarks = _settings.ShowIconMarks;
        _view.Brain.LookAtIcons = _settings.LookAtIcons;
        _view.Brain.LookAtWindows = _settings.LookAtWindows;
        _view.Brain.ReactToChanges = _settings.ReactToChanges;
        _view.Brain.LookAtText = _settings.LookAtText;
        _view.Brain.DodgeWindows = _settings.DodgeWindows;
        _windows.TrackTyping = _settings.LookAtText;
        _view.DropStyle = _settings.DropStyle;
        _view.HoardCorner = _settings.HoardCorner;
        _view.Spider.SteppedTurning = _settings.SteppedTurning;
        _view.WanderEnabled = _settings.WanderAround && !_settings.PilotMode;   // piloting it yourself stops it moving on its own
        _view.Wanderer.AvoidIcons = _settings.AvoidIcons;
        _view.Brain.ReactToCursor = _settings.CursorInteraction;
        _windows.TrackIcons = _settings.ShowIconMarks || _settings.LookAtIcons || (_settings.WanderAround && _settings.AvoidIcons);
        
        if (_spiderItem is not null) _spiderItem.Header = SpiderHeader();

        if (_wasPilot && !_settings.PilotMode)
        {
            _view.ReleaseAllKeys();
            _up = _down = _left = _right = _sprint = _plusHeld = _minusHeld = false;
        }
        _wasPilot = _settings.PilotMode;

        if (_pilotItem is not null) _pilotItem.Header = PilotHeader();
        if (_outlinesItem is not null) _outlinesItem.Header = OutlinesHeader();
    }

    private void OnFrame()
    {
        if (++_frames % 6 != 0) return;
        if (!_settings.ShowInfoPanel) return;

        Spider s = _view.Spider;
        _hud.Text =
            $"spider overlay   {_view.Fps:0} fps   speed {s.Speed:0} px/s\n" +
            $"windows tracked: {_view.Watcher.Current.Windows.Count}   standing on: {_view.Perch?.Title ?? "-"}\n" +
            $"last event: {_view.LastEvent}\n" +
            $"on desktop: {(_view.Brain.OnDesktop ? "yes" : "no")}   icons: {_view.Watcher.Current.Icons?.Count ?? 0}   looking at: {_view.Brain.FocusLabel}\n" +
            $"carrying: {_view.Carry.Phase}   (drop a file onto it; double-click to redirect)\n" +
            $"pilot: {(_settings.PilotMode ? "ON" : "off")} (Ctrl+Alt+F8)   outlines (Ctrl+Alt+F9)   quit (Ctrl+Alt+F12)\n" +
            "right-click the spider for its menu";
    }

    // ------------------------------------------------------------------ right-click menu, settings, quit

    private Border MenuItemControl(string glyph, string text, Action action)
    {
        var item = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = glyph,
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 14, Width = 18, Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13 },
                },
            },
        };
        item.PointerEntered += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        item.PointerExited += (_, _) => item.Background = Brushes.Transparent;
        item.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            CloseMenu();
            action();
        };
        return item;
    }

    private void ShowMenu(Vec2 at)
    {
        double x = Math.Max(0, Math.Min(at.X, Bounds.Width - 170));
        double y = Math.Max(0, Math.Min(at.Y, Bounds.Height - 130));
        _menu.Margin = new Thickness(x, y, 0, 0);
        _menuShield.IsVisible = true;
        _menu.IsVisible = true;
        _menuOpen = true;
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menu.IsVisible = false;
        _menuShield.IsVisible = false;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, Quit);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        SettingsWindow window = _settingsWindow;
        BringToFront(window);

        // A tray menu is still closing at this moment and may take the focus back, so ask once more afterwards.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_settingsWindow, window))
                BringToFront(window);
        }, DispatcherPriority.Background);
    }

    private static void BringToFront(Window window)
    {
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;   // un-minimise
        window.Activate();
    }

    // Shutdown() rather than Close(): an open settings window would otherwise keep the process alive.
    private void Quit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
        else
            Close();
    }

    // ------------------------------------------------------------------ tray

    private string PilotHeader() => _settings.PilotMode ? "Pilot mode: ON (Ctrl+Alt+F8)" : "Pilot mode: off (Ctrl+Alt+F8)";

    private string OutlinesHeader() => _settings.ShowWindowOutlines ? "Window outlines: ON (Ctrl+Alt+F9)" : "Window outlines: off (Ctrl+Alt+F9)";

    private void CreateTray()
    {
        if (Application.Current is null) return;

        _pilotItem = new NativeMenuItem(PilotHeader());
        _pilotItem.Click += (_, _) => _settings.PilotMode = !_settings.PilotMode;

        _outlinesItem = new NativeMenuItem(OutlinesHeader());
        _outlinesItem.Click += (_, _) => _settings.ShowWindowOutlines = !_settings.ShowWindowOutlines;

        _spiderItem = new NativeMenuItem(SpiderHeader());
        _spiderItem.Click += (_, _) => _settings.ShowSpider = !_settings.ShowSpider;

        var settings = new NativeMenuItem("Settings...");
        settings.Click += (_, _) => OpenSettings();

        var quit = new NativeMenuItem("Quit (Ctrl+Alt+F12)");
        quit.Click += (_, _) => Quit();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpiderPet.Sandbox/Assets/spider.ico"))),
            ToolTipText = "Desktop spider",
            IsVisible = true,
            Menu = new NativeMenu
            {
                settings,
                _spiderItem,
                _pilotItem,
                _outlinesItem,
                new NativeMenuItemSeparator(),
                quit,
            },
        };

        _tray.Clicked += (_, _) => OpenSettings();

        TrayIcon.SetIcons(Application.Current, new TrayIcons { _tray });
    }
}
