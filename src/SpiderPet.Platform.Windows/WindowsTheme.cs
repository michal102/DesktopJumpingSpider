using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SpiderPet.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsTheme
{
    /// <summary>True when Windows is set to dark mode (system theme, falling back to the app theme). False if it cannot be read.</summary>
    public static bool IsDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme") ?? key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
