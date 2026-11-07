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
/// not tool windows / non-activating popups, not shell windows (desktop, taskbar), with a title, not owned
/// by this process (so the overlay itself never shows up), and at least 50x30 px.
/// Bounds are the DWM extended frame bounds, i.e. the visible frame without the invisible resize borders.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopSource : IDesktopSource
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

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly double _scanIntervalSeconds;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StringBuilder _text = new(512);
    private readonly Dictionary<long, (string Title, string Class, double At)> _cache = new();

    private double _lastScan = double.NegativeInfinity;
    private IReadOnlyList<DesktopWindow> _windows = Array.Empty<DesktopWindow>();

    /// <param name="scanIntervalSeconds">How often the window list is re-enumerated. The cursor is read on every call.</param>
    public WindowsDesktopSource(double scanIntervalSeconds = 1.0 / 60.0)
    {
        _scanIntervalSeconds = scanIntervalSeconds;
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

        return new DesktopSnapshot(new Vec2(cursor.X, cursor.Y), _windows);
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

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == _ownProcessId)
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

        RECT r;
        int hr = NativeMethods.DwmGetWindowAttribute(
            hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>());
        if (hr != 0 && !NativeMethods.GetWindowRect(hWnd, out r))
            return false;

        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w < 50 || h < 30)
            return false;

        window = new DesktopWindow(title, new RectD(r.Left, r.Top, w, h), id);
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
