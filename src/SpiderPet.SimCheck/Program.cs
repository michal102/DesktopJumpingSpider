using SpiderPet.Core;

// Headless regression check for the spider simulation. No window, no Avalonia, runs the same on every OS.
// Exit code 0 = all good, 1 = something regressed.

const double Fps = 120.0;
int failures = 0;

RunScenario("walk straight, facing up", 4.0,
    new Spider(new Vec2(0, 0)),
    (s, t) =>
    {
        s.LookTarget = new Vec2(0, -10000);
        s.MoveInput = t > 0.3 && t < 3.0 ? new Vec2(0, -1) : Vec2.Zero;
    });

RunScenario("turn in place (up -> right -> down-left)", 2.8,
    new Spider(new Vec2(0, 0)),
    (s, t) =>
    {
        s.LookTarget = t < 0.5 ? new Vec2(0, -1000) : t < 1.6 ? new Vec2(1000, 0) : new Vec2(-300, 300);
        s.MoveInput = Vec2.Zero;
    });

RunScenario("strafe right while facing up", 3.0,
    new Spider(new Vec2(0, 0)),
    (s, t) =>
    {
        s.LookTarget = new Vec2(s.Position.X, -5000);
        s.MoveInput = new Vec2(1, 0);
    });

var slow = new Spider(new Vec2(0, 0)) { Speed = 25.0 };
RunScenario("slow creep (25 px/s)", 4.0, slow,
    (s, t) =>
    {
        s.LookTarget = new Vec2(0, -5000);
        s.MoveInput = new Vec2(0, -1);
    });

RunWindowChecks();
RunBrainChecks();
RunCarryChecks();
RunDropFeatureChecks();
RunStateChecks();
RunPokeAndSkitterChecks();
RunDeleteTypeDodgeChecks();

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

void RunScenario(string name, double seconds, Spider s, Action<Spider, double> controller)
{
    int clampedFrames = 0;
    int bothGroupsFrames = 0;
    int minPlanted = 8;
    double maxFootError = 0.0;
    bool nan = false;

    double dt = 1.0 / Fps;
    double t = 0.0;
    int frames = (int)(seconds * Fps);
    for (int i = 0; i < frames; i++)
    {
        controller(s, t);
        s.Update(dt);
        t += dt;

        bool g0 = false, g1 = false;
        int planted = 0;
        foreach (SpiderLeg leg in s.Legs)
        {
            if (leg.Stepping)
            {
                if (leg.Group == 0) g0 = true; else g1 = true;
            }
            else
            {
                planted++;
            }

            if (leg.Clamped && !leg.Stepping) clampedFrames++;
            maxFootError = Math.Max(maxFootError, (leg.Foot - s.HomeOf(leg)).Length / s.PixelsPerUnit);
            if (double.IsNaN(leg.Knee.X) || double.IsNaN(leg.Tip.X) || double.IsNaN(leg.Foot.X)) nan = true;
        }
        if (g0 && g1) bothGroupsFrames++;
        minPlanted = Math.Min(minPlanted, planted);
    }

    var problems = new List<string>();
    if (nan) problems.Add("NaN in leg geometry");
    if (minPlanted < 4) problems.Add($"fewer than 4 planted legs ({minPlanted})");
    if (bothGroupsFrames > 0) problems.Add($"both tetrapod groups airborne for {bothGroupsFrames} frames");
    if (clampedFrames > 10) problems.Add($"feet out of reach for {clampedFrames} frames");
    if (maxFootError > 4.5) problems.Add($"foot strayed {maxFootError:0.0} units from home");

    Console.WriteLine($"{(problems.Count == 0 ? "PASS" : "FAIL")}  {name}: min planted {minPlanted}, max foot error {maxFootError:0.0} u, out-of-reach frames {clampedFrames}");
    foreach (string p in problems) Console.WriteLine($"      - {p}");
    if (problems.Count > 0) failures++;
}

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok) failures++;
}

