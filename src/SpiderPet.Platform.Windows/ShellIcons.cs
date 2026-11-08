using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpiderPet.Platform.Windows;

/// <summary>A decoded icon: <see cref="Bgra"/> holds Width*Height pixels, 4 bytes each (blue, green, red, alpha), top row first, not premultiplied.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>Fetches the icon Explorer shows for a file (the real one: shortcut arrows, app icons, file type icons).</summary>
[SupportedOSPlatform("windows")]
public static class ShellIcons
{
    /// <summary>Returns the large shell icon of an existing file or folder, or null if there is none.</summary>
    public static IconPixels? Load(string path)
    {
        try
        {
            var info = new NativeMethods.SHFILEINFO();
            IntPtr result = NativeMethods.SHGetFileInfo(
                path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                return Decode(info.hIcon);
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IconPixels? Decode(IntPtr icon)
    {
        if (!NativeMethods.GetIconInfo(icon, out NativeMethods.ICONINFO info))
            return null;

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            if (info.hbmColor == IntPtr.Zero)
                return null; // old monochrome icon, not worth supporting

            if (NativeMethods.GetObject(info.hbmColor, Marshal.SizeOf<NativeMethods.BITMAP>(), out NativeMethods.BITMAP bm) == 0)
                return null;

            int w = bm.bmWidth, h = bm.bmHeight;
            if (w <= 0 || h <= 0 || w > 512 || h > 512)
                return null;

            byte[] color = ReadBits(screen, info.hbmColor, w, h);
            if (color.Length == 0)
                return null;

            // Modern icons carry their own alpha. Older ones do not: then the AND mask decides what is transparent.
            bool hasAlpha = false;
            for (int i = 3; i < color.Length; i += 4)
            {
                if (color[i] != 0) { hasAlpha = true; break; }
            }

            if (!hasAlpha)
            {
                byte[] mask = info.hbmMask != IntPtr.Zero ? ReadBits(screen, info.hbmMask, w, h) : Array.Empty<byte>();
                for (int i = 0; i < color.Length; i += 4)
                {
                    bool transparent = mask.Length == color.Length && mask[i] != 0; // white = see-through
                    color[i + 3] = transparent ? (byte)0 : (byte)255;
                }
            }

            return new IconPixels(w, h, color);
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmMask);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // Asks GDI for the bitmap as 32 bit BGRA, top row first (negative height).
    private static byte[] ReadBits(IntPtr dc, IntPtr bitmap, int w, int h)
    {
        var header = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        var bits = new byte[w * h * 4];
        int lines = NativeMethods.GetDIBits(dc, bitmap, 0, (uint)h, bits, ref header, 0 /* DIB_RGB_COLORS */);
        return lines == h ? bits : Array.Empty<byte>();
    }
}
