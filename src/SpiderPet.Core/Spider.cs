namespace SpiderPet.Core;

/// <summary>One of the eight legs. Read-only for consumers; all state is driven by <see cref="Spider"/>.</summary>
public sealed class SpiderLeg
{
    internal SpiderLeg(int index, int side)
    {
        Index = index;
        Side = side;
        Group = (index + (side > 0 ? 1 : 0)) % 2;
        Outline = Array.Empty<Vec2>();
    }
    
    /// <summary>Exposes the calculated IK joint positions: [0] Hip, [1] Knee, [2] Ankle, [3] Tip.</summary>
    public IReadOnlyList<Vec2> Joints => new[] { Hip, Knee, Ankle, Tip };

    /// <summary>Leg pair, 0 = front (leg I) .. 3 = rear (leg IV).</summary>
    public int Index { get; }

    /// <summary>-1 = left, +1 = right.</summary>
    public int Side { get; }

    /// <summary>Alternating-tetrapod group (0 or 1).</summary>
    public int Group { get; }

    public bool Stepping { get; internal set; }
    public double Lift { get; internal set; }
    public bool Clamped { get; internal set; }

    /// <summary>Where the foot is planted (or flying to), world pixels.</summary>
    public Vec2 Foot { get; internal set; }

    public Vec2 Hip { get; internal set; }
    public Vec2 Knee { get; internal set; }
    public Vec2 Ankle { get; internal set; }
    public Vec2 Tip { get; internal set; }

    /// <summary>Closed polygon of the whole leg (soft tapered stroke), world pixels.</summary>
    public Vec2[] Outline { get; internal set; }

    internal Vec2 HipLocal;
    internal Vec2 HomeLocal;
    internal double SegA, SegB, SegC, Full, Rho, Sign, MinReach;
    internal double WidthBase, WidthMid, WidthTip;
    internal Vec2 StepFrom, StepTo;
    internal double StepT, StepDur;
    internal double Err;
}

/// <summary>
/// Top-down jumping spider: procedural alternating-tetrapod gait, soft 3-segment leg IK,
/// and a body that bends at the pedicel when turning.
///
/// Conventions: screen space (+X right, +Y down), pixels. Heading 0 deg = up, clockwise positive.
/// Local frame: +X = spider's right, -Y = forward.
/// </summary>
public sealed class Spider
{
    // ---------------------------------------------------------------- control surface
    /// <summary>Top speed in px/s when <see cref="MoveInput"/> has length 1. The "speed variable".</summary>
    public double Speed { get; set; } = 110.0;

    /// <summary>px/s^2 while speeding up. High values give the snappy start of a real jumping spider.</summary>
    public double Acceleration { get; set; } = 2000.0;

    /// <summary>px/s^2 while stopping.</summary>
    public double Deceleration { get; set; } = 3600.0;

    /// <summary>Desired direction in screen space, length 0..1 (longer vectors are normalised).</summary>
    public Vec2 MoveInput { get; set; }

    /// <summary>World point the body turns to face. Null = face the direction of travel.</summary>
    public Vec2? LookTarget { get; set; }

    /// <summary>Max body turn rate, deg/s.</summary>
    public double TurnRate { get; set; } = 600.0;

    /// <summary>Turn responsiveness (1/s): desired turn rate = error * gain, capped by <see cref="TurnRate"/>.</summary>
    public double TurnGain { get; set; } = 14.0;

    /// <summary>Angular acceleration limit, deg/s^2.</summary>
    public double TurnAcceleration { get; set; } = 5000.0;

    /// <summary>Largest abdomen bend relative to the carapace, degrees.</summary>
    public double MaxBendDeg { get; set; } = 38.0;

    // ---------------------------------------------------------------- state
    /// <summary>Centre of the carapace; the origin all angles are measured from.</summary>
    public Vec2 Position { get; set; }

    public Vec2 Velocity { get; set; }