void RunWindowChecks()
{
    DesktopWindow Win(long id, double x, double y) => new("w" + id, new RectD(x, y, 300, 200), id);
    var inside = new Vec2(150, 100);

    // lands only after the window has been still for SettleTime, then is carried along with it
    var rider = new WindowRider();
    var list = new List<DesktopWindow> { Win(1, 0, 0) };
    Vec2 carry = rider.Update(0.016, inside, list, false);
    Check("not perched on the first frame", rider.RidingId is null && carry == Vec2.Zero);
    for (int i = 0; i < 20; i++) rider.Update(0.016, inside, list, false);
    Check("perches on a settled window", rider.RidingId == 1);
    carry = rider.Update(0.016, inside, new List<DesktopWindow> { Win(1, 40, 25) }, false);
    Check("carried by the window's move", carry == new Vec2(40, 25));

    // a different window dragged across the spider must not pick it up
    var r2 = new WindowRider();
    var desk = new List<DesktopWindow>();
    for (int i = 0; i < 10; i++) r2.Update(0.016, inside, desk, false);
    bool stolen = false;
    for (int i = 0; i < 30; i++)
    {
        r2.Update(0.016, inside, new List<DesktopWindow> { Win(2, i * 10 - 100, 0) }, false);
        stolen |= r2.RidingId is not null;
    }
    Check("dragged window does not kidnap the spider", !stolen);

    // closing the perch drops the spider; grabbing drops it too
    var r3 = new WindowRider();
    var one = new List<DesktopWindow> { Win(3, 0, 0) };
    for (int i = 0; i < 20; i++) r3.Update(0.016, inside, one, false);
    r3.Update(0.016, inside, new List<DesktopWindow>(), false);
    Check("perch closed -> no perch", r3.RidingId is null);
    for (int i = 0; i < 20; i++) r3.Update(0.016, inside, one, false);
    r3.Update(0.016, inside, one, true);
    Check("grabbed -> no perch", r3.RidingId is null);

    // watcher events
    var src = new ScriptedSource();
    var watcher = new DesktopWatcher(src);
    string log = "";
    watcher.WindowAppeared += w => log += "A" + w.Id;
    watcher.WindowMoved += (_, n) => log += "M" + n.Id;
    watcher.WindowClosed += w => log += "C" + w.Id;
    src.Windows = new List<DesktopWindow> { Win(1, 0, 0) }; watcher.Poll();
    src.Windows = new List<DesktopWindow> { Win(1, 5, 0), Win(2, 0, 0) }; watcher.Poll();
    src.Windows = new List<DesktopWindow> { Win(2, 0, 0) }; watcher.Poll();
    Check($"watcher events (got '{log}')", log == "A1M1A2C1");

    // Translate keeps feet under the body
    var sp = new Spider(new Vec2(100, 100));
    double before = (sp.Legs[0].Foot - sp.Position).Length;
    sp.Translate(new Vec2(50, -30));
    Check("Translate moves feet with the body", Math.Abs((sp.Legs[0].Foot - sp.Position).Length - before) < 1e-9);
}

