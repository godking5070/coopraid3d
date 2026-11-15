using System;
using RaidRogue;

internal static class DashTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception(message);
    }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.00001, message);

    private static void MotionAndCooldown()
    {
        var settings = new DashSettings();
        Near(settings.distance, 4, "default distance");
        Near(settings.duration, 0.2, "default duration");
        Near(settings.cooldown, 5, "default cooldown");
        foreach (double dt in new[] { 1.0 / 240, 1.0 / 60, 1.0 / 30, 0.09, 0.2, 0.5 })
        {
            foreach (int yaw in new[] { 0, 45, 90, 180, 270 })
            {
                CameraControlMath.MovementXZ(1, 1, yaw, out double x, out double z);
                var dash = new DashMotion();
                Check(dash.TryStart(settings, true, x, z, 0, -1), "ready dash starts");
                Near(dash.CooldownRemaining, 5, "cooldown begins on activation");
                Check(!dash.TryStart(settings, true, -x, -z, 0, 1), "active dash cannot be redirected or retriggered");
                Near(dash.DirectionX, x, "camera input captured X");
                Near(dash.DirectionZ, z, "camera input captured Z");
                double total = 0, elapsed = 0;
                while (dash.Active)
                {
                    double step = dash.Step(dt);
                    Check(step >= 0 && step <= 20.00001 * dt, "fixed dash speed and final partial step");
                    total += step;
                    elapsed += dt;
                }
                Near(total, 4, "cardinal/diagonal movement remains 4m at every frame rate");
                Check(elapsed + 0.00001 >= 0.2 && elapsed < 0.20001 + dt, "duration bounded to one frame");
                Near(dash.Step(dt), 0, "completed dash does not continue");
                Check(!dash.TryStart(settings, true, x, z, 0, 1), "cooldown blocks restart");
                dash.TickCooldown(4.999);
                Check(!dash.TryStart(settings, true, x, z, 0, 1), "cooldown cannot finish early");
                dash.TickCooldown(0.002);
                Check(dash.TryStart(settings, true, x, z, 0, 1), "ready after full cooldown");
            }
        }

        var idle = new DashMotion();
        Check(idle.TryStart(settings, true, 0, 0, -3, 4), "idle dash uses facing");
        Near(idle.DirectionX, -0.6, "facing X normalized"); Near(idle.DirectionZ, 0.8, "facing Z normalized");
        double remaining = idle.CooldownRemaining;
        idle.TickCooldown(0); Near(idle.CooldownRemaining, remaining, "pause does not advance cooldown");
        idle.Cancel(); Check(!idle.Active, "forced movement/menu interrupt stops dash");
        Near(idle.Step(1), 0, "cancel leaves no residual dash motion");
        Near(idle.CooldownRemaining, remaining, "interrupt does not refund cooldown");
        var blocked = new DashMotion();
        Check(!blocked.TryStart(settings, false, 1, 0, 0, 1), "death/stun/root/menu/forced movement gate rejects start");
        Near(blocked.CooldownRemaining, 0, "rejected activation costs no cooldown");
        Check(!blocked.TryStart(new DashSettings { duration = 0 }, true, 1, 0, 0, 1), "invalid duration rejected");
        Check(!blocked.TryStart(new DashSettings { distance = float.NaN }, true, 1, 0, 0, 1), "invalid distance rejected");
        Check(!blocked.TryStart(new DashSettings { cooldown = -1 }, true, 1, 0, 0, 1), "invalid cooldown rejected");
        var custom = new DashSettings { distance = 6, duration = 0.3f, cooldown = 8 };
        Check(blocked.TryStart(custom, true, 1, 0, 0, 1), "shared settings support tuning");
        custom.distance = 100; custom.duration = 5; custom.cooldown = 0;
        Near(blocked.Step(1), 6, "settings changes do not steer/extend an in-progress dash");
        Near(blocked.CooldownRemaining, 8, "activation snapshots cooldown");
    }

    private static void InputGate()
    {
        var gate = new DashPressGate();
        Check(gate.Consume(true, true, true), "fresh press fires");
        for (int i = 0; i < 600; i++) Check(!gate.Consume(i % 5 == 0, true, true), "held key never repeats even after cooldown");
        Check(!gate.Consume(false, false, true), "release is not an activation");
        Check(gate.Consume(true, false, true), "tap entirely within a frame fires once");
        Check(!gate.Consume(true, true, false), "menu consumes blocked press");
        Check(!gate.Consume(false, true, true), "closing menu with held key does not queue dash");
        gate.Consume(false, false, true);
        Check(gate.Consume(true, true, true), "new press after menu works");
        gate.RequireRelease();
        Check(!gate.Consume(true, true, true), "focus regain requires release");
        gate.Consume(false, false, true);
        Check(gate.Consume(true, true, true), "fresh press after focus regain works");
    }

    private static void Arena()
    {
        Near(DashGeometry.ArenaTravel(0, 0, 1, 0, 4, 18), 4, "unobstructed full dash");
        Near(DashGeometry.ArenaTravel(17, 0, 1, 0, 4, 18), 1, "stop at arena edge");
        Near(DashGeometry.ArenaTravel(18, 0, 1, 0, 4, 18), 0, "no outward movement at boundary");
        Near(DashGeometry.ArenaTravel(18, 0, -1, 0, 4, 18), 4, "inward movement at boundary");
        for (int angle = 0; angle < 360; angle++)
        {
            double dx = Math.Sin(angle * Math.PI / 180), dz = Math.Cos(angle * Math.PI / 180);
            double length = DashGeometry.ArenaTravel(17, 0, dx, dz, 4, 18);
            Check(length >= 0 && length <= 4, "clipping cannot extend a dash");
            double x = 17 + dx * length, z = dz * length;
            Check(x * x + z * z <= 18 * 18 + 0.00001, "arbitrary direction remains inside arena");
            double edgeX = (float)(18 * dx), edgeZ = (float)(18 * dz);
            Near(DashGeometry.ArenaTravel(edgeX, edgeZ, -dx, -dz, 4, 18), 4,
                "float-rounded boundary position never traps inward movement");
        }
    }

    private static void Tethers()
    {
        Func<double, double, double, double, bool> hit = (x0, z0, x1, z1) =>
            DashGeometry.CrossesTether(x0, z0, x1, z1, 0, 0, 10, 0, 1.25);
        Check(hit(5, -2, 5, 2), "4m dash crosses tether despite both endpoints missing");
        Check(hit(5, 2, 5, -2), "reverse dash crosses tether");
        Check(hit(5, 1.25, 5, 1.25), "stationary point on inclusive boundary");
        Check(!hit(5, 1.26, 5, 1.26), "stationary point outside radius");
        Check(!hit(0.49, -2, 0.49, 2), "do not intercept behind boss-side t limit");
        Check(!hit(9.81, -2, 9.81, 2), "do not intercept beyond tank-side t limit");
        Check(hit(0.5, -2, 0.5, 2), "boss-side inclusive t boundary");
        Check(hit(-2, 0, 12, 0), "longitudinal path through entire strip");
        Check(!hit(5, -2, 5, -1.3), "wall-clipped actual path cannot intercept the line behind wall");
        Check(!hit(2, 2, 8, 2), "parallel path outside strip misses");
        Check(!DashGeometry.CrossesTether(-2, 0, 2, 0, 0, 0, 0, 0, 1.25), "degenerate tether does not intercept");
        // Independent original point-distance formula; ensures stationary behavior was preserved.
        var random = new Random(47);
        for (int i = 0; i < 2000; i++)
        {
            double x = random.NextDouble() * 14 - 2, z = random.NextDouble() * 6 - 3;
            double t = Math.Max(0, Math.Min(1, x / 10));
            double distance = Math.Sqrt((x - 10 * t) * (x - 10 * t) + z * z);
            Check(hit(x, z, x, z) == (t >= 0.05 && t <= 0.98 && distance <= 1.25), "stationary rule unchanged");
            double angle = random.NextDouble() * 2 * Math.PI;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            double aX = 7 + 5 * c + 2 * s, aZ = -3 + 5 * s - 2 * c;
            double bX = 7 + 5 * c - 2 * s, bZ = -3 + 5 * s + 2 * c;
            Check(DashGeometry.CrossesTether(aX, aZ, bX, bZ, 7, -3, 7 + 10 * c, -3 + 10 * s, 1.25),
                "swept intersection follows rotated/translated boss-tank line");
        }
    }

    public static void Main()
    {
        MotionAndCooldown(); InputGate(); Arena(); Tethers(); JobProfiles();
        Console.WriteLine($"PASS: {_checks} dash state, direction, input and swept geometry checks.");
    }
    private static void JobProfiles()
    {
        for(int i=0;i<12;i++)
        {
            var settings=CharacterDashProfiles.ForJob(i);
            Check(settings!=null&&settings.maximumCharges>=1&&settings.maximumCharges<=3,"valid job dash profile");
            var motion=new DashMotion();
            Check(motion.TryStart(settings,true,1,0,0,1),"job dash starts with input against facing");
            Near(motion.DirectionX,i==0?0:1,"backstep overrides input only for shield tank");
            Near(motion.DirectionZ,i==0?-1:0,"tank moves opposite facing");
            Near(motion.Step(10),settings.distance,"job distance is bounded and independent of frame duration");
            Check(motion.Charges==settings.maximumCharges-1,"one use consumes exactly one charge");
            for(int n=1;n<settings.maximumCharges;n++)
            {Check(motion.TryStart(settings,true,1,0,0,1),"remaining short step can start");motion.Step(10);}
            Check(!motion.TryStart(settings,true,1,0,0,1),"empty job charges block dash");
            motion.TickCooldown(settings.cooldown);
            Check(motion.Charges==1,"multi-charge profile recovers sequentially");
            var restored=new DashMotion();restored.Restore(motion.Capture());
            Check(restored.Charges==motion.Charges&&restored.MaximumCharges==motion.MaximumCharges,"job charge snapshot roundtrip");
            Check(settings.preserveCast==(i==10),"only existing caster retains cast movement");
        }
        var back=new DashMotion();var config=CharacterDashProfiles.ForJob(0);
        Check(back.TryStart(config,true,-1,0,3,4),"rotated backstep starts");
        Near(back.DirectionX,-.6,"backstep captures facing X");Near(back.DirectionZ,-.8,"backstep captures facing Z");
        Check(!back.TryStart(config,true,1,0,-3,-4),"active backstep cannot turn or retrigger");
        Check(CharacterDashProfiles.ForJob(10).distance>CharacterDashProfiles.ForJob(6).distance,"long single warp differs from short triple step");
    }
}
