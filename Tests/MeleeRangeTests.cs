using System;
using RaidRogue;

internal static class MeleeRangeTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.00001, message);

    private static void DistancesAndProfiles()
    {
        Near(CombatRangeMath.CenterDistance(1, 2, 4, 6), 5, "XZ distance");
        Near(CombatRangeMath.BoundaryDistance(7, 0.5, 4), 2.5, "edge distance to a large boss");
        Check(CombatRangeMath.InRange(7, 0.5, 4, 2.5), "large boss edge is hittable");
        Check(!CombatRangeMath.InRange(7.01, 0.5, 4, 2.5), "just outside is rejected");
        Check(!CombatRangeMath.InRange(7, 0.5, 1, 2.5), "small target at same center distance is out");
        Check(CombatRangeMath.InRange(0.5, 1, 2, 0), "overlapping boundaries are in reach");
        Check(!CombatRangeMath.InRange(double.NaN, 1, 2, 3), "invalid distance is not a hit");
        Check(!CombatRangeMath.InRange(100, 1, 2, 3), "distant melee cannot hit");
        var settings = new MeleeRangeSettings();
        Near(settings.Get(MeleeSkill.TankPrimary), 2, "tank default");
        Near(settings.Get(MeleeSkill.BladeFlank), 2.5, "blade default");
        Near(settings.Get(MeleeSkill.BladeGale), 3, "per-skill default override");
        settings.overrides = new[] {
            new MeleeSkillRange { skill = MeleeSkill.BladeRear, comboStage = 0, range = 2.75f },
            new MeleeSkillRange { skill = MeleeSkill.BladeRear, comboStage = 3, range = 4 },
            new MeleeSkillRange { skill = MeleeSkill.TankPrimary, comboStage = 2, range = 1.5f }
        };
        Near(settings.Get(MeleeSkill.BladeRear, 1), 2.75, "whole-combo override");
        Near(settings.Get(MeleeSkill.BladeRear, 3), 4, "specific combo stage override wins");
        Near(settings.Get(MeleeSkill.TankPrimary, 2), 1.5, "tank skill configuration");
        Near(settings.Get(MeleeSkill.BladeFlank, 3), 2.5, "other skills retain their defaults");
    }

    private static void DisplayAndHitAgreement()
    {
        // The player's reach circle intersects a target combat circle exactly when a hit is legal.
        for (int i = 0; i < 80; i++)
        {
            double attackerRadius = 0.25 + i * 0.01;
            double targetRadius = 0.5 + i * 0.08;
            double range = 0.5 + i * 0.03;
            double boundary = CombatRangeMath.RingRadius(attackerRadius, range) + targetRadius;
            Check(CombatRangeMath.InRange(boundary, attackerRadius, targetRadius, range), "ring tangent means in range");
            Check(!CombatRangeMath.InRange(boundary + 0.01, attackerRadius, targetRadius, range), "disjoint rings mean out of range");
            double botDistance = CombatRangeMath.AttackCenterDistance(attackerRadius, targetRadius, range);
            Check(botDistance > attackerRadius + targetRadius, "bot positions outside target combat body");
            Check(CombatRangeMath.InRange(botDistance, attackerRadius, targetRadius, range), "bot goal is inside its real attack range");
        }
    }

    private static void Pursuit()
    {
        const double radii = 2.2, range = 1.8, hysteresis = 0.3, frameStep = 2.8 / 60.0;
        double distance = 15;
        bool chasing = false;
        for (int i = 0; i < 400; i++)
        {
            double step = CombatRangeMath.ChaseStep(distance, radii, range, hysteresis, frameStep, ref chasing);
            Check(step >= 0 && step <= frameStep, "bounded forward movement");
            distance -= step;
            Check(distance >= radii, "boss does not overlap target during approach");
        }
        Near(distance, radii + range - hysteresis, "boss stops at inner threshold");
        Check(CombatRangeMath.InRange(distance, 1.65, 0.55, range), "stopping position can auto-attack");
        for (int i = 0; i < 100; i++)
        {
            double jitteredDistance = distance + 0.1 + 0.05 * Math.Sin(i);
            Near(CombatRangeMath.ChaseStep(jitteredDistance, radii, range, hysteresis, frameStep, ref chasing), 0, "small target jitter does not restart pursuit");
        }
        double movedTargetDistance = radii + range + 1;
        Check(CombatRangeMath.ChaseStep(movedTargetDistance, radii, range, hysteresis, frameStep, ref chasing) > 0, "tank moves away: pursuit restarts");
        Check(!CombatRangeMath.InRange(movedTargetDistance, 1.65, 0.55, range), "boss cannot auto-attack far tank");
        chasing = false;
        double longFrame = CombatRangeMath.ChaseStep(20, radii, range, hysteresis, 100, ref chasing);
        Near(20 - longFrame, radii + range - hysteresis, "large frame never overshoots through target");
        double separation = CombatRangeMath.ChaseStep(1, radii, range, hysteresis, 100, ref chasing);
        Near(1 - separation, radii, "overlap is resolved away from target");
        Near(CombatRangeMath.ChaseStep(20, radii, range, hysteresis, 0, ref chasing), 0, "zero movement budget is stationary");
    }

    private static void BossRingContact()
    {
        const double contact = CombatRangeMath.MeleeContactTolerance;
        // Exercise old serialized values as well: both visuals and combat must retain the wide rings.
        foreach (double bossRadius in new[] {
            CombatRangeMath.ResolveBossCombatRadius(1, 1.65f),
            CombatRangeMath.ResolveBossCombatRadius(2, 2f),
            CombatRangeMath.ResolveBossCombatRadius(3, 2.5f) })
        foreach (double tankRadius in new[] { 0.35, 0.55, 0.9 })
        {
            double radii = bossRadius + tankRadius;
            double distance = radii + 8;
            bool chasing = false;
            for (int frame = 0; frame < 300; frame++)
            {
                distance -= CombatRangeMath.ChaseStep(distance, radii, contact, contact, 2.8 / 60, ref chasing);
                Check(distance >= radii - 0.00001, "boss ring never moves through tank's combat body");
            }
            Near(distance - tankRadius, bossRadius, "tank's near edge stops exactly on the visible boss circle");
            Check(CombatRangeMath.InRange(distance, bossRadius, tankRadius, contact), "boss can attack at ring contact");
            Check(!CombatRangeMath.InRange(radii + contact + 0.001, bossRadius, tankRadius, contact), "outside ring contact cannot receive boss auto-attack");
            Near(CombatRangeMath.ChaseStep(distance + contact * 0.5, radii, contact, contact, 0.1, ref chasing), 0, "contact jitter does not restart chase");
            Check(CombatRangeMath.ChaseStep(distance + 0.2, radii, contact, contact, 0.1, ref chasing) > 0, "tank leaving ring restarts chase");
            Check(!CombatRangeMath.InRange(radii + 1.5, bossRadius, tankRadius, contact), "old extra melee reach no longer hits");
            double edgeAttackDistance = bossRadius + tankRadius + 2;
            Check(CombatRangeMath.InRange(edgeAttackDistance, tankRadius, bossRadius, 2), "tank attacks measured from boss ring edge");
        }
    }

    private static void WideBossRingProfiles()
    {
        float[] legacy = { 1.65f, 2f, 2.5f };
        double[] originalRings = { 3.2, 3.648, 4.32 };
        for (int stage = 1; stage <= 3; stage++)
        {
            Near(CombatRangeMath.ResolveBossCombatRadius(stage, legacy[stage - 1]), originalRings[stage - 1], "legacy body radius cannot shrink the positional ring");
            Near(CombatRangeMath.ResolveBossCombatRadius(stage, 0), originalRings[stage - 1], "missing serialized radius retains wide default");
            Near(CombatRangeMath.ResolveBossCombatRadius(stage, (float)originalRings[stage - 1]), originalRings[stage - 1], "original ring size is preserved");
            Near(CombatRangeMath.ResolveBossCombatRadius(stage, 6), 6, "explicit larger hitbox remains configurable");
        }
    }

    public static int Main()
    {
        DistancesAndProfiles();
        DisplayAndHitAgreement();
        Pursuit();
        BossRingContact();
        WideBossRingProfiles();
        Console.WriteLine("PASS: " + _checks + " melee geometry, configuration and pursuit assertions.");
        return 0;
    }
}
