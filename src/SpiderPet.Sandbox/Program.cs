using Avalonia;
using SpiderPet.Platform.Windows;

namespace SpiderPet.Sandbox;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LaunchOptions.Parse(args);

        // Every Win32 coordinate (window rects, cursor) must be a physical pixel; do this before any window exists.
        if (LaunchOptions.Overlay && OperatingSystem.IsWindows())
            WindowsOverlay.EnsurePerMonitorDpiAware();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