void RunBrainChecks()
{
    var spider = new Spider(new Vec2(200, 200));   // 3 px per unit -> icons: 84 px (release 109), windows: 66 px
    var cursor = new Vec2(900, 900);
    var none = new List<DesktopWindow>();
    const double Dt = 0.25;

    DesktopIcon Icon(int i, double x, double y) => new(i, new RectD(x - 24, y - 24, 48, 48));
    DesktopWindow Win(long id, double x, double y, double w, double h) => new("w" + id, new RectD(x, y, w, h), id);
    DesktopSnapshot Snap(IReadOnlyList<DesktopWindow> w, params DesktopIcon[] icons) => new(cursor, w, icons);

    // ---- proximity rules (sudden-change reactions off, so only these are tested)
    var brain = new SpiderBrain { ReactToChanges = false };

    Vec2 t = brain.Update(Dt, spider, Snap(none, Icon(0, 600, 200)));
    Check("far icon -> keeps looking at the cursor", t == cursor && brain.Focus == FocusKind.Cursor);

    t = brain.Update(Dt, spider, Snap(none, Icon(0, 260, 200)));
    Check("near icon -> looks at the icon", t == new Vec2(260, 200) && brain.Attention?.Index == 0 && brain.Focus == FocusKind.Icon);

    brain.Update(Dt, spider, Snap(none, Icon(0, 300, 200)));
    Check("hysteresis: still interested at 100 px", brain.Attention?.Index == 0);

    t = brain.Update(Dt, spider, Snap(none, Icon(0, 330, 200)));
    Check("loses interest beyond the release radius", brain.Attention is null && t == cursor);

    brain.Update(Dt, spider, Snap(none, Icon(1, 280, 200), Icon(2, 230, 200)));
    Check("nearest icon wins", brain.Attention?.Index == 2);

    var cover = new List<DesktopWindow> { Win(1, 0, 0, 500, 500) };
    t = brain.Update(Dt, spider, Snap(cover, Icon(0, 260, 200)));
    Check("standing on a window -> ignores icons and that window", brain.Focus == FocusKind.Cursor && !brain.OnDesktop && t == cursor);

    brain.LookAtIcons = false;
    t = brain.Update(Dt, spider, Snap(none, Icon(0, 260, 200)));
    Check("icons switched off -> the cursor", brain.Attention is null && t == cursor);
    brain.LookAtIcons = true;

    t = brain.Update(Dt, spider, new DesktopSnapshot(cursor, none));
    Check("source without icons -> cursor", t == cursor);

    t = brain.Update(Dt, spider, Snap(new List<DesktopWindow> { Win(2, 250, 100, 300, 300) }));
    Check("window edge 50 px away -> looks at the nearest edge point",
        t == new Vec2(250, 200) && brain.Focus == FocusKind.Window && brain.AttentionWindow?.Id == 2);

    t = brain.Update(Dt, spider, Snap(new List<DesktopWindow> { Win(3, 400, 100, 300, 300) }));
    Check("window 200 px away -> cursor", t == cursor && brain.Focus == FocusKind.Cursor);

    t = brain.Update(Dt, spider, Snap(new List<DesktopWindow> { Win(4, 250, 100, 300, 300) }, Icon(0, 230, 200)));
    Check("icon (30 px) beats window edge (50 px)", brain.Focus == FocusKind.Icon && t == new Vec2(230, 200));

    brain.LookAtWindows = false;
    t = brain.Update(Dt, spider, Snap(new List<DesktopWindow> { Win(5, 250, 100, 300, 300) }));
    Check("windows switched off -> cursor", t == cursor);

    // ---- sudden changes
    var cb = new SpiderBrain();
    cb.Update(Dt, spider, new DesktopSnapshot(cursor, new List<DesktopWindow>()));
    Check("nothing is news on the first frame", cb.Focus == FocusKind.Cursor);

    var opened = new List<DesktopWindow> { Win(5, 600, 600, 200, 200) };
    t = cb.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    Check("a window opening far away grabs attention", cb.Focus == FocusKind.Change && t == new Vec2(700, 700));

    cb.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    cb.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    cb.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    Check("...and keeps it for one second (4 frames of 0.25 s)", cb.Focus == FocusKind.Change);

    cb.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    Check("...then lets go", cb.Focus == FocusKind.Cursor);

    var pb = new SpiderBrain();
    pb.Update(Dt, spider, Snap(none, Icon(0, 230, 200)));
    Check("(setup) near icon is watched", pb.Focus == FocusKind.Icon);
    t = pb.Update(Dt, spider, Snap(new List<DesktopWindow> { Win(9, 800, 800, 100, 100) }, Icon(0, 230, 200)));
    Check("a change takes priority over a near icon", pb.Focus == FocusKind.Change && t == new Vec2(850, 850));

    var ib = new SpiderBrain();
    ib.Update(Dt, spider, Snap(none, Icon(0, 600, 600)));
    t = ib.Update(Dt, spider, Snap(none, Icon(0, 700, 600)));
    Check("a moved icon grabs attention", ib.Focus == FocusKind.Change && t == new Vec2(700, 600));

    var xb = new SpiderBrain();
    xb.Update(Dt, spider, new DesktopSnapshot(cursor, new List<DesktopWindow> { Win(7, 600, 600, 200, 200) }));
    t = xb.Update(Dt, spider, new DesktopSnapshot(cursor, new List<DesktopWindow>()));
    Check("a closed window is looked at where it was", xb.Focus == FocusKind.Change && t == new Vec2(700, 700));

    var tb = new SpiderBrain();
    tb.Update(Dt, spider, Snap(none, Icon(0, 600, 600), Icon(1, 700, 600)));
    tb.Update(Dt, spider, Snap(none));
    tb.Update(Dt, spider, Snap(none, Icon(0, 600, 600), Icon(1, 700, 600)));
    Check("icons coming back after an empty scan are not news", tb.Focus == FocusKind.Cursor);

    var ob = new SpiderBrain { ReactToChanges = false };
    ob.Update(Dt, spider, new DesktopSnapshot(cursor, new List<DesktopWindow>()));
    t = ob.Update(Dt, spider, new DesktopSnapshot(cursor, opened));
    Check("reactions switched off -> cursor", t == cursor);
}

