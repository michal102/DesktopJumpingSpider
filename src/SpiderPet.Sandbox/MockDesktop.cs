using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>
/// Fake desktop for the sandbox: a cursor that follows the mouse over the sandbox view and a few
/// static rectangles standing in for application windows. Nothing reads them yet (by design);
/// later a real per-OS <see cref="IDesktopSource"/> replaces this class.
/// </summary>
public sealed class MockDesktop : IDesktopSource
{
    private readonly List<DesktopWindow> _windows = new()
    {
        new DesktopWindow("Editor",   new RectD(60, 70, 420, 300), 1),
        new DesktopWindow("Browser",  new RectD(560, 50, 520, 360), 2),
        new DesktopWindow("Terminal", new RectD(130, 470, 360, 230), 3),
        new DesktopWindow("Chat",     new RectD(830, 510, 300, 210), 4),
        new DesktopWindow("Dialog",   new RectD(560, 470, 190, 110), 5),
    };

    private readonly List<DesktopIcon> _icons = new()
    {
        new DesktopIcon(0, new RectD(1180, 60, 48, 48)),
        new DesktopIcon(1, new RectD(1180, 150, 48, 48)),
        new DesktopIcon(2, new RectD(1180, 240, 48, 48)),
        new DesktopIcon(3, new RectD(1180, 330, 48, 48)),
        new DesktopIcon(4, new RectD(1100, 60, 48, 48)),
    };

    public Vec2 Cursor { get; set; } = new Vec2(640, 200);

    public IReadOnlyList<DesktopIcon> Icons => _icons;

    public IReadOnlyList<DesktopWindow> Windows => _windows;

    public DesktopSnapshot GetSnapshot() => new(Cursor, _windows, _icons);
}
