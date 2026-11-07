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