void RunDropFeatureChecks()
{
    Check("junk by name is junk", FilePolicy.Judge("Microsoft Edge.lnk").Verdict == FileVerdict.Junk
                                  || FilePolicy.Judge("setup.tmp").Verdict == FileVerdict.Junk);
    Check("a normal file is worthy", FilePolicy.Judge("holiday photos.jpg").Verdict == FileVerdict.Worthy);
    Check("the verdict and quip are deterministic",
        FilePolicy.Judge("report.docx") == FilePolicy.Judge("report.docx"));

    Check("opposite corners",
        SpiderCarry.Opposite(SpiderCorner.TopRight) == SpiderCorner.BottomLeft
        && SpiderCarry.Opposite(SpiderCorner.TopLeft) == SpiderCorner.BottomRight);

    var area = new RectD(0, 0, 1000, 800);
    Vec2 slot = SpiderCarry.FindCornerSlot(area, SpiderCorner.TopRight, new List<DesktopIcon>(), -1);
    Check("the hoard slot is inside the area, in the right corner", area.Contains(slot) && slot.X > 500 && slot.Y < 400);

    const double dt = 1.0 / 120.0;
    var spider = new Spider(new Vec2(300, 300));
    var carry = new SpiderCarry();
    Vec2? placed = null;
    carry.Placed += p => placed = p;
    carry.Start(new Vec2(330, 310), new Vec2(900, 300));
    for (int i = 0; i < 60; i++) { carry.Update(dt, spider); spider.Update(dt); }
    var target = new Vec2(300, 600);
    Check("double click redirects a running carry", carry.Redirect(target) && carry.Pinned);
    for (int i = 0; i < 120 * 40 && carry.Phase != CarryPhase.Rest; i++) { carry.Update(dt, spider); spider.Update(dt); }
    Check("the item ends up at the double-click spot", placed is Vec2 pv && (pv - target).Length < 60);
    Check("redirect after set-down is refused", !carry.Redirect(new Vec2(10, 10)));

    var s2 = new Spider(new Vec2(300, 300));
    var gift = new SpiderCarry { WaitForCursorSeconds = 1.0 };
    gift.Start(new Vec2(300, 300), new Vec2(300, 300), new CarryOptions(BringToCursor: true));
    for (int i = 0; i < 120 * 12 && gift.Phase != CarryPhase.Rest; i++)
    {
        gift.Update(dt, s2, new Vec2(300, 300)); // cursor never leaves
        s2.Update(dt);
    }
    Check("gift mode gives up waiting instead of hanging", gift.Phase == CarryPhase.Rest);

}

int CountTurnBursts(double targetDeg, out double finalErr)
{
    var sp = new Spider(new Vec2(500, 500), 0.0) { SteppedTurning = true };
    double rad = targetDeg * Math.PI / 180.0;
    sp.LookTarget = sp.Position + new Vec2(Math.Sin(rad), -Math.Cos(rad)) * 1000;
    const double dt = 1.0 / 120.0;
    int bursts = 0;
    bool moving = false;
    for (int i = 0; i < 120 * 4; i++)
    {
        sp.Update(dt);
        bool now = Math.Abs(sp.AngularVelocityDeg) > 1e-6;
        if (now && !moving) bursts++;
        moving = now;
    }
    finalErr = Math.Abs(MathUtilWrap(sp.HeadingDeg - targetDeg));
    return bursts;
}

