using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SpiderPet.Core;

namespace SpiderPet.Platform.Windows;

/// <summary>
/// Reads the positions of the icons on the Windows desktop.
///
/// The desktop icons are items of a list view (SysListView32 inside SHELLDLL_DefView, hosted by Progman or a
/// WorkerW window) that belongs to explorer.exe. LVM_GETITEMRECT writes into a buffer, and that buffer must
/// live in the list view's own process, so this class allocates a small block inside explorer, asks the list
/// view to fill it, and reads it back. Coordinates are physical screen pixels.
/// Returns an empty list (never throws) when the desktop icons are hidden, explorer is not reachable or hung.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopIconReader : IDisposable
{
    private const uint LVM_GETITEMCOUNT = 0x1004;
    private const uint LVM_GETITEMRECT = 0x100E;
    private const int LVIR_ICON = 1;
    private const int MaxIcons = 2048;
    private const uint SendTimeoutMs = 100;

    private static readonly IReadOnlyList<DesktopIcon> None = Array.Empty<DesktopIcon>();

    private IntPtr _listView;
    private IntPtr _process;
    private IntPtr _remoteRect;

    public IReadOnlyList<DesktopIcon> Read()
    {
        try
        {
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                Detach();
                return None;
            }

            // explorer restarted, or the icons moved to another host window: re-attach
            if (listView != _listView || _process == IntPtr.Zero)
            {
                Detach();
                if (!Attach(listView))
                    return None;
            }

            if (!NativeMethods.IsWindowVisible(_listView))
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
            var size = new UIntPtr((uint)Marshal.SizeOf<RECT>());

            for (int i = 0; i < count; i++)
            {
                // For LVM_GETITEMRECT the rectangle's Left member selects which part of the item is wanted.
                var rect = new RECT { Left = LVIR_ICON };
                if (!NativeMethods.WriteProcessMemory(_process, _remoteRect, ref rect, size, out _))
                {
                    Detach();
                    return icons;
                }

                if (!Send(LVM_GETITEMRECT, new IntPtr(i), _remoteRect, out IntPtr ok) || ok == IntPtr.Zero)
                    continue;

                if (!NativeMethods.ReadProcessMemory(_process, _remoteRect, out rect, size, out _))
                {
                    Detach();
                    return icons;
                }

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

    public void Dispose() => Detach();

    private bool Send(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result) =>
        NativeMethods.SendMessageTimeout(
            _listView, message, wParam, lParam, NativeMethods.SMTO_ABORTIFHUNG, SendTimeoutMs, out result) != IntPtr.Zero;

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
            process, IntPtr.Zero, new UIntPtr((uint)Marshal.SizeOf<RECT>()),
            NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);
        if (remote == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(process);
            return false;
        }

        _listView = listView;
        _process = process;
        _remoteRect = remote;
        return true;
    }

    private void Detach()
    {
        if (_process != IntPtr.Zero)
        {
            if (_remoteRect != IntPtr.Zero)
                NativeMethods.VirtualFreeEx(_process, _remoteRect, UIntPtr.Zero, NativeMethods.MEM_RELEASE);
            NativeMethods.CloseHandle(_process);
        }

        _listView = IntPtr.Zero;
        _process = IntPtr.Zero;
        _remoteRect = IntPtr.Zero;
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
