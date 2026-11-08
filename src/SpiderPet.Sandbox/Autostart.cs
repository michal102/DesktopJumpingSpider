using Microsoft.Win32;

namespace SpiderPet.Sandbox;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopJumpingSpider";

    public static string? ExePath
    {
        get
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return null;

            // not "dotnet run"
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return null;

            // not an IDE / debug run
            if (System.Diagnostics.Debugger.IsAttached) return null;

            // not a plain build output (...\bin\Debug\... or ...\bin\Release\...), only a publish folder counts
            string p = path.Replace('/', '\\');
            bool buildOutput = p.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
                               || p.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
            if (buildOutput && !p.Contains(@"\publish\", StringComparison.OrdinalIgnoreCase)) return null;

            return path;
        }
    }

    public static bool IsSupported => OperatingSystem.IsWindows() && ExePath is not null;

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) is string value
                       && ExePath is string exe
                       && value.Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }

    public static bool Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                if (ExePath is not string exe) return false;
                key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
            }
            else key.DeleteValue(ValueName, false);
            return true;
        }
        catch (Exception) { return false; }
    }
}