double MathUtilWrap(double d)
{
    d %= 360.0;
    if (d > 180) d -= 360;
    if (d <= -180) d += 360;
    return d;
}

void RunStateChecks()
{
    int small = CountTurnBursts(20, out double e1);
    int big = CountTurnBursts(170, out double e2);
    Check("a small turn is one single move", small == 1 && e1 < 5);
    Check("a big turn is split into several steps", big >= 3 && big <= 5 && e2 < 5);

    // reaching: the front legs go for the target
    var sp = new Spider(new Vec2(400, 400), 0.0);
    for (int i = 0; i < 60; i++) sp.Update(1.0 / 120.0);
    var target = new Vec2(400, 360);
    Vec2 before = sp.Legs[0].Tip;
    sp.ReachTarget = target;
    for (int i = 0; i < 120; i++) sp.Update(1.0 / 120.0);
    double dBefore = (before - target).Length, dAfter = (sp.Legs[0].Tip - target).Length;
    Check("front legs reach towards the item", sp.ReachAmount > 0.99 && dAfter < dBefore);
    sp.ReachTarget = null;
    for (int i = 0; i < 120; i++) sp.Update(1.0 / 120.0);
    Check("legs relax afterwards", sp.ReachAmount < 0.01);

    // state machine priorities
    var spider = new Spider(new Vec2(300, 300));
    var brain = new SpiderBrain();
    var snap = new DesktopSnapshot(new Vec2(600, 300), new List<DesktopWindow>());
    var seen = new List<SpiderState>();
    brain.StateChanged += (_, n) => seen.Add(n);

    brain.Think(0.1, spider, snap, default);
    Check("default state is watching the cursor", brain.State == SpiderState.Watching);

    Vec2? look = brain.Think(0.1, spider, snap, new BrainContext(HoverPoint: new Vec2(310, 300)));
    Check("hovering item -> reaching, looks at it", brain.State == SpiderState.Reaching && look == new Vec2(310, 300) && brain.ReachPoint is not null);

    look = brain.Think(0.1, spider, snap, new BrainContext(Carrying: true, HoverPoint: new Vec2(310, 300)));
    Check("carrying overrides reaching and leaves looking to the carry task", brain.State == SpiderState.Carrying && look is null && brain.ReachPoint is null);

    look = brain.Think(0.1, spider, snap, new BrainContext(Held: true, Carrying: true));
    Check("held overrides everything", brain.State == SpiderState.Held && look is null);

    brain.NotifyDropped(new Vec2(340, 300));
    look = brain.Think(0.1, spider, snap, default);
    Check("tap-drop -> looks at what it dropped", brain.State == SpiderState.Dropping && look == new Vec2(340, 300));
    for (int i = 0; i < 10; i++) brain.Think(0.1, spider, snap, default);
    Check("dropping state ends by itself", brain.State == SpiderState.Watching);
    Check("state changes were reported", seen.Contains(SpiderState.Reaching) && seen.Contains(SpiderState.Dropping));

    // dropping on tap fires Placed and frees the spider
    var carry = new SpiderCarry();
    Vec2? placedAt = null;
    carry.Placed += pt => placedAt = pt;
    carry.Start(new Vec2(300, 300), new Vec2(800, 300));
    Check("tap drops the item where it is", carry.Drop() && !carry.Busy && placedAt is not null && !carry.Drop());
}

