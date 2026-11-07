namespace SpiderPet.Core;

/// <summary>
/// The spider's decision layer: for now it only decides <b>where to look</b>.
/// By default the spider watches the cursor; while it is on the bare desktop (no window under it) and a
/// desktop icon is close enough, it looks at that icon instead.
/// Call <see cref="Update"/> once per frame and assign the result to <see cref="Spider.LookTarget"/>.
/// </summary>
public sealed class SpiderBrain
{
    /// <summary>An icon this close (in spider body units, see <see cref="Spider.PixelsPerUnit"/>) catches the spider's attention.</summary>
    public double AttentionRadiusUnits { get; set; } = 28.0;

    /// <summary>Once looking at an icon, the spider only loses interest beyond radius * this factor (prevents flicker at the edge).</summary>
    public double ReleaseFactor { get; set; } = 1.3;

    /// <summary>Icons closer than this many pixels are ignored: looking at a point under the body is meaningless.</summary>
    public double MinDistance { get; set; } = 8.0;

    /// <summary>Master switch for the icon behaviour; when false the spider always looks at the cursor.</summary>
    public bool LookAtIcons { get; set; } = true;

    /// <summary>The icon the spider is currently looking at, or null when it is looking at the cursor.</summary>
    public DesktopIcon? Attention { get; private set; }

    /// <summary>True when no window covers the spider, i.e. it is standing on the desktop.</summary>
    public bool OnDesktop { get; private set; }

    /// <summary>Returns the point the spider should look at.</summary>
    public Vec2 Update(Spider spider, DesktopSnapshot snapshot)
    {
        OnDesktop = true;
        foreach (DesktopWindow w in snapshot.Windows)
        {
            if (w.Bounds.Contains(spider.Position))
            {
                OnDesktop = false;
                break;
            }
        }

        IReadOnlyList<DesktopIcon>? icons = snapshot.Icons;
        if (!LookAtIcons || !OnDesktop || icons is null || icons.Count == 0)
        {
            Attention = null;
            return snapshot.Cursor;
        }

        double enter = AttentionRadiusUnits * spider.PixelsPerUnit;
        double leave = enter * ReleaseFactor;
        int currentIndex = Attention?.Index ?? -1;

        DesktopIcon? best = null;
        double bestDistance = double.MaxValue;
        foreach (DesktopIcon icon in icons)
        {
            double d = (icon.Center - spider.Position).Length;
            double limit = icon.Index == currentIndex ? leave : enter;
            if (d < MinDistance || d > limit || d >= bestDistance)
                continue;
            best = icon;
            bestDistance = d;
        }

        Attention = best;
        return best?.Center ?? snapshot.Cursor;
    }
}
