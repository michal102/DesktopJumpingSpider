namespace SpiderPet.Core;

/// <summary>
/// Estimates how fast each window is moving from the stream of snapshots, so the spider can tell a window that is
/// being dragged towards it from one that just sits there. Feed it every frame; it only samples when the source
/// handed over a new window list.
/// </summary>
public sealed class WindowMotion
{
    private sealed class Track
    {
        public readonly List<(double T, RectD R)> Samples = new();
        public int Generation;
    }

    private readonly Dictionary<long, Track> _tracks = new();
    private IReadOnlyList<DesktopWindow>? _lastList;
    private int _generation;

    /// <summary>How much history is used for the estimate, seconds.</summary>
    public double HistorySeconds { get; set; } = 0.25;

    /// <summary>A window that has not changed for this long counts as standing still, seconds.</summary>
    public double StillAfterSeconds { get; set; } = 0.15;

    public void Update(double now, IReadOnlyList<DesktopWindow> windows)
    {
        if (ReferenceEquals(windows, _lastList)) return;   // nothing was re-read, so nothing moved
        _lastList = windows;
        _generation++;

        foreach (DesktopWindow w in windows)
        {
            if (w.Id == 0) continue;
            if (!_tracks.TryGetValue(w.Id, out Track? track))
                _tracks[w.Id] = track = new Track();

            track.Generation = _generation;
            if (track.Samples.Count == 0 || track.Samples[^1].R != w.Bounds)
                track.Samples.Add((now, w.Bounds));

            while (track.Samples.Count > 2 && now - track.Samples[0].T > HistorySeconds)
                track.Samples.RemoveAt(0);
        }

        // forget windows that are gone
        List<long>? dead = null;
        foreach ((long id, Track t) in _tracks)
            if (t.Generation != _generation)
                (dead ??= new List<long>()).Add(id);
        if (dead is not null)
            foreach (long id in dead)
                _tracks.Remove(id);
    }

    /// <summary>Velocity of the window's top-left corner in px/s; zero when it is still, being resized, or unknown.</summary>
    public Vec2 VelocityOf(long id, double now)
    {
        if (!_tracks.TryGetValue(id, out Track? track) || track.Samples.Count < 2)
            return Vec2.Zero;

        (double t1, RectD r1) = track.Samples[^1];
        if (now - t1 > StillAfterSeconds)
            return Vec2.Zero;

        (double t0, RectD r0) = track.Samples[0];
        double dt = t1 - t0;
        if (dt < 0.04)
            return Vec2.Zero;

        // a window that is being resized is not "coming at" anything
        if (Math.Abs(r1.Width - r0.Width) > 2.0 || Math.Abs(r1.Height - r0.Height) > 2.0)
            return Vec2.Zero;

        return new Vec2((r1.X - r0.X) / dt, (r1.Y - r0.Y) / dt);
    }
}