void RunPokeAndSkitterChecks()
{
    // skittering path
    var path = new SkitterPath(new Random(3));
    path.Plan(new Vec2(0, 0), new Vec2(600, 0));
    var pts = path.Points;
    double maxSide = pts.Max(p => Math.Abs(p.Y));
    Check("a long walk is divided into several waypoints", pts.Count >= 6);
    Check("the last waypoint is the goal", pts[^1] == new Vec2(600, 0));
    Check("waypoints swing to both sides of the straight line", pts.Any(p => p.Y > 5) && pts.Any(p => p.Y < -5));
    Check("the detours stay small", maxSide <= path.MaxSideOffset + 1e-6);
    var shortPath = new SkitterPath(new Random(3));
    shortPath.Plan(new Vec2(0, 0), new Vec2(90, 0));
    Check("a short walk stays direct", shortPath.Points.Count == 1);

    // a carry that skitters still arrives, and does not walk a ruler-straight line
    const double dt = 1.0 / 120.0;
    var spider = new Spider(new Vec2(300, 300));
    var carry = new SpiderCarry();
    Vec2? placed = null;
    carry.Placed += pt => placed = pt;
    carry.Start(new Vec2(330, 310), new Vec2(900, 300));
    double maxOff = 0;
    for (int i = 0; i < 120 * 40 && carry.Phase != CarryPhase.Rest; i++)
    {
        carry.Update(dt, spider);
        spider.Update(dt);
        if (carry.Phase == CarryPhase.Carry)
            maxOff = Math.Max(maxOff, Math.Abs(spider.Position.Y - 300));
    }
    Check("a skittering carry arrives at the goal", placed is Vec2 pv && (pv - new Vec2(900, 300)).Length < 40);
    Check("it wandered off the straight line while walking", maxOff > 6);

    var straight = new Spider(new Vec2(300, 300));
    var c2 = new SpiderCarry { Skitter = false, Jerky = false };
    c2.Start(new Vec2(330, 310), new Vec2(900, 300));
    double maxOff2 = 0;
    for (int i = 0; i < 120 * 40 && c2.Phase != CarryPhase.Rest; i++)
    {
        c2.Update(dt, straight);
        straight.Update(dt);
        if (c2.Phase == CarryPhase.Carry)
            maxOff2 = Math.Max(maxOff2, Math.Abs(straight.Position.Y - 300));
    }
    Check("with skittering off the walk is straight", maxOff2 < maxOff);

    // stop-and-go: it freezes between dashes, several times on a walk
    var jSpider = new Spider(new Vec2(300, 300));
    var jerky = new SpiderCarry(new Random(11));
    jerky.Start(new Vec2(330, 310), new Vec2(900, 300));
    int pauses = 0, stillFrames = 0, movingFrames = 0;
    bool wasPausing = false;
    for (int i = 0; i < 120 * 40 && jerky.Phase != CarryPhase.Rest; i++)
    {
        jerky.Update(dt, jSpider);
        jSpider.Update(dt);
        if (jerky.Phase != CarryPhase.Carry) continue;
        if (jerky.Pausing && !wasPausing) pauses++;
        wasPausing = jerky.Pausing;
        if (jerky.Pausing) stillFrames++; else movingFrames++;
    }
    Check("a jerky walk stops several times on the way", pauses >= 5);
    Check("it spends a fair share of the walk standing still, and the rest moving",
        stillFrames > movingFrames / 6 && movingFrames > stillFrames / 3);
    Check("a jerky walk still arrives", jerky.Phase == CarryPhase.Rest);

    // leaping
    var hopper = new Spider(new Vec2(500, 500));
    for (int i = 0; i < 60; i++) hopper.Update(dt);
    Vec2 start = hopper.Position;
    hopper.Leap(new Vec2(1, 0), 150);
    var brain = new SpiderBrain();
    var snap = new DesktopSnapshot(new Vec2(100, 500), new List<DesktopWindow>());
    brain.Think(dt, hopper, snap, default);
    Check("a leap puts the brain in the leaping state", hopper.IsLeaping && brain.State == SpiderState.Leaping);

    double peak = 0;
    for (int i = 0; i < 120 && hopper.IsLeaping; i++)
    {
        hopper.Update(dt);
        peak = Math.Max(peak, hopper.LeapArc);
    }
    for (int i = 0; i < 60; i++) hopper.Update(dt);
    double travelled = (hopper.Position - start).Length;
    Check("a leap covers about the asked distance", travelled > 130 && travelled < 175);
    Check("a leap has an arc that peaks mid-air", peak > 0.95);
    Check("it lands facing the way it went", Math.Abs(MathUtilWrap(hopper.HeadingDeg - 90.0)) < 25);
    brain.Think(dt, hopper, snap, default);
    Check("the brain goes back to watching after the leap", brain.State == SpiderState.Watching);

    var grabbed = new Spider(new Vec2(500, 500));
    grabbed.Leap(new Vec2(0, -1), 150);
    grabbed.CancelLeap();
    Check("grabbing the spider cancels the leap", !grabbed.IsLeaping && grabbed.LeapArc == 0.0);
}

