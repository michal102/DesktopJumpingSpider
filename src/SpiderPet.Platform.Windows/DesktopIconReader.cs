using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SpiderPet.Core;

namespace SpiderPet.Platform.Windows;

/// <summary>
/// Reads and moves the icons on the Windows desktop.
///
/// The desktop icons are items of a list view (SysListView32 inside SHELLDLL_DefView, hosted by Progman or a
/// WorkerW window) that belongs to explorer.exe. List view messages that pass a pointer (LVM_GETITEMRECT, ...) need
/// that pointer to be valid inside the list view's own process, so this class allocates a small block inside explorer,
/// lets the list view read/write it, and copies it back and forth with ReadProcessMemory/WriteProcessMemory.
/// Coordinates are physical screen pixels. 64-bit only (the item structure layout below is the 64-bit one).
/// Methods return empty/false (never throw) when the desktop icons are hidden or explorer is unreachable or hung.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopIconReader : IDisposable
{
    private const uint LVM_GETITEMCOUNT = 0x1004;
    private const uint LVM_GETITEMRECT = 0x100E;
    private const uint LVM_SETITEMPOSITION = 0x100F;
    private const uint LVM_GETITEMPOSITION = 0x1010;
    private const uint LVM_GETITEMTEXTW = 0x1073;
    private const int LVIR_ICON = 1;
    private const uint LVIF_TEXT = 0x1;
    private const int MaxIcons = 2048;
    private const uint SendTimeoutMs = 100;

    // Layout of the block allocated inside explorer.
    private const int RectOffset = 0;      // RECT / POINT, 16 bytes
    private const int ItemOffset = 64;     // LVITEMW, 88 bytes (x64)
    private const int TextOffset = 256;    // UTF-16 text, up to 260 chars
    private const int TextChars = 260;
    private const int BlockSize = 1024;
    private const int ItemSize = 88;

    private static readonly IReadOnlyList<DesktopIcon> None = Array.Empty<DesktopIcon>();

    private IntPtr _listView;
    private IntPtr _process;
    private IntPtr _remote;

    public IReadOnlyList<DesktopIcon> Read()
    {
        try
        {
            if (!EnsureAttached())
                return None;

            if (!Send(LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, out IntPtr countResult))
            {
                Detach();
                return None;
            }

            int count = Math.Clamp((int)countResult, 0, MaxIcons);
            var origin = new POINT();
            if (!NativeMethods.ClientToScreen(_listView, ref origin))
                return None;

            var icons = new List<DesktopIcon>(count);
            for (int i = 0; i < count; i++)
            {
                if (!TryGetIconRect(i, out RECT rect))
                    continue;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w <= 0 || h <= 0)
                    continue;

                icons.Add(new DesktopIcon(i, new RectD(origin.X + rect.Left, origin.Y + rect.Top, w, h)));
            }

            return icons;
        }
        catch (Exception)
        {
            Detach();
            return None;
        }
    }

    /// <summary>Index of the icon whose label equals one of the names (case-insensitive), or -1.</summary>
    public int FindIcon(IReadOnlyList<string> names)
    {
        try
        {
            if (!EnsureAttached() || !Environment.Is64BitProcess)
                return -1;
            if (!Send(LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, out IntPtr countResult))
                return -1;

            int count = Math.Clamp((int)countResult, 0, MaxIcons);
            for (int i = 0; i < count; i++)
            {
                string? text = ReadLabel(i);
                if (text is null)
                    continue;
                foreach (string name in names)
                    if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                        return i;
            }
        }
        catch (Exception)
        {
            Detach();
        }

        return -1;
    }

    /// <summary>Moves icon <paramref name="index"/> so that its centre is at the screen point (cx, cy).</summary>
    public bool MoveIcon(int index, double cx, double cy)
    {
        try
        {
            if (!EnsureAttached())
                return false;

            if (!TryGetIconRect(index, out RECT rect))
                return false;

            // The "position" of an item is not the same point as the corner of its icon image; measure the difference.
            if (!Send(LVM_GETITEMPOSITION, new IntPtr(index), _remote + RectOffset, out IntPtr gotPosition) || gotPosition == IntPtr.Zero)
                return false;
            if (!NativeMethods.ReadProcessMemory(_process, _remote + RectOffset, out RECT position, new UIntPtr((uint)Marshal.SizeOf<RECT>()), out _))
                return false;

            var origin = new POINT();
            if (!NativeMethods.ClientToScreen(_listView, ref origin))
                return false;

            double width = rect.Right - rect.Left;
            double height = rect.Bottom - rect.Top;
            double newLeft = cx - origin.X - width / 2.0;
            double newTop = cy - origin.Y - height / 2.0;

            int x = (int)Math.Round(newLeft + (position.Left - rect.Left));
            int y = (int)Math.Round(newTop + (position.Top - rect.Top));
            if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue)
                return false;

            // MAKELPARAM(x, y): two signed 16 bit halves
            long packed = (uint)((y << 16) | (x & 0xFFFF));
            return Send(LVM_SETITEMPOSITION, new IntPtr(index), new IntPtr(packed), out IntPtr moved) && moved != IntPtr.Zero;
        }
        catch (Exception)
        {
            Detach();
            return false;
        }
    }

    public void Dispose() => Detach();

    // ------------------------------------------------------------------ list view access

    private bool TryGetIconRect(int index, out RECT rect)
    {
        rect = new RECT { Left = LVIR_ICON }; // for LVM_GETITEMRECT the Left member selects which part of the item is wanted
        var size = new UIntPtr((uint)Marshal.SizeOf<RECT>());
        if (!NativeMethods.WriteProcessMemory(_process, _remote + RectOffset, ref rect, size, out _))
            return false;
        if (!Send(LVM_GETITEMRECT, new IntPtr(index), _remote + RectOffset, out IntPtr ok) || ok == IntPtr.Zero)
            return false;
        return NativeMethods.ReadProcessMemory(_process, _remote + RectOffset, out rect, size, out _);
    }

    private string? ReadLabel(int index)
    {
        // LVITEMW (x64): mask@0, iItem@4, iSubItem@8, state@12, stateMask@16, pszText@24, cchTextMax@32, iImage@36, lParam@40, ...
        var item = new byte[ItemSize];
        BitConverter.GetBytes(LVIF_TEXT).CopyTo(item, 0);
        BitConverter.GetBytes(index).CopyTo(item, 4);
        BitConverter.GetBytes((long)(_remote + TextOffset)).CopyTo(item, 24);
        BitConverter.GetBytes(TextChars).CopyTo(item, 32);

        if (!NativeMethods.WriteProcessMemoryBytes(_process, _remote + ItemOffset, item, new UIntPtr((uint)item.Length), out _))
            return null;
        if (!Send(LVM_GETITEMTEXTW, new IntPtr(index), _remote + ItemOffset, out IntPtr length))
            return null;

        int chars = Math.Clamp((int)length, 0, TextChars - 1);
        if (chars == 0)
            return "";

        var text = new byte[chars * 2];
        if (!NativeMethods.ReadProcessMemoryBytes(_process, _remote + TextOffset, text, new UIntPtr((uint)text.Length), out _))
            return null;
        return Encoding.Unicode.GetString(text);
    }

    private bool Send(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result) =>
        NativeMethods.SendMessageTimeout(
            _listView, message, wParam, lParam, NativeMethods.SMTO_ABORTIFHUNG, SendTimeoutMs, out result) != IntPtr.Zero;

    private bool EnsureAttached()
    {
        IntPtr listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            Detach();
            return false;
        }

        // explorer restarted, or the icons moved to another host window: re-attach
        if (listView != _listView || _process == IntPtr.Zero)
        {
            Detach();
            if (!Attach(listView))
                return false;
        }

        return NativeMethods.IsWindowVisible(_listView);
    }

    private bool Attach(IntPtr listView)
    {
        NativeMethods.GetWindowThreadProcessId(listView, out uint pid);
        if (pid == 0)
            return false;

        IntPtr process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            false, pid);
        if (process == IntPtr.Zero)
            return false;

        IntPtr remote = NativeMethods.VirtualAllocEx(
            process, IntPtr.Zero, new UIntPtr((uint)BlockSize),
            NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);
        if (remote == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(process);
            return false;
        }

        _listView = listView;
        _process = process;
        _remote = remote;
        return true;
    }

    private void Detach()
    {
        if (_process != IntPtr.Zero)
        {
            if (_remote != IntPtr.Zero)
                NativeMethods.VirtualFreeEx(_process, _remote, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(_process);
        }

        _listView = IntPtr.Zero;
        _process = IntPtr.Zero;
        _remote = IntPtr.Zero;
    }

    private static IntPtr FindDesktopListView()
    {
        IntPtr view = IntPtr.Zero;

        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            view = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        // With a wallpaper slideshow or after "Show desktop" the icons are hosted by a top-level WorkerW instead.
        if (view == IntPtr.Zero)
        {
            IntPtr worker = IntPtr.Zero;
            while ((worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                view = NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero)
                    break;
            }
        }

        return view == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }
}
