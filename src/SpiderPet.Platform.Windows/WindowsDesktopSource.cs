using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SpiderPet.Core;

namespace SpiderPet.Platform.Windows;

/// <summary>
/// Real desktop for Windows: the mouse position from <c>GetCursorPos</c> and the list of "real" application
/// windows from <c>EnumWindows</c> (frontmost first). Everything is in <b>physical screen pixels</b>
/// (the process must be per-monitor DPI aware - see <see cref="WindowsOverlay.EnsurePerMonitorDpiAware"/>).
///
/// Which windows count: visible, not minimised, not DWM-cloaked (hidden UWP/virtual-desktop windows),
/// not tool windows / non-activating popups, not shell windows (desktop, taskbar), with a title, not the overlay
/// itself (other windows of this process, such as the settings window, do count), and at least 50x30 px.
/// Bounds are the DWM extended frame bounds, i.e. the visible frame without the invisible resize borders.
/// Desktop icons come from <see cref="DesktopIconReader"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopSource : IDesktopSource, IDesktopIconControl, IDisposable
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",                    // desktop
        "WorkerW",                    // desktop wallpaper host
        "Shell_TrayWnd",              // taskbar
        "Shell_SecondaryTrayWnd",     // taskbar on other monitors
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
    };

    private const double TitleRefreshSeconds = 1.5;

    private readonly double _scanIntervalSeconds;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StringBuilder _text = new(512);
    private readonly Dictionary<long, (string Title, string Class, double At)> _cache = new();

    private const double IconScanSeconds = 0.2;

    private readonly TypingWatcher _typing = new();

    private readonly DesktopIconReader _iconReader = new();
    private double _lastIconScan = double.NegativeInfinity;
    private IReadOnlyList<DesktopIcon> _icons = Array.Empty<DesktopIcon>();

    private double _lastScan = double.NegativeInfinity;
    private IReadOnlyList<DesktopWindow> _windows = Array.Empty<DesktopWindow>();

    /// <param name="scanIntervalSeconds">How often the window <b>list</b> is re-enumerated (new, closed, re-ordered windows).
    /// The cursor and the bounds of the known windows are read on every call, so moving windows are tracked without delay.</param>
    public WindowsDesktopSource(double scanIntervalSeconds = 1.0 / 30.0)
    {
        _scanIntervalSeconds = scanIntervalSeconds;
    }

    /// <summary>Whether desktop icons are read (5 times a second). Turn off when nothing needs them.</summary>
    public bool TrackIcons { get; set; } = true;

    /// <summary>Whether typing is noticed and the caret position reported (see <see cref="TypingWatcher"/>).</summary>
    public bool TrackTyping { get; set; } = true;

    /// <summary>A window that must never be reported (the overlay itself). Other windows of this process, e.g. settings, are reported.</summary>
    public IntPtr IgnoredWindow { get; set; }

    public void Dispose() => _iconReader.Dispose();

    // ---- IDesktopIconControl (physical screen pixels)

    public int FindIcon(IReadOnlyList<string> displayNames) => _iconReader.FindIcon(displayNames);

    public bool MoveIcon(int index, Vec2 center)
    {
        bool moved = _iconReader.MoveIcon(index, center.X, center.Y);
        if (moved)
            _lastIconScan = double.NegativeInfinity; // re-read the icons on the next snapshot
        return moved;
    }

    public DesktopSnapshot GetSnapshot()
    {
        NativeMethods.GetCursorPos(out POINT cursor);

        double now = _clock.Elapsed.TotalSeconds;
        if (now - _lastScan >= _scanIntervalSeconds)
        {
            _windows = Scan(now);
            _lastScan = now;
        }
        else
        {
            _windows = RefreshBounds(_windows);
        }

        if (!TrackIcons)
        {
            _icons = Array.Empty<DesktopIcon>();
        }
        else if (now - _lastIconScan >= IconScanSeconds)
        {
            _icons = _iconReader.Read();
            _lastIconScan = now;
        }

        bool typing = TrackTyping && _typing.Update();
        Vec2? caret = null;
        if (typing && TypingWatcher.TryGetCaret(out Vec2 caretAt))
            caret = caretAt;

        return new DesktopSnapshot(new Vec2(cursor.X, cursor.Y), _windows, _icons, caret, typing && caret is not null);
    }

    // Between full scans only the rectangles are re-read. Without this a dragged window would be sampled at the
    // scan rate, which beats against the frame rate and makes anything attached to it (the riding spider) stutter.
    private static IReadOnlyList<DesktopWindow> RefreshBounds(IReadOnlyList<DesktopWindow> current)
    {
        List<DesktopWindow>? updated = null;
        for (int i = 0; i < current.Count; i++)
        {
            DesktopWindow w = current[i];
            var hWnd = new IntPtr(w.Id);

            // a window that just got minimised reports a rectangle far off screen; keep the old one until the next scan drops it
            if (NativeMethods.IsIconic(hWnd) || !TryGetBounds(hWnd, out RectD bounds) || bounds == w.Bounds)
                continue;

            updated ??= new List<DesktopWindow>(current);
            updated[i] = w with { Bounds = bounds };
        }

        return updated ?? current;
    }

    private static bool TryGetBounds(IntPtr hWnd, out RectD bounds)
    {
        RECT r;
        int hr = NativeMethods.DwmGetWindowAttribute(
            hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>());
        if (hr != 0 && !NativeMethods.GetWindowRect(hWnd, out r))
        {
            bounds = default;
            return false;
        }

        bounds = new RectD(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return true;
    }

    private List<DesktopWindow> Scan(double now)
    {
        var list = new List<DesktopWindow>(32);

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (TryDescribe(hWnd, now, out DesktopWindow? window))
                list.Add(window);
            return true;
        }, IntPtr.Zero);

        if (_cache.Count > 4096)
            _cache.Clear();

        return list;
    }

    private bool TryDescribe(IntPtr hWnd, double now, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DesktopWindow? window)
    {
        window = null;

        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
            return false;

        if (hWnd == IgnoredWindow)
            return false;

        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
            return false;

        long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
        bool appWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (!appWindow && (exStyle & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)) != 0)
            return false;

        long id = hWnd.ToInt64();
        (string title, string cls) = TitleAndClass(hWnd, id, now);
        if (title.Length == 0 || IgnoredClasses.Contains(cls))
            return false;

        if (!TryGetBounds(hWnd, out RectD bounds) || bounds.Width < 50 || bounds.Height < 30)
            return false;

        window = new DesktopWindow(title, bounds, id);
        return true;
    }

    // Title and class name are cached: reading them means a call into another process for every window,
    // and neither changes often enough to matter at 60 scans per second.
    private (string Title, string Class) TitleAndClass(IntPtr hWnd, long id, double now)
    {
        if (_cache.TryGetValue(id, out var cached) && now - cached.At < TitleRefreshSeconds)
            return (cached.Title, cached.Class);

        string title = "";
        int len = NativeMethods.GetWindowTextLength(hWnd);
        if (len > 0)
        {
            _text.Clear();
            _text.EnsureCapacity(len + 1);
            NativeMethods.GetWindowText(hWnd, _text, len + 1);
            title = _text.ToString();
        }

        _text.Clear();
        _text.EnsureCapacity(256);
        NativeMethods.GetClassName(hWnd, _text, 256);
        string cls = _text.ToString();

        _cache[id] = (title, cls, now);
        return (title, cls);
    }
}
