using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>
/// Converts a desktop source that speaks physical screen pixels into the overlay window's own
/// coordinates (device-independent pixels, origin at the overlay's top-left corner), which is the space
/// the spider lives in. The overlay updates <see cref="OriginX"/>, <see cref="OriginY"/> and
/// <see cref="Scale"/> whenever it moves or the display scaling changes.
/// </summary>
public sealed class OverlayDesktopSource : IDesktopSource
{
    private readonly IDesktopSource _inner;

    private IReadOnlyList<DesktopWindow>? _lastIn;
    private IReadOnlyList<DesktopWindow> _lastOut = Array.Empty<DesktopWindow>();
    private double _lastOx = double.NaN, _lastOy = double.NaN, _lastScale = double.NaN;

    public OverlayDesktopSource(IDesktopSource inner) => _inner = inner;

    /// <summary>Physical screen position of the overlay's top-left corner.</summary>
    public double OriginX { get; set; }
    public double OriginY { get; set; }

    /// <summary>Physical pixels per device-independent pixel (Avalonia's RenderScaling).</summary>
    public double Scale { get; set; } = 1.0;

    public DesktopSnapshot GetSnapshot()
    {
        DesktopSnapshot s = _inner.GetSnapshot();
        double scale = Scale > 0.0 ? Scale : 1.0;

        // Re-convert only when the source produced a new list or the mapping changed, and otherwise return the
        // same list instance: DesktopWatcher uses that to skip needless diffing.
        if (!ReferenceEquals(s.Windows, _lastIn) || OriginX != _lastOx || OriginY != _lastOy || scale != _lastScale)
        {
            var list = new List<DesktopWindow>(s.Windows.Count);
            foreach (DesktopWindow w in s.Windows)
            {
                RectD b = w.Bounds;
                list.Add(w with
                {
                    Bounds = new RectD((b.X - OriginX) / scale, (b.Y - OriginY) / scale, b.Width / scale, b.Height / scale),
                });
            }

            _lastOut = list;
            _lastIn = s.Windows;
            _lastOx = OriginX;
            _lastOy = OriginY;
            _lastScale = scale;
        }

        return new DesktopSnapshot(
            new Vec2((s.Cursor.X - OriginX) / scale, (s.Cursor.Y - OriginY) / scale),
            _lastOut);
    }
}