void RunDeleteTypeDodgeChecks()
{
    const double dt = 1.0 / 60.0;
    var windows = new List<DesktopWindow>();

    // a deleted icon is noticed even though the indices of the others shift
    var spider = new Spider(new Vec2(320, 400));
    var brain = new SpiderBrain();
    var icons1 = new List<DesktopIcon>
    {
        new(0, new RectD(100, 100, 48, 48)), new(1, new RectD(300, 100, 48, 48)), new(2, new RectD(500, 100, 48, 48)),
    };
    brain.Think(dt, spider, new DesktopSnapshot(new Vec2(900, 700), windows, icons1), default);
    var icons2 = new List<DesktopIcon>
    {
        new(0, new RectD(100, 100, 48, 48)), new(1, new RectD(500, 100, 48, 48)),
    };
    brain.Think(dt, spider, new DesktopSnapshot(new Vec2(900, 700), windows, icons2), default);
    Check("deleting an icon startles the spider",
        brain.State == SpiderState.Startled && brain.FocusLabel == "change: icon removed");
    Check("it looks at the empty spot", (brain.FocusPoint - new Vec2(324, 124)).Length < 1.0);

    // the shift of the other indices alone is not a change
    var calm = new SpiderBrain();
    calm.Think(dt, spider, new DesktopSnapshot(new Vec2(900, 700), windows, icons1), default);
    var same = new List<DesktopIcon>(icons1);
    calm.Think(dt, spider, new DesktopSnapshot(new Vec2(900, 700), windows, same), default);
    Check("an unchanged desktop does not startle it", calm.State == SpiderState.Watching);

    // typing
    var s2 = new Spider(new Vec2(300, 300));
    var b2 = new SpiderBrain();
    Vec2? look = b2.Think(dt, s2, new DesktopSnapshot(new Vec2(900, 700), windows, null, new Vec2(360, 300), true), default);
    Check("typing close by makes it watch the text", b2.State == SpiderState.LookingAtText && look == new Vec2(360, 300));
    b2.Think(dt, s2, new DesktopSnapshot(new Vec2(900, 700), windows, null, new Vec2(360, 300), false), default);
    Check("no typing, no looking", b2.State == SpiderState.Watching);
    b2.Think(dt, s2, new DesktopSnapshot(new Vec2(900, 700), windows, null, new Vec2(900, 300), true), default);
    Check("typing far away is ignored", b2.State == SpiderState.Watching);
    b2.LookAtText = false;
    b2.Think(dt, s2, new DesktopSnapshot(new Vec2(900, 700), windows, null, new Vec2(360, 300), true), default);
    Check("the toggle turns it off", b2.State == SpiderState.Watching);

    // an approaching window: a short hop back, but not a full escape
    Vec2 Run(double perFrame, bool away, out bool dodged)
    {
        var sp = new Spider(new Vec2(600, 300));
        for (int i = 0; i < 30; i++) sp.Update(dt);
        var br = new SpiderBrain();
        bool hopped = false;
        br.Dodged += () => hopped = true;
        Vec2 start = sp.Position;
        double x = 100.0;
        for (int i = 0; i < 90; i++)
        {
            x += away ? -perFrame : perFrame;
            var list = new List<DesktopWindow> { new("w", new RectD(x, 100, 200, 400), 7) };
            br.Think(dt, sp, new DesktopSnapshot(new Vec2(50, 700), list), default);
            sp.Update(dt);
            if (hopped) break;
        }
        for (int i = 0; i < 60; i++) sp.Update(dt);
        dodged = hopped;
        return sp.Position - start;
    }

    Vec2 moved = Run(12.0, false, out bool fast);
    Check("a fast window coming at it makes it hop back", fast && moved.X > 15 && moved.X < 70);
    Run(1.5, false, out bool slow);
    Check("a slow window is no reason to hop", !slow);
    Run(12.0, true, out bool receding);
    Check("a window moving away is ignored", !receding);
}

