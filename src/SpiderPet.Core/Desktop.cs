namespace SpiderPet.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed record DesktopWindow(string Title, RectD Bounds);

public sealed record DesktopSnapshot(Vec2 Cursor, IReadOnlyList<DesktopWindow> Windows);

/// <summary>
/// The only thing the pet will ever need to know about the outside world.
/// The sandbox implements it with mock data; later each OS gets a real implementation
/// (Win32 / X11 / macOS) behind this same interface.
/// </summary>
public interface IDesktopSource
{
    DesktopSnapshot GetSnapshot();
}
