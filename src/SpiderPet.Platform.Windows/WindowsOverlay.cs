using System.Runtime.Versioning;

namespace SpiderPet.Platform.Windows;

/// <summary>Win32 helpers for turning an Avalonia window into a desktop overlay.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsOverlay
{
    /// <summary>
    /// Makes the process per-monitor-v2 DPI aware so that every Win32 coordinate is a physical pixel.
    /// Call before any window exists. Harmless if the process is already aware (the call just fails).
    /// </summary>
    public static void EnsurePerMonitorDpiAware()
    {
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch (EntryPointNotFoundException)
        {
            // pre-Windows-10-1703: nothing to do, Avalonia falls back to its own handling
        }
    }

    /// <summary>
    /// One-time setup of the overlay's extended styles: layered (required for click-through to work across
    /// processes), tool window (no taskbar / Alt+Tab entry), non-activating (never steals focus), and
    /// optionally click-through. Also pins the window topmost.
    /// </summary>
    public static void Prepare(IntPtr hWnd, bool clickThrough)
    {
        long ex = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        if (clickThrough) ex |= NativeMethods.WS_EX_TRANSPARENT;
        else ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, ex);

        // A layered window draws nothing until its attributes are set; alpha 255 = fully opaque, so only the
        // per-pixel alpha of the Avalonia surface decides what is visible.
        NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 255, NativeMethods.LWA_ALPHA);

        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>Turns click-through on or off. Takes effect immediately; cheap enough to call on every hover change.</summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        long ex = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
        long wanted = clickThrough ? ex | NativeMethods.WS_EX_TRANSPARENT : ex & ~NativeMethods.WS_EX_TRANSPARENT;
        if (wanted != ex)
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, wanted);
    }

    /// <summary>Places the window at an exact rectangle in physical pixels and keeps it topmost, without activating it.</summary>
    public static void Place(IntPtr hWnd, int x, int y, int width, int height)
    {
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, x, y, width, height, NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>The window that has the keyboard focus right now (zero when there is none).</summary>
    public static IntPtr GetForeground() => NativeMethods.GetForegroundWindow();

    /// <summary>Re-asserts topmost (other topmost windows can end up above the overlay over time).</summary>
    public static void KeepTopmost(IntPtr hWnd)
    {
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// True while the key is physically down, regardless of which application has focus
    /// (the overlay never takes focus, so Avalonia key events cannot be used).
    /// <paramref name="virtualKey"/> is a Win32 VK_* code.
    /// </summary>
    public static bool IsKeyDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