void RunCarryChecks()
{
    var spider = new Spider(new Vec2(300, 300));
    var carry = new SpiderCarry();
    var dest = new Vec2(600, 300);
    carry.Start(new Vec2(330, 310), dest);
    Check("carry starts in the grab phase", carry.Phase == CarryPhase.Grab && carry.Busy && carry.HasItem);

    const double dt = 1.0 / 120.0;
    var seen = new HashSet<CarryPhase>();
    double minDistance = double.MaxValue;
    int frames = 0;
    for (; frames < 120 * 40 && carry.Phase != CarryPhase.Rest; frames++)
    {
        carry.Update(dt, spider);
        spider.Update(dt);
        seen.Add(carry.Phase);
        minDistance = Math.Min(minDistance, (spider.Position - dest).Length);
    }

    Check("walks to the destination and sets the item down",
        carry.Phase == CarryPhase.Rest && seen.Contains(CarryPhase.Carry) && seen.Contains(CarryPhase.Place) && minDistance < 40);
    Check("the spider is free again once the item lies down", !carry.Busy && carry.HasItem);

    for (int i = 0; i < 120 * 8 && carry.Phase != CarryPhase.Idle; i++)
        carry.Update(dt, spider);
    Check("the item fades away after resting", carry.Phase == CarryPhase.Idle && !carry.HasItem);

    var s2 = new Spider(new Vec2(300, 300));
    var c2 = new SpiderCarry();
    c2.Start(new Vec2(330, 310), new Vec2(900, 300));
    for (int i = 0; i < 120; i++) { c2.Update(dt, s2); s2.Update(dt); }
    c2.Cancel();
    Check("picking the spider up drops the item where it is", c2.Phase == CarryPhase.Rest && !c2.Busy);

    var area = new RectD(0, 0, 1000, 800);
    var snap = new DesktopSnapshot(new Vec2(500, 400), new List<DesktopWindow>(), new List<DesktopIcon>());
    bool inside = true, near = true, gift = true;
    var rng = new Random(7);
    for (int i = 0; i < 200; i++)
    {
        Vec2 d = SpiderCarry.PickDestination(spider, snap, area, rng, false);
        inside &= d.X >= 60 && d.X <= 940 && d.Y >= 60 && d.Y <= 740;
        near &= (d - spider.Position).Length <= 330 + 1e-6 || d.X <= 60 || d.X >= 940 || d.Y <= 60 || d.Y >= 740;
        Vec2 g = SpiderCarry.PickDestination(spider, snap, area, rng, true);
        gift &= (g - snap.Cursor).Length <= 102 + 1e-6;
    }
    Check("destinations stay inside the screen", inside);
    Check("nearby destinations are a short walk away", near);
    Check("gift destinations are beside the cursor", gift);

    var win = new DesktopWindow("w", new RectD(0, 0, 400, 800), 1);
    var snapWin = new DesktopSnapshot(new Vec2(900, 400), new List<DesktopWindow> { win });
    var onDesktop = new Spider(new Vec2(700, 400));
    bool avoids = true;
    for (int i = 0; i < 200; i++)
    {
        Vec2 d = SpiderCarry.PickDestination(onDesktop, snapWin, area, rng, false);
        avoids &= !win.Bounds.Contains(d);
    }
    Check("a spider on the bare desktop does not carry things into a window", avoids);
}

sealed class ScriptedSource : IDesktopSource
{
    public IReadOnlyList<DesktopWindow> Windows { get; set; } = new List<DesktopWindow>();
    public DesktopSnapshot GetSnapshot() => new(Vec2.Zero, Windows);
}
