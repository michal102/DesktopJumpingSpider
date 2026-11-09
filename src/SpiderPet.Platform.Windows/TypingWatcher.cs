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
/// The caret comes from <c>GetGUIThreadInfo</c>: it works for programs that use the system caret (Notepad, classic
/// edit controls, Chromium-based browsers and Electron apps expose one), and gives nothing for programs that draw
/// their own caret without telling Windows. In that case the spider simply does not react.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TypingWatcher
{
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private static readonly int[] TypingKeys = BuildKeyList();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastKey = double.NegativeInfinity;

    /// <summary>How long after the last key press it still counts as typing, seconds.</summary>
    public double HoldSeconds { get; set; } = 1.5;

    /// <summary>Call once per frame. True while somebody is typing (or has just stopped).</summary>
    public bool Update()
    {
        double now = _clock.Elapsed.TotalSeconds;

        bool winHeld = (NativeMethods.GetAsyncKeyState(VkLWin) & 0x8000) != 0
                       || (NativeMethods.GetAsyncKeyState(VkRWin) & 0x8000) != 0;
        if (!winHeld)
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
    public static bool TryGetCaret(out Vec2 centre)
    {
        centre = default;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        uint thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero)
            return false;

        RECT r = info.rcCaret;
        if (r.Left == 0 && r.Top == 0 && r.Right == 0 && r.Bottom == 0)
            return false;

        var p = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        if (!NativeMethods.ClientToScreen(info.hwndCaret, ref p))
            return false;

        centre = new Vec2(p.X, p.Y);
        return true;
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
