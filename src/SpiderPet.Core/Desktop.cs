namespace SpiderPet.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>Half-open containment test: left/top edges inside, right/bottom edges outside.</summary>
    public bool Contains(Vec2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
}

/// <param name="Title">Window title (may be empty for mock windows).</param>
/// <param name="Bounds">Visible frame in the coordinate space of the <see cref="IDesktopSource"/>.</param>
/// <param name="Id">Stable identity of the window for as long as it exists (the HWND on Windows).
/// Needed to tell "this window moved" from "that window closed and another opened".</param>
public sealed record DesktopWindow(string Title, RectD Bounds, long Id = 0);

/// <summary>An icon sitting on the desktop (the icon image only, not its label).</summary>
/// <param name="Index">Position in the desktop's item list. Not a stable identity: it changes when icons are sorted or deleted.</param>
/// <param name="Bounds">Icon rectangle in the coordinate space of the <see cref="IDesktopSource"/>.</param>
public sealed record DesktopIcon(int Index, RectD Bounds)
{
    public Vec2 Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
}

/// <param name="Cursor">Real mouse position.</param>
/// <param name="Windows">Visible top-level windows, <b>frontmost first</b> (z-order).</param>
/// <param name="Icons">Desktop icons, or null when the source does not know about them.</param>
public sealed record DesktopSnapshot(
    Vec2 Cursor,
    IReadOnlyList<DesktopWindow> Windows,
    IReadOnlyList<DesktopIcon>? Icons = null);

/// <summary>
/// The only thing the pet will ever need to know about the outside world.
/// The sandbox implements it with mock data; real per-OS implementations (Win32 / X11 / macOS)
/// sit behind this same interface.
/// </summary>
public interface IDesktopSource
{
    DesktopSnapshot GetSnapshot();
}

/// <summary>
/// Optional capability of a desktop source: identify and move icons on the desktop.
/// Positions use the same coordinate space as <see cref="DesktopSnapshot"/>.
/// </summary>
public interface IDesktopIconControl
{
    /// <summary>Index of the desktop icon whose label equals one of <paramref name="displayNames"/> (case-insensitive), or -1.</summary>
    int FindIcon(IReadOnlyList<string> displayNames);

    /// <summary>
    /// Moves the icon so that its centre ends up at <paramref name="center"/>. Returns false when that is not possible
    /// (desktop icons hidden, explorer not reachable, auto-arrange on...). The shell may snap the icon to its grid.
    /// </summary>
    bool MoveIcon(int index, Vec2 center);
}