    /// <summary>
    /// Moves the whole spider by <paramref name="delta"/> <b>including its planted feet</b>, as if the
    /// ground under it slid (a window being dragged). Unlike setting <see cref="Position"/> it does not
    /// make the legs stretch and re-step.
    /// </summary>
    public void Translate(Vec2 delta)
    {
        Position += delta;
        foreach (SpiderLeg leg in _legs)
        {
            leg.Foot += delta;
            leg.StepFrom += delta;
            leg.StepTo += delta;
        }
    }

    /// <summary>Carapace heading in degrees, 0 = up, clockwise positive, (-180, 180].</summary>
    public double HeadingDeg { get; private set; }

    /// <summary>Abdomen heading in degrees (same convention). Lags behind the carapace in turns.</summary>
    public double AbdomenDeg { get; private set; }

    public double AngularVelocityDeg { get; private set; }

    public double PixelsPerUnit { get; }

    public IReadOnlyList<SpiderLeg> Legs => _legs;
    public Vec2[] CarapaceOutline => _carapaceWorld;
    public Vec2[] AbdomenOutline => _abdomenWorld;

    /// <summary>Two small pedipalps (left, right), world pixels.</summary>
    public Vec2[][] PalpOutlines { get; } = new[] { Array.Empty<Vec2>(), Array.Empty<Vec2>() };

    // ---------------------------------------------------------------- tuning constants (units unless noted)
    private const double IdleThr = 0.3;
    private const double MinSwing = 0.04;
    private const double MaxSwing = 0.4;         // Changed from 0.22 (Allows longer air-time when settling)
    private const double BaseSwingSpeed = 7.0;    // Changed from 16.0 (Slows down idle settling steps)
    private const double SwingSpeedMult = 1.5;    // Changed from 4.5 (Slows down moving settling steps)
    private const double SwingFrac = 0.5;        // Changed from 0.4 (Leg spends more of the gait cycle in the air)
    private const double FMax = 7.0;              // Changed from 16.0 (Caps the maximum cycles per second)
    private const double StrideHalf = 1.3;       // half the body travel per gait cycle // fraction of the cycle a leg group is in the air
    private const double FMin = 2.0;              // gait cycles per second
    private const double MoveEnter = 1.5;         // units/s
    private const double MoveExit = 0.8;
    private const double TurnRadius = 5.5;
    private const double LiftPull = 0.35;
    private const double LiftWidth = 0.10;
    private const double PedicelY = 2.2;
    private const double AbdomenOverlap = 0.6;
    private const double AbdomenStiffness = 160.0;
    private const double AbdomenDamping = 0.6;
    private const double DragFraction = 0.985;
    
    /// <summary>True while the spider is being held by the mouse.</summary>
    public bool IsGrabbed { get; set; }
    
    private double _grabTime;
    

    private static readonly (double S, double W)[] CarapaceProfile =
    {
        // Matches the wider head and narrower pedicel connection of the SVG
        (0.00, 0.45), (0.10, 0.70), (0.25, 0.95), (0.45, 1.00), 
        (0.65, 0.90), (0.85, 0.65), (1.00, 0.35)
    };

    private static readonly (double S, double W)[] AbdomenProfile =
    {
        // Matches the large, bulbous, slightly teardrop-shaped abdomen of the SVG
        (0.00, 0.40), (0.15, 0.85), (0.35, 1.00), (0.55, 0.98), 
        (0.75, 0.80), (0.90, 0.45), (0.95, 0.25), (1.00, 0.15)
    };

    /// <summary>
    /// Per-leg anatomy. Leg I is stout and held forward, II is the shortest and thinnest,
    /// III is a bit longer, IV is the longest and sweeps backward.
    /// </summary>
    private readonly record struct LegDef(
        double Alpha,                 // rest direction, degrees from forward toward the outside
        double Femur, double Tibia, double Meta,
        double HipX, double HipY,
        double W0, double W1, double W2,
        double HintX, double HintY,   // which way the leg bows (x mirrored by side)
        double Rho);                  // distal-joint coupling

