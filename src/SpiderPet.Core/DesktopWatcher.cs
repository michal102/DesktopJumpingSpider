namespace SpiderPet.Core;

/// <summary>
/// Polls an <see cref="IDesktopSource"/> and turns the stream of snapshots into events,
/// so behaviour code can react to "a window opened / closed / moved or resized" instead of diffing lists itself.
/// Windows are matched by <see cref="DesktopWindow.Id"/>; windows with Id 0 are ignored for events.
/// </summary>
public sealed class DesktopWatcher
{
    private readonly IDesktopSource _source;
    private IReadOnlyList<DesktopWindow> _lastList;
    private Dictionary<long, DesktopWindow> _byId = new();
    private Dictionary<long, DesktopWindow> _scratch = new();

    public DesktopWatcher(IDesktopSource source)
    {
        _source = source;
        Current = source.GetSnapshot();
        _lastList = Current.Windows;
        Index(Current.Windows, _byId);
    }

    /// <summary>The most recent snapshot returned by <see cref="Poll"/>.</summary>
    public DesktopSnapshot Current { get; private set; }

    /// <summary>A window that was not present in the previous snapshot.</summary>
    public event Action<DesktopWindow>? WindowAppeared;

    /// <summary>A window from the previous snapshot that is gone now (closed, minimised, cloaked...).</summary>
    public event Action<DesktopWindow>? WindowClosed;

    /// <summary>Same window, different bounds: (before, after).</summary>
    public event Action<DesktopWindow, DesktopWindow>? WindowMoved;

    /// <summary>Takes a new snapshot, raises events for what changed, and returns it.</summary>
    public DesktopSnapshot Poll()
    {
        DesktopSnapshot snap = _source.GetSnapshot();
        Current = snap;

        // Sources may hand back the same list instance while nothing was re-scanned; skip the diff then.
        if (ReferenceEquals(snap.Windows, _lastList))
            return snap;
        _lastList = snap.Windows;

        _scratch.Clear();
        Index(snap.Windows, _scratch);

        foreach ((long id, DesktopWindow now) in _scratch)
        {
            if (_byId.TryGetValue(id, out DesktopWindow? before))
            {
                if (before.Bounds != now.Bounds)
                    WindowMoved?.Invoke(before, now);
            }
            else
            {
                WindowAppeared?.Invoke(now);
            }
        }

        foreach ((long id, DesktopWindow before) in _byId)
        {
            if (!_scratch.ContainsKey(id))
                WindowClosed?.Invoke(before);
        }

        (_byId, _scratch) = (_scratch, _byId);
        return snap;
    }

    /// <summary>Frontmost window containing <paramref name="p"/> in the current snapshot, or null.</summary>
    public DesktopWindow? FrontmostAt(Vec2 p)
    {
        foreach (DesktopWindow w in Current.Windows)
            if (w.Bounds.Contains(p))
                return w;
        return null;
    }

    private static void Index(IReadOnlyList<DesktopWindow> windows, Dictionary<long, DesktopWindow> into)
    {
        foreach (DesktopWindow w in windows)
            if (w.Id != 0)
                into[w.Id] = w;
    }
}
