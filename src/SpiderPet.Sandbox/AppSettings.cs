using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>What happens to a dropped file the spider approves of.</summary>
public enum DropMode
{
    /// <summary>Carried a short way off and laid down.</summary>
    Nearby,
    /// <summary>Carried to the spider's corner and tucked away there.</summary>
    Hoard,
    /// <summary>Carried to the mouse cursor and set down next to it, like a gift.</summary>
    Cursor,
}

/// <summary>User-facing toggles shared by the overlay, its right-click menu, the tray and the settings window.</summary>
public sealed class AppSettings
{
    private bool _showWindowOutlines = false;
    private bool _showInfoPanel = false;
    private bool _showGlow;
    private bool _pilotMode= false;
    private bool _wanderAround = true;
    private bool _avoidIcons = true;
    private bool _cursorInteraction = true;
    private bool _idleBehaviors = true;
    private bool _huntCursor = true;
    private bool _watchMedia = true;
    private RestSpot _restCorner = RestSpot.Auto;
    private RestSpot _watchCorner = RestSpot.Auto;
    private double _baseSpeed = 300.0;
    private bool _showSpider = true;
    private bool _showIconMarks = false;
    private bool _lookAtIcons = true;
    private bool _lookAtWindows = true;
    private bool _reactToChanges = true;
    private DropMode _dropStyle = DropMode.Hoard;
    private SpiderCorner _hoardCorner = SpiderCorner.TopRight;
    private bool _steppedTurning = true;
    private bool _lookAtText = true;
    private bool _dodgeWindows = true;
    public bool ShowSpider { get => _showSpider; set => Set(ref _showSpider, value); }

    /// <summary>Outline every desktop icon the program has found.</summary>
    public bool ShowIconMarks { get => _showIconMarks; set => Set(ref _showIconMarks, value); }

    /// <summary>When standing on the desktop, look at a desktop icon that is close instead of at the cursor.</summary>
    public bool LookAtIcons { get => _lookAtIcons; set => Set(ref _lookAtIcons, value); }

    /// <summary>Look at a window whose edge is close to the spider.</summary>
    public bool LookAtWindows { get => _lookAtWindows; set => Set(ref _lookAtWindows, value); }

    /// <summary>Look at sudden changes (window opened or closed, icon moved) for a second, before anything else.</summary>
    public bool ReactToChanges { get => _reactToChanges; set => Set(ref _reactToChanges, value); }

    /// <summary>Look at the text caret while someone types close to the spider.</summary>
    public bool LookAtText { get => _lookAtText; set => Set(ref _lookAtText, value); }

    /// <summary>Hop back a little from a window that is coming fast at the spider.</summary>
    public bool DodgeWindows { get => _dodgeWindows; set => Set(ref _dodgeWindows, value); }

    /// <summary>What the spider does with a file it likes (files it dislikes are always dumped in the far corner).</summary>
    public DropMode DropStyle { get => _dropStyle; set => Set(ref _dropStyle, value); }

    /// <summary>The spider's own corner, where it keeps its favourites; rubbish goes to the opposite one.</summary>
    public SpiderCorner HoardCorner { get => _hoardCorner; set => Set(ref _hoardCorner, value); }

    /// <summary>Turn in a few quick jerky snaps like a real spider instead of one smooth rotation.</summary>
    public bool SteppedTurning { get => _steppedTurning; set => Set(ref _steppedTurning, value); }

    /// <summary>Raised after any value actually changed.</summary>
    public event Action? Changed;

    /// <summary>Red outlines around the tracked windows (the one the spider stands on is amber).</summary>
    public bool ShowWindowOutlines { get => _showWindowOutlines; set => Set(ref _showWindowOutlines, value); }

    /// <summary>The small text panel in the corner.</summary>
    public bool ShowInfoPanel { get => _showInfoPanel; set => Set(ref _showInfoPanel, value); }

    /// <summary>Soft light glow under the spider so it stays visible on dark backgrounds.</summary>
    public bool ShowGlow { get => _showGlow; set => Set(ref _showGlow, value); }

    /// <summary>The spider walks around on its own, drifting towards screen and window edges. Paused while <see cref="PilotMode"/> is on.</summary>
    public bool WanderAround { get => _wanderAround; set => Set(ref _wanderAround, value); }

    /// <summary>While wandering, walk round the desktop icons instead of over them (only when there are few of them).</summary>
    public bool AvoidIcons { get => _avoidIcons; set => Set(ref _avoidIcons, value); }

    /// <summary>Watch the mouse when it is waved near the spider, and come over when double-clicked meanwhile.</summary>
    public bool CursorInteraction { get => _cursorInteraction; set => Set(ref _cursorInteraction, value); }

    /// <summary>Cleans itself now and then, and goes to a corner to sleep when nothing has happened for a long time.</summary>
    public bool IdleBehaviors { get => _idleBehaviors; set => Set(ref _idleBehaviors, value); }

    /// <summary>Sometimes stalks and pounces on the mouse cursor when it is close and moves slowly.</summary>
    public bool HuntCursor { get => _huntCursor; set => Set(ref _huntCursor, value); }

    /// <summary>Tucks away in a corner and watches along while a game or video is on.</summary>
    public bool WatchMedia { get => _watchMedia; set => Set(ref _watchMedia, value); }

    /// <summary>The corner it goes to when it sleeps (Auto = a free bottom corner).</summary>
    public RestSpot RestCorner { get => _restCorner; set => Set(ref _restCorner, value); }

    /// <summary>The corner it goes to when it watches a video, game or photos (Auto = a free bottom corner).</summary>
    public RestSpot WatchCorner { get => _watchCorner; set => Set(ref _watchCorner, value); }

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
