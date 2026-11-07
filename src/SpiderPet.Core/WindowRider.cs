namespace SpiderPet.Core;

/// <summary>
/// "The spider is standing on a window." Tracks which window is under the spider and, when that
/// window is dragged, returns how far the spider must be carried along with it.
///
/// Rules (so that merely dragging some window across the spider does not kidnap it):
///  * a window becomes the spider's perch only when it is the frontmost window under the spider
///    <b>and</b> has been sitting still for <see cref="SettleTime"/> seconds;
///  * once perched, the spider is carried while that window moves, even though it is still moving;
///  * the spider lets go when it is grabbed, walks off the window, or the window disappears
///    (closed or minimised) - it then simply stays where it is.
/// Resizing a window does not carry the spider (only a pure move does).
/// </summary>
public sealed class WindowRider
{
    /// <summary>Seconds a window must be motionless before the spider will land on it.</summary>
    public const double SettleTime = 0.2;

    private sealed class Track
    {
        public RectD Bounds;
        public double Still;
        public int Generation;
    }

    private readonly Dictionary<long, Track> _tracks = new();
    private readonly List<long> _stale = new();
    private int _generation;

    /// <summary>Id of the window the spider is standing on, or null.</summary>
    public long? RidingId { get; private set; }

    /// <summary>The window the spider is standing on (as of the last update), or null.</summary>
    public DesktopWindow? Riding { get; private set; }

    /// <summary>
    /// Call once per frame <b>before</b> moving the spider. Returns the displacement to apply to the
    /// spider this frame (zero unless the window it is standing on was moved).
    /// </summary>
    /// <param name="dt">Seconds since the previous call.</param>
    /// <param name="position">Spider position in the same space as the window bounds.</param>
    /// <param name="windows">Frontmost-first window list.</param>
    /// <param name="grabbed">True while the user holds the spider.</param>
    public Vec2 Update(double dt, Vec2 position, IReadOnlyList<DesktopWindow> windows, bool grabbed)
    {
        Vec2 carry = Vec2.Zero;

        // 1) carry along with the perch, measured against where the window was last frame
        if (RidingId is long rid)
        {
            DesktopWindow? perch = Find(windows, rid);
            if (grabbed || perch is null || !_tracks.TryGetValue(rid, out Track? old))
            {
                RidingId = null;
            }
            else if (old.Bounds.Contains(position)
                     && old.Bounds.Width == perch.Bounds.Width
                     && old.Bounds.Height == perch.Bounds.Height)
            {
                carry = new Vec2(perch.Bounds.X - old.Bounds.X, perch.Bounds.Y - old.Bounds.Y);
            }
        }

        // 2) refresh stillness bookkeeping for every window
        _generation++;
        foreach (DesktopWindow w in windows)
        {
            if (w.Id == 0) continue;
            if (_tracks.TryGetValue(w.Id, out Track? t))
            {
                if (t.Bounds == w.Bounds) t.Still += dt;
                else { t.Bounds = w.Bounds; t.Still = 0.0; }
                t.Generation = _generation;
            }
            else
            {
                _tracks[w.Id] = new Track { Bounds = w.Bounds, Still = 0.0, Generation = _generation };
            }
        }
        _stale.Clear();
        foreach ((long id, Track t) in _tracks)
            if (t.Generation != _generation)
                _stale.Add(id);
        foreach (long id in _stale)
            _tracks.Remove(id);

        // 3) decide the perch for the spider's new position
        Vec2 p = position + carry;

        if (grabbed)
        {
            RidingId = null;
        }
        else
        {
            if (RidingId is long cur)
            {
                DesktopWindow? perch = Find(windows, cur);
                if (perch is null || !perch.Bounds.Contains(p))
                    RidingId = null;
            }

            DesktopWindow? front = null;
            foreach (DesktopWindow w in windows)
            {
                if (w.Id != 0 && w.Bounds.Contains(p)) { front = w; break; }
            }

            if (front is null)
            {
                RidingId = null;
            }
            else if (front.Id != RidingId
                     && _tracks.TryGetValue(front.Id, out Track? ft)
                     && ft.Still >= SettleTime)
            {
                RidingId = front.Id;
            }
        }

        Riding = RidingId is long id2 ? Find(windows, id2) : null;
        return carry;
    }

    private static DesktopWindow? Find(IReadOnlyList<DesktopWindow> windows, long id)
    {
        foreach (DesktopWindow w in windows)
            if (w.Id == id)
                return w;
        return null;
    }
}
