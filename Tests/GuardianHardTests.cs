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
        Near(hard.combat.health, 40000, "hard HP is 40000");
        hard.combat.health = 30000;
        hard.EnsureDefaults();
        Near(hard.combat.health, 40000, "serialized old hard HP migrates");
        hard.combat.health = 45000;
        hard.EnsureDefaults();
        Near(hard.combat.health, 45000, "custom hard HP preserved");
        hard.combat.health = 0;
        hard.EnsureDefaults();
        Near(hard.combat.health, 40000, "invalid hard HP defaults to 40000");
        Near(hard.combat.Duration(GuardianPattern.Sweep), 1.9, "hard quick sweep preparation");
        Near(hard.combat.Duration(GuardianPattern.GateCharge), 2, "hard charge prepare");
        Near(hard.combat.counterWindowSeconds, 1.6, "hard counter window");
        Near(hard.combat.Duration(GuardianPattern.Overload), 9, "hard stagger window");
        Near(hard.lockdownLockAt, 1.8, "quick aim lock");
        Near(hard.aimTurnDegreesPerSecond,90,"hard aim turn speed cap");
        Near(GuardianHardGeometry.TrackYaw(0,180,90,1.0/60),358.5,"teleport behind cannot snap facing");
        Near(GuardianHardGeometry.TrackYaw(350,10,90,.1),359,"wraparound follows shortest arc");
        Near(GuardianHardGeometry.TrackYaw(10,350,90,.1),1,"reverse wraparound follows shortest arc");
        Near(GuardianHardGeometry.TrackYaw(0,5,90,.1),5,"near target never overshoots");
        Near(GuardianHardGeometry.TrackYaw(30,180,90,0),30,"zero delta cannot turn");
        double yaw30=0,yaw60=0;
        for(int i=0;i<30;i++)yaw30=GuardianHardGeometry.TrackYaw(yaw30,170,90,1.0/30);
        for(int i=0;i<60;i++)yaw60=GuardianHardGeometry.TrackYaw(yaw60,170,90,1.0/60);
        Near(yaw30,90,"one-second rotation budget at 30fps");
        Near(yaw60,yaw30,"turn rate independent of frame rate");
        Near(hard.lockdownLineAt, 2.7, "quick wall breach");
        Near(hard.lockdownEraseAt, 6, "erase gives 3.3 seconds to escape");
        Near(hard.transformationSeconds,4,"jump transformation uses four seconds");
        hard.transformationSeconds=8;hard.EnsureDefaults();
        Near(hard.transformationSeconds,4,"old eight-second transformation migrates");
        Near(GuardianClashRules.JumpHeight(0),0,"jump begins grounded");
        Near(GuardianClashRules.JumpHeight(.25),6,"jump rises quickly");
        Near(GuardianClashRules.JumpHeight(.65),6,"jump holds in air before slam");
        Check(GuardianClashRules.JumpHeight(.9)<GuardianClashRules.JumpHeight(.75),"slam descends");
        Near(GuardianClashRules.JumpHeight(1),0,"slam lands at end");
        Check(GuardianHardGeometry.LockdownEraseHits(0, 0, 12, .55), "erase hits center");
        Check(GuardianHardGeometry.LockdownEraseHits(11.9, 0, 12, .55), "wall interior edge is not safe");
        Check(GuardianHardGeometry.LockdownEraseHits(12, 0, 12, .55), "wall boundary is not safe");
        Check(GuardianHardGeometry.LockdownEraseHits(12.4, 0, 12, .55), "partial escape still overlaps erase");
        Check(GuardianHardGeometry.LockdownEraseHits(0, -12.4, 12, .55), "erase is symmetric on Z");
        Check(!GuardianHardGeometry.LockdownEraseHits(12.56, 0, 12, .55), "fully outside survives");
        Check(!GuardianHardGeometry.LockdownEraseHits(0, -15.2, 12, .55), "AI exit destination survives");
        Check(GuardianHardGeometry.LockdownEraseHits(7.5, 10, 12, .55), "diagonal partial escape hits");
        Check(!GuardianHardGeometry.LockdownEraseHits(9, 12, 12, .55), "diagonal full escape survives");
        Check(GuardianHardGeometry.LockdownEraseHits(10.2, 0, 10, .4), "custom wall and member radii apply");
        Near(hard.punishmentSecondDelay, .85, "buster followup");
        Near(hard.sweepSecondDelay, .55, "sweep followup");
        Near(hard.slamRingDelay, .65, "ring followup");
        Near(hard.impactWarningSeconds, .4, "late ground warning");
        Near(hard.impactAfterimageSeconds, .08, "brief impact afterimage");
        Near(hard.slamRingInner, hard.combat.slamRadius, "center and ring share 8m boundary");
        hard.sweepSecondDelay = hard.slamRingDelay = 3;
        hard.slamRingInner = 7.8f;
        hard.EnsureDefaults();
        Near(hard.sweepSecondDelay, .55, "serialized three-second sweep migration");
        Near(hard.slamRingDelay, .65, "serialized three-second ring migration");
        Near(hard.slamRingInner, 8, "serialized ring boundary migration");
        Near(hard.twistedGap, .55, "twisted sequence immediate followups");
        Check(hard.aerialChargePasses==3,"aerial charge repeats three times");
        Near(hard.aerialLeapSeconds,.35,"quick escape before outside charge");
        Near(hard.aerialAimSeconds,1,"each charge warns for one second");
        Near(hard.aerialChargeSpeed,65,"fast map-crossing charge");
        Check(GuardianHardGeometry.AerialChargeStartRadius(18,2)-2>18,"boss starts fully outside map");
        double chargeRadius=GuardianHardGeometry.AerialChargeStartRadius(18,2);
        Check(GuardianHardGeometry.SweptChargeHits(0,18-chargeRadius,0,-1,chargeRadius*2,2.4,.55),"full charge covers starting map edge");
        Check(GuardianHardGeometry.SweptChargeHits(0,-18-chargeRadius,0,-1,chargeRadius*2,2.4,.55),"full charge covers opposite map edge");
        Check(-chargeRadius+2 < -18,"whole boss exits opposite edge on every pass");
        hard.aerialLeapSeconds=.65f;hard.aerialAimSeconds=1.5f;hard.aerialChargeSpeed=26;hard.EnsureDefaults();
        Near(hard.aerialLeapSeconds,.35,"old leap duration migrates");
        Near(hard.aerialAimSeconds,1,"old warning duration migrates");
        Near(hard.aerialChargeSpeed,65,"old charge speed migrates");
        Check(GuardianHardGeometry.LurePhase(GuardianPattern.PlazaSlam,false,0),"center slam includes lure");
        Check(!GuardianHardGeometry.LurePhase(GuardianPattern.PlazaSlam,true,0),"ring followup never lures");
        Check(GuardianHardGeometry.LurePhase(GuardianPattern.TwistedExecution,false,2),"twisted center slam includes lure");
        Check(!GuardianHardGeometry.LurePhase(GuardianPattern.TwistedExecution,false,3),"twisted ring never lures");
        Check(!GuardianHardGeometry.RescueWindow(2,1.5),"early rescue does not break lure");
        Check(GuardianHardGeometry.RescueWindow(1.5,1.5),"rescue window opens before impact");
        Check(GuardianHardGeometry.RescueWindow(.5,1.5),"last safe rescue timing");
        Check(!GuardianHardGeometry.RescueWindow(.4,1.5),"too late to complete the pull");
        Check(!GuardianHardGeometry.RescueWindow(0,1.5),"resolved slam cannot be rescued");
        Check(!GuardianHardGeometry.RescueOutside(8,8,.55),"rescue into damage circle does not break lure");
        Check(GuardianHardGeometry.RescueOutside(9,8,.55),"rescue outside circle breaks lure");
        Near(GuardianHardGeometry.AerialChargeAngle(30,0),30,"first perimeter approach");
        Near(GuardianHardGeometry.AerialChargeAngle(30,1),150,"second approach rotates 120 degrees");
        Near(GuardianHardGeometry.AerialChargeAngle(30,2),270,"third approach rotates 120 degrees");
        Check(GuardianHardGeometry.SweptChargeHits(0,7,0,1,14,2.4,.55),"fast charge crosses a player between frames");
        Check(GuardianHardGeometry.SweptChargeHits(2.9,7,0,1,14,2.4,.55),"charge touches player radius at edge");
        Check(!GuardianHardGeometry.SweptChargeHits(3,7,0,1,14,2.4,.55),"outside charge width is safe");
        Check(!GuardianHardGeometry.SweptChargeHits(0,18,0,1,14,2.4,.55),"beyond swept endpoint is safe");
        double diag=1/Math.Sqrt(2);
        Check(GuardianHardGeometry.SweptChargeHits(7,7,diag,diag,14,2.4,.55),"diagonal swept charge");
        Check(hard.stoneWaves == 8, "eight overlapping falling-stone waves");
        Near(hard.stoneCastSeconds, 1.2, "falling-stone boss cast");
        Near(hard.stoneTrackSeconds, 1.1, "falling-stone tracking");
        Near(hard.stoneLockedSeconds, .95, "falling-stone fixed warning");
        Near(hard.resonanceKnockback, 5, "stack begins with knockback");
        Near(hard.clashPrepareSeconds, 2, "tank regroup lasts two seconds");
        hard.clashPrepareSeconds=8;hard.EnsureDefaults();
        Near(hard.clashPrepareSeconds,2,"eight-second regroup migrates");
        hard.clashPrepareSeconds=11;hard.EnsureDefaults();
        Near(hard.clashPrepareSeconds,2,"eleven-second regroup migrates");
        Check(GuardianClashRules.TransformationActive(true,GuardianPattern.Transformation,0),"cutscene blocks damage and AI attacks");
        for(int phase=1;phase<=5;phase++)
            Check(!GuardianClashRules.TransformationActive(true,GuardianPattern.Transformation,phase),"clash phase accepts damage "+phase);
        Check(!GuardianClashRules.TransformationActive(false,GuardianPattern.Transformation,0),"normal is not hard transformation");
        Check(!GuardianClashRules.TransformationActive(true,GuardianPattern.Sweep,0),"other patterns are not transformation");
        Near(hard.clashCounterSeconds, 1.8, "tank counter window");
        Near(hard.clashHoldSeconds, 6, "hold duration");
        Near(hard.clashPulseInterval, 2, "healer pulse spacing");
        Near(hard.clashOpeningSeconds, 2, "short counter hold opening");
        hard.clashOpeningSeconds=5;hard.EnsureDefaults();
        Near(hard.clashOpeningSeconds,2,"old five-second counter opening migrates");
        var clashTiming=new GuardianNormalState();
        clashTiming.Begin(GuardianPattern.Transformation,0,hard.combat);
        clashTiming.Enter(GuardianStep.Locked,0,hard.clashHoldSeconds);
        Check(!GuardianClashRules.OpeningActive(3,clashTiming.Step),"holding is not groggy");
        clashTiming.Enter(GuardianStep.Groggy,6,hard.clashOpeningSeconds);
        Check(GuardianClashRules.OpeningActive(4,clashTiming.Step)&&clashTiming.Ends==8,"one two-second opening after hold");
        clashTiming.Enter(GuardianStep.Recovery,8,hard.clashRecoverySeconds);
        Check(!GuardianClashRules.OpeningActive(5,clashTiming.Step),"post-opening recovery cannot extend groggy");
        Check(!GuardianClashRules.OpeningActive(5,GuardianStep.Groggy),"recovery phase rejects stale groggy state");
        Near(hard.clashRecoverySeconds, .9, "post-shock recovery");
        Near(hard.clashCircleRadius, 4.2, "circle includes boss contact range");
        Check(GuardianClashRules.CounterEligible(true, true, true, hard.clashCircleRadius, hard.clashCircleRadius), "living tank at circle edge can counter");
        Check(!GuardianClashRules.CounterEligible(true, true, false, 1, hard.clashCircleRadius), "non-tank cannot start clash");
        Check(!GuardianClashRules.CounterEligible(true, false, true, 1, hard.clashCircleRadius), "dead tank cannot start clash");
        Check(!GuardianClashRules.CounterEligible(true, true, true, 4.21, hard.clashCircleRadius), "tank outside circle cannot start clash");
        Check(!GuardianClashRules.CounterEligible(false, true, true, 1, hard.clashCircleRadius), "standing in circle before window cannot succeed");

        var state = new GuardianHardState();
        state.Reset();
        Check(state.Next(.71f) == GuardianPattern.None, "threshold not early");
        Check(state.HasPendingMilestone(.69f), "stone rain defers queued milestone");
        Check(state.Next(.24f) == GuardianPattern.Lockdown, "70 first after burst");
        Check(state.Next(.24f) == GuardianPattern.Transformation, "40 follows lockdown without a stagger check");
        Check(state.Next(.24f) == GuardianPattern.None, "25 waits for transformation to finish");
        state.FinishTransformation();
        Check(state.Next(.24f) == GuardianPattern.Sweep, "new-form sweep introduction");
        Check(state.Next(.24f) == GuardianPattern.PlazaSlam, "new-form center and ring introduction");
        Check(state.Next(.24f) == GuardianPattern.AerialCharge, "aerial charge introduced after transformation");
        Check(state.Next(.24f) == GuardianPattern.FullLockdown, "25 follows completed 40 sequence");
        Check(state.Next(.24f) == GuardianPattern.TwistedExecution, "twisted sequence joins rotation");
        Check(state.Next(.24f) == GuardianPattern.AerialCharge, "aerial charge remains in transformed rotation");
        Check(state.Next(.24f) == GuardianPattern.None, "base attacks still rotate");
        Check(state.Next(0) == GuardianPattern.None, "death cancels pending");
        state.Reset();
        Check(!state.Transformed && state.Next(.69f) == GuardianPattern.Lockdown, "retry resets thresholds");

        state.Reset();
        Check(!state.TryStartTransformation(.4f), "earlier milestones remain ordered");
        Check(state.Next(.69f) == GuardianPattern.Lockdown, "lockdown starts first");
        Check(!state.HasPendingMilestone(.49f), "50 percent no longer queues a stagger milestone");
        Check(state.Next(.49f) == GuardianPattern.None, "50 percent continues the ordinary rotation");
        Check(!state.TryStartTransformation(.401f), "phase change never starts above 40 percent");
        Check(state.TryStartTransformation(.4f), "40 percent starts without waiting for rest");
        Check(!state.TryStartTransformation(.3f), "phase change starts only once");
        state.FinishTransformation();
        Check(state.Next(.3f) == GuardianPattern.Sweep, "transformed attacks follow immediate phase change");
        state.Reset();
        state.Next(.49f);
        Check(!state.TryStartTransformation(0), "dead boss cannot transform");
        Check(state.TryStartTransformation(.35f), "threshold crossing below 40 percent also transforms");

        Check(GuardianHardGeometry.LineHitsCrack(0, 0, 0, 1, 0, 1, 12, 24), "north line opens north crack");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, 1, 0, 0, 1, 12, 24), "east line misses north crack");
        Check(GuardianHardGeometry.LineHitsCrack(3, 2, -3 / Math.Sqrt(109), 10 / Math.Sqrt(109), 0, 1, 12, 24),
            "offset boss may hit cardinal crack");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, 0, -1, 0, 1, 12, 24), "backward ray cannot open crack");
        Check(GuardianHardGeometry.LineHitsCrack(0, 0, Math.Sin(.17), Math.Cos(.17), 0, 1, 12, 24, 2),
            "wide visible line touching the crack opens it");
        Check(!GuardianHardGeometry.LineHitsCrack(0, 0, Math.Sin(.4), Math.Cos(.4), 0, 1, 12, 24, 2),
            "line beyond displayed crack remains closed");
        for (int segments = 12; segments <= 30; segments++)
            for (int rotation = 0; rotation < segments; rotation++)
            {
                int[] cracks = GuardianHardGeometry.ThreeCracks(segments, rotation, 1, segments / 3 - 2, 2);
                Check(cracks.Length == 3 && cracks[0] != cracks[1] && cracks[1] != cracks[2] && cracks[0] != cracks[2],
                    "three distinct randomizable cracks");
                foreach (int crack in cracks)
                {
                    double angle = crack * Math.PI * 2 / segments;
                    Check(GuardianHardGeometry.LineHitsCrack(0, 0, Math.Sin(angle), Math.Cos(angle),
                        Math.Sin(angle), Math.Cos(angle), 12, segments, 2), "each visible crack can open");
                }
            }

        var encounter = new GuardianNormalState();
        encounter.Reset(0, hard.combat);
        foreach(var pattern in GuardianNormalState.Introduction)
            Check(encounter.Choose(0)==pattern,"hard restores original basic pattern order");
        encounter.Reset(0, normal);
        foreach(var pattern in GuardianNormalState.Introduction)
            Check(encounter.Choose(0)==pattern,"normal introduction order unchanged");
        encounter.Reset(0, hard.combat);
        Check(encounter.Choose(0)==GuardianPattern.Punishment,"retry restores original first pattern");
        encounter.Reset(0, hard.combat);
        encounter.Begin(GuardianPattern.FullLockdown, 5, hard.combat);
        encounter.Enter(GuardianStep.CounterWindow, 20, hard.combat.counterWindowSeconds);
        Check(encounter.Counter(21) && encounter.Step==GuardianStep.Groggy && encounter.Ends == 23, "full-lockdown counter grants two-second groggy");
        Check(!encounter.Counter(21), "second counter ignored");
        Console.WriteLine($"PASS: {checks} guardian hard state and geometry assertions.");
    }
}