    private static readonly LegDef[] LegDefs =
    {
        // Leg I: Thickest, forward-reaching
        new(15.0, 3.8, 3.2, 2.5, 1.40, -1.80, 1.10, 0.90, 0.30, 1.0, 0.0, 0.35),
        
        // Leg II: Shorter, wider angle
        new(50.0, 2.8, 2.6, 2.2, 1.65, -0.70, 0.75, 0.55, 0.20, 1.0, -0.2, 0.45),
        
        // Leg III: Reaching back, similar to II
        new(115.0, 3.1, 2.8, 2.4, 1.65, 0.40, 0.80, 0.60, 0.20, 1.0, 0.2, 0.45),
        
        // Leg IV: Longest, sweeping back, thinner tapers
        new(165.0, 4.2, 3.6, 3.0, 1.40, 1.40, 0.85, 0.65, 0.15, 1.0, 0.0, 0.50),
    };

    // ---------------------------------------------------------------- internals
    private readonly double _u;
    private readonly List<SpiderLeg> _legs = new();
    private readonly Vec2[] _carapaceLocal;
    private readonly Vec2[] _carapaceWorld;
    private readonly Vec2[] _abdomenLocal;
    private readonly Vec2[] _abdomenWorld;
    private readonly int[] _steppingCount = new int[2];
    private readonly double[] _trigger = new double[2];
    private double _abdomenOmega;
    private double _cmdSpeed;
    private double _cmdTurn;
    private double _palpPhase;
    private double _phase;
    private bool _moving;

    public Spider(Vec2 position, double headingDeg = 0.0, double pixelsPerUnit = 3.0)
    {
        _u = pixelsPerUnit;
        PixelsPerUnit = pixelsPerUnit;
        Position = position;
        HeadingDeg = MathUtil.Wrap(headingDeg);
        AbdomenDeg = HeadingDeg;

        Vec2[] carapace = Outline.Build(CarapaceProfile, 5.0, 2.1, -2.5);
        _carapaceLocal = new Vec2[carapace.Length];
        for (int i = 0; i < carapace.Length; i++)
            _carapaceLocal[i] = carapace[i] * _u;
        _carapaceWorld = new Vec2[carapace.Length];

        Vec2[] abdomen = Outline.Build(AbdomenProfile, 6.6, 2.15, 0.0);
        _abdomenLocal = new Vec2[abdomen.Length];
        for (int i = 0; i < abdomen.Length; i++)
            _abdomenLocal[i] = new Vec2(abdomen[i].X * _u, (abdomen[i].Y - AbdomenOverlap) * _u);
        _abdomenWorld = new Vec2[abdomen.Length];

        for (int idx = 0; idx < LegDefs.Length; idx++)
        {
            LegDef def = LegDefs[idx];
            for (int side = -1; side <= 1; side += 2)
                _legs.Add(BuildLeg(idx, side, def));
        }

        foreach (SpiderLeg leg in _legs)
            leg.Foot = HomeWorld(leg);
        UpdatePose(true);
    }

    private const double LegWidthScale = 0.82;
    private const double LegLengthScale = 0.92;
    
