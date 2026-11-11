namespace SpiderPet.Core;

/// <summary>
/// Notices a mouse that is being waved: moved back and forth a few times within a moment. It counts direction
/// reversals of at least <see cref="MinSwing"/> pixels on each axis and says "wave" when there are
/// <see cref="Reversals"/> of them within <see cref="WindowSeconds"/>.
/// </summary>
public sealed class WaveDetector
{
    private sealed class Axis
    {
        public double Extreme = double.NaN;
        public int Direction;           // 0 = not known yet, +1 rising, -1 falling
        public readonly List<double> Times = new();
    }

    private readonly Axis _x = new();
    private readonly Axis _y = new();
    private double _quietUntil;

    /// <summary>How far (px) the mouse must travel one way before a turn counts.</summary>
    public double MinSwing { get; set; } = 30.0;

    /// <summary>This many turns...</summary>
    public int Reversals { get; set; } = 3;

    /// <summary>...within this many seconds make a wave.</summary>
    public double WindowSeconds { get; set; } = 1.4;

    /// <summary>After a wave was reported, the next one needs this long.</summary>
    public double CooldownSeconds { get; set; } = 0.6;

    /// <summary>Forget everything (the mouse left the area, say).</summary>
    public void Reset()
    {
        _x.Direction = 0; _x.Extreme = double.NaN; _x.Times.Clear();
        _y.Direction = 0; _y.Extreme = double.NaN; _y.Times.Clear();
    }

    /// <summary>Feed the mouse position every frame. True on the frame a wave is recognised.</summary>
    public bool Update(double time, Vec2 cursor)
    {
        bool a = Feed(_x, cursor.X, time);
        bool b = Feed(_y, cursor.Y, time);
        if ((a || b) && time >= _quietUntil)
        {
            Reset();
            _quietUntil = time + CooldownSeconds;
            return true;
        }
        return false;
    }

    // true when this axis has enough recent turns
    private bool Feed(Axis axis, double value, double time)
    {
        if (axis.Direction == 0)
        {
            if (double.IsNaN(axis.Extreme))
                axis.Extreme = value;
            if (Math.Abs(value - axis.Extreme) >= MinSwing)
            {
                axis.Direction = value > axis.Extreme ? 1 : -1;
                axis.Extreme = value;
            }
        }
        else if (axis.Direction > 0)
        {
            if (value > axis.Extreme) axis.Extreme = value;
            else if (axis.Extreme - value >= MinSwing)
            {
                axis.Direction = -1;
                axis.Extreme = value;
                axis.Times.Add(time);
            }
        }
        else
        {
            if (value < axis.Extreme) axis.Extreme = value;
            else if (value - axis.Extreme >= MinSwing)
            {
                axis.Direction = 1;
                axis.Extreme = value;
                axis.Times.Add(time);
            }
        }

        axis.Times.RemoveAll(t => time - t > WindowSeconds);
        return axis.Times.Count >= Reversals;
    }
}
