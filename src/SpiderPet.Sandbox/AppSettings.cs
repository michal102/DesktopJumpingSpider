namespace SpiderPet.Sandbox;

/// <summary>User-facing toggles shared by the overlay, its right-click menu, the tray and the settings window.</summary>
public sealed class AppSettings
{
    private bool _showWindowOutlines = false;
    private bool _showInfoPanel = false;
    private bool _showGlow;
    private bool _pilotMode= true;
    private double _baseSpeed = 180.0;
    private bool _showSpider = true;
    private bool _showIconMarks = false;
    private bool _lookAtIcons = true;
    public bool ShowSpider { get => _showSpider; set => Set(ref _showSpider, value); }

    /// <summary>Outline every desktop icon the program has found.</summary>
    public bool ShowIconMarks { get => _showIconMarks; set => Set(ref _showIconMarks, value); }

    /// <summary>When standing on the desktop, look at a desktop icon that is close instead of at the cursor.</summary>
    public bool LookAtIcons { get => _lookAtIcons; set => Set(ref _lookAtIcons, value); }

    /// <summary>Raised after any value actually changed.</summary>
    public event Action? Changed;

    /// <summary>Red outlines around the tracked windows (the one the spider stands on is amber).</summary>
    public bool ShowWindowOutlines { get => _showWindowOutlines; set => Set(ref _showWindowOutlines, value); }

    /// <summary>The small text panel in the corner.</summary>
    public bool ShowInfoPanel { get => _showInfoPanel; set => Set(ref _showInfoPanel, value); }

    /// <summary>Soft light glow under the spider so it stays visible on dark backgrounds.</summary>
    public bool ShowGlow { get => _showGlow; set => Set(ref _showGlow, value); }

    /// <summary>Keyboard control (WASD/arrows, shift, +/-), read globally.</summary>
    public bool PilotMode { get => _pilotMode; set => Set(ref _pilotMode, value); }

    /// <summary>Walking speed in px/s (before sprint).</summary>
    public double BaseSpeed { get => _baseSpeed; set => Set(ref _baseSpeed, value); }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }
}