    private SpiderLeg BuildLeg(int idx, int side, LegDef def)
    {
        var leg = new SpiderLeg(idx, side);
        double femur = def.Femur * LegLengthScale;
        double tibia = def.Tibia * LegLengthScale;
        double meta = def.Meta * LegLengthScale;

        double full = femur + tibia + meta;

        leg.HipLocal = new Vec2(side * def.HipX * _u, def.HipY * _u);
        leg.SegA = femur * _u;
        leg.SegB = tibia * _u;
        leg.SegC = meta * _u;
        leg.Full = full * _u;
        leg.Rho = def.Rho;
        leg.WidthBase = def.W0 * _u * LegWidthScale;
        leg.WidthMid = def.W1 * _u * LegWidthScale;
        leg.WidthTip = def.W2 * _u * LegWidthScale;

        var dir = new Vec2(side * Math.Sin(def.Alpha * MathUtil.DegToRad), -Math.Cos(def.Alpha * MathUtil.DegToRad));
        leg.HomeLocal = leg.HipLocal + dir * (LegIk.RestRatio * full * _u);
        leg.MinReach = LegIk.MinReach(leg.SegA, leg.SegB, leg.SegC, leg.Rho);

        // Pick the bend side whose bow points the way the hint says (mirrored for the other side).
        double bestSign = 1.0;
        double bestScore = double.NegativeInfinity;
        Vec2 mid = (leg.HipLocal + leg.HomeLocal) * 0.5;
        var hint = new Vec2(side * def.HintX, def.HintY);
        foreach (double sg in new[] { 1.0, -1.0 })
        {
            LegIk.Result r = LegIk.Solve(leg.HipLocal, leg.HomeLocal, leg.SegA, leg.SegB, leg.SegC, leg.Rho, sg, leg.MinReach);
            Vec2 off = ((r.Knee + r.Ankle) * 0.5 - mid).Normalized();
            double score = off.X * hint.X + off.Y * hint.Y;
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                bestSign = sg;
            }
        }
        leg.Sign = bestSign;
        return leg;
    }

    // ---------------------------------------------------------------- public helpers
    /// <summary>Resting (home) foot position of a leg for the current body pose, world pixels.</summary>
    public Vec2 HomeOf(SpiderLeg leg) => HomeWorld(leg);

    // ---------------------------------------------------------------- simulation
    public void Update(double dt)
    {
        if (!(dt > 0.0)) return;
        dt = Math.Min(dt, 0.05);
        int n = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120.0)));
        double h = dt / n;
        for (int i = 0; i < n; i++)
            Step(h, i == n - 1);
    }

    // ---------------------------------------------------------------- stepped turning

    /// <summary>
    /// Real spiders turn in a few discrete snaps rather than one smooth sweep. When true, a turn is split into steps
    /// (about one per <see cref="StepSpanDeg"/> degrees, at most <see cref="MaxSteps"/>): a small turn is a single
    /// quick move, a half turn takes several, each followed by a brief pause. Off = the smooth turn controller.
    /// </summary>
    public bool SteppedTurning { get; set; }

    /// <summary>A turn gets roughly one step per this many degrees (so 100 deg = 2 steps, 180 deg = 4).</summary>
    public double StepSpanDeg { get; set; } = 45.0;

    /// <summary>Largest number of steps one turn is split into.</summary>
    public int MaxSteps { get; set; } = 5;

    /// <summary>Average speed during a step, deg/s (each step is eased in and out, so it never looks like a snap).</summary>
    public double StepRateDeg { get; set; } = 420.0;

    /// <summary>Pause between two steps of one turn, seconds.</summary>
    public double StepPauseSeconds { get; set; } = 0.06;

    /// <summary>Heading errors smaller than this are left alone, degrees.</summary>
    public double StepMinDeg { get; set; } = 4.0;

    private int _stepsLeft;         // steps still to do in the current turn (0 = no turn planned)
    private double _stepFrom;       // heading at the start of the running step
    private double _stepDelta;      // signed size of the running step, 0 = not stepping
    private double _stepT;          // seconds into the running step
    private double _stepDur;        // duration of the running step
    private double _stepHold;       // seconds to wait before the next step

    private void SteppedTurn(double dt, double? target)
    {
        if (_stepDelta == 0.0)
        {
            if (_stepHold > 0.0)
                _stepHold -= dt;

            if (target is not double tgt)
            {
                _stepsLeft = 0;
            }
            else
            {
                double delta = MathUtil.Wrap(tgt - HeadingDeg);
                if (Math.Abs(delta) < StepMinDeg)
                {
                    _stepsLeft = 0;
                }
                else if (_stepHold <= 0.0)
                {
                    // plan a new turn: the number of steps depends on how far there is to go
                    if (_stepsLeft <= 0)
                        _stepsLeft = Math.Clamp((int)Math.Round(Math.Abs(delta) / StepSpanDeg), 1, Math.Max(1, MaxSteps));

                    // each step covers an equal share of what is left, so a moving target is simply followed
                    _stepDelta = delta / _stepsLeft;
                    _stepsLeft--;
                    _stepFrom = HeadingDeg;
                    _stepT = 0.0;
                    _stepDur = Math.Clamp(Math.Abs(_stepDelta) / StepRateDeg, 0.07, 0.35);
                }
            }
        }

        if (_stepDelta == 0.0)
        {
            AngularVelocityDeg = 0.0;
            _cmdTurn = 0.0;
            return;
        }

        double before = HeadingDeg;
        _stepT = Math.Min(_stepDur, _stepT + dt);
        double k = _stepT / _stepDur;
        double eased = k * k * (3.0 - 2.0 * k);
        HeadingDeg = MathUtil.Wrap(_stepFrom + _stepDelta * eased);
        double moved = MathUtil.Wrap(HeadingDeg - before);

        // The gait and the foot prediction read these; a fraction of the real speed keeps the legs calm.
        AngularVelocityDeg = moved / dt * 0.35;
        _cmdTurn = Math.Sign(moved) * StepRateDeg * 0.35;

        if (_stepT >= _stepDur)
        {
            _stepDelta = 0.0;
            _stepHold = _stepsLeft > 0 ? StepPauseSeconds : 0.0;
        }
    }

    // ---------------------------------------------------------------- reaching

    /// <summary>
    /// A world point the two front legs reach for, like hands towards something held out to the spider.
    /// Null = normal stance. The legs ease in and out, so setting and clearing it is always smooth.
    /// </summary>
    public Vec2? ReachTarget { get; set; }

    /// <summary>0..1: how far into the reach pose the front legs are right now.</summary>
    public double ReachAmount => _reach;

    private double _reach;
    private double _reachTime;

    private Vec2 HomeWorld(SpiderLeg leg) => Position + GetDynamicHomeLocal(leg).Rotated(HeadingDeg);

    private Vec2 HomeAt(SpiderLeg leg, double lead)
    {
        // 1. Predict future core position and heading
        Vec2 p = Position + Velocity * lead;
        double h = HeadingDeg + AngularVelocityDeg * lead;
        
        // 2. Apply the widened sprawling stance for the future heading
        Vec2 baseTarget = p + GetDynamicHomeLocal(leg).Rotated(h);
        
        // 3. Kinematic over-reach (crucial for natural strafing)
        // Forces the spider to actively reach into its movement path
        Vec2 strafeBias = Velocity * (lead * 0.4); 
        
        return baseTarget + strafeBias;
    }

    private void Step(double dt, bool buildGeometry)
    {
        // ---- heading
        double? target = null;
        if (LookTarget is Vec2 look && (look - Position).Length > 6.0)
            target = MathUtil.HeadingFromTo(Position, look);
        else if (LookTarget is null && Velocity.Length > 5.0)
            target = MathUtil.HeadingFromTo(Vec2.Zero, Velocity);

        if (SteppedTurning)
        {
            SteppedTurn(dt, target);
        }
        else
        {
            _stepsLeft = 0;
            _stepDelta = 0.0;

            double want = 0.0;
            if (target is double tgt)
            {
                double delta = MathUtil.Wrap(tgt - HeadingDeg);
                want = Math.Clamp(delta * TurnGain, -TurnRate, TurnRate);
            }
            _cmdTurn = want;
            AngularVelocityDeg = MathUtil.MoveToward(AngularVelocityDeg, want, TurnAcceleration * dt);
            HeadingDeg = MathUtil.Wrap(HeadingDeg + AngularVelocityDeg * dt);
        }

        // ---- velocity
        Vec2 input = MoveInput;
        double inputLen = input.Length;
        if (inputLen > 1.0)
        {
            input = input * (1.0 / inputLen);
            inputLen = 1.0;
        }
        Vec2 desired = input * Speed;
        _cmdSpeed = desired.Length;
        Vec2 dv = desired - Velocity;
        double rate = (inputLen > 1e-6 ? Acceleration : Deceleration) * dt;
        double dl = dv.Length;
        Velocity = dl <= rate ? desired : Velocity + dv * (rate / dl);
        Position = Position + Velocity * dt;

        // ---- abdomen: damped spring chasing the carapace heading (this is the body bend)
        double cdamp = 2.0 * AbdomenDamping * Math.Sqrt(AbdomenStiffness);
        double diff = MathUtil.Wrap(HeadingDeg - AbdomenDeg);
        _abdomenOmega += (AbdomenStiffness * diff - cdamp * _abdomenOmega) * dt;
        AbdomenDeg = MathUtil.Wrap(AbdomenDeg + _abdomenOmega * dt);
        double rel = MathUtil.Wrap(AbdomenDeg - HeadingDeg);
        if (Math.Abs(rel) > MaxBendDeg)
        {
            rel = Math.CopySign(MaxBendDeg, rel);
            AbdomenDeg = MathUtil.Wrap(HeadingDeg + rel);
            if (_abdomenOmega * rel > 0.0) _abdomenOmega = 0.0;
        }

        _palpPhase += dt * (2.5 + Velocity.Length / (40.0 * _u) * 6.0);

        if (IsGrabbed)
            _grabTime += dt;

        _reach = MathUtil.MoveToward(_reach, ReachTarget is null ? 0.0 : 1.0, dt / 0.2);
        if (_reach > 0.0)
            _reachTime += dt;
        else
            _reachTime = 0.0;

        UpdateLegs(dt);
        UpdatePose(buildGeometry);
    }

    private void BeginStep(SpiderLeg leg, double dur, double tail)
    {
        leg.StepTo = HomeAt(leg, dur + tail);
        leg.StepFrom = leg.Foot;
        leg.StepDur = dur;
        leg.StepT = 0.0;
        leg.Stepping = true;
    }

    private void BeginSettleStep(SpiderLeg leg)
    {
        double speed = Velocity.Length;
        double swingSpeed = Math.Max(BaseSwingSpeed * _u, SwingSpeedMult * speed);
        double dist = (HomeWorld(leg) - leg.Foot).Length;
        double dur = Math.Clamp(dist / swingSpeed, MinSwing, MaxSwing);
        BeginStep(leg, dur, 0.0);
    }

    private void UpdateLegs(double dt)
    {
        double speed = Velocity.Length;
        double turnRad = Math.Max(Math.Abs(AngularVelocityDeg), Math.Abs(_cmdTurn)) * MathUtil.DegToRad;
        double rate = Math.Sqrt(Math.Pow(Math.Max(speed, _cmdSpeed), 2.0) + Math.Pow(turnRad * TurnRadius * _u, 2.0));

        // advance swings in progress
        foreach (SpiderLeg leg in _legs)
        {
            if (!leg.Stepping) continue;
            leg.StepT += dt / leg.StepDur;
            if (leg.StepT >= 1.0)
            {
                leg.StepT = 1.0;
                leg.Stepping = false;
                leg.Foot = leg.StepTo;
                leg.Lift = 0.0;
            }
            else
            {
                double s = leg.StepT * leg.StepT * (3.0 - 2.0 * leg.StepT);
                leg.Foot = leg.StepFrom.Lerp(leg.StepTo, s);
                leg.Lift = Math.Sin(Math.PI * leg.StepT);
            }
        }

        // moving / settling mode, with hysteresis
        if (!_moving && rate >= MoveEnter * _u)
        {
            _moving = true;
            _phase = -1e-6;
        }
        else if (_moving && rate < MoveExit * _u)
        {
            _moving = false;
        }

        if (_moving)
        {
            // Phase-driven alternating tetrapod: group 0 swings at phase 0, group 1 at phase 0.5.
            double f = Math.Clamp(rate / (2.0 * StrideHalf * _u), FMin, FMax);
            double prev = _phase;
            _phase += f * dt;
            double dur = SwingFrac / f;
            double tail = (1.0 - SwingFrac) / (2.0 * f);
            for (int g = 0; g < 2; g++)
            {
                double off = 0.5 * g;
                if (Math.Floor(_phase - off) > Math.Floor(prev - off))
                {
                    foreach (SpiderLeg leg in _legs)
                    {
                        if (leg.Group == g && !leg.Stepping)
                            BeginStep(leg, dur, tail);
                    }
                }
            }
            if (_phase > 1000.0) _phase -= 1000.0;
        }
        else
        {
            // Settling: step feet back to their home spots, one tetrapod group at a time.
            double thr = IdleThr * _u;
            _steppingCount[0] = 0;
            _steppingCount[1] = 0;
            _trigger[0] = 0.0;
            _trigger[1] = 0.0;
            foreach (SpiderLeg leg in _legs)
            {
                if (leg.Stepping)
                {
                    _steppingCount[leg.Group]++;
                }
                else
                {
                    leg.Err = (leg.Foot - HomeWorld(leg)).Length;
                    _trigger[leg.Group] = Math.Max(_trigger[leg.Group], leg.Err);
                }
            }

            int best = -1;
            double bestTrig = 0.0;
            for (int g = 0; g < 2; g++)
            {
                if (_steppingCount[1 - g] == 0 && _trigger[g] > thr && _trigger[g] > bestTrig)
                {
                    best = g;
                    bestTrig = _trigger[g];
                }
            }
            if (best >= 0)
            {
                foreach (SpiderLeg leg in _legs)
                {
                    if (leg.Group == best && !leg.Stepping && leg.Err > 0.35 * thr)
                        BeginSettleStep(leg);
                }
            }
        }
    }

    private void UpdatePose(bool buildGeometry)
    {
        foreach (SpiderLeg leg in _legs)
        {
            leg.Hip = Position + leg.HipLocal.Rotated(HeadingDeg);

            Vec2 dv = leg.Foot - leg.Hip;
            double d = dv.Length;
            leg.Clamped = false;
            if (!leg.Stepping && d > DragFraction * leg.Full)
            {
                // planted foot is out of reach: drag it along instead of letting the leg detach
                leg.Foot = leg.Hip + dv * (DragFraction * leg.Full / d);
                leg.Clamped = true;
            }

            Vec2 aim = leg.Foot;

            if (IsGrabbed)
            {
                // Give every leg its own slightly different squirm rhythm.
                double seed = leg.Index * 1.731 +
                              (leg.Side > 0 ? 2.417 : 0.0);

                double squirmSpeed = 4.0; // Default speed

                double phaseX = _grabTime * (7.0 + leg.Index * 0.41) * squirmSpeed + seed;
                double phaseY = _grabTime * (5.6 + leg.Index * 0.33) * squirmSpeed + seed * 1.7;

                // Tiny sideways and forward/backward movements.
                double squirmX =
                    Math.Sin(phaseX) * 0.18 +
                    Math.Sin(phaseX * 1.73 + 1.1) * 0.06;

                double squirmY =
                    Math.Sin(phaseY) * 0.13 +
                    Math.Sin(phaseY * 1.37 + 0.8) * 0.05;

                Vec2 squirm = new Vec2(
                    leg.Side * squirmX * _u,
                    squirmY * _u
                );

                // Apply the movement to the IK target, not the actual foot.
                aim += squirm.Rotated(HeadingDeg);
            }

            if (_reach > 0.0 && leg.Index == 0 && ReachTarget is Vec2 reachAt)
            {
                // Front legs go for the item from both sides and pat at it in turns.
                double e = _reach * _reach * (3.0 - 2.0 * _reach);
                double pat = Math.Sin(_reachTime * 9.0 + (leg.Side > 0 ? Math.PI : 0.0)) * 0.35 * _u;
                Vec2 want = reachAt + new Vec2(leg.Side * 0.9 * _u, pat).Rotated(HeadingDeg);
                Vec2 rv = want - leg.Hip;
                double rl = rv.Length;
                double lo = leg.MinReach * 1.05;
                double hi = leg.Full * 0.97;
                if (rl > hi) rv = rv * (hi / rl);
                else if (rl < lo && rl > 1e-6) rv = rv * (lo / rl);
                aim = aim.Lerp(leg.Hip + rv, e);
            }

            if (leg.Lift > 0.0)
                aim = leg.Hip + (aim - leg.Hip) * (1.0 - LiftPull * leg.Lift);

            LegIk.Result r = LegIk.Solve(leg.Hip, aim, leg.SegA, leg.SegB, leg.SegC, leg.Rho, leg.Sign, leg.MinReach);
            leg.Knee = r.Knee;
            leg.Ankle = r.Ankle;
            leg.Tip = r.Tip;

            if (buildGeometry)
            {
                double wf = 1.0 + LiftWidth * leg.Lift;
                double[] widths =
                {
                    leg.WidthBase * wf,
                    0.5 * (leg.WidthBase + leg.WidthMid) * 1.08 * wf,
                    leg.WidthMid * wf,
                    leg.WidthTip * wf,
                };
                leg.Outline = Stroke.Build(new[] { leg.Hip, r.Knee, r.Ankle, r.Tip }, widths);
            }
        }

        if (!buildGeometry) return;

        for (int i = 0; i < _carapaceLocal.Length; i++)
            _carapaceWorld[i] = Position + _carapaceLocal[i].Rotated(HeadingDeg);

        Vec2 pedicel = Position + new Vec2(0.0, PedicelY * _u).Rotated(HeadingDeg);
        for (int i = 0; i < _abdomenLocal.Length; i++)
            _abdomenWorld[i] = pedicel + _abdomenLocal[i].Rotated(AbdomenDeg);

        double sway = Math.Sin(_palpPhase);
        for (int k = 0; k < 2; k++)
        {
            int side = k == 0 ? -1 : 1;
            Vec2 pBase = Position + new Vec2(side * 0.75 * _u, -2.4 * _u).Rotated(HeadingDeg);
            double a1 = 18.0 + 4.0 * sway;
            double a2 = -12.0 + 5.0 * sway;
            Vec2 d1 = new Vec2(side * Math.Sin(a1 * MathUtil.DegToRad), -Math.Cos(a1 * MathUtil.DegToRad)).Rotated(HeadingDeg);
            Vec2 d2 = new Vec2(side * Math.Sin(a2 * MathUtil.DegToRad), -Math.Cos(a2 * MathUtil.DegToRad)).Rotated(HeadingDeg);
            Vec2 p1 = pBase + d1 * (0.6 * _u);
            Vec2 p2 = p1 + d2 * (0.45 * _u);
            PalpOutlines[k] = Stroke.Build(new[] { pBase, p1, p2 }, new[] { 0.34 * _u, 0.28 * _u, 0.12 * _u }, 4);
        }
    }
    
    private Vec2 GetDynamicHomeLocal(SpiderLeg leg)
    {
        // Calculate how fast the spider is moving relative to max speed (0.0 to 1.0)
        double speedRatio = Math.Clamp(Velocity.Length / Math.Max(Speed, 1.0), 0.0, 1.0);
        
        // Widen the leg stance by up to 25% when moving fast
        double sprawlMult = 1.0 + (0.25 * speedRatio);
        
        return leg.HomeLocal * sprawlMult;
    }
}
