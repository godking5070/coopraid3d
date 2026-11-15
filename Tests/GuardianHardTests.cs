using System;
using RaidRogue;

internal static class GuardianHardTests
{
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception(reason); }
    private static void Near(double actual, double expected, string reason)
        => Check(Math.Abs(actual - expected) < .0001, reason + $": {actual} != {expected}");

    private static void Main()
    {
        var normal = new GuardianNormalSettings();
        var hard = new GuardianHardSettings();
        hard.EnsureDefaults();
        Near(normal.health, 30000, "normal HP unchanged");
        Near(normal.Duration(GuardianPattern.Sweep), 4 / 1.2, "normal tempo unchanged");
        Near(normal.counterWindowSeconds, 2, "normal counter window");
        Near(normal.overloadSeconds, 10, "normal stagger window");
        Near(hard.combat.health, 30000, "temporary hard HP");
        Near(hard.combat.Duration(GuardianPattern.Sweep), 4, "hard has explicit tempo 1");
        Near(hard.combat.Duration(GuardianPattern.GateCharge), 4, "hard charge prepare");
        Near(hard.combat.counterWindowSeconds, 1.6, "hard counter window");
        Near(hard.combat.Duration(GuardianPattern.Overload), 9, "hard stagger window");
        Near(hard.lockdownLockAt, 3, "aim lock at 3");
        Near(hard.lockdownLineAt, 5, "line hits at 5");
        Near(hard.lockdownEraseAt, 11, "inner erase at 11");
        Near(hard.punishmentSecondDelay, 2, "buster followup");
        Near(hard.sweepSecondDelay, 3, "sweep followup");
        Near(hard.slamRingDelay, 3, "ring followup");

        var state = new GuardianHardState();
        state.Reset();
        Check(state.Next(.71f) == GuardianPattern.None, "threshold not early");
        Check(state.Next(.24f) == GuardianPattern.Lockdown, "70 first after burst");
        Check(state.Next(.24f) == GuardianPattern.Overload, "50 second after burst");
        Check(state.Next(.24f) == GuardianPattern.Transformation, "40 third after burst");
        Check(state.Next(.24f) == GuardianPattern.None, "25 waits for transformation to finish");
        state.FinishTransformation();
        Check(state.Next(.24f) == GuardianPattern.Sweep, "new-form sweep introduction");
        Check(state.Next(.24f) == GuardianPattern.GateCharge, "new-form counter introduction");
        Check(state.Next(.24f) == GuardianPattern.FullLockdown, "25 follows completed 40 sequence");
        Check(state.Next(.24f) == GuardianPattern.TwistedExecution, "twisted sequence joins rotation");
        Check(state.Next(.24f) == GuardianPattern.None, "base attacks still rotate");
        Check(state.Next(0) == GuardianPattern.None, "death cancels pending");
        state.Reset();
        Check(!state.Transformed && state.Next(.69f) == GuardianPattern.Lockdown, "retry resets thresholds");

        Check(GuardianHardGeometry.LineHitsCrack(0, 0, 0, 1, 0, 1, 12, 24), "north line opens north crack");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, 1, 0, 0, 1, 12, 24), "east line misses north crack");
        Check(GuardianHardGeometry.LineHitsCrack(3, 2, -3 / Math.Sqrt(109), 10 / Math.Sqrt(109), 0, 1, 12, 24),
            "offset boss may hit cardinal crack");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, 0, -1, 0, 1, 12, 24), "backward ray cannot open crack");
        Check(GuardianHardGeometry.LineHitsCrack(0, 0, Math.Sin(.17), Math.Cos(.17), 0, 1, 12, 24, 2),
            "wide visible line touching the crack opens it");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, Math.Sin(.4), Math.Cos(.4), 0, 1, 12, 24, 2),
            "line beyond displayed crack remains closed");

        var encounter = new GuardianNormalState();
        encounter.Reset(0, hard.combat);
        encounter.Begin(GuardianPattern.FullLockdown, 5, hard.combat);
        encounter.Enter(GuardianStep.CounterWindow, 20, hard.combat.counterWindowSeconds);
        Check(encounter.Counter(21) && encounter.Ends == 27, "full-lockdown counter consumed once");
        Check(!encounter.Counter(21), "second counter ignored");
        Console.WriteLine($"PASS: {checks} guardian hard state and geometry assertions.");
    }
}
