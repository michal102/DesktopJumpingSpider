using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpiderPet.Platform.Windows;

namespace SpiderPet.Sandbox;

/// <summary>The icon Explorer shows for a file, as an Avalonia bitmap.</summary>
internal static class FileIcons
{
    public static Bitmap? TryLoad(string path) =>
        OperatingSystem.IsWindows() ? LoadWindows(path) : null;

    [SupportedOSPlatform("windows")]
    private static Bitmap? LoadWindows(string path)
    {
        try
        {
            IconPixels? pixels = ShellIcons.Load(path);
            if (pixels is null)
                return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                int rowBytes = pixels.Width * 4;
                for (int y = 0; y < pixels.Height; y++)
                    Marshal.Copy(pixels.Bgra, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
