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
        new DesktopWindow("Editor",   new RectD(60, 70, 420, 300)),
        new DesktopWindow("Browser",  new RectD(560, 50, 520, 360)),
        new DesktopWindow("Terminal", new RectD(130, 470, 360, 230)),
        new DesktopWindow("Chat",     new RectD(830, 510, 300, 210)),
        new DesktopWindow("Dialog",   new RectD(560, 470, 190, 110)),
    };

    public Vec2 Cursor { get; set; } = new Vec2(640, 200);

    public IReadOnlyList<DesktopWindow> Windows => _windows;

    public DesktopSnapshot GetSnapshot() => new(Cursor, _windows);
}
