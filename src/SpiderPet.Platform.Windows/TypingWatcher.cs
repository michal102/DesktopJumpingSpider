using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SpiderPet.Core;

namespace SpiderPet.Platform.Windows;

/// <summary>
/// Notices that somebody is typing and finds the text caret of the focused window.
///
/// Privacy: it only asks "is any typing key down right now?" and remembers the time. It never records which key,
/// and never reads any text.
///
/// Where the text is, best source first:
///  1. the system caret (<c>GetGUIThreadInfo</c>): Notepad, classic edit controls, Chromium and Electron apps;
///  2. the position the program gave the input method (<c>ImmGetCompositionWindow</c>): programs that draw their own
///     caret, such as the JetBrains IDEs, usually still tell the input method where it is;
///  3. the place in that window where the mouse was last clicked: when a program gives no position at all, you
///     almost always click into the text before you type in it. (It never guesses "the middle of the window":
///     with no position at all the spider simply does not react.)
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TypingWatcher
{
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkControl = 0x11;
    private const int VkLButton = 0x01;
    private const int VkMenu = 0x12;   // Alt (AltGr, used for ą ę ś..., counts as Ctrl and Alt together)

    private static readonly int[] TypingKeys = BuildKeyList();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastKey = double.NegativeInfinity;
    private bool _mouseWasDown;

    // where the left button was last pressed in each top-level window, relative to the window's top-left corner
    private readonly Dictionary<IntPtr, (double dx, double dy)> _clicks = new();

    /// <summary>How long after the last key press it still counts as typing, seconds.</summary>
    public double HoldSeconds { get; set; } = 1.5;

    /// <summary>Call once per frame. True while somebody is typing (or has just stopped).</summary>
    public bool Update()
    {
        double now = _clock.Elapsed.TotalSeconds;
        TrackClicks();

        bool winHeld = (NativeMethods.GetAsyncKeyState(VkLWin) & 0x8000) != 0
                       || (NativeMethods.GetAsyncKeyState(VkRWin) & 0x8000) != 0;
        // Ctrl+letter is a shortcut, not typing (but Ctrl together with Alt is AltGr, which does type)
        bool shortcut = (NativeMethods.GetAsyncKeyState(VkControl) & 0x8000) != 0
                        && (NativeMethods.GetAsyncKeyState(VkMenu) & 0x8000) == 0;
        if (!winHeld && !shortcut)
        {
            foreach (int vk in TypingKeys)
            {
                if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    _lastKey = now;
                    break;
                }
            }
        }

        return now - _lastKey < HoldSeconds;
    }

    /// <summary>Centre of the focused window's text caret in physical screen pixels, or false when there is none.</summary>
    public bool TryGetCaret(out Vec2 centre)
    {
        centre = default;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        uint thread = NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return false;   // our own windows

        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        bool haveInfo = NativeMethods.GetGUIThreadInfo(thread, ref info);

        // 1. the system caret
        if (haveInfo && info.hwndCaret != IntPtr.Zero)
        {
            RECT r = info.rcCaret;
            if (!(r.Left == 0 && r.Top == 0 && r.Right == 0 && r.Bottom == 0))
            {
                var p = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
                if (NativeMethods.ClientToScreen(info.hwndCaret, ref p))
                {
                    centre = new Vec2(p.X, p.Y);
                    return true;
                }
            }
        }

        // 2. where the program told the input method its text is
        IntPtr focus = haveInfo && info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foreground;
        if (TryGetImePosition(focus, out centre))
            return true;

        // 3. where the mouse last clicked in this window
        return TryGetClick(foreground, out centre);
    }

    // Remembers where a click landed, as an offset inside the top-level window under the mouse. Nothing else about it
    // (no key, no text, no window title) is kept.
    private void TrackClicks()
    {
        bool down = (NativeMethods.GetAsyncKeyState(VkLButton) & 0x8000) != 0;
        bool pressed = down && !_mouseWasDown;
        _mouseWasDown = down;
        if (!pressed || !NativeMethods.GetCursorPos(out POINT cursor))
            return;

        IntPtr root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cursor), 2);   // GA_ROOT
        if (root == IntPtr.Zero || !NativeMethods.GetWindowRect(root, out RECT r))
            return;

        NativeMethods.GetWindowThreadProcessId(root, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return;   // clicks on the spider itself

        if (_clicks.Count > 32)
            _clicks.Clear();
        _clicks[root] = (cursor.X - r.Left, cursor.Y - r.Top);
    }

    private bool TryGetClick(IntPtr window, out Vec2 centre)
    {
        centre = default;
        if (!_clicks.TryGetValue(window, out var offset) || !NativeMethods.GetWindowRect(window, out RECT r))
            return false;

        double x = r.Left + offset.dx, y = r.Top + offset.dy;
        if (x < r.Left || x > r.Right || y < r.Top || y > r.Bottom)
            return false;   // the window was resized since

        centre = new Vec2(x, y);
        return true;
    }

    private static bool TryGetImePosition(IntPtr window, out Vec2 centre)
    {
        centre = default;
        IntPtr himc = NativeMethods.ImmGetContext(window);
        if (himc == IntPtr.Zero)
            return false;

        try
        {
            if (!NativeMethods.ImmGetCompositionWindow(himc, out COMPOSITIONFORM form) || form.dwStyle == 0)
                return false;

            var p = form.ptCurrentPos;
            if (p.X == 0 && p.Y == 0)
                return false;   // never set
            if (!NativeMethods.ClientToScreen(window, ref p))
                return false;
            // a stale or nonsense position outside the window it belongs to is worthless
            IntPtr top = NativeMethods.GetAncestor(window, 2);
            if (top != IntPtr.Zero && NativeMethods.GetWindowRect(top, out RECT tr)
                && (p.X < tr.Left || p.X > tr.Right || p.Y < tr.Top || p.Y > tr.Bottom))
                return false;

            centre = new Vec2(p.X, p.Y);
            return true;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(window, himc);
        }
    }

    private static int[] BuildKeyList()
    {
        var keys = new List<int> { 0x08, 0x0D, 0x20 };          // backspace, enter, space
        for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk);     // 0-9
        for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk);     // A-Z
        for (int vk = 0x60; vk <= 0x69; vk++) keys.Add(vk);     // numpad digits
        for (int vk = 0xBA; vk <= 0xC0; vk++) keys.Add(vk);     // ; = , - . / `
        for (int vk = 0xDB; vk <= 0xDF; vk++) keys.Add(vk);     // [ \ ] ' and friends
        return keys.ToArray();
    }
}
