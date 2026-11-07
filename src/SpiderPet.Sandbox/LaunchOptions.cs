namespace SpiderPet.Sandbox;

/// <summary>Command-line switches, parsed once in <see cref="Program.Main"/>.</summary>
internal static class LaunchOptions
{
    /// <summary>Transparent full-screen overlay on the real desktop (Windows only for now). Default on Windows.</summary>
    public static bool Overlay { get; private set; }

    /// <summary>Overlay that does not let clicks through, to debug transparency separately from click-through.</summary>
    public static bool Solid { get; private set; }

    public static void Parse(string[] args)
    {
        bool mock = args.Contains("--mock", StringComparer.OrdinalIgnoreCase);
        Solid = args.Contains("--solid", StringComparer.OrdinalIgnoreCase);
        Overlay = !mock && OperatingSystem.IsWindows();
    }
}
