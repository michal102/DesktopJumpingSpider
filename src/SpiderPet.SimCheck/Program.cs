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

sealed class ScriptedSource : IDesktopSource
{
    public IReadOnlyList<DesktopWindow> Windows { get; set; } = new List<DesktopWindow>();
    public DesktopSnapshot GetSnapshot() => new(Vec2.Zero, Windows);
}
