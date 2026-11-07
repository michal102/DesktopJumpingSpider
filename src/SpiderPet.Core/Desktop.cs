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

/// <param name="Cursor">Real mouse position.</param>
/// <param name="Windows">Visible top-level windows, <b>frontmost first</b> (z-order).</param>
public sealed record DesktopSnapshot(Vec2 Cursor, IReadOnlyList<DesktopWindow> Windows);

/// <summary>
/// The only thing the pet will ever need to know about the outside world.
/// The sandbox implements it with mock data; real per-OS implementations (Win32 / X11 / macOS)
/// sit behind this same interface.
/// </summary>
public interface IDesktopSource
{
    DesktopSnapshot GetSnapshot();
}